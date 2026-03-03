namespace IQFlowAgent.Web.Models;

public class MasterLob
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;

    /// <summary>Parent department name (stored as string for loose coupling).</summary>
    public string DepartmentName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
