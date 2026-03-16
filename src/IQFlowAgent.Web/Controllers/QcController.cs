using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using IQFlowAgent.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace IQFlowAgent.Web.Controllers;

[Authorize]
public class QcController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IAzureOpenAiService  _ai;
    private readonly IBlobStorageService  _blob;
    private readonly IWebHostEnvironment  _env;
    private readonly ILogger<QcController> _logger;
    private readonly ITenantContextService _tenantContext;

    private const int MaxDocTextChars = 8000;

    public QcController(
        ApplicationDbContext db,
        IAzureOpenAiService ai,
        IBlobStorageService blob,
        IWebHostEnvironment env,
        ILogger<QcController> logger,
        ITenantContextService tenantContext)
    {
        _db     = db;
        _ai     = ai;
        _blob   = blob;
        _env    = env;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    // GET /Qc/Index — list closed intakes with Run QC button and latest QC score
    public async Task<IActionResult> Index()
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var intakes = await _db.IntakeRecords
            .Where(i => i.Status == "Closed" && i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        var intakeIds  = intakes.Select(i => i.Id).ToList();

        // Load latest QC check per intake
        var latestQcs  = await _db.QcChecks
            .Where(q => intakeIds.Contains(q.IntakeRecordId))
            .GroupBy(q => q.IntakeRecordId)
            .Select(g => g.OrderByDescending(q => q.CreatedAt).First())
            .ToListAsync();

        // Count open (non-NA) tasks per intake so the view can show a readiness hint
        var openTaskCounts = await _db.IntakeTasks
            .Where(t => intakeIds.Contains(t.IntakeRecordId)
                        && !t.IsNotApplicable
                        && (t.Status == "Open" || t.Status == "In Progress"))
            .GroupBy(t => t.IntakeRecordId)
            .Select(g => new { IntakeRecordId = g.Key, Count = g.Count() })
            .ToListAsync();

        ViewBag.LatestQcs       = latestQcs.ToDictionary(q => q.IntakeRecordId);
        ViewBag.OpenTaskCounts  = openTaskCounts.ToDictionary(x => x.IntakeRecordId, x => x.Count);
        return View(intakes);
    }

    // POST /Qc/RunQc/{intakeId} — triggers QC check (synchronous for simplicity)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunQc(int intakeId)
    {
        var intake = await _db.IntakeRecords.FindAsync(intakeId);
        if (intake == null) return NotFound();

        if (intake.Status != "Closed")
        {
            TempData["Error"] = "QC check can only be run on closed intakes.";
            return RedirectToAction(nameof(Index));
        }

        // Block QC if there are open (non-NA) tasks — they must be resolved first
        var openTaskCount = await _db.IntakeTasks
            .CountAsync(t => t.IntakeRecordId == intakeId
                             && !t.IsNotApplicable
                             && (t.Status == "Open" || t.Status == "In Progress"));
        if (openTaskCount > 0)
        {
            TempData["Error"] = $"QC cannot be run: {openTaskCount} task(s) are still open for {intake.IntakeId}. " +
                                "Close or mark all tasks as N/A before running QC.";
            return RedirectToAction(nameof(Index));
        }

        // Block QC if AI analysis has not been completed yet
        if (string.IsNullOrWhiteSpace(intake.AnalysisResult))
        {
            TempData["Error"] = $"QC cannot be run: AI analysis has not been completed for {intake.IntakeId}. " +
                                "Please run the intake analysis first.";
            return RedirectToAction(nameof(Index));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Create a pending QC record
        var qc = new QcCheck
        {
            IntakeRecordId = intake.Id,
            Status         = "Running",
            RunByUserId    = userId
        };
        _db.QcChecks.Add(qc);
        await _db.SaveChangesAsync();

        try
        {
            // Build tasks summary
            var tasks = await _db.IntakeTasks
                .Where(t => t.IntakeRecordId == intake.Id)
                .Include(t => t.ActionLogs)
                .ToListAsync();

            var tasksSummary = BuildTasksSummary(tasks);

            // Get document text if available
            string? documentText = null;
            if (!string.IsNullOrWhiteSpace(intake.UploadedFilePath))
            {
                try
                {
                    documentText = await _blob.DownloadTextAsync(intake.UploadedFilePath);
                    if (string.IsNullOrWhiteSpace(documentText) && !intake.UploadedFilePath.StartsWith("http"))
                    {
                        // Validate that the resolved path stays within wwwroot/uploads to prevent path traversal
                        var uploadsRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads"));
                        var localPath   = Path.GetFullPath(Path.Combine(_env.WebRootPath, intake.UploadedFilePath.TrimStart('/')));
                        if (localPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase)
                            && System.IO.File.Exists(localPath))
                        {
                            documentText = await System.IO.File.ReadAllTextAsync(localPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read document for QC check {IntakeId}", intake.IntakeId);
                }
            }

            // Run AI QC
            var resultJson = await _ai.RunQcCheckAsync(
                intake,
                intake.AnalysisResult,
                tasksSummary,
                documentText);

            // Parse overall score
            int overallScore = 0;
            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                if (doc.RootElement.TryGetProperty("overallScore", out var scoreEl))
                    overallScore = scoreEl.GetInt32();
            }
            catch { /* keep 0 */ }

            qc.Status            = "Complete";
            qc.OverallScore      = overallScore;
            qc.ScoreBreakdownJson = resultJson;
            qc.CompletedAt       = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"QC check completed for {intake.IntakeId} — Overall Score: {overallScore}/100.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QC check failed for intake {IntakeId}", intake.IntakeId);
            qc.Status       = "Error";
            qc.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync();
            TempData["Error"] = "QC check encountered an error. Please try again.";
        }

        return RedirectToAction(nameof(Index));
    }

    // GET /Qc/Result/{qcId} — detailed QC result view
    public async Task<IActionResult> Result(int id)
    {
        var qc = await _db.QcChecks
            .Include(q => q.IntakeRecord)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (qc == null) return NotFound();
        return View(qc);
    }

    // GET /Qc/Dashboard — QC score summary across all intakes
    public async Task<IActionResult> Dashboard()
    {
        // Load all completed QC checks with their intake
        var qcs = await _db.QcChecks
            .Include(q => q.IntakeRecord)
            .Where(q => q.Status == "Complete")
            .OrderByDescending(q => q.CompletedAt)
            .ToListAsync();

        // Keep only the latest QC per intake
        var latestQcs = qcs
            .GroupBy(q => q.IntakeRecordId)
            .Select(g => g.First())
            .OrderByDescending(q => q.OverallScore)
            .ToList();

        ViewBag.TotalChecked   = latestQcs.Count;
        ViewBag.AvgScore       = latestQcs.Count > 0 ? (int)latestQcs.Average(q => q.OverallScore) : 0;
        ViewBag.HighScoreCount = latestQcs.Count(q => q.OverallScore >= 80);
        ViewBag.LowScoreCount  = latestQcs.Count(q => q.OverallScore < 60);
        ViewBag.AllQcs         = qcs.Take(50).ToList(); // recent 50 runs

        return View(latestQcs);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildTasksSummary(IList<IntakeTask> tasks)
    {
        if (!tasks.Any()) return "No tasks created for this intake.";

        var sb = new System.Text.StringBuilder();
        foreach (var t in tasks)
        {
            var statusLabel = t.IsNotApplicable ? "N/A" : t.Status;
            sb.AppendLine($"- [{statusLabel}] {t.Title} (Priority: {t.Priority})");
            var lastComment = t.ActionLogs?
                .Where(l => l.ActionType == "Comment")
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefault();
            if (lastComment != null)
                sb.AppendLine($"  Last comment: {lastComment.Comment}");
        }
        return sb.ToString();
    }
}
