using IQFlowAgent.Web.Models;
using Microsoft.AspNetCore.Identity;

namespace IQFlowAgent.Web.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();

        string[] roles = { "SuperAdmin", "Admin", "User" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        if (await userManager.FindByNameAsync("admin") == null)
        {
            var admin = new ApplicationUser
            {
                UserName = "admin",
                Email = "admin@iqflowagent.com",
                FullName = "Super Administrator",
                IsActive = true,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(admin, "Admin@123!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, "SuperAdmin");
        }

        if (!db.AuthSettings.Any())
        {
            db.AuthSettings.Add(new AuthSettings());
            await db.SaveChangesAsync();
        }

        // Seed default "Orange" tenant (Id=1)
        if (!db.Tenants.Any())
        {
            db.Tenants.Add(new Tenant
            {
                Id = 1,
                Name = "Orange",
                Slug = "orange",
                Color = "#FF6B35",
                Description = "Default tenant - Orange project",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Assign admin user to Orange tenant
        var adminUser = await userManager.FindByNameAsync("admin");
        if (adminUser != null && !db.UserTenants.Any(ut => ut.UserId == adminUser.Id))
        {
            db.UserTenants.Add(new UserTenant
            {
                UserId = adminUser.Id,
                TenantId = 1,
                TenantRole = "Admin",
                IsDefault = true
            });
            await db.SaveChangesAsync();
        }

        // Seed empty TenantAiSettings for Orange tenant
        if (!db.TenantAiSettings.Any(s => s.TenantId == 1))
        {
            db.TenantAiSettings.Add(new TenantAiSettings { TenantId = 1 });
            await db.SaveChangesAsync();
        }

        if (!db.MasterDepartments.Any())
        {
            var departments = new[]
            {
                "Finance", "Human Resources", "Information Technology", "Operations",
                "Customer Service", "Sales", "Marketing", "Legal & Compliance",
                "Supply Chain", "Risk Management"
            };
            foreach (var dept in departments)
                db.MasterDepartments.Add(new MasterDepartment { Name = dept, TenantId = 1 });
            await db.SaveChangesAsync();
        }
    }
}
