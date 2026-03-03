using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Services;

public class TenantContextService : ITenantContextService
{
    private const string SessionKey = "CurrentTenantId";
    private const int DefaultTenantId = 1;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _db;

    public TenantContextService(IHttpContextAccessor httpContextAccessor, ApplicationDbContext db)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
    }

    public int GetCurrentTenantId()
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session != null)
        {
            var stored = session.GetString(SessionKey);
            if (int.TryParse(stored, out var id))
                return id;
        }
        return DefaultTenantId;
    }

    public void SetCurrentTenant(int tenantId)
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        session?.SetString(SessionKey, tenantId.ToString());
    }

    public async Task<Tenant?> GetCurrentTenantAsync()
    {
        var id = GetCurrentTenantId();
        return await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<TenantAiSettings?> GetCurrentTenantAiSettingsAsync()
    {
        var id = GetCurrentTenantId();
        return await _db.TenantAiSettings.FirstOrDefaultAsync(s => s.TenantId == id);
    }

    public async Task<List<Tenant>> GetUserTenantsAsync(string userId)
    {
        return await _db.UserTenants
            .Where(ut => ut.UserId == userId)
            .Select(ut => ut.Tenant!)
            .Where(t => t != null && t.IsActive)
            .ToListAsync();
    }
}
