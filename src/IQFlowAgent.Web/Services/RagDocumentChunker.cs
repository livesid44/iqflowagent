namespace IQFlowAgent.Web.Services;

/// <summary>
/// Lightweight RAG helper: splits a document into overlapping text chunks and
/// retrieves the most relevant chunks for each BARTOK S8 SOP section using
/// keyword-frequency scoring.  No embedding model or vector store is required —
/// this is a lexical RAG approach that is fast, zero-cost, and effective for
/// structured business-process documents.
///
/// Upgrade path: replace <see cref="GetTopChunksForSection"/> with a semantic
/// vector-similarity lookup (e.g., Azure AI Search or a local Faiss index)
/// once an embedding deployment is available.
/// </summary>
internal static class RagDocumentChunker
{
    /// <summary>Characters per chunk — large enough to contain a full paragraph or table section.</summary>
    private const int ChunkSize = 1_500;

    /// <summary>Overlap between consecutive chunks — preserves context at chunk boundaries.</summary>
    private const int ChunkOverlap = 200;

    /// <summary>Maximum chunks returned per section call.</summary>
    internal const int DefaultTopK = 4;

    // ── BARTOK section → representative keywords (lowercased) ────────────────
    // Keywords drive the retrieval scoring: a chunk that contains more of the
    // section-specific terms ranks higher and is included in the focused context
    // sent to the LLM for that section's checkpoint assessment.
    internal static readonly Dictionary<string, string[]> SectionKeywords =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["Document Control"]        = ["author", "approver", "version", "document date",
                                       "created by", "document owner", "sign-off", "review date",
                                       "lot", "sdc", "prepared by"],

        ["Purpose & Scope"]         = ["purpose", "scope", "objective", "countries in scope",
                                       "input artefact", "applicable", "intended for",
                                       "geography", "in-scope", "aim", "goal", "applies to",
                                       "this document", "this process", "this sop",
                                       "this procedure", "introduction", "in scope",
                                       "out of scope", "coverage", "applicability"],

        ["Process Overview"]        = ["overview", "description", "process owner", "volume",
                                       "monthly volume", "peak", "hours of operation",
                                       "systems used", "tools", "weekday", "weekend", "holiday",
                                       "incident", "unplanned", "disruption", "service",
                                       "major incident", "definition", "terminology",
                                       "background", "context", "process description",
                                       "business process", "as-is process", "to-be process",
                                       "lifecycle", "end to end", "end-to-end"],

        ["RACI"]                    = ["responsible", "accountable", "consulted", "informed",
                                       "raci", "role", "task owner", "matrix", "r/a/c/i",
                                       "who is", "team responsible", "assigned to",
                                       "accountability", "ownership", "stakeholder",
                                       "responsible party", "roles and responsibilities",
                                       "responsibility matrix", "team ownership"],

        // Broad: covers any described process activity, incident-type workflows,
        // ServiceNow case handling, categorisation, investigation, resolution, etc.
        ["SOP Steps"]               = ["step", "sop step", "action", "procedure",
                                       "decision point", "automation", "system used",
                                       "expected output", "process step", "manual",
                                       "incident", "case", "task", "log", "record",
                                       "categorize", "categorise", "investigate",
                                       "diagnose", "resolve", "prioritize", "prioritise",
                                       "workflow", "process flow", "handle", "assign",
                                       "ticket", "request", "classification", "unify",
                                       "servicenow", "service now", "mytools",
                                       "level 1", "level 2", "l1", "l2", "support team",
                                       "impact", "urgency", "priority"],

        // Broad: covers portal usage, ServiceNow navigation, system-specific steps
        ["Work Instructions"]       = ["work instruction", "navigate", "click", "enter",
                                       "login", "screen", "field", "error handling",
                                       "detailed step", "how to", "portal", "access",
                                       "log in", "system access", "unify desk",
                                       "servicenow", "mytools", "tool",
                                       "complete the form", "fill in", "submit",
                                       "open a", "create a", "update the",
                                       "user guide", "system guide", "instruction",
                                       "reference guide", "quick guide",
                                       "select", "choose", "drop-down", "dropdown",
                                       "search for", "filter by"],

