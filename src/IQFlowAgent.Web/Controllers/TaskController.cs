using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Controllers;

[Authorize]
public class TaskController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IBlobStorageService _blobService;
    private readonly ILogger<TaskController> _logger;
    private readonly IWebHostEnvironment _env;

    public TaskController(ApplicationDbContext db, IBlobStorageService blobService,
        ILogger<TaskController> logger, IWebHostEnvironment env)
    {
        _db = db;
        _blobService = blobService;
        _logger = logger;
        _env = env;
    }

    private static string GenerateTaskId() =>
        "TSK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpper();

    // ── GET /Task/Index (optionally filtered by intakeRecordId) ──────────────
    public async Task<IActionResult> Index(int? intakeRecordId, string? status, string? priority)
    {
        var query = _db.IntakeTasks
            .Include(t => t.IntakeRecord)
            .Include(t => t.ActionLogs)
            .AsQueryable();

        if (intakeRecordId.HasValue)
            query = query.Where(t => t.IntakeRecordId == intakeRecordId.Value);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(priority))
            query = query.Where(t => t.Priority == priority);

        var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

        ViewBag.FilterIntakeId = intakeRecordId;
        ViewBag.FilterStatus = status;
        ViewBag.FilterPriority = priority;

        // For the intake name in breadcrumb if filtered
        if (intakeRecordId.HasValue)
        {
            var intake = await _db.IntakeRecords.FindAsync(intakeRecordId.Value);
            ViewBag.IntakeName = intake?.ProcessName;
            ViewBag.IntakeRef = intake?.IntakeId;
        }

        return View(tasks);
    }

    // ── GET /Task/Details/5 ──────────────────────────────────────────────────
    public async Task<IActionResult> Details(int id)
    {
        var task = await _db.IntakeTasks
            .Include(t => t.IntakeRecord)
            .Include(t => t.ActionLogs.OrderBy(l => l.CreatedAt))
            .Include(t => t.Documents)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task == null) return NotFound();
        return View(task);
    }

    // ── POST /Task/CreateFromAction ──────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFromAction(
        int intakeRecordId, string title, string description, string priority, string owner)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeRecordId);
        if (intake == null) return NotFound();

        // Guard: if a task with the same title already exists, redirect to it
        var existing = await _db.IntakeTasks
            .FirstOrDefaultAsync(t => t.IntakeRecordId == intakeRecordId && t.Title == title);
        if (existing != null)
        {
            TempData["Info"] = $"A task for '{title}' already exists.";
            return RedirectToAction(nameof(Details), new { id = existing.Id });
        }

        var now = DateTime.UtcNow;
        var resolvedOwner = string.IsNullOrWhiteSpace(owner)
            ? (intake.ProcessOwnerEmail ?? User.Identity?.Name ?? "Unassigned")
            : owner;

        var task = new IntakeTask
        {
            TaskId = GenerateTaskId(),
            IntakeRecordId = intakeRecordId,
            Title = title,
            Description = description,
            Priority = priority,
            Owner = resolvedOwner,
            Status = "Open",
            CreatedAt = now,
            DueDate = now.AddHours(48),
            CreatedByUserId = User.Identity?.Name
        };

        _db.IntakeTasks.Add(task);

        // Initial action log entry
        _db.TaskActionLogs.Add(new TaskActionLog
        {
            Task = task,
            ActionType = "StatusChange",
            OldStatus = null,
            NewStatus = "Open",
            Comment = $"Task created from AI analysis of intake {intake.IntakeId}.",
            CreatedAt = now,
            CreatedByUserId = User.Identity?.Name,
            CreatedByName = User.Identity?.Name
        });

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Task {task.TaskId} created successfully.";
        return RedirectToAction(nameof(Details), new { id = task.Id });
    }

    // ── POST /Task/UpdateStatus/5 ────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string newStatus, string? comment)
    {
        var task = await _db.IntakeTasks.FindAsync(id);
        if (task == null) return NotFound();

        var oldStatus = task.Status;
        task.Status = newStatus;
        if (newStatus is "Completed" or "Cancelled")
            task.CompletedAt = DateTime.UtcNow;

        _db.TaskActionLogs.Add(new TaskActionLog
        {
            IntakeTaskId = task.Id,
            ActionType = "StatusChange",
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Comment = comment,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = User.Identity?.Name,
            CreatedByName = User.Identity?.Name
        });

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Status updated to '{newStatus}'.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── POST /Task/AddComment/5 ──────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int id, string comment)
    {
        var task = await _db.IntakeTasks.FindAsync(id);
        if (task == null) return NotFound();

        if (string.IsNullOrWhiteSpace(comment))
        {
            TempData["Error"] = "Comment cannot be empty.";
            return RedirectToAction(nameof(Details), new { id });
        }

        _db.TaskActionLogs.Add(new TaskActionLog
        {
            IntakeTaskId = task.Id,
            ActionType = "Comment",
            Comment = comment,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = User.Identity?.Name,
            CreatedByName = User.Identity?.Name
        });

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── POST /Task/UploadArtifact/5 ──────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadArtifact(int id, IFormFile artifact)
    {
        var task = await _db.IntakeTasks.FindAsync(id);
        if (task == null) return NotFound();

        if (artifact == null || artifact.Length == 0)
        {
            TempData["Error"] = "Please select a file to upload.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var ext = Path.GetExtension(artifact.FileName);
        var uniqueSuffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        string filePath;

        if (_blobService.IsConfigured)
        {
            var blobName = $"{task.TaskId}-{uniqueSuffix}{ext}";
            using var stream = artifact.OpenReadStream();
            filePath = await _blobService.UploadAsync(stream, blobName, artifact.ContentType);
        }
        else
        {
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploadsDir);
            var safeFileName = $"{task.TaskId}-{uniqueSuffix}{ext}";
            var fullPath = Path.Combine(uploadsDir, safeFileName);
            using var stream = new FileStream(fullPath, FileMode.Create);
            await artifact.CopyToAsync(stream);
            filePath = $"/uploads/{safeFileName}";
        }

        _db.IntakeDocuments.Add(new IntakeDocument
        {
            IntakeRecordId = task.IntakeRecordId,
            IntakeTaskId = task.Id,
            FileName = artifact.FileName,
            FilePath = filePath,
            ContentType = artifact.ContentType,
            FileSize = artifact.Length,
            DocumentType = "TaskArtifact",
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = User.Identity?.Name,
            UploadedByName = User.Identity?.Name
        });

        _db.TaskActionLogs.Add(new TaskActionLog
        {
            IntakeTaskId = task.Id,
            ActionType = "ArtifactUploaded",
            Comment = $"Uploaded artifact: {artifact.FileName}",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = User.Identity?.Name,
            CreatedByName = User.Identity?.Name
        });

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Artifact '{artifact.FileName}' uploaded successfully.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
