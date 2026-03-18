using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Extracts plain text from binary document formats (.xlsx, .docx) using the
/// DocumentFormat.OpenXml SDK so their content can be included in AI prompts
/// and in the offline field extractor.
/// </summary>
internal static class DocumentTextExtractor
{
    /// <summary>
    /// Extracts readable text from a binary document.
    /// <para>
    /// For <c>.docx</c>: body paragraphs are emitted as individual lines; table rows are
    /// emitted as <em>tab-separated</em> lines (one line per row) so that column structure
    /// is preserved for pattern-based extraction of OCC, SLA, volume tables etc.
    /// </para>
    /// <para>
    /// For <c>.xlsx</c>: each worksheet row is emitted as a tab-separated line.
    /// </para>
    /// Returns <c>null</c> if the format is unsupported or the document body is empty.
    /// Throws on extraction errors so callers can log with full file context.
    /// </summary>
    public static string? Extract(byte[] bytes, string ext)
    {
        using var ms = new MemoryStream(bytes);
        var sb = new System.Text.StringBuilder();

        if (ext == ".xlsx")
        {
            using var spreadsheet = SpreadsheetDocument.Open(ms, false);
            var workbookPart = spreadsheet.WorkbookPart;
            if (workbookPart == null) return null;

            // Build shared strings table for cell value lookup
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
                .Elements<SharedStringItem>()
                .Select(s => s.InnerText)
                .ToList() ?? [];

            foreach (var sheetPart in workbookPart.WorksheetParts)
            {
                var sheet = sheetPart.Worksheet.GetFirstChild<SheetData>();
                if (sheet == null) continue;

                foreach (var row in sheet.Elements<Row>())
                {
                    var cells = row.Elements<Cell>().ToList();
                    if (!cells.Any()) continue;

                    // ── Column-aligned emission ───────────────────────────────
                    // Excel omits empty cells in sparse rows.  Without position-
                    // awareness, a row with data in columns B, C, D would be
                    // serialised as three values with no leading tab, making the
                    // month column look like it is in column A.
                    // We convert the cell reference (e.g. "C11") to a 0-based
                    // column index and insert empty strings for any skipped columns.
                    var cellDict = new SortedDictionary<int, string>();
                    foreach (var c in cells)
                    {
                        int colIdx = ColumnIndexFromRef(c.CellReference?.Value);

                        string value;
                        if (c.DataType?.Value == CellValues.Error)
                            value = "not available";
                        else
                        {
                            var raw = c.CellValue?.Text ?? "";
                            if (c.DataType?.Value == CellValues.SharedString
                                && int.TryParse(raw, out var idx)
                                && idx < sharedStrings.Count)
                                value = sharedStrings[idx];
                            else
                                value = raw;
                        }

                        cellDict[colIdx] = value;
                    }

                    if (cellDict.Count == 0) continue;

                    // Fill gaps with empty strings so column positions are preserved
                    int maxCol = cellDict.Keys.Max();
                    var ordered = Enumerable.Range(0, maxCol + 1)
                        .Select(i => cellDict.TryGetValue(i, out var v) ? v : string.Empty);

                    var line = string.Join("\t", ordered);
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine(line);
                }
            }
        }
        else if (ext == ".docx")
        {
            using var wordDoc = WordprocessingDocument.Open(ms, false);
            var body = wordDoc.MainDocumentPart?.Document?.Body;
            if (body == null) return null;

            // Iterate top-level body children so we can handle paragraphs and
            // tables distinctly, preserving table column structure.
            foreach (var element in body.ChildElements)
            {
                if (element is DocumentFormat.OpenXml.Wordprocessing.Paragraph para)
                {
                    var text = para.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.AppendLine(text);
                }
                else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
                {
                    foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
                    {
                        // For each cell, join its paragraphs with " | " so bullet-list cells
                        // preserve all individual bullets rather than merging them into one
                        // unreadable blob (c.InnerText concatenates without any separator).
                        var cells = row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>()
                            .Select(c => string.Join(" | ",
                                c.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                                 .Select(p => p.InnerText.Trim())
                                 .Where(t => !string.IsNullOrWhiteSpace(t))))
                            .ToList();
                        var line = string.Join("\t", cells);
                        if (!string.IsNullOrWhiteSpace(line))
                            sb.AppendLine(line);
                    }
                }
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// Converts an Excel cell reference column letters to a 0-based column index.
    /// E.g. "A" → 0, "B" → 1, "Z" → 25, "AA" → 26, "C11" → 2.
    /// Returns 0 for null or empty references.
    /// </summary>
    private static int ColumnIndexFromRef(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;

        // Strip trailing digits (row number)
        int i = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
        var colLetters = cellRef[..i].ToUpperInvariant();

        // Guard: no leading letters means the reference is digits-only or malformed
        if (colLetters.Length == 0) return 0;

        int index = 0;
        foreach (var ch in colLetters)
            index = index * 26 + (ch - 'A' + 1);

        return index - 1; // 0-based
    }
}
