using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

public class DocxReportService : IDocxReportService
{
    // ── Static field catalogue ─────────────────────────────────────────────────
    // AutoSource values:
    //   "<PropertyName>" → resolved directly from IntakeRecord property
    //   "AI:<key>"       → pulled from AI analysis JSON
    //   ""               → no automatic mapping; user must supply the value manually
    //
    // TemplatePlaceholder is the exact text to find-and-replace inside the DOCX.

    private static readonly List<FieldDefinition> FieldDefs =
    [
        // ── Document Control (Table 0 / Table 1) ──────────────────────────────
        // TemplatePlaceholder must exactly match text inside BARTOK_S8_SOP_Template_v2.docx.
        new("dc_process_name",    "Document Control",            "Process Name",
            "[Process Name]",
            "ProcessName"),

        new("dc_lot",             "Document Control",            "Lot Number and Name",
            "[Lot Number and Name]",
            "SdcLots"),

        new("dc_date",            "Document Control",            "Document Date",
            "[Add date in dd-mmm-yyyy format e.g. 01-May-2026]",
            "TODAY"),

        new("dc_author",          "Document Control",            "Process Author",
            "[Process Author Name | Email]",
            "ProcessOwnerContact"),

        new("dc_approver",        "Document Control",            "Approver",
            "[Approver Name | Email]",
            "AI:approver"),

        // ── 1.2 Scope ─────────────────────────────────────────────────────────
        new("dc_countries",       "1.2 Scope",                   "Countries in Scope",
            "[List all countries where this process operates]",
            "Country"),

        // ── 1.2 Inputs & Artefacts ────────────────────────────────────────────
        new("artefact_doc1",      "1.2 Inputs & Artefacts",      "Document / Artefact #1",
            "Enter document name...",
            "UploadedFileName"),

        // ── 2. Process Overview ────────────────────────────────────────────────
        new("po_description",     "2. Process Overview",         "Process Description",
            "Detailed description",
            "AI:processDescription"),

        new("po_owner",           "2. Process Overview",         "Process Owner",
            "[Name, Role, OB]",
            "ProcessOwnerContact"),

        new("po_volumes",         "2. Process Overview",         "Monthly Volumes",
            "[RACI Sharepoint]",
            "AI:monthlyVolumes"),

        new("po_peak_volume",     "2. Process Overview",         "Peak Volume",
            "[Peak volume and period \u2014 e.g. month-end, quarter-end]",
            "AI:peakVolume"),

        new("po_hours_weekday",   "2. Process Overview",         "Weekday Hours",
            "[Hours, e.g. 08:00\u201318:00 local time]",
            "AI:hoursWeekday"),

        new("po_hours_weekend",   "2. Process Overview",         "Weekend Hours",
            "[Hours \u2014 or \u2014 Not Operational]",
            "AI:hoursWeekend"),

        new("po_hours_holiday",   "2. Process Overview",         "Public Holiday Cover",
            "[Cover arrangements]",
            "AI:hoursHoliday"),

        new("po_systems",         "2. Process Overview",         "Systems Used",
            "[Primary systems \u2014 ERP, ticketing, reporting tools]",
            "AI:systemsUsed"),

        // ── 3. RACI ───────────────────────────────────────────────────────────
        // Single consolidated field — the LLM returns the complete RACI table as
        // structured text; GenerateReportAsync inserts it into the RACI table via
        // table-manipulation (data rows cleared, merged-cell LLM block inserted).
        new("raci_content",       "3. RACI",                     "RACI Assignments (LLM Response)",
            "",                    // no find-and-replace placeholder; handled via table manipulation
            "AI:raciContent"),

        // ── 4. Standard Operating Procedure ───────────────────────────────────
        // Single consolidated field — the LLM returns all SOP steps as structured
        // text; GenerateReportAsync inserts it into the SOP table after clearing
        // all template data rows (keeping the header + instruction rows).
        new("sop_content",        "4. SOP",                      "SOP Steps (LLM Response)",
            "",                    // no find-and-replace placeholder; handled via table manipulation
            "AI:sopContent"),

        // ── 5. Work Instructions ──────────────────────────────────────────────
        new("wi_step_name",       "5. Work Instructions",         "Step Name",
            "[Step Name]",
            "AI:wiStepName"),

        new("wi_instr1a",         "5. Work Instructions",         "Step 1 — Instruction 1",
            "[Instruction 1 \u2014 system navigation, field entries, validation checks]",
            "AI:wiInstruction1a"),

        new("wi_instr1b",         "5. Work Instructions",         "Step 1 — Instruction 2",
            "[Instruction 2]",
            "AI:wiInstruction1b"),

        new("wi_instr1c",         "5. Work Instructions",         "Step 1 — Error Handling",
            "[Instruction 3 \u2014 what to do if an error occurs]",
            "AI:wiErrorInstruction"),

        new("wi_instr2a",         "5. Work Instructions",         "Step 2 — Instruction 1",
            "[Instruction 1]",
            "AI:wiInstruction2a"),

        // ── 6.1 Escalation Matrix ─────────────────────────────────────────────
        new("esc_trigger",        "6.1 Escalation",               "Escalation Trigger",
            "[Trigger]",
            "AI:escalationTrigger"),

        new("esc_path",           "6.1 Escalation",               "Escalation Path",
            "[Who to notify and how]",
            "AI:escalationPath"),

        new("esc_timeframe",      "6.1 Escalation",               "Escalation Timeframe",
            "[Within X hours]",
            "AI:escalationTimeframe"),

        new("esc_target",         "6.1 Escalation",               "Resolution Target",
            "[Target]",
            "AI:escalationTarget"),

        // ── 6.2 Exception Handling ────────────────────────────────────────────
        new("exc_type",           "6.2 Exceptions",               "Exception Type",
            "[Exception type]",
            "AI:exceptionType"),

        new("exc_handling",       "6.2 Exceptions",               "Handling Approach",
            "[How to handle]",
            "AI:exceptionHandling"),

        new("exc_approval",       "6.2 Exceptions",               "Approval Required",
            "[Yes/No \u2014 who approves]",
            "AI:exceptionApproval"),

        // ── 7.1 Service Level Agreements ──────────────────────────────────────
        new("sla_metric",         "7.1 SLAs",                     "SLA Metric",
            "[Metric name]",
            "AI:slaMetric"),

        new("sla_measurement",    "7.1 SLAs",                     "Measurement Method",
            "[How measured]",
            "AI:slaMeasurement"),

        new("sla_frequency",      "7.1 SLAs",                     "Reporting Frequency",
            "[Frequency]",
            "AI:slaFrequency"),

        new("sla_tool",           "7.1 SLAs",                     "Measurement Tool",
            "[Tool name]",
            "AI:slaTool"),

        // ── 7.2 Actual vs Target Performance ──────────────────────────────────
        new("sla_metric_perf",    "7.2 Performance",              "Performance Metric",
            "[Metric]",
            "AI:perfMetric"),

        new("sla_actual_perf",    "7.2 Performance",              "Actual Performance",
            "[Actual]",
            "AI:perfActual"),

        // ── 8. Volumetrics ────────────────────────────────────────────────────
        // Single consolidated field — the LLM returns month-by-month volume data as
        // structured text; GenerateReportAsync inserts it into the Volumetrics table
        // after clearing all template data rows (keeping only the header row).
        new("vol_content",        "8. Volumetrics",               "Monthly Volume Data (LLM Response)",
            "",                    // no find-and-replace placeholder; handled via table manipulation
            "AI:volContent"),

        // ── 9. Regulatory and Compliance ──────────────────────────────────────
        new("reg_regulation",     "9. Regulatory",                "Regulation / Standard",
            "[Regulation]",
            "AI:regulation"),

        new("reg_obligation",     "9. Regulatory",                "Obligation",
            "[Obligation]",
            "AI:regObligation"),

        new("reg_control",        "9. Regulatory",                "Control in Process",
            "[How process meets it]",
            "AI:regControl"),

        new("reg_evidence",       "9. Regulatory",                "Evidence Artefact",
            "[Document / log / report]",
            "AI:regEvidence"),

        new("techm_framework",    "9. Regulatory",                "TechM Framework Reference",
            "[TechM Control Framework Document Reference]",
            "AI:techMFramework"),

        // ── 10. Training Materials ────────────────────────────────────────────
        new("train_module",       "10. Training",                 "Training Module",
            "[Module name]",
            "AI:trainingModule"),

        new("train_delivery",     "10. Training",                 "Delivery Method",
            "[Classroom / e-learning / on-the-job]",
            "AI:trainingDelivery"),

        new("train_verification", "10. Training",                 "Competency Verification",
            "[Assessment / sign-off / observation]",
            "AI:trainingVerification"),

        // ── 11. Orange Customer Contract Obligations ───────────────────────────
        new("occ_ref",            "11. OCC",                      "OCC Reference",
            "[OCC Ref \u2014 provided by OBI]",
            "AI:occRef"),

        new("occ_obligation",     "11. OCC",                      "OCC Obligation",
            "[Obligation from Orange Customer Contract]",
            "AI:occObligation"),

        new("occ_control",        "11. OCC",                      "OCC Control",
            "[How this process addresses the obligation]",
            "AI:occControl"),
    ];

