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

    public TenantAiSettingsController(ApplicationDbContext db, ITenantContextService tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IActionResult> Index()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return NotFound();
        var settings = await _db.TenantAiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId)
            ?? new TenantAiSettings { TenantId = tenantId };
        ViewBag.Tenant = tenant;
        return View(settings);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(TenantAiSettings model)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();

        // Reload tenant for the view in case we need to return the form
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return NotFound();

        // Clear model-binding errors for the navigation property (BindNever skips it but
        // older model-state entries from complex-type probing may still linger)
        ModelState.Remove(nameof(TenantAiSettings.Tenant));

        if (!ModelState.IsValid)
        {
            ViewBag.Tenant = tenant;
            TempData["Error"] = "Please correct the validation errors below.";
            return View("Index", model);
        }

        try
        {
            var existing = await _db.TenantAiSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            if (existing == null)
            {
                model.TenantId   = tenantId;
                model.UpdatedAt  = DateTime.UtcNow;
                model.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                _db.TenantAiSettings.Add(model);
            }
            else
            {
                existing.AzureOpenAIEndpoint        = model.AzureOpenAIEndpoint;
                existing.AzureOpenAIApiKey           = model.AzureOpenAIApiKey;
                existing.AzureOpenAIDeploymentName   = model.AzureOpenAIDeploymentName;
                existing.AzureOpenAIApiVersion       = model.AzureOpenAIApiVersion;
                existing.AzureOpenAIMaxTokens        = model.AzureOpenAIMaxTokens;
                existing.AzureStorageConnectionString = model.AzureStorageConnectionString;
                existing.AzureStorageContainerName   = model.AzureStorageContainerName;
                // Speech-to-Text fields (were previously missing from the update path)
                existing.AzureSpeechRegion  = model.AzureSpeechRegion;
                existing.AzureSpeechApiKey  = model.AzureSpeechApiKey;
                existing.UpdatedAt          = DateTime.UtcNow;
                existing.UpdatedByUserId    = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "AI & Storage settings saved successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ViewBag.Tenant = tenant;
            TempData["Error"] = $"Failed to save settings: {ex.Message}";
            return View("Index", model);
        }
    }
}
