using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

public interface ITenantContextService
{
    int GetCurrentTenantId();
    Task<Tenant?> GetCurrentTenantAsync();
    Task<TenantAiSettings?> GetCurrentTenantAiSettingsAsync();
    void SetCurrentTenant(int tenantId);
    Task<List<Tenant>> GetUserTenantsAsync(string userId);
}
