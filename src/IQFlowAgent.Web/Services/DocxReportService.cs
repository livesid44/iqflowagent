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
        // For RACI, SOP, and Volumetrics the template has multiple rows with
        // identical placeholder text, causing duplicate content when using simple
        // find-and-replace. Instead we clear all data rows and paste the LLM's
        // checkpoint response as a single merged-cell block per section.
        var body = wordDoc.MainDocumentPart!.Document.Body!;

        var raciValue = GetFieldFillValue(fieldStatuses, "raci_content");
        if (!string.IsNullOrWhiteSpace(raciValue))
            ReplaceTableDataWithLlmContent(
                body,
                headerKeywords : ["Role", "Task"],  // RACI header row keywords
                keepHeaderRows : 1,
                content        : raciValue);

        var sopValue = GetFieldFillValue(fieldStatuses, "sop_content");
        if (!string.IsNullOrWhiteSpace(sopValue))
            ReplaceTableDataWithLlmContent(
                body,
                headerKeywords : ["Step", "Action", "Auto Status"],  // SOP header keywords
                keepHeaderRows : 2,  // column-header row + instruction row
                content        : sopValue);

        var volValue = GetFieldFillValue(fieldStatuses, "vol_content");
        if (!string.IsNullOrWhiteSpace(volValue))
            ReplaceTableDataWithLlmContent(
                body,
                headerKeywords : ["Month", "Transaction"],  // Volumetrics header keywords
                keepHeaderRows : 1,
                content        : volValue);

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
        "RACI Sharepoint",
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
        // is treated as real volume data and passed through as-is regardless of formatting.
        // The LLM may produce bullets ("- Jan-25: ...") or plain lines ("Jan-25: ...") —
        // both are valid.  None of the known-bad instruction strings contain month tokens,
        // so this check is safe.
        if (MonthBulletRegex.IsMatch(value))
            return value;

        // If the value matches any known instruction/template pattern, discard it.
        foreach (var pattern in VolumeInstructionPatterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return VolumeFallback;
        }

        return value;
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
}
