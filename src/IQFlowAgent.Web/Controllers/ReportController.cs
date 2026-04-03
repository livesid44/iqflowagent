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
    /// <summary>
    /// Unified single-button action. Runs the full AI field analysis pipeline, then:
    ///  • If all fields are resolved (no Missing / Pending / TaskCreated) →
    ///    automatically runs the DOCX generation pipeline and redirects to the
    ///    Generated / download page so the user lands straight on their report.
    ///  • If fields are still outstanding → redirects back to the Prepare page
    ///    showing exactly which fields still need attention.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalyzeFields(int intakeId)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeId);
        if (intake == null) return NotFound();

        var (sectionsAnalyzed, aiFilledCount) = await RunAiAnalysisAsync(intake);

        // Reload the updated field statuses to decide the next step.
        var fieldStatuses = await _db.ReportFieldStatuses
            .Where(f => f.IntakeRecordId == intakeId)
            .ToListAsync();

        bool allReady = fieldStatuses.Count > 0
            && !fieldStatuses.Any(f => f.Status is "Missing" or "Pending" or "TaskCreated");

        if (allReady)
        {
            // All fields resolved — automatically generate the report and go to download.
            return await ExecuteDocxPipelineAsync(intake, intakeId, fieldStatuses);
        }

        // Open points remain — tell the user what was found and let them review.
        var openCount = fieldStatuses.Count(f => f.Status is "Missing" or "Pending" or "TaskCreated");
        TempData["Success"] = aiFilledCount > 0
            ? $"Analysis complete ({sectionsAnalyzed} sections processed, {aiFilledCount} field(s) extracted). " +
              $"{openCount} field(s) still need attention — please resolve them below."
            : $"Analysis complete. {openCount} field(s) could not be resolved automatically — " +
              "please fill them in manually or mark them N/A, then run the analysis again.";

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

        // ── RACI supplement: concat existing RACI sibling field values ──────────
        // When generating raci_content, load all raci_task*/raci_role* sibling
        // fields and build a clear role-activity-paired context block. This gives
        // GenerateSingleFieldAsync (which has a focused RACI prompt) the best
        // possible input to produce an accurate pipe-delimited RACI matrix.
        if (field.FieldKey.Equals("raci_content", StringComparison.OrdinalIgnoreCase))
        {
            var raciSiblings = await _db.ReportFieldStatuses
                .Where(f => f.IntakeRecordId == intake.Id
                         && f.FieldKey != "raci_content"
                         && f.FieldKey.StartsWith("raci_", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(f.FillValue))
                .OrderBy(f => f.FieldKey)
                .ToListAsync();

            if (raciSiblings.Count > 0)
            {
                var raciContext = BuildRaciContext(raciSiblings);
                artifactText = string.IsNullOrWhiteSpace(artifactText)
                    ? raciContext
                    : raciContext + "\n\n" + artifactText;
            }
        }


        var generated = await _aiService.GenerateSingleFieldAsync(
            intake, field.FieldKey, field.FieldLabel, userContext, analysisJson, artifactText);

        if (string.IsNullOrWhiteSpace(generated))
            return Json(new { success = false, message = "AI could not generate content. Ensure Azure OpenAI is configured and try again." });

        return Json(new { success = true, value = generated });
    }

    // ── POST /Report/Generate ────────────────────────────────────────────────
    /// <summary>
    /// Legacy endpoint kept for backward-compat (direct form posts, deep links).
    /// Runs the full AI analysis pipeline then delegates to ExecuteDocxPipelineAsync.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int intakeId)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeId);
        if (intake == null) return NotFound();

        // Run fresh AI analysis so RACI, SOP, Volume, OSS etc. are all current.
        await RunAiAnalysisAsync(intake);

        var fieldStatuses = await _db.ReportFieldStatuses
            .Where(r => r.IntakeRecordId == intakeId)
            .ToListAsync();

        return await ExecuteDocxPipelineAsync(intake, intakeId, fieldStatuses);
    }

    // ── POST /Report/RegenerateReport ────────────────────────────────────────
    /// <summary>
    /// Regenerates the final DOCX report from the current stored field values without
    /// re-running the full AI analysis pipeline.  Useful when fields have been manually
    /// edited or updated from task artefacts and the user just wants a fresh document.
    /// Available for intakes in Closed, Complete, or Error status.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateReport(int intakeId)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeId);
        if (intake == null) return NotFound();

        if (intake.Status is not ("Closed" or "Complete" or "Error"))
        {
            TempData["Error"] = "Report regeneration is only available for completed or closed intakes.";
            return RedirectToAction(nameof(Prepare), new { selectedId = intakeId });
        }

        var fieldStatuses = await _db.ReportFieldStatuses
            .Where(f => f.IntakeRecordId == intakeId)
            .ToListAsync();

        if (fieldStatuses.Count == 0)
        {
            TempData["Error"] = "No report fields found for this intake. Run AI Field Analysis first.";
            return RedirectToAction(nameof(Prepare), new { selectedId = intakeId });
        }

        // Allow ExecuteDocxPipelineAsync to set status to Closed; temporarily lift it
        // from Closed so the pipeline's status update is persisted (no-op when already Complete/Error).
        if (intake.Status == "Closed")
            intake.Status = "Complete";

        return await ExecuteDocxPipelineAsync(intake, intakeId, fieldStatuses);
    }

    // ── Private: polish + DOCX + upload + save + redirect ───────────────────
    /// <summary>
    /// Runs the final LLM polish pass on <paramref name="fieldStatuses"/>, generates
    /// the DOCX, uploads it (blob or local), persists a <see cref="FinalReport"/> record,
    /// closes the intake, and redirects to the Generated page.
    /// On any error, sets TempData["Error"] and redirects to the Prepare page.
    /// </summary>
    private async Task<IActionResult> ExecuteDocxPipelineAsync(
        IntakeRecord intake, int intakeId, List<ReportFieldStatus> fieldStatuses)
    {
        // ── Polish pass ──────────────────────────────────────────────────────
        try
        {
            var structuredKeys = new HashSet<string>(
                ["raci_content", "sop_content", "vol_content"],
                StringComparer.OrdinalIgnoreCase);

            var snapshot = fieldStatuses
                .Where(fs =>
                    fs.Status != "NA"
                    && !string.IsNullOrWhiteSpace(fs.FillValue)
                    && !structuredKeys.Contains(fs.FieldKey))
                .Select(fs => (fs.FieldKey, fs.FieldLabel, fs.Section, fs.FillValue!))
                .ToList();

            if (snapshot.Count > 0)
            {
                var allTasks = await _db.IntakeTasks
                    .Where(t => t.IntakeRecordId == intakeId)
                    .Include(t => t.Documents)
                    .Include(t => t.ActionLogs)
                    .ToListAsync();
                var artifactText = allTasks.Count > 0
                    ? await AggregateArtifactTextAsync(allTasks)
                    : null;

                var polishedValues = await _aiService.PolishDocumentFieldsAsync(
                    intake, snapshot, artifactText);

                int polishedCount = 0;
                foreach (var kv in polishedValues)
                {
                    var fs = fieldStatuses.FirstOrDefault(f =>
                        f.FieldKey.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (fs != null && !IsPlaceholderText(kv.Value))
                    {
                        fs.FillValue = kv.Value;
                        polishedCount++;
                    }
                }

                _logger.LogInformation(
                    "Document polish pass completed for intake {IntakeId}: {Count}/{Total} fields improved.",
                    intake.IntakeId, polishedCount, snapshot.Count);
            }
        }
        catch (Exception ex)
        {
            // Polish pass is best-effort — a failure must not block DOCX generation.
            _logger.LogWarning(ex,
                "Document polish pass failed for intake {IntakeId} — proceeding with unpolished values.",
                intake.IntakeId);
        }

        // ── DOCX generation ──────────────────────────────────────────────────
        var templatePath = Path.Combine(_env.WebRootPath, "templates", TemplateName);
        if (!System.IO.File.Exists(templatePath))
        {
            TempData["Error"] = "Report template file not found on the server.";
            return RedirectToAction(nameof(Prepare), new { selectedId = intakeId });
        }

        try
        {
            var docxBytes = await _docxService.GenerateReportAsync(intake, fieldStatuses, templatePath);

            var now            = DateTime.UtcNow;
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

            intake.Status = "Closed";
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Final report generated for intake {IntakeId} — file: {FileName}",
                intake.IntakeId, reportFileName);

            TempData["ReportPath"]     = filePath;
            TempData["ReportFileName"] = reportFileName;
            TempData["ReportSizeKb"]   = (docxBytes.Length / 1024).ToString();
            TempData["CanClose"]       = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate report for intake {IntakeId}", intakeId);
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

    // ── POST /Report/GenerateRaciContent — REMOVED ───────────────────────────
    // The dedicated "Generate RACI Matrix" endpoint has been removed. The full AI
    // analysis pipeline (RunAiAnalysisAsync) is now called directly from the Generate
    // action, ensuring RACI, SOP, Volume, OSS and all other sections are processed
    // through the same unified pipeline when clicking "Generate Final Report".

    // ── Helper: run the full AI field analysis pipeline ──────────────────────
    /// <summary>
    /// Runs section-by-section AI analysis for all BARTOK template fields, including a
    /// dedicated RACI matrix generation pass. Upserts ReportFieldStatus rows in the DB.
    /// Called by both AnalyzeFields (standalone re-analysis) and Generate (full pipeline).
    /// Returns (sectionsAnalyzed, aiFilledCount) for progress reporting.
    /// </summary>
    private async Task<(int sectionsAnalyzed, int aiFilledCount)> RunAiAnalysisAsync(IntakeRecord intake)
    {
        var intakeId  = intake.Id;
        var fieldDefs = _docxService.GetFieldDefinitions();

        // Load all tasks with their artifacts and action logs
        var tasks = await _db.IntakeTasks
            .Where(t => t.IntakeRecordId == intakeId)
            .Include(t => t.Documents)
            .Include(t => t.ActionLogs)
            .ToListAsync();

        // Existing field statuses — needed to resolve LinkedTaskId and for RACI supplement
        var existingStatuses = await _db.ReportFieldStatuses
            .Where(r => r.IntakeRecordId == intakeId)
            .ToListAsync();

        // Intake-level documents (original uploads) — used as global background context
        var intakeDocs = await _db.IntakeDocuments
            .Where(d => d.IntakeRecordId == intakeId && d.IntakeTaskId == null)
            .ToListAsync();
        var globalDocText = await AggregateIntakeDocumentsTextAsync(intakeDocs);

        // ── Build field-key → related-tasks map ─────────────────────────────
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

        var taskWordMap = tasks.ToDictionary(
            t => t.Id,
            t => (t.Title + " " + (t.Description ?? ""))
                    .Split([' ', '-', '_', '.', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.ToLowerInvariant())
                    .Where(w => w.Length >= 3)
                    .ToArray());

        foreach (var sectionGroup in fieldDefs.GroupBy(f => f.Section, StringComparer.OrdinalIgnoreCase))
        {
            var sectionKeywords = sectionKeywordMap[sectionGroup.Key];
            if (sectionKeywords.Length == 0) continue;

            var keywordMatchedTasks = tasks.Where(t =>
            {
                var taskWords = taskWordMap[t.Id];
                return sectionKeywords.Any(kw =>
                    t.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(t.Description)
                        && t.Description.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    || taskWords.Any(tw =>
                        kw.StartsWith(tw, StringComparison.OrdinalIgnoreCase)
                        || tw.StartsWith(kw, StringComparison.OrdinalIgnoreCase)));
            }).ToList();

            if (keywordMatchedTasks.Count == 0) continue;

            foreach (var fd in sectionGroup)
                foreach (var t in keywordMatchedTasks.Where(t => !fieldTaskMap[fd.Key].Contains(t)))
                    fieldTaskMap[fd.Key].Add(t);
        }

        var allTaskArtifactText = tasks.Count > 0
            ? await AggregateArtifactTextAsync(tasks)
            : null;

        // ── Per-section AI analysis ──────────────────────────────────────────
        var aiValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int sectionsAnalyzed = 0;

        foreach (var section in fieldDefs.GroupBy(f => f.Section, StringComparer.OrdinalIgnoreCase))
        {
            var sectionFields = section.ToList();

            var sectionTasks = sectionFields
                .SelectMany(f => fieldTaskMap.TryGetValue(f.Key, out var ts)
                    ? ts : Enumerable.Empty<IntakeTask>())
                .Distinct()
                .ToList();

            var sectionArtifactText = sectionTasks.Count > 0
                ? await AggregateArtifactTextAsync(sectionTasks)
                : allTaskArtifactText;

            // Cross-section artifact supplement
            if (sectionTasks.Count > 0)
            {
                var crossSectionTasks = tasks.Where(t => !sectionTasks.Contains(t)).ToList();
                if (crossSectionTasks.Count > 0)
                {
                    var crossText = await AggregateArtifactTextAsync(crossSectionTasks);
                    if (!string.IsNullOrWhiteSpace(crossText))
                        sectionArtifactText = string.IsNullOrWhiteSpace(sectionArtifactText)
                            ? crossText
                            : sectionArtifactText
                              + "\n\n=== SUPPLEMENTARY ARTIFACTS FROM OTHER SECTIONS ===\n"
                              + crossText;
                }
            }

            // RACI supplement: prepend role-activity pairs for the RACI section
            if (section.Key.Contains("RACI", StringComparison.OrdinalIgnoreCase))
            {
                var raciSiblings = existingStatuses
                    .Where(s => s.FieldKey != "raci_content"
                             && s.FieldKey.StartsWith("raci_", StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(s.FillValue))
                    .OrderBy(s => s.FieldKey)
                    .ToList();

                if (raciSiblings.Count > 0)
                {
                    var raciContext = BuildRaciContext(raciSiblings);
                    sectionArtifactText = string.IsNullOrWhiteSpace(sectionArtifactText)
                        ? raciContext
                        : raciContext + "\n\n" + sectionArtifactText;
                }
            }

            var sectionValues = await _aiService.AnalyzeSectionFieldsAsync(
                intake, section.Key, sectionFields,
                sectionArtifactText, globalDocText, intake.AnalysisResult);

            foreach (var kv in sectionValues)
                aiValues[kv.Key] = kv.Value;

            sectionsAnalyzed++;
        }

        // ── Dedicated RACI matrix generation (post-section-loop) ────────────────
        // After all sections are analyzed, generate raci_content using a focused
        // GenerateSingleFieldAsync call with the paired role-activity context.
        // This overrides the AnalyzeSectionFieldsAsync result for the RACI section.
        {
            var raciSiblings = existingStatuses
                .Where(s => s.FieldKey != "raci_content"
                         && s.FieldKey.StartsWith("raci_", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(s.FillValue))
                .OrderBy(s => s.FieldKey)
                .ToList();

            if (raciSiblings.Count > 0)
            {
                var raciCtx       = BuildRaciContext(raciSiblings);
                var raciGenerated = await _aiService.GenerateSingleFieldAsync(
                    intake, "raci_content", "RACI Assignments (LLM Response)",
                    null, intake.AnalysisResult, raciCtx);
                if (!string.IsNullOrWhiteSpace(raciGenerated))
                {
                    aiValues["raci_content"] = raciGenerated;
                    _logger.LogInformation(
                        "Dedicated RACI generation completed for intake {IntakeId} using {Count} sibling fields.",
                        intake.IntakeId, raciSiblings.Count);
                }
            }
        }

        // ── Dedicated SOP generation (post-section-loop) ─────────────────────────
        // GenerateSingleFieldAsync has an explicit "Step N: … Role: … Automation: …"
        // format prompt that the general AnalyzeSectionFieldsAsync lacks. Run a
        // targeted pass so the SOP content is always in the parseable structured format.
        {
            var combinedArtifact = string.Join("\n\n", new[] { allTaskArtifactText, globalDocText }
                .Where(t => !string.IsNullOrWhiteSpace(t)));
            var sopGenerated = await _aiService.GenerateSingleFieldAsync(
                intake, "sop_content", "SOP Steps (LLM Response)",
                null, intake.AnalysisResult,
                string.IsNullOrWhiteSpace(combinedArtifact) ? null : combinedArtifact);
            if (!string.IsNullOrWhiteSpace(sopGenerated))
            {
                aiValues["sop_content"] = sopGenerated;
                _logger.LogInformation(
                    "Dedicated SOP generation completed for intake {IntakeId}.", intake.IntakeId);
            }
        }

        // ── Dedicated Volumetrics generation (post-section-loop) ─────────────────
        // GenerateSingleFieldAsync has an explicit month-by-month tabular format prompt.
        // Run a targeted pass so vol_content is always structured for the Volumetrics table.
        {
            var volGenerated = await _aiService.GenerateSingleFieldAsync(
                intake, "vol_content", "Monthly Volume Data (LLM Response)",
                null, intake.AnalysisResult, allTaskArtifactText);
            if (!string.IsNullOrWhiteSpace(volGenerated))
            {
                aiValues["vol_content"] = volGenerated;
                _logger.LogInformation(
                    "Dedicated Volumetrics generation completed for intake {IntakeId}.", intake.IntakeId);
            }
        }

        _logger.LogInformation(
            "RunAiAnalysisAsync for intake {IntakeId}: {Sections} sections, {AiCount} fields extracted.",
            intake.IntakeId, sectionsAnalyzed, aiValues.Count);

        // ── Upsert field statuses ────────────────────────────────────────────
        var now = DateTime.UtcNow;
        int aiFilledCount = 0;

        foreach (var fd in fieldDefs)
        {
            var existing = existingStatuses.FirstOrDefault(s => s.FieldKey == fd.Key);

            aiValues.TryGetValue(fd.Key, out var aiVal);
            if (!string.IsNullOrWhiteSpace(aiVal) && IsPlaceholderText(aiVal))
                aiVal = null;

            var fillValue = !string.IsNullOrWhiteSpace(aiVal) ? aiVal : null;
            var notes     = !string.IsNullOrWhiteSpace(aiVal)
                ? "Extracted by AI analysis of task artifacts and documents."
                : null;

            if (!string.IsNullOrWhiteSpace(aiVal)) aiFilledCount++;

            if (existing == null)
            {
                _db.ReportFieldStatuses.Add(new ReportFieldStatus
                {
                    IntakeRecordId      = intakeId,
                    FieldKey            = fd.Key,
                    FieldLabel          = fd.Label,
                    Section             = fd.Section,
                    TemplatePlaceholder = fd.TemplatePlaceholder,
                    Status              = !string.IsNullOrWhiteSpace(fillValue) ? "Available" : "Missing",
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
                        existing.Status    = "Available";
                        existing.FillValue = fillValue;
                        existing.Notes     = notes;
                    }
                    else if (string.IsNullOrWhiteSpace(existing.FillValue))
                    {
                        existing.Status = "Missing";
                    }
                }
            }
        }

        // Safety net: Status must always match the actual FillValue
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
        return (sectionsAnalyzed, aiFilledCount);
    }

    // ── Helper: build role-activity paired RACI context string ───────────────
    /// <summary>
    /// Pairs raci_roleN (role name) with raci_taskN (activities) to create a clear,
    /// role-labelled context block for the LLM. The LLM can then derive concise
    /// task names from the activities and produce an accurate R/A/C/I matrix.
    /// </summary>
    private static string BuildRaciContext(IList<ReportFieldStatus> raciSiblings)
    {
        var taskFields = raciSiblings
            .Where(f => f.FieldKey.StartsWith("raci_task", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.FieldKey)
            .ToList();
        var roleFields = raciSiblings
            .Where(f => f.FieldKey.StartsWith("raci_role", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.FieldKey)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== RACI Source Data: Roles and their Activities ===");
        sb.AppendLine("Use this data to create a compact pipe-delimited RACI matrix.");
        sb.AppendLine("Each role below has a list of activities (pipe-separated) they perform in this process.");
        sb.AppendLine("Derive concise 2–5 word task names from the activity lists for the TASKS line.");
        sb.AppendLine();

        bool anyEntry = false;
        for (int i = 1; i <= 4; i++)
        {
            var roleField = roleFields.FirstOrDefault(f =>
                f.FieldKey.Equals($"raci_role{i}", StringComparison.OrdinalIgnoreCase));
            var taskField = taskFields.FirstOrDefault(f =>
                f.FieldKey.Equals($"raci_task{i}", StringComparison.OrdinalIgnoreCase));

            if (roleField != null || taskField != null)
            {
                anyEntry = true;
                var roleName = roleField?.FillValue ?? $"Role {i}";
                sb.AppendLine($"Role {i}: {roleName}");
                if (taskField != null)
                    sb.AppendLine($"  Activities: {taskField.FillValue}");
                sb.AppendLine();
            }
        }

        // Fallback: any siblings not matched by the raci_task/raci_role pattern
        if (!anyEntry)
        {
            foreach (var f in raciSiblings)
                sb.AppendLine($"{f.FieldLabel}: {f.FillValue}");
        }

        return sb.ToString();
    }

    // ── Helper: aggregate readable artifact text ─────────────────────────────
    private async Task<string?> AggregateArtifactTextAsync(List<IntakeTask> tasks)    {
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
