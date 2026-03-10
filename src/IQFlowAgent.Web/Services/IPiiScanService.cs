namespace IQFlowAgent.Web.Services;

/// <summary>A single PII/SPII finding inside a scanned text.</summary>
public sealed class PiiFinding
{
    /// <summary>Category of the detected entity (e.g. "Email Address", "Phone Number").</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>The exact text that was matched.</summary>
    public string MatchedText { get; init; } = string.Empty;
}

/// <summary>Result of a PII scan.</summary>
public sealed class PiiScanResult
{
    /// <summary>Whether any PII was detected.</summary>
    public bool HasPii => Findings.Count > 0;

    /// <summary>
    /// When <c>true</c> the tenant setting <c>BlockOnDetection</c> is active and PII was found.
    /// The caller should abort the LLM request and surface an error instead.
    /// </summary>
    public bool ShouldBlock { get; init; }

    /// <summary>Individual findings, each naming the entity type and the matched text.</summary>
    public List<PiiFinding> Findings { get; init; } = [];

    /// <summary>
    /// The sanitised version of the original text with all PII replaced by
    /// placeholders (e.g. <c>[EMAIL]</c>).  Equal to the original text when
    /// <see cref="HasPii"/> is <c>false</c>.
    /// </summary>
    public string RedactedText { get; init; } = string.Empty;
}

public interface IPiiScanService
{
    /// <summary>
    /// Scans <paramref name="text"/> for PII/SPII using the settings configured for the
    /// current tenant.  Returns a <see cref="PiiScanResult"/> that contains the list of
    /// findings, the block policy decision, and the redacted version of the text.
    /// </summary>
    Task<PiiScanResult> ScanAsync(string text);

    /// <summary>
    /// Scans <paramref name="text"/> using an explicit settings object.
    /// Useful for preview / testing without persisting a settings change.
    /// </summary>
    PiiScanResult ScanWithSettings(string text, Models.TenantPiiSettings settings);
}
