using System.Security.Claims;
using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class TenantAiSettingsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContextService _tenantContext;
    private readonly ILogger<TenantAiSettingsController> _logger;

    public TenantAiSettingsController(
        ApplicationDbContext db,
        ITenantContextService tenantContext,
        ILogger<TenantAiSettingsController> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            var tenantId = _tenantContext.GetCurrentTenantId();
            var tenant = await _db.Tenants.FindAsync(tenantId);
            if (tenant == null)
            {
                _logger.LogWarning("TenantAiSettings/Index: tenant {TenantId} not found", tenantId);
                return NotFound();
            }
            var settings = await _db.TenantAiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new TenantAiSettings { TenantId = tenantId };
            ViewBag.Tenant = tenant;
            return View(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TenantAiSettings/Index failed");
            TempData["Error"] = $"Failed to load settings: {ex.Message}";
            ViewBag.Tenant = null;
            return View(new TenantAiSettings());
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(TenantAiSettings model)
    {
        // Clear model-binding errors for the navigation property (BindNever skips it but
        // older model-state entries from complex-type probing may still linger)
        ModelState.Remove(nameof(TenantAiSettings.Tenant));

        // Wrap EVERYTHING (including tenant lookup) in try/catch so any DB or
        // infrastructure exception shows the user a meaningful error instead of
        // the generic ASP.NET Core 500 page.
        try
        {
            var tenantId = _tenantContext.GetCurrentTenantId();

            var tenant = await _db.Tenants.FindAsync(tenantId);
            if (tenant == null)
            {
                _logger.LogWarning("TenantAiSettings/Save: tenant {TenantId} not found", tenantId);
                TempData["Error"] = $"Tenant (id={tenantId}) was not found. Please log out and log back in.";
                ViewBag.Tenant = null;
                return View("Index", model);
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Tenant = tenant;
                TempData["Error"] = "Please correct the validation errors below.";
                return View("Index", model);
            }

            var existing = await _db.TenantAiSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            if (existing == null)
            {
                model.TenantId        = tenantId;
                model.UpdatedAt       = DateTime.UtcNow;
                model.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                _db.TenantAiSettings.Add(model);
            }
            else
            {
                existing.AzureOpenAIEndpoint          = model.AzureOpenAIEndpoint;
                existing.AzureOpenAIApiKey             = model.AzureOpenAIApiKey;
                existing.AzureOpenAIDeploymentName     = model.AzureOpenAIDeploymentName;
                existing.AzureOpenAIApiVersion         = model.AzureOpenAIApiVersion;
                existing.AzureOpenAIMaxTokens          = model.AzureOpenAIMaxTokens;
                existing.AzureStorageConnectionString  = model.AzureStorageConnectionString;
                existing.AzureStorageContainerName     = model.AzureStorageContainerName;
                existing.AzureSpeechRegion             = model.AzureSpeechRegion;
                existing.AzureSpeechApiKey             = model.AzureSpeechApiKey;
                existing.UpdatedAt                     = DateTime.UtcNow;
                existing.UpdatedByUserId               = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "AI & Storage settings saved successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TenantAiSettings/Save failed for model {@Model}", new
            {
                model.TenantId,
                HasEndpoint = !string.IsNullOrEmpty(model.AzureOpenAIEndpoint),
                HasDeployment = !string.IsNullOrEmpty(model.AzureOpenAIDeploymentName)
            });
            ViewBag.Tenant = null;
            TempData["Error"] = $"Failed to save settings: {ex.Message}";
            return View("Index", model);
        }
    }
}
