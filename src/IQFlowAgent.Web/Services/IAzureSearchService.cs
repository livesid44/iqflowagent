namespace IQFlowAgent.Web.Services;

public record DocumentChunkRecord(
    string Id,
    int IntakeDbId,
    string IntakePublicId,
    int TenantId,
    string FolderPath,
    string DocumentName,
    string Content,
    int ChunkIndex,
    float[] Embedding
);

public interface IAzureSearchService
{
    bool IsConfigured();
    Task EnsureIndexExistsAsync(CancellationToken ct = default);
    Task IndexChunksAsync(IEnumerable<DocumentChunkRecord> chunks, CancellationToken ct = default);
    Task DeleteIntakeChunksAsync(int intakeDbId, CancellationToken ct = default);
    /// <summary>Returns top-K content strings most relevant to the query for the given intake.</summary>
    Task<List<string>> SearchAsync(int intakeDbId, float[]? queryVector, string queryText, int topK = 5, CancellationToken ct = default);

    /// <summary>
    /// Verifies that the configured Azure AI Search endpoint and API key are reachable.
    /// Returns a success flag, HTTP status code, and a human-readable message.
    /// </summary>
    Task<(bool success, int statusCode, string message)> TestConnectionAsync();
}
