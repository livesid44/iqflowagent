using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

[Authorize]
public class TenantController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContextService _tenantContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public TenantController(ApplicationDbContext db, ITenantContextService tenantContext,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _tenantContext = tenantContext;
        _userManager = userManager;
    }

    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Index()
    {
        var tenants = await _db.Tenants.OrderBy(t => t.Name).ToListAsync();
        var userCounts = await _db.UserTenants
            .GroupBy(ut => ut.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count);
        ViewBag.UserCounts = userCounts;
        return View(tenants);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Create(string name, string slug, string color, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
        {
            TempData["Error"] = "Name and Slug are required.";
            return RedirectToAction(nameof(Index));
        }
        if (await _db.Tenants.AnyAsync(t => t.Slug == slug.ToLower()))
        {
            TempData["Error"] = $"Slug '{slug}' is already in use.";
            return RedirectToAction(nameof(Index));
        }
        var tenant = new Tenant
        {
            Name = name.Trim(),
            Slug = slug.ToLower().Trim(),
            Color = string.IsNullOrWhiteSpace(color) ? "#e31837" : color,
            Description = description?.Trim(),
            IsActive = true
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();
        _db.TenantAiSettings.Add(new TenantAiSettings { TenantId = tenant.Id });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Tenant '{name}' created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Edit(int id, string name, string color, string? description, bool isActive)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();
        tenant.Name = name.Trim();
        tenant.Color = color;
        tenant.Description = description?.Trim();
        tenant.IsActive = isActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Tenant '{name}' updated.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Users(int id)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();
        var assignments = await _db.UserTenants
            .Where(ut => ut.TenantId == id)
            .Include(ut => ut.User)
            .ToListAsync();
        var allUsers = await _userManager.Users.Where(u => u.IsActive).ToListAsync();
        ViewBag.Tenant = tenant;
        ViewBag.AllUsers = allUsers;
        return View(assignments);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> AssignUser(int tenantId, string userId, string tenantRole)
    {
        if (!await _db.Tenants.AnyAsync(t => t.Id == tenantId))
            return NotFound();
        if (await _db.UserTenants.AnyAsync(ut => ut.TenantId == tenantId && ut.UserId == userId))
        {
            TempData["Info"] = "User is already assigned to this tenant.";
            return RedirectToAction(nameof(Users), new { id = tenantId });
        }
        _db.UserTenants.Add(new UserTenant
        {
            UserId = userId,
            TenantId = tenantId,
            TenantRole = tenantRole,
            IsDefault = !await _db.UserTenants.AnyAsync(ut => ut.UserId == userId)
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "User assigned to tenant.";
        return RedirectToAction(nameof(Users), new { id = tenantId });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> RemoveUser(int tenantId, string userId)
    {
        var assignment = await _db.UserTenants
            .FirstOrDefaultAsync(ut => ut.TenantId == tenantId && ut.UserId == userId);
        if (assignment != null)
        {
            _db.UserTenants.Remove(assignment);
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = "User removed from tenant.";
        return RedirectToAction(nameof(Users), new { id = tenantId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(int tenantId, string? returnUrl)
    {
        var userId = _userManager.GetUserId(User);
        var isSuperAdmin = User.IsInRole("SuperAdmin");
        if (!isSuperAdmin && !await _db.UserTenants.AnyAsync(ut => ut.UserId == userId && ut.TenantId == tenantId))
        {
            TempData["Error"] = "You do not have access to this tenant.";
            return Redirect(returnUrl ?? "/Dashboard");
        }
        _tenantContext.SetCurrentTenant(tenantId);
        return Redirect(returnUrl ?? "/Dashboard");
    }
}
