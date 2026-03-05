using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IQFlowAgent.Web.Models;

namespace IQFlowAgent.Web.Services;

public class DocxReportService : IDocxReportService
{
    // ── Static field catalogue ─────────────────────────────────────────────────
    // AutoSource values:
    //   "<PropertyName>" → resolved from IntakeRecord property
    //   "AI:summary"     → pulled from AI analysis JSON summary
    //   ""               → no automatic mapping; AI or user must supply value

    private static readonly List<FieldDefinition> FieldDefs =
    [
        // ── Cover / Header ───────────────────────────────────────────────────
        new("header_process_name",    "Cover",          "Process Name",              "Enter Process Name",        "ProcessName"),
        new("header_service_line",    "Cover",          "Service Line / LOT",        "Enter Service Line / LOT",  "BusinessUnit"),
        new("header_reviewer",        "Cover",          "Reviewer Name",             "Enter Reviewer Name",       "ProcessOwnerName"),
        new("header_review_date",     "Cover",          "Review Date",               "DD MMM YYYY",               "TODAY"),

        // ── 0. Client Inputs ─────────────────────────────────────────────────
        new("client_doc1",            "0. Client Inputs & Artefacts",  "Document / Artefact #1",   "Enter document name...",    "UploadedFileName"),

        // ── 1.1 Process Overview ─────────────────────────────────────────────
        new("process_summary",        "1.1 Process Overview",          "Process Summary",           "Describe the process in your own words. Cover: what it does, who it serves, how it is triggered, and why it matters to the business. Avoid bullet points here — use complete sentences so this section can be read in isolation by a senior stakeholder.", "AI:summary"),

        // ── 1.2 Basic Process Information ────────────────────────────────────
        new("basic_process_name",     "1.2 Basic Process Information", "Process Name",              "Enter process name...",     "ProcessName"),
        new("basic_service_line",     "1.2 Basic Process Information", "Service Line / LOT",        "Enter service line...",     "BusinessUnit"),
        new("basic_sub_process",      "1.2 Basic Process Information", "Sub-process Name",          "If applicable...",          ""),
        new("basic_process_category", "1.2 Basic Process Information", "Process Category",          "Service Delivery / Service Assurance / Service Management / Billing & Invoicing", "ProcessType"),
        new("basic_process_owner",    "1.2 Basic Process Information", "Process Owner",             "Name and role...",          "ProcessOwnerName"),
        new("basic_geo_location",     "1.2 Basic Process Information", "Geo Location(s)",           "India / Brazil / Slovakia / Egypt / Multi-Geo", "GeoLocation"),
        new("basic_time_zone",        "1.2 Basic Process Information", "Time Zone Coverage",        "e.g. GMT, IST, BRT...",     "TimeZone"),

        // ── 1.3 Service & Customer Scope ─────────────────────────────────────
        new("services_scope",         "1.3 Service & Customer Scope",  "Services Scope",            "List services in scope per customer...", "Description"),
        new("product_portfolio",      "1.3 Service & Customer Scope",  "Product Portfolio",         "List products covered...",  ""),
        new("contractual_commits",    "1.3 Service & Customer Scope",  "Contractual Commitments",   "SLAs, CSI targets, governance clauses...", ""),

        // ── 2.1 Operating Model Overview ─────────────────────────────────────
        new("operating_model",        "2. Operating Model Mapping",    "Operating Model Description", "Describe the team structure, reporting lines, and how the operating model functions across geographies. Note any shared service arrangements, dedicated client teams, or satellite structures. Highlight any named individuals critical to continuity.", "AI:operatingModel"),

        // ── 2.2 Roles & Responsibilities ─────────────────────────────────────
        new("named_resources",        "2.2 Roles & Responsibilities",  "Named / Critical Resources", "List names and roles...",   "ProcessOwnerName"),
        new("skill_gaps",             "2.2 Roles & Responsibilities",  "Skill Gaps Identified",     "Describe gaps...",          ""),

        // ── 3.1 Process Narrative ─────────────────────────────────────────────
        new("flow_summary",           "3.1 Process Narrative",         "Process Flow Summary",      "Describe the end-to-end flow in plain language. Walk through the process as a story: how is it triggered, what happens in sequence, where are the key decision points, and how does it conclude? Note any parallel workstreams or loops.", "AI:flowSummary"),

        // ── 9. Reversibility & Exit ───────────────────────────────────────────
        new("exit_risks",             "9. Reversibility & Exit",       "Risks from Transition",     "Risks from transition...",  ""),

        // ── 10. Reviewer Confidence ───────────────────────────────────────────
        new("confidence_score",       "10. Reviewer Confidence Score", "Confidence Score (1-5)",    "Rate 1 (low confidence) to 5 (complete and verified)...", "AI:confidenceScore"),
        new("next_steps",             "10. Reviewer Confidence Score", "Next Steps / Owner",        "Enter next step and owner...", ""),
        new("unresolved_items",       "10. Reviewer Confidence Score", "Unresolved Items",          "List unresolved items...",  ""),
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
        foreach (var text in root.Descendants<Text>())
        {
            foreach (var kvp in replacements)
            {
                if (text.Text.Contains(kvp.Key, StringComparison.Ordinal))
                    text.Text = text.Text.Replace(kvp.Key, kvp.Value, StringComparison.Ordinal);
            }
        }
    }
}
