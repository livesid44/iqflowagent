using System.Text.RegularExpressions;
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
    // Excel's base date epoch (Jan 1, 1900).  Excel incorrectly treats 1900 as a
    // leap year, so dates after Feb 28 1900 are offset by one extra day — but all
    // practical business dates (post-2000) are unaffected by this quirk.
    private static readonly DateTime ExcelEpoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Excel serial number 60 = the phantom Feb 29 1900 that never existed.
    // All serials above this value must be decremented by 1 to compensate for
    // Excel's well-known 1900-leap-year bug.
    private const int ExcelLeapYearBugThreshold = 60;

    // Built-in Excel number format IDs that represent date or date-time values.
    // Reference: OOXML spec §18.8.30 (numFmt).
    private static readonly HashSet<uint> BuiltInDateFormatIds =
    [
        14, 15, 16, 17, 18, 19, 20, 21, 22,  // date & date-time
        45, 46, 47,                            // time
    ];

    // Heuristic: a custom format string that contains d, m, y, or h tokens
    // (case-insensitive) is almost certainly a date/time format.
    private static readonly Regex DateFormatPattern =
        new(@"[dmyh]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extracts readable text from a binary document.
    /// <para>
    /// For <c>.docx</c>: body paragraphs are emitted as individual lines; table rows are
    /// emitted as <em>tab-separated</em> lines (one line per row) so that column structure
    /// is preserved for pattern-based extraction of OCC, SLA, volume tables etc.
    /// </para>
    /// <para>
    /// For <c>.xlsx</c>: each worksheet row is emitted as a tab-separated line.
    /// Date-formatted cells are converted to human-readable date strings (e.g. "Jan-2025")
    /// so that the AI can correctly map volume rows to calendar months.
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

            // Build a map from CellFormat index → is-date-format, so we can detect
            // date-formatted numeric cells (Excel stores dates as numbers with a style).
            var dateFormatIndexes = BuildDateFormatIndexSet(workbookPart);

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
                            {
                                value = sharedStrings[idx];
                            }
                            else if (c.DataType?.Value == CellValues.InlineString)
                            {
                                // Inline-string cells store their text in <is><t>…</t></is>,
                                // not in <v>; reading CellValue.Text returns empty for these.
                                value = c.InlineString?.InnerText ?? raw;
                            }
                            else if (c.DataType == null || c.DataType.Value == CellValues.Number)
                            {
                                // Numeric cell — check whether it carries a date format style.
                                // If so, convert the Excel serial number to a readable date so
                                // that the AI can map rows to calendar months correctly.
                                value = TryConvertDateCell(c, raw, dateFormatIndexes) ?? raw;
                            }
                            else
                            {
                                value = raw;
                            }
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

    /// <summary>
    /// Builds the set of CellFormat indices (from the workbook stylesheet) that
    /// represent date or date-time formats.  Used to detect date-formatted numeric cells.
    /// </summary>
    private static HashSet<uint> BuildDateFormatIndexSet(WorkbookPart workbookPart)
    {
        var result = new HashSet<uint>();
        var stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
        if (stylesheet == null) return result;

        // Collect custom format IDs that look like date patterns
        var customDateFormatIds = new HashSet<uint>();
        var numFmts = stylesheet.NumberingFormats;
        if (numFmts != null)
        {
            foreach (var fmt in numFmts.Elements<NumberingFormat>())
            {
                if (fmt.NumberFormatId?.Value is uint fid && fid >= 164)
                {
                    var fmtCode = fmt.FormatCode?.Value ?? string.Empty;
                    if (DateFormatPattern.IsMatch(fmtCode))
                        customDateFormatIds.Add(fid);
                }
            }
        }

        // Walk CellFormats and record the index of any that map to a date format id
        var cellFormats = stylesheet.CellFormats;
        if (cellFormats == null) return result;

        uint cfIdx = 0;
        foreach (var cf in cellFormats.Elements<CellFormat>())
        {
            var nfId = cf.NumberFormatId?.Value ?? 0;
            if (BuiltInDateFormatIds.Contains(nfId) || customDateFormatIds.Contains(nfId))
                result.Add(cfIdx);
            cfIdx++;
        }

        return result;
    }

    /// <summary>
    /// If the cell has a date-format style index and a parseable numeric value,
    /// converts the Excel date serial to a "MMM-yyyy" string (e.g. "Jan-2025").
    /// Returns <c>null</c> when the cell is not a date cell or conversion fails.
    /// </summary>
    private static string? TryConvertDateCell(Cell cell, string raw, HashSet<uint> dateFormatIndexes)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var serial))
            return null;

        // Only treat as date if the style index is in our detected date-format set
        var styleIdx = cell.StyleIndex?.Value ?? 0;
        if (!dateFormatIndexes.Contains(styleIdx)) return null;

        // Convert Excel serial to DateTime.
        // Excel serial 1 = Jan 1 1900; serial 2 = Jan 2 1900 etc.
        // Excel has a known off-by-one bug where it treats 1900 as a leap year, so
        // serial 60 = Feb 28 1900 and serial 61 = Mar 1 1900.  For serials > 60 we
        // subtract 1 extra day to compensate.  All modern business dates are > 60.
        int days = (int)serial;
        if (days > ExcelLeapYearBugThreshold) days--;  // compensate for the phantom Feb 29 1900
        var date = ExcelEpoch.AddDays(days - 1);

        return date.ToString("MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }
}
