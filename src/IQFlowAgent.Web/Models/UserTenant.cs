namespace IQFlowAgent.Web.Models;

public class UserTenant
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public string TenantRole { get; set; } = "User";
    public bool IsDefault { get; set; } = false;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
