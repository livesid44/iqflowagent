using System.Text;
using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Hubs;
using IQFlowAgent.Web.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Background service that processes RAG jobs: reads uploaded files, transcribes audio/video,
/// aggregates document text, runs AI analysis, and notifies the user via SignalR when done.
/// </summary>
public class RagProcessorService : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IServiceProvider    _services;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<RagProcessorService> _logger;
    private readonly IWebHostEnvironment _env;

    // File extension categories
    private static readonly HashSet<string> AudioVideoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".mp4", ".wav", ".m4a", ".ogg", ".webm", ".mkv", ".avi", ".mov" };

    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".csv", ".json", ".xml", ".md", ".log" };

    private static readonly HashSet<string> OpenXmlExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".docx", ".xlsx" };

    private static readonly HashSet<string> OcrExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".pptx", ".jpg", ".jpeg", ".png", ".tiff", ".bmp" };

    public RagProcessorService(
        IBackgroundJobQueue queue,
        IServiceProvider services,
        IHubContext<NotificationHub> hub,
        ILogger<RagProcessorService> logger,
        IWebHostEnvironment env)
    {
        _queue    = queue;
        _services = services;
        _hub      = hub;
        _logger   = logger;
        _env      = env;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Outermost try-catch: guarantees that no exception can ever escape ExecuteAsync
        // and propagate back to the host.  BackgroundServiceExceptionBehavior.Ignore is
        // also set in Program.cs, but keeping this method self-contained is best practice
        // and covers the rare case of a synchronous throw before the first await (e.g.
        // if the logger itself fails at startup).
        try
        {
            _logger.LogInformation("RagProcessorService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                int ragJobId;
                try
                {
                    ragJobId = await _queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown — exit the loop cleanly.
                    break;
                }
                catch (Exception ex)
                {
                    // Unexpected error from the queue (should not happen, but guard anyway).
                    _logger.LogError(ex, "Unexpected error dequeueing RAG job — retrying after delay.");
                    try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                    catch (OperationCanceledException) { break; } // Host is shutting down — exit cleanly.
                    continue;
                }

                try
                {
                    await ProcessRagJobAsync(ragJobId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error processing RAG job {JobId}.", ragJobId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown via CancellationToken — normal, not an error.
        }
        catch (Exception ex)
        {
            // Should never reach here given the inner guards, but log if it does.
            _logger.LogCritical(ex, "RagProcessorService encountered an unrecoverable error and has exited.");
        }

        _logger.LogInformation("RagProcessorService stopping.");
    }

    // ─── Process one RAG job ──────────────────────────────────────────────────

    private async Task ProcessRagJobAsync(int ragJobId, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var aiService   = scope.ServiceProvider.GetRequiredService<IAzureOpenAiService>();
        var speechSvc   = scope.ServiceProvider.GetRequiredService<IAzureSpeechService>();
        var blobSvc     = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();

        var docIntel  = scope.ServiceProvider.GetRequiredService<IDocumentIntelligenceService>();
        var embedSvc  = scope.ServiceProvider.GetRequiredService<IAzureEmbeddingService>();
        var searchSvc = scope.ServiceProvider.GetRequiredService<IAzureSearchService>();

        if (searchSvc.IsConfigured())
            await searchSvc.EnsureIndexExistsAsync(ct);

        var job = await db.RagJobs
            .Include(j => j.IntakeRecord)
            .FirstOrDefaultAsync(j => j.Id == ragJobId, ct);

        if (job is null)
        {
            _logger.LogWarning("RAG job {JobId} not found — skipping.", ragJobId);
            return;
        }

        job.Status    = "Processing";
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var intake = job.IntakeRecord;
        _logger.LogInformation("Processing RAG job {JobId} for intake {IntakeId}.", ragJobId, intake.IntakeId);

        try
        {
            // Load all documents for this intake
            var docs = await db.IntakeDocuments
                .Where(d => d.IntakeRecordId == intake.Id && d.DocumentType == "IntakeDocument")
                .ToListAsync(ct);

            job.TotalFiles = docs.Count;
            await db.SaveChangesAsync(ct);

            var aggregatedText = new StringBuilder();

            foreach (var doc in docs)
            {
                ct.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(doc.FileName).ToLowerInvariant();

                if (AudioVideoExtensions.Contains(ext))
                {
                    // Ensure audio/video is in blob storage — Azure Batch Transcription requires
                    // a publicly accessible HTTPS URL. Files saved to local disk during intake
                    // submission are uploaded here (deferred from the HTTP POST handler).
                    await EnsureAudioInBlobAsync(doc, intake, db, blobSvc, ct);

                    // ── Transcribe audio/video ─────────────────────────────
                    await TranscribeDocumentAsync(doc, intake, db, speechSvc, aiService, blobSvc, aggregatedText, ct);
                }
                else
                {
                    // ── Upload local file to per-intake blob folder ────────
                    // When Blob Storage is configured and the document is still on
                    // local disk, migrate it to blob now (deferred from the HTTP POST
                    // that originally saved it locally to keep the response fast).
                    await EnsureDocumentInBlobAsync(doc, intake, db, blobSvc, ct);

                    // ── Read text content for RAG ──────────────────────────
                    var text = await ReadDocumentTextAsync(doc, blobSvc, docIntel, ct);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        aggregatedText.AppendLine($"=== File: {doc.FileName} ===");
                        aggregatedText.AppendLine(text);
                        aggregatedText.AppendLine();

                        // ── Chunk + Embed + Index into Azure AI Search ─────
                        if (searchSvc.IsConfigured() && embedSvc.IsConfigured())
                        {
                            var chunks = RagDocumentChunker.Chunk(text);
                            var chunkRecords = new List<DocumentChunkRecord>();
                            for (int i = 0; i < chunks.Count; i++)
                            {
                                var embedding = await embedSvc.GetEmbeddingAsync(chunks[i], ct) ?? Array.Empty<float>();
                                chunkRecords.Add(new DocumentChunkRecord(
                                    Id            : $"{intake.Id}-{doc.Id}-{i}",
                                    IntakeDbId    : intake.Id,
                                    IntakePublicId: intake.IntakeId,
                                    TenantId      : intake.TenantId,
                                    FolderPath    : $"{intake.TenantId}/{intake.IntakeId}/documents",
                                    DocumentName  : doc.FileName,
                                    Content       : chunks[i],
                                    ChunkIndex    : i,
                                    Embedding     : embedding
                                ));
                            }
                            await searchSvc.IndexChunksAsync(chunkRecords, ct);
                            _logger.LogInformation("Indexed {Count} chunks from {FileName} into Azure AI Search.", chunks.Count, doc.FileName);
                        }
                    }
                }

                job.ProcessedFiles++;
                await db.SaveChangesAsync(ct);
            }

            // ── Run AI analysis on all aggregated text ─────────────────────
            var combinedText = aggregatedText.ToString();
            intake.Status = "Analyzing";
            await db.SaveChangesAsync(ct);

            var analysisJson = await aiService.AnalyzeIntakeAsync(intake, combinedText);

            intake.AnalysisResult = analysisJson;
            intake.AnalyzedAt     = DateTime.UtcNow;
            intake.Status         = "Complete";

            // Persist any PII/SPII values that were masked before the LLM call.
            var piiFindings = aiService.GetLastPiiFindings();
            if (piiFindings.Count > 0)
                intake.PiiMaskingLog = System.Text.Json.JsonSerializer.Serialize(piiFindings);

            job.Status      = "Complete";
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // ── Auto-create tasks from analysis ───────────────────────────
            await AutoCreateTasksAsync(intake, analysisJson, db, _logger, ct);

            _logger.LogInformation("RAG job {JobId} complete for intake {IntakeId}.", ragJobId, intake.IntakeId);

            // ── Notify user via SignalR ────────────────────────────────────
            if (!string.IsNullOrEmpty(job.NotifyUserId))
            {
                await _hub.Clients.Group($"user-{job.NotifyUserId}").SendAsync(
                    "AnalysisReady",
                    new
                    {
                        intakeId     = intake.IntakeId,
                        intakeDbId   = intake.Id,
                        processName  = intake.ProcessName,
                        filesProcessed = job.ProcessedFiles
                    },
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG job {JobId} failed for intake {IntakeId}.", ragJobId, intake.IntakeId);

            job.Status       = "Error";
            job.ErrorMessage = ex.Message;
            job.CompletedAt  = DateTime.UtcNow;
            intake.Status    = "Error";
            await db.SaveChangesAsync(CancellationToken.None); // don't pass cancelled token

            if (!string.IsNullOrEmpty(job.NotifyUserId))
            {
                await _hub.Clients.Group($"user-{job.NotifyUserId}").SendAsync(
                    "AnalysisFailed",
                    new { intakeId = intake.IntakeId, error = ex.Message },
                    CancellationToken.None);
            }
        }
    }

    // ─── Transcribe audio/video document ─────────────────────────────────────

    private async Task TranscribeDocumentAsync(
        IntakeDocument doc,
        IntakeRecord intake,
        ApplicationDbContext db,
        IAzureSpeechService speechSvc,
        IAzureOpenAiService aiService,
        IBlobStorageService blobSvc,
        StringBuilder aggregatedText,
        CancellationToken ct)
    {
        doc.TranscriptStatus = "Processing";
        await db.SaveChangesAsync(ct);

        try
        {
            var transcript = await speechSvc.TranscribeAsync(doc.FilePath, intake.TenantId, doc.FileName);
            doc.TranscriptText   = transcript;
            doc.TranscriptStatus = "Complete";
            await db.SaveChangesAsync(ct);

            // Generate SOP from transcript and convert to PDF
            var sopMarkdown = await aiService.GenerateSopFromTranscriptAsync(transcript, intake);

            // Convert the SOP markdown to a PDF — sanitize source filename for safe path
            var safeSourceName = System.Text.RegularExpressions.Regex.Replace(
                Path.GetFileNameWithoutExtension(doc.FileName), @"[^a-zA-Z0-9\-_]", "_");
            var sopFileName = $"{intake.IntakeId}-SOP-{safeSourceName}.pdf";
            var sopBytes    = SopPdfRenderer.Render(sopMarkdown, intake.ProcessName);

            string sopPath;
            if (await blobSvc.IsConfiguredAsync())
            {
                var sopFolderPath = $"{intake.TenantId}/{intake.IntakeId}/sop";
                sopPath = await blobSvc.UploadToFolderAsync(
                    new MemoryStream(sopBytes),
                    sopFolderPath,
                    sopFileName,
                    "application/pdf");
            }
            else
            {
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
                Directory.CreateDirectory(uploadsDir);
                sopPath = Path.Combine(uploadsDir, sopFileName);
                await File.WriteAllBytesAsync(sopPath, sopBytes, ct);
                sopPath = $"/uploads/{sopFileName}";
            }

            doc.SopDocumentPath = sopPath;
            await db.SaveChangesAsync(ct);

            // Add SOP PDF as a separate document record attached to the intake
            db.IntakeDocuments.Add(new IntakeDocument
            {
                IntakeRecordId   = intake.Id,
                FileName         = sopFileName,
                FilePath         = sopPath,
                ContentType      = "application/pdf",
                FileSize         = sopBytes.Length,
                DocumentType     = "SopDocument",
                UploadedAt       = DateTime.UtcNow,
                TranscriptStatus = "NA"
            });
            await db.SaveChangesAsync(ct);

            // Include transcript in aggregated text for AI analysis
            aggregatedText.AppendLine($"=== Transcript: {doc.FileName} ===");
            aggregatedText.AppendLine(transcript);
            aggregatedText.AppendLine();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed for document {DocId} ({FileName}).", doc.Id, doc.FileName);
            doc.TranscriptStatus = "Error";
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    // ─── Read text from document ──────────────────────────────────────────────

    private async Task<string?> ReadDocumentTextAsync(
        IntakeDocument doc, IBlobStorageService blobSvc,
        IDocumentIntelligenceService docIntel, CancellationToken ct)
    {
        var ext = Path.GetExtension(doc.FileName).ToLowerInvariant();

        try
        {
            if (TextExtensions.Contains(ext))
            {
                // Plain text: read directly
                if (doc.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return await blobSvc.DownloadTextAsync(doc.FilePath);

                var localPath = doc.FilePath.StartsWith('/')
                    ? Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
                    : doc.FilePath;

                var uploadsRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads"));
                var fullPath    = Path.GetFullPath(localPath);
                if (!fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Document path '{Path}' is outside uploads root — skipping.", doc.FilePath);
                    return null;
                }
                return File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath, ct) : null;
            }

            if (OpenXmlExtensions.Contains(ext))
            {
                var bytes = await DownloadBytesAsync(doc, blobSvc, ct);
                if (bytes == null) return null;
                return DocumentTextExtractor.Extract(bytes, ext);
            }

            if (OcrExtensions.Contains(ext))
            {
                var bytes = await DownloadBytesAsync(doc, blobSvc, ct);
                if (bytes == null) return null;
                return await docIntel.ExtractTextAsync(bytes, doc.FileName);
            }

            _logger.LogDebug("Skipping unsupported extension '{Ext}' for {FileName}.", ext, doc.FileName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read text from document {DocId} ({FileName}).", doc.Id, doc.FileName);
            return null;
        }
    }

    /// <summary>Downloads document bytes either from blob URL or local disk.</summary>
    private async Task<byte[]?> DownloadBytesAsync(
        IntakeDocument doc, IBlobStorageService blobSvc, CancellationToken ct)
    {
        if (doc.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return await blobSvc.DownloadBytesAsync(doc.FilePath);

        var localPath = doc.FilePath.StartsWith('/')
            ? Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
            : doc.FilePath;

        var uploadsRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads"));
        var fullPath    = Path.GetFullPath(localPath);
        if (!fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Document path '{Path}' is outside uploads root — skipping.", doc.FilePath);
            return null;
        }
        return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath, ct) : null;
    }

    // ─── Auto-create tasks from analysis ─────────────────────────────────────

    private static async Task AutoCreateTasksAsync(
        IntakeRecord intake, string analysisJson,
        ApplicationDbContext db, ILogger logger, CancellationToken ct)
    {
        try
        {
            using var doc        = System.Text.Json.JsonDocument.Parse(analysisJson);
            var existingTitles   = await db.IntakeTasks
                .Where(t => t.IntakeRecordId == intake.Id)
                .Select(t => t.Title)
                .ToListAsync(ct);
            var titleSet = existingTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Build the set of BARTOK sections already covered by a Fail/Warning checkpoint.
            // Checkpoint tasks take priority — action items for the same section are skipped
            // so that exactly one task is created per section.
            var checkpointSections = GetJsonArraySafe(doc.RootElement, "checkPoints")
                .Where(IsFailOrWarn)
                .Select(cp => GetStringProp(cp, "label") ?? "")
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Combine checkpoints (Fail/Warning) with action items that are NOT covered by a
            // checkpoint, so that the checkpoint version is the single authoritative task.
            var items = GetJsonArraySafe(doc.RootElement, "checkPoints")
                .Where(IsFailOrWarn)
                .Concat(GetJsonArraySafe(doc.RootElement, "actionItems")
                    .Where(item =>
                    {
                        var bs = item.TryGetProperty("bartokSection", out var bsEl) ? bsEl.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(bs)) return true;
                        // Skip if any checkpoint label contains (or is contained by) the bartokSection value
                        return !checkpointSections.Any(lbl =>
                            lbl.Contains(bs, StringComparison.OrdinalIgnoreCase) ||
                            bs.Contains(lbl, StringComparison.OrdinalIgnoreCase));
                    }))
                .ToList();

            foreach (var item in items)
            {
                var title     = GetStringProp(item, "title", "label") ?? "Task";
                var priority  = GetStringProp(item, "priority") ?? (IsCheckPoint(item) ? "High" : "Medium");
                var desc      = GetStringProp(item, "description", "note") ?? string.Empty;

                // For checkpoint tasks include the sectionId (e.g. "5") in the title so the
                // task board immediately shows which BARTOK section needs attention.
                string fullTitle;
                if (IsCheckPoint(item))
                {
                    var sectionId = GetStringProp(item, "sectionId") ?? string.Empty;
                    fullTitle = string.IsNullOrWhiteSpace(sectionId)
                        ? $"[Checkpoint] {title}"
                        : $"[Checkpoint][{sectionId}] {title}";
                }
                else
                {
                    fullTitle = title;
                }

                // Guard against duplicates — also check the legacy format (no sectionId)
                // for checkpoints that were created before sectionId was introduced.
                // Additionally guard by sectionId prefix so that re-runs where the AI
                // produces a slightly different label wording do not create duplicate tasks.
                var cpSectionIdForGuard = IsCheckPoint(item) ? (GetStringProp(item, "sectionId") ?? "") : "";
                var cpSectionIdPrefix   = string.IsNullOrWhiteSpace(cpSectionIdForGuard)
                    ? null
                    : $"[Checkpoint][{cpSectionIdForGuard}]";

                if (titleSet.Contains(fullTitle) ||
                    (IsCheckPoint(item) && titleSet.Contains($"[Checkpoint] {title}")) ||
                    (cpSectionIdPrefix != null && titleSet.Any(t => t.StartsWith(cpSectionIdPrefix, StringComparison.OrdinalIgnoreCase))))
                    continue;
                titleSet.Add(fullTitle);

                // Map each task to its BARTOK output-document section for traceability.
                // Checkpoint items use their "label" as the section name.
                // Action items use "bartokSection" when present.
                if (IsCheckPoint(item))
                {
                    var sectionName  = GetStringProp(item, "label") ?? "";
                    var sectionId    = GetStringProp(item, "sectionId") ?? "";
                    var requiredInfo = GetStringProp(item, "note") ?? "";
                    if (!string.IsNullOrWhiteSpace(sectionName))
                    {
                        desc = desc.TrimEnd();
                        desc += $"\n\n📄 BARTOK S8 SOP — Output Document Section"
                              + $"\nSection ID: {(string.IsNullOrWhiteSpace(sectionId) ? "—" : sectionId)}"
                              + $"\nSection   : {sectionName}"
                              + $"\nRequired  : {(string.IsNullOrWhiteSpace(requiredInfo) ? "See checkpoint status above." : requiredInfo)}";
                    }
                }
                else if (item.TryGetProperty("bartokSection", out var bsSec))
                {
                    var sectionName  = bsSec.GetString() ?? "";
                    var requiredInfo = item.TryGetProperty("requiredInfo", out var riEl) ? riEl.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(sectionName))
                    {
                        desc = desc.TrimEnd();
                        desc += $"\n\n📄 BARTOK S8 SOP — Output Document Section\nSection : {sectionName}\nRequired: {(string.IsNullOrWhiteSpace(requiredInfo) ? "See task description above." : requiredInfo)}";
                    }
                }

                var task = new IntakeTask
                {
                    TaskId         = "TSK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
                    IntakeRecordId = intake.Id,
                    Title          = fullTitle,
                    Description    = desc,
                    Status         = "Open",
                    Priority       = NormalisePriority(priority),
                    Owner          = intake.ProcessOwnerEmail,
                    DueDate        = DateTime.UtcNow.AddHours(48),
                    CreatedAt      = DateTime.UtcNow
                };
                db.IntakeTasks.Add(task);
                await db.SaveChangesAsync(ct);

                db.TaskActionLogs.Add(new TaskActionLog
                {
                    IntakeTaskId   = task.Id,
                    ActionType     = "StatusChange",
                    NewStatus      = "Open",
                    Comment        = $"Task automatically created from AI analysis of intake {intake.IntakeId}",
                    CreatedAt      = DateTime.UtcNow,
                    CreatedByName  = "System"
                });
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AutoCreateTasks failed for intake {IntakeId} — tasks may not have been created.", intake.IntakeId);
        }
    }

    // ─── JSON helpers ─────────────────────────────────────────────────────────

    private static IEnumerable<System.Text.Json.JsonElement> GetJsonArraySafe(
        System.Text.Json.JsonElement root, string propName)
    {
        if (root.TryGetProperty(propName, out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            return arr.EnumerateArray();
        return [];
    }

    private static bool IsFailOrWarn(System.Text.Json.JsonElement el)
    {
        var status = GetStringProp(el, "status") ?? string.Empty;
        return status.Equals("Fail", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Warning", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCheckPoint(System.Text.Json.JsonElement el) =>
        el.TryGetProperty("status", out _); // checkpoints have a "status" field

    private static string? GetStringProp(System.Text.Json.JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                return v.GetString();
        return null;
    }

    private static string NormalisePriority(string raw) =>
        raw.ToLowerInvariant() switch
        {
            "high" => "High",
            "low"  => "Low",
            _      => "Medium"
        };

    // ─── Blob upload helpers ──────────────────────────────────────────────────

    /// <summary>
    /// If the document is stored at a local disk path and Azure Blob Storage is
    /// configured, upload it to the per-intake folder (<c>{tenantId}/{intakeId}/documents/</c>)
    /// and update <see cref="IntakeDocument.FilePath"/> to the blob URL so that
    /// all intake files are co-located under one folder in the storage account.
    /// </summary>
    private async Task EnsureDocumentInBlobAsync(
        IntakeDocument doc,
        IntakeRecord   intake,
        ApplicationDbContext db,
        IBlobStorageService blobSvc,
        CancellationToken ct)
    {
        // Already in blob storage — nothing to do.
        if (doc.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;

        if (!await blobSvc.IsConfiguredAsync()) return;

        try
        {
            var localPath = doc.FilePath.StartsWith('/')
                ? Path.Combine(_env.WebRootPath, doc.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
                : doc.FilePath;

            var uploadsRoot = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads"));
            var fullPath    = Path.GetFullPath(localPath);
            if (!fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath))
                return;

            var folderPath  = $"{intake.TenantId}/{intake.IntakeId}/documents";
            var contentType = doc.ContentType ?? "application/octet-stream";

            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var blobUrl = await blobSvc.UploadToFolderAsync(stream, folderPath, doc.FileName, contentType);

            doc.FilePath = blobUrl;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Uploaded {FileName} to blob folder {Folder} for intake {IntakeId}.",
                doc.FileName, folderPath, intake.IntakeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not upload {FileName} to blob for intake {IntakeId} — keeping local path.",
                doc.FileName, intake.IntakeId);
        }
    }

    /// <summary>
    /// If the audio/video document is stored at a local disk path and blob storage is
    /// configured, upload it to blob now and update the stored path.  Azure Batch
    /// Transcription requires a publicly accessible HTTPS URL, so this step must happen
    /// before <see cref="TranscribeDocumentAsync"/> is called.
    /// </summary>
    private async Task EnsureAudioInBlobAsync(
        IntakeDocument doc,
        IntakeRecord   intake,
        ApplicationDbContext db,
        IBlobStorageService blobSvc,
        CancellationToken ct)
    {
        // Already a blob URL — nothing to do.
        if (doc.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;

        // Blob not configured — transcription will fall back to mock, no upload needed.
        if (!await blobSvc.IsConfiguredAsync())
            return;

        var physicalPath = MapToPhysicalPath(doc.FilePath);
        if (!File.Exists(physicalPath))
        {
            _logger.LogWarning("Audio file not found on disk for upload to blob: {Path}", physicalPath);
            return;
        }

        try
        {
            using var fs = File.OpenRead(physicalPath);
            var folderPath = $"{intake.TenantId}/{intake.IntakeId}/audio";
            var blobUrl = await blobSvc.UploadToFolderAsync(
                fs,
                folderPath,
                doc.FileName,
                doc.ContentType ?? "application/octet-stream");

            _logger.LogInformation("Uploaded audio/video {FileName} to blob: {Url}", doc.FileName, blobUrl);

            // Update path references in the DB so downstream code (speech service) uses the blob URL.
            if (string.Equals(intake.UploadedFilePath, doc.FilePath, StringComparison.OrdinalIgnoreCase))
                intake.UploadedFilePath = blobUrl;

            doc.FilePath = blobUrl;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload audio/video {FileName} to blob — transcription will use mock.", doc.FileName);
        }
    }

    /// <summary>Maps a virtual path like <c>/uploads/foo.mp4</c> to its absolute physical path.
    /// If the path already exists verbatim on disk it is treated as an absolute system path.</summary>
    private string MapToPhysicalPath(string filePath)
    {
        // If the file exists at the given path verbatim, it is already a physical path.
        if (Path.IsPathRooted(filePath) && File.Exists(filePath))
            return filePath;

        // Virtual web path like /uploads/filename → map relative to wwwroot.
        return Path.Combine(_env.WebRootPath,
            filePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    }
}
