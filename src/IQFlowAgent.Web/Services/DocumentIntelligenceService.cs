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
    private readonly ILogger<DocumentIntelligenceService> _logger;

    public DocumentIntelligenceService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<DocumentIntelligenceService> logger)
    {
        _config            = config;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    public bool IsConfigured()
    {
        var ep  = _config["AzureDocumentIntelligence:Endpoint"];
        var key = _config["AzureDocumentIntelligence:ApiKey"];
        return !string.IsNullOrWhiteSpace(ep)
            && !string.IsNullOrWhiteSpace(key)
            && !ep.Contains("YOUR_ENDPOINT",  StringComparison.OrdinalIgnoreCase)
            && !key.Contains("YOUR_API_KEY",  StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> ExtractTextAsync(byte[] bytes, string fileName)
    {
        if (!IsConfigured()) return null;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext)) return null;

        var endpoint = _config["AzureDocumentIntelligence:Endpoint"]!.TrimEnd('/');
        var apiKey   = _config["AzureDocumentIntelligence:ApiKey"]!;

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
}
