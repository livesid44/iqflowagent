using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class MasterDataController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<MasterDataController> _logger;
    private readonly ITenantContextService _tenantContext;

    public MasterDataController(ApplicationDbContext db, ILogger<MasterDataController> logger, ITenantContextService tenantContext)
    {
        _db = db;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    // GET /MasterData/Department
    public async Task<IActionResult> Department()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var departments = await _db.MasterDepartments
            .Where(d => d.TenantId == tenantId)
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

        var tenantId = _tenantContext.GetCurrentTenantId();
        var exists = await _db.MasterDepartments
            .AnyAsync(d => d.TenantId == tenantId && d.Name.ToLower() == name.Trim().ToLower());
        if (exists)
        {
            TempData["Error"] = $"Department '{name.Trim()}' already exists.";
            return RedirectToAction(nameof(Department));
        }

        _db.MasterDepartments.Add(new MasterDepartment
        {
            TenantId    = tenantId,
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

    // ═══════════════════════════════════════════════════════════
    //  LOB (Line of Business) master data
    // ═══════════════════════════════════════════════════════════

    // GET /MasterData/Lob
    public async Task<IActionResult> Lob()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var lobs = await _db.MasterLobs
            .Where(l => l.TenantId == tenantId)
            .OrderBy(l => l.DepartmentName)
            .ThenBy(l => l.Name)
            .ToListAsync();
        ViewBag.Departments = await _db.MasterDepartments
            .Where(d => d.IsActive && d.TenantId == tenantId)
            .OrderBy(d => d.Name)
            .Select(d => d.Name)
            .ToListAsync();
        return View(lobs);
    }

    // POST /MasterData/CreateLob
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLob(string name, string departmentName, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(departmentName))
        {
            TempData["Error"] = "LOB name and Department are required.";
            return RedirectToAction(nameof(Lob));
        }

        var tenantId = _tenantContext.GetCurrentTenantId();
        var exists = await _db.MasterLobs.AnyAsync(l =>
            l.TenantId == tenantId &&
            l.DepartmentName == departmentName.Trim() &&
            l.Name.ToLower() == name.Trim().ToLower());

        if (exists)
        {
            TempData["Error"] = $"LOB '{name.Trim()}' already exists under '{departmentName.Trim()}'.";
            return RedirectToAction(nameof(Lob));
        }

        _db.MasterLobs.Add(new MasterLob
        {
            TenantId       = tenantId,
            DepartmentName = departmentName.Trim(),
            Name           = name.Trim(),
            Description    = description?.Trim(),
            IsActive       = true
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"LOB '{name.Trim()}' created.";
        return RedirectToAction(nameof(Lob));
    }

    // POST /MasterData/EditLob/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditLob(int id, string name, string departmentName, string? description, bool isActive)
    {
        var lob = await _db.MasterLobs.FindAsync(id);
        if (lob == null) return NotFound();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "LOB name is required.";
            return RedirectToAction(nameof(Lob));
        }

        lob.Name           = name.Trim();
        lob.DepartmentName = departmentName.Trim();
        lob.Description    = description?.Trim();
        lob.IsActive       = isActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"LOB '{name.Trim()}' updated.";
        return RedirectToAction(nameof(Lob));
    }

    // POST /MasterData/DeleteLob/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> DeleteLob(int id)
    {
        var lob = await _db.MasterLobs.FindAsync(id);
        if (lob == null) return NotFound();

        _db.MasterLobs.Remove(lob);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"LOB '{lob.Name}' deleted.";
        return RedirectToAction(nameof(Lob));
    }

    // POST /MasterData/ToggleLob/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLob(int id)
    {
        var lob = await _db.MasterLobs.FindAsync(id);
        if (lob == null) return NotFound();

        lob.IsActive = !lob.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"LOB '{lob.Name}' {(lob.IsActive ? "activated" : "deactivated")}.";
        return RedirectToAction(nameof(Lob));
    }
}