    // Matches any remaining [placeholder] style text left over in the template.
    private static readonly Regex RemainingPlaceholderRegex =
        new(@"\[.*?\]", RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches genuine month-pointer output (e.g. "- Jan-25:" or "- Feb-2025:").
    private static readonly Regex MonthBulletRegex =
        new(@"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)-\d{2,4}\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches any ASCII digit — used to detect whether real numeric volume data
    // remains after stripping month-year tokens from a volume field value.
    private static readonly Regex AnyDigitRegex =
        new(@"\d", RegexOptions.Compiled);

    // Matches the opening line of a SOP step block: "Step N: text" or "Step N- text"
    private static readonly Regex SopStepLineRegex =
        new(@"^Step\s+(\d+)\s*[:\-]\s*(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Maximum number of words allowed in a RACI task header cell.
    // Longer names are trimmed to this word count to prevent cells overflowing.
    private const int MaxRaciTaskNameWords = 4;

    public IReadOnlyList<FieldDefinition> GetFieldDefinitions() => FieldDefs.AsReadOnly();

    public Task<byte[]> GenerateReportAsync(
        IntakeRecord intake, IList<ReportFieldStatus> fieldStatuses, string templatePath)
    {
        var templateBytes = File.ReadAllBytes(templatePath);
        using var ms = new MemoryStream();
        ms.Write(templateBytes, 0, templateBytes.Length);
        ms.Position = 0;

        // Build a lookup of the canonical TemplatePlaceholders from FieldDefs so that
        // stale DB copies (written when code had different placeholder text) are never used.
        var fieldDefLookup = FieldDefs.ToDictionary(f => f.Key, f => f.TemplatePlaceholder,
            StringComparer.Ordinal);

        // Build replacement dictionary: placeholder → fill value
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fs in fieldStatuses)
        {
            var value = fs.IsNA ? "N/A" : (fs.FillValue ?? string.Empty);

            // ── Guard: strip known template-instruction text from po_volumes ──────
            // The LLM can mistakenly extract Word-template guidance text (e.g.
            // "Enter actual transaction volume for each of the past 12 months:
            //  | 3. Roles and Responsibilities (RACI)") verbatim from uploaded docs.
            // Catch that here so the Word report always gets clean output.
            if (fs.FieldKey == "po_volumes" && !fs.IsNA)
                value = SanitizeMonthlyVolumes(value);

            // Prefer the current FieldDefs placeholder; fall back to the DB-stored one for
            // any field whose key is no longer in FieldDefs (orphaned legacy record).
            var placeholder = fieldDefLookup.TryGetValue(fs.FieldKey, out var fp)
                ? fp : fs.TemplatePlaceholder;
            if (!string.IsNullOrEmpty(placeholder))
                replacements[placeholder] = value;
        }

        // Ensure every FieldDef placeholder is in the dictionary — fields with no
        // ReportFieldStatus record yet would otherwise be left as raw placeholder text.
        foreach (var fd in FieldDefs)
        {
            if (!string.IsNullOrEmpty(fd.TemplatePlaceholder) &&
                !replacements.ContainsKey(fd.TemplatePlaceholder))
                replacements[fd.TemplatePlaceholder] = string.Empty;
        }

        using var wordDoc = WordprocessingDocument.Open(ms, isEditable: true);

        // Process main body
        ApplyReplacements(wordDoc.MainDocumentPart!.Document.Body!, replacements);

        // Process headers and footers
        foreach (var headerPart in wordDoc.MainDocumentPart.HeaderParts)
            ApplyReplacements(headerPart.Header, replacements);
        foreach (var footerPart in wordDoc.MainDocumentPart.FooterParts)
            ApplyReplacements(footerPart.Footer, replacements);

        // Final cleanup: erase any remaining [placeholder] text that was not covered
        // by a known field definition (e.g. template sections not yet mapped).
        ClearRemainingPlaceholders(wordDoc.MainDocumentPart!.Document.Body!);
        foreach (var headerPart in wordDoc.MainDocumentPart.HeaderParts)
            ClearRemainingPlaceholders(headerPart.Header);
        foreach (var footerPart in wordDoc.MainDocumentPart.FooterParts)
            ClearRemainingPlaceholders(footerPart.Footer);

        // ── Structured section insertion ───────────────────────────────────────
        // For RACI, SOP, Volumetrics, and Work Instructions the template has
        // multiple rows/paragraphs with identical placeholder text, causing
        // duplicate content when using simple find-and-replace. Instead we
        // clear all data rows/paragraphs and paste the LLM's checkpoint response
        // as a single merged-cell block (or paragraph block) per section.
        var body = wordDoc.MainDocumentPart!.Document.Body!;

        var raciValue = GetFieldFillValue(fieldStatuses, "raci_content");
        if (!string.IsNullOrWhiteSpace(raciValue))
            ReplaceRaciTable(body, raciValue);

        var sopValue = GetFieldFillValue(fieldStatuses, "sop_content");
        if (!string.IsNullOrWhiteSpace(sopValue))
            ReplaceSopTableRows(body, sopValue);

        // ── Work Instructions 5 paragraph-block insertion ──────────────────────
        // Section 5 is structured as paragraphs (not a table): a "5. Work
        // Instructions" heading followed by step sub-headings "5.1 Step 1 —
        // [Step Name]" and instruction paragraphs. The same placeholder [Step Name]
        // appears in BOTH 5.1 and 5.2, so find-and-replace fills both sub-headings
        // with the same value. We remove all step paragraphs and insert a single
        // formatted block from the existing wi_* field values — same principle as
        // the Volumetrics merged-cell approach.
        var wiStepName = GetFieldFillValue(fieldStatuses, "wi_step_name") ?? string.Empty;
        var wiInstr1a  = GetFieldFillValue(fieldStatuses, "wi_instr1a")   ?? string.Empty;
        var wiInstr1b  = GetFieldFillValue(fieldStatuses, "wi_instr1b")   ?? string.Empty;
        var wiInstr1c  = GetFieldFillValue(fieldStatuses, "wi_instr1c")   ?? string.Empty;
        var wiInstr2a  = GetFieldFillValue(fieldStatuses, "wi_instr2a")   ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(wiStepName) || !string.IsNullOrWhiteSpace(wiInstr1a))
        {
            var wiContent = string.Empty;
            if (!string.IsNullOrWhiteSpace(wiStepName))
                wiContent += $"Step 1 — {wiStepName}";
            if (!string.IsNullOrWhiteSpace(wiInstr1a))
                wiContent += (wiContent.Length > 0 ? "\n" : "") + $"Instruction 1: {wiInstr1a}";
            if (!string.IsNullOrWhiteSpace(wiInstr1b))
                wiContent += $"\nInstruction 2: {wiInstr1b}";
            if (!string.IsNullOrWhiteSpace(wiInstr1c))
                wiContent += $"\nError Handling: {wiInstr1c}";
            if (!string.IsNullOrWhiteSpace(wiInstr2a))
                wiContent += $"\n\nStep 2 — Instruction 1: {wiInstr2a}";
            ReplaceWorkInstructionParagraphs(body, wiContent);
        }

        var volValue = GetFieldFillValue(fieldStatuses, "vol_content");
        if (!string.IsNullOrWhiteSpace(volValue))
            ReplaceTableDataWithLlmContent(
                body,
                headerKeywords : ["Month", "Transaction"],  // Volumetrics header keywords
                keepHeaderRows : 1,
                content        : volValue);

        // ── SLA 7.1 merged-cell insertion ──────────────────────────────────────
        // The individual sla_* fields store all metric values as a single
        // pipe-separated string (e.g. "Metric 1 | Metric 2 | ..."). Simple
        // find-and-replace packs every metric into one cell, making the table
        // unreadable.  Clear all data rows and insert the combined content as a
        // single merged-cell block — identical to the Volumetrics approach.
        var slaMetric = GetFieldFillValue(fieldStatuses, "sla_metric") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(slaMetric))
        {
            var slaMeasurement = GetFieldFillValue(fieldStatuses, "sla_measurement") ?? string.Empty;
            var slaFrequency   = GetFieldFillValue(fieldStatuses, "sla_frequency")   ?? string.Empty;
            var slaTool        = GetFieldFillValue(fieldStatuses, "sla_tool")        ?? string.Empty;
            var slaContent = $"Metric: {slaMetric}"
                + (string.IsNullOrWhiteSpace(slaMeasurement) ? "" : $"\nMeasurement Method: {slaMeasurement}")
                + (string.IsNullOrWhiteSpace(slaFrequency)   ? "" : $"\nReporting Frequency: {slaFrequency}")
                + (string.IsNullOrWhiteSpace(slaTool)        ? "" : $"\nMeasurement Tool: {slaTool}");
            ReplaceTableDataWithLlmContent(
                body,
                headerKeywords : ["Metric", "Measurement"],  // SLA 7.1 header keywords
                keepHeaderRows : 1,
                content        : slaContent);
        }

        // ── Actual vs Target Performance 7.2 merged-cell insertion ─────────────
        // Same issue as SLA 7.1: perf fields pack all metric data into one cell.
        // Use month-name keywords (Sep, Oct) to identify this table uniquely since
        // both the SLA and Performance tables contain "Metric" and "Target".
        var perfMetric = GetFieldFillValue(fieldStatuses, "sla_metric_perf") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(perfMetric))
        {
            var perfActual = GetFieldFillValue(fieldStatuses, "sla_actual_perf") ?? string.Empty;
            var perfContent = $"Metric: {perfMetric}"
                + (string.IsNullOrWhiteSpace(perfActual) ? "" : $"\nActual Performance: {perfActual}");
            ReplaceTableDataWithLlmContent(
                body,
                headerKeywords : ["Sep", "Oct"],  // month-name headers unique to Performance 7.2
                keepHeaderRows : 1,
                content        : perfContent);
        }

