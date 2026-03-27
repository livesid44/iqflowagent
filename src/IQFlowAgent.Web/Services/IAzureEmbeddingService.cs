namespace IQFlowAgent.Web.Services;

public interface IAzureEmbeddingService
{
    bool IsConfigured();
    /// <summary>Returns null when service is not configured or embedding fails.</summary>
    Task<float[]?> GetEmbeddingAsync(string text, CancellationToken ct = default);
    Task<List<float[]?>> GetEmbeddingsBatchAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
