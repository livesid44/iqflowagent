using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Extracts plain text from binary document formats (.xlsx, .docx) using the
/// DocumentFormat.OpenXml SDK so their content can be included in AI prompts.
/// </summary>
internal static class DocumentTextExtractor
{
    /// <summary>
    /// Extracts readable text from a binary document.
    /// Returns <c>null</c> if the format is unsupported (<see cref="ext"/> is neither
    /// ".xlsx" nor ".docx") or if the document body is empty after extraction.
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

            foreach (var para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            {
                var text = para.InnerText;
                if (!string.IsNullOrWhiteSpace(text))
                    sb.AppendLine(text);
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }
}