        // ── Deduplication pass ─────────────────────────────────────────────────
        // Several template tables (Escalation Matrix, Exception Handling, SLA 7.1,
        // Performance 7.2) have multiple data rows with identical placeholder text.
        // After find-and-replace every such row carries the same value, producing
        // visually doubled rows. Remove any consecutive duplicate data rows across
        // all tables — the structured sections above (RACI, SOP, Vol) have already
        // been rebuilt with clean, unique rows so they are unaffected.
        RemoveDuplicateDataRows(body);

        // ── Strip all Word reviewer comments ──────────────────────────────────
        // The source template contains reviewer comments that must not appear in
        // delivered documents. Remove the comments part, all in-text comment
        // reference anchors, and any comment-mark runs.
        RemoveAllComments(wordDoc);

        wordDoc.MainDocumentPart.Document.Save();
        wordDoc.Dispose();

        return Task.FromResult(ms.ToArray());
    }

    /// <summary>
    /// Returns the fill value for a given field key, respecting the N/A flag.
    /// Returns null if the field has no status record.
    /// </summary>
    private static string? GetFieldFillValue(IList<ReportFieldStatus> fieldStatuses, string key)
    {
        var fs = fieldStatuses.FirstOrDefault(f => f.FieldKey == key);
        if (fs == null) return null;
        return fs.IsNA ? "N/A" : fs.FillValue;
    }

    // Known instruction/placeholder strings that the LLM sometimes copies verbatim
    // from uploaded Word documents that contain old BARTOK template content.
    private static readonly string[] VolumeInstructionPatterns =
    [
        "Enter actual transaction volume",
        "RACI SharePoint",
        "RACI Checkpoint",
        "3. Roles and Responsibilities",
        "Record actual transaction volumes",
        "[Transaction type(s) and volume",
    ];

    private const string VolumeFallback =
        "Volume data to be confirmed with process owner — upload Excel/volume file and regenerate.";

    /// <summary>
    /// Detects when the AI-extracted value for <c>po_volumes</c> is actually
    /// template instruction text (e.g. from an old BARTOK Word template uploaded
    /// as a task artifact) and replaces it with the proper fallback message.
    /// Genuine bullet-pointer output (lines starting with "- ") is passed through unchanged.
    /// </summary>
    private static string SanitizeMonthlyVolumes(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        // Genuine output: any value that contains month abbreviations (e.g. "Jan-25", "Feb-25")
        // AND at least one actual numeric volume figure passes through.
        // Strip all month tokens first so that year digits ("2025", "25") in those tokens
        // don't count as volume figures — then check whether any digit remains.
        // This catches the common failure mode where the LLM outputs the bullet format with
        // empty values ("- Jan-2025: Received  | Handled") because the Excel cell values
        // were blank during extraction.
        if (MonthBulletRegex.IsMatch(value))
        {
            var withoutMonthTokens = MonthBulletRegex.Replace(value, "");
            if (AnyDigitRegex.IsMatch(withoutMonthTokens))
                return value;  // real numeric volume data is present — pass through

            // Month tokens present but no actual numbers → LLM produced the format
            // template with empty Received/Handled slots.  Use the fallback.
            return VolumeFallback;
        }

        // If the value matches any known instruction/template pattern, discard it.
        foreach (var pattern in VolumeInstructionPatterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return VolumeFallback;
        }

        return value;
    }

    /// <summary>
    /// Parses a structured RACI matrix (produced by the LLM) and inserts it
    /// into the RACI table in the DOCX body as proper individual-cell table rows.
    /// Expected format:
    ///   Line 1: TASKS: [Task 1] ; [Task 2] ; [Task 3] ; [Task 4]
    ///   Line N: [Role name]: R | - | A | -
    /// The TASKS: line uses semicolons to separate task names (avoiding collision with
    /// the pipe characters that appear inside the source RACI column header descriptions).
    /// Pipe-separated TASKS: lines are also accepted for backward compatibility.
    /// Falls back to <see cref="ReplaceTableDataWithLlmContent"/> when parsing fails.
    /// </summary>
    private static void ReplaceRaciTable(Body body, string raciContent)
    {
        // ── Parse structured format ────────────────────────────────────────────
        var lines = raciContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string[]? taskNames = null;
        var roleRows = new List<(string RoleName, string[] Assignments)>();

        foreach (var line in lines)
        {
            if (line.StartsWith("TASKS:", StringComparison.OrdinalIgnoreCase))
            {
                var tasksPart = line["TASKS:".Length..];

                // Prefer semicolon separator (avoids collision with in-cell pipe bullets).
                // Fall back to pipe separator for responses that pre-date this format change.
                var separator = tasksPart.Contains(';') ? ';' : '|';

                taskNames = tasksPart
                    .Split(separator, StringSplitOptions.TrimEntries)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    // Truncate as a safety net: if the LLM still copies a responsibility
                    // bullet list (containing " | ") as the task name, shorten it.
                    .Select(ShortenTaskName)
                    .ToArray();
            }
            else
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx > 0 && colonIdx < line.Length - 1)
                {
                    var roleName    = line[..colonIdx].Trim();
                    var assignments = line[(colonIdx + 1)..]
                        .Split('|', StringSplitOptions.TrimEntries)
                        .ToArray();
                    if (roleName.Length > 0 && assignments.Length > 0)
                        roleRows.Add((roleName, assignments));
                }
            }
        }

        // Fall back to merged-cell approach if the LLM did not use the structured format.
        if (taskNames == null || roleRows.Count == 0)
        {
            ReplaceTableDataWithLlmContent(body, ["Role", "Task"], 1, raciContent);
            return;
        }

        // ── Locate RACI table ──────────────────────────────────────────────────
        var table = body.Descendants<Table>().FirstOrDefault(t =>
        {
            var firstRow = t.Descendants<TableRow>().FirstOrDefault();
            if (firstRow == null) return false;
            var text = string.Concat(firstRow.Descendants<Text>().Select(x => x.Text));
            return text.Contains("Role", StringComparison.OrdinalIgnoreCase)
                && text.Contains("Task", StringComparison.OrdinalIgnoreCase);
        });

        if (table == null) return;

        var allRows = table.Elements<TableRow>().ToList();
        if (allRows.Count == 0) return;

        // ── Update header row with actual task names ───────────────────────────
        var headerCells = allRows[0].Elements<TableCell>().ToList();
        for (int i = 0; i < taskNames.Length && i + 1 < headerCells.Count; i++)
            SetCellText(headerCells[i + 1], taskNames[i]);

        // ── Remove all data rows ───────────────────────────────────────────────
        foreach (var row in allRows.Skip(1))
            row.Remove();

        // ── Add one data row per role ──────────────────────────────────────────
        foreach (var (roleName, assignments) in roleRows)
        {
            var dataRow = new TableRow();

            // Role name (first column)
            dataRow.AppendChild(BuildRaciCell(roleName,
                headerCells.Count > 0 ? headerCells[0] : null));

            // Assignment cells for each task column
            for (int col = 0; col < headerCells.Count - 1; col++)
            {
                var assignment = col < assignments.Length ? assignments[col] : "-";
                var templateCell = col + 1 < headerCells.Count ? headerCells[col + 1] : null;
                dataRow.AppendChild(BuildRaciCell(assignment, templateCell));
            }

            table.AppendChild(dataRow);
        }
    }

    /// <summary>Overwrites all text content in a table cell with <paramref name="text"/>.</summary>
    private static void SetCellText(TableCell cell, string text)
    {
        bool isFirst = true;
        foreach (var t in cell.Descendants<Text>())
        {
            t.Text = isFirst ? text : string.Empty;
            isFirst = false;
        }

        // No existing Text nodes — append a paragraph with the value.
        if (isFirst)
            cell.AppendChild(new Paragraph(new Run(new Text(text))));
    }

    /// <summary>
    /// Creates a <see cref="TableCell"/> with <paramref name="text"/>, optionally
    /// inheriting cell/run/paragraph properties from <paramref name="templateCell"/>.
    /// </summary>
    private static TableCell BuildRaciCell(string text, TableCell? templateCell)
    {
        var cell = new TableCell();

        // Copy cell-level properties (borders, shading, width) from the template cell.
        if (templateCell?.TableCellProperties is { } srcCellProps)
        {
            var cloned = (TableCellProperties)srcCellProps.CloneNode(deep: true);
            cloned.RemoveAllChildren<GridSpan>();   // each cell must span exactly 1 column
            cell.AppendChild(cloned);
        }
        else
        {
            cell.AppendChild(new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Auto, Width = "0" }));
        }

        var para = new Paragraph();

        // Copy paragraph properties (alignment, spacing) from the template.
        if (templateCell?.Descendants<ParagraphProperties>().FirstOrDefault() is { } srcParaProps)
            para.AppendChild((ParagraphProperties)srcParaProps.CloneNode(deep: true));

        var run = new Run();

        // Copy run properties (bold, font, size) from the template.
        if (templateCell?.Descendants<RunProperties>().FirstOrDefault() is { } srcRunProps)
            run.AppendChild((RunProperties)srcRunProps.CloneNode(deep: true));

        run.AppendChild(new Text(text)
        {
            Space = text.StartsWith(' ') || text.EndsWith(' ')
                ? DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve
                : DocumentFormat.OpenXml.SpaceProcessingModeValues.Default
        });

        para.AppendChild(run);
        cell.AppendChild(para);
        return cell;
    }

    /// <summary>
    /// Reduces a RACI task name to at most <see cref="MaxRaciTaskNameWords"/> words.
    /// When the LLM outputs full activity descriptions as the task name, this trims it to
    /// a concise label that fits in a table header cell.
    /// </summary>
    private static string ShortenTaskName(string name)
    {
        var trimmed = name.Trim();
        var words   = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= MaxRaciTaskNameWords
            ? trimmed
            : string.Join(" ", words.Take(MaxRaciTaskNameWords));
    }

    // ── SOP table row-by-row insertion ────────────────────────────────────────

    /// <summary>
    /// One parsed SOP step from the LLM's structured sopContent block.
    /// </summary>
    private sealed record SopStep(
        string StepNumber,
        string Action,
        string Role,
        string System,
        string Output,
        string AutoStatus,
        string OppRating,
        string AutoType);

    /// <summary>
    /// Extracts the value for <paramref name="key"/> from a pipe-separated
    /// "Key1: value1 | Key2: value2" line. Returns empty string when not found.
    /// </summary>
    private static string ExtractSopPart(string line, string key)
    {
        var parts = line.Split('|');
        foreach (var part in parts)
        {
            var colonIdx = part.IndexOf(':', StringComparison.Ordinal);
            if (colonIdx < 0) continue;
            var partKey = part[..colonIdx].Trim();
            if (partKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                return part[(colonIdx + 1)..].Trim();
        }
        return string.Empty;
    }

    /// <summary>
    /// Parses the LLM's structured sopContent block into individual <see cref="SopStep"/> records.
    /// Expected format per step:
    ///   Step N: [Action]
    ///     Role: [Role] | System: [System] | Output: [Output]
    ///     Automation: [status] | Rating: [rating] | Type: [type]
    /// Returns an empty list when the format is not recognised.
    /// </summary>
    private static List<SopStep> ParseSopSteps(string sopContent)
    {
        var steps  = new List<SopStep>();
        var lines  = sopContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string stepNum = "", action = "", role = "", system = "", output = "";
        string autoStatus = "Manual", rating = "Low", autoType = "N/A";
        bool inStep = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // "Step N: Action text"
            var m = SopStepLineRegex.Match(line);
            if (m.Success)
            {
                if (inStep)
                    steps.Add(new SopStep(stepNum, action, role, system, output, autoStatus, rating, autoType));

                stepNum    = "Step " + m.Groups[1].Value;
                action     = m.Groups[2].Value.Trim();
                role       = system = output = string.Empty;
                autoStatus = "Manual"; rating = "Low"; autoType = "N/A";
                inStep     = true;
                continue;
            }

            if (!inStep) continue;

            // "Role: X | System: Y | Output: Z"
            if (line.Contains("Role:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("System:", StringComparison.OrdinalIgnoreCase))
            {
                var r = ExtractSopPart(line, "Role");
                var s = ExtractSopPart(line, "System");
                var o = ExtractSopPart(line, "Output");
                if (!string.IsNullOrEmpty(r)) role   = r;
                if (!string.IsNullOrEmpty(s)) system = s;
                if (!string.IsNullOrEmpty(o)) output = o;
                continue;
            }

            // "Automation: X | Rating: Y | Type: Z"
            if (line.Contains("Automation:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Rating:", StringComparison.OrdinalIgnoreCase))
            {
                var a = ExtractSopPart(line, "Automation");
                var rt = ExtractSopPart(line, "Rating");
                var t = ExtractSopPart(line, "Type");
                if (!string.IsNullOrEmpty(a))  autoStatus = a;
                if (!string.IsNullOrEmpty(rt)) rating     = rt;
                if (!string.IsNullOrEmpty(t))  autoType   = t;
                continue;
            }
        }

        if (inStep)
            steps.Add(new SopStep(stepNum, action, role, system, output, autoStatus, rating, autoType));

        return steps;
    }

    /// <summary>
    /// Finds the SOP table in <paramref name="body"/> (identified by "Step" and "Action"
    /// in the first row), clears all data rows beyond the two header rows, then inserts
    /// one properly-structured table row per parsed SOP step — mapping step number, action,
    /// role, system, output, and automation fields to the correct column by header text.
    /// Falls back to the merged-cell approach when the sopContent cannot be parsed.
    /// </summary>
    private static void ReplaceSopTableRows(Body body, string sopContent)
    {
        var steps = ParseSopSteps(sopContent);
        if (steps.Count == 0)
        {
            // Fallback: merged-cell dump (preserves previous behaviour)
            ReplaceTableDataWithLlmContent(
                body, ["Step", "Action", "Auto Status"], 2, sopContent);
            return;
        }

        // Locate SOP table by header keywords
        var table = body.Descendants<Table>().FirstOrDefault(t =>
        {
            var firstRow = t.Descendants<TableRow>().FirstOrDefault();
            if (firstRow == null) return false;
            var txt = string.Concat(firstRow.Descendants<Text>().Select(x => x.Text));
            return txt.Contains("Step",   StringComparison.OrdinalIgnoreCase)
                && txt.Contains("Action", StringComparison.OrdinalIgnoreCase);
        });

        if (table == null) return;

        var allRows = table.Elements<TableRow>().ToList();
        if (allRows.Count == 0) return;

        // Determine column positions from the first header row
        var headerCells = allRows[0].Elements<TableCell>().ToList();
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerCells.Count; i++)
        {
            var txt = string.Concat(headerCells[i].Descendants<Text>().Select(x => x.Text));
            if (txt.Contains("Step",   StringComparison.OrdinalIgnoreCase) && !colMap.ContainsKey("step"))
                colMap["step"]   = i;
            else if (txt.Contains("Action", StringComparison.OrdinalIgnoreCase) && !colMap.ContainsKey("action"))
                colMap["action"] = i;
            else if (txt.Contains("Role",   StringComparison.OrdinalIgnoreCase) && !colMap.ContainsKey("role"))
                colMap["role"]   = i;
            else if (txt.Contains("System", StringComparison.OrdinalIgnoreCase) && !colMap.ContainsKey("system"))
                colMap["system"] = i;
            else if (txt.Contains("Output", StringComparison.OrdinalIgnoreCase) && !colMap.ContainsKey("output"))
                colMap["output"] = i;
            else if ((txt.Contains("Auto Status", StringComparison.OrdinalIgnoreCase)
                   || txt.Contains("Automation",  StringComparison.OrdinalIgnoreCase))
                && !colMap.ContainsKey("auto"))
                colMap["auto"]   = i;
            else if ((txt.Contains("Rating", StringComparison.OrdinalIgnoreCase)
                   || txt.Contains("Opp",    StringComparison.OrdinalIgnoreCase))
                && !colMap.ContainsKey("rating"))
                colMap["rating"] = i;
            else if (txt.Contains("Type",   StringComparison.OrdinalIgnoreCase) && !colMap.ContainsKey("type"))
                colMap["type"]   = i;
        }

        int colCount = headerCells.Count;

        // Use the instruction row (index 1) as the formatting template for data rows
        var templateRow   = allRows.Count > 1 ? allRows[1] : allRows[0];
        var templateCells = templateRow.Elements<TableCell>().ToList();

        // Remove all data rows beyond the 2 header rows
        foreach (var row in allRows.Skip(2))
            row.Remove();

        // Insert one row per step
        foreach (var step in steps)
        {
            var cellValues = new string[colCount];
            for (int i = 0; i < colCount; i++) cellValues[i] = string.Empty;

            if (colMap.TryGetValue("step",   out int si)) cellValues[si] = step.StepNumber;
            if (colMap.TryGetValue("action", out int ai)) cellValues[ai] = step.Action;
            if (colMap.TryGetValue("role",   out int ri)) cellValues[ri] = step.Role;
            if (colMap.TryGetValue("system", out int sy)) cellValues[sy] = step.System;
            if (colMap.TryGetValue("output", out int oi)) cellValues[oi] = step.Output;
            if (colMap.TryGetValue("auto",   out int aui)) cellValues[aui] = step.AutoStatus;
            if (colMap.TryGetValue("rating", out int rati)) cellValues[rati] = step.OppRating;
            if (colMap.TryGetValue("type",   out int ti)) cellValues[ti]  = step.AutoType;

            var newRow = new TableRow();
            for (int c = 0; c < colCount; c++)
            {
                var tmplCell = c < templateCells.Count ? templateCells[c] : null;
                newRow.AppendChild(BuildRaciCell(cellValues[c], tmplCell));
            }
            table.AppendChild(newRow);
        }
    }

    /// <summary>
    /// Fixes the "5. Work Instructions" section which is structured as body paragraphs
    /// (not a table). The template contains sub-heading paragraphs "5.1 Step 1 —
    /// [Step Name]" and "5.2 Step 2 — [Step Name]" that share the same placeholder, so
    /// find-and-replace fills every step heading with the same value. This method removes
    /// all paragraphs from the WI heading down to (but not including) the "6." section,
    /// then inserts <paramref name="content"/> as a formatted paragraph block — the same
    /// principle as the merged-cell approach used for Volumetrics and SOP.
    /// </summary>
    private static void ReplaceWorkInstructionParagraphs(Body body, string content)
    {
        var bodyChildren = body.ChildElements.ToList();

        int wiHeadingIdx   = -1;
        int nextSectionIdx = bodyChildren.Count;

        for (int i = 0; i < bodyChildren.Count; i++)
        {
            if (bodyChildren[i] is not Paragraph p) continue;
            var txt = string.Concat(p.Descendants<Text>().Select(t => t.Text));

            if (wiHeadingIdx < 0 &&
                txt.Contains("5. Work Instructions", StringComparison.OrdinalIgnoreCase))
            {
                wiHeadingIdx = i;
            }
            else if (wiHeadingIdx >= 0 && Regex.IsMatch(txt.TrimStart(), @"^6[\.\s]"))
            {
                nextSectionIdx = i;
                break;
            }
        }

        if (wiHeadingIdx < 0) return;

        // Remove all paragraphs after the WI heading up to (but not including) the "6." heading.
        for (int i = nextSectionIdx - 1; i > wiHeadingIdx; i--)
            bodyChildren[i].Remove();

        // After removals, the WI heading is still at wiHeadingIdx in the live DOM —
        // retrieve it fresh from the body.
        var headingPara = body.ChildElements.ElementAt(wiHeadingIdx) as Paragraph;
        if (headingPara == null) return;

        // Insert content lines as individual paragraphs after the WI heading.
        // Iterate in reverse so that each InsertAfterSelf call builds the list in order.
        var lines = content.Split('\n');
        foreach (var line in lines.Reverse())
        {
            var newPara  = new Paragraph();
            var textElem = new Text(line);
            if (line.StartsWith(' ') || line.EndsWith(' '))
                textElem.Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;
            newPara.AppendChild(new Run(textElem));
            headingPara.InsertAfterSelf(newPara);
        }
    }

    /// <summary>
    /// Finds the first table in <paramref name="body"/> whose first row contains ALL
    /// of the <paramref name="headerKeywords"/>, removes every data row beyond the
    /// first <paramref name="keepHeaderRows"/> rows, then appends a new merged-cell row
    /// containing <paramref name="content"/> (newlines become OOXML line-breaks).
    /// </summary>
    private static void ReplaceTableDataWithLlmContent(
        Body body,
        string[] headerKeywords,
        int keepHeaderRows,
        string content)
    {
        // Locate the target table by matching keywords against its first row.
        var table = body.Descendants<Table>().FirstOrDefault(t =>
        {
            var firstRow = t.Descendants<TableRow>().FirstOrDefault();
            if (firstRow == null) return false;
            var headerText = string.Concat(firstRow.Descendants<Text>().Select(x => x.Text));
            return headerKeywords.All(k =>
                headerText.Contains(k, StringComparison.OrdinalIgnoreCase));
        });

        if (table == null) return;

        // Remove all rows beyond the header section.
        var allRows = table.Elements<TableRow>().ToList();
        if (allRows.Count == 0) return;

        foreach (var row in allRows.Skip(keepHeaderRows))
            row.Remove();

        // Determine column count from the first header row for correct grid-span.
        int colCount = Math.Max(1, allRows.First().Elements<TableCell>().Count());

        // Build the new merged-cell content row.
        var newRow  = new TableRow();
        var newCell = new TableCell();

        var cellProps = new TableCellProperties();
        if (colCount > 1)
            cellProps.AppendChild(new GridSpan { Val = colCount });
        // Give the cell a full-width setting so Word's layout engine is happy.
        cellProps.AppendChild(new TableCellWidth
        {
            Type  = TableWidthUnitValues.Auto,
            Width = "0"
        });
        newCell.AppendChild(cellProps);

        // Build paragraph with line-break aware text.
        var para  = new Paragraph();
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                para.AppendChild(new Run(new Break()));

            var line = lines[i];
            if (!string.IsNullOrEmpty(line))
            {
                var textElem = new Text(line);
                if (line.StartsWith(' ') || line.EndsWith(' '))
                    textElem.Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;
                para.AppendChild(new Run(textElem));
            }
        }

        newCell.AppendChild(para);
        newRow.AppendChild(newCell);
        table.AppendChild(newRow);
    }

    /// <summary>
    /// Scans every table in <paramref name="body"/> and removes any data row whose
    /// text fingerprint is identical to the immediately preceding row in the same table.
    /// <para>
    /// This eliminates the doubled-row artefact that occurs when the DOCX template has
    /// multiple rows with the same placeholder text and find-and-replace fills them all
    /// with identical values (e.g. Escalation Matrix, Exception Handling, SLA 7.1, and
    /// Actual vs Target Performance 7.2).
    /// </para>
    /// A row whose cells are all blank or whitespace-only is never treated as a duplicate
    /// anchor — it resets the comparison so that a blank separator row followed by a real
    /// data row is never incorrectly removed.
    /// </summary>
    private static void RemoveDuplicateDataRows(Body body)
    {
        foreach (var table in body.Descendants<Table>())
        {
            var rows = table.Elements<TableRow>().ToList();
            string? lastFingerprint = null;
            foreach (var row in rows)
            {
                var fingerprint = string.Concat(row.Descendants<Text>().Select(t => t.Text));
                if (string.IsNullOrWhiteSpace(fingerprint))
                {
                    // Blank rows reset the tracking anchor so they never cause
                    // a following non-blank row to be treated as a duplicate.
                    lastFingerprint = null;
                    continue;
                }

                if (fingerprint == lastFingerprint)
                    row.Remove();   // identical to the preceding non-empty row → drop it
                else
                    lastFingerprint = fingerprint;
            }
        }
    }

    private static void ApplyReplacements(
        DocumentFormat.OpenXml.OpenXmlElement root,
        Dictionary<string, string> replacements)
    {
        // Partition into single-line and multi-line replacements so the fast path
        // handles most fields while multi-line values get proper <w:br/> treatment.
        var singleLine = replacements
            .Where(kvp => !kvp.Value.Contains('\n'))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var multiLine = replacements
            .Where(kvp => kvp.Value.Contains('\n'))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        // ── Pass 1: single-line replacement within individual text runs ────────
        foreach (var text in root.Descendants<Text>())
        {
            foreach (var kvp in singleLine)
            {
                if (text.Text.Contains(kvp.Key, StringComparison.Ordinal))
                    text.Text = text.Text.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);
            }
        }

        // ── Pass 2: single-line replacement across split runs in a paragraph ──
        // For each paragraph, merge all run texts, apply replacements, then write
        // the result into the first run and blank the rest.
        foreach (var para in root.Descendants<Paragraph>())
        {
            var allTexts = para.Descendants<Run>()
                               .SelectMany(r => r.Descendants<Text>())
                               .ToList();
            // Skip paragraphs with 0 or 1 text nodes — pass 1 already handled those.
            if (allTexts.Count <= 1) continue;

            var merged = string.Concat(allTexts.Select(t => t.Text));
            var replaced = merged;
            foreach (var kvp in singleLine)
                replaced = replaced.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);

            if (replaced == merged) continue;

            allTexts[0].Text = replaced;
            if (replaced.Length > 0 && (replaced[0] == ' ' || replaced[^1] == ' '))
                allTexts[0].Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;

            for (int i = 1; i < allTexts.Count; i++)
                allTexts[i].Text = string.Empty;
        }

        // ── Pass 3: multi-line replacement (inserts <w:br/> for each \n) ──────
        if (multiLine.Count == 0) return;

        foreach (var para in root.Descendants<Paragraph>().ToList())
        {
            var allRuns = para.Elements<Run>().ToList();
            if (allRuns.Count == 0) continue;

            var merged = string.Concat(
                allRuns.SelectMany(r => r.Descendants<Text>()).Select(t => t.Text));

            foreach (var kvp in multiLine)
            {
                if (!merged.Contains(kvp.Key, StringComparison.Ordinal)) continue;

                var newContent = merged.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);
                var lines = newContent.Split('\n');

                // Capture run properties for styling (font, size, bold, etc.).
                var rPr = allRuns.FirstOrDefault()?.GetFirstChild<RunProperties>();

                // Remove all existing runs; rebuild with line-break aware runs.
                foreach (var run in allRuns) run.Remove();

                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                    {
                        var brRun = new Run(new Break());
                        if (rPr != null) brRun.PrependChild((RunProperties)rPr.CloneNode(true));
                        para.AppendChild(brRun);
                    }

                    var line = lines[i];
                    if (!string.IsNullOrEmpty(line))
                    {
                        var textElem = new Text(line);
                        if (line.StartsWith(' ') || line.EndsWith(' '))
                            textElem.Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;
                        var textRun = new Run(textElem);
                        if (rPr != null) textRun.PrependChild((RunProperties)rPr.CloneNode(true));
                        para.AppendChild(textRun);
                    }
                }
                break; // Only one multi-line match per paragraph.
            }
        }
    }

    /// <summary>
    /// Removes any remaining [placeholder] style text from the document that was not
    /// matched by a known field definition. This handles template sections that have no
    /// corresponding field mapping — they are cleared rather than left as raw placeholder text.
    /// </summary>
    private static void ClearRemainingPlaceholders(DocumentFormat.OpenXml.OpenXmlElement root)
    {
        // First pass: clear within individual runs
        foreach (var text in root.Descendants<Text>())
        {
            if (RemainingPlaceholderRegex.IsMatch(text.Text))
                text.Text = RemainingPlaceholderRegex.Replace(text.Text, string.Empty);
        }

        // Second pass: handle placeholders split across multiple runs in a paragraph
        foreach (var para in root.Descendants<Paragraph>())
        {
            var allTexts = para.Descendants<Run>()
                               .SelectMany(r => r.Descendants<Text>())
                               .ToList();
            if (allTexts.Count <= 1) continue;

            var merged = string.Concat(allTexts.Select(t => t.Text));
            if (!RemainingPlaceholderRegex.IsMatch(merged)) continue;

            var cleared = RemainingPlaceholderRegex.Replace(merged, string.Empty);
            allTexts[0].Text = cleared;
            for (int i = 1; i < allTexts.Count; i++)
                allTexts[i].Text = string.Empty;
        }
    }

    /// <summary>
    /// Removes all Word reviewer comments from the generated document so that
    /// template author comments never appear in delivered reports.
    /// Deletes:
    ///   • the WordprocessingCommentsPart (the comment text store)
    ///   • every CommentRangeStart / CommentRangeEnd markup pair in the body
    ///   • every CommentReference run element in the body
    /// </summary>
    private static void RemoveAllComments(WordprocessingDocument wordDoc)
    {
        var mainPart = wordDoc.MainDocumentPart;
        if (mainPart == null) return;

        // 1. Drop the comments part (the sidebar panel content).
        if (mainPart.WordprocessingCommentsPart != null)
            mainPart.DeletePart(mainPart.WordprocessingCommentsPart);

        // 2. Remove in-body comment anchors from the document body, headers and footers.
        static void StripCommentMarkup(DocumentFormat.OpenXml.OpenXmlElement root)
        {
            // Remove CommentRangeStart and CommentRangeEnd elements.
            root.Descendants<CommentRangeStart>().ToList().ForEach(e => e.Remove());
            root.Descendants<CommentRangeEnd>().ToList().ForEach(e => e.Remove());

            // CommentReference lives inside a Run — remove the enclosing Run when it is
            // the only meaningful child (keeps formatting runs intact).
            foreach (var run in root.Descendants<Run>().ToList())
            {
                var children = run.ChildElements.ToList();
                if (children.Any(c => c is CommentReference))
                {
                    // If the run contains only RunProperties and a CommentReference, remove the whole run.
                    bool onlyCommentRef = children.All(c => c is RunProperties || c is CommentReference);
                    if (onlyCommentRef)
                        run.Remove();
                    else
                        run.Descendants<CommentReference>().ToList().ForEach(e => e.Remove());
                }
            }
        }

        StripCommentMarkup(mainPart.Document.Body!);
        foreach (var hp in mainPart.HeaderParts) StripCommentMarkup(hp.Header);
        foreach (var fp in mainPart.FooterParts)  StripCommentMarkup(fp.Footer);
    }
}
