namespace IQFlowAgent.Web.Models;

public class IntakeDocument
{
    public int Id { get; set; }

    // Every document belongs to an intake
    public int IntakeRecordId { get; set; }
    public IntakeRecord IntakeRecord { get; set; } = null!;

    // Optionally also belongs to a specific task (null = intake-level document)
    public int? IntakeTaskId { get; set; }
    public IntakeTask? Task { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;   // blob URL or /uploads/... path
    public string? ContentType { get; set; }
    public long? FileSize { get; set; }

    /// <summary>IntakeDocument | TaskArtifact</summary>
    public string DocumentType { get; set; } = "IntakeDocument";

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedByUserId { get; set; }
    public string? UploadedByName { get; set; }
}
