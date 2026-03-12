namespace IQFlowAgent.Web.Models;

public class IntakeRecord
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string IntakeId { get; set; } = string.Empty;

    // Meta Information
    public string ProcessName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BusinessUnit { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Lob { get; set; } = string.Empty; // Line of Business (maps to Deployment)
    public string SdcLots { get; set; } = string.Empty; // Lots or SDC (comma-separated multi-select)
    public string ProcessOwnerName { get; set; } = string.Empty;
    public string ProcessOwnerEmail { get; set; } = string.Empty;
    public string ProcessType { get; set; } = string.Empty; // Manual, Semi-Automated, Automated
    public int EstimatedVolumePerDay { get; set; }
    public string Priority { get; set; } = "Medium"; // High, Medium, Low

    // Location
    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string SiteLocation { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;

    // Document upload
    public string? UploadedFileName { get; set; }
    public string? UploadedFilePath { get; set; }
    public string? UploadedFileContentType { get; set; }
    public long? UploadedFileSize { get; set; }

    // Status & Analysis
    public string Status { get; set; } = "Draft"; // Draft, Submitted, Analyzing, Complete, Error
    public string? AnalysisResult { get; set; }      // JSON from Azure OpenAI

    /// <summary>
    /// JSON array of PII/SPII findings that were masked before the analysis was sent to the LLM.
    /// Each element has the shape { "entityType": "…", "matchedText": "…" }.
    /// Null when no PII was detected (or PII scanning was disabled at the time of analysis).
    /// </summary>
    public string? PiiMaskingLog { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? AnalyzedAt { get; set; }
    public string? CreatedByUserId { get; set; }
}
