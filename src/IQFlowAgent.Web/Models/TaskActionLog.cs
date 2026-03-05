namespace IQFlowAgent.Web.Models;

public class TaskActionLog
{
    public int Id { get; set; }

    public int IntakeTaskId { get; set; }
    public IntakeTask Task { get; set; } = null!;

    /// <summary>StatusChange | Comment | ArtifactUploaded</summary>
    public string ActionType { get; set; } = "Comment";

    public string? OldStatus { get; set; }
    public string? NewStatus { get; set; }
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
}
