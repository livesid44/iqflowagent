namespace IQFlowAgent.Web.Models;

/// <summary>
/// Tracks the fill-status of each BARTOK DD template field for a specific intake.
/// </summary>
public class ReportFieldStatus
{
    public int Id { get; set; }

    public int IntakeRecordId { get; set; }
    public IntakeRecord IntakeRecord { get; set; } = null!;

    /// <summary>Unique key that identifies the template field (e.g. "header_process_name").</summary>
    public string FieldKey { get; set; } = string.Empty;

    public string FieldLabel { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;

    /// <summary>Exact placeholder text in the .docx template that should be replaced.</summary>
    public string TemplatePlaceholder { get; set; } = string.Empty;

    /// <summary>Pending | Available | Missing | NA | TaskCreated</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Value that will be substituted into the document.</summary>
    public string? FillValue { get; set; }

    /// <summary>True when the user has explicitly marked this field as Not Applicable.</summary>
    public bool IsNA { get; set; }

    public string? Notes { get; set; }

    /// <summary>Task Id (TSK-…) if a task was created for this field.</summary>
    public string? LinkedTaskId { get; set; }

    public DateTime? AnalyzedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
