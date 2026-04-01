using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IQFlowAgent.Web.Controllers;

[Authorize]
public class TaskController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IBlobStorageService _blobService;
    private readonly IAzureOpenAiService _aiService;
    private readonly IDocumentIntelligenceService _docIntel;
    private readonly ILogger<TaskController> _logger;
    private readonly IWebHostEnvironment _env;

    public TaskController(ApplicationDbContext db, IBlobStorageService blobService,
        IAzureOpenAiService aiService,
        IDocumentIntelligenceService docIntel,
        ILogger<TaskController> logger, IWebHostEnvironment env)
    {
        _db = db;
        _blobService = blobService;
        _aiService = aiService;
        _docIntel = docIntel;
        _logger = logger;
        _env = env;
    }

    private static string GenerateTaskId() =>
        "TSK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpper();

    // ── GET /Task/Index (intake selector + optional status/priority filters) ──
    public async Task<IActionResult> Index(
        int? selectedId, int? intakeRecordId,
        string? status, string? priority,
        string? search, string? country, string? businessUnit, string? processType)
    {
        // selectedId takes precedence over the legacy intakeRecordId param
        var resolvedIntakeId = selectedId ?? intakeRecordId;

        var allIntakes = await _db.IntakeRecords
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        // ── Populate picker ViewBag ─────────────────────────────────────────
        var filtered = allIntakes.Where(x =>
            (string.IsNullOrWhiteSpace(search) ||
             x.IntakeId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.BusinessUnit.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.Department.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(country) || x.Country == country)
            && (string.IsNullOrWhiteSpace(businessUnit) || x.BusinessUnit == businessUnit)
            && (string.IsNullOrWhiteSpace(processType) || x.ProcessType == processType)
        ).ToList();

        ViewBag.IntakePickerIntakes      = filtered;
        ViewBag.IntakePickerSelected     = resolvedIntakeId;
        ViewBag.IntakePickerSearch       = search;
        ViewBag.IntakePickerCountry      = country;
        ViewBag.IntakePickerBusinessUnit = businessUnit;
        ViewBag.IntakePickerProcessType  = processType;
        ViewBag.IntakePickerController   = "Task";
        ViewBag.IntakePickerAction       = "Index";
        ViewBag.Countries     = allIntakes.Select(x => x.Country)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        ViewBag.BusinessUnits = allIntakes.Select(x => x.BusinessUnit)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        ViewBag.ProcessTypes  = allIntakes.Select(x => x.ProcessType)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();

        // ── Load tasks only when an intake is selected ──────────────────────
        List<IntakeTask> tasks = new();
        if (resolvedIntakeId.HasValue)
        {
            var query = _db.IntakeTasks
                .Include(t => t.IntakeRecord)
                .Include(t => t.ActionLogs)
                .Where(t => t.IntakeRecordId == resolvedIntakeId.Value)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(t => t.Status == status);
            if (!string.IsNullOrWhiteSpace(priority))
                query = query.Where(t => t.Priority == priority);

            tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

            var intake = allIntakes.FirstOrDefault(x => x.Id == resolvedIntakeId.Value);
            ViewBag.IntakeName = intake?.ProcessName;
            ViewBag.IntakeRef  = intake?.IntakeId;
        }

        ViewBag.FilterIntakeId = resolvedIntakeId;
        ViewBag.FilterStatus   = status;
        ViewBag.FilterPriority = priority;

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
        int intakeRecordId, string title, string description, string priority, string owner,
        string? bartokSectionName = null)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeRecordId);
        if (intake == null) return NotFound();

        // Reject sentinel titles used by the "Create All" batch buttons; they must never
        // result in a real task even if the client-side filtering somehow lets them through.
        if (string.IsNullOrWhiteSpace(title) || title == "[BATCH]" || title == "[BATCH-CP]")
            return BadRequest("Invalid task title.");

        // Guard: if a task with the same title already exists, redirect to it.
        // For checkpoint tasks (title starts with "[Checkpoint]"), also check
        // whether a task for the same BARTOK section already exists under the
        // new [Checkpoint][sectionId] format or the legacy [Checkpoint] format
        // so that the user can never accidentally create two tasks for the same section.
        IntakeTask? existing;
        if (title.StartsWith("[Checkpoint]", StringComparison.OrdinalIgnoreCase))
        {
            // Extract the bare checkpoint label: strip "[Checkpoint][...] " or "[Checkpoint] " prefix.
            var bareLabel = System.Text.RegularExpressions.Regex.Replace(
                title, @"^\[Checkpoint\](\[[^\]]*\])?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            existing = await _db.IntakeTasks
                .FirstOrDefaultAsync(t =>
                    t.IntakeRecordId == intakeRecordId
                    && (t.Title == title
                        || t.Title.StartsWith($"[Checkpoint] {bareLabel}", StringComparison.OrdinalIgnoreCase)
                        || System.Text.RegularExpressions.Regex.IsMatch(
                            t.Title,
                            $@"^\[Checkpoint\]\[[^\]]+\]\s*{System.Text.RegularExpressions.Regex.Escape(bareLabel)}",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
        }
        else
        {
            existing = await _db.IntakeTasks
                .FirstOrDefaultAsync(t => t.IntakeRecordId == intakeRecordId && t.Title == title);
        }
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
            CreatedByUserId = User.Identity?.Name,
            BartokSectionName = string.IsNullOrWhiteSpace(bartokSectionName) ? null : bartokSectionName
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
    [RequestSizeLimit(AppConstants.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AppConstants.MaxUploadBytes)]
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
        var safeFileName = $"{task.TaskId}-{uniqueSuffix}{ext}";
        string filePath;

        if (await _blobService.IsConfiguredAsync())
        {
            // Resolve intake to get tenantId and intakeId for folder structure
            var intakeRecord = await _db.IntakeRecords.FindAsync(task.IntakeRecordId);
            if (intakeRecord != null)
            {
                var folderPath = $"{intakeRecord.TenantId}/{intakeRecord.IntakeId}/tasks";
                using var stream = artifact.OpenReadStream();
                filePath = await _blobService.UploadToFolderAsync(stream, folderPath, safeFileName, artifact.ContentType);
            }
            else
            {
                using var stream = artifact.OpenReadStream();
                filePath = await _blobService.UploadAsync(stream, safeFileName, artifact.ContentType);
            }
        }
        else
        {
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploadsDir);
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

    // ── POST /Task/AnalyzeForTask/{id} ───────────────────────────────────────
    // Re-analyzes the uploaded artifacts for a specific task using the task's
    // BARTOK section keywords.  If the uploaded document does not contain relevant
    // content for the section, the task is immediately reopened so the user knows
    // more information is required.  If it does contain content the task keeps its
    // current status and a confirmation comment is added.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalyzeForTask(int id)
    {
        var task = await _db.IntakeTasks
            .Include(t => t.Documents)
            .Include(t => t.ActionLogs)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return NotFound();

        var sectionName = task.BartokSectionName;
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            TempData["Error"] = "This task is not linked to a specific BARTOK section and cannot be re-analysed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ── Aggregate artifact text from files uploaded to this task ──────────
        var artifactText = await AggregateArtifactTextAsync(new List<IntakeTask> { task });

        if (string.IsNullOrWhiteSpace(artifactText))
        {
            TempData["Error"] = "No artifact content found. Please upload a document before running analysis.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ── Keyword-based RAG scoring for the section ─────────────────────────
        // Chunk the artifact and score it against the section's keywords.
        var chunks    = RagDocumentChunker.Chunk(artifactText);
        var topChunks = RagDocumentChunker.GetTopChunksForSection(sectionName, chunks, topK: 1);
        var hasContent = !string.IsNullOrWhiteSpace(topChunks);

        var now = DateTime.UtcNow;

        if (hasContent)
        {
            // Content found — log a pass comment, keep current status.
            _db.TaskActionLogs.Add(new TaskActionLog
            {
                IntakeTaskId    = task.Id,
                ActionType      = "Comment",
                Comment         = $"✅ Document analysis passed for section '{sectionName}': relevant content found in the uploaded artifact. No action required.",
                CreatedAt       = now,
                CreatedByUserId = User.Identity?.Name,
                CreatedByName   = User.Identity?.Name
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Analysis passed — relevant content found for '{sectionName}'.";
        }
        else
        {
            // No relevant content — reopen the task so the user must provide a better document.
            var oldStatus = task.Status;
            task.Status      = "Open";
            task.CompletedAt = null;

            _db.TaskActionLogs.Add(new TaskActionLog
            {
                IntakeTaskId    = task.Id,
                ActionType      = "StatusChange",
                OldStatus       = oldStatus,
                NewStatus       = "Open",
                Comment         = $"⚠️ Document analysis failed for section '{sectionName}': the uploaded artifact does not contain sufficient content for this section. Task has been reopened — please upload a document that covers this section.",
                CreatedAt       = now,
                CreatedByUserId = User.Identity?.Name,
                CreatedByName   = User.Identity?.Name
            });

            await _db.SaveChangesAsync();
            TempData["Error"] = $"Analysis failed — no relevant content found for '{sectionName}'. Task has been reopened.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // ── POST /Task/MarkNotApplicable/{id} ────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotApplicable(int id, string? naReason)
    {
        var task = await _db.IntakeTasks.FindAsync(id);
        if (task == null) return NotFound();

        // A comment explaining why the task is N/A is mandatory.
        if (string.IsNullOrWhiteSpace(naReason))
        {
            TempData["Error"] = "A reason is required when marking a task as Not Applicable.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var oldStatus = task.Status;
        task.IsNotApplicable = true;
        task.NaReason        = naReason.Trim();

        // Auto-close: N/A tasks are considered closed immediately.
        task.Status      = "Cancelled";
        task.CompletedAt = DateTime.UtcNow;

        _db.TaskActionLogs.Add(new TaskActionLog
        {
            IntakeTaskId    = task.Id,
            ActionType      = "StatusChange",
            OldStatus       = oldStatus,
            NewStatus       = "Cancelled",
            Comment         = $"Task marked as Not Applicable (auto-closed). Reason: {naReason.Trim()}",
            CreatedAt       = DateTime.UtcNow,
            CreatedByUserId = User.Identity?.Name,
            CreatedByName   = User.Identity?.Name
        });

        await _db.SaveChangesAsync();
        TempData["Success"] = "Task marked as Not Applicable and closed — it will be bypassed in AI closure verification.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── POST /Task/UndoNotApplicable/{id} ─────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoNotApplicable(int id)
    {
        var task = await _db.IntakeTasks.FindAsync(id);
        if (task == null) return NotFound();

        task.IsNotApplicable = false;
        task.NaReason = null;

        _db.TaskActionLogs.Add(new TaskActionLog
        {
            IntakeTaskId    = task.Id,
            ActionType      = "StatusChange",
            OldStatus       = task.Status,
            NewStatus       = task.Status,
            Comment         = "Not Applicable flag removed — task will be included in AI closure verification.",
            CreatedAt       = DateTime.UtcNow,
            CreatedByUserId = User.Identity?.Name,
            CreatedByName   = User.Identity?.Name
        });

        await _db.SaveChangesAsync();
        TempData["Success"] = "Not Applicable flag removed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── POST /Task/VerifyAndClose/{intakeId} ─────────────────────────────────
    // Only callable when all tasks are Completed, Cancelled, or Not Applicable.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyAndClose(int intakeId)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeId);
        if (intake == null) return NotFound();

        // Load all tasks with action logs and documents
        var tasks = await _db.IntakeTasks
            .Include(t => t.ActionLogs)
            .Include(t => t.Documents)
            .Where(t => t.IntakeRecordId == intakeId)
            .ToListAsync();

        if (tasks.Count == 0)
        {
            TempData["Error"] = "No tasks found for this intake.";
            return RedirectToAction(nameof(Index), new { selectedId = intakeId });
        }

        // Guard: all tasks must be Completed, Cancelled, or Not Applicable
        var openTasks = tasks.Where(t => t.Status is "Open" or "In Progress" && !t.IsNotApplicable).ToList();
        if (openTasks.Count > 0)
        {
            TempData["Error"] = $"{openTasks.Count} task(s) are still open. Close or mark as N/A before verifying intake closure.";
            return RedirectToAction(nameof(Index), new { selectedId = intakeId });
        }

        // Separate N/A tasks (they are automatically verified/bypassed)
        var naTasks     = tasks.Where(t => t.IsNotApplicable).ToList();
        var activeTasks = tasks.Where(t => !t.IsNotApplicable).ToList();

        // Aggregate text from text-based task artifacts for AI context
        var artifactText = await AggregateArtifactTextAsync(activeTasks);

        // Call AI to verify closure (only active tasks, not NA ones)
        var verificationJson = await _aiService.VerifyIntakeClosureAsync(intake, activeTasks, artifactText);

        // Inject NA tasks into the JSON as auto-passed before saving/displaying
        verificationJson = InjectNaTasksIntoJson(verificationJson, naTasks);

        // Parse result and act on it
        var now = DateTime.UtcNow;
        var reopened = new List<IntakeTask>();
        bool canClose = false;

        try
        {
            using var doc = JsonDocument.Parse(verificationJson);
            var root = doc.RootElement;
            canClose = root.TryGetProperty("canCloseIntake", out var cc) && cc.GetBoolean();

            if (root.TryGetProperty("taskVerifications", out var tvs))
            {
                foreach (var tv in tvs.EnumerateArray())
                {
                    var tvTaskId   = tv.TryGetProperty("taskId",   out var tid) ? tid.GetString() : null;
                    var tvCanClose = tv.TryGetProperty("canClose",  out var tc)  && tc.GetBoolean();
                    var tvReason   = tv.TryGetProperty("reason",    out var tr)  ? tr.GetString() : "";
                    var tvMissing  = tv.TryGetProperty("missingEvidence", out var tm)
                        ? tm.EnumerateArray().Select(m => m.GetString()).Where(m => m != null).ToList()
                        : new List<string?>();

                    if (tvCanClose || string.IsNullOrWhiteSpace(tvTaskId)) continue;

                    // Reopen this task
                    var task = tasks.FirstOrDefault(t => t.TaskId == tvTaskId);
                    if (task == null) continue;

                    var oldStatus = task.Status;
                    task.Status = "Open";
                    task.CompletedAt = null;
                    reopened.Add(task);

                    var missingList = string.Join("; ", tvMissing);
                    var commentPrefix = $"Task reopened by AI closure verification: {tvReason}";
                    var comment = string.IsNullOrWhiteSpace(missingList)
                        ? commentPrefix
                        : $"{commentPrefix} Missing: {missingList}";

                    _db.TaskActionLogs.Add(new TaskActionLog
                    {
                        IntakeTaskId    = task.Id,
                        ActionType      = "StatusChange",
                        OldStatus       = oldStatus,
                        NewStatus       = "Open",
                        Comment         = comment,
                        CreatedAt       = now,
                        CreatedByUserId = User.Identity?.Name,
                        CreatedByName   = User.Identity?.Name
                    });
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse closure verification JSON for intake {IntakeId}", intake.IntakeId);
            TempData["Error"] = "AI verification returned an unexpected response. Please try again.";
            return RedirectToAction(nameof(Index), new { selectedId = intakeId });
        }

        // Update intake status
        if (canClose && reopened.Count == 0)
        {
            intake.Status = "Closed";
            _logger.LogInformation("Intake {IntakeId} verified and closed.", intake.IntakeId);
        }

        await _db.SaveChangesAsync();

        // Pass verification result to the result view via TempData
        TempData["VerificationJson"] = verificationJson;
        TempData["ReopenedCount"]    = reopened.Count;
        TempData["CanClose"]         = canClose && reopened.Count == 0;

        return RedirectToAction(nameof(VerificationResult), new { intakeId });
    }

    // ── GET /Task/VerificationResult ─────────────────────────────────────────
    public IActionResult VerificationResult(int intakeId)
    {
        ViewBag.IntakeId         = intakeId;
        ViewBag.VerificationJson = TempData["VerificationJson"] as string;
        ViewBag.ReopenedCount    = TempData["ReopenedCount"] is int rc ? rc : 0;
        ViewBag.CanClose         = TempData["CanClose"] is bool cc && cc;
        return View();
    }

    // ── Helper: aggregate readable text from task artifacts ──────────────────
    private async Task<string?> AggregateArtifactTextAsync(List<IntakeTask> tasks)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var task in tasks)
        {
            // ── Task comments from action logs ────────────────────────────────
            var comments = task.ActionLogs
                .Where(l => !string.IsNullOrWhiteSpace(l.Comment))
                .OrderBy(l => l.CreatedAt)
                .ToList();
            if (comments.Count > 0)
            {
                sb.AppendLine($"--- Notes on Task {task.TaskId}: {task.Title} ---");
                foreach (var c in comments)
                {
                    var prefix = c.ActionType == "StatusChange"
                        ? $"[Status → {c.NewStatus}]"
                        : "[Comment]";
                    sb.AppendLine($"{prefix} {c.Comment}");
                }
                sb.AppendLine();
            }

            // ── Uploaded artifact files ────────────────────────────────────────
            foreach (var doc in task.Documents.Where(d => d.DocumentType == "TaskArtifact"))
            {
                var ext = Path.GetExtension(doc.FileName ?? "").ToLowerInvariant();

                try
                {
                    string? content = null;

                    if (ext is ".xlsx" or ".docx")
                    {
                        // Binary document: download bytes then extract text
                        byte[]? bytes = null;
                        if (await _blobService.IsConfiguredAsync()
                            && doc.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            bytes = await _blobService.DownloadBytesAsync(doc.FilePath);
                        }
                        else
                        {
                            var fullPath = Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/'));
                            if (System.IO.File.Exists(fullPath))
                                bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                        }

                        if (bytes != null)
                            content = DocumentTextExtractor.Extract(bytes, ext);
                    }
                    else if (ext is ".txt" or ".csv" or ".json" or ".xml" or ".md")
                    {
                        if (await _blobService.IsConfiguredAsync()
                            && doc.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            content = await _blobService.DownloadTextAsync(doc.FilePath);
                        }
                        else
                        {
                            var fullPath = Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/'));
                            if (System.IO.File.Exists(fullPath))
                                content = await System.IO.File.ReadAllTextAsync(fullPath);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        sb.AppendLine($"--- Artifact: {doc.FileName} (Task: {task.TaskId}) ---");
                        sb.AppendLine(content.Length > 2000 ? content[..2000] + "[...truncated]" : content);
                        sb.AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read artifact {FileName} for task {TaskId}", doc.FileName, task.TaskId);
                }
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    // ── Helper: inject N/A tasks into verification JSON as auto-passed ─────────
    private static string InjectNaTasksIntoJson(string verificationJson, List<IntakeTask> naTasks)
    {
        if (naTasks.Count == 0) return verificationJson;

        try
        {
            using var doc  = JsonDocument.Parse(verificationJson);
            var root       = doc.RootElement;
            var writer     = new System.Text.StringBuilder();
            writer.Append("{");
            writer.Append($"\"canCloseIntake\":{(root.TryGetProperty("canCloseIntake", out var cc) ? cc.GetRawText() : "true")},");
            writer.Append($"\"summary\":{(root.TryGetProperty("summary", out var s) ? s.GetRawText() : "\"All tasks verified or marked as Not Applicable.\"")},");
            writer.Append("\"taskVerifications\":[");

            // existing verifications
            var existing = new List<string>();
            if (root.TryGetProperty("taskVerifications", out var tvs))
                foreach (var tv in tvs.EnumerateArray())
                    existing.Add(tv.GetRawText());

            // append N/A entries (always canClose=true, isNotApplicable=true)
            foreach (var t in naTasks)
            {
                var reason = string.IsNullOrWhiteSpace(t.NaReason)
                    ? "Marked as Not Applicable — bypassed from AI verification."
                    : $"N/A — {t.NaReason.Trim()}";
                existing.Add(
                    $"{{\"taskId\":{JsonSerializer.Serialize(t.TaskId)}," +
                    $"\"title\":{JsonSerializer.Serialize(t.Title)}," +
                    $"\"canClose\":true," +
                    $"\"isNotApplicable\":true," +
                    $"\"reason\":{JsonSerializer.Serialize(reason)}," +
                    $"\"missingEvidence\":[]}}");
            }

            writer.Append(string.Join(",", existing));
            writer.Append("]}");
            return writer.ToString();
        }
        catch
        {
            // If JSON manipulation fails, return original
            return verificationJson;
        }
    }
}
