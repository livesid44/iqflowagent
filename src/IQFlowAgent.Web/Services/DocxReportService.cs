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
        // ── Cover ────────────────────────────────────────────────────────────
        new("cover_process_name",    "Cover",                    "Process Name",
            "[Process Name]",
            "ProcessName"),

        new("cover_lot",             "Cover",                    "Lot Number and Name",
            "[Lot Number and Name]",
            "SdcLots"),

        new("cover_date",            "Cover",                    "Document Date",
            "[Add date in dd-mmm-yyyy format e.g. 01-May-2026]",
            "TODAY"),

        new("cover_author",          "Cover",                    "Process Author",
            "[Process Author Name | Email]",
            "ProcessOwnerContact"),

        new("cover_approver",        "Cover",                    "Approver",
            "[Approver Name | Email]",
            ""),

        // ── 1. Purpose and Scope ─────────────────────────────────────────────
        new("scope_countries",       "1. Purpose and Scope",     "Countries in Scope",
            "[List all countries where this process operates]",
            "Country"),

        // ── 1.2 Inputs & Artefacts ───────────────────────────────────────────
        new("artefact_doc1",         "1.2 Inputs & Artefacts",   "Document / Artefact #1",
            "Enter document name...",
            "UploadedFileName"),

        // ── 2. Process Overview ───────────────────────────────────────────────
        new("process_description",   "2. Process Overview",      "Process Description",
            "Detailed description",
            "AI:summary"),

        new("process_owner",         "2. Process Overview",      "Process Owner (Name, Role, OB)",
            "[Name, Role, OB]",
            "ProcessOwnerName"),

        new("peak_volume",           "2. Process Overview",      "Peak Volume",
            "[Peak volume and period — e.g. month-end, quarter-end]",
            "AI:peakVolume"),

        new("systems_used",          "2. Process Overview",      "Systems Used",
            "[Primary systems — ERP, ticketing, reporting tools]",
            "AI:systemsUsed"),

        // ── 5. Work Instructions ─────────────────────────────────────────────
        new("work_instructions",     "5. Work Instructions",     "Step 1 Instructions",
            "[Instruction 1 — system navigation, field entries, validation checks]",
            "AI:workInstructions"),

        // ── 9. Regulatory and Compliance ─────────────────────────────────────
        new("control_framework_ref", "9. Regulatory and Compliance", "Control Framework Reference",
            "[TechM Control Framework Document Reference]",
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

        // Build replacement dictionary: placeholder → fill value
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fs in fieldStatuses)
        {
            var value = fs.IsNA ? "N/A" : (fs.FillValue ?? string.Empty);
            if (!string.IsNullOrEmpty(fs.TemplatePlaceholder))
                replacements[fs.TemplatePlaceholder] = value;
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
