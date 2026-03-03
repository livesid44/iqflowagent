using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Services;

public class AuthSettingsService : IAuthSettingsService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContextService _tenantContext;

    public AuthSettingsService(ApplicationDbContext db, ITenantContextService tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<AuthSettings> GetSettingsAsync()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var settings = await _db.AuthSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId);
        if (settings == null)
        {
            settings = new AuthSettings { TenantId = tenantId };
            _db.AuthSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task SaveSettingsAsync(AuthSettings settings)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var existing = await _db.AuthSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId);
        if (existing == null)
        {
            settings.TenantId = tenantId;
            settings.UpdatedAt = DateTime.UtcNow;
            _db.AuthSettings.Add(settings);
        }
        else
        {
            existing.AuthMode = settings.AuthMode;
            existing.LdapServer = settings.LdapServer;
            existing.LdapPort = settings.LdapPort;
            existing.LdapBaseDn = settings.LdapBaseDn;
            existing.LdapBindDn = settings.LdapBindDn;
            existing.LdapBindPassword = settings.LdapBindPassword;
            existing.LdapUseSsl = settings.LdapUseSsl;
            existing.LdapSearchFilter = settings.LdapSearchFilter;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }
}
