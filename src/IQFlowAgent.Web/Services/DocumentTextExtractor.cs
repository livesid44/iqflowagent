using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;

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

                    var values = cells.Select(c =>
                    {
                        var raw = c.CellValue?.Text ?? "";
                        if (c.DataType?.Value == CellValues.SharedString
                            && int.TryParse(raw, out var idx)
                            && idx < sharedStrings.Count)
                            return sharedStrings[idx];
                        return raw;
                    });

                    var line = string.Join("\t", values);
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
                if (element is Paragraph para)
                {
                    var text = para.InnerText;
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.AppendLine(text);
                }
                else if (element is Table table)
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        // Join each cell's full inner text with a tab so the columns
                        // are distinguishable downstream (e.g. OCC Reference\tObligation\t…)
                        var cells = row.Elements<TableCell>()
                            .Select(c => c.InnerText.Trim())
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
}
