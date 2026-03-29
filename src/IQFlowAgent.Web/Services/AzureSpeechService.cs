using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IQFlowAgent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Calls Azure Batch Transcription REST API (Speech-to-Text) for audio/video files.
/// Falls back to a realistic mock transcript when the tenant has no Speech credentials.
/// </summary>
public class AzureSpeechService : IAzureSpeechService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AzureSpeechService> _logger;
    private readonly IAuditLogService _auditLog;

    // Polling constants — 5 min max (60 polls × 5 s)
    private static readonly TimeSpan MaxPollDuration = TimeSpan.FromMinutes(5);
    private const int PollIntervalMs = 5_000;
    private static readonly int MaxPollAttempts = (int)(MaxPollDuration.TotalMilliseconds / PollIntervalMs);

    public AzureSpeechService(IHttpClientFactory httpFactory, ApplicationDbContext db,
        ILogger<AzureSpeechService> logger, IAuditLogService auditLog)
    {
        _httpFactory = httpFactory;
        _db          = db;
        _logger      = logger;
        _auditLog    = auditLog;
    }

    // ─── IsConfigured ────────────────────────────────────────────────────────

    public async Task<bool> IsConfiguredAsync(int tenantId)
    {
        var s = await _db.TenantAiSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId);
        return s is not null
            && !string.IsNullOrWhiteSpace(s.AzureSpeechRegion)
            && !string.IsNullOrWhiteSpace(s.AzureSpeechApiKey)
            && !s.AzureSpeechApiKey.Equals("YOUR_SPEECH_KEY", StringComparison.OrdinalIgnoreCase);
    }

    // ─── TranscribeAsync ─────────────────────────────────────────────────────

    public async Task<string> TranscribeAsync(string filePathOrUrl, int tenantId, string fileName)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var settings = await _db.TenantAiSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId);

        if (settings is null
            || string.IsNullOrWhiteSpace(settings.AzureSpeechRegion)
            || string.IsNullOrWhiteSpace(settings.AzureSpeechApiKey)
            || settings.AzureSpeechApiKey.Equals("YOUR_SPEECH_KEY", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Azure Speech not configured for tenant {TenantId} — using mock transcript.", tenantId);
            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "SpeechTranscribe",
                eventType      : "SpeechApi",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : null,
                httpStatusCode : null,
                durationMs     : 0,
                isMocked       : true,
                outcome        : "MockResponse");
            return GenerateMockTranscript(fileName);
        }

        // Batch Transcription requires a publicly accessible URL
        if (!filePathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Speech transcription requires a blob URL; '{Path}' is a local path — using mock.", filePathOrUrl);
            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "SpeechTranscribe",
                eventType      : "SpeechApi",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : null,
                httpStatusCode : null,
                durationMs     : 0,
                isMocked       : true,
                outcome        : "MockResponse");
            return GenerateMockTranscript(fileName);
        }

        var baseUrl    = $"https://{settings.AzureSpeechRegion}.api.cognitive.microsoft.com/speechtotext/v3.2";
        var requestUrl = $"{baseUrl}/transcriptions";
        var sw         = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await RunBatchTranscriptionAsync(
                filePathOrUrl,
                settings.AzureSpeechRegion,
                settings.AzureSpeechApiKey,
                fileName);
            sw.Stop();

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "SpeechTranscribe",
                eventType      : "SpeechApi",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : requestUrl,
                httpStatusCode : 200,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Success");

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Azure Speech transcription failed for file '{File}' in tenant {TenantId}.", fileName, tenantId);

            await _auditLog.LogExternalCallAsync(
                correlationId  : correlationId,
                callSite       : "SpeechTranscribe",
                eventType      : "SpeechApi",
                tenantId       : tenantId,
                intakeRecordId : null,
                requestUrl     : requestUrl,
                httpStatusCode : null,
                durationMs     : sw.ElapsedMilliseconds,
                isMocked       : false,
                outcome        : "Error",
                errorMessage   : ex.Message);

            return GenerateMockTranscript(fileName);
        }
    }

    // ─── Private: Batch Transcription ────────────────────────────────────────

    private async Task<string> RunBatchTranscriptionAsync(
        string blobUrl, string region, string apiKey, string fileName)
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

        var baseUrl = $"https://{region}.api.cognitive.microsoft.com/speechtotext/v3.2";

        // 1. Create transcription job
        var createBody = new
        {
            contentUrls = new[] { blobUrl },
            locale       = "en-US",
            displayName  = $"intake-{DateTime.UtcNow:yyyyMMddHHmmss}-{SanitizeJobName(Path.GetFileNameWithoutExtension(fileName))}",
            properties   = new { wordLevelTimestampsEnabled = false, punctuationMode = "DictatedAndAutomatic" }
        };

        var createResp = await client.PostAsync(
            $"{baseUrl}/transcriptions",
            new StringContent(JsonSerializer.Serialize(createBody), Encoding.UTF8, "application/json"));

        if (!createResp.IsSuccessStatusCode)
        {
            var err = await createResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Speech create job failed ({(int)createResp.StatusCode}): {err}");
        }

        var createJson  = await createResp.Content.ReadAsStringAsync();
        var createDoc   = JsonDocument.Parse(createJson);
        var jobUrl      = createDoc.RootElement.GetProperty("self").GetString()
                          ?? throw new InvalidOperationException("Speech API did not return 'self' URL.");

        _logger.LogInformation("Azure Speech batch job created: {JobUrl}", jobUrl);

        // 2. Poll for completion
        for (int i = 0; i < MaxPollAttempts; i++)
        {
            await Task.Delay(PollIntervalMs);

            var pollResp = await client.GetAsync(jobUrl);
            pollResp.EnsureSuccessStatusCode();
            var pollJson = await pollResp.Content.ReadAsStringAsync();
            var pollDoc  = JsonDocument.Parse(pollJson);
            var status   = pollDoc.RootElement.GetProperty("status").GetString();

            _logger.LogDebug("Speech job poll [{Attempt}/{Max}]: {Status}", i + 1, MaxPollAttempts, status);

            if (status is "Succeeded")
            {
                // 3. Fetch files list to get transcript URL
                var filesResp = await client.GetAsync($"{jobUrl}/files");
                filesResp.EnsureSuccessStatusCode();
                var filesJson = await filesResp.Content.ReadAsStringAsync();
                var filesDoc  = JsonDocument.Parse(filesJson);

                foreach (var file in filesDoc.RootElement.GetProperty("values").EnumerateArray())
                {
                    var kind = file.GetProperty("kind").GetString();
                    if (kind is not "Transcription") continue;

                    var transcriptUrl = file.GetProperty("links").GetProperty("contentUrl").GetString();
                    if (transcriptUrl is null) continue;

                    // 4. Download transcript JSON and extract display text
                    var transcriptResp = await client.GetAsync(transcriptUrl);
                    transcriptResp.EnsureSuccessStatusCode();
                    var transcriptJson = await transcriptResp.Content.ReadAsStringAsync();
                    return ExtractDisplayText(transcriptJson, fileName);
                }

                throw new InvalidOperationException("Speech job Succeeded but no Transcription file found.");
            }

            if (status is "Failed")
            {
                var errMsg = pollDoc.RootElement.TryGetProperty("properties", out var props)
                    && props.TryGetProperty("error", out var errProp)
                    ? errProp.GetRawText()
                    : "Unknown error";
                throw new InvalidOperationException($"Speech batch job Failed: {errMsg}");
            }
        }

        throw new TimeoutException($"Speech batch job did not complete within {MaxPollAttempts * PollIntervalMs / 1000}s.");
    }

    /// <summary>Removes characters that are unsafe for Azure API job names (alphanumeric, hyphen, underscore only).</summary>
    private static string SanitizeJobName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9\-_]", "_")[..Math.Min(64, name.Length)];

    private static string ExtractDisplayText(string transcriptJson, string fileName)
    {
        using var doc = JsonDocument.Parse(transcriptJson);
        var sb = new StringBuilder();

        if (doc.RootElement.TryGetProperty("recognizedPhrases", out var phrases))
        {
            foreach (var phrase in phrases.EnumerateArray())
            {
                if (phrase.TryGetProperty("nBest", out var nBest)
                    && nBest.GetArrayLength() > 0
                    && nBest[0].TryGetProperty("display", out var display))
                {
                    sb.AppendLine(display.GetString());
                }
            }
        }

        return sb.Length > 0
            ? sb.ToString().Trim()
            : $"[Transcript extracted from {fileName}]";
    }

    // ─── Mock Transcript ─────────────────────────────────────────────────────

    private static string GenerateMockTranscript(string fileName) => $"""
        [MOCK TRANSCRIPT — Azure Speech not configured]
        File: {Path.GetFileName(fileName)}

        Moderator: Good afternoon, thank you all for joining today's process discussion session.
        We're here to walk through the end-to-end workflow and capture key information for the intake.

        Participant 1: The current process is largely manual. Our team spends approximately four hours
        per day on data entry, reconciliation, and exception handling.

        Moderator: Can you describe the main pain points?

        Participant 1: Yes — the biggest issues are duplicate data entry across three systems,
        no single source of truth for customer records, and delays in exception resolution
        that sometimes extend to three business days.

        Participant 2: From the compliance side, we also lack a proper audit trail.
        Every status change needs to be logged, but it's currently done in spreadsheets.

        Moderator: What would the ideal future state look like?

        Participant 1: Automated intake from the source system, real-time validation,
        instant exception flagging, and a dashboard for management visibility.

        Participant 2: And an audit log that meets SOX requirements without manual effort.

        Moderator: Perfect. I'll capture these as action items and we'll include them in the SOP document.
        Thank you everyone.

        [End of transcript — Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC]
        """;

    public async Task<(bool success, int statusCode, string message)> TestConnectionAsync(int tenantId)
    {
        var settings = await _db.TenantAiSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId);

        if (settings is null
            || string.IsNullOrWhiteSpace(settings.AzureSpeechRegion)
            || string.IsNullOrWhiteSpace(settings.AzureSpeechApiKey)
            || settings.AzureSpeechApiKey.Equals("YOUR_SPEECH_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return (false, 0, "Azure Speech is not configured. Please provide Speech Region and API Key.");
        }

        var region = settings.AzureSpeechRegion.Trim();
        var apiKey = settings.AzureSpeechApiKey.Trim();

        // Lightweight check: list the first base model — validates both region and key
        var url = $"https://{region}.api.cognitive.microsoft.com/speechtotext/v3.2/models/base?top=1";

        try
        {
            var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
            var response = await http.GetAsync(url);
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Azure Speech connection test succeeded for region '{Region}'.", region);
                return (true, code, $"Connected successfully to Azure Speech in region '{region}'.");
            }

            var hint = code switch
            {
                401 => " Check your API Key.",
                403 => " Access denied — verify the subscription and region.",
                404 => $" Region '{region}' may be incorrect.",
                _   => string.Empty
            };
            _logger.LogWarning("Azure Speech connection test returned HTTP {Code} for region '{Region}'.", code, region);
            return (false, code, $"Azure Speech returned HTTP {code}.{hint}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure Speech connection test failed for region '{Region}'.", region);
            return (false, 0, $"Connection failed: {ex.Message}");
        }
    }
}
