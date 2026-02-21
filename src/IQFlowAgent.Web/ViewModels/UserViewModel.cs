using System.ComponentModel.DataAnnotations;

namespace IQFlowAgent.Web.ViewModels;

public class UserViewModel
{
    public string? Id { get; set; }

    [Required]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "User";

    public bool IsActive { get; set; } = true;

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; }
}