        // Broad: covers RCA, post-incident review, PMIR, corrective/preventive actions
        ["Escalation & Exceptions"] = ["escalation", "exception", "trigger",
                                       "escalation path", "resolution timeframe",
                                       "approval required", "exception handling",
                                       "escalate to",
                                       "rca", "root cause", "root cause analysis",
                                       "post-incident", "post incident",
                                       "post major incident", "incident review",
                                       "pmir", "post major incident review",
                                       "corrective action", "preventive action",
                                       "lessons learned", "draft rca", "final rca",
                                       "investigation", "incident resolution",
                                       "major incident", "sla breach",
                                       "business days", "2 business days", "5 business days"],

        ["SLAs & Performance"]      = ["sla", "service level", "kpi", "metric", "target",
                                       "performance", "measurement", "reporting frequency",
                                       "actual vs target", "turnaround time",
                                       "breach", "sla breach", "resolution time",
                                       "response time", "within", "business days",
                                       "time to resolve", "time to respond",
                                       "2 business days", "5 business days"],

        ["Volumetrics"]             = ["volume", "transaction volume", "monthly", "peak volume",
                                       "annual", "forecast",
                                       "transactions per day", "items per month",
                                       "daily volumes", "weekly volumes", "per day", "per month",
                                       "total transactions", "transaction count",
                                       "incidents per", "cases per", "requests per",
                                       "high season", "low season"],

        ["Regulatory & Compliance"] = ["regulation", "compliance regulation", "gdpr", "iso 27001",
                                       "iso 9001", "iso 22301", "hipaa", "sox",
                                       "audit", "audit trail", "audit evidence",
                                       "evidence", "regulatory obligation", "regulatory framework",
                                       "data protection", "data privacy",
                                       "data protection regulation", "dpa 2018",
                                       "legislative", "legal requirement", "legal obligation",
                                       "policy compliance", "regulatory compliance",
                                       "techm control framework", "techm framework"],

        ["Training"]                = ["training", "training module", "training material",
                                       "competency", "e-learning", "elearning",
                                       "classroom training", "on-the-job training",
                                       "induction", "learning objectives",
                                       "certification", "knowledge check",
                                       "onboarding", "upskilling", "refresher"],

        ["OCC"]                     = ["occ", "orange customer contract",
                                       "contract obligation", "customer obligation",
                                       "schedule 8", "sow", "statement of work",
                                       "contractual obligation", "customer contract",
                                       "occ reference", "occ number", "occ clause"],

