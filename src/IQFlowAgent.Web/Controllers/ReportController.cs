using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IQFlowAgent.Web.Controllers;

[Authorize]
public class ReportController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IDocxReportService _docxService;
    private readonly IAzureOpenAiService _aiService;
    private readonly IBlobStorageService _blobService;
    private readonly IDocumentIntelligenceService _docIntelligence;
    private readonly ILogger<ReportController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly ITenantContextService _tenantContext;

    private const string TemplateName = "BARTOK_S8_SOP_Template_v2.docx";
    private const int MaxArtifactCharsPerFile = 8_000;  // per-file cap when aggregating task artifacts

    private static string GenerateTaskId() =>
        "TSK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpper();

    public ReportController(
        ApplicationDbContext db,
        IDocxReportService docxService,
        IAzureOpenAiService aiService,
        IBlobStorageService blobService,
        IDocumentIntelligenceService docIntelligence,
        ILogger<ReportController> logger,
        IWebHostEnvironment env,
        ITenantContextService tenantContext)
    {
        _db              = db;
        _docxService     = docxService;
        _aiService       = aiService;
        _blobService     = blobService;
        _docIntelligence = docIntelligence;
        _logger          = logger;
        _env             = env;
        _tenantContext   = tenantContext;
    }

    // ── GET /Report/Prepare?selectedId=&search=&country=&businessUnit=&processType= ─
    public async Task<IActionResult> Prepare(
        int? selectedId,
        string? search, string? country, string? businessUnit, string? processType)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var allIntakes = await _db.IntakeRecords
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
        var filtered = allIntakes.Where(x =>
            (string.IsNullOrWhiteSpace(search) ||
             x.IntakeId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.BusinessUnit.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             x.Department.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(country)      || x.Country      == country)
            && (string.IsNullOrWhiteSpace(businessUnit) || x.BusinessUnit == businessUnit)
            && (string.IsNullOrWhiteSpace(processType)  || x.ProcessType  == processType)
        ).ToList();

        ViewBag.IntakePickerIntakes      = filtered;
        ViewBag.IntakePickerSelected     = selectedId;
        ViewBag.IntakePickerSearch       = search;
        ViewBag.IntakePickerCountry      = country;
        ViewBag.IntakePickerBusinessUnit = businessUnit;
        ViewBag.IntakePickerProcessType  = processType;
        ViewBag.IntakePickerController   = "Report";
        ViewBag.IntakePickerAction       = "Prepare";
        ViewBag.Countries     = allIntakes.Select(x => x.Country)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        ViewBag.BusinessUnits = allIntakes.Select(x => x.BusinessUnit)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        ViewBag.ProcessTypes  = allIntakes.Select(x => x.ProcessType)
            .Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();

        // ── No intake selected: show picker only ───────────────────────────
        if (!selectedId.HasValue)
        {
            ViewBag.Intake = null;
            return View(new List<ReportFieldStatus>());
        }

        var intake = allIntakes.FirstOrDefault(x => x.Id == selectedId.Value);
        if (intake == null) return NotFound();

        // ── Load field statuses, tasks and existing report ──────────────────
        var fieldStatuses = await _db.ReportFieldStatuses
            .Where(r => r.IntakeRecordId == selectedId.Value)
            .OrderBy(r => r.Id)
            .ToListAsync();

        var tasks = await _db.IntakeTasks
            .Where(t => t.IntakeRecordId == selectedId.Value)
            .Include(t => t.ActionLogs)
            .Include(t => t.Documents)
            .ToListAsync();

        var existingReport = await _db.FinalReports
            .Where(r => r.IntakeRecordId == selectedId.Value)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync();

        ViewBag.Intake         = intake;
        ViewBag.ExistingReport = existingReport;
        ViewBag.TaskCount      = tasks.Count;
        ViewBag.OpenTaskCount  = tasks.Count(t => t.Status is "Open" or "In Progress");

        return View(fieldStatuses);
    }

    // ── POST /Report/AnalyzeFields ───────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalyzeFields(int intakeId)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeId);
        if (intake == null) return NotFound();

        var fieldDefs = _docxService.GetFieldDefinitions();

        // Load all tasks with their artifacts and action logs
        var tasks = await _db.IntakeTasks
            .Where(t => t.IntakeRecordId == intakeId)
            .Include(t => t.Documents)
            .Include(t => t.ActionLogs)
            .ToListAsync();

        // Existing field statuses — needed to resolve LinkedTaskId
        var existingStatuses = await _db.ReportFieldStatuses
            .Where(r => r.IntakeRecordId == intakeId)
            .ToListAsync();

        // Intake-level documents (original uploads) — used as global background context
        var intakeDocs = await _db.IntakeDocuments
            .Where(d => d.IntakeRecordId == intakeId && d.IntakeTaskId == null)
            .ToListAsync();
        var globalDocText = await AggregateIntakeDocumentsTextAsync(intakeDocs);

        // ── Build field-key → related-tasks map ─────────────────────────────
        // A task is related to a field when:
        //   (a) its title matches "[Report] Gather: {field.Label}"  (auto-created by CreateTaskForField), OR
        //   (b) a field status has LinkedTaskId pointing to this task.
        const string reportGatherPrefix = "[Report] Gather: ";
        var fieldTaskMap = new Dictionary<string, List<IntakeTask>>(StringComparer.OrdinalIgnoreCase);
        foreach (var fd in fieldDefs)
        {
            fieldTaskMap[fd.Key] = tasks
                .Where(t =>
                    t.Title.Equals(reportGatherPrefix + fd.Label, StringComparison.OrdinalIgnoreCase)
                    || existingStatuses.Any(s =>
                        s.FieldKey == fd.Key
                        && !string.IsNullOrWhiteSpace(s.LinkedTaskId)
                        && s.LinkedTaskId == t.TaskId))
                .ToList();
        }

        // ── Section-keyword task mapping (second pass) ────────────────────────
        // Any task whose title or description mentions a keyword derived from a
        // BARTOK section name is mapped to ALL fields in that section.
        // This ensures that user-created checkpoint tasks (e.g. a task titled
        // "RACI Review" or "Volumetrics Data") automatically feed the right
        // section without requiring the exact "[Report] Gather: {label}" prefix.
        // Keywords are derived dynamically from section names — no hardcoding.

        // Pre-compute per-section keyword arrays (section names are static — avoid
        // repeating the split+filter inside every loop iteration).
        var sectionKeywordMap = fieldDefs
            .GroupBy(f => f.Section, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Key
                    .Split([' ', '.', ':', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.Trim().ToLowerInvariant())
                    .Where(w => w.Length >= 3 && !int.TryParse(w, out _))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        // Pre-compute per-task word arrays so the inner predicate doesn't re-split strings.
        var taskWordMap = tasks.ToDictionary(
            t => t.Id,
            t => (t.Title + " " + (t.Description ?? ""))
                    .Split([' ', '-', '_', '.', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.ToLowerInvariant())
                    .Where(w => w.Length >= 3)  // covers SLA, OCC, SOP and longer words
                    .ToArray());

        foreach (var sectionGroup in fieldDefs.GroupBy(f => f.Section, StringComparer.OrdinalIgnoreCase))
        {
            var sectionKeywords = sectionKeywordMap[sectionGroup.Key];
            if (sectionKeywords.Length == 0) continue;

            var keywordMatchedTasks = tasks.Where(t =>
            {
                var taskWords = taskWordMap[t.Id];
                return sectionKeywords.Any(kw =>
                    // Direct contains (section keyword appears verbatim in task text)
                    t.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(t.Description)
                        && t.Description.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    // Bidirectional prefix: "volume" is a prefix of "volumetrics";
                    // "sla" is a prefix of "slas" — either direction counts.
                    || taskWords.Any(tw =>
                        kw.StartsWith(tw, StringComparison.OrdinalIgnoreCase)
                        || tw.StartsWith(kw, StringComparison.OrdinalIgnoreCase)));
            }).ToList();

            if (keywordMatchedTasks.Count == 0) continue;

            // Add matched tasks to each field in the section (deduplicated)
            foreach (var fd in sectionGroup)
                foreach (var t in keywordMatchedTasks.Where(t => !fieldTaskMap[fd.Key].Contains(t)))
                    fieldTaskMap[fd.Key].Add(t);
        }

        // Pre-compute ALL task artifacts as a global fallback — used for BARTOK sections
        // that have no dedicated checkpoint task yet ("remaining from intake tasks").
        var allTaskArtifactText = tasks.Count > 0
            ? await AggregateArtifactTextAsync(tasks)
            : null;

        // ── Per-section AI analysis ──────────────────────────────────────────
        // One focused Azure OpenAI call per BARTOK section.
        // • Sections with specific tasks: LLM receives only those tasks' artifacts
        //   (keeps each call focused and prevents unrelated docs from drowning the signal).
        // • Sections WITHOUT specific tasks: LLM receives ALL task artifacts as a
        //   fallback ("remaining one should come from intake tasks").
        var aiValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int sectionsAnalyzed = 0;

        foreach (var section in fieldDefs.GroupBy(f => f.Section, StringComparer.OrdinalIgnoreCase))
        {
            var sectionFields = section.ToList();

            // Collect tasks specific to any field in this section (deduplicated)
            var sectionTasks = sectionFields
                .SelectMany(f => fieldTaskMap.TryGetValue(f.Key, out var ts)
                    ? ts : Enumerable.Empty<IntakeTask>())
                .Distinct()
                .ToList();

            // Section-specific tasks → focused artifact text.
            // No specific tasks → fall back to ALL task artifacts (user's uploaded documents).
            var sectionArtifactText = sectionTasks.Count > 0
                ? await AggregateArtifactTextAsync(sectionTasks)
                : allTaskArtifactText;

            // ── Special case: po_volumes cross-section artifact supplement ───────
            // po_volumes lives in "2. Process Overview" but users typically upload their
            // monthly-volume Excel against the Volumetrics (section 8) task.  When the
            // Process Overview section is being analyzed, its task list does not include
            // the Volumetrics task, so the LLM has no access to the Excel and correctly
            // falls back to the placeholder message.
            // Fix: if this section contains po_volumes, append any task artifacts that
            // are mapped to vol_content (the Volumetrics field) but are not already
            // included in the current section's artifact text.
            if (sectionFields.Any(f => f.Key == "po_volumes"))
            {
                var volTasks = fieldTaskMap.TryGetValue("vol_content", out var vts)
                    ? vts.Where(t => !sectionTasks.Contains(t)).ToList()
                    : [];
                if (volTasks.Count > 0)
                {
                    var volText = await AggregateArtifactTextAsync(volTasks);
                    if (!string.IsNullOrWhiteSpace(volText))
                        sectionArtifactText = string.IsNullOrWhiteSpace(sectionArtifactText)
                            ? volText
                            : sectionArtifactText
                              + "\n\n=== SUPPLEMENTARY VOLUMETRICS ARTIFACTS ===\n"
                              + volText;
                }
            }

            var sectionValues = await _aiService.AnalyzeSectionFieldsAsync(
                intake, section.Key, sectionFields,
                sectionArtifactText, globalDocText, intake.AnalysisResult);

            foreach (var kv in sectionValues)
                aiValues[kv.Key] = kv.Value;

            sectionsAnalyzed++;
        }

        // NOTE: OfflineFieldExtractor (hardcoded regex/pattern extraction) has been removed.
        // All field extraction is now done exclusively by the LLM (AnalyzeSectionFieldsAsync),
        // which receives the raw document text from uploaded files and extracts values verbatim.
        // This avoids the fragility of hardcoded patterns that break when document formats change.

        _logger.LogInformation(
            "Section-by-section analysis for intake {IntakeId}: {Sections} sections, " +
            "{AiCount} AI field values extracted by LLM.",
            intake.IntakeId, sectionsAnalyzed, aiValues.Count);

        // ── Upsert field statuses ────────────────────────────────────────────
        var now = DateTime.UtcNow;
        int aiFilledCount = 0;

        foreach (var fd in fieldDefs)
        {
            var existing = existingStatuses.FirstOrDefault(s => s.FieldKey == fd.Key);

            aiValues.TryGetValue(fd.Key, out var aiVal);
            // Discard AI values that are just placeholder text
            if (!string.IsNullOrWhiteSpace(aiVal) && IsPlaceholderText(aiVal))
                aiVal = null;

            // All extraction is now LLM-based; no offline fallback
            var fillValue = !string.IsNullOrWhiteSpace(aiVal) ? aiVal : null;
            var notes     = !string.IsNullOrWhiteSpace(aiVal)
                ? "Extracted by AI analysis of task artifacts and documents."
                : null;

            if (!string.IsNullOrWhiteSpace(aiVal)) aiFilledCount++;

            if (existing == null)
            {
                var newStatus = !string.IsNullOrWhiteSpace(fillValue) ? "Available" : "Missing";
                _db.ReportFieldStatuses.Add(new ReportFieldStatus
                {
                    IntakeRecordId      = intakeId,
                    FieldKey            = fd.Key,
                    FieldLabel          = fd.Label,
                    Section             = fd.Section,
                    TemplatePlaceholder = fd.TemplatePlaceholder,
                    Status              = newStatus,
                    FillValue           = fillValue,
                    Notes               = notes,
                    AnalyzedAt          = now,
                    UpdatedAt           = now
                });
            }
            else
            {
                existing.TemplatePlaceholder = fd.TemplatePlaceholder;
                existing.FieldLabel          = fd.Label;
                existing.Section             = fd.Section;
                existing.UpdatedAt           = now;
                existing.AnalyzedAt          = now;

                if (existing.Status != "NA")
                {
                    if (!string.IsNullOrWhiteSpace(fillValue))
                    {
                        // New value found — update everything
                        existing.Status    = "Available";
                        existing.FillValue = fillValue;
                        existing.Notes     = notes;
                    }
                    else if (string.IsNullOrWhiteSpace(existing.FillValue))
                    {
                        // No new value and nothing previously saved — mark Missing
                        existing.Status = "Missing";
                    }
                    // else: re-run found nothing new, but field already has a good value
                    // → preserve existing Status and FillValue unchanged
                }
            }
        }

        // ── Safety net: Status must always match the actual FillValue ─────────
        // Guards against any edge-case in the update branches above (or stale data
        // left by older code versions) where a non-empty FillValue ends up with a
        // "Missing" status badge, confusing users.
        foreach (var entry in _db.ChangeTracker.Entries<ReportFieldStatus>()
            .Where(e => e.State is Microsoft.EntityFrameworkCore.EntityState.Added
                                 or Microsoft.EntityFrameworkCore.EntityState.Modified))
        {
            var e = entry.Entity;
            if (e.Status != "NA")
            {
                if (!string.IsNullOrWhiteSpace(e.FillValue) && e.Status == "Missing")
                    e.Status = "Available";
                else if (string.IsNullOrWhiteSpace(e.FillValue) && e.Status == "Available")
                    e.Status = "Missing";
            }
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = aiFilledCount > 0
            ? $"Analysis complete ({sectionsAnalyzed} sections processed). " +
              $"{aiFilledCount} field(s) extracted by AI from task documents."
            : "Analysis complete. No data found in task artifacts — please ensure task " +
              "documents are uploaded and try again.";

        return RedirectToAction(nameof(Prepare), new { selectedId = intakeId });
    }

    // ── POST /Report/MarkNA ──────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNA(int fieldStatusId, bool isNA)
    {
        var field = await _db.ReportFieldStatuses.FindAsync(fieldStatusId);
        if (field == null) return NotFound();

        field.IsNA      = isNA;
        field.Status    = isNA ? "NA" : "Missing";
        field.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Prepare), new { selectedId = field.IntakeRecordId });
    }

    // ── POST /Report/SetValue ────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetValue(int fieldStatusId, string fillValue)
    {
        var field = await _db.ReportFieldStatuses.FindAsync(fieldStatusId);
        if (field == null) return NotFound();

        field.FillValue  = fillValue;
        field.Status     = string.IsNullOrWhiteSpace(fillValue) ? "Missing" : "Available";
        field.IsNA       = false;
        field.UpdatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Prepare), new { selectedId = field.IntakeRecordId });
    }

    // ── POST /Report/CreateTaskForField ─────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTaskForField(int fieldStatusId)
    {
        var field = await _db.ReportFieldStatuses
            .Include(f => f.IntakeRecord)
            .FirstOrDefaultAsync(f => f.Id == fieldStatusId);
        if (field == null) return NotFound();

        var intake = field.IntakeRecord;

        // Check for existing task with same title
        var title = $"[Report] Gather: {field.FieldLabel}";
        var existing = await _db.IntakeTasks
            .FirstOrDefaultAsync(t => t.IntakeRecordId == intake.Id && t.Title == title);

        string taskId;
        if (existing != null)
        {
            taskId = existing.TaskId;
        }
        else
        {
            var now = DateTime.UtcNow;
            var task = new IntakeTask
            {
                TaskId         = GenerateTaskId(),
                IntakeRecordId = intake.Id,
                Title          = title,
                Description    = $"Gather the following information required for the BARTOK DD Report field '{field.FieldLabel}' (Section: {field.Section}): {field.Notes}",
                Priority       = "High",
                Owner          = intake.ProcessOwnerEmail ?? User.Identity?.Name ?? "Unassigned",
                Status         = "Open",
                CreatedAt      = now,
                DueDate        = now.AddHours(48),
                CreatedByUserId = User.Identity?.Name
            };
            _db.IntakeTasks.Add(task);
            _db.TaskActionLogs.Add(new TaskActionLog
            {
                Task            = task,
                ActionType      = "StatusChange",
                OldStatus       = null,
                NewStatus       = "Open",
                Comment         = $"Task created to gather report field '{field.FieldLabel}' for BARTOK DD Report.",
                CreatedAt       = now,
                CreatedByUserId = User.Identity?.Name,
                CreatedByName   = User.Identity?.Name
            });
            taskId = task.TaskId;
        }

        field.Status        = "TaskCreated";
        field.LinkedTaskId  = taskId;
        field.UpdatedAt     = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Task created for field '{field.FieldLabel}'.";
        return RedirectToAction(nameof(Prepare), new { selectedId = field.IntakeRecordId });
    }

    // ── POST /Report/AiGenerateField ─────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AiGenerateField(int fieldStatusId, string? userContext)
    {
        var field = await _db.ReportFieldStatuses
            .Include(f => f.IntakeRecord)
            .FirstOrDefaultAsync(f => f.Id == fieldStatusId);
        if (field == null) return NotFound();

        var intake = field.IntakeRecord;

        // Aggregate analysis JSON from intake
        var analysisJson = intake.AnalysisResult;

        // Aggregate artifact text from tasks (comments + uploaded files)
        var tasks = await _db.IntakeTasks
            .Where(t => t.IntakeRecordId == intake.Id)
            .Include(t => t.Documents)
            .Include(t => t.ActionLogs)
            .ToListAsync();
        var taskArtifactText = await AggregateArtifactTextAsync(tasks);

        // Aggregate text from intake-level documents (original uploads)
        var intakeDocs = await _db.IntakeDocuments
            .Where(d => d.IntakeRecordId == intake.Id && d.IntakeTaskId == null)
            .ToListAsync();
        var intakeDocText = await AggregateIntakeDocumentsTextAsync(intakeDocs);

        var combinedDocText = string.Join("\n", new[] { intakeDocText, taskArtifactText }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var artifactText = string.IsNullOrWhiteSpace(combinedDocText) ? null : combinedDocText;

        var generated = await _aiService.GenerateSingleFieldAsync(
            intake, field.FieldKey, field.FieldLabel, userContext, analysisJson, artifactText);

        if (string.IsNullOrWhiteSpace(generated))
            return Json(new { success = false, message = "AI could not generate content. Ensure Azure OpenAI is configured and try again." });

        return Json(new { success = true, value = generated });
    }

    // ── POST /Report/Generate ────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int intakeId)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeId);
        if (intake == null) return NotFound();

        var fieldStatuses = await _db.ReportFieldStatuses
            .Where(r => r.IntakeRecordId == intakeId)
            .ToListAsync();

        // Guard: all fields must be Available or NA
        var blocking = fieldStatuses.Where(f => f.Status is "Missing" or "Pending" or "TaskCreated").ToList();
        if (blocking.Count > 0)
        {
            TempData["Error"] = $"{blocking.Count} field(s) are not yet resolved. Please address them before generating the report.";
            return RedirectToAction(nameof(Prepare), new { selectedId = intakeId });
        }

        // Locate the template
        var templatePath = Path.Combine(_env.WebRootPath, "templates", TemplateName);
        if (!System.IO.File.Exists(templatePath))
        {
            TempData["Error"] = "Report template file not found on the server.";
            return RedirectToAction(nameof(Prepare), new { selectedId = intakeId });
        }

        try
        {
            // Generate the filled docx
            var docxBytes = await _docxService.GenerateReportAsync(intake, fieldStatuses, templatePath);

            var now = DateTime.UtcNow;
            var reportFileName = $"BARTOK_DD_{intake.IntakeId}_{now:yyyyMMddHHmmss}.docx";
            string filePath;

            if (await _blobService.IsConfiguredAsync())
            {
                using var stream = new MemoryStream(docxBytes);
                filePath = await _blobService.UploadAsync(
                    stream, reportFileName,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            }
            else
            {
                var reportsDir = Path.Combine(_env.WebRootPath, "reports");
                Directory.CreateDirectory(reportsDir);
                var fullPath = Path.Combine(reportsDir, reportFileName);
                await System.IO.File.WriteAllBytesAsync(fullPath, docxBytes);
                filePath = $"/reports/{reportFileName}";
            }

            // Save FinalReport record
            _db.FinalReports.Add(new FinalReport
            {
                IntakeRecordId    = intakeId,
                ReportFileName    = reportFileName,
                FilePath          = filePath,
                FileSizeBytes     = docxBytes.Length,
                GeneratedAt       = now,
                GeneratedByUserId = User.Identity?.Name,
                GeneratedByName   = User.Identity?.Name
            });

            // Close the intake
            intake.Status = "Closed";
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Final report generated for intake {IntakeId} — file: {FileName}", intake.IntakeId, reportFileName);

            TempData["ReportPath"]     = filePath;
            TempData["ReportFileName"] = reportFileName;
            TempData["ReportSizeKb"]   = (docxBytes.Length / 1024).ToString();
            TempData["CanClose"]       = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate report for intake {IntakeId}", intake.IntakeId);
            TempData["Error"] = "Report generation failed. Please check the server logs and try again.";
            return RedirectToAction(nameof(Prepare), new { selectedId = intakeId });
        }

        return RedirectToAction(nameof(Generated), new { intakeId });
    }

    // ── GET /Report/Generated ────────────────────────────────────────────────
    public async Task<IActionResult> Generated(int intakeId)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeId);
        var report = await _db.FinalReports
            .Where(r => r.IntakeRecordId == intakeId)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync();

        ViewBag.Intake           = intake;
        ViewBag.Report           = report;
        ViewBag.ReportPath       = TempData["ReportPath"] as string ?? report?.FilePath;
        ViewBag.ReportFileName   = TempData["ReportFileName"] as string ?? report?.ReportFileName;
        ViewBag.ReportSizeKb     = TempData["ReportSizeKb"] as string;
        return View();
    }

    // ── GET /Report/Download ─────────────────────────────────────────────────
    public async Task<IActionResult> Download(int intakeId)
    {
        var report = await _db.FinalReports
            .Where(r => r.IntakeRecordId == intakeId)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync();

        if (report == null) return NotFound();

        if (report.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var sasUrl = await _blobService.GenerateSasDownloadUrlAsync(report.FilePath);
            if (string.IsNullOrWhiteSpace(sasUrl))
                return NotFound();
            return Redirect(sasUrl);
        }

        var fullPath = Path.Combine(_env.WebRootPath, report.FilePath.TrimStart('/'));
        if (!System.IO.File.Exists(fullPath)) return NotFound();

        var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            report.ReportFileName);
    }

    // ── Helper: aggregate readable artifact text ─────────────────────────────
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
                try
                {
                    var content = await ReadDocumentContentAsync(doc);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        sb.AppendLine($"--- {doc.FileName} (Task: {task.TaskId}) ---");
                        sb.AppendLine(content.Length > MaxArtifactCharsPerFile ? content[..MaxArtifactCharsPerFile] + "[...truncated]" : content);
                        sb.AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read artifact {FileName}", doc.FileName);
                }
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    // ── Helper: aggregate text from intake-level documents (original uploads) ─
    private async Task<string?> AggregateIntakeDocumentsTextAsync(List<IntakeDocument> intakeDocs)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var doc in intakeDocs)
        {
            try
            {
                var content = await ReadDocumentContentAsync(doc);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine($"--- {doc.FileName} ---");
                    sb.AppendLine(content.Length > MaxArtifactCharsPerFile ? content[..MaxArtifactCharsPerFile] + "[...truncated]" : content);
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read intake document {FileName}", doc.FileName);
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    // ── Helper: read text content from a single document (blob or local) ──────
    private async Task<string?> ReadDocumentContentAsync(IntakeDocument doc)
    {
        var ext = Path.GetExtension(doc.FileName ?? "").ToLowerInvariant();

        // ── Binary formats: try Azure Document Intelligence first ─────────────
        // Document Intelligence handles Excel tables, Word tables, and PDF layouts
        // with far greater accuracy than raw OpenXML text extraction.
        if (ext is ".xlsx" or ".docx" or ".pdf")
        {
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
            {
                // Primary: Azure Document Intelligence (accurate table + paragraph extraction)
                if (_docIntelligence.IsConfigured())
                {
                    var diText = await _docIntelligence.ExtractTextAsync(bytes, doc.FileName ?? "file" + ext);
                    if (!string.IsNullOrWhiteSpace(diText)) return diText;
                }

                // Fallback: local OpenXML extraction (no external service needed)
                if (ext is ".xlsx" or ".docx")
                    return DocumentTextExtractor.Extract(bytes, ext);
            }

            return null;
        }

        // ── Plain-text formats ────────────────────────────────────────────────
        if (ext is ".txt" or ".csv" or ".json" or ".xml" or ".md")
        {
            if (await _blobService.IsConfiguredAsync()
                && doc.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return await _blobService.DownloadTextAsync(doc.FilePath);
            }

            var fullPath = Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/'));
            if (System.IO.File.Exists(fullPath))
                return await System.IO.File.ReadAllTextAsync(fullPath);
        }

        return null;
    }

    /// <summary>
    /// Returns true when <paramref name="value"/> is an AI-generated placeholder that
    /// carries no real information (e.g. "To be confirmed with process owner").
    /// Offline-extracted values should replace these.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex TbcWordPattern =
        new(@"\bTBC\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsPlaceholderText(string value) =>
        value.Contains("to be confirmed", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("not in document",  StringComparison.OrdinalIgnoreCase) ||
        value.Contains("not provided",     StringComparison.OrdinalIgnoreCase) ||
        value.Contains("not specified",    StringComparison.OrdinalIgnoreCase) ||
        TbcWordPattern.IsMatch(value)                                          ||
        value.Contains("n/a — not",        StringComparison.OrdinalIgnoreCase);
}
