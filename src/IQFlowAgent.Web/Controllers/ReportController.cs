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
    private readonly ILogger<ReportController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly ITenantContextService _tenantContext;

    private const string TemplateName = "BARTOK_S8_SOP_Template_v2.docx";
    private const int MaxArtifactCharsPerFile = 1500;

    private static string GenerateTaskId() =>
        "TSK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpper();

    public ReportController(
        ApplicationDbContext db,
        IDocxReportService docxService,
        IAzureOpenAiService aiService,
        IBlobStorageService blobService,
        ILogger<ReportController> logger,
        IWebHostEnvironment env,
        ITenantContextService tenantContext)
    {
        _db = db;
        _docxService = docxService;
        _aiService = aiService;
        _blobService = blobService;
        _logger = logger;
        _env = env;
        _tenantContext = tenantContext;
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

        // Aggregate artifact text from tasks
        var tasks = await _db.IntakeTasks
            .Where(t => t.IntakeRecordId == intakeId)
            .Include(t => t.Documents)
            .ToListAsync();
        var artifactText = await AggregateArtifactTextAsync(tasks);

        // Build field definitions JSON for the AI prompt
        var fieldDefsJson = JsonSerializer.Serialize(fieldDefs.Select(f => new
        {
            key        = f.Key,
            label      = f.Label,
            section    = f.Section,
            autoSource = f.AutoSource
        }));

        var aiJson = await _aiService.AnalyzeReportFieldsAsync(
            intake, fieldDefsJson, intake.AnalysisResult, artifactText);

        // Parse AI response and upsert field statuses
        var now = DateTime.UtcNow;
        var existingStatuses = await _db.ReportFieldStatuses
            .Where(r => r.IntakeRecordId == intakeId)
            .ToListAsync();

        try
        {
            using var doc = JsonDocument.Parse(aiJson);
            if (!doc.RootElement.TryGetProperty("fields", out var fieldsEl))
                throw new JsonException("Missing 'fields' property");

            var aiResults = fieldsEl.EnumerateArray()
                .Select(el => new
                {
                    Key       = el.TryGetProperty("key",       out var k) ? k.GetString() ?? "" : "",
                    Status    = el.TryGetProperty("status",    out var s) ? s.GetString() ?? "Missing" : "Missing",
                    FillValue = el.TryGetProperty("fillValue", out var v) ? v.GetString() ?? "" : "",
                    Notes     = el.TryGetProperty("notes",     out var n) ? n.GetString() ?? "" : ""
                })
                .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var fd in fieldDefs)
            {
                var existing = existingStatuses.FirstOrDefault(s => s.FieldKey == fd.Key);
                aiResults.TryGetValue(fd.Key, out var aiResult);

                if (existing == null)
                {
                    // Create new
                    var newStatus = new ReportFieldStatus
                    {
                        IntakeRecordId      = intakeId,
                        FieldKey            = fd.Key,
                        FieldLabel          = fd.Label,
                        Section             = fd.Section,
                        TemplatePlaceholder = fd.TemplatePlaceholder,
                        Status              = aiResult?.Status ?? "Missing",
                        FillValue           = aiResult?.FillValue,
                        Notes               = aiResult?.Notes,
                        AnalyzedAt          = now,
                        UpdatedAt           = now
                    };
                    _db.ReportFieldStatuses.Add(newStatus);
                }
                else
                {
                    // Always refresh metadata so stale DB copies (written by an older code version
                    // with different placeholder text) are updated and generation works correctly.
                    existing.TemplatePlaceholder = fd.TemplatePlaceholder;
                    existing.FieldLabel          = fd.Label;
                    existing.Section             = fd.Section;
                    existing.UpdatedAt           = now;

                    // Always apply LLM results so every re-analysis refreshes the entire document.
                    // NA is the only status that represents a deliberate user decision — preserve it.
                    if (existing.Status != "NA")
                    {
                        existing.Status     = aiResult?.Status ?? existing.Status;
                        existing.FillValue  = aiResult?.FillValue ?? existing.FillValue;
                        existing.Notes      = aiResult?.Notes ?? existing.Notes;
                        existing.AnalyzedAt = now;
                    }
                }
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "AI field analysis complete. Review and adjust values below.";
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI field analysis for intake {IntakeId}", intake.IntakeId);
            TempData["Error"] = "AI analysis returned an unexpected response. Please try again.";
        }

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
        var textExtensions = new[] { ".txt", ".csv", ".json", ".xml", ".md" };

        foreach (var task in tasks)
        {
            foreach (var doc in task.Documents.Where(d => d.DocumentType == "TaskArtifact"))
            {
                var ext = Path.GetExtension(doc.FileName ?? "").ToLowerInvariant();
                if (!textExtensions.Contains(ext)) continue;

                try
                {
                    string? content = null;
                    if (await _blobService.IsConfiguredAsync() && doc.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        content = await _blobService.DownloadTextAsync(doc.FilePath);
                    else
                    {
                        var fullPath = Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/'));
                        if (System.IO.File.Exists(fullPath))
                            content = await System.IO.File.ReadAllTextAsync(fullPath);
                    }

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        sb.AppendLine($"--- {doc.FileName} (Task: {task.TaskId}) ---");
                        sb.AppendLine(content.Length > MaxArtifactCharsPerFile ? content[..MaxArtifactCharsPerFile] + "[...truncated]" : content);
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
}
