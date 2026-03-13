using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IQFlowAgent.Web.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class AuditLogController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContextService _tenantContext;
    private readonly ILogger<AuditLogController> _logger;

    private const int PageSize = 50;

    public AuditLogController(
        ApplicationDbContext db,
        ITenantContextService tenantContext,
        ILogger<AuditLogController> logger)
    {
        _db            = db;
        _tenantContext = tenantContext;
        _logger        = logger;
    }

    // GET /AuditLog?page=1&eventType=&callSite=&intakeId=
    public async Task<IActionResult> Index(
        int page = 1,
        string? eventType = null,
        string? callSite  = null,
        string? outcome   = null,
        int?    intakeId  = null)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();

        var query = _db.AuditLogs
            .Where(l => l.TenantId == tenantId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(l => l.EventType == eventType);
        if (!string.IsNullOrWhiteSpace(callSite))
            query = query.Where(l => l.CallSite == callSite);
        if (!string.IsNullOrWhiteSpace(outcome))
            query = query.Where(l => l.Outcome == outcome);
        if (intakeId.HasValue)
            query = query.Where(l => l.IntakeRecordId == intakeId.Value);

        var totalCount = await query.CountAsync();

        // Paginate with a DB-level sort on CreatedAt (datetime2 after FixAuditLogsColumnTypes
        // migration).  Only the 50 rows on the requested page are fetched.
        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        // Summary stats — WHERE comparisons on string columns are safe on SQL Server
        // even for legacy text type; only ORDER BY / DISTINCT are restricted.
        var allForTenant = _db.AuditLogs.Where(l => l.TenantId == tenantId);
        ViewBag.TotalCalls    = await allForTenant.CountAsync(l => l.EventType == "LlmCall");
        ViewBag.TotalPiiScans = await allForTenant.CountAsync(l => l.EventType == "PiiScan");
        ViewBag.PiiFailCount  = await allForTenant.CountAsync(l => l.EventType == "PiiScan" && l.PiiScanStatus == "Fail");
        ViewBag.BlockedCount  = await allForTenant.CountAsync(l => l.WasBlocked);
        ViewBag.MockedCount   = await allForTenant.CountAsync(l => l.IsMocked);

        ViewBag.Page       = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / PageSize);
        ViewBag.TotalCount = totalCount;

        ViewBag.FilterEventType = eventType ?? "";
        ViewBag.FilterCallSite  = callSite  ?? "";
        ViewBag.FilterOutcome   = outcome   ?? "";
        ViewBag.FilterIntakeId  = intakeId;

        // Distinct dropdown values — fetched as a lightweight 2-column projection and
        // processed in memory to avoid SQL Server "text cannot be used in ORDER BY /
        // DISTINCT" errors that occur when the column type is still the legacy TEXT type
        // (i.e., before the FixAuditLogsColumnTypes migration has been applied).
        var dropdownMeta = await _db.AuditLogs
            .Where(l => l.TenantId == tenantId)
            .Select(l => new { l.EventType, l.CallSite })
            .ToListAsync();

        ViewBag.EventTypes = dropdownMeta
            .Select(l => l.EventType)
            .Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
        ViewBag.CallSites  = dropdownMeta
            .Select(l => l.CallSite)
            .Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();

        return View(logs);
    }

    // GET /AuditLog/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var log = await _db.AuditLogs
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId);

        if (log == null) return NotFound();

        // Load paired records that share the same correlationId (PII scan + LLM call)
        var paired = await _db.AuditLogs
            .Where(l => l.TenantId == tenantId
                     && l.CorrelationId == log.CorrelationId
                     && l.Id != id)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        ViewBag.PairedLogs = paired;

        // Parse PII findings
        List<PiiAuditFinding> findings = [];
        if (!string.IsNullOrWhiteSpace(log.PiiFindingsJson))
        {
            try
            {
                findings = JsonSerializer.Deserialize<List<PiiAuditFinding>>(log.PiiFindingsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse PiiFindingsJson for log {LogId}", id);
            }
        }
        ViewBag.PiiFindings = findings;

        // Load intake if linked
        IntakeRecord? intake = null;
        if (log.IntakeRecordId.HasValue)
            intake = await _db.IntakeRecords.FindAsync(log.IntakeRecordId.Value);
        ViewBag.LinkedIntake = intake;

        return View(log);
    }

    // GET /AuditLog/Sequence/5  — view full correlation sequence for a CorrelationId
    public async Task<IActionResult> Sequence(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
            return BadRequest("Invalid correlation ID.");

        var tenantId = _tenantContext.GetCurrentTenantId();
        var logs = await _db.AuditLogs
            .Where(l => l.TenantId == tenantId && l.CorrelationId == correlationId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        if (logs.Count == 0) return NotFound();

        // Parse findings for any PII scan entries
        var findingsByLogId = new Dictionary<int, List<PiiAuditFinding>>();
        foreach (var log in logs.Where(l => !string.IsNullOrWhiteSpace(l.PiiFindingsJson)))
        {
            try
            {
                var f = JsonSerializer.Deserialize<List<PiiAuditFinding>>(log.PiiFindingsJson!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                findingsByLogId[log.Id] = f;
            }
            catch { /* ignore parse errors */ }
        }
        ViewBag.FindingsByLogId = findingsByLogId;

        // Load linked intake
        var intakeId = logs.Select(l => l.IntakeRecordId).FirstOrDefault(id => id.HasValue);
        IntakeRecord? intake = intakeId.HasValue ? await _db.IntakeRecords.FindAsync(intakeId.Value) : null;
        ViewBag.LinkedIntake   = intake;
        ViewBag.CorrelationId  = correlationId;

        return View(logs);
    }
}

/// <summary>DTO used to deserialise the PII findings JSON stored in the audit log.</summary>
public sealed class PiiAuditFinding
{
    public string EntityType  { get; set; } = string.Empty;
    public string MatchedText { get; set; } = string.Empty;
}
