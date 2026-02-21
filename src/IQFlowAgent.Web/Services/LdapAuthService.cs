using IQFlowAgent.Web.Models;
using Novell.Directory.Ldap;

namespace IQFlowAgent.Web.Services;

public class LdapAuthService : ILdapAuthService
{
    private readonly ILogger<LdapAuthService> _logger;

    public LdapAuthService(ILogger<LdapAuthService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> AuthenticateAsync(string username, string password, AuthSettings settings)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var conn = new LdapConnection();
                conn.Connect(settings.LdapServer!, settings.LdapPort);
                conn.Bind(settings.LdapBindDn, settings.LdapBindPassword);

                var searchFilter = string.Format(settings.LdapSearchFilter ?? "(sAMAccountName={0})", username);
                var results = conn.Search(
                    settings.LdapBaseDn,
                    LdapConnection.ScopeSub,
                    searchFilter,
                    new[] { "dn" },
                    false);

                if (!results.HasMore()) return false;

                var entry = results.Next();
                var userDn = entry.Dn;

                using var userConn = new LdapConnection();
                userConn.Connect(settings.LdapServer!, settings.LdapPort);
                userConn.Bind(userDn, password);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LDAP authentication failed for user {Username}", username);
                return false;
            }
        });
    }
}
