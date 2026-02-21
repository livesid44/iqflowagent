using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using IQFlowAgent.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

[Authorize]
public class IntakeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAzureOpenAiService _aiService;
    private readonly IBlobStorageService _blobService;
    private readonly ILogger<IntakeController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly IServiceProvider _services;

    public IntakeController(ApplicationDbContext db, IAzureOpenAiService aiService,
        IBlobStorageService blobService, ILogger<IntakeController> logger,
        IWebHostEnvironment env, IServiceProvider services)
    {
        _db = db;
        _aiService = aiService;
        _blobService = blobService;
        _logger = logger;
        _env = env;
        _services = services;
    }

    // Supported plain-text file extensions that can be parsed for AI analysis
    private static readonly string[] ParseableExtensions = [".txt", ".csv", ".json", ".xml", ".md"];

    private static string GenerateIntakeId() =>
        "INT-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpper();
    public async Task<IActionResult> Index()
    {
        var intakes = await _db.IntakeRecords
            .OrderByDescending(x => x.CreatedAt)
            .Take(20)
            .ToListAsync();
        return View(intakes);
    }

    // GET /Intake/Chat — conversational chat-style intake
    public IActionResult Chat()
    {
        return View();
    }

    // GET /Intake/Create — traditional form intake (kept for fallback)
    public IActionResult Create()
    {
        return View(new IntakeViewModel());
    }

    // POST /Intake/Create — save intake + trigger analysis
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IntakeViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var intakeId = GenerateIntakeId();

        string? savedFilePath = null;
        string? savedFileName = null;
        string? savedContentType = null;
        long? savedFileSize = null;

        // Handle document upload
        if (model.Document != null && model.Document.Length > 0)
        {
            var ext = Path.GetExtension(model.Document.FileName);
            savedFileName = model.Document.FileName;
            savedContentType = model.Document.ContentType;
            savedFileSize = model.Document.Length;

            if (_blobService.IsConfigured)
            {
                // Upload to Azure Blob Storage
                var blobName = $"{intakeId}{ext}";
                using var stream = model.Document.OpenReadStream();
                savedFilePath = await _blobService.UploadAsync(stream, blobName, model.Document.ContentType);
                _logger.LogInformation("Uploaded document for {IntakeId} to blob: {BlobUrl}", intakeId, savedFilePath);
            }
            else
            {
                // Fallback: save to local wwwroot/uploads
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
                Directory.CreateDirectory(uploadsDir);
                var safeFileName = $"{intakeId}{ext}";
                var fullPath = Path.Combine(uploadsDir, safeFileName);

                using var stream = new FileStream(fullPath, FileMode.Create);
                await model.Document.CopyToAsync(stream);

                savedFilePath = $"/uploads/{safeFileName}";
                _logger.LogInformation(
                    "Azure Blob Storage not configured — saved document for {IntakeId} to local path: {Path}",
                    intakeId, savedFilePath);
            }
        }

        var record = new IntakeRecord
        {
            IntakeId = intakeId,
            ProcessName = model.ProcessName,
            Description = model.Description,
            BusinessUnit = model.BusinessUnit,
            Department = model.Department,
            ProcessOwnerName = model.ProcessOwnerName,
            ProcessOwnerEmail = model.ProcessOwnerEmail,
            ProcessType = model.ProcessType,
            EstimatedVolumePerDay = model.EstimatedVolumePerDay,
            Priority = model.Priority,
            Country = model.Country,
            City = model.City,
            SiteLocation = model.SiteLocation,
            TimeZone = model.TimeZone,
            UploadedFileName = savedFileName,
            UploadedFilePath = savedFilePath,
            UploadedFileContentType = savedContentType,
            UploadedFileSize = savedFileSize,
            Status = "Submitted",
            SubmittedAt = DateTime.UtcNow,
            CreatedByUserId = User.Identity?.Name
        };

        _db.IntakeRecords.Add(record);
        await _db.SaveChangesAsync();

        // Run analysis in background using a scoped service provider
        var recordId = record.Id;
        var webRoot = _env.WebRootPath;
        _ = Task.Run(async () => await RunAnalysisInBackgroundAsync(recordId, savedFilePath, webRoot));

        TempData["Success"] = $"Intake {intakeId} submitted successfully. Analysis is running…";
        return RedirectToAction(nameof(AnalysisResult), new { id = record.Id });
    }

    // GET /Intake/AnalysisResult/5 — show analysis result for a specific intake
    public async Task<IActionResult> AnalysisResult(int id)
    {
        var record = await _db.IntakeRecords.FindAsync(id);
        if (record == null) return NotFound();
        return View(record);
    }

    // POST /Intake/Reanalyze/5 — re-trigger analysis
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reanalyze(int id)
    {
        var record = await _db.IntakeRecords.FindAsync(id);
        if (record == null) return NotFound();

        record.Status = "Submitted";
        record.AnalysisResult = null;
        await _db.SaveChangesAsync();

        var webRoot = _env.WebRootPath;
        var filePath = record.UploadedFilePath;
        _ = Task.Run(async () => await RunAnalysisInBackgroundAsync(id, filePath, webRoot));

        TempData["Success"] = "Analysis re-triggered. Refresh in a moment.";
        return RedirectToAction(nameof(AnalysisResult), new { id });
    }

    private async Task RunAnalysisInBackgroundAsync(int intakeId, string? filePath, string webRoot)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ai = scope.ServiceProvider.GetRequiredService<IAzureOpenAiService>();
        var blob = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();

        try
        {
            var record = await db.IntakeRecords.FindAsync(intakeId);
            if (record == null) return;

            record.Status = "Analyzing";
            await db.SaveChangesAsync();

            // Try to read document text for AI analysis
            string? docText = null;
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                if (blob.IsConfigured && filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // Download from Azure Blob Storage
                    docText = await blob.DownloadTextAsync(filePath);
                }
                else
                {
                    // Read from local disk (fallback path)
                    var fullPath = Path.Combine(webRoot, filePath.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
                        if (ParseableExtensions.Contains(ext))
                            docText = await System.IO.File.ReadAllTextAsync(fullPath);
                    }
                }
            }

            var result = await ai.AnalyzeIntakeAsync(record, docText);
            record.AnalysisResult = result;
            record.Status = "Complete";
            record.AnalyzedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background analysis failed for intake id {IntakeId}", intakeId);
            try
            {
                using var scope2 = _services.CreateScope();
                var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var rec = await db2.IntakeRecords.FindAsync(intakeId);
                if (rec != null) { rec.Status = "Error"; await db2.SaveChangesAsync(); }
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to update error status for intake id {IntakeId}", intakeId);
            }
        }
    }
}
