using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Services;

public class AuthSettingsService : IAuthSettingsService
{
    private readonly ApplicationDbContext _db;

    public AuthSettingsService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AuthSettings> GetSettingsAsync()
    {
        var settings = await _db.AuthSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new AuthSettings();
            _db.AuthSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task SaveSettingsAsync(AuthSettings settings)
    {
        var existing = await _db.AuthSettings.FirstOrDefaultAsync();
        if (existing == null)
        {
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
