using System.ComponentModel.DataAnnotations;

namespace IQFlowAgent.Web.ViewModels;

/// <summary>
/// View model for editing an existing intake. Inherits all intake fields from
/// IntakeViewModel and adds fields for document management and re-analysis.
/// </summary>
public class EditIntakeViewModel : IntakeViewModel
{
    /// <summary>Original document file name (for display).</summary>
    public string? CurrentDocumentName { get; set; }

    /// <summary>Original document path / blob URL (used for deletion).</summary>
    public string? CurrentDocumentPath { get; set; }

    /// <summary>When true, the existing document is deleted on save.</summary>
    [Display(Name = "Remove existing document")]
    public bool DeleteDocument { get; set; }

    /// <summary>When true, AI analysis is re-triggered after save.</summary>
    [Display(Name = "Re-run AI Analysis after saving")]
    public bool RerunAnalysis { get; set; }
}
