using System.ComponentModel.DataAnnotations;

namespace IQFlowAgent.Web.ViewModels;

public class IntakeEditViewModel
{
    // Hidden — used for routing
    public int Id { get; set; }
    public string IntakeId { get; set; } = string.Empty;

    // ── Meta ────────────────────────────────────────────────────────
    [Required(ErrorMessage = "Process name is required")]
    [Display(Name = "Process Name")]
    public string ProcessName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Business Unit is required")]
    [Display(Name = "Business Unit")]
    public string BusinessUnit { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    [Required(ErrorMessage = "Process Owner Name is required")]
    [Display(Name = "Process Owner Name")]
    public string ProcessOwnerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Process Owner Email is required")]
    [EmailAddress]
    [Display(Name = "Process Owner Email")]
    public string ProcessOwnerEmail { get; set; } = string.Empty;

    [Display(Name = "Process Type")]
    public string ProcessType { get; set; } = "Manual";

    [Display(Name = "Estimated Volume / Day")]
    [Range(0, 1_000_000)]
    public int EstimatedVolumePerDay { get; set; }

    public string Priority { get; set; } = "Medium";

    // ── Location ─────────────────────────────────────────────────────
    [Required(ErrorMessage = "Country is required")]
    public string Country { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    [Display(Name = "Site / Office Location")]
    public string SiteLocation { get; set; } = string.Empty;

    [Display(Name = "Time Zone")]
    public string TimeZone { get; set; } = string.Empty;

    // ── Document ─────────────────────────────────────────────────────
    /// <summary>Name of the currently stored document (display only).</summary>
    public string? CurrentFileName { get; set; }

    /// <summary>Path / URL of the currently stored document (display only).</summary>
    public string? CurrentFilePath { get; set; }

    /// <summary>When true the user wants to delete the old document and upload a new one.</summary>
    public bool ReplaceDocument { get; set; }

    /// <summary>New document file (only used when ReplaceDocument is true).</summary>
    public IFormFile? NewDocument { get; set; }

    /// <summary>When true, re-run AI analysis after saving the changes.</summary>
    [Display(Name = "Re-run AI Analysis after saving")]
    public bool RerunAnalysis { get; set; } = true;

    public int TaskCount { get; set; }
}
