namespace IQFlowAgent.Web.Models;

public class QcCheck
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int IntakeRecordId { get; set; }
    public IntakeRecord IntakeRecord { get; set; } = null!;

    /// <summary>Overall QC score 0–100.</summary>
    public int OverallScore { get; set; }

    /// <summary>JSON array of QC parameter scores from Azure OpenAI.</summary>
    public string? ScoreBreakdownJson { get; set; }

    /// <summary>Status: Pending, Running, Complete, Error.</summary>
    public string Status { get; set; } = "Pending";

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? RunByUserId { get; set; }
}
