using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Hubs;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database provider selection: SQL Server if configured, otherwise SQLite fallback
var sqlServerCs = builder.Configuration.GetConnectionString("SqlServer") ?? string.Empty;
var sqliteCs    = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=iqflowagent.db";
var useSqlServer = !string.IsNullOrWhiteSpace(sqlServerCs)
    && !sqlServerCs.Contains("YOUR_SQL_SERVER", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (useSqlServer)
        options.UseSqlServer(sqlServerCs);
    else
        options.UseSqlite(sqliteCs);
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/Login";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.AddScoped<IAuthSettingsService, AuthSettingsService>();
builder.Services.AddScoped<ILdapAuthService, LdapAuthService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IAzureOpenAiService, AzureOpenAiService>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IDocxReportService, DocxReportService>();
builder.Services.AddScoped<IAzureSpeechService, AzureSpeechService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContextService, TenantContextService>();

// RAG background processing
builder.Services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
builder.Services.AddHostedService<RagProcessorService>();

// SignalR for real-time notifications
builder.Services.AddSignalR();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
