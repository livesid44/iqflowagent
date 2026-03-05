using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Services;

public class TenantContextService : ITenantContextService
{
    private const string SessionKey = "CurrentTenantId";

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
            if (int.TryParse(stored, out var id) && id > 0)
                return id;
        }

        // No session value — resolve the first active tenant from the DB
        // (synchronous; using FirstOrDefault on the local cache is safe here)
        var firstTenant = _db.Tenants
            .Where(t => t.IsActive)
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .FirstOrDefault();

        // If found, persist to session so subsequent requests don't hit the DB
        if (firstTenant > 0)
        {
            session?.SetString(SessionKey, firstTenant.ToString());
            return firstTenant;
        }

        // Absolute fallback — no tenants in DB yet (e.g. seeder hasn't run)
        return 1;
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
            .Where(ut => ut.UserId == userId && ut.Tenant != null && ut.Tenant.IsActive)
            .Select(ut => ut.Tenant!)
            .ToListAsync();
    }
}
