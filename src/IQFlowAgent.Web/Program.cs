using IQFlowAgent.Web;
using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Hubs;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database provider selection: SQL Server if configured, otherwise SQLite fallback
var sqlServerCs = builder.Configuration.GetConnectionString("SqlServer") ?? string.Empty;
var rawSqliteCs = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=App_Data/iqflowagent.db";
var useSqlServer = !string.IsNullOrWhiteSpace(sqlServerCs)
    && !sqlServerCs.Contains("YOUR_SQL_SERVER", StringComparison.OrdinalIgnoreCase);

// Resolve a relative SQLite "Data Source" path against ContentRootPath so the database
// file is always placed in the same directory regardless of the process working directory.
// This prevents the file from being recreated empty when IIS changes the working directory,
// and makes the path predictable across development, staging, and production.
var sqliteCs = rawSqliteCs;
if (!useSqlServer && rawSqliteCs.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
{
    var dataSource = rawSqliteCs["Data Source=".Length..].Trim();
    if (!Path.IsPathRooted(dataSource) && !dataSource.StartsWith("|", StringComparison.Ordinal))
    {
        // The pipe prefix (e.g. |DataDirectory|) is a special SQLite macro — skip those.
        dataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, dataSource));
        var dir = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        sqliteCs = $"Data Source={dataSource}";
    }
}

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
builder.Services.AddScoped<IPiiScanService, PiiScanService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContextService, TenantContextService>();

// RAG background processing
builder.Services.AddSingleton<IBackgroundJobQueue, BackgroundJobQueue>();
builder.Services.AddHostedService<RagProcessorService>();

// By default (.NET 6+) an unhandled exception inside any BackgroundService causes the
// generic host to call IHostApplicationLifetime.StopApplication().  On IIS in-process
// hosting this produces the dreaded "503 (Application Shutting Down)" response for
// every subsequent request — the app appears to start, handles a few requests, and
// then goes dark.  Setting the behavior to Ignore prevents a single background-job
// crash from taking down the entire web process.
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior =
        BackgroundServiceExceptionBehavior.Ignore;
});

// SignalR for real-time notifications
builder.Services.AddSignalR();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddControllersWithViews();

// Allow file uploads up to 250 MB (intake documents, audio/video recordings).
// Must be set on both Kestrel and the MVC form-options layer.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = AppConstants.MaxUploadBytes;
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(formOptions =>
{
    formOptions.MultipartBodyLengthLimit = AppConstants.MaxUploadBytes;
    formOptions.ValueLengthLimit         = int.MaxValue;
    formOptions.MultipartHeadersLengthLimit = int.MaxValue;
});

var app = builder.Build();

// Create the IIS stdout-log directory so it exists before the web.config
// stdoutLogEnabled="true" switch is flipped.  Failure here is non-fatal.
try { Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "logs")); }
catch (Exception ex) { Console.Error.WriteLine($"[Startup] Could not create logs directory: {ex.GetType().Name}: {ex.Message}"); }

// Apply any pending EF migrations and seed initial data.
// Wrapped in a try-catch so that a transient database error (e.g. connection refused,
// a migration conflict on the first deploy of a new schema) does NOT crash the
// ASP.NET Core in-process host.  On IIS an unhandled startup exception kills the
// w3wp worker, triggering rapid-fail protection and returning HTTP 503 to every
// visitor.  By catching here we keep the process alive, log the error clearly, and
// let the application serve an informative error page instead of a blank 503.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}
catch (Exception ex)
{
    // Use the framework logger so the message appears in both the ASP.NET Core
    // log pipeline AND the IIS stdout log (when stdoutLogEnabled="true").
    var startupLog = app.Services.GetRequiredService<ILogger<Program>>();
    startupLog.LogCritical(ex,
        "Startup: database migration or seeding failed. " +
        "The application will continue but some features may be unavailable. " +
        "Verify the connection string and database state, then restart the application.");
    // Do NOT re-throw — re-throwing terminates the process and causes IIS 503.
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSession();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
