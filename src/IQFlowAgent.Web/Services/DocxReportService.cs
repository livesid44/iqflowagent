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
        // ── Cover page header fields ─────────────────────────────────────────
        // TemplatePlaceholder must exactly match text in BARTOK_DD_Template_v2.docx
        new("cover_process_name",    "Cover",                         "Process Name",
            "Enter Process Name",
            "ProcessName"),

        new("cover_lot",             "Cover",                         "Service Line / LOT",
            "Enter Service Line / LOT",
            "SdcLots"),

        new("cover_reviewer",        "Cover",                         "Reviewed By",
            "Enter Reviewer Name",
            "ProcessOwnerName"),

        new("cover_date",            "Cover",                         "Review Date",
            "DD MMM YYYY",
            "TODAY"),

        // Cover page location row ("India / Egypt / Brazil / Slovakia" default text)
        new("cover_location",        "Cover",                         "Location(s)",
            "India / Egypt / Brazil / Slovakia",
            "Country"),

        // ── 0. Client Inputs & Artefacts ─────────────────────────────────────
        new("artefact_doc1",         "0. Client Inputs & Artefacts",  "Document / Artefact #1",
            "Enter document name...",
            "UploadedFileName"),

        // ── 1.1 Process Overview ─────────────────────────────────────────────
        new("process_summary",       "1.1 Process Overview",          "Process Summary",
            "Describe the process in your own words. Cover: what it does, who it serves, how it is triggered, and why it matters to the business. Avoid bullet points here — use complete sentences so this section can be read in isolation by a senior stakeholder.",
            "AI:summary"),

        // ── 1.2 Basic Process Information ────────────────────────────────────
        new("s12_process_name",      "1.2 Basic Process Information", "Process Name",
            "Enter process name...",
            "ProcessName"),

        new("s12_service_line",      "1.2 Basic Process Information", "Service Line / LOT",
            "Enter service line...",
            "SdcLots"),

        new("s12_process_owner",     "1.2 Basic Process Information", "Process Owner",
            "Name and role...",
            "ProcessOwnerContact"),

        new("s12_geo_location",      "1.2 Basic Process Information", "Geo Location(s)",
            "India / Brazil / Slovakia / Egypt / Multi-Geo",
            "Country"),

        new("s12_timezone",          "1.2 Basic Process Information", "Time Zone Coverage",
            "e.g. GMT, IST, BRT...",
            "TimeZone"),

        // ── 2.1 Operating Model Overview ─────────────────────────────────────
        new("operating_model",       "2.1 Operating Model Overview",  "Operating Model",
            "Describe the team structure, reporting lines, and how the operating model functions across geographies. Note any shared service arrangements, dedicated client teams, or satellite structures. Highlight any named individuals critical to continuity.",
            "AI:operatingModel"),

        // ── 3.1 Process Narrative ─────────────────────────────────────────────
        new("flow_summary",          "3.1 Process Narrative",         "Flow Summary",
            "Describe the end-to-end flow in plain language. Walk through the process as a story: how is it triggered, what happens in sequence, where are the key decision points, and how does it conclude? Note any parallel workstreams or loops.",
            "AI:flowSummary"),

        // ── 3.2 Inputs ───────────────────────────────────────────────────────
        new("input_volume",          "3.2 Inputs",                    "Volume",
            "Typical volume per day/week...",
            "AI:peakVolume"),

        // ── 7. WITO Impact Assessment ─────────────────────────────────────────
        new("wito_summary",          "7. WITO Impact Assessment",     "WITO Summary",
            "Summarise the overall impact of the transition on this process. What changes fundamentally, what stays the same, and where are the highest-risk change points? This section is read by transition leadership.",
            "AI:witoSummary"),

        // ── 10. Reviewer Confidence Score ─────────────────────────────────────
        new("confidence_score",      "10. Reviewer Confidence Score", "Completeness Score",
            "Rate 1 (low confidence) to 5 (complete and verified)...",
            "AI:confidenceScore"),

        new("reviewer_name_date",    "10. Reviewer Confidence Score", "Reviewer Name & Date",
            "Name / Date...",
            "ProcessOwnerContact"),

        new("open_questions",        "10. Reviewer Confidence Score", "Open Questions",
            "List unresolved items...",
            ""),

        new("sign_off_status",       "10. Reviewer Confidence Score", "Sign-off Status",
            "Draft / Reviewed / Approved",
            ""),

        new("next_action",           "10. Reviewer Confidence Score", "Next Action",
            "Enter next step and owner...",
            ""),
    ];

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

        using var wordDoc = WordprocessingDocument.Open(ms, isEditable: true);

        // Process main body
        ApplyReplacements(wordDoc.MainDocumentPart!.Document.Body!, replacements);

        // Process headers and footers
        foreach (var headerPart in wordDoc.MainDocumentPart.HeaderParts)
            ApplyReplacements(headerPart.Header, replacements);
        foreach (var footerPart in wordDoc.MainDocumentPart.FooterParts)
            ApplyReplacements(footerPart.Footer, replacements);

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
}
