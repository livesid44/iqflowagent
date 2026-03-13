using System.Security.Claims;
using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

/// <summary>Flat DTO for binding TenantPiiSettings from a form post.</summary>
public class TenantPiiSettingsDto
{
    public int  Id       { get; set; }
    public int  TenantId { get; set; }

    public bool IsEnabled            { get; set; }
    public bool BlockOnDetection     { get; set; }

    public bool DetectEmailAddresses    { get; set; }
    public bool DetectPhoneNumbers      { get; set; }
    public bool DetectCreditCardNumbers { get; set; }
    public bool DetectSsnNumbers        { get; set; }
    public bool DetectIpAddresses       { get; set; }
    public bool DetectPassportNumbers   { get; set; }
    public bool DetectDatesOfBirth      { get; set; }
    public bool DetectUrls              { get; set; }
    public bool DetectPersonNames       { get; set; }
}

[Authorize(Roles = "SuperAdmin,Admin")]
public class PiiSettingsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContextService _tenantContext;
    private readonly IPiiScanService _piiScanner;
    private readonly ILogger<PiiSettingsController> _logger;

    public PiiSettingsController(
        ApplicationDbContext db,
        ITenantContextService tenantContext,
        IPiiScanService piiScanner,
        ILogger<PiiSettingsController> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _piiScanner = piiScanner;
        _logger = logger;
    }

    // GET /PiiSettings
    public async Task<IActionResult> Index()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var tenant   = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return NotFound();

        var settings = await _db.TenantPiiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId)
            ?? new TenantPiiSettings { TenantId = tenantId };

        ViewBag.Tenant = tenant;
        return View(settings);
    }

    // POST /PiiSettings/Save
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] TenantPiiSettingsDto model)
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
            var tenant   = await _db.Tenants.FindAsync(tenantId);
            if (tenant == null)
                return Json(new { success = false, message = $"Tenant (id={tenantId}) not found." });

            var existing = await _db.TenantPiiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId);

            if (existing == null)
            {
                _db.TenantPiiSettings.Add(new TenantPiiSettings
                {
                    TenantId                = tenantId,
                    IsEnabled               = model.IsEnabled,
                    BlockOnDetection        = model.BlockOnDetection,
                    DetectEmailAddresses    = model.DetectEmailAddresses,
                    DetectPhoneNumbers      = model.DetectPhoneNumbers,
                    DetectCreditCardNumbers = model.DetectCreditCardNumbers,
                    DetectSsnNumbers        = model.DetectSsnNumbers,
                    DetectIpAddresses       = model.DetectIpAddresses,
                    DetectPassportNumbers   = model.DetectPassportNumbers,
                    DetectDatesOfBirth      = model.DetectDatesOfBirth,
                    DetectUrls              = model.DetectUrls,
                    DetectPersonNames       = model.DetectPersonNames,
                    UpdatedAt               = DateTime.UtcNow,
                    UpdatedByUserId         = User.FindFirstValue(ClaimTypes.NameIdentifier),
                });
            }
            else
            {
                existing.IsEnabled               = model.IsEnabled;
                existing.BlockOnDetection        = model.BlockOnDetection;
                existing.DetectEmailAddresses    = model.DetectEmailAddresses;
                existing.DetectPhoneNumbers      = model.DetectPhoneNumbers;
                existing.DetectCreditCardNumbers = model.DetectCreditCardNumbers;
                existing.DetectSsnNumbers        = model.DetectSsnNumbers;
                existing.DetectIpAddresses       = model.DetectIpAddresses;
                existing.DetectPassportNumbers   = model.DetectPassportNumbers;
                existing.DetectDatesOfBirth      = model.DetectDatesOfBirth;
                existing.DetectUrls              = model.DetectUrls;
                existing.DetectPersonNames       = model.DetectPersonNames;
                existing.UpdatedAt               = DateTime.UtcNow;
                existing.UpdatedByUserId         = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("PII settings saved for tenant {TenantId}", tenantId);
            return Json(new { success = true, message = "PII/SPII safeguard settings saved successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PiiSettings/Save failed");
            return Json(new { success = false, message = ex.Message });
        }
    }

    // POST /PiiSettings/Scan  (test / preview endpoint — does not persist any settings)
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Scan([FromBody] PiiScanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Json(new { success = false, message = "No text provided." });

        // Limit input to 50 KB to prevent resource abuse via the preview endpoint.
        const int MaxScanChars = 50_000;
        if (request.Text.Length > MaxScanChars)
            return Json(new { success = false, message = $"Input is too large. Maximum allowed size for the scanner preview is {MaxScanChars:N0} characters." });

        var settings = new TenantPiiSettings
        {
            IsEnabled               = true,
            BlockOnDetection        = false,
            DetectEmailAddresses    = request.DetectEmailAddresses,
            DetectPhoneNumbers      = request.DetectPhoneNumbers,
            DetectCreditCardNumbers = request.DetectCreditCardNumbers,
            DetectSsnNumbers        = request.DetectSsnNumbers,
            DetectIpAddresses       = request.DetectIpAddresses,
            DetectPassportNumbers   = request.DetectPassportNumbers,
            DetectDatesOfBirth      = request.DetectDatesOfBirth,
            DetectUrls              = request.DetectUrls,
            DetectPersonNames       = request.DetectPersonNames,
        };

        var result = _piiScanner.ScanWithSettings(request.Text, settings);

        return Json(new
        {
            success     = true,
            hasPii      = result.HasPii,
            findingCount = result.Findings.Count,
            findings    = result.Findings.Select(f => new { f.EntityType, f.MatchedText }),
            redactedText = result.RedactedText,
        });
    }
}

/// <summary>Request body for the live-scan preview endpoint.</summary>
public sealed class PiiScanRequest
{
    public string Text { get; set; } = string.Empty;
    public bool DetectEmailAddresses    { get; set; } = true;
    public bool DetectPhoneNumbers      { get; set; } = true;
    public bool DetectCreditCardNumbers { get; set; } = true;
    public bool DetectSsnNumbers        { get; set; } = true;
    public bool DetectIpAddresses       { get; set; } = true;
    public bool DetectPassportNumbers   { get; set; } = true;
    public bool DetectDatesOfBirth      { get; set; }
    public bool DetectUrls              { get; set; }
    public bool DetectPersonNames       { get; set; }
}
