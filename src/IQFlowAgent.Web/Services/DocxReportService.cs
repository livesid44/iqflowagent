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
        // TemplatePlaceholder must exactly match the text inside BARTOK_DD_Template_v2.docx.
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

        new("process_context",       "1.1 Process Overview",          "Process Context",
            "Include any important context about the process history, recent changes, or known pain points at a high level.",
            "AI:processContext"),

        // ── 1.2 Basic Process Information ────────────────────────────────────
        new("s12_process_name",      "1.2 Basic Process Information", "Process Name",
            "Enter process name...",
            "ProcessName"),

        new("s12_service_line",      "1.2 Basic Process Information", "Service Line / LOT",
            "Enter service line...",
            "SdcLots"),

        new("s12_sub_process",       "1.2 Basic Process Information", "Sub-process Name",
            "If applicable...",
            "AI:subProcess"),

        new("s12_process_category",  "1.2 Basic Process Information", "Process Category",
            "Service Delivery / Service Assurance / Service Management / Billing & Invoicing",
            "AI:processCategory"),

        new("s12_process_owner",     "1.2 Basic Process Information", "Process Owner",
            "Name and role...",
            "ProcessOwnerContact"),

        new("s12_geo_location",      "1.2 Basic Process Information", "Geo Location(s)",
            "India / Brazil / Slovakia / Egypt / Multi-Geo",
            "Country"),

        new("s12_timezone",          "1.2 Basic Process Information", "Time Zone Coverage",
            "e.g. GMT, IST, BRT...",
            "TimeZone"),

        new("s12_shift_model",       "1.2 Basic Process Information", "Shift Model",
            "24x7 / Business hours / Follow-the-sun",
            "AI:shiftModel"),

        new("s12_team_size",         "1.2 Basic Process Information", "Team Size (FTE)",
            "Total FTE count...",
            "AI:teamSize"),

        new("s12_skill_profile",     "1.2 Basic Process Information", "Skill Profile",
            "L1 / L2 / L3 / SME / Architect — describe mix",
            "AI:skillProfile"),

        // ── 1.3 Service & Customer Scope ─────────────────────────────────────
        new("s13_services_scope",    "1.3 Service & Customer Scope",  "Services Scope",
            "List services in scope per customer...",
            "AI:servicesScope"),

        new("s13_products",          "1.3 Service & Customer Scope",  "Product Portfolio",
            "List products covered...",
            "AI:productPortfolio"),

        new("s13_sla_commitments",   "1.3 Service & Customer Scope",  "Contractual Commitments",
            "SLAs, CSI targets, governance clauses...",
            "AI:slaCommitments"),

        new("s13_customer_type",     "1.3 Service & Customer Scope",  "Customer Type",
            "Strategic / Non-strategic",
            "AI:customerType"),

        new("s13_renewal_timelines", "1.3 Service & Customer Scope",  "Contract Renewal Timelines",
            "Enter dates or timeframes...",
            "AI:renewalTimelines"),

        new("s13_exit_projects",     "1.3 Service & Customer Scope",  "Customer Exit Projects",
            "Any ongoing exits — describe...",
            "AI:exitProjects"),

        // ── 2.1 Operating Model Overview ─────────────────────────────────────
        new("operating_model",       "2.1 Operating Model Overview",  "Operating Model",
            "Describe the team structure, reporting lines, and how the operating model functions across geographies. Note any shared service arrangements, dedicated client teams, or satellite structures. Highlight any named individuals critical to continuity.",
            "AI:operatingModel"),

        // ── 2.2 Roles & Responsibilities ─────────────────────────────────────
        new("s22_named_resources",   "2.2 Roles & Responsibilities",  "Named / Critical Resources",
            "List names and roles...",
            "AI:namedResources"),

        new("s22_key_personnel",     "2.2 Roles & Responsibilities",  "Contractual Key Personnel",
            "As per contract schedule...",
            "AI:keyPersonnel"),

        new("s22_skill_gaps",        "2.2 Roles & Responsibilities",  "Skill Gaps Identified",
            "Describe gaps...",
            "AI:skillGaps"),

        new("s22_seed_team",         "2.2 Roles & Responsibilities",  "Seed Team Requirement",
            "Post-transition seed team need...",
            "AI:seedTeam"),

        new("s22_escalation",        "2.2 Roles & Responsibilities",  "Escalation Hierarchy",
            "L1 → L2 → L3 → Management path...",
            "AI:escalationHierarchy"),

        new("s22_org_hierarchy",     "2.2 Roles & Responsibilities",  "Org Hierarchy",
            "Map to current org structure...",
            "AI:orgHierarchy"),

        // ── 2.5 Geo Distribution ──────────────────────────────────────────────
        new("s25_follow_sun",        "2.5 Geo Distribution",         "Follow-the-Sun Model?",
            "Yes / No — describe handoff points",
            "AI:followSunModel"),

        new("s25_regional_variations", "2.5 Geo Distribution",       "Regional Variations",
            "Any process differences by country?",
            "AI:regionalVariations"),

        new("s25_delivery_model",    "2.5 Geo Distribution",         "Delivery Model",
            "Shared / Dedicated / Satellite",
            "AI:deliveryModel"),

        new("s25_geo_risks",         "2.5 Geo Distribution",         "Geo Dependency Risks",
            "Single-location risks...",
            "AI:geoDependencyRisks"),

        new("s25_bcp",               "2.5 Geo Distribution",         "Business Continuity per Geo",
            "Describe BCP per geo...",
            "AI:bcpPerGeo"),

        // ── 3.1 Process Narrative ─────────────────────────────────────────────
        new("flow_summary",          "3.1 Process Narrative",         "Flow Summary",
            "Describe the end-to-end flow in plain language. Walk through the process as a story: how is it triggered, what happens in sequence, where are the key decision points, and how does it conclude? Note any parallel workstreams or loops.",
            "AI:flowSummary"),

        // ── 3.2 Inputs ───────────────────────────────────────────────────────
        new("s32_input_source",      "3.2 Inputs",                   "Input Source",
            "Customer / Monitoring / Partner / Internal",
            "AI:inputSource"),

        new("s32_input_format",      "3.2 Inputs",                   "Input Format",
            "API / Email / Portal / E-bonding / Manual",
            "AI:inputFormat"),

        new("s32_frequency",         "3.2 Inputs",                   "Frequency",
            "Real-time / Hourly / Daily / Weekly",
            "AI:processFrequency"),

        new("input_volume",          "3.2 Inputs",                   "Volume",
            "Typical volume per day/week...",
            "AI:peakVolume"),

        new("s32_data_issues",       "3.2 Inputs",                   "Data Completeness Issues",
            "Known gaps or quality issues...",
            "AI:dataQualityIssues"),

        // ── 3.4 Outputs ───────────────────────────────────────────────────────
        new("s34_output",            "3.4 Outputs",                  "Output Generated",
            "Describe the output...",
            "AI:processOutput"),

        new("s34_recipient",         "3.4 Outputs",                  "Recipient",
            "Who receives it?",
            "AI:outputRecipient"),

        // ── 3.6 Controls & Governance ─────────────────────────────────────────
        new("s36_governance_forum",  "3.6 Controls & Governance",    "Governance Forum",
            "Name, frequency...",
            "AI:governanceForum"),

        new("s36_control_checkpoints", "3.6 Controls & Governance",  "Control Checkpoints",
            "List checkpoints...",
            "AI:controlCheckpoints"),

        // ── 3.7 Exceptions & Variations ──────────────────────────────────────
        new("s37_exceptions",        "3.7 Exceptions & Variations",  "Known Exceptions",
            "Describe common exceptions...",
            "AI:knownExceptions"),

        new("s37_exception_volume",  "3.7 Exceptions & Variations",  "Exception Volume (%)",
            "Approx % of total volume...",
            "AI:exceptionVolume"),

        // ── 4.1 Baseline Commentary ───────────────────────────────────────────
        new("s41_baseline_commentary", "4.1 Baseline Commentary",    "Reviewer Commentary",
            "Comment on the quality and reliability of the baseline data. Note whether figures are system-generated or estimated, any recent performance trends, and whether there are disputes about SLA measurement methodology.",
            "AI:baselineCommentary"),

        // ── 5.1 Tool Landscape ────────────────────────────────────────────────
        new("s51_tools",             "5.1 Tool Landscape",           "Global vs Regional Tools",
            "List tools and scope...",
            "AI:toolLandscape"),

        new("s51_api_maturity",      "5.1 Tool Landscape",           "API Maturity",
            "Low / Medium / High — describe...",
            "AI:apiMaturity"),

        // ── 5.2 Digital Platform & Automation ────────────────────────────────
        new("s52_automation_opps",   "5.2 Digital Platform & Automation Requirements", "Automation Opportunities",
            "Describe feasible automations...",
            "AI:automationOpps"),

        new("s52_ai_use_cases",      "5.2 Digital Platform & Automation Requirements", "AI Use Cases Identified",
            "Describe...",
            "AI:aiUseCases"),

        // ── 6.1 Data Sources & Quality ────────────────────────────────────────
        new("s61_data_platform",     "6.1 Data Sources & Quality",   "Data Platform Usage",
            "EDH / other — describe...",
            "AI:dataPlatform"),

        new("s61_data_quality",      "6.1 Data Sources & Quality",   "Known Data Quality Issues",
            "High / Medium / Low — gaps...",
            "AI:dataQualityRating"),

        new("s61_data_ownership",    "6.1 Data Sources & Quality",   "Data Ownership",
            "Who owns each source?",
            "AI:dataOwnership"),

        // ── 7. WITO Impact Assessment ─────────────────────────────────────────
        new("wito_summary",          "7. WITO Impact Assessment",     "WITO Summary",
            "Summarise the overall impact of the transition on this process. What changes fundamentally, what stays the same, and where are the highest-risk change points? This section is read by transition leadership.",
            "AI:witoSummary"),

        new("s71_resolver_changes",  "7. WITO Impact Assessment",     "Resolver Group Restructuring",
            "Changes expected...",
            "AI:resolverChanges"),

        new("s71_transition_risk",   "7. WITO Impact Assessment",     "Contractual Performance Risk",
            "Risks from transition...",
            "AI:transitionRisk"),

        // ── 9. Reversibility & Exit Assessment ───────────────────────────────
        new("s91_active_exits",      "9. Reversibility & Exit Assessment", "Ongoing Customer Exit Projects",
            "Describe active exits...",
            "AI:activeExits"),

        new("s91_asset_billing",     "9. Reversibility & Exit Assessment", "Asset-based Invoicing Impact",
            "Describe asset billing risk...",
            "AI:assetBilling"),

        new("s91_exit_obligations",  "9. Reversibility & Exit Assessment", "Exit Obligations",
            "Contractual exit clauses...",
            "AI:exitObligations"),

        new("s91_kt_risk",           "9. Reversibility & Exit Assessment", "Knowledge Transfer Risk",
            "Key person dependencies...",
            "AI:ktRisk"),

        new("s91_reversibility",     "9. Reversibility & Exit Assessment", "Reversibility Clauses",
            "Any in-scope? Describe...",
            "AI:reversibilityClauses"),

        // ── 10. Reviewer Confidence Score ─────────────────────────────────────
        new("confidence_score",      "10. Reviewer Confidence Score", "Completeness Score",
            "Rate 1 (low confidence) to 5 (complete and verified)...",
            "AI:confidenceScore"),

        new("reviewer_name_date",    "10. Reviewer Confidence Score", "Reviewer Name & Date",
            "Name / Date...",
            "ProcessOwnerContact"),

        new("pending_documents",     "10. Reviewer Confidence Score", "Documents Pending",
            "List outstanding documents...",
            "AI:pendingDocuments"),

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
