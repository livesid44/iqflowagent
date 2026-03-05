using System.Security.Claims;
using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IQFlowAgent.Web.Controllers;

// Flat DTO — no EF navigation properties, no complex-type binding issues
public class TenantAiSettingsDto
{
    public int    Id                            { get; set; }
    public int    TenantId                      { get; set; }
    public string? AzureOpenAIEndpoint          { get; set; }
    public string? AzureOpenAIApiKey            { get; set; }
    public string? AzureOpenAIDeploymentName    { get; set; }
    public string? AzureOpenAIApiVersion        { get; set; }
    public int    AzureOpenAIMaxTokens          { get; set; }
    public string? AzureStorageConnectionString { get; set; }
    public string? AzureStorageContainerName    { get; set; }
    public string? AzureSpeechRegion            { get; set; }
    public string? AzureSpeechApiKey            { get; set; }
}

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

    // Returns JSON so the browser can display the real error in console / inline banner.
    // Uses a flat DTO to avoid model-binding issues with EF navigation properties.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] TenantAiSettingsDto model)
    {
        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ", ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));
            return Json(new { success = false, message = $"Validation failed: {errors}" });
        }

        try
        {
            var tenantId = _tenantContext.GetCurrentTenantId();

            var tenant = await _db.Tenants.FindAsync(tenantId);
            if (tenant == null)
            {
                var msg = $"Tenant (id={tenantId}) not found. Please log out and log in again.";
                _logger.LogWarning("TenantAiSettings/Save: {Msg}", msg);
                return Json(new { success = false, error = msg });
            }

            var existing = await _db.TenantAiSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            if (existing == null)
            {
                _db.TenantAiSettings.Add(new TenantAiSettings
                {
                    TenantId                      = tenantId,
                    AzureOpenAIEndpoint            = model.AzureOpenAIEndpoint ?? string.Empty,
                    AzureOpenAIApiKey              = model.AzureOpenAIApiKey ?? string.Empty,
                    AzureOpenAIDeploymentName      = model.AzureOpenAIDeploymentName ?? string.Empty,
                    AzureOpenAIApiVersion          = model.AzureOpenAIApiVersion ?? "2025-01-01-preview",
                    AzureOpenAIMaxTokens           = model.AzureOpenAIMaxTokens > 0 ? model.AzureOpenAIMaxTokens : 4096,
                    AzureStorageConnectionString   = model.AzureStorageConnectionString ?? string.Empty,
                    AzureStorageContainerName      = model.AzureStorageContainerName ?? "intakes",
                    AzureSpeechRegion              = model.AzureSpeechRegion ?? string.Empty,
                    AzureSpeechApiKey              = model.AzureSpeechApiKey ?? string.Empty,
                    UpdatedAt                      = DateTime.UtcNow,
                    UpdatedByUserId                = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                });
            }
            else
            {
                existing.AzureOpenAIEndpoint          = model.AzureOpenAIEndpoint ?? string.Empty;
                existing.AzureOpenAIApiKey             = model.AzureOpenAIApiKey ?? string.Empty;
                existing.AzureOpenAIDeploymentName     = model.AzureOpenAIDeploymentName ?? string.Empty;
                existing.AzureOpenAIApiVersion         = model.AzureOpenAIApiVersion ?? "2025-01-01-preview";
                existing.AzureOpenAIMaxTokens          = model.AzureOpenAIMaxTokens > 0 ? model.AzureOpenAIMaxTokens : 4096;
                existing.AzureStorageConnectionString  = model.AzureStorageConnectionString ?? string.Empty;
                existing.AzureStorageContainerName     = model.AzureStorageContainerName ?? "intakes";
                existing.AzureSpeechRegion             = model.AzureSpeechRegion ?? string.Empty;
                existing.AzureSpeechApiKey             = model.AzureSpeechApiKey ?? string.Empty;
                existing.UpdatedAt                     = DateTime.UtcNow;
                existing.UpdatedByUserId               = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("TenantAiSettings saved for tenant {TenantId}", tenantId);
            return Json(new { success = true, message = "AI & Storage settings saved successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TenantAiSettings/Save failed");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // Diagnostic endpoint — call GET /TenantAiSettings/Diagnose for DB health JSON
    [HttpGet]
    public async Task<IActionResult> Diagnose()
    {
        var result = new Dictionary<string, object>();
        try
        {
            var tenantId = _tenantContext.GetCurrentTenantId();
            result["tenantId"] = tenantId;
            result["provider"] = _db.Database.ProviderName ?? "unknown";

            // Connection test
            try
            {
                await _db.Database.OpenConnectionAsync();
                await _db.Database.CloseConnectionAsync();
                result["canConnect"] = true;
            }
            catch (Exception cx)
            {
                result["canConnect"] = false;
                result["connectionError"] = cx.Message;
            }

            // Migration status
            try
            {
                var migrator = _db.GetInfrastructure().GetRequiredService<IMigrator>();
                var applied  = (await _db.Database.GetAppliedMigrationsAsync()).ToList();
                var pending  = (await _db.Database.GetPendingMigrationsAsync()).ToList();
                result["appliedMigrations"] = applied.Count;
                result["pendingMigrations"] = pending.Count;
                result["pendingMigrationNames"] = pending;
            }
            catch (Exception mx)
            {
                result["migrationError"] = mx.Message;
            }

            // Table list
            try
            {
                var tables = new List<string>();
                var conn = _db.Database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = _db.Database.ProviderName?.Contains("Sqlite") == true
                    ? "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
                    : "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME";
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync()) tables.Add(rdr.GetString(0));
                await conn.CloseAsync();
                result["tables"] = tables;
            }
            catch (Exception tx)
            {
                result["tableError"] = tx.Message;
            }

            // TenantAiSettings row
            try
            {
                var settings = await _db.TenantAiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId);
                result["hasSettings"] = settings != null;
                if (settings != null)
                {
                    result["hasEndpoint"]    = !string.IsNullOrEmpty(settings.AzureOpenAIEndpoint);
                    result["hasDeployment"]  = !string.IsNullOrEmpty(settings.AzureOpenAIDeploymentName);
                    result["lastUpdated"]    = settings.UpdatedAt;
                }
            }
            catch (Exception sx)
            {
                result["settingsError"] = sx.Message;
            }
        }
        catch (Exception ex)
        {
            result["fatalError"] = ex.Message;
        }

        return Json(result);
    }
}
