using System.Text.RegularExpressions;
using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Deterministic, pattern-based extractor that populates BARTOK DD template fields
/// directly from aggregated document text and task comments — without calling an LLM.
///
/// Extraction strategy (applied in order; first match wins for each field):
///   1. Task-title-to-field mapping   — when a task was created via "Create Task for Field",
///      its title is "[Report] Gather: {FieldLabel}"; every comment/artifact in that task
///      is mapped directly to the corresponding field key.
///   2. Inline Label:value patterns   — comments often contain lines like
///      "OCC Reference: OCC_0956" or "Systems Used: SAP, Oracle".
///   3. Section-header extraction     — documents often have labelled sections
///      ("Process Description\n…\nSystems Used\n…").  Content between matched headers
///      is mapped to the relevant fields.
///   4. Structural pattern extraction — OCC table rows, month+number volume tables,
///      known system names, regulation acronyms, SLA metrics, hours of operation.
///
/// Offline values take absolute priority over AI results in AnalyzeFields so that
/// real data from documents never gets replaced by "To be confirmed" placeholders.
/// </summary>
internal static class OfflineFieldExtractor
{
    // ── Label → field key (exact match on [Report] Gather: {label}) ───────────
    private static readonly Dictionary<string, string> LabelToKey =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Document Control
        ["Process Name"]              = "dc_process_name",
        ["Lot Number and Name"]       = "dc_lot",
        ["Document Date"]             = "dc_date",
        ["Process Author"]            = "dc_author",
        ["Approver"]                  = "dc_approver",
        // 1. Scope / Artefacts
        ["Countries in Scope"]        = "dc_countries",
        ["Document / Artefact #1"]    = "artefact_doc1",
        // 2. Process Overview
        ["Process Description"]       = "po_description",
        ["Process Owner"]             = "po_owner",
        ["Monthly Volumes"]           = "po_volumes",
        ["Peak Volume"]               = "po_peak_volume",
        ["Weekday Hours"]             = "po_hours_weekday",
        ["Weekend Hours"]             = "po_hours_weekend",
        ["Public Holiday Cover"]      = "po_hours_holiday",
        ["Systems Used"]              = "po_systems",
        // 3. RACI
        ["RACI Task 1"]               = "raci_task1",
        ["RACI Task 2"]               = "raci_task2",
        ["RACI Task 3"]               = "raci_task3",
        ["RACI Task 4"]               = "raci_task4",
        ["RACI Role 1"]               = "raci_role1",
        ["RACI Role 2"]               = "raci_role2",
        ["RACI Role 3"]               = "raci_role3",
        ["RACI Role 4"]               = "raci_role4",
        // 4. SOP
        ["Step Action"]               = "sop_action",
        ["Step Role"]                 = "sop_role",
        ["Step System"]               = "sop_system",
        ["Step Output"]               = "sop_output",
        ["Decision Point"]            = "sop_decision",
        ["Decision Outcome"]          = "sop_decision_out",
        ["Automation Status"]         = "sop_auto_status",
        ["Opportunity Rating"]        = "sop_opp_rating",
        ["Automation Type"]           = "sop_auto_type",
        ["Additional Step"]           = "sop_extra_step",
        // 5. Work Instructions
        ["Step Name"]                 = "wi_step_name",
        ["Step 1 — Instruction 1"]    = "wi_instr1a",
        ["Step 1 — Instruction 2"]    = "wi_instr1b",
        ["Step 1 — Error Handling"]   = "wi_instr1c",
        ["Step 2 — Instruction 1"]    = "wi_instr2a",
        // 6. Escalation
        ["Escalation Trigger"]        = "esc_trigger",
        ["Escalation Path"]           = "esc_path",
        ["Escalation Timeframe"]      = "esc_timeframe",
        ["Resolution Target"]         = "esc_target",
        // 6.2 Exception
        ["Exception Type"]            = "exc_type",
        ["Handling Approach"]         = "exc_handling",
        ["Approval Required"]         = "exc_approval",
        // 7. SLA
        ["SLA Metric"]                = "sla_metric",
        ["Measurement Method"]        = "sla_measurement",
        ["Reporting Frequency"]       = "sla_frequency",
        ["Measurement Tool"]          = "sla_tool",
        ["Performance Metric"]        = "sla_metric_perf",
        ["Actual Performance"]        = "sla_actual_perf",
        // 8. Volumetrics
        ["Transaction Volumes"]       = "vol_transaction",
        ["Volume Notes"]              = "vol_note",
        ["Volume Forecast"]           = "vol_forecast",
        // 9. Regulatory
        ["Regulation / Standard"]     = "reg_regulation",
        ["Obligation"]                = "reg_obligation",
        ["Control in Process"]        = "reg_control",
        ["Evidence Artefact"]         = "reg_evidence",
        ["TechM Framework Reference"] = "techm_framework",
        // 10. Training
        ["Training Module"]           = "train_module",
        ["Delivery Method"]           = "train_delivery",
        ["Competency Verification"]   = "train_verification",
        // 11. OCC
        ["OCC Reference"]             = "occ_ref",
        ["OCC Obligation"]            = "occ_obligation",
        ["OCC Control"]               = "occ_control",
    };

    // ── Section header → field keys (keyword sets for section detection) ────────
    // When a document section headed by any of these keywords is found, the section
    // body is mapped to each listed field key (first key = primary).
    private static readonly (string[] Headers, string[] Keys)[] SectionMap =
    [
        (["process description", "process overview", "about this process"],
            ["po_description"]),
        // Volume fields are intentionally excluded from the SectionMap.
        // They are populated exclusively by ExtractVolumeData (Pass 4b) which uses
        // strict Mon-YY + large-number validation to avoid false captures from
        // document revision histories and other prose that mention month names.
        // Previously having "transaction volumes", "volumetrics" etc. here caused the
        // entire revision-history section of a process document to be stored as volume
        // data whenever those words appeared as section headings.
        (["peak volume", "peak period", "peak transaction"],
            ["po_peak_volume", "vol_note"]),
        (["weekday hours", "operating hours", "business hours", "hours of operation", "working hours"],
            ["po_hours_weekday"]),
        (["weekend hours", "weekend coverage"],
            ["po_hours_weekend"]),
        (["public holiday", "holiday cover"],
            ["po_hours_holiday"]),
        (["systems used", "applications used", "tools used", "systems and tools", "platforms"],
            ["po_systems"]),
        (["raci", "raci matrix", "roles and responsibilities"],
            ["raci_task1", "raci_task2", "raci_task3", "raci_task4",
             "raci_role1", "raci_role2", "raci_role3", "raci_role4"]),
        (["standard operating procedure", "sop steps", "process steps", "procedure"],
            ["sop_action", "sop_role"]),
        (["work instructions", "detailed instructions", "step-by-step"],
            ["wi_step_name", "wi_instr1a"]),
        (["escalation", "escalation matrix", "escalation path", "escalation procedure"],
            ["esc_trigger", "esc_path", "esc_timeframe"]),
        (["exception handling", "exceptions", "exception types"],
            ["exc_type", "exc_handling"]),
        (["service level", "sla", "kpis", "performance targets", "sla metrics"],
            ["sla_metric", "sla_measurement", "sla_actual_perf"]),
        // "volumetrics", "transaction volume detail", "volume analysis" are intentionally
        // absent — see comment near SectionMap definition.  Volume fields are owned by
        // ExtractVolumeData exclusively.
        (["regulatory", "compliance", "regulations", "compliance obligations", "regulatory requirements"],
            ["reg_regulation", "reg_obligation", "reg_control"]),
        (["training", "training materials", "training plan", "training requirements"],
            ["train_module", "train_delivery"]),
        (["orange customer contract", "occ obligations", "contract obligations", "orange contract"],
            ["occ_ref", "occ_obligation", "occ_control"]),
        (["approver", "document approver", "approved by", "sign-off authority"],
            ["dc_approver"]),
        (["automation", "automation assessment", "automation potential"],
            ["sop_auto_status", "sop_opp_rating", "sop_auto_type"]),
    ];

    // ── Inline label patterns: "Label: value" lines in comments ──────────────
    // Maps a regex (matching the label part) to the field key.
    private static readonly (Regex Pattern, string Key)[] InlineLabelMap =
    [
        (new Regex(@"^OCC\s+Ref(?:erence)?\s*:", RegexOptions.IgnoreCase), "occ_ref"),
        (new Regex(@"^OCC\s+Obligation\s*:", RegexOptions.IgnoreCase), "occ_obligation"),
        (new Regex(@"^OCC\s+Control\s*:", RegexOptions.IgnoreCase), "occ_control"),
        (new Regex(@"^Process\s+Description\s*:", RegexOptions.IgnoreCase), "po_description"),
        (new Regex(@"^Process\s+Overview\s*:", RegexOptions.IgnoreCase), "po_description"),
        (new Regex(@"^Description\s*:", RegexOptions.IgnoreCase), "po_description"),
        (new Regex(@"^(?:Monthly\s+)?Volumes?\s*:", RegexOptions.IgnoreCase), "po_volumes"),
        (new Regex(@"^Peak\s+Volume\s*:", RegexOptions.IgnoreCase), "po_peak_volume"),
        (new Regex(@"^Weekday\s+Hours?\s*:", RegexOptions.IgnoreCase), "po_hours_weekday"),
        (new Regex(@"^(?:Operating|Business|Working)\s+Hours?\s*:", RegexOptions.IgnoreCase), "po_hours_weekday"),
        (new Regex(@"^Weekend\s+Hours?\s*:", RegexOptions.IgnoreCase), "po_hours_weekend"),
        (new Regex(@"^(?:Public\s+)?Holiday\s+(?:Cover|Hours?)\s*:", RegexOptions.IgnoreCase), "po_hours_holiday"),
        (new Regex(@"^Systems?\s+(?:Used|Utilised)?\s*:", RegexOptions.IgnoreCase), "po_systems"),
        (new Regex(@"^Applications?\s*:", RegexOptions.IgnoreCase), "po_systems"),
        (new Regex(@"^Approver\s*:", RegexOptions.IgnoreCase), "dc_approver"),
        (new Regex(@"^(?:Approved\s+By|Sign.?Off)\s*:", RegexOptions.IgnoreCase), "dc_approver"),
        (new Regex(@"^Escalation\s+Trigger\s*:", RegexOptions.IgnoreCase), "esc_trigger"),
        (new Regex(@"^Escalation\s+Path\s*:", RegexOptions.IgnoreCase), "esc_path"),
        (new Regex(@"^Escalation\s+Timeframe\s*:", RegexOptions.IgnoreCase), "esc_timeframe"),
        (new Regex(@"^Resolution\s+Target\s*:", RegexOptions.IgnoreCase), "esc_target"),
        (new Regex(@"^Exception\s+Type\s*:", RegexOptions.IgnoreCase), "exc_type"),
        (new Regex(@"^(?:Handling|Exception\s+Handling)\s*:", RegexOptions.IgnoreCase), "exc_handling"),
        (new Regex(@"^Approval\s+Required\s*:", RegexOptions.IgnoreCase), "exc_approval"),
        (new Regex(@"^SLA\s+Metric\s*:", RegexOptions.IgnoreCase), "sla_metric"),
        (new Regex(@"^(?:SLA|KPI|Service\s+Level)\s*:", RegexOptions.IgnoreCase), "sla_metric"),
        (new Regex(@"^Measurement\s+(?:Method|Tool)\s*:", RegexOptions.IgnoreCase), "sla_measurement"),
        (new Regex(@"^Reporting\s+Frequency\s*:", RegexOptions.IgnoreCase), "sla_frequency"),
        (new Regex(@"^Regulation\s*:", RegexOptions.IgnoreCase), "reg_regulation"),
        (new Regex(@"^Compliance\s*:", RegexOptions.IgnoreCase), "reg_regulation"),
        (new Regex(@"^Regulatory\s+Obligation\s*:", RegexOptions.IgnoreCase), "reg_obligation"),
        (new Regex(@"^(?:Regulatory\s+)?Control\s*:", RegexOptions.IgnoreCase), "reg_control"),
        (new Regex(@"^Evidence\s*:", RegexOptions.IgnoreCase), "reg_evidence"),
        (new Regex(@"^Training\s+Module\s*:", RegexOptions.IgnoreCase), "train_module"),
        (new Regex(@"^Delivery\s+Method\s*:", RegexOptions.IgnoreCase), "train_delivery"),
        (new Regex(@"^Competency\s+Verification\s*:", RegexOptions.IgnoreCase), "train_verification"),
        (new Regex(@"^(?:SOP\s+)?Step\s+(?:Action|Description)\s*:", RegexOptions.IgnoreCase), "sop_action"),
        (new Regex(@"^Step\s+Role\s*:", RegexOptions.IgnoreCase), "sop_role"),
        (new Regex(@"^Step\s+System\s*:", RegexOptions.IgnoreCase), "sop_system"),
        (new Regex(@"^Step\s+Output\s*:", RegexOptions.IgnoreCase), "sop_output"),
        (new Regex(@"^(?:Automation|Automation\s+Status)\s*:", RegexOptions.IgnoreCase), "sop_auto_status"),
        (new Regex(@"^Work\s+Instruction\s*:", RegexOptions.IgnoreCase), "wi_instr1a"),
        (new Regex(@"^RACI\s+Task\s*1\s*:", RegexOptions.IgnoreCase), "raci_task1"),
        (new Regex(@"^RACI\s+Task\s*2\s*:", RegexOptions.IgnoreCase), "raci_task2"),
        (new Regex(@"^RACI\s+Task\s*3\s*:", RegexOptions.IgnoreCase), "raci_task3"),
        (new Regex(@"^RACI\s+Task\s*4\s*:", RegexOptions.IgnoreCase), "raci_task4"),
        (new Regex(@"^RACI\s+Role\s*1\s*:", RegexOptions.IgnoreCase), "raci_role1"),
        (new Regex(@"^RACI\s+Role\s*2\s*:", RegexOptions.IgnoreCase), "raci_role2"),
        (new Regex(@"^RACI\s+Role\s*3\s*:", RegexOptions.IgnoreCase), "raci_role3"),
        (new Regex(@"^RACI\s+Role\s*4\s*:", RegexOptions.IgnoreCase), "raci_role4"),
    ];

    // ── Segment separator pattern — matches "--- anything ---" header lines ──
    private static readonly Regex SegmentHeaderPattern =
        new(@"^---\s+(.+?)\s+---\s*$", RegexOptions.Compiled);

    // ── Task title prefix written by CreateTaskForField ───────────────────────
    private const string ReportGatherPrefix = "[Report] Gather: ";

    // ── Structural patterns ───────────────────────────────────────────────────
    private static readonly Regex OccCodePattern =
        new(@"\bOCC[_\-]?\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
        "Jan", "Feb", "Mar", "Apr", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    ];
    // Generic month presence — used for context/section detection only
    private static readonly Regex MonthPattern = new(
        $@"\b({string.Join("|", MonthNames)})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Strict volume-row pattern: requires "Mon-YY" or "Mon-YYYY" format
    // (e.g. "Jan-25", "Feb-2026"). This deliberately excludes prose dates like
    // "26 May, 14" or "22 August 2022" that appear in document revision histories.
    private static readonly Regex VolumeRowMonthPattern = new(
        @"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)-(?:\d{4}|\d{2})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A volume row must also contain a "large" number (≥ 100) — transaction counts
    // are never single- or double-digit, unlike version-history year-numbers (14, 16 …).
    private static readonly Regex LargeNumberPattern =
        new(@"\b\d{3,}[\d,\.]*", RegexOptions.Compiled);

    private static readonly Regex NumberPattern =
        new(@"\d[\d,\.]*", RegexOptions.Compiled);

    private static readonly Regex PeakVolumePattern = new(
        @"\b(peak|month[\s\-]?end|quarter[\s\-]?end|year[\s\-]?end|highest|maximum|surge|busiest)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RegulationPattern = new(
        @"\b(GDPR|MiFID\s*I{0,2}|DORA|FCA|PCI[\s\-]?DSS|ISO\s*\d+|SOX|HIPAA|AML|KYC|" +
        @"Basel\s*I{0,3}|Dodd[\s\-]?Frank|EMIR|SFTR|CSDR|CASS|MAR|PRIIPs|UCITS|AIFMD|MLD\s*\d)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SlaMetricPattern =
        new(@"\b(SLA|KPI|service[\s\-]level|turnaround|resolution[\s\-]time|uptime|availability|accuracy|error[\s\-]rate)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SlaValuePattern =
        new(@"(\d+\.?\d*\s*%|\d+\s*(hours?|hrs?|days?|minutes?|mins?|seconds?|secs?))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TimePattern =
        new(@"\b\d{1,2}[:h]\d{2}\b", RegexOptions.Compiled);

    private static readonly Regex WeekdayPattern =
        new(@"\b(Mon|Tue|Wed|Thu|Fri|Monday|Tuesday|Wednesday|Thursday|Friday|weekday|week\s+day)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WeekendPattern =
        new(@"\b(Sat|Sun|Saturday|Sunday|weekend|week\s+end)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Keywords that identify RACI role column headers in a table.
    // At least two of these must appear in a tab-separated row for it to be treated
    // as the RACI header row.
    private static readonly string[] KnownRaciRoleKeywords =
    [
        "change requester", "change originator", "change approver",
        "change qualifier", "change scheduler", "change implementer",
        "change manager", "change coordinator", "change owner",
        "cab", "implementer", "requester", "originator", "approver",
        "scheduler", "qualifier"
    ];

    private static readonly string[] KnownSystems =
    [
        "SAP", "Oracle", "Salesforce", "ServiceNow", "Dynamics", "Workday",
        "DocuSign", "BACS", "SWIFT", "Bloomberg", "Temenos", "Murex",
        "SharePoint", "Teams", "Outlook", "Excel", "PowerBI", "Power BI",
        "Jira", "Confluence", "Zendesk", "Freshdesk", "HubSpot",
        "Avaloq", "Finastra", "FIS", "Calypso", "SunGard", "Flexcube",
        "TM1", "Cognos", "Tableau", "Snowflake", "Azure", "AWS"
    ];

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Extracts BARTOK DD field values from aggregated document/comment text using
    /// deterministic pattern matching.  Returns a dictionary of fieldKey → extracted value.
    /// Only fields for which a real value was found are included.
    /// </summary>
    public static Dictionary<string, string> Extract(string aggregatedText, IntakeRecord intake)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(aggregatedText)) return result;

        // Split the aggregated text into per-source segments delimited by "--- … ---" lines.
        var segments = SplitIntoSegments(aggregatedText);

        // ── Pass 1: direct task-title → field mapping ─────────────────────────
        // Tasks created via "Create Task for Field" have the title "[Report] Gather: {Label}".
        // All text collected from that task is the answer for the corresponding field.
        foreach (var seg in segments)
        {
            var title = seg.Source;
            // Strip the "[Report] Gather: " prefix if present
            if (title.StartsWith(ReportGatherPrefix, StringComparison.OrdinalIgnoreCase))
                title = title[ReportGatherPrefix.Length..].Trim();

            if (LabelToKey.TryGetValue(title, out var key))
            {
                var body = CleanBody(seg.Body);
                if (!string.IsNullOrWhiteSpace(body))
                    result.TryAdd(key, body);
            }
        }

        // ── Pass 2: inline "Label: value" extraction from every line ──────────
        // Works for textbox-style comments such as "OCC Reference: OCC_0956" or
        // "Systems Used: SAP, Oracle".
        var allLines = aggregatedText
            .Split('\n', StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        ExtractInlineKeyValues(allLines, result);

        // ── Pass 3: section-header extraction within each segment ─────────────
        // Documents often group content under labelled headings:
        //   "Process Description\n<body text>\n\nSystems Used\n<body text>"
        foreach (var seg in segments)
        {
            var segLines = seg.Body
                .Split('\n', StringSplitOptions.None)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();
            ExtractFromSections(segLines, result);
        }

        // ── Pass 4: structural / pattern extraction ───────────────────────────
        ExtractOccTable(allLines, result);
        ExtractVolumeData(allLines, result, intake);
        ExtractRaciTable(allLines, result);
        ExtractSystemNames(allLines, result);
        ExtractRegulations(allLines, result);
        ExtractSlaValues(allLines, result);
        ExtractHoursOfOperation(allLines, result);

        return result;
    }

    // ── Pass 1 helper: split by "--- Source ---" segment headers ─────────────

    private sealed record TextSegment(string Source, string Body);

    private static List<TextSegment> SplitIntoSegments(string aggregatedText)
    {
        var segments = new List<TextSegment>();
        var lines    = aggregatedText.Split('\n', StringSplitOptions.None);
        string? currentSource = null;
        var currentBody = new System.Text.StringBuilder();

        void Flush()
        {
            if (currentSource != null && currentBody.Length > 0)
                segments.Add(new TextSegment(currentSource, currentBody.ToString()));
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var match = SegmentHeaderPattern.Match(line);
            if (match.Success)
            {
                Flush();
                // Source may be "Notes on Task TSK-...: [Report] Gather: OCC Reference"
                // Extract the part after the last colon that follows "TSK-...:"
                var source = match.Groups[1].Value;
                var taskMarker = Regex.Match(source, @":\s*(.+)$");
                currentSource = taskMarker.Success ? taskMarker.Groups[1].Value.Trim() : source;
                currentBody.Clear();
            }
            else if (currentSource != null)
            {
                currentBody.AppendLine(line);
            }
        }
        Flush();

        // If no segment headers found treat the whole text as one anonymous segment
        if (segments.Count == 0)
            segments.Add(new TextSegment("(document)", aggregatedText));

        return segments;
    }

    // ── Pass 2: inline "Label: value" ────────────────────────────────────────

    private static void ExtractInlineKeyValues(string[] lines, Dictionary<string, string> result)
    {
        // Multi-line accumulation: if a label matches and the value continues on the
        // next line(s), keep appending until we hit a blank line or another label.
        string? currentKey  = null;
        var     currentVal  = new System.Text.StringBuilder();

        void FlushCurrent()
        {
            if (currentKey == null) return;
            var v = currentVal.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(v))
                result.TryAdd(currentKey, v);
            currentKey = null;
            currentVal.Clear();
        }

        foreach (var line in lines)
        {
            // Check if this line starts a new label
            bool matched = false;
            foreach (var (pattern, key) in InlineLabelMap)
            {
                var m = pattern.Match(line);
                if (!m.Success) continue;

                FlushCurrent();
                var value = line[m.Length..].Trim();
                currentKey = key;
                if (!string.IsNullOrWhiteSpace(value))
                    currentVal.Append(value);
                matched = true;
                break;
            }

            if (!matched && currentKey != null)
            {
                // Continuation line — append if non-empty; stop accumulating on blank/separator
                if (line.Length == 0 || line.StartsWith("---") || line.StartsWith("==="))
                    FlushCurrent();
                else
                {
                    if (currentVal.Length > 0) currentVal.Append(' ');
                    currentVal.Append(line);
                }
            }
        }
        FlushCurrent();
    }

    // ── Pass 3: section-header-based extraction ───────────────────────────────

    private static void ExtractFromSections(string[] lines, Dictionary<string, string> result)
    {
        // Identify lines that look like section headings (short, no sentence-ending punctuation,
        // may be followed by a colon).  The content between two headings goes to all mapped fields.
        int i = 0;
        while (i < lines.Length)
        {
            var line        = lines[i].TrimEnd(':');
            var lineLower   = line.ToLowerInvariant().Trim();
            var sectionKeys = MatchSectionHeader(lineLower);

            if (sectionKeys.Length > 0)
            {
                // Collect body until the next section header or end
                var body = new System.Text.StringBuilder();
                i++;
                while (i < lines.Length)
                {
                    var nextLower = lines[i].TrimEnd(':').ToLowerInvariant().Trim();
                    if (MatchSectionHeader(nextLower).Length > 0) break;
                    body.AppendLine(lines[i]);
                    i++;
                }

                var bodyText = CleanBody(body.ToString());
                if (!string.IsNullOrWhiteSpace(bodyText))
                {
                    foreach (var key in sectionKeys)
                        result.TryAdd(key, bodyText);
                }
            }
            else
            {
                i++;
            }
        }
    }

    private static string[] MatchSectionHeader(string lineLower)
    {
        // A section header must be reasonably short and not look like a sentence
        if (lineLower.Length > 80 || lineLower.Contains(". ")) return [];

        foreach (var (headers, keys) in SectionMap)
        {
            foreach (var h in headers)
            {
                if (lineLower == h || lineLower.StartsWith(h + ":") || lineLower.StartsWith(h + " -"))
                    return keys;
            }
        }
        return [];
    }

    // ── Pass 4a: OCC table extraction ────────────────────────────────────────

    private static void ExtractOccTable(string[] lines, Dictionary<string, string> result)
    {
        // Strategy 1: find a header row containing "OCC Reference" and parse data rows
        int headerIdx = -1;
        int refCol = 0, obligCol = 1, controlCol = 2;

        for (int i = 0; i < lines.Length; i++)
        {
            var lower = lines[i].ToLowerInvariant();
            if (!lower.Contains("occ ref")) continue;

            headerIdx = i;
            var cols = lines[i].Split('\t');
            for (int c = 0; c < cols.Length; c++)
            {
                var col = cols[c].ToLowerInvariant().Trim();
                if (col.Contains("occ ref"))                                      refCol     = c;
                else if (col.Contains("obligation") || col.Contains("description")) obligCol = c;
                else if (col.Contains("control") || col.Contains("policy"))         controlCol = c;
            }
            break;
        }

        var refs = new List<string>(); var obligs = new List<string>(); var controls = new List<string>();

        if (headerIdx >= 0)
        {
            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("---") || line.StartsWith("===")) break;
                var parts     = line.Split('\t');
                var firstCell = GetCell(parts, refCol);
                if (string.IsNullOrWhiteSpace(firstCell)) continue;
                if (!OccCodePattern.IsMatch(firstCell) && firstCell.ToLowerInvariant().Contains("reference")) continue;
                AddNE(refs,     GetCell(parts, refCol));
                AddNE(obligs,   GetCell(parts, obligCol));
                AddNE(controls, GetCell(parts, controlCol));
            }
        }

        // Strategy 2: scan for OCC_XXXX codes anywhere in the text
        if (refs.Count == 0)
        {
            foreach (var line in lines)
            {
                if (!OccCodePattern.IsMatch(line)) continue;
                var parts = line.Split('\t');
                AddNE(refs, GetCell(parts, 0));
                if (parts.Length > 1) AddNE(obligs,   GetCell(parts, 1));
                if (parts.Length > 2) AddNE(controls, GetCell(parts, 2));
            }
        }

        if (refs.Count     > 0) result.TryAdd("occ_ref",        string.Join("; ", refs));
        if (obligs.Count   > 0) result.TryAdd("occ_obligation", string.Join("; ", obligs));
        if (controls.Count > 0) result.TryAdd("occ_control",    string.Join("; ", controls));
    }

    // ── Pass 4b: volume data ─────────────────────────────────────────────────

    private static void ExtractVolumeData(string[] lines, Dictionary<string, string> result, IntakeRecord intake)
    {
        // Use the strict VolumeRowMonthPattern (requires "Mon-YY" format) AND a large number
        // (≥ 100) so that document revision-history lines like "26 May, 14 original version"
        // are never mis-identified as volume data.
        var volLines = lines
            .Where(l => VolumeRowMonthPattern.IsMatch(l) && LargeNumberPattern.IsMatch(l))
            .ToList();

        if (volLines.Count >= 2)
        {
            var formatted = volLines.Select(l => l.Replace('\t', ' ')).ToList();
            var combined  = string.Join("; ", formatted);
            // Use direct assignment (not TryAdd) so we override any garbage that
            // section-header extraction may have written to these keys earlier.
            result["po_volumes"]      = combined;
            result["vol_transaction"] = combined;
            result["vol_forecast"]    =
                $"Based on historical data: avg {EstimateAverage(volLines)} transactions/month.";
        }
        else if (intake.EstimatedVolumePerDay > 0)
        {
            // Fall back to daily estimate × 22 working days; only write if nothing
            // better has been found (use TryAdd so AI-extracted or section data wins).
            var monthly = intake.EstimatedVolumePerDay * 22;
            result.TryAdd("po_volumes",
                $"Approx. {monthly:N0} transactions/month (based on intake estimate of " +
                $"{intake.EstimatedVolumePerDay:N0}/day × 22 working days).");
        }

        var peakLines = lines.Where(l => PeakVolumePattern.IsMatch(l) && NumberPattern.IsMatch(l)).ToList();
        if (peakLines.Count > 0)
        {
            result.TryAdd("po_peak_volume", string.Join("; ", peakLines.Take(2)));
            result.TryAdd("vol_note",       string.Join("; ", peakLines.Take(2)));
        }
    }

    // ── Pass 4b2: RACI table extraction ─────────────────────────────────────────
    // Reads tab-separated rows produced by DocumentTextExtractor from a Word RACI
    // table.  The header row identifies role names by column position; subsequent
    // rows contain the responsibilities for each role (bullets joined with " | ").
    // Maps the first four role columns to raci_role1..raci_role4 and their tasks
    // to raci_task1..raci_task4.

    private static void ExtractRaciTable(string[] lines, Dictionary<string, string> result)
    {
        // Find the header row: a tab-separated line where ≥ 2 columns match known
        // RACI role keywords.
        for (int headerIdx = 0; headerIdx < lines.Length; headerIdx++)
        {
            var headerLine = lines[headerIdx];
            if (!headerLine.Contains('\t')) continue;

            var cols = headerLine.Split('\t');
            // Identify which column indices are RACI role headers
            var roleColIndices = cols
                .Select((c, i) =>
                    (ColIdx: i,
                     Match: KnownRaciRoleKeywords.Any(k =>
                         c.Trim().ToLowerInvariant().Contains(k))))
                .Where(x => x.Match)
                .Select(x => x.ColIdx)
                .ToList();

            if (roleColIndices.Count < 2) continue; // Not a RACI header row

            // Collect content from all subsequent rows (until separator or end)
            // per column index.
            var roleContent = roleColIndices.ToDictionary(i => i, _ => new System.Text.StringBuilder());

            for (int bodyIdx = headerIdx + 1; bodyIdx < lines.Length; bodyIdx++)
            {
                var bodyLine = lines[bodyIdx];
                if (bodyLine.StartsWith("---") || bodyLine.StartsWith("===")) break;

                var bodyCols = bodyLine.Split('\t');
                foreach (var colIdx in roleColIndices)
                {
                    var cellText = GetCell(bodyCols, colIdx).Trim();
                    if (!string.IsNullOrWhiteSpace(cellText))
                    {
                        if (roleContent[colIdx].Length > 0)
                            roleContent[colIdx].Append("; ");
                        roleContent[colIdx].Append(cellText);
                    }
                }
            }

            // Map the first 4 matched columns to raci_role/raci_task fields
            var fieldSlots = new[]
            {
                ("raci_role1", "raci_task1"),
                ("raci_role2", "raci_task2"),
                ("raci_role3", "raci_task3"),
                ("raci_role4", "raci_task4"),
            };

            int slot = 0;
            foreach (var colIdx in roleColIndices.Take(4))
            {
                var (roleKey, taskKey) = fieldSlots[slot++];
                var roleName  = cols[colIdx].Trim();
                var taskBody  = roleContent[colIdx].ToString().Trim();

                // Override any section-extraction result with the structured table data
                if (!string.IsNullOrWhiteSpace(roleName))
                    result[roleKey] = roleName;

                if (!string.IsNullOrWhiteSpace(taskBody))
                    result[taskKey] = taskBody;
            }

            break; // Processed the first RACI table found; stop searching
        }
    }

    // ── Pass 4c: system names ─────────────────────────────────────────────────

    private static void ExtractSystemNames(string[] lines, Dictionary<string, string> result)
    {
        var found = new List<string>();
        foreach (var line in lines)
            foreach (var kw in KnownSystems)
                if (line.Contains(kw, StringComparison.OrdinalIgnoreCase) &&
                    !found.Any(f => f.Equals(kw, StringComparison.OrdinalIgnoreCase)))
                    found.Add(kw);

        if (found.Count > 0)
            result.TryAdd("po_systems", string.Join(", ", found));
    }

    // ── Pass 4d: regulations ──────────────────────────────────────────────────

    private static void ExtractRegulations(string[] lines, Dictionary<string, string> result)
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

    // ── Pass 4e: SLA values ────────────────────────────────────────────────────

    private static void ExtractSlaValues(string[] lines, Dictionary<string, string> result)
    {
        var slaLines = lines.Where(l => SlaMetricPattern.IsMatch(l) && SlaValuePattern.IsMatch(l)).ToList();
        if (slaLines.Count == 0) return;
        result.TryAdd("sla_metric",      slaLines[0]);
        result.TryAdd("sla_actual_perf", string.Join("; ", slaLines.Take(3)));
        result.TryAdd("sla_metric_perf", slaLines[0]);
    }

    // ── Pass 4f: hours of operation ───────────────────────────────────────────

    private static void ExtractHoursOfOperation(string[] lines, Dictionary<string, string> result)
    {
        var weekdayLine = lines.FirstOrDefault(l => TimePattern.IsMatch(l) && WeekdayPattern.IsMatch(l));
        var weekendLine = lines.FirstOrDefault(l =>
            WeekendPattern.IsMatch(l) &&
            (TimePattern.IsMatch(l) || l.ToLowerInvariant().Contains("not operational")));

        if (weekdayLine != null) result.TryAdd("po_hours_weekday", weekdayLine);
        if (weekendLine != null) result.TryAdd("po_hours_weekend", weekendLine);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetCell(string[] parts, int idx) =>
        idx >= 0 && idx < parts.Length ? parts[idx].Trim() : string.Empty;

    private static void AddNE(List<string> list, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) list.Add(value);
    }

    /// <summary>Trims segment body, strips action-log prefixes like "[Comment]" and "[Status → X]".</summary>
    private static string CleanBody(string raw)
    {
        var lines = raw.Split('\n', StringSplitOptions.None)
            .Select(l =>
            {
                l = l.Trim();
                // Strip action-log prefixes written by AggregateArtifactTextAsync
                if (l.StartsWith("[Comment]", StringComparison.OrdinalIgnoreCase))
                    l = l["[Comment]".Length..].Trim();
                else if (l.StartsWith("[Status →", StringComparison.OrdinalIgnoreCase))
                {
                    // Strip "[Status → Completed] " prefix
                    var closeBracket = l.IndexOf(']');
                    if (closeBracket >= 0) l = l[(closeBracket + 1)..].Trim();
                }
                return l;
            })
            .Where(l => l.Length > 0)
            .ToArray();

        return string.Join(" ", lines);
    }

    /// <summary>Rough average of the first number found in each volume line.</summary>
    private static string EstimateAverage(IReadOnlyList<string> lines)
    {
        var nums = lines
            .Select(l => { var m = NumberPattern.Match(l.Replace(",", "")); return double.TryParse(m.Value, out var v) ? v : (double?)null; })
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (nums.Count == 0) return "N/A";
        return ((long)nums.Average()).ToString("N0");
    }
}
