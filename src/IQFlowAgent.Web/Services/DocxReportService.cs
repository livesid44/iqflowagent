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
            "Enter actual transaction volume for each of the past 12 months:",
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
        new("raci_task1",         "3. RACI",                     "RACI Task 1",
            "[Task 1]",
            "AI:raciTask1"),

        new("raci_task2",         "3. RACI",                     "RACI Task 2",
            "[Task 2]",
            "AI:raciTask2"),

        new("raci_task3",         "3. RACI",                     "RACI Task 3",
            "[Task 3]",
            "AI:raciTask3"),

        new("raci_task4",         "3. RACI",                     "RACI Task 4",
            "[Task 4]",
            "AI:raciTask4"),

        new("raci_role1",         "3. RACI",                     "RACI Role 1",
            "[Role 1]",
            "AI:raciRole1"),

        new("raci_role2",         "3. RACI",                     "RACI Role 2",
            "[Role 2]",
            "AI:raciRole2"),

        new("raci_role3",         "3. RACI",                     "RACI Role 3",
            "[Role 3]",
            "AI:raciRole3"),

        new("raci_role4",         "3. RACI",                     "RACI Role 4",
            "[Role 4]",
            "AI:raciRole4"),

        // ── 4. Standard Operating Procedure ───────────────────────────────────
        new("sop_action",         "4. SOP",                      "Step Action",
            "[Describe action]",
            "AI:sopAction"),

        new("sop_role",           "4. SOP",                      "Step Role",
            "[Role]",
            "AI:sopRole"),

        new("sop_system",         "4. SOP",                      "Step System",
            "[System / Manual]",
            "AI:sopSystem"),

        new("sop_output",         "4. SOP",                      "Step Output",
            "[Expected output]",
            "AI:sopOutput"),

        new("sop_decision",       "4. SOP",                      "Decision Point",
            "[Decision point \u2014 describe condition and Yes/No paths]",
            "AI:sopDecision"),

        new("sop_decision_out",   "4. SOP",                      "Decision Outcome",
            "[Go / No-Go / Escalate]",
            "AI:sopDecisionOutput"),

        new("sop_auto_status",    "4. SOP",                      "Automation Status",
            "Manual / Partially Automated / Fully Automated",
            "AI:sopAutoStatus"),

        new("sop_opp_rating",     "4. SOP",                      "Opportunity Rating",
            "Low / Medium / High / Prime",
            "AI:sopOppRating"),

        new("sop_auto_type",      "4. SOP",                      "Automation Type",
            "RPA / AI / Workflow / Integration / N/A",
            "AI:sopAutoType"),

        new("sop_extra_step",     "4. SOP",                      "Additional Step",
            "[Add rows as required]",
            "AI:sopExtraStep"),

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
        new("vol_transaction",    "8. Volumetrics",               "Transaction Volumes",
            "[Transaction type(s) and volume \u2014 add rows if multiple types]",
            "AI:volumeTransaction"),

        new("vol_note",           "8. Volumetrics",               "Volume Notes",
            "[Note any peak, anomaly or seasonal factor]",
            "AI:volumeNote"),

        new("vol_forecast",       "8. Volumetrics",               "Volume Forecast",
            "[Expected average monthly volume going forward]",
            "AI:volumeForecast"),

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

        wordDoc.MainDocumentPart.Document.Save();
        wordDoc.Dispose();

        return Task.FromResult(ms.ToArray());
    }

    private static void ApplyReplacements(
        DocumentFormat.OpenXml.OpenXmlElement root,
        Dictionary<string, string> replacements)
    {
        // First pass: replace within individual runs (fast path for unbroken placeholders)
        foreach (var text in root.Descendants<Text>())
        {
            foreach (var kvp in replacements)
            {
                if (text.Text.Contains(kvp.Key, StringComparison.Ordinal))
                    text.Text = text.Text.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);
            }
        }

        // Second pass: handle placeholders that Word has split across multiple runs.
        // For each paragraph, merge all run texts, apply replacements, then redistribute
        // the result by writing everything into the first run and clearing the rest.
        foreach (var para in root.Descendants<Paragraph>())
        {
            var allTexts = para.Descendants<Run>()
                               .SelectMany(r => r.Descendants<Text>())
                               .ToList();
            // Skip paragraphs with 0 or 1 text nodes — first pass already handled those
            if (allTexts.Count <= 1) continue;

            var merged = string.Concat(allTexts.Select(t => t.Text));
            var replaced = merged;
            foreach (var kvp in replacements)
                replaced = replaced.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);

            if (replaced == merged) continue;

            // Write the replaced value into the first Text element and blank the others.
            // Preserve xml:space="preserve" when the value has leading/trailing whitespace.
            allTexts[0].Text = replaced;
            if (replaced.Length > 0 && (replaced[0] == ' ' || replaced[^1] == ' '))
                allTexts[0].Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;

            for (int i = 1; i < allTexts.Count; i++)
                allTexts[i].Text = string.Empty;
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
