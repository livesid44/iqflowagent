using System.Text.Json;
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
    private readonly ITenantContextService _tenantContext;

    public IntakeController(ApplicationDbContext db, IAzureOpenAiService aiService,
        IBlobStorageService blobService, ILogger<IntakeController> logger,
        IWebHostEnvironment env, IServiceProvider services, ITenantContextService tenantContext)
    {
        _db = db;
        _aiService = aiService;
        _blobService = blobService;
        _logger = logger;
        _env = env;
        _services = services;
        _tenantContext = tenantContext;
    }

    // Supported plain-text file extensions that can be parsed for AI analysis
    private static readonly string[] ParseableExtensions = [".txt", ".csv", ".json", ".xml", ".md"];

    private static string GenerateIntakeId() =>
        "INT-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpper();
    public async Task<IActionResult> Index()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var intakes = await _db.IntakeRecords
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(20)
            .ToListAsync();
        return View(intakes);
    }

    // GET /Intake/Chat — conversational chat-style intake
    public async Task<IActionResult> Chat()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        ViewBag.Departments = await _db.MasterDepartments
            .Where(d => d.IsActive && d.TenantId == tenantId)
            .OrderBy(d => d.Name)
            .Select(d => d.Name)
            .ToListAsync();
        ViewBag.LobsByDept = await _db.MasterLobs
            .Where(l => l.IsActive && l.TenantId == tenantId)
            .OrderBy(l => l.DepartmentName).ThenBy(l => l.Name)
            .Select(l => new { l.DepartmentName, l.Name })
            .ToListAsync();
        return View();
    }

    // GET /Intake/Create — traditional form intake (kept for fallback)
    public async Task<IActionResult> Create()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        ViewBag.Departments = await _db.MasterDepartments
            .Where(d => d.IsActive && d.TenantId == tenantId)
            .OrderBy(d => d.Name)
            .Select(d => d.Name)
            .ToListAsync();
        ViewBag.LobsByDept = await _db.MasterLobs
            .Where(l => l.IsActive && l.TenantId == tenantId)
            .OrderBy(l => l.DepartmentName).ThenBy(l => l.Name)
            .Select(l => new { l.DepartmentName, l.Name })
            .ToListAsync();
        return View(new IntakeViewModel());
    }

    // POST /Intake/Create — save intake + trigger analysis
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IntakeViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var tenantId = _tenantContext.GetCurrentTenantId();
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

            if (await _blobService.IsConfiguredAsync())
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
            TenantId = tenantId,
            IntakeId = intakeId,
            ProcessName = model.ProcessName,
            Description = model.Description,
            BusinessUnit = model.BusinessUnit,
            Department = model.Department,
            Lob = model.Lob,
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

        // Pass existing task titles so the view can show "Already Created" badges
        var existingTitles = await _db.IntakeTasks
            .Where(t => t.IntakeRecordId == id)
            .Select(t => t.Title)
            .ToListAsync();
        ViewBag.ExistingTaskTitles = existingTitles;
        ViewBag.TaskCount = existingTitles.Count;

        return View(record);
    }

    // GET /Intake/Edit/5 — edit form (blocked for Closed intakes)
    public async Task<IActionResult> Edit(int id)
    {
        var record = await _db.IntakeRecords.FindAsync(id);
        if (record == null) return NotFound();

        if (record.Status == "Closed")
        {
            TempData["Error"] = "Closed intakes cannot be edited.";
            return RedirectToAction(nameof(AnalysisResult), new { id });
        }

        // Only the creator, or Admin/SuperAdmin roles, may edit
        var currentUser = User.Identity?.Name;
        bool isAdmin = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");
        if (!isAdmin && record.CreatedByUserId != currentUser)
        {
            TempData["Error"] = "You do not have permission to edit this intake.";
            return RedirectToAction(nameof(AnalysisResult), new { id });
        }

        var taskCount = await _db.IntakeTasks.CountAsync(t => t.IntakeRecordId == id);
        var tenantId = _tenantContext.GetCurrentTenantId();
        ViewBag.Departments = await _db.MasterDepartments
            .Where(d => d.IsActive && d.TenantId == tenantId)
            .OrderBy(d => d.Name)
            .Select(d => d.Name)
            .ToListAsync();
        ViewBag.LobsByDept = await _db.MasterLobs
            .Where(l => l.IsActive && l.TenantId == tenantId)
            .OrderBy(l => l.DepartmentName).ThenBy(l => l.Name)
            .Select(l => new { l.DepartmentName, l.Name })
            .ToListAsync();

        var vm = new IntakeEditViewModel
        {
            Id                   = record.Id,
            IntakeId             = record.IntakeId,
            ProcessName          = record.ProcessName,
            Description          = record.Description,
            BusinessUnit         = record.BusinessUnit,
            Department           = record.Department,
            Lob                  = record.Lob,
            ProcessOwnerName     = record.ProcessOwnerName,
            ProcessOwnerEmail    = record.ProcessOwnerEmail,
            ProcessType          = record.ProcessType,
            EstimatedVolumePerDay = record.EstimatedVolumePerDay,
            Priority             = record.Priority,
            Country              = record.Country,
            City                 = record.City,
            SiteLocation         = record.SiteLocation,
            TimeZone             = record.TimeZone,
            CurrentFileName      = record.UploadedFileName,
            CurrentFilePath      = record.UploadedFilePath,
            RerunAnalysis        = true,
            TaskCount            = taskCount,
        };
        return View(vm);
    }

    // POST /Intake/Edit/5 — save edits (blocked for Closed intakes)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, IntakeEditViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var record = await _db.IntakeRecords.FindAsync(id);
        if (record == null) return NotFound();

        if (record.Status == "Closed")
        {
            TempData["Error"] = "Closed intakes cannot be edited.";
            return RedirectToAction(nameof(AnalysisResult), new { id });
        }

        var currentUser = User.Identity?.Name;
        bool isAdmin = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");
        if (!isAdmin && record.CreatedByUserId != currentUser)
        {
            TempData["Error"] = "You do not have permission to edit this intake.";
            return RedirectToAction(nameof(AnalysisResult), new { id });
        }

        // ── Update meta + location ────────────────────────────────────
        record.ProcessName          = model.ProcessName;
        record.Description          = model.Description;
        record.BusinessUnit         = model.BusinessUnit;
        record.Department           = model.Department;
        record.Lob                  = model.Lob;
        record.ProcessOwnerName     = model.ProcessOwnerName;
        record.ProcessOwnerEmail    = model.ProcessOwnerEmail;
        record.ProcessType          = model.ProcessType;
        record.EstimatedVolumePerDay = model.EstimatedVolumePerDay;
        record.Priority             = model.Priority;
        record.Country              = model.Country;
        record.City                 = model.City;
        record.SiteLocation         = model.SiteLocation;
        record.TimeZone             = model.TimeZone;

        // ── Document replacement ──────────────────────────────────────
        string? newFilePath = record.UploadedFilePath;
        if (model.ReplaceDocument)
        {
            // Delete old document
            await DeleteDocumentAsync(record.UploadedFilePath);

            if (model.NewDocument != null && model.NewDocument.Length > 0)
            {
                var ext = Path.GetExtension(model.NewDocument.FileName);
                var blobName = $"{record.IntakeId}-v{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";

                if (await _blobService.IsConfiguredAsync())
                {
                    using var stream = model.NewDocument.OpenReadStream();
                    newFilePath = await _blobService.UploadAsync(stream, blobName, model.NewDocument.ContentType);
                    _logger.LogInformation("Replaced document for {IntakeId} to blob: {BlobUrl}", record.IntakeId, newFilePath);
                }
                else
                {
                    var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
                    Directory.CreateDirectory(uploadsDir);
                    var fullPath = Path.Combine(uploadsDir, blobName);
                    using var stream = new FileStream(fullPath, FileMode.Create);
                    await model.NewDocument.CopyToAsync(stream);
                    newFilePath = $"/uploads/{blobName}";
                }

                record.UploadedFileName        = model.NewDocument.FileName;
                record.UploadedFilePath        = newFilePath;
                record.UploadedFileContentType = model.NewDocument.ContentType;
                record.UploadedFileSize        = model.NewDocument.Length;
            }
            else
            {
                // Cleared without replacement
                record.UploadedFileName        = null;
                record.UploadedFilePath        = null;
                record.UploadedFileContentType = null;
                record.UploadedFileSize        = null;
                newFilePath = null;
            }
        }

        await _db.SaveChangesAsync();

        // ── Optionally re-run analysis ────────────────────────────────
        if (model.RerunAnalysis)
        {
            record.Status         = "Submitted";
            record.AnalysisResult = null;
            record.AnalyzedAt     = null;
            await _db.SaveChangesAsync();

            var webRoot = _env.WebRootPath;
            var recordId = record.Id;
            _ = Task.Run(async () => await RunAnalysisInBackgroundAsync(recordId, newFilePath, webRoot));

            TempData["Success"] = $"Intake {record.IntakeId} updated. AI analysis is running…";
        }
        else
        {
            TempData["Success"] = $"Intake {record.IntakeId} updated successfully.";
        }

        return RedirectToAction(nameof(AnalysisResult), new { id });
    }

    // GET /Intake/LobsByDepartment?deptName=Finance — AJAX endpoint for cascading LOB dropdown
    public async Task<IActionResult> LobsByDepartment(string deptName)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var lobs = await _db.MasterLobs
            .Where(l => l.IsActive && l.TenantId == tenantId && l.DepartmentName == deptName)
            .OrderBy(l => l.Name)
            .Select(l => l.Name)
            .ToListAsync();
        return Json(lobs);
    }

    private async Task DeleteDocumentAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        try
        {
            if (await _blobService.IsConfiguredAsync() && filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                await _blobService.DeleteAsync(filePath);
            else
            {
                var fullPath = Path.Combine(_env.WebRootPath, filePath.TrimStart('/'));
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete old document at {FilePath}", filePath);
        }
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
                if (await blob.IsConfiguredAsync() && filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
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

            // Auto-create tasks from action items
            await AutoCreateTasksAsync(db, record, result);
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

    private async Task AutoCreateTasksAsync(ApplicationDbContext db, IntakeRecord record, string analysisJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(analysisJson);
            var root = doc.RootElement;

            var now = DateTime.UtcNow;
            var owner = string.IsNullOrWhiteSpace(record.ProcessOwnerEmail)
                ? (record.CreatedByUserId ?? "Unassigned")
                : record.ProcessOwnerEmail;

            // ── Action Items ────────────────────────────────────────────────────
            if (root.TryGetProperty("actionItems", out var actionItems))
            {
                foreach (var item in actionItems.EnumerateArray())
                {
                    var title       = item.TryGetProperty("title",       out var t) ? t.GetString() ?? "" : "";
                    var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    var priority    = item.TryGetProperty("priority",    out var p) ? p.GetString() ?? "Medium" : "Medium";

                    if (string.IsNullOrWhiteSpace(title)) continue;

                    if (await db.IntakeTasks.AnyAsync(tk => tk.IntakeRecordId == record.Id && tk.Title == title))
                        continue;

                    AddTask(db, record, title, description, priority, owner, now,
                        $"Task automatically created from AI analysis of intake {record.IntakeId}.");
                }
            }

            // ── Pending Check Points (Fail / Warning) ────────────────────────
            if (root.TryGetProperty("checkPoints", out var checkPoints))
            {
                foreach (var cp in checkPoints.EnumerateArray())
                {
                    var cpStatus = cp.TryGetProperty("status", out var cs) ? cs.GetString() ?? "" : "";
                    if (cpStatus != "Fail" && cpStatus != "Warning") continue;

                    var cpLabel = cp.TryGetProperty("label", out var cl) ? cl.GetString() ?? "" : "";
                    var cpNote  = cp.TryGetProperty("note",  out var cn) ? cn.GetString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(cpLabel)) continue;

                    var title       = $"[Checkpoint] {cpLabel}";
                    var description = string.IsNullOrWhiteSpace(cpNote) ? cpLabel : cpNote;
                    var priority    = cpStatus == "Fail" ? "High" : "Medium";

                    if (await db.IntakeTasks.AnyAsync(tk => tk.IntakeRecordId == record.Id && tk.Title == title))
                        continue;

                    AddTask(db, record, title, description, priority, owner, now,
                        $"Task automatically created from pending checkpoint '{cpLabel}' (status: {cpStatus}) for intake {record.IntakeId}.");
                }
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Auto-created tasks from analysis for intake {IntakeId}", record.IntakeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-create tasks from analysis for intake {IntakeId}", record.IntakeId);
        }
    }

    private static void AddTask(ApplicationDbContext db, IntakeRecord record,
        string title, string description, string priority, string owner,
        DateTime now, string logComment)
    {
        var task = new IntakeTask
        {
            TaskId          = $"TSK-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            IntakeRecordId  = record.Id,
            Title           = title,
            Description     = description,
            Priority        = priority,
            Owner           = owner,
            Status          = "Open",
            CreatedAt       = now,
            DueDate         = now.AddHours(48),
            CreatedByUserId = record.CreatedByUserId
        };
        db.IntakeTasks.Add(task);

        db.TaskActionLogs.Add(new TaskActionLog
        {
            Task            = task,
            ActionType      = "StatusChange",
            OldStatus       = null,
            NewStatus       = "Open",
            Comment         = logComment,
            CreatedAt       = now,
            CreatedByUserId = record.CreatedByUserId,
            CreatedByName   = record.CreatedByUserId
        });
    }
}
