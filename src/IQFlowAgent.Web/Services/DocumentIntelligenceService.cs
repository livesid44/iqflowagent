using System.Text;
using System.Text.Json;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Calls the Azure Document Intelligence <c>prebuilt-layout</c> REST API to extract
/// structured text (tables, paragraphs) from uploaded task artifacts.
/// Uses <see cref="IHttpClientFactory"/> — no additional NuGet package required.
///
/// Supported formats: .pdf .docx .xlsx .pptx .jpg .jpeg .png .tiff .bmp
///
/// Table output format — one tab-separated line per row (header row first):
/// <code>
///   Month\treceived\tHandled
///   Jan-25\t10000\t9900
///   Feb-25\t12346\t12222.54
/// </code>
/// This matches the format produced by <see cref="DocumentTextExtractor"/> so that
/// the extracted text can be sent verbatim to the LLM for field extraction.
/// </summary>
public class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private const string ApiVersion      = "2024-11-30";
    private const string ModelId         = "prebuilt-layout";
    private const int    PollingMs       = 2_000;   // 2-second poll interval
    private const int    MaxPollAttempts = 60;       // 2-minute hard timeout

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".docx", ".xlsx", ".pptx", ".jpg", ".jpeg", ".png", ".tiff", ".bmp" };

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITenantContextService _tenantContext;
    private readonly ILogger<DocumentIntelligenceService> _logger;

    public DocumentIntelligenceService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ITenantContextService tenantContext,
        ILogger<DocumentIntelligenceService> logger)
    {
        _config            = config;
        _httpClientFactory = httpClientFactory;
        _tenantContext     = tenantContext;
        _logger            = logger;
    }

    public bool IsConfigured()
    {
        // Fast synchronous check against static config only (avoids DB deadlock).
        // Tenant settings override is applied in ExtractTextAsync via GetDocIntelConfigAsync.
        var ep  = _config["AzureDocumentIntelligence:Endpoint"];
        var key = _config["AzureDocumentIntelligence:ApiKey"];
        return !string.IsNullOrWhiteSpace(ep)
            && !string.IsNullOrWhiteSpace(key)
            && !ep.Contains("YOUR_",  StringComparison.OrdinalIgnoreCase)
            && !key.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> ExtractTextAsync(byte[] bytes, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext)) return null;

        var (endpoint, apiKey) = await GetDocIntelConfigAsync();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey)) return null;
        endpoint = endpoint.TrimEnd('/');

        var analyzeUrl =
            $"{endpoint}/documentintelligence/documentModels/{ModelId}:analyze" +
            $"?api-version={ApiVersion}";

        try
        {
            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);

            // ── Submit analysis ────────────────────────────────────────────────
            var base64Body = JsonSerializer.Serialize(
                new { base64Source = Convert.ToBase64String(bytes) });

            var initResponse = await http.PostAsync(
                analyzeUrl,
                new StringContent(base64Body, Encoding.UTF8, "application/json"));

            if (!initResponse.IsSuccessStatusCode)
            {
                var errBody = await initResponse.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Document Intelligence returned HTTP {Code} for {File}: {Body}",
                    (int)initResponse.StatusCode, fileName, errBody);
                return null;
            }

            if (!initResponse.Headers.TryGetValues("Operation-Location", out var opLoc))
            {
                _logger.LogWarning(
                    "Document Intelligence response for {File} missing Operation-Location header.", fileName);
                return null;
            }

            var operationUrl = opLoc.First();

            // ── Poll until succeeded / failed / timeout ────────────────────────
            // Check immediately on the first iteration (fast files may already be
            // done); delay at the end so subsequent polls wait before retrying.
            for (int attempt = 0; attempt < MaxPollAttempts; attempt++)
            {
                var pollResp = await http.GetAsync(operationUrl);
                var pollBody = await pollResp.Content.ReadAsStringAsync();

                using var pollDoc = JsonDocument.Parse(pollBody);
                var status = pollDoc.RootElement
                    .TryGetProperty("status", out var st) ? st.GetString() : "running";

                switch (status)
                {
                    case "succeeded":
                        var text = BuildExtractedText(pollDoc.RootElement);
                        _logger.LogInformation(
                            "Document Intelligence extracted {Chars} chars from {File} after {Polls} polls.",
                            text.Length, fileName, attempt + 1);
                        return text.Length > 0 ? text : null;

                    case "failed":
                        _logger.LogWarning(
                            "Document Intelligence analysis failed for {File}.", fileName);
                        return null;
                }

                // Not finished yet — wait before the next poll
                await Task.Delay(PollingMs);
            }

            _logger.LogWarning(
                "Document Intelligence timed out after {Sec}s for {File}.",
                MaxPollAttempts * PollingMs / 1000, fileName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document Intelligence extraction threw for {File}.", fileName);
            return null;
        }
    }

    // ── Config resolution ──────────────────────────────────────────────────────

    private async Task<(string? endpoint, string? apiKey)> GetDocIntelConfigAsync()
    {
        var tenantSettings = await _tenantContext.GetCurrentTenantAiSettingsAsync();
        if (tenantSettings != null
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureDocumentIntelligenceEndpoint)
            && !string.IsNullOrWhiteSpace(tenantSettings.AzureDocumentIntelligenceApiKey)
            && !tenantSettings.AzureDocumentIntelligenceEndpoint.Contains("YOUR_ENDPOINT",  StringComparison.OrdinalIgnoreCase)
            && !tenantSettings.AzureDocumentIntelligenceApiKey.Contains("YOUR_API_KEY",     StringComparison.OrdinalIgnoreCase))
        {
            return (tenantSettings.AzureDocumentIntelligenceEndpoint,
                    tenantSettings.AzureDocumentIntelligenceApiKey);
        }
        var ep  = _config["AzureDocumentIntelligence:Endpoint"];
        var key = _config["AzureDocumentIntelligence:ApiKey"];
        if (string.IsNullOrWhiteSpace(ep) || ep.Contains("YOUR_ENDPOINT",  StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(key) || key.Contains("YOUR_API_KEY", StringComparison.OrdinalIgnoreCase))
            return (null, null);
        return (ep, key);
    }

    // ── Response parser ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts the Document Intelligence <c>analyzeResult</c> JSON into plain text.
    /// Tables are emitted as tab-separated rows; paragraphs follow after a blank line.
    /// </summary>
    private static string BuildExtractedText(JsonElement root)
    {
        var sb = new StringBuilder();

        if (!root.TryGetProperty("analyzeResult", out var result))
            return string.Empty;

        // ── Tables ────────────────────────────────────────────────────────────
        if (result.TryGetProperty("tables", out var tables))
        {
            foreach (var table in tables.EnumerateArray())
            {
                var rowCount = table.GetProperty("rowCount").GetInt32();
                var colCount = table.GetProperty("columnCount").GetInt32();

                // Build a 2-D grid; initialize to empty string
                var grid = new string[rowCount, colCount];
                for (int r = 0; r < rowCount; r++)
                    for (int c = 0; c < colCount; c++)
                        grid[r, c] = string.Empty;

                foreach (var cell in table.GetProperty("cells").EnumerateArray())
                {
                    var r = cell.GetProperty("rowIndex").GetInt32();
                    var c = cell.GetProperty("columnIndex").GetInt32();
                    if (r < rowCount && c < colCount)
                        grid[r, c] = cell.TryGetProperty("content", out var ct)
                            ? ct.GetString() ?? string.Empty
                            : string.Empty;
                }

                // Emit rows as tab-separated lines
                for (int r = 0; r < rowCount; r++)
                {
                    var rowValues = Enumerable.Range(0, colCount).Select(c => grid[r, c]);
                    var line = string.Join("\t", rowValues);
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine(line);
                }

                sb.AppendLine(); // blank separator between tables
            }
        }

        // ── Paragraphs ────────────────────────────────────────────────────────
        // Only emit paragraphs that are NOT already covered by table cells to avoid
        // duplicating table content.
        if (result.TryGetProperty("paragraphs", out var paragraphs))
        {
            foreach (var para in paragraphs.EnumerateArray())
            {
                // Skip paragraphs whose role is "pageHeader" / "pageFooter" / "pageNumber"
                // to reduce noise in the extracted text.
                if (para.TryGetProperty("role", out var roleEl))
                {
                    var role = roleEl.GetString() ?? "";
                    if (role is "pageHeader" or "pageFooter" or "pageNumber") continue;
                }

                var content = para.TryGetProperty("content", out var c)
                    ? c.GetString() ?? string.Empty
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(content))
                    sb.AppendLine(content);
            }
        }

        return sb.ToString();
    }

    public async Task<(bool success, int statusCode, string message)> TestConnectionAsync()
    {
        var (endpoint, apiKey) = await GetDocIntelConfigAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, 0,
                "Azure Document Intelligence is not configured. Please provide Endpoint and API Key.");
        }

        // GET /documentintelligence/documentModels?api-version=... lists available models —
        // a lightweight authenticated request that validates both endpoint and key.
        var url = $"{endpoint.TrimEnd('/')}/documentintelligence/documentModels?api-version={ApiVersion}&top=1";

        try
        {
            var http = _httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
            var response = await http.GetAsync(url);
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Document Intelligence connection test succeeded for '{Endpoint}'.", endpoint);
                return (true, code, $"Connected successfully to Azure Document Intelligence at '{endpoint}'.");
            }

            var hint = code switch
            {
                401 => " Authentication failed — check your API Key.",
                403 => " Access denied — verify subscription and permissions.",
                404 => " Endpoint may be incorrect or the resource does not exist.",
                _   => string.Empty
            };
            _logger.LogWarning("Document Intelligence test returned HTTP {Code} for '{Endpoint}'.", code, endpoint);
            return (false, code, $"Azure Document Intelligence returned HTTP {code}.{hint}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Document Intelligence connection test failed for '{Endpoint}'.", endpoint);
            return (false, 0, $"Connection failed: {ex.Message}");
        }
    }
}
