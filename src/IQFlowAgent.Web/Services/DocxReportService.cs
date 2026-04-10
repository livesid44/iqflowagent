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
        // ── Document Control (Table 0) ─────────────────────────────────────────
        // TemplatePlaceholder must exactly match text inside BARTOK_DD_Template.docx.
        // Author and Approver share the same placeholder text in the template, so they
        // are handled via row-label lookup in GenerateReportAsync (empty placeholder here).
        new("dc_process_name",    "Document Control",            "Process Name",
            "[Process Name as per Intake]",
            "ProcessName"),

        new("dc_date",            "Document Control",            "Document Date",
            "[Intake date]",
            "TODAY"),

        new("dc_author",          "Document Control",            "Process Author",
            "",                    // duplicate placeholder — updated via row-label lookup
            "ProcessOwnerContact"),

        new("dc_approver",        "Document Control",            "Approver",
            "",                    // duplicate placeholder — updated via row-label lookup
            "AI:approver"),

        new("dc_contributors",    "Document Control",            "Contributors",
            "[Person who has closed the tasks]",
            ""),

        // ── Version History (Table 1) ──────────────────────────────────────────
        new("dc_version_date",    "Document Control",            "Version Date",
            "[Date of This document creation]",
            "TODAY"),

        // ── 2. Process Overview (Table 3) ─────────────────────────────────────
        new("po_description",     "2. Process Overview",         "Process Description",
            "[From Intake]",
            "AI:processDescription"),

        new("po_owner",           "2. Process Overview",         "Process Owner",
            "[From Intake and all document]",
            "ProcessOwnerContact"),

        // ── 1.2 Inputs & Artefacts (paragraph) ────────────────────────────────
        // The LLM / artefact aggregation produces a formatted text list of all
        // uploaded documents; GenerateReportAsync replaces the paragraph placeholder.
        new("artefact_content",   "1.2 Artefacts",               "Documents & Artefacts Received",
            "[All Document uploaded in task or Name of document, date when uploaded in a table]",
            "UploadedFileName"),

        // ── 3. RACI (paragraph placeholder) ───────────────────────────────────
        // The LLM generates the complete RACI block; inserted as formatted paragraph text.
        new("raci_content",       "3. RACI",                     "RACI Assignments",
            "[Roles and Responsibilities to be updated in a table here]",
            "AI:raciContent"),

        // ── 4. Standard Operating Procedure (paragraph placeholder) ───────────
        // Note: the template uses a mixed bracket "{Detailed SOP should come here]"
        // (curly-open, square-close). GenerateReportAsync handles this variant.
        new("sop_content",        "4. SOP",                      "SOP Steps",
            "{Detailed SOP should come here]",
            "AI:sopContent"),

        // ── 5. Work Instructions (heading placeholder) ─────────────────────────
        new("wi_content",         "5. Work Instructions",        "Work Instructions",
            "[Details Work Instructions to come here]",
            "AI:wiContent"),

        // ── 6.1 Escalation Matrix (paragraph) ─────────────────────────────────
        new("esc_content",        "6.1 Escalation",              "Escalation Matrix",
            "[Escalation matrix should come here]",
            "AI:escalationContent"),

        // ── 6.2 Exception Handling (paragraph) ────────────────────────────────
        new("exc_content",        "6.2 Exceptions",              "Exception Handling",
            "[Exception handling to come here in pointer or table]",
            "AI:exceptionContent"),

        // ── 7.1 Service Level Agreements (paragraph) ──────────────────────────
        new("sla_content",        "7.1 SLAs",                    "Service Level Agreements",
            "[SLA to come here in table]",
            "AI:slaContent"),

        // ── 7.2 Actual vs Target Performance (paragraph) ──────────────────────
        new("perf_content",       "7.2 Performance",             "Actual vs Target Performance",
            "[Actual vs Target performance to come here]",
            "AI:perfContent"),

        // ── 8. Volumetrics (paragraph placeholder) ────────────────────────────
        // The LLM generates month-by-month volume data; inserted as paragraph text.
        new("vol_content",        "8. Volumetrics",              "Monthly Volume Data",
            "[Month on month volumetrics should come here from intake and tasks files.]",
            "AI:volContent"),

        // ── 9. Regulatory and Compliance (Table 4) ────────────────────────────
        // Handled via ReplaceTableDataWithLlmContent — no find-and-replace placeholder.
        new("reg_content",        "9. Regulatory",               "Regulatory Mapping",
            "",
            "AI:regulatoryContent"),

        // ── 10. Training Materials (paragraph) ────────────────────────────────
        new("train_content",      "10. Training",                "Training Materials",
            "[LLM To design training materials based on all document uploaded]",
            "AI:trainingContent"),

        // ── 11. Orange Customer Contract Obligations (Table 5) ────────────────
        // Handled via ReplaceTableDataWithLlmContent — no find-and-replace placeholder.
        new("occ_content",        "11. OCC",                     "OCC Obligations",
            "",
            "AI:occContent"),

        // ── A. Process Flow Diagram (paragraph) ───────────────────────────────
        new("flow_content",       "A. Process Flow",             "Process Flow Description",
            "[LLM to design end to end process map based on all documents uploaded]",
            "AI:processFlow"),

        // ── B. Glossary (Table 6) ─────────────────────────────────────────────
        // Populated via ReplaceTableDataWithLlmContent matching "Term" + "Definition" headers.
        new("glossary_content",   "B. Glossary",                 "Glossary Terms",
            "",
            "AI:glossaryContent"),
    ];

    // Matches any remaining [placeholder] style text left over in the template.
    private static readonly Regex RemainingPlaceholderRegex =
        new(@"\[.*?\]", RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches the mixed-bracket SOP placeholder "{Detailed SOP should come here]"
    // which has a curly-open and square-close — a typo in the template source.
    private static readonly Regex CurlyBracketPlaceholderRegex =
        new(@"\{[^}]*\]", RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches the opening line of a SOP step block: "Step N: text" or "Step N- text"
    private static readonly Regex SopStepLineRegex =
        new(@"^Step\s+(\d+)\s*[:\-]\s*(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Maximum number of words allowed in a RACI task header cell.
    // Longer names are trimmed to this word count to prevent cells overflowing.
    private const int MaxRaciTaskNameWords = 4;

    public IReadOnlyList<FieldDefinition> GetFieldDefinitions() => FieldDefs.AsReadOnly();

    public Task<byte[]> GenerateReportAsync(
        IntakeRecord intake, IList<ReportFieldStatus> fieldStatuses, string templatePath,
        IList<ArtefactFile>? artefactFiles = null,
        IList<(string FileName, byte[] Data)>? processFlowImages = null)
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

            // ── Volume instruction echo guard ────────────────────────────────
            // The LLM sometimes copies template instruction text verbatim from uploaded
            // Word documents instead of extracting real data. Detect and replace with fallback.
            if (fs.FieldKey == "vol_content" && !string.IsNullOrWhiteSpace(value)
                && VolumeInstructionPatterns.Any(p =>
                    value.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                value = VolumeFallback;
            }

            // ── Fallback for critical sections that must never be blank ──────
            if (string.IsNullOrWhiteSpace(value) && !fs.IsNA)
            {
                value = fs.FieldKey switch
                {
                    "vol_content"  => VolumeFallback,
                    "sla_content"  => "SLAs to be confirmed with process owner.",
                    "perf_content" => "Performance data to be confirmed with process owner — provide figures for the past 6 months.",
                    _              => value
                };
            }

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

        // ── Artefact list: clear the paragraph placeholder ─────────────────────────
        // The actual file list is rendered by ReplaceArtefactsTable into the proper table.
        // Setting the placeholder to empty here prevents a numbered list like
        // "1. file1\n...\n8. file8\n..." from being left as a body paragraph, which would
        // otherwise cause ReplaceStructuredSectionTable (sectionHeading "8.") to false-match
        // the artefact paragraph and overwrite the artefacts table with volumetrics data.
        const string artefactPlaceholder =
            "[All Document uploaded in task or Name of document, date when uploaded in a table]";
        replacements[artefactPlaceholder] = string.Empty;

        // ── SOP placeholder: clear so ApplyReplacements does not inject raw SOP text ─
        // The template uses a mixed-bracket placeholder "{Detailed SOP should come here]".
        // If this placeholder is put into the document body via ApplyReplacements, the raw
        // sopContent text appears as unformatted text (before or at section 4) instead of
        // as a proper table. Setting it to empty here lets ApplyReplacements erase the
        // placeholder text, and ReplaceSopTableRows inserts the correctly formatted table
        // at section "4." without interference from the injected raw text.
        const string sopParagraphPlaceholder = "{Detailed SOP should come here]";
        replacements[sopParagraphPlaceholder] = string.Empty;

        // ── WI placeholder: clear so ApplyReplacements does not inject WI text ──────
        // The wi_content placeholder [Details Work Instructions to come here] is a standard
        // bracket placeholder, so ApplyReplacements would replace it wherever it appears —
        // including any matching text in the TOC area — causing WI content to surface in
        // unexpected locations. Setting it to empty ensures only ReplaceWorkInstructionParagraphs
        // handles the insertion at section "5."
        const string wiParagraphPlaceholder = "[Details Work Instructions to come here]";
        replacements[wiParagraphPlaceholder] = string.Empty;

        // ── RACI placeholder: clear so ApplyReplacements does not inject raw RACI text ─
        // The raci_content placeholder [Roles and Responsibilities to be updated in a table
        // here] is a standard bracket placeholder. If the raw RACI content (TASKS: / role rows)
        // is injected here by ApplyReplacements it appears as unformatted text in section 3
        // (between/after the RACI table that ReplaceRaciTable creates), making the raw content
        // look like a "step by step" block sitting below the RACI section.
        // Setting it to empty ensures only ReplaceRaciTable handles section 3.
        const string raciParagraphPlaceholder = "[Roles and Responsibilities to be updated in a table here]";
        replacements[raciParagraphPlaceholder] = string.Empty;

        using var wordDoc = WordprocessingDocument.Open(ms, isEditable: true);
        var body = wordDoc.MainDocumentPart!.Document.Body!;

        // ── Apply standard find-and-replace (covers all paragraph/table placeholders) ──
        ApplyReplacements(body, replacements);

        // Process headers and footers
        foreach (var headerPart in wordDoc.MainDocumentPart.HeaderParts)
            ApplyReplacements(headerPart.Header, replacements);
        foreach (var footerPart in wordDoc.MainDocumentPart.FooterParts)
            ApplyReplacements(footerPart.Footer, replacements);

        // ── Handle the mixed-bracket SOP placeholder "{Detailed SOP should come here]" ──
        // Clear the placeholder text (replace with empty string) so that the table-based
        // rendering in ReplaceSopTableRows can insert a proper table at section "4."
        // without the placeholder paragraph fighting the table insertion.
        var sopValue = GetFieldFillValue(fieldStatuses, "sop_content") ?? string.Empty;
        ApplyCurlyBracketReplacement(body, string.Empty);

        // ── Artefacts table: populate with uploaded file names ───────────────────
        if (artefactFiles != null && artefactFiles.Count > 0)
            ReplaceArtefactsTable(body, artefactFiles);

        // ── RACI table: populate as a proper table with individual cells ────────
        var raciValue = GetFieldFillValue(fieldStatuses, "raci_content");
        if (!string.IsNullOrWhiteSpace(raciValue))
            ReplaceRaciTable(body, raciValue);

        // ── SOP table: populate as a proper table with individual cells ─────────
        if (!string.IsNullOrWhiteSpace(sopValue))
            ReplaceSopTableRows(body, sopValue);

        // ── Work Instructions: replace paragraph section with LLM content ───────
        var wiValue = GetFieldFillValue(fieldStatuses, "wi_content");
        if (!string.IsNullOrWhiteSpace(wiValue))
            ReplaceWorkInstructionParagraphs(body, wiValue);

        // ── Update Document Control table rows for Author and Approver ──────────
        // Both rows have the same placeholder text in the template, so we must use
        // row-label lookup to set them independently.
        var authorValue      = GetFieldFillValue(fieldStatuses, "dc_author")      ?? string.Empty;
        var approverValue    = GetFieldFillValue(fieldStatuses, "dc_approver")    ?? string.Empty;
        UpdateDocControlRowsByLabel(body, authorValue, approverValue);

        // ── Escalation Matrix: populate as a proper table ───────────────────────
        var escValue = GetFieldFillValue(fieldStatuses, "esc_content");
        if (!string.IsNullOrWhiteSpace(escValue))
            ReplaceStructuredSectionTable(body, escValue,
                sectionHeading: "6.1",
                defaultHeaders: ["Trigger", "Escalation Path", "Timeframe", "Resolution Target"]);

        // ── Exception Handling: populate as a proper table ──────────────────────
        var excValue = GetFieldFillValue(fieldStatuses, "exc_content");
        if (!string.IsNullOrWhiteSpace(excValue))
            ReplaceStructuredSectionTable(body, excValue,
                sectionHeading: "6.2",
                defaultHeaders: ["Exception Type", "Handling Approach", "Approval Required"]);

        // ── SLA table (7.1): populate as a proper table ──────────────────────────
        var slaValue = GetFieldFillValue(fieldStatuses, "sla_content");
        if (!string.IsNullOrWhiteSpace(slaValue))
            ReplaceStructuredSectionTable(body, slaValue,
                sectionHeading: "7.1",
                defaultHeaders: ["Metric", "Target", "Measurement Method", "Reporting Frequency", "Tool"]);

        // ── Regulatory table (Table 4): replace N/A rows with LLM content ──────
        var regValue = GetFieldFillValue(fieldStatuses, "reg_content");
        if (!string.IsNullOrWhiteSpace(regValue))
            ReplaceTableWithParsedRows(
                body,
                headerKeywords : ["Regulation", "Standard"],
                keepHeaderRows : 1,
                content        : regValue);

        // ── OCC table (Table 5): replace N/A rows with LLM content ──────────────
        var occValue = GetFieldFillValue(fieldStatuses, "occ_content");
        if (!string.IsNullOrWhiteSpace(occValue))
            ReplaceTableWithParsedRows(
                body,
                headerKeywords : ["OCC Reference"],
                keepHeaderRows : 1,
                content        : occValue);

        // ── Glossary table: populate Term/Definition table ──────────────────────
        var glossaryValue = GetFieldFillValue(fieldStatuses, "glossary_content");
        if (!string.IsNullOrWhiteSpace(glossaryValue))
            ReplaceGlossaryTableRows(body, glossaryValue);

        // ── Volumetrics: populate as a proper table ─────────────────────────────
        var volValue = GetFieldFillValue(fieldStatuses, "vol_content");
        if (!string.IsNullOrWhiteSpace(volValue)
            && volValue != VolumeFallback)
            ReplaceStructuredSectionTable(body, volValue,
                sectionHeading: "8.",
                defaultHeaders: ["Month", "Volume / Transaction Type", "Notes"]);

        // ── Process Flow Diagram: embed uploaded images ─────────────────────────
        if (processFlowImages != null && processFlowImages.Count > 0)
            InsertProcessFlowImages(wordDoc, body, processFlowImages);

        // ── Deduplication pass ─────────────────────────────────────────────────
        RemoveDuplicateDataRows(body);

        // ── Final cleanup: erase any remaining [placeholder] text ──────────────
        ClearRemainingPlaceholders(body);
        foreach (var headerPart in wordDoc.MainDocumentPart.HeaderParts)
            ClearRemainingPlaceholders(headerPart.Header);
        foreach (var footerPart in wordDoc.MainDocumentPart.FooterParts)
            ClearRemainingPlaceholders(footerPart.Footer);

        // ── Strip mixed-bracket leftovers not already replaced ──────────────────
        ClearCurlyBracketPlaceholders(body);

        // ── Strip all Word reviewer comments ──────────────────────────────────
        RemoveAllComments(wordDoc);

        // ── Strip italic instruction paragraphs ────────────────────────────────
        RemoveInstructionParagraphs(body);

        // ── Clear stale cached TOC entries ─────────────────────────────────────
        // The TOC SdtBlock caches the last-rendered TOC text. If the template was
        // previously saved with Work Instruction sub-headings or other placeholder
        // text as TOC entries, those stale entries show up in the generated document.
        // Remove all TOC-styled paragraphs from the SdtContent so Word shows a blank
        // TOC that it will regenerate on open (driven by UpdateFieldsOnOpen below).
        ClearStaleTocContent(body);

        // ── Mark TOC as dirty so Word refreshes it on open ──────────────────────
        MarkTocDirty(wordDoc);

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
    /// Handles the mixed-bracket SOP placeholder <c>{Detailed SOP should come here]</c>
    /// (curly-open, square-close — a typo in the template). Replaces the entire
    /// paragraph text with <paramref name="content"/> if present, or erases it if empty.
    /// </summary>
    private static void ApplyCurlyBracketReplacement(Body body, string content)
    {
        const string sopMarker = "{Detailed SOP should come here]";
        foreach (var text in body.Descendants<Text>().ToList())
        {
            if (text.Text.Contains(sopMarker, StringComparison.OrdinalIgnoreCase))
            {
                text.Text = text.Text.Replace(sopMarker, content, StringComparison.OrdinalIgnoreCase);
                return;
            }
        }

        // Handle case where the marker is split across runs in a paragraph
        foreach (var para in body.Descendants<Paragraph>().ToList())
        {
            var allTexts = para.Descendants<Run>()
                               .SelectMany(r => r.Descendants<Text>())
                               .ToList();
            if (allTexts.Count <= 1) continue;
            var merged = string.Concat(allTexts.Select(t => t.Text));
            if (!merged.Contains(sopMarker, StringComparison.OrdinalIgnoreCase)) continue;
            var replaced = merged.Replace(sopMarker, content, StringComparison.OrdinalIgnoreCase);
            allTexts[0].Text = replaced;
            for (int i = 1; i < allTexts.Count; i++) allTexts[i].Text = string.Empty;
            return;
        }
    }

    /// <summary>
    /// Clears any remaining mixed-bracket <c>{...}</c> or <c>{...[</c> placeholders
    /// that were not filled (e.g. because no sop_content was generated).
    /// </summary>
    private static void ClearCurlyBracketPlaceholders(Body body)
    {
        foreach (var text in body.Descendants<Text>())
        {
            if (CurlyBracketPlaceholderRegex.IsMatch(text.Text))
                text.Text = CurlyBracketPlaceholderRegex.Replace(text.Text, string.Empty);
        }
    }

    /// <summary>
    /// Updates the Document Control table (Table 0) by finding each row whose first
    /// cell label matches "Process Author" or "Approver" and writing the corresponding
    /// value into the second cell. This is required because both rows share the same
    /// placeholder text in the template, making standard find-and-replace ambiguous.
    /// </summary>
    private static void UpdateDocControlRowsByLabel(Body body, string authorValue, string approverValue)
    {
        var table = body.Descendants<Table>().FirstOrDefault(t =>
        {
            var rows = t.Elements<TableRow>().ToList();
            if (rows.Count < 2) return false;
            var firstRowText = string.Concat(rows[0].Descendants<Text>().Select(x => x.Text));
            return firstRowText.Contains("BARTOK", StringComparison.OrdinalIgnoreCase)
                || firstRowText.Contains("Process Name", StringComparison.OrdinalIgnoreCase);
        });

        if (table == null) return;

        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().ToList();
            if (cells.Count < 2) continue;
            var label = string.Concat(cells[0].Descendants<Text>().Select(t => t.Text)).Trim();

            if (label.Equals("Process Author", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(authorValue))
            {
                SetCellText(cells[1], authorValue);
            }
            else if (label.Equals("Approver", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(approverValue))
            {
                SetCellText(cells[1], approverValue);
            }
        }
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

        // Fall back to generic table approach (section-scoped) if the LLM did
        // not use the structured TASKS: format.
        if (taskNames == null || roleRows.Count == 0)
        {
            ReplaceStructuredSectionTable(body, raciContent, "3.", ["Role", "Task"]);
            return;
        }

        // ── Locate RACI table within section "3." ─────────────────────────────
        // Find the "3." heading paragraph first so the table search is scoped to
        // the RACI section and cannot accidentally match a table in another section.
        Paragraph? sectionPara = null;
        foreach (var para in body.Descendants<Paragraph>())
        {
            if (IsTocParagraph(para) || IsInsideTable(para)) continue;
            var txt = string.Concat(para.Descendants<Text>().Select(t => t.Text)).TrimStart();
            if (Regex.IsMatch(txt, @"^3[\.\s]"))
            {
                sectionPara = para;
                break;
            }
        }

        if (sectionPara == null) return;

        Table? table = null;
        var curr = sectionPara.NextSibling();
        while (curr != null)
        {
            if (curr is Paragraph p)
            {
                var txt = string.Concat(p.Descendants<Text>().Select(t => t.Text)).TrimStart();
                if (Regex.IsMatch(txt, @"^\d+[\.\s]") || txt.StartsWith("A.", StringComparison.OrdinalIgnoreCase))
                    break;
            }
            else if (curr is Table t)
            {
                table = t;
                break;
            }
            curr = curr.NextSibling();
        }

        if (table == null)
        {
            // No existing RACI table — create a new matrix table at section "3."
            InsertRaciMatrixAtSection(body, taskNames, roleRows, sectionPara);
            return;
        }

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

    /// <summary>
    /// Creates a new RACI matrix table immediately after <paramref name="sectionPara"/>
    /// (the "3. RACI" heading) when no existing RACI table is found in the DOCX.
    /// The table has a header row of "Role" followed by each task name, and one data
    /// row per role with its R/A/C/I assignments.
    /// </summary>
    private static void InsertRaciMatrixAtSection(
        Body body,
        string[] taskNames,
        List<(string RoleName, string[] Assignments)> roleRows,
        Paragraph sectionPara)
    {
        // Remove existing content paragraphs between the heading and the next section
        var toRemove = new List<DocumentFormat.OpenXml.OpenXmlElement>();
        var curr = sectionPara.NextSibling();
        while (curr != null)
        {
            if (curr is Paragraph p)
            {
                var txt = string.Concat(p.Descendants<Text>().Select(t => t.Text)).TrimStart();
                if (Regex.IsMatch(txt, @"^\d+[\.\s]") || txt.StartsWith("A.", StringComparison.OrdinalIgnoreCase))
                    break;
                if (!string.IsNullOrWhiteSpace(txt))
                    toRemove.Add(p);
            }
            else if (curr is Table)
            {
                break;
            }
            curr = curr.NextSibling();
        }
        foreach (var el in toRemove)
            el.Remove();

        // Build the RACI matrix table
        var tbl = new Table();
        tbl.AppendChild(new TableProperties(
            new TableBorders(
                new TopBorder     { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new BottomBorder  { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new LeftBorder    { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new RightBorder   { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4, Color = "000000" }),
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }));

        // Header row: "Role" | task1 | task2 | ...
        var headerRow = new TableRow();
        foreach (var h in new[] { "Role" }.Concat(taskNames))
        {
            var cell = new TableCell();
            cell.AppendChild(new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = "D9E2F3" }));
            var run = new Run(new Text(h));
            run.PrependChild(new RunProperties(new Bold()));
            cell.AppendChild(new Paragraph(run));
            headerRow.AppendChild(cell);
        }
        tbl.AppendChild(headerRow);

        // Data rows: role name | R | A | C | I | ...
        foreach (var (roleName, assignments) in roleRows)
        {
            var dataRow = new TableRow();
            dataRow.AppendChild(BuildRaciCell(roleName, null));
            for (int i = 0; i < taskNames.Length; i++)
            {
                var assignment = i < assignments.Length ? assignments[i] : "-";
                dataRow.AppendChild(BuildRaciCell(assignment, null));
            }
            tbl.AppendChild(dataRow);
        }

        sectionPara.InsertAfterSelf(tbl);
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
    /// Finds (or creates) the SOP table in section "4." of <paramref name="body"/>,
    /// then fills it with the parsed <see cref="SopStep"/> records.
    /// The table search is section-scoped: it only looks between the "4." heading and the
    /// next numbered heading, preventing accidental matches from tables in other sections.
    /// When no existing table is present, a new bordered table is inserted after the
    /// section heading with standard SOP columns.
    /// Falls back gracefully when no steps can be parsed from <paramref name="sopContent"/>.
    /// </summary>
    private static void ReplaceSopTableRows(Body body, string sopContent)
    {
        var steps = ParseSopSteps(sopContent);

        // ── Find section "4." heading (section-scoped, not doc-wide) ─────────────
        Paragraph? sectionPara = null;
        foreach (var para in body.Descendants<Paragraph>())
        {
            if (IsTocParagraph(para) || IsInsideTable(para)) continue;
            var txt = string.Concat(para.Descendants<Text>().Select(t => t.Text)).TrimStart();
            if (Regex.IsMatch(txt, @"^4[\.\s]"))
            {
                sectionPara = para;
                break;
            }
        }

        // ── Find existing SOP table within section "4." ───────────────────────────
        Table? table = null;
        if (sectionPara != null)
        {
            var curr = sectionPara.NextSibling();
            while (curr != null)
            {
                if (curr is Paragraph p)
                {
                    var txt = string.Concat(p.Descendants<Text>().Select(t => t.Text)).TrimStart();
                    if (Regex.IsMatch(txt, @"^\d+[\.\s]") || txt.StartsWith("A.", StringComparison.OrdinalIgnoreCase))
                        break; // reached the next section — stop searching
                }
                else if (curr is Table t)
                {
                    var firstRow = t.Descendants<TableRow>().FirstOrDefault();
                    var headerTxt = firstRow != null
                        ? string.Concat(firstRow.Descendants<Text>().Select(x => x.Text))
                        : string.Empty;
                    if (headerTxt.Contains("Step",   StringComparison.OrdinalIgnoreCase)
                        && headerTxt.Contains("Action", StringComparison.OrdinalIgnoreCase))
                    {
                        table = t;
                        break;
                    }
                }
                curr = curr.NextSibling();
            }
        }

        if (steps.Count == 0)
        {
            // Nothing to render — section is intact (placeholder already cleared).
            return;
        }

        if (table == null)
        {
            // No existing SOP table — create a fresh one at section "4."
            if (sectionPara != null)
                InsertSopTableAtSection(steps, sectionPara);
            return;
        }

        // ── Populate existing table ───────────────────────────────────────────────
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
    /// Creates a new SOP table immediately after <paramref name="sectionPara"/>
    /// (the "4. SOP" heading) when no existing SOP table is found in the DOCX.
    /// Standard columns: Step | Action | Role | System | Output | Automation | Rating | Type.
    /// </summary>
    private static void InsertSopTableAtSection(List<SopStep> steps, Paragraph sectionPara)
    {
        // Remove any placeholder paragraphs between the heading and the next section.
        var toRemove = new List<DocumentFormat.OpenXml.OpenXmlElement>();
        var curr = sectionPara.NextSibling();
        while (curr != null)
        {
            if (curr is Paragraph p)
            {
                var txt = string.Concat(p.Descendants<Text>().Select(t => t.Text)).TrimStart();
                if (Regex.IsMatch(txt, @"^\d+[\.\s]") || txt.StartsWith("A.", StringComparison.OrdinalIgnoreCase))
                    break;
                if (!string.IsNullOrWhiteSpace(txt))
                    toRemove.Add(p);
            }
            else if (curr is Table)
            {
                break;
            }
            curr = curr.NextSibling();
        }
        foreach (var el in toRemove) el.Remove();

        var headers = new[] { "Step", "Action", "Role", "System", "Output", "Automation", "Rating", "Type" };

        var tbl = new Table();
        tbl.AppendChild(new TableProperties(
            new TableBorders(
                new TopBorder    { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new LeftBorder   { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new RightBorder  { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4, Color = "000000" }),
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }));

        // Header row
        var headerRow = new TableRow();
        foreach (var h in headers)
        {
            var cell = new TableCell();
            cell.AppendChild(new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = "D9E2F3" }));
            var run = new Run(new Text(h));
            run.PrependChild(new RunProperties(new Bold()));
            cell.AppendChild(new Paragraph(run));
            headerRow.AppendChild(cell);
        }
        tbl.AppendChild(headerRow);

        // Data rows — one per parsed SOP step
        foreach (var step in steps)
        {
            var dataRow = new TableRow();
            foreach (var val in new[]
            {
                step.StepNumber, step.Action, step.Role, step.System,
                step.Output, step.AutoStatus, step.OppRating, step.AutoType
            })
            {
                dataRow.AppendChild(BuildRaciCell(val, null));
            }
            tbl.AppendChild(dataRow);
        }

        sectionPara.InsertAfterSelf(tbl);
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
            // Skip TOC entries — the "5. Work Instructions" text appears in the TOC
            // before the real heading in the document body.
            if (IsTocParagraph(p)) continue;
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
    /// Parses pipe-delimited or tab-delimited LLM output into rows of cell values.
    /// Skips empty lines and lines that look like header separators (e.g. "---|---|---").
    /// </summary>
    private static List<string[]> ParseDelimitedContent(string content)
    {
        var rows = new List<string[]>();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            // Skip separator lines (e.g., "---|---|---" or "--- | --- | ---")
            if (Regex.IsMatch(line, @"^[\s\-|]+$")) continue;
            // Skip lines that are just whitespace
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Determine separator: prefer pipe, fall back to tab
            var separator = line.Contains('|') ? '|' : '\t';
            var cells = line.Split(separator, StringSplitOptions.TrimEntries);

            // Skip if all cells are empty
            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            rows.Add(cells);
        }

        return rows;
    }

    /// <summary>
    /// Finds the first table matching <paramref name="headerKeywords"/> in its first row,
    /// clears all data rows, then inserts properly-structured individual-cell rows by
    /// parsing the pipe/tab-delimited <paramref name="content"/>.
    /// Falls back to merged-cell approach if the content cannot be parsed into rows.
    /// </summary>
    private static void ReplaceTableWithParsedRows(
        Body body,
        string[] headerKeywords,
        int keepHeaderRows,
        string content)
    {
        // Locate the target table by matching keywords against its first row.
        // Skip nested tables (IsInsideTable) to avoid false matches.
        var table = body.Descendants<Table>().FirstOrDefault(t =>
        {
            if (IsInsideTable(t)) return false;
            var firstRow = t.Descendants<TableRow>().FirstOrDefault();
            if (firstRow == null) return false;
            var headerText = string.Concat(firstRow.Descendants<Text>().Select(x => x.Text));
            return headerKeywords.All(k =>
                headerText.Contains(k, StringComparison.OrdinalIgnoreCase));
        });

        if (table == null) return;

        var parsedRows = ParseDelimitedContent(content);

        // If the first parsed row looks like a header (matches keywords), skip it
        if (parsedRows.Count > 0)
        {
            var firstRow = parsedRows[0];
            bool looksLikeHeader = headerKeywords.All(k =>
                firstRow.Any(c => c.Contains(k, StringComparison.OrdinalIgnoreCase)));
            if (looksLikeHeader)
                parsedRows = parsedRows.Skip(1).ToList();
        }

        // Fall back to merged-cell approach if parsing yields no data rows
        if (parsedRows.Count == 0)
        {
            ReplaceTableDataWithLlmContent(body, headerKeywords, keepHeaderRows, content);
            return;
        }

        var allRows = table.Elements<TableRow>().ToList();
        if (allRows.Count == 0) return;

        // Keep header rows, remove data rows
        foreach (var row in allRows.Skip(keepHeaderRows))
            row.Remove();

        int colCount = allRows.First().Elements<TableCell>().Count();
        var templateCells = allRows.Last().Elements<TableCell>().ToList();

        // Insert one row per parsed data line
        foreach (var cells in parsedRows)
        {
            var newRow = new TableRow();
            for (int c = 0; c < colCount; c++)
            {
                var cellText = c < cells.Length ? cells[c].Trim() : string.Empty;
                var tmplCell = c < templateCells.Count ? templateCells[c] : null;
                newRow.AppendChild(BuildRaciCell(cellText, tmplCell));
            }
            table.AppendChild(newRow);
        }
    }

    /// <summary>
    /// Parses pipe/tab-delimited glossary content (Term\tDefinition or Term | Definition)
    /// and inserts proper individual-cell rows into the Glossary table.
    /// </summary>
    private static void ReplaceGlossaryTableRows(Body body, string glossaryContent)
    {
        // Locate the Glossary table (skip nested tables)
        var table = body.Descendants<Table>().FirstOrDefault(t =>
        {
            if (IsInsideTable(t)) return false;
            var firstRow = t.Descendants<TableRow>().FirstOrDefault();
            if (firstRow == null) return false;
            var headerText = string.Concat(firstRow.Descendants<Text>().Select(x => x.Text));
            return headerText.Contains("Term", StringComparison.OrdinalIgnoreCase)
                && headerText.Contains("Definition", StringComparison.OrdinalIgnoreCase);
        });

        if (table == null) return;

        var parsedRows = ParseDelimitedContent(glossaryContent);

        // If the first parsed row looks like a header, skip it
        if (parsedRows.Count > 0)
        {
            var first = parsedRows[0];
            if (first.Any(c => c.Contains("Term", StringComparison.OrdinalIgnoreCase))
                && first.Any(c => c.Contains("Definition", StringComparison.OrdinalIgnoreCase)))
                parsedRows = parsedRows.Skip(1).ToList();
        }

        // Fall back to merged-cell approach if parsing fails
        if (parsedRows.Count == 0)
        {
            ReplaceTableDataWithLlmContent(body, ["Term", "Definition"], 1, glossaryContent);
            return;
        }

        var allRows = table.Elements<TableRow>().ToList();
        if (allRows.Count == 0) return;

        // Keep header row, remove data rows
        foreach (var row in allRows.Skip(1))
            row.Remove();

        int colCount = allRows.First().Elements<TableCell>().Count();
        var templateCells = allRows[0].Elements<TableCell>().ToList();

        foreach (var cells in parsedRows)
        {
            var newRow = new TableRow();
            for (int c = 0; c < colCount; c++)
            {
                var cellText = c < cells.Length ? cells[c].Trim() : string.Empty;
                var tmplCell = c < templateCells.Count ? templateCells[c] : null;
                newRow.AppendChild(BuildRaciCell(cellText, tmplCell));
            }
            table.AppendChild(newRow);
        }
    }

    /// <summary>
    /// Handles sections (Escalation Matrix, Volumetrics, SLA, Exception Handling, etc.)
    /// where the template has a paragraph placeholder but the content should be rendered
    /// as a table. Finds the heading paragraph matching <paramref name="sectionHeading"/>,
    /// then scans forward within that section (stopping at the next numbered heading) to
    /// detect any existing table. If found, it populates that table. If not found, it
    /// removes the placeholder paragraphs and inserts a brand-new table.
    /// The table search is section-scoped — it never reaches into a different section —
    /// which prevents false matches from tables in adjacent sections (e.g. a Performance
    /// table that also has a "Month" column is no longer confused with Volumetrics).
    /// </summary>
    private static void ReplaceStructuredSectionTable(
        Body body,
        string content,
        string sectionHeading,
        string[] defaultHeaders)
    {
        var parsedRows = ParseDelimitedContent(content);
        if (parsedRows.Count == 0) return;

        // Check if the first row looks like a header
        string[] headers;
        List<string[]> dataRows;
        bool firstRowIsHeader = parsedRows[0].Length >= 2
            && defaultHeaders.Any(h =>
                parsedRows[0].Any(c => c.Contains(h, StringComparison.OrdinalIgnoreCase)));

        if (firstRowIsHeader)
        {
            headers = parsedRows[0];
            dataRows = parsedRows.Skip(1).ToList();
        }
        else
        {
            headers = defaultHeaders;
            dataRows = parsedRows;
        }

        if (dataRows.Count == 0) return;

        int colCount = Math.Max(headers.Length, dataRows.Max(r => r.Length));

        // ── Find the section heading paragraph ────────────────────────────────────
        // Skip TOC paragraphs: the section number appears in the TOC before the real heading.
        // Skip paragraphs inside table cells: numbered list items like "8. filename" in the
        // artefacts table would otherwise match section "8." and put the table in the wrong place.
        // Use StartsWith (not Contains) so that a paragraph whose text is the artefact numbered
        // list "1. file1...8. file8..." (which CONTAINS "8." mid-string) is never treated as the
        // "8. Volumetrics" section heading.
        Paragraph? sectionPara = null;
        foreach (var para in body.Descendants<Paragraph>())
        {
            if (IsTocParagraph(para) || IsInsideTable(para)) continue;
            var txt = string.Concat(para.Descendants<Text>().Select(t => t.Text));
            if (txt.TrimStart().StartsWith(sectionHeading, StringComparison.OrdinalIgnoreCase))
            {
                sectionPara = para;
                break;
            }
        }

        if (sectionPara == null) return;

        // ── Look for an existing table scoped to this section ─────────────────────
        // Scan from the section heading to the next numbered section heading.
        // This prevents false matches from tables in adjacent sections (e.g. a
        // Performance 7.2 table that also has "Month" would be mistaken for Volumetrics
        // if we scan the entire document).
        Table? existingTable = null;
        var sibling = sectionPara.NextSibling();
        while (sibling != null)
        {
            if (sibling is Paragraph p)
            {
                var txt = string.Concat(p.Descendants<Text>().Select(t => t.Text)).TrimStart();
                if (Regex.IsMatch(txt, @"^\d+[\.\s]") || txt.StartsWith("A.", StringComparison.OrdinalIgnoreCase))
                    break; // reached the next section — stop
            }
            else if (sibling is Table t)
            {
                existingTable = t;
                break;
            }
            sibling = sibling.NextSibling();
        }

        if (existingTable != null)
        {
            // Populate existing table: keep header row, replace all data rows.
            var allRows = existingTable.Elements<TableRow>().ToList();
            foreach (var row in allRows.Skip(1))
                row.Remove();

            var templateCells = allRows[0].Elements<TableCell>().ToList();
            int existingColCount = templateCells.Count;

            foreach (var cells in dataRows)
            {
                var newRow = new TableRow();
                for (int c = 0; c < existingColCount; c++)
                {
                    var cellText = c < cells.Length ? cells[c].Trim() : string.Empty;
                    var tmplCell = c < templateCells.Count ? templateCells[c] : null;
                    newRow.AppendChild(BuildRaciCell(cellText, tmplCell));
                }
                existingTable.AppendChild(newRow);
            }
            return;
        }

        // ── No existing table — remove placeholder paragraphs and insert a new one ─
        var toRemove = new List<DocumentFormat.OpenXml.OpenXmlElement>();
        var curr = sectionPara.NextSibling();
        while (curr != null)
        {
            if (curr is Paragraph p)
            {
                var txt = string.Concat(p.Descendants<Text>().Select(t => t.Text)).TrimStart();
                // Stop at the next numbered section heading
                if (Regex.IsMatch(txt, @"^\d+[\.\s]") || txt.StartsWith("A.", StringComparison.OrdinalIgnoreCase))
                    break;
                // Only remove non-empty content paragraphs (keep spacing)
                if (!string.IsNullOrWhiteSpace(txt))
                    toRemove.Add(p);
            }
            else if (curr is Table)
            {
                break; // Stop at existing tables
            }
            curr = curr.NextSibling();
        }

        foreach (var el in toRemove)
            el.Remove();

        // Build the new table
        var table = new Table();

        // Add table properties for borders
        var tblProps = new TableProperties(
            new TableBorders(
                new TopBorder     { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new BottomBorder  { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new LeftBorder    { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new RightBorder   { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4, Color = "000000" }),
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }); // 100%
        table.AppendChild(tblProps);

        // Header row
        var headerRow = new TableRow();
        foreach (var h in headers)
        {
            var cell = new TableCell();
            cell.AppendChild(new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = "D9E2F3" }));
            var run = new Run(new Text(h));
            run.PrependChild(new RunProperties(new Bold()));
            cell.AppendChild(new Paragraph(run));
            headerRow.AppendChild(cell);
        }
        // Pad header if needed
        for (int c = headers.Length; c < colCount; c++)
        {
            var cell = new TableCell();
            cell.AppendChild(new Paragraph());
            headerRow.AppendChild(cell);
        }
        table.AppendChild(headerRow);

        // Data rows
        foreach (var cells in dataRows)
        {
            var dataRow = new TableRow();
            for (int c = 0; c < colCount; c++)
            {
                var cellText = c < cells.Length ? cells[c].Trim() : string.Empty;
                var cell = new TableCell();
                cell.AppendChild(new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Auto, Width = "0" }));
                cell.AppendChild(new Paragraph(new Run(new Text(cellText))));
                dataRow.AppendChild(cell);
            }
            table.AppendChild(dataRow);
        }

        // Insert the table after the section heading
        sectionPara.InsertAfterSelf(table);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="element"/> is nested inside a <see cref="Table"/>.
    /// Used to prevent section-heading searches from matching paragraphs inside table cells
    /// (e.g. a numbered file list "8. filename.xlsx" in the artefacts table would otherwise
    /// match the "8. Volumetrics" section heading check).
    /// </summary>
    private static bool IsInsideTable(DocumentFormat.OpenXml.OpenXmlElement element)
    {
        var parent = element.Parent;
        while (parent != null)
        {
            if (parent is Table) return true;
            parent = parent.Parent;
        }
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="para"/> is part of the Table of Contents —
    /// either because it lives inside an <see cref="SdtBlock"/> (Word wraps the TOC in one)
    /// or because its paragraph style begins with "TOC" (e.g. "TOC1", "TOC2").
    /// Used to prevent section-heading searches from matching TOC entries before the
    /// real headings in the document body.
    /// </summary>
    private static bool IsTocParagraph(Paragraph para)
    {
        // Check for TOC paragraph style (e.g. "TOC1", "TOC2", "TOCHeading")
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId != null && styleId.StartsWith("TOC", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check if the paragraph is nested inside an SdtBlock (Word's TOC container)
        var parent = para.Parent;
        while (parent != null)
        {
            if (parent is SdtBlock) return true;
            parent = parent.Parent;
        }
        return false;
    }

    /// <summary>
    /// Marks all TOC (Table of Contents) fields in the document as dirty so that
    /// Word will prompt the user to refresh them on open. This ensures the TOC reflects
    /// any content changes made by the report generator.
    /// </summary>
    private static void MarkTocDirty(WordprocessingDocument wordDoc)
    {
        var settings = wordDoc.MainDocumentPart?.DocumentSettingsPart?.Settings;
        if (settings != null)
        {
            // Add UpdateFieldsOnOpen if not already present
            if (settings.GetFirstChild<UpdateFieldsOnOpen>() == null)
                settings.PrependChild(new UpdateFieldsOnOpen { Val = true });
        }

        // Also mark individual SdtBlock (structured document tags) as dirty —
        // the TOC is typically wrapped in an SdtBlock.
        var body = wordDoc.MainDocumentPart?.Document.Body;
        if (body == null) return;

        foreach (var sdt in body.Descendants<SdtBlock>())
        {
            var alias = sdt.Descendants<SdtAlias>().FirstOrDefault();
            if (alias?.Val?.Value?.Contains("TOC", StringComparison.OrdinalIgnoreCase) == true
                || alias?.Val?.Value?.Contains("Table of Contents", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Mark the field codes inside the TOC as dirty
                foreach (var fldChar in sdt.Descendants<FieldChar>())
                {
                    if (fldChar.FieldCharType?.Value == FieldCharValues.Begin)
                        fldChar.Dirty = true;
                }
            }
        }

        // Also handle inline field codes (not in SdtBlock)
        foreach (var fldCode in body.Descendants<FieldCode>())
        {
            if (fldCode.Text?.Contains("TOC", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Find the preceding FieldChar (Begin) and mark it dirty
                var parent = fldCode.Parent;
                if (parent != null)
                {
                    var prevSibling = parent.PreviousSibling();
                    while (prevSibling != null)
                    {
                        var fldChar = prevSibling.Descendants<FieldChar>().FirstOrDefault();
                        if (fldChar?.FieldCharType?.Value == FieldCharValues.Begin)
                        {
                            fldChar.Dirty = true;
                            break;
                        }
                        prevSibling = prevSibling.PreviousSibling();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Removes stale cached TOC entries (paragraphs with "TOC1"/"TOC2"/etc. styles)
    /// from every TOC <see cref="SdtBlock"/> in <paramref name="body"/>.
    /// Word stores the last-rendered TOC text inside the SdtBlock's SdtContent; when
    /// the document is generated the cached entries reflect the OLD template content
    /// (e.g. Work Instruction sub-headings, placeholder text). Clearing them ensures
    /// the TOC area is blank rather than showing misleading stale entries.
    /// Word's <c>UpdateFieldsOnOpen</c> flag (set by <see cref="MarkTocDirty"/>) will
    /// regenerate the correct TOC from the actual document headings on open.
    /// </summary>
    private static void ClearStaleTocContent(Body body)
    {
        foreach (var sdt in body.Descendants<SdtBlock>())
        {
            // Identify TOC SdtBlocks: either by alias name or by containing a TOC field code.
            var alias = sdt.SdtProperties?.GetFirstChild<SdtAlias>();
            bool isToc = alias?.Val?.Value?.Contains("TOC", StringComparison.OrdinalIgnoreCase) == true
                      || alias?.Val?.Value?.Contains("Table of Contents", StringComparison.OrdinalIgnoreCase) == true
                      || sdt.Descendants<FieldCode>().Any(fc =>
                             fc.Text?.Contains("TOC", StringComparison.OrdinalIgnoreCase) == true);

            if (!isToc) continue;

            var sdtContent = sdt.GetFirstChild<SdtContentBlock>();
            if (sdtContent == null) continue;

            // Remove all paragraphs whose style begins with "TOC" (TOC1, TOC2, TOCHeading…).
            // These are the cached rendered entries. Preserve all other paragraphs
            // (the field-begin/field-end paragraphs that drive TOC regeneration).
            foreach (var para in sdtContent.Elements<Paragraph>().ToList())
            {
                var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                if (styleId != null && styleId.StartsWith("TOC", StringComparison.OrdinalIgnoreCase))
                    para.Remove();
            }
        }
    }

    /// <summary>
    /// Finds the first table in <paramref name="body"/> whose first row contains ALL
    /// of the <paramref name="headerKeywords"/>, removes every data row beyond the
    /// first <paramref name="keepHeaderRows"/> rows, then appends a new merged-cell row
    /// containing <paramref name="content"/> (newlines become OOXML line-breaks).
    /// Used as a fallback when structured row parsing fails.
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

    /// <summary>
    /// Rebuilds the "1.2 Inputs &amp; Artefacts Received" table with one data row per
    /// uploaded document.  The table is identified by the keyword "Artefact" in its
    /// header row.  Column layout: No | Document / Artefact Name | Type | Source.
    /// </summary>
    private static void ReplaceArtefactsTable(Body body, IList<ArtefactFile> artefactFiles)
    {
        // ── Locate the artefacts table by scoping to section "1.2" ─────────────────
        // Section-scoped search is more reliable than header-text matching because the
        // actual header text in the template varies ("Document Name", "Artefact Name",
        // "Document / Artefact Name" etc.). The "1.2 Inputs & Artefacts Received" heading
        // always precedes the artefacts table.
        Paragraph? sectionPara = null;
        foreach (var para in body.Descendants<Paragraph>())
        {
            if (IsTocParagraph(para) || IsInsideTable(para)) continue;
            var txt = string.Concat(para.Descendants<Text>().Select(t => t.Text)).TrimStart();
            // Match "1.2" followed by a space/dot/colon/end — e.g. "1.2 Inputs", "1.2.", "1.2:"
            if (Regex.IsMatch(txt, @"^1\.2[\s\.\:]"))
            {
                sectionPara = para;
                break;
            }
        }

        Table? table = null;

        if (sectionPara != null)
        {
            // Find first table between "1.2" heading and "2." section heading.
            var curr = sectionPara.NextSibling();
            while (curr != null)
            {
                if (curr is Paragraph p)
                {
                    var txt = string.Concat(p.Descendants<Text>().Select(t => t.Text)).TrimStart();
                    if (Regex.IsMatch(txt, @"^2[\.\s]")) break; // reached section 2 — stop
                }
                else if (curr is Table t)
                {
                    table = t;
                    break;
                }
                curr = curr.NextSibling();
            }
        }

        // Fallback: search by header keywords when section-scoped search fails.
        // Prefer "Artefact" (unambiguous), fall back to "Uploaded By" (unique to artefacts
        // table — the Document Control table does not contain this phrase).
        if (table == null)
        {
            table = body.Descendants<Table>().FirstOrDefault(t =>
            {
                if (IsInsideTable(t)) return false;
                var rows = t.Descendants<TableRow>().ToList();
                if (rows.Count == 0) return false;
                var headerText = string.Concat(rows[0].Descendants<Text>().Select(x => x.Text));
                return headerText.Contains("Artefact",   StringComparison.OrdinalIgnoreCase)
                    || headerText.Contains("Uploaded By", StringComparison.OrdinalIgnoreCase);
            });
        }

        if (table == null) return;

        var allRows = table.Elements<TableRow>().ToList();
        if (allRows.Count == 0) return;

        // Keep only the header row; remove all existing data rows.
        foreach (var row in allRows.Skip(1))
            row.Remove();

        // Determine column count from header row.
        int colCount = allRows[0].Elements<TableCell>().Count();

        var srcCells = allRows[0].Elements<TableCell>().ToList();

        // Build one new row per file.
        for (int i = 0; i < artefactFiles.Count; i++)
        {
            var file   = artefactFiles[i];
            var newRow = new TableRow();

            // Copy table-row properties from the header row for consistent styling.
            var srcRowProps = allRows[0].GetFirstChild<TableRowProperties>()?.CloneNode(true);
            if (srcRowProps != null) newRow.AppendChild(srcRowProps);

            for (int col = 0; col < colCount; col++)
            {
                var newCell = new TableCell();

                if (col < srcCells.Count)
                {
                    var srcCellProps = srcCells[col].GetFirstChild<TableCellProperties>()?.CloneNode(true);
                    if (srcCellProps != null) newCell.AppendChild(srcCellProps);
                }

                var cellText = col switch
                {
                    0 => (i + 1).ToString(),                        // No.
                    1 => file.FileName,                              // Document / Artefact Name
                    2 => file.UploadedBy ?? string.Empty,            // Uploaded By
                    3 => file.SourceId,                              // Task / Intake ID
                    _ => string.Empty
                };

                var para = new Paragraph();
                if (!string.IsNullOrEmpty(cellText))
                    para.AppendChild(new Run(new Text(cellText)));
                newCell.AppendChild(para);
                newRow.AppendChild(newCell);
            }

            table.AppendChild(newRow);
        }
    }

    /// <summary>
    /// Removes template-author instruction paragraphs from the generated document.
    /// These are paragraphs whose every text-bearing run is formatted as italic —
    /// a convention used throughout the BARTOK template to mark guidance text that
    /// should only be visible while authoring, not in the final delivered report.
    /// Paragraphs that are empty or contain only whitespace are not affected.
    /// </summary>
    private static void RemoveInstructionParagraphs(Body body)
    {
        foreach (var para in body.Descendants<Paragraph>().ToList())
        {
            var runs = para.Elements<Run>().ToList();
            if (runs.Count == 0) continue;

            // Keep paragraph if ANY run contains non-whitespace text that is NOT italic.
            bool allItalic = runs.All(run =>
            {
                var rpr = run.GetFirstChild<RunProperties>();
                bool italic = rpr?.Italic != null && (rpr.Italic.Val is null || rpr.Italic.Val == true);
                // A run with no text content (e.g. line-break-only run) doesn't break the rule.
                var text = string.Concat(run.Descendants<Text>().Select(t => t.Text));
                if (string.IsNullOrWhiteSpace(text)) return true;
                return italic;
            });

            if (!allItalic) continue;

            // Confirm at least one run has real (non-whitespace) text to avoid removing
            // legitimate empty paragraph spacers.
            bool hasText = runs.Any(r =>
                !string.IsNullOrWhiteSpace(
                    string.Concat(r.Descendants<Text>().Select(t => t.Text))));

            if (hasText)
                para.Remove();
        }
    }

    /// <summary>
    /// Inserts uploaded process flow diagram images into the "A. Process Flow Diagram"
    /// section of the document. Images are added as inline drawings after the section heading.
    /// </summary>
    private static void InsertProcessFlowImages(
        WordprocessingDocument wordDoc,
        Body body,
        IList<(string FileName, byte[] Data)> images)
    {
        // Find the "A. Process Flow Diagram" heading paragraph
        Paragraph? flowHeading = null;
        foreach (var para in body.Descendants<Paragraph>())
        {
            var paraText = string.Concat(para.Descendants<Text>().Select(t => t.Text));
            if (paraText.Contains("A. Process Flow Diagram", StringComparison.OrdinalIgnoreCase)
                && !paraText.Contains("10", StringComparison.Ordinal))  // skip TOC entry
            {
                flowHeading = para;
                // keep searching for the last (actual heading, not TOC)
            }
        }

        if (flowHeading == null) return;

        // Find insertion point: after the flow heading and its sub-paragraphs,
        // but before the "B. Glossary" section
        var insertAfter = flowHeading;
        var sibling = flowHeading.NextSibling();
        while (sibling != null)
        {
            if (sibling is Paragraph nextPara)
            {
                var txt = string.Concat(nextPara.Descendants<Text>().Select(t => t.Text));
                if (txt.StartsWith("B.", StringComparison.OrdinalIgnoreCase)
                    || txt.Contains("Glossary", StringComparison.OrdinalIgnoreCase))
                    break;
                insertAfter = nextPara;
            }
            else
            {
                break;
            }
            sibling = sibling.NextSibling();
        }

        // Insert each image
        var mainPart = wordDoc.MainDocumentPart!;
        foreach (var (fileName, data) in images)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var contentType = ext switch
            {
                ".png"  => "image/png",
                ".jpg"  => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".tiff" => "image/tiff",
                ".bmp"  => "image/bmp",
                _       => "image/jpeg"
            };

            // Add image part
            var imagePart = mainPart.AddImagePart(ext switch
            {
                ".png"  => ImagePartType.Png,
                ".tiff" => ImagePartType.Tiff,
                ".bmp"  => ImagePartType.Bmp,
                _       => ImagePartType.Jpeg
            });

            using (var stream = new MemoryStream(data))
                imagePart.FeedData(stream);

            var relationshipId = mainPart.GetIdOfPart(imagePart);

            // Default image dimensions: 6 inches wide × 4 inches tall (in EMUs)
            // 1 inch = 914400 EMUs
            const long defaultWidthEmu  = 5486400L; // 6 inches
            const long defaultHeightEmu = 3657600L; // 4 inches

            // Create the drawing element
            var drawing = CreateInlineDrawing(relationshipId, fileName,
                defaultWidthEmu, defaultHeightEmu);

            // Add caption paragraph with image name
            var captionPara = new Paragraph(
                new Run(new Text($"Process Flow Diagram: {fileName}")
                {
                    Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve
                }));

            // Add image paragraph
            var imagePara = new Paragraph(new Run(drawing));

            // Insert after the current insertion point
            insertAfter.InsertAfterSelf(captionPara);
            captionPara.InsertAfterSelf(imagePara);
            insertAfter = imagePara;
        }
    }

    /// <summary>
    /// Creates an OpenXML <see cref="Drawing"/> element for an inline image.
    /// </summary>
    private static Drawing CreateInlineDrawing(
        string relationshipId, string name, long widthEmu, long heightEmu)
    {
        var inline = new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent
            {
                Cx = widthEmu,
                Cy = heightEmu
            },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties
            {
                Id = (uint)Math.Abs(name.GetHashCode()),
                Name = name
            },
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.NonVisualGraphicFrameDrawingProperties(
                new DocumentFormat.OpenXml.Drawing.GraphicFrameLocks { NoChangeAspect = true }),
            new DocumentFormat.OpenXml.Drawing.Graphic(
                new DocumentFormat.OpenXml.Drawing.GraphicData(
                    new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                        new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                            new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties
                            {
                                Id = 0U,
                                Name = name
                            },
                            new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()),
                        new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                            new DocumentFormat.OpenXml.Drawing.Blip
                            {
                                Embed = relationshipId
                            },
                            new DocumentFormat.OpenXml.Drawing.Stretch(
                                new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                        new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                            new DocumentFormat.OpenXml.Drawing.Transform2D(
                                new DocumentFormat.OpenXml.Drawing.Offset { X = 0L, Y = 0L },
                                new DocumentFormat.OpenXml.Drawing.Extents { Cx = widthEmu, Cy = heightEmu }),
                            new DocumentFormat.OpenXml.Drawing.PresetGeometry(
                                new DocumentFormat.OpenXml.Drawing.AdjustValueList())
                            { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle })))
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U
        };

        return new Drawing(inline);
    }
}
