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
                                       "geography", "in-scope"],

        ["Process Overview"]        = ["overview", "description", "process owner", "volume",
                                       "monthly volume", "peak", "hours of operation",
                                       "systems used", "tools", "weekday", "weekend", "holiday"],

        ["RACI"]                    = ["responsible", "accountable", "consulted", "informed",
                                       "raci", "role", "task owner", "matrix", "r/a/c/i"],

        ["SOP Steps"]               = ["step", "sop step", "action", "procedure",
                                       "decision point", "automation", "system used",
                                       "expected output", "process step", "manual"],

        ["Work Instructions"]       = ["work instruction", "navigate", "click", "enter",
                                       "login", "screen", "field", "error handling",
                                       "detailed step", "how to"],

        ["Escalation & Exceptions"] = ["escalation", "exception", "trigger",
                                       "escalation path", "resolution timeframe",
                                       "approval required", "exception handling",
                                       "escalate to"],

        ["SLAs & Performance"]      = ["sla", "service level", "kpi", "metric", "target",
                                       "performance", "measurement", "reporting frequency",
                                       "actual vs target", "turnaround time"],

        ["Volumetrics"]             = ["volume", "transaction volume", "monthly", "peak volume",
                                       "annual", "forecast", "count", "quantity",
                                       "transactions per day", "items per month"],

        ["Regulatory & Compliance"] = ["regulation", "compliance", "gdpr", "iso", "audit",
                                       "control", "evidence", "framework", "obligation",
                                       "standard", "policy"],

        ["Training"]                = ["training", "module", "competency", "e-learning",
                                       "classroom", "on-the-job", "assessment",
                                       "training material", "induction"],

        ["OCC"]                     = ["occ", "orange customer contract", "contract obligation",
                                       "reference number", "customer obligation",
                                       "schedule 8", "sow"],
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
        var scored = chunks
            .Select((chunk, idx) =>
            {
                var lower = chunk.ToLowerInvariant();
                int score = keywords.Sum(kw => CountOccurrences(lower, kw));
                return (chunk, idx, score);
            })
            .OrderByDescending(x => x.score)
            .Take(topK)
            .OrderBy(x => x.idx)   // restore document order for readability
            .ToList();

        return string.Join("\n\n[...]\n\n", scored.Select(x => x.chunk));
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 0;
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
