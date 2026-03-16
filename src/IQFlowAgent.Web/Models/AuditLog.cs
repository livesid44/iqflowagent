namespace IQFlowAgent.Web.Models;

/// <summary>
/// Persisted record of every external API call made by the application and every
/// PII/SPII scan event that precedes an LLM call.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }

    /// <summary>Multi-tenant owner.</summary>
    public int TenantId { get; set; }

    /// <summary>The intake this log entry relates to (nullable for non-intake calls).</summary>
    public int? IntakeRecordId { get; set; }

    // ── Identity ────────────────────────────────────────────────────────────

    /// <summary>Unique correlation ID (GUID) that ties a PII scan + LLM call together.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Category of event.
    /// Values: "LlmCall" | "PiiScan" | "BlobStorage" | "SpeechApi" | "Other"
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Logical operation within the service (e.g. "AnalyzeIntake", "AnalyzeReportFields",
    /// "RunQcCheck", "GenerateSop", "VerifyIntakeClosure").
    /// </summary>
    public string CallSite { get; set; } = string.Empty;

    // ── PII scan ────────────────────────────────────────────────────────────

    /// <summary>"Pass" | "Fail" | "Skipped"</summary>
    public string PiiScanStatus { get; set; } = "Skipped";

    /// <summary>
    /// JSON array of PiiFinding objects when status is "Fail", null otherwise.
    /// [{"entityType":"Email Address","matchedText":"j...@x.com"}, …]
    /// The matched text is partially redacted before storage so as not to persist raw PII.
    /// </summary>
    public string? PiiFindingsJson { get; set; }

    /// <summary>Number of PII/SPII entities found (0 = clean).</summary>
    public int PiiFindingCount { get; set; }

    /// <summary>Whether the call was blocked because PII was found and BlockOnDetection=true.</summary>
    public bool WasBlocked { get; set; }

    // ── External call ───────────────────────────────────────────────────────

    /// <summary>Full URL called (may be omitted for BlobStorage to avoid leaking SAS tokens).</summary>
    public string? RequestUrl { get; set; }

    /// <summary>HTTP status code returned (null for mocked/skipped calls).</summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>Elapsed wall-clock time in milliseconds for the external call.</summary>
    public long? DurationMs { get; set; }

    /// <summary>Whether the LLM/API call was mocked (no real HTTP request made).</summary>
    public bool IsMocked { get; set; }

    /// <summary>Brief outcome summary — "Success", "Error", "Blocked", "MockResponse".</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Error message if the call failed.</summary>
    public string? ErrorMessage { get; set; }

    // ── Timestamps / actor ──────────────────────────────────────────────────

    public string? UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
