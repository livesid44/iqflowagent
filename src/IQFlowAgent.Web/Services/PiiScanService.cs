using System.Text.RegularExpressions;
using IQFlowAgent.Web.Data;
using IQFlowAgent.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Local, regex-based PII/SPII scanner.  No data leaves the server — detection is
/// entirely in-process using compiled regular expressions.
/// </summary>
public sealed class PiiScanService : IPiiScanService
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContextService _tenantContext;

    public PiiScanService(ApplicationDbContext db, ITenantContextService tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    // ── Compiled regex patterns ───────────────────────────────────────────────

    // RFC 5321-style email — rejects consecutive dots and requires valid TLD (2+ chars).
    // Word boundary ensures we don't match inside URLs that already contain emails.
    private static readonly Regex RxEmail = new(
        @"(?<![.\w])[a-zA-Z0-9](?:[a-zA-Z0-9._%+\-]{0,62}[a-zA-Z0-9])?@[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}(?![.\w])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // International phone numbers — requires at least one separator or international prefix
    // to avoid matching arbitrary digit sequences (e.g. version numbers, IDs).
    // Matches: +1-800-555-1234  (800) 555-1234  +44 20 7946 0958
    private static readonly Regex RxPhone = new(
        @"(?<!\d)(?:\+\d{1,3}[\s\-.])?(?:\(\d{2,4}\)[\s\-.]|\d{2,4}[\s\-.])(?:\d{2,4}[\s\-.]){1,3}\d{2,4}(?!\d)",
        RegexOptions.Compiled);

    // Major credit card patterns (Visa, Mastercard, Amex, Discover) with word boundaries
    private static readonly Regex RxCreditCard = new(
        @"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13}|6011[0-9]{12})\b",
        RegexOptions.Compiled);

    // US Social Security Number — requires separator or explicit format
    private static readonly Regex RxSsn = new(
        @"\b(?!000|666|9\d{2})\d{3}[-\s](?!00)\d{2}[-\s](?!0000)\d{4}\b",
        RegexOptions.Compiled);

    // IPv4 address
    private static readonly Regex RxIpV4 = new(
        @"\b(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b",
        RegexOptions.Compiled);

    // Passport numbers — broad pattern covering UK, US, EU formats
    private static readonly Regex RxPassport = new(
        @"\b[A-Z]{1,2}\d{6,9}\b",
        RegexOptions.Compiled);

    // Date of birth — requires explicit separator (/ or -) to avoid matching version numbers.
    // NOTE: This detector is disabled by default because it will match ordinary process dates,
    // deadlines, and timestamps.  Enable only when the input domain is specifically user profile data.
    private static readonly Regex RxDob = new(
        @"\b(?:\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4}|\d{4}[/\-]\d{1,2}[/\-]\d{1,2})\b",
        RegexOptions.Compiled);

    // URLs (http / https / ftp)
    private static readonly Regex RxUrl = new(
        @"https?://[^\s""'<>]+|ftp://[^\s""'<>]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Capitalised full-name pattern: two or more capitalised words that aren't
    // common business terms.  Very conservative to minimise false positives.
    private static readonly Regex RxPersonName = new(
        @"\b([A-Z][a-z]{1,20})\s+([A-Z][a-z]{1,20})(?:\s+([A-Z][a-z]{1,20}))?\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> _commonCapitalisedPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Process Name", "Business Unit", "Process Owner", "Process Type",
        "Estimated Volume", "Site Location", "Time Zone", "Line Of Business",
        "Customer Service", "Tech Mahindra", "Due Date", "New York", "Los Angeles",
        "San Francisco", "United States", "United Kingdom", "New Zealand",
        "South Africa", "North America", "South America", "Azure OpenAI",
        "Machine Learning", "Artificial Intelligence", "Key Performance",
    };

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<PiiScanResult> ScanAsync(string text)
    {
        var tenantId = _tenantContext.GetCurrentTenantId();
        var settings = await _db.TenantPiiSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId);

        // If no settings row or scanning is disabled, return the original text unchanged.
        if (settings == null || !settings.IsEnabled)
            return new PiiScanResult { RedactedText = text };

        return ScanWithSettings(text, settings);
    }

    public PiiScanResult ScanWithSettings(string text, TenantPiiSettings settings)
    {
        if (!settings.IsEnabled)
            return new PiiScanResult { RedactedText = text };

        var findings = new List<PiiFinding>();
        var redacted = text;

        Redact(ref redacted, findings, settings.DetectEmailAddresses,    RxEmail,      "Email Address");
        Redact(ref redacted, findings, settings.DetectPhoneNumbers,      RxPhone,      "Phone Number");
        Redact(ref redacted, findings, settings.DetectCreditCardNumbers, RxCreditCard, "Credit Card Number");
        Redact(ref redacted, findings, settings.DetectSsnNumbers,        RxSsn,        "SSN / National ID");
        Redact(ref redacted, findings, settings.DetectIpAddresses,       RxIpV4,       "IP Address");
        Redact(ref redacted, findings, settings.DetectPassportNumbers,   RxPassport,   "Passport / ID Number");
        Redact(ref redacted, findings, settings.DetectDatesOfBirth,      RxDob,        "Date of Birth");
        Redact(ref redacted, findings, settings.DetectUrls,              RxUrl,        "URL");

        if (settings.DetectPersonNames)
            RedactPersonNames(ref redacted, findings);

        var shouldBlock = findings.Count > 0 && settings.BlockOnDetection;
        return new PiiScanResult { Findings = findings, RedactedText = redacted, ShouldBlock = shouldBlock };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void Redact(
        ref string text,
        List<PiiFinding> findings,
        bool enabled,
        Regex pattern,
        string entityType)
    {
        if (!enabled) return;

        var placeholder = $"[{entityType.Replace(" ", "_").Replace("/", "_").ToUpperInvariant()}]";

        text = pattern.Replace(text, m =>
        {
            findings.Add(new PiiFinding { EntityType = entityType, MatchedText = m.Value });
            return placeholder;
        });
    }

    private static void RedactPersonNames(ref string text, List<PiiFinding> findings)
    {
        const string placeholder = "[PERSON_NAME]";
        text = RxPersonName.Replace(text, m =>
        {
            var candidate = m.Value.Trim();
            if (_commonCapitalisedPhrases.Contains(candidate))
                return m.Value; // keep — it's a known non-PII phrase
            findings.Add(new PiiFinding { EntityType = "Person Name", MatchedText = candidate });
            return placeholder;
        });
    }
}
