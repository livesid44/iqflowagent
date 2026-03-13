using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using System.Text.Json;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Writes structured audit log entries to the database.
/// One entry is written per PII scan and per external API call.
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Record the outcome of a PII/SPII scan that was performed before forwarding
    /// content to an external LLM.
    /// </summary>
    Task LogPiiScanAsync(
        string correlationId,
        string callSite,
        int tenantId,
        int? intakeRecordId,
        PiiScanResult scanResult,
        string? userId = null);

    /// <summary>
    /// Record the outcome of an external LLM / API call.
    /// </summary>
    Task LogExternalCallAsync(
        string correlationId,
        string callSite,
        string eventType,
        int tenantId,
        int? intakeRecordId,
        string? requestUrl,
        int? httpStatusCode,
        long durationMs,
        bool isMocked,
        string outcome,
        string? errorMessage = null,
        string? userId = null);
}

public sealed class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ApplicationDbContext db, ILogger<AuditLogService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task LogPiiScanAsync(
        string correlationId,
        string callSite,
        int tenantId,
        int? intakeRecordId,
        PiiScanResult scanResult,
        string? userId = null)
    {
        try
        {
            string? findingsJson = null;
            if (scanResult.HasPii)
            {
                // Partially redact matched text before persisting — show type but mask value.
                var safe = scanResult.Findings.Select(f => new
                {
                    entityType  = f.EntityType,
                    matchedText = RedactForStorage(f.MatchedText)
                });
                findingsJson = JsonSerializer.Serialize(safe);
            }

            var entry = new AuditLog
            {
                CorrelationId    = correlationId,
                EventType        = "PiiScan",
                CallSite         = callSite,
                TenantId         = tenantId,
                IntakeRecordId   = intakeRecordId,
                PiiScanStatus    = scanResult.ShouldBlock ? "Blocked" :
                                   scanResult.HasPii      ? "Fail"    : "Pass",
                PiiFindingsJson  = findingsJson,
                PiiFindingCount  = scanResult.Findings.Count,
                WasBlocked       = scanResult.ShouldBlock,
                Outcome          = scanResult.ShouldBlock ? "Blocked" :
                                   scanResult.HasPii      ? "Redacted" : "Clean",
                UserId           = userId,
                CreatedAt        = DateTime.UtcNow
            };

            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write PII scan audit log for correlation {CorrelationId}", correlationId);
        }
    }

    public async Task LogExternalCallAsync(
        string correlationId,
        string callSite,
        string eventType,
        int tenantId,
        int? intakeRecordId,
        string? requestUrl,
        int? httpStatusCode,
        long durationMs,
        bool isMocked,
        string outcome,
        string? errorMessage = null,
        string? userId = null)
    {
        try
        {
            var entry = new AuditLog
            {
                CorrelationId  = correlationId,
                EventType      = eventType,
                CallSite       = callSite,
                TenantId       = tenantId,
                IntakeRecordId = intakeRecordId,
                PiiScanStatus  = "Skipped",
                RequestUrl     = SanitizeUrl(requestUrl),
                HttpStatusCode = httpStatusCode,
                DurationMs     = durationMs,
                IsMocked       = isMocked,
                Outcome        = outcome,
                ErrorMessage   = errorMessage,
                UserId         = userId,
                CreatedAt      = DateTime.UtcNow
            };

            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write external call audit log for correlation {CorrelationId}", correlationId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Show the first 3 and last 2 characters of the matched value, masked in the middle.
    /// "john.smith@example.com" → "joh***om"
    /// </summary>
    private static string RedactForStorage(string value)
    {
        if (string.IsNullOrEmpty(value)) return "[redacted]";
        if (value.Length <= 6) return new string('*', value.Length);
        return value[..3] + "***" + value[^2..];
    }

    /// <summary>Strip SAS tokens and API keys from URLs before persisting.</summary>
    private static string? SanitizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var uri = new Uri(url);
            // Return scheme + host + path only
            return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        }
        catch
        {
            return url.Length > 200 ? url[..200] + "…" : url;
        }
    }
}
