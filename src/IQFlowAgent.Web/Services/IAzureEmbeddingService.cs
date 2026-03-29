namespace IQFlowAgent.Web.Services;

public interface IAzureEmbeddingService
{
    bool IsConfigured();
    /// <summary>Returns null when service is not configured or embedding fails.</summary>
    Task<float[]?> GetEmbeddingAsync(string text, CancellationToken ct = default);
    Task<List<float[]?>> GetEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);

    /// <summary>
    /// Verifies that the configured Azure OpenAI Embeddings endpoint and deployment are reachable
    /// by sending a minimal embedding request.
    /// Returns a success flag, HTTP status code, and a human-readable message.
    /// </summary>
    Task<(bool success, int statusCode, string message)> TestConnectionAsync();
}
