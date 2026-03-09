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

    // ═══════════════════════════════════════════════════════════
    //  LOT Country/City Mapping
    // ═══════════════════════════════════════════════════════════

    private static readonly string[] SdcLotOptions =
    [
        "Lot 1 – Global Customer Support",
        "Lot 2 – Quote to Bill",
        "Lot 3 – International Integrator",
        "Lot 4 – One Post Sales"
    ];

    // GET /MasterData/LotCountryMapping
    public async Task<IActionResult> LotCountryMapping()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var mappings = await _db.LotCountryMappings
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.LotName)
            .ThenBy(m => m.Country)
            .ToListAsync();

        var settings = await _db.TenantAiSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId);

        ViewBag.LotOptions = SdcLotOptions;
        ViewBag.UseCountryFilterByLot = settings?.UseCountryFilterByLot ?? false;
        return View(mappings);
    }

    // POST /MasterData/CreateLotCountryMapping
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLotCountryMapping(
        string lotName, string country, string? cities)
    {
        if (string.IsNullOrWhiteSpace(lotName) || string.IsNullOrWhiteSpace(country))
        {
            TempData["Error"] = "LOT name and Country are required.";
            return RedirectToAction(nameof(LotCountryMapping));
        }

        var tenantId       = _tenantContext.GetCurrentTenantId();
        var trimmedLot     = lotName.Trim();
        var trimmedCountry = country.Trim();

        // New cities submitted by the user (empty list = "all cities" wildcard)
        var newCities = (cities ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var existing = await _db.LotCountryMappings.FirstOrDefaultAsync(m =>
            m.TenantId == tenantId &&
            m.LotName  == trimmedLot &&
            m.Country.ToLower() == trimmedCountry.ToLower());

        if (existing == null)
        {
            // No row yet for this LOT + Country — create fresh
            _db.LotCountryMappings.Add(new LotCountryMapping
            {
                TenantId = tenantId,
                LotName  = trimmedLot,
                Country  = trimmedCountry,
                Cities   = cities?.Trim() ?? string.Empty,
                IsActive = true
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Mapping '{trimmedCountry}' → '{trimmedLot}' created.";
            return RedirectToAction(nameof(LotCountryMapping));
        }

        // Row already exists — merge at city level
        if (newCities.Count == 0)
        {
            // Submitting "all cities" when a row already exists is ambiguous; redirect to Edit
            TempData["Error"] = $"'{trimmedCountry}' is already mapped under '{trimmedLot}'. Use Edit to update its cities.";
            return RedirectToAction(nameof(LotCountryMapping));
        }

        // If the existing row already covers "all cities" (empty Cities), any specific city is already included
        if (string.IsNullOrWhiteSpace(existing.Cities))
        {
            TempData["Error"] = $"'{trimmedCountry}' under '{trimmedLot}' is already mapped for all cities. Use Edit to restrict it to specific cities.";
            return RedirectToAction(nameof(LotCountryMapping));
        }

        var existingCities = existing.Cities
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = newCities.Where(c => !existingCities.Contains(c)).ToList();

        if (toAdd.Count == 0)
        {
            var dupes = string.Join(", ", newCities);
            TempData["Error"] = $"City/cities '{dupes}' already exist under '{trimmedCountry}' → '{trimmedLot}'.";
            return RedirectToAction(nameof(LotCountryMapping));
        }

        // Merge and save
        existing.Cities = string.Join(",", existingCities.Concat(toAdd).OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Added {toAdd.Count} city/cities ({string.Join(", ", toAdd)}) to '{trimmedCountry}' → '{trimmedLot}'.";
        return RedirectToAction(nameof(LotCountryMapping));
    }

    // POST /MasterData/EditLotCountryMapping/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditLotCountryMapping(
        int id, string lotName, string country, string? cities, bool isActive)
    {
        var mapping = await _db.LotCountryMappings.FindAsync(id);
        if (mapping == null) return NotFound();

        if (string.IsNullOrWhiteSpace(lotName) || string.IsNullOrWhiteSpace(country))
        {
            TempData["Error"] = "LOT name and Country are required.";
            return RedirectToAction(nameof(LotCountryMapping));
        }

        var trimmedLot     = lotName.Trim();
        var trimmedCountry = country.Trim();
        var tenantId       = _tenantContext.GetCurrentTenantId();

        // If the LOT + Country target is being changed, ensure no city-level clash
        // with any OTHER existing row for the new LOT + Country.
        if (!string.Equals(mapping.LotName, trimmedLot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(mapping.Country, trimmedCountry, StringComparison.OrdinalIgnoreCase))
        {
            var newCities = (cities ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var sibling = await _db.LotCountryMappings.FirstOrDefaultAsync(m =>
                m.Id       != id &&
                m.TenantId == tenantId &&
                m.LotName  == trimmedLot &&
                m.Country.ToLower() == trimmedCountry.ToLower());

            if (sibling != null && newCities.Count > 0)
            {
                var siblingCities = sibling.Cities
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var clashes = newCities.Where(c => siblingCities.Contains(c)).ToList();

                // A sibling row with empty Cities already covers all cities — every new city clashes
                if (string.IsNullOrWhiteSpace(sibling.Cities) || clashes.Count > 0)
                {
                    var clashList = string.IsNullOrWhiteSpace(sibling.Cities)
                        ? string.Join(", ", newCities)
                        : string.Join(", ", clashes);
                    TempData["Error"] = $"City/cities '{clashList}' already exist under '{trimmedCountry}' → '{trimmedLot}'.";
                    return RedirectToAction(nameof(LotCountryMapping));
                }
            }
        }

        mapping.LotName  = trimmedLot;
        mapping.Country  = trimmedCountry;
        mapping.Cities   = cities?.Trim() ?? string.Empty;
        mapping.IsActive = isActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Mapping '{trimmedCountry}' updated.";
        return RedirectToAction(nameof(LotCountryMapping));
    }

    // POST /MasterData/DeleteLotCountryMapping/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> DeleteLotCountryMapping(int id)
    {
        var mapping = await _db.LotCountryMappings.FindAsync(id);
        if (mapping == null) return NotFound();

        _db.LotCountryMappings.Remove(mapping);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Mapping '{mapping.Country}' deleted.";
        return RedirectToAction(nameof(LotCountryMapping));
    }

    // POST /MasterData/ToggleLotCountryMapping/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLotCountryMapping(int id)
    {
        var mapping = await _db.LotCountryMappings.FindAsync(id);
        if (mapping == null) return NotFound();

        mapping.IsActive = !mapping.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Mapping '{mapping.Country}' {(mapping.IsActive ? "activated" : "deactivated")}.";
        return RedirectToAction(nameof(LotCountryMapping));
    }

    // POST /MasterData/SaveLotFilterSetting — toggle UseCountryFilterByLot
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLotFilterSetting(bool useCountryFilterByLot)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var settings = await _db.TenantAiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId);
        if (settings != null)
        {
            settings.UseCountryFilterByLot = useCountryFilterByLot;
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = useCountryFilterByLot
            ? "Country/City filter by LOT is now enabled."
            : "Country/City filter by LOT is now disabled (global master list will be used).";
        return RedirectToAction(nameof(LotCountryMapping));
    }
}

