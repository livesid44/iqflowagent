namespace IQFlowAgent.Web.Services;

/// <summary>
/// Wraps the Azure Document Intelligence <c>prebuilt-layout</c> model to extract
/// structured text (tables, paragraphs) from .xlsx, .docx, .pdf, and image files.
/// Falls back gracefully to <c>null</c> when the service is not configured.
/// </summary>
public interface IDocumentIntelligenceService
{
    /// <summary>
    /// Returns true when Azure Document Intelligence is configured with a valid
    /// endpoint and API key.
    /// </summary>
    bool IsConfigured();

    /// <summary>
    /// Extracts all text and table data from <paramref name="bytes"/>.
    /// Tables are emitted as tab-separated rows (header row first), one line per row,
    /// followed by free-form paragraphs.
    /// Returns <c>null</c> if the file format is unsupported, extraction fails, or the
    /// service is not configured — the caller should fall back to OpenXML extraction.
    /// </summary>
    Task<string?> ExtractTextAsync(byte[] bytes, string fileName);

    /// <summary>
    /// Verifies that the configured Document Intelligence endpoint and API key are reachable.
    /// Returns a success flag, HTTP status code, and a human-readable message.
    /// </summary>
    Task<(bool success, int statusCode, string message)> TestConnectionAsync();
}
