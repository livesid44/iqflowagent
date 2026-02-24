namespace IQFlowAgent.Web.Models;

public class IntakeTask
{
    public int Id { get; set; }
    public string TaskId { get; set; } = string.Empty; // TSK-YYYYMMDD-XXXXXX

    // Link to parent intake
    public int IntakeRecordId { get; set; }
    public IntakeRecord IntakeRecord { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;      // user who created intake
    public string Priority { get; set; } = "Medium";       // High, Medium, Low
    public string Status { get; set; } = "Open";           // Open, In Progress, Completed, Cancelled

    // Dates
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; }                  // CreatedAt + 48 hours
    public DateTime? CompletedAt { get; set; }

    public string? CreatedByUserId { get; set; }

    // Not-Applicable flag — task is exempt from AI closure verification
    public bool IsNotApplicable { get; set; } = false;
    public string? NaReason { get; set; }

    // Navigation
    public ICollection<TaskActionLog> ActionLogs { get; set; } = [];
    public ICollection<IntakeDocument> Documents { get; set; } = [];
}
