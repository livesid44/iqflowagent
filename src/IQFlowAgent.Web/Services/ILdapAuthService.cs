using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

public interface ILdapAuthService
{
    Task<bool> AuthenticateAsync(string username, string password, AuthSettings settings);
}
