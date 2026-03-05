using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

public interface IAuthSettingsService
{
    Task<AuthSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AuthSettings settings);
}
