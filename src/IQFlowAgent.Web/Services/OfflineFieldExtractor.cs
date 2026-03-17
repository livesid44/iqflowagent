using System.Text.RegularExpressions;
using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Deterministic, pattern-based extractor that populates BARTOK DD template fields
/// directly from aggregated document text and task comments — without calling an LLM.
/// <para>
/// Extracted values take priority over AI-generated values in <c>AnalyzeFields</c> so
/// that structured data (OCC tables, volume sheets, SLA metrics, etc.) always appears
/// in the final report rather than being replaced with "To be confirmed" placeholder text.
/// </para>
/// </summary>
internal static class OfflineFieldExtractor
{
    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
        "Jan", "Feb", "Mar", "Apr", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    ];

    private static readonly Regex MonthPattern = new(
        $@"\b({string.Join("|", MonthNames)})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberPattern =
        new(@"\d[\d,\.]*", RegexOptions.Compiled);

    private static readonly Regex OccCodePattern =
        new(@"\bOCC[_\-]?\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RegulationPattern = new(
        @"\b(GDPR|MiFID\s*I{0,2}|DORA|FCA|PCI[\s\-]?DSS|ISO\s*\d+|SOX|HIPAA|AML|KYC|Basel\s*I{0,3}|Dodd[\s\-]?Frank|EMIR|SFTR|CSDR|CASS|MAR|PRIIPs|UCITS|AIFMD|MLD\s*\d)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SlaMetricPattern = new(
        @"\b(SLA|KPI|service[\s\-]level|turnaround|resolution[\s\-]time|uptime|availability|accuracy|error[\s\-]rate)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SlaValuePattern = new(
        @"(\d+\.?\d*\s*%|\d+\s*(hours?|hrs?|days?|minutes?|mins?|seconds?|secs?))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] KnownSystems =
    [
        "SAP", "Oracle", "Salesforce", "ServiceNow", "Dynamics", "Workday",
        "DocuSign", "BACS", "SWIFT", "Bloomberg", "Temenos", "Murex",
        "SharePoint", "Teams", "Outlook", "Excel", "PowerBI", "Power BI",
        "Jira", "Confluence", "Zendesk", "Freshdesk", "HubSpot",
        "Avaloq", "Finastra", "FIS", "Calypso", "SunGard", "Flexcube",
        "TM1", "Cognos", "Tableau", "Snowflake", "Azure", "AWS"
    ];

    private static readonly Regex SystemsHeaderPattern = new(
        @"\b(systems?\s+used|platforms?|applications?\s+used|tools?\s+used)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PeakVolumePattern = new(
        @"\b(peak|month[\s\-]?end|quarter[\s\-]?end|year[\s\-]?end|highest|maximum|surge|busiest)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts BARTOK DD field values from aggregated document/comment text using
    /// deterministic pattern matching.
    /// </summary>
    /// <param name="aggregatedText">
    /// Combined text from task artifacts, task comments, and intake-level documents,
    /// as produced by <c>AggregateArtifactTextAsync</c>.  Tab-separated rows are
    /// expected for tabular content extracted from <c>.docx</c> and <c>.xlsx</c> files.
    /// </param>
    /// <param name="intake">The intake record (used for fallback values).</param>
    /// <returns>
    /// Dictionary mapping BARTOK field keys to extracted values.  Only fields for which
    /// a concrete value was found are included — the caller should treat missing keys as
    /// "not found by offline extraction" and fall back to the AI result.
    /// </returns>
    public static Dictionary<string, string> Extract(string aggregatedText, IntakeRecord intake)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(aggregatedText)) return result;

        var lines = aggregatedText
            .Split('\n', StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        ExtractOccFields(lines, result);
        ExtractVolumeFields(lines, result);
        ExtractSystemFields(lines, result);
        ExtractRegulatoryFields(lines, result);
        ExtractSlaFields(lines, result);
        ExtractHoursFields(lines, result);

        return result;
    }

    // ── OCC (Orange Customer Contract Obligations) ────────────────────────────

    private static void ExtractOccFields(string[] lines, Dictionary<string, string> result)
    {
        // Strategy 1: detect table header row containing "OCC Reference" (or similar)
        //             and parse the subsequent data rows.
        int headerIdx = -1;
        int refCol = 0, obligCol = 1, controlCol = 2;

        for (int i = 0; i < lines.Length; i++)
        {
            var lower = lines[i].ToLowerInvariant();
            if (!lower.Contains("occ ref") && !lower.Contains("occ reference")) continue;

            headerIdx = i;
            var cols = lines[i].Split('\t');
            for (int c = 0; c < cols.Length; c++)
            {
                var col = cols[c].ToLowerInvariant().Trim();
                if (col.Contains("occ ref"))                           refCol     = c;
                else if (col.Contains("obligation") || col.Contains("description")) obligCol = c;
                else if (col.Contains("control") || col.Contains("policy"))         controlCol = c;
            }
            break;
        }

        var refs     = new List<string>();
        var obligs   = new List<string>();
        var controls = new List<string>();

        if (headerIdx >= 0)
        {
            // Consume data rows after the header until a section separator
            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("---") || line.StartsWith("===")) break;

                var parts = line.Split('\t');
                var firstCell = GetCell(parts, refCol);
                if (string.IsNullOrWhiteSpace(firstCell)) continue;

                // Require an OCC code OR at least some content in the first cell
                // that follows the header (skip rows that look like sub-headers)
                if (!OccCodePattern.IsMatch(firstCell) &&
                    firstCell.ToLowerInvariant().Contains("reference")) continue;

                AddNonEmpty(refs,     GetCell(parts, refCol));
                AddNonEmpty(obligs,   GetCell(parts, obligCol));
                AddNonEmpty(controls, GetCell(parts, controlCol));
            }
        }

        // Strategy 2 (fallback): no header found — scan for standalone OCC_XXXX codes
        if (refs.Count == 0)
        {
            foreach (var line in lines)
            {
                if (!OccCodePattern.IsMatch(line)) continue;
                var parts = line.Split('\t');
                AddNonEmpty(refs,     GetCell(parts, 0));
                if (parts.Length > 1) AddNonEmpty(obligs,   GetCell(parts, 1));
                if (parts.Length > 2) AddNonEmpty(controls, GetCell(parts, 2));
            }
        }

        if (refs.Count     > 0) result["occ_ref"]        = string.Join("; ", refs);
        if (obligs.Count   > 0) result["occ_obligation"] = string.Join("; ", obligs);
        if (controls.Count > 0) result["occ_control"]    = string.Join("; ", controls);
    }

    // ── Monthly volumes / volumetrics ─────────────────────────────────────────

    private static void ExtractVolumeFields(string[] lines, Dictionary<string, string> result)
    {
        var volumeLines = lines
            .Where(l => MonthPattern.IsMatch(l) && NumberPattern.IsMatch(l))
            .ToList();

        if (volumeLines.Count >= 3)
        {
            // Format: Month\tVolume (from xlsx) or "Month: 1,234" (from comments)
            var formatted = volumeLines.Select(l =>
            {
                // Tab-separated → keep as-is; otherwise normalise spacing
                return l.Contains('\t') ? l.Replace('\t', ' ') : l;
            });

            var combined = string.Join("; ", formatted);
            result["po_volumes"]       = combined;
            result["vol_transaction"]  = combined;
            result["vol_forecast"]     = $"Based on historical data: avg {EstimateAverage(volumeLines)} transactions/month";
        }

        // Peak volume
        var peakLines = lines
            .Where(l => PeakVolumePattern.IsMatch(l) && NumberPattern.IsMatch(l))
            .ToList();
        if (peakLines.Count > 0)
        {
            result["po_peak_volume"] = string.Join("; ", peakLines.Take(2));
            result.TryAdd("vol_note", string.Join("; ", peakLines.Take(2)));
        }
    }

    // ── Systems used ─────────────────────────────────────────────────────────

    private static void ExtractSystemFields(string[] lines, Dictionary<string, string> result)
    {
        var found = new List<string>();

        foreach (var line in lines)
        {
            // Named system keywords
            foreach (var kw in KnownSystems)
                if (line.Contains(kw, StringComparison.OrdinalIgnoreCase) &&
                    !found.Any(f => f.Equals(kw, StringComparison.OrdinalIgnoreCase)))
                    found.Add(kw);

            // Lines that follow a "Systems Used" header
            if (SystemsHeaderPattern.IsMatch(line))
            {
                // The header line itself might contain the value (e.g. "Systems Used: SAP, Oracle")
                var afterColon = line.Contains(':') ? line[(line.IndexOf(':') + 1)..].Trim() : null;
                if (!string.IsNullOrWhiteSpace(afterColon) &&
                    !found.Any(f => f.Equals(afterColon, StringComparison.OrdinalIgnoreCase)))
                    found.Add(afterColon);
            }
        }

        if (found.Count > 0)
            result["po_systems"] = string.Join(", ", found);
    }

    // ── Regulatory / compliance ───────────────────────────────────────────────

    private static void ExtractRegulatoryFields(string[] lines, Dictionary<string, string> result)
    {
        var regLines = lines.Where(l => RegulationPattern.IsMatch(l)).ToList();
        if (regLines.Count == 0) return;

        var regs = regLines
            .SelectMany(l => RegulationPattern.Matches(l).Select(m => m.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.TryAdd("reg_regulation", string.Join(", ", regs));
        result.TryAdd("reg_obligation", string.Join("; ", regLines.Take(3)));
    }

    // ── SLA / performance ─────────────────────────────────────────────────────

    private static void ExtractSlaFields(string[] lines, Dictionary<string, string> result)
    {
        var slaLines = lines
            .Where(l => SlaMetricPattern.IsMatch(l) && SlaValuePattern.IsMatch(l))
            .ToList();

        if (slaLines.Count == 0) return;

        result.TryAdd("sla_metric",      slaLines[0]);
        result.TryAdd("sla_actual_perf", string.Join("; ", slaLines.Take(3)));
        result.TryAdd("sla_metric_perf", slaLines[0]);
    }

    // ── Hours of operation ────────────────────────────────────────────────────

    private static void ExtractHoursFields(string[] lines, Dictionary<string, string> result)
    {
        var timePattern = new Regex(@"\b\d{1,2}[:h]\d{2}\b", RegexOptions.IgnoreCase);
        var weekdayPattern = new Regex(@"\b(Mon|Tue|Wed|Thu|Fri|Monday|Tuesday|Wednesday|Thursday|Friday|weekday|week\s+day)\b",
            RegexOptions.IgnoreCase);
        var weekendPattern = new Regex(@"\b(Sat|Sun|Saturday|Sunday|weekend|week\s+end)\b",
            RegexOptions.IgnoreCase);

        var weekdayLine = lines.FirstOrDefault(l => timePattern.IsMatch(l) && weekdayPattern.IsMatch(l));
        var weekendLine = lines.FirstOrDefault(l =>
            weekendPattern.IsMatch(l) && (timePattern.IsMatch(l) || l.ToLowerInvariant().Contains("not operational")));

        if (weekdayLine != null) result.TryAdd("po_hours_weekday", weekdayLine);
        if (weekendLine != null) result.TryAdd("po_hours_weekend", weekendLine);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetCell(string[] parts, int idx) =>
        idx >= 0 && idx < parts.Length ? parts[idx].Trim() : string.Empty;

    private static void AddNonEmpty(List<string> list, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) list.Add(value);
    }

    /// <summary>
    /// Rough average of the first number found in each volume line.
    /// Used for a simple forecast sentence.
    /// </summary>
    private static string EstimateAverage(IReadOnlyList<string> lines)
    {
        var nums = lines
            .Select(l =>
            {
                var m = NumberPattern.Match(l.Replace(",", ""));
                return double.TryParse(m.Value, out var v) ? v : (double?)null;
            })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (nums.Count == 0) return "N/A";
        var avg = (long)(nums.Average());
        return avg.ToString("N0");
    }
}
