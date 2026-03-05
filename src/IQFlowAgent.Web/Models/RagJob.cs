namespace IQFlowAgent.Web.Models;

/// <summary>
/// Tracks background RAG (Retrieval-Augmented Generation) processing jobs for an intake.
/// One job is created per intake submission; it processes all uploaded files asynchronously.
/// </summary>
public class RagJob
{
    public int Id { get; set; }
    public int IntakeRecordId { get; set; }
    public IntakeRecord IntakeRecord { get; set; } = null!;

    /// <summary>Queued | Processing | Complete | Error</summary>
    public string Status { get; set; } = "Queued";

    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>User ID to notify on completion via SignalR.</summary>
    public string? NotifyUserId { get; set; }
}