        ["Glossary"]                = ["glossary", "term", "definition", "acronym",
                                       "abbreviation", "terminology", "meaning",
                                       "stands for", "refers to", "defined as"],
    };

    /// <summary>
    /// Returns a richer natural-language search query for a BARTOK section.
    /// Used for vector/semantic search so that section embeddings cast a wider
    /// net than the bare section name alone.
    /// </summary>
    internal static string GetSectionSearchQuery(string sectionId, string sectionName) =>
        sectionId switch
        {
            "DC"  => "document control author approver version date prepared by sign-off",
            "1"   => "purpose scope objective countries in scope applicable intended for input artefacts",
            "2"   => "process overview description background context systems used tools process owner incident major incident definitions terminology",
            "3"   => "RACI responsible accountable consulted informed role assignment matrix",
            "4"   => "process steps SOP steps procedures workflow actions incident case handling categorise investigate resolve assign ticket ServiceNow Unify Desk",
            "5"   => "work instructions detailed steps portal access navigate system how to log in fill in submit create update",
            "6"   => "escalation exceptions triggers escalation path RCA root cause analysis post-incident review PMIR corrective preventive actions major incident resolution",
            "7"   => "SLA service level performance metrics KPI target measurement reporting breach resolution timeframe business days",
            "8"   => "volumetrics transaction volume monthly peak annual forecast transactions per day per month",
            "9"   => "regulatory compliance GDPR ISO 27001 ISO 9001 audit data protection regulation legislation legal obligation",
            "10"  => "training module competency e-learning classroom induction on-the-job training material upskilling",
            "11"  => "OCC Orange Customer Contract obligation schedule 8 contractual obligation",
            "12"  => "glossary term definition acronym abbreviation terminology meaning",
            _     => sectionName,
        };

    /// <summary>
    /// Ordered list of all BARTOK sections: (sectionId, sectionName).
    /// Used when iterating all sections to build a RAG-structured user message.
    /// </summary>
    internal static readonly (string Id, string Name)[] BartokSections =
    [
        ("DC", "Document Control"),
        ("1",  "Purpose & Scope"),
        ("2",  "Process Overview"),
        ("3",  "RACI"),
        ("4",  "SOP Steps"),
        ("5",  "Work Instructions"),
        ("6",  "Escalation & Exceptions"),
        ("7",  "SLAs & Performance"),
        ("8",  "Volumetrics"),
        ("9",  "Regulatory & Compliance"),
        ("10", "Training"),
        ("11", "OCC"),
        ("12", "Glossary"),
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits <paramref name="text"/> into overlapping chunks of approximately
    /// <see cref="ChunkSize"/> characters, preferring to break on whitespace
    /// to avoid cutting words mid-way.
    /// </summary>
    public static List<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var chunks = new List<string>();
        int start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + ChunkSize, text.Length);

            // Try to snap the chunk boundary to a whitespace character so we
            // don't cut in the middle of a word.
            if (end < text.Length)
            {
                int ws = text.LastIndexOf(' ', end, Math.Min(end - start, 200));
                if (ws > start) end = ws;
            }

            chunks.Add(text[start..end]);
            if (end >= text.Length) break;

            // Advance by (ChunkSize - overlap); always move forward at least 1 char.
            int advance = Math.Max(ChunkSize - ChunkOverlap, 1);
            start = Math.Min(start + advance, end);
        }

        return chunks;
    }

    /// <summary>
    /// Returns the <paramref name="topK"/> chunks most relevant to
    /// <paramref name="sectionName"/>, ordered by their original position in
    /// the document so the excerpts read naturally.
    /// Returns an empty string if <paramref name="chunks"/> is empty.
    /// </summary>
    public static string GetTopChunksForSection(
        string sectionName, IReadOnlyList<string> chunks, int topK = DefaultTopK)
    {
        if (chunks.Count == 0) return string.Empty;

        if (!SectionKeywords.TryGetValue(sectionName, out var keywords))
            return string.Join("\n\n", chunks.Take(topK));

        // Score each chunk by total keyword frequency (simple but effective).
        // Only return chunks that actually contain at least one section keyword —
        // zero-score chunks are random document content unrelated to this section
        // and would cause the AI to label the section as CONTENT FOUND incorrectly.
        var scored = chunks
            .Select((chunk, idx) =>
            {
                var lower = chunk.ToLowerInvariant();
                int score = keywords.Sum(kw => CountOccurrences(lower, kw));
                return (chunk, idx, score);
            })
            .Where(x => x.score > 0)   // exclude chunks with no keyword matches
            .OrderByDescending(x => x.score)
            .Take(topK)
            .OrderBy(x => x.idx)   // restore document order for readability
            .ToList();

        if (scored.Count == 0) return string.Empty;   // no relevant content found

        return string.Join("\n\n[...]\n\n", scored.Select(x => x.chunk));
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Counts how many times <paramref name="pattern"/> appears in <paramref name="text"/>.
    /// For single-word patterns (those without a space), a lightweight word-boundary
    /// check is applied: the character immediately before and after the match must
    /// not be an ASCII letter.  This prevents short keywords such as "occ" from
    /// matching inside longer words like "occasionally", and "iso" from matching
    /// inside "supervisor".
    /// Multi-word phrases are matched as plain substrings (no boundary check) because
    /// they are already specific enough to avoid false positives.
    /// </summary>
    private static int CountOccurrences(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 0;
        bool requireWordBoundary = !pattern.Contains(' ');
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            if (requireWordBoundary)
            {
                bool startOk = idx == 0 || !char.IsLetter(text[idx - 1]);
                bool endOk   = idx + pattern.Length >= text.Length
                               || !char.IsLetter(text[idx + pattern.Length]);
                if (startOk && endOk) count++;
            }
            else
            {
                count++;
            }
            idx += pattern.Length;
        }
        return count;
    }
}
