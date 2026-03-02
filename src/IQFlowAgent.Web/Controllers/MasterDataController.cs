using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class MasterDataController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<MasterDataController> _logger;

    public MasterDataController(ApplicationDbContext db, ILogger<MasterDataController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // GET /MasterData/Department
    public async Task<IActionResult> Department()
    {
        var departments = await _db.MasterDepartments
            .OrderBy(d => d.Name)
            .ToListAsync();
        return View(departments);
    }

    // POST /MasterData/CreateDepartment
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDepartment(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Department name is required.";
            return RedirectToAction(nameof(Department));
        }

        var exists = await _db.MasterDepartments
            .AnyAsync(d => d.Name.ToLower() == name.Trim().ToLower());
        if (exists)
        {
            TempData["Error"] = $"Department '{name.Trim()}' already exists.";
            return RedirectToAction(nameof(Department));
        }

        _db.MasterDepartments.Add(new MasterDepartment
        {
            Name        = name.Trim(),
            Description = description?.Trim(),
            IsActive    = true
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Department '{name.Trim()}' created successfully.";
        return RedirectToAction(nameof(Department));
    }

    // POST /MasterData/EditDepartment/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDepartment(int id, string name, string? description, bool isActive)
    {
        var dept = await _db.MasterDepartments.FindAsync(id);
        if (dept == null) return NotFound();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Department name is required.";
            return RedirectToAction(nameof(Department));
        }

        dept.Name        = name.Trim();
        dept.Description = description?.Trim();
        dept.IsActive    = isActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Department '{name.Trim()}' updated.";
        return RedirectToAction(nameof(Department));
    }

    // POST /MasterData/DeleteDepartment/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> DeleteDepartment(int id)
    {
        var dept = await _db.MasterDepartments.FindAsync(id);
        if (dept == null) return NotFound();

        _db.MasterDepartments.Remove(dept);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Department '{dept.Name}' deleted.";
        return RedirectToAction(nameof(Department));
    }

    // POST /MasterData/ToggleDepartment/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleDepartment(int id)
    {
        var dept = await _db.MasterDepartments.FindAsync(id);
        if (dept == null) return NotFound();

        dept.IsActive = !dept.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Department '{dept.Name}' {(dept.IsActive ? "activated" : "deactivated")}.";
        return RedirectToAction(nameof(Department));
    }
}
