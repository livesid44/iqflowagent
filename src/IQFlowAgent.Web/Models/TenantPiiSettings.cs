namespace IQFlowAgent.Web.Models;

/// <summary>
/// Per-tenant configuration for the local PII/SPII safeguard that scans content
/// before it is sent to an LLM.
/// </summary>
public class TenantPiiSettings
{
    public int Id { get; set; }
    public int TenantId { get; set; }

    /// <summary>When true the PII scanner runs before every LLM call.</summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// When true, a request that contains PII is blocked and an error is returned.
    /// When false, the detected PII is replaced with a placeholder (e.g. [EMAIL]) before
    /// the content is forwarded to the LLM.
    /// </summary>
    public bool BlockOnDetection { get; set; } = false;

    // ── Which entity types to detect ─────────────────────────────────────────
    public bool DetectEmailAddresses    { get; set; } = true;
    public bool DetectPhoneNumbers      { get; set; } = true;
    public bool DetectCreditCardNumbers { get; set; } = true;
    public bool DetectSsnNumbers        { get; set; } = true;
    public bool DetectIpAddresses       { get; set; } = true;
    public bool DetectPassportNumbers   { get; set; } = true;
    public bool DetectDatesOfBirth      { get; set; } = false;
    public bool DetectUrls              { get; set; } = false;
    public bool DetectPersonNames       { get; set; } = false;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
