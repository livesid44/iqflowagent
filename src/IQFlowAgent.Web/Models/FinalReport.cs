namespace IQFlowAgent.Web.Models;

/// <summary>
/// Represents a generated BARTOK DD final report for an intake.
/// </summary>
public class FinalReport
{
    public int Id { get; set; }

    public int IntakeRecordId { get; set; }
    public IntakeRecord IntakeRecord { get; set; } = null!;

    public string ReportFileName { get; set; } = string.Empty;

    /// <summary>Blob URL or local relative path to the generated .docx file.</summary>
    public string FilePath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public string? GeneratedByUserId { get; set; }
    public string? GeneratedByName { get; set; }
}
