using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Services;
using IQFlowAgent.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContextService _tenantContext;

    public DashboardController(ApplicationDbContext db, ITenantContextService tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    // GET /Dashboard
    public async Task<IActionResult> Index(
        string? search, string? status, string? businessUnit, string? country)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        // ── 1. Load all intakes (used for KPIs + filter dropdowns) ──────
        var allIntakes = await _db.IntakeRecords
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        // ── 2. Load task breakdown grouped by intake ─────────────────────
        var taskGroups = await _db.IntakeTasks
            .GroupBy(t => t.IntakeRecordId)
            .Select(g => new
            {
                IntakeRecordId = g.Key,
                Total      = g.Count(),
                Open       = g.Count(t => t.Status == "Open"),
                InProgress = g.Count(t => t.Status == "In Progress"),
                Completed  = g.Count(t => t.Status == "Completed"),
                Cancelled  = g.Count(t => t.Status == "Cancelled"),
            })
            .ToDictionaryAsync(g => g.IntakeRecordId);

        // ── 3. Which intakes have a generated final report ───────────────
        var reportIntakeIdsList = await _db.FinalReports
            .Select(r => r.IntakeRecordId)
            .ToListAsync();
        var reportIntakeIds = reportIntakeIdsList.ToHashSet();

        // ── 4. Apply filters for the table rows ──────────────────────────
        var filtered = allIntakes.Where(x =>
            (string.IsNullOrWhiteSpace(search) ||
             x.IntakeId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.BusinessUnit.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.Department.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(status)       || x.Status       == status)
            && (string.IsNullOrWhiteSpace(businessUnit) || x.BusinessUnit == businessUnit)
            && (string.IsNullOrWhiteSpace(country)      || x.Country      == country)
        ).ToList();

        // ── 5. Build per-intake summary rows ─────────────────────────────
        var rows = filtered.Select(intake =>
        {
            taskGroups.TryGetValue(intake.Id, out var tg);
            return new IntakeSummaryRow
            {
                Id             = intake.Id,
                IntakeId       = intake.IntakeId,
                ProcessName    = intake.ProcessName,
                BusinessUnit   = intake.BusinessUnit,
                Country        = intake.Country,
                Priority       = intake.Priority,
                Status         = intake.Status,
                CreatedAt      = intake.CreatedAt,
                HasFinalReport = reportIntakeIds.Contains(intake.Id),
                TotalTasks      = tg?.Total      ?? 0,
                OpenTasks       = tg?.Open       ?? 0,
                InProgressTasks = tg?.InProgress ?? 0,
                CompletedTasks  = tg?.Completed  ?? 0,
                CancelledTasks  = tg?.Cancelled  ?? 0,
            };
        }).ToList();

        // ── 6. Global KPI totals (always unfiltered) ─────────────────────
        var allTaskVals = taskGroups.Values;
        var vm = new DashboardViewModel
        {
            TotalIntakes    = allIntakes.Count,
            ActiveIntakes   = allIntakes.Count(x =>
                x.Status is "Submitted" or "Analyzing" or "Complete"),
            ClosedIntakes   = allIntakes.Count(x => x.Status == "Closed"),
            DraftIntakes    = allIntakes.Count(x => x.Status == "Draft"),
            ErrorIntakes    = allIntakes.Count(x => x.Status == "Error"),
            TotalTasks      = allTaskVals.Sum(t => t.Total),
            OpenTasks       = allTaskVals.Sum(t => t.Open),
            InProgressTasks = allTaskVals.Sum(t => t.InProgress),
            CompletedTasks  = allTaskVals.Sum(t => t.Completed),
            CancelledTasks  = allTaskVals.Sum(t => t.Cancelled),
            IntakeRows      = rows,
        };

        // ── 7. Filter dropdown values ─────────────────────────────────────
        ViewBag.SearchFilter       = search;
        ViewBag.StatusFilter       = status;
        ViewBag.BusinessUnitFilter = businessUnit;
        ViewBag.CountryFilter      = country;
        ViewBag.AllStatuses        = allIntakes.Select(x => x.Status)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        ViewBag.AllBusinessUnits   = allIntakes.Select(x => x.BusinessUnit)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        ViewBag.AllCountries       = allIntakes.Select(x => x.Country)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();

        return View(vm);
    }
}
