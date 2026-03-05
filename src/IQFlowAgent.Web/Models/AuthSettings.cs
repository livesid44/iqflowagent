namespace IQFlowAgent.Web.Models;

public class AuthSettings
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string AuthMode { get; set; } = "AppPassword";
    public string? LdapServer { get; set; }
    public int LdapPort { get; set; } = 389;
    public string? LdapBaseDn { get; set; }
    public string? LdapBindDn { get; set; }
    public string? LdapBindPassword { get; set; }
    public bool LdapUseSsl { get; set; } = false;
    public string? LdapSearchFilter { get; set; } = "(sAMAccountName={0})";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
