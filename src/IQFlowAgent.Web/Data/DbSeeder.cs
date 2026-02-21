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
    }
}
