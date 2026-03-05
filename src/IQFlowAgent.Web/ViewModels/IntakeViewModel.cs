using System.ComponentModel.DataAnnotations;

namespace IQFlowAgent.Web.ViewModels;

public class IntakeViewModel
{
    // Meta
    [Required(ErrorMessage = "Process name is required")]
    [Display(Name = "Process Name")]
    public string ProcessName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Business Unit is required")]
    [Display(Name = "Business Unit")]
    public string BusinessUnit { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;
    public string Lob { get; set; } = string.Empty;

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
    [Range(0, 1000000)]
    public int EstimatedVolumePerDay { get; set; }

    public string Priority { get; set; } = "Medium";

    // Location
    [Required(ErrorMessage = "Country is required")]
    public string Country { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    [Display(Name = "Site / Office Location")]
    public string SiteLocation { get; set; } = string.Empty;

    [Display(Name = "Time Zone")]
    public string TimeZone { get; set; } = string.Empty;

    // File upload (optional)
    public IFormFile? Document { get; set; }
}
