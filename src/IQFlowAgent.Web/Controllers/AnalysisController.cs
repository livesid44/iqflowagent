using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

[Authorize]
public class AnalysisController : Controller
{
    private readonly ApplicationDbContext _db;

    public AnalysisController(ApplicationDbContext db) => _db = db;

    // GET /Analysis?selectedId=&search=&country=&businessUnit=&processType=
    public async Task<IActionResult> Index(
        int? selectedId, string? search, string? country,
        string? businessUnit, string? processType)
    {
        var allIntakes = await _db.IntakeRecords
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        FillPickerViewBag(allIntakes, selectedId, search, country, businessUnit, processType,
            "Analysis", "Index");

        IntakeRecord? selected = selectedId.HasValue
            ? allIntakes.FirstOrDefault(x => x.Id == selectedId.Value)
            : null;

        if (selected != null)
        {
            ViewBag.SelectedTaskCount = await _db.IntakeTasks
                .CountAsync(t => t.IntakeRecordId == selected.Id);
            ViewBag.SelectedOpenTaskCount = await _db.IntakeTasks
                .CountAsync(t => t.IntakeRecordId == selected.Id && t.Status == "Open");
        }

        var model = selected != null
            ? new List<IntakeRecord> { selected }
            : new List<IntakeRecord>();

        return View(model);
    }

    private void FillPickerViewBag(
        List<IntakeRecord> allIntakes, int? selectedId,
        string? search, string? country, string? businessUnit, string? processType,
        string controller, string action)
    {
        var filtered = allIntakes.Where(x =>
            (string.IsNullOrWhiteSpace(search) ||
             x.IntakeId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.BusinessUnit.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.Department.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(country) || x.Country == country)
            && (string.IsNullOrWhiteSpace(businessUnit) || x.BusinessUnit == businessUnit)
            && (string.IsNullOrWhiteSpace(processType) || x.ProcessType == processType)
        ).ToList();

        ViewBag.IntakePickerIntakes      = filtered;
        ViewBag.IntakePickerSelected     = selectedId;
        ViewBag.IntakePickerSearch       = search;
        ViewBag.IntakePickerCountry      = country;
        ViewBag.IntakePickerBusinessUnit = businessUnit;
        ViewBag.IntakePickerProcessType  = processType;
        ViewBag.IntakePickerController   = controller;
        ViewBag.IntakePickerAction       = action;
        ViewBag.Countries     = allIntakes.Select(x => x.Country)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        ViewBag.BusinessUnits = allIntakes.Select(x => x.BusinessUnit)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        ViewBag.ProcessTypes  = allIntakes.Select(x => x.ProcessType)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
    }
}
