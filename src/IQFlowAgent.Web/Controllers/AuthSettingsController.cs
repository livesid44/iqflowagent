using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IQFlowAgent.Web.Controllers;

[Authorize(Roles = "SuperAdmin")]
public class AuthSettingsController : Controller
{
    private readonly IAuthSettingsService _authSettings;

    public AuthSettingsController(IAuthSettingsService authSettings)
    {
        _authSettings = authSettings;
    }

    public async Task<IActionResult> Index()
    {
        var settings = await _authSettings.GetSettingsAsync();
        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AuthSettings model)
    {
        try
        {
            await _authSettings.SaveSettingsAsync(model);
            TempData["Success"] = "Authentication settings saved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to save settings: {ex.Message}";
        }
        return RedirectToAction("Index");
    }
}
