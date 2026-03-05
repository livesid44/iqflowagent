using System.Text;
using System.Text.RegularExpressions;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Converts a SOP markdown document to a standards-compliant PDF 1.4 file using only
/// built-in Type1 fonts (Helvetica / Helvetica-Bold).  No external library or font
/// embedding is required; the output is a self-contained binary PDF.
/// </summary>
internal static partial class SopPdfRenderer
{
    // ── A4 page geometry (PDF user units = points, 72 pts = 1 inch) ──────────
    private const float PageWidth  = 595f;
    private const float PageHeight = 842f;
    private const float MarginLeft = 65f;
    private const float MarginTop  = 55f;
    private const float MarginBot  = 60f;
    private const float TextWidth  = PageWidth - MarginLeft - 65f;  // ≈ 465 pts

    // Approximate average advance width of a Helvetica glyph at 1 pt
    private const float AvgGlyph  = 0.53f;

    // PDF font resource names used in page /Resources dict
    private const string FontRegular = "F1";  // Helvetica
    private const string FontBold    = "F2";  // Helvetica-Bold

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>Renders <paramref name="markdown"/> to a PDF byte array.</summary>
    public static byte[] Render(string markdown, string docTitle)
    {
        var elements = ParseMarkdown(markdown ?? string.Empty);
        var pages    = Paginate(elements);
        return AssemblePdf(pages, docTitle ?? string.Empty);
    }

    // ── Step 1: Parse markdown into typed elements ────────────────────────────

    private enum EKind { H1, H2, H3, Body, Bullet, Rule }
    private sealed record Elem(EKind Kind, string Text);

    private static List<Elem> ParseMarkdown(string md)
    {
        var list = new List<Elem>();
        foreach (var raw in md.ReplaceLineEndings("\n").Split('\n'))
        {
            var l = raw.TrimEnd();
            if      (l.StartsWith("### ", StringComparison.Ordinal)) list.Add(new(EKind.H3,     l[4..].Trim()));
            else if (l.StartsWith("## ",  StringComparison.Ordinal)) list.Add(new(EKind.H2,     l[3..].Trim()));
            else if (l.StartsWith("# ",   StringComparison.Ordinal)) list.Add(new(EKind.H1,     l[2..].Trim()));
            else if (l.StartsWith("- ",   StringComparison.Ordinal)
                 ||  l.StartsWith("* ",   StringComparison.Ordinal)) list.Add(new(EKind.Bullet, l[2..].Trim()));
            else if (l == "---" || l == "===")                        list.Add(new(EKind.Rule,   string.Empty));
            else if (!string.IsNullOrWhiteSpace(l))                   list.Add(new(EKind.Body,   StripInline(l.Trim())));
            // blank lines → implicit vertical space (handled in Paginate)
        }
        return list;
    }

    // Strip **bold**, *italic*, and `code` markers from inline text
    [System.Text.RegularExpressions.GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial System.Text.RegularExpressions.Regex BoldPattern();
    [System.Text.RegularExpressions.GeneratedRegex(@"\*(.+?)\*")]
    private static partial System.Text.RegularExpressions.Regex ItalicPattern();
    [System.Text.RegularExpressions.GeneratedRegex(@"`(.+?)`")]
    private static partial System.Text.RegularExpressions.Regex CodePattern();

    private static string StripInline(string s)
    {
        s = BoldPattern().Replace(s, "$1");
        s = ItalicPattern().Replace(s, "$1");
        s = CodePattern().Replace(s, "$1");
        return s;
    }

    // ── Step 2: Layout — wrap text and split into pages ───────────────────────

    private sealed record RLine(float X, float Y, float Sz, bool IsBold, string Text);

    private static List<List<RLine>> Paginate(List<Elem> elems)
    {
        var pages = new List<List<RLine>>();
        var page  = NewPage(pages);
        float y   = PageHeight - MarginTop;

        void PutLine(string text, bool bold, float sz, float indent = 0)
        {
            if (y - sz < MarginBot) { page = NewPage(pages); y = PageHeight - MarginTop; }
            y -= sz;
            page.Add(new RLine(MarginLeft + indent, y, sz, bold, text));
        }

        void Gap(float pts)
        {
            y -= pts;
            if (y < MarginBot) { page = NewPage(pages); y = PageHeight - MarginTop; }
        }

        Elem? prev = null;
        foreach (var el in elems)
        {
            switch (el.Kind)
            {
                case EKind.H1:
                    if (prev is not null) Gap(12);
                    foreach (var s in WrapWords(el.Text, TextWidth, 20f))
                        PutLine(s, true, 24f);
                    Gap(6);
                    break;

                case EKind.H2:
                    if (prev is not null) Gap(10);
                    foreach (var s in WrapWords(el.Text, TextWidth, 14f))
                        PutLine(s, true, 17f);
                    Gap(4);
                    break;

                case EKind.H3:
                    if (prev is not null) Gap(6);
                    foreach (var s in WrapWords(el.Text, TextWidth, 12f))
                        PutLine(s, true, 14.5f);
                    Gap(2);
                    break;

                case EKind.Bullet:
                    foreach (var s in WrapWords("- " + el.Text, TextWidth - 14f, 11f))
                        PutLine(s, false, 13.5f, 14f);
                    break;

                case EKind.Body:
                    foreach (var s in WrapWords(el.Text, TextWidth, 11f))
                        PutLine(s, false, 13.5f);
                    break;

                case EKind.Rule:
                    Gap(6);
                    break;
            }
            prev = el;
        }

        return pages;
    }

    private static List<RLine> NewPage(List<List<RLine>> pages)
    {
        var p = new List<RLine>();
        pages.Add(p);
        return p;
    }

    // Word-wrap a single string to fit within <maxWidth> pts at <fontSize>
    private static IEnumerable<string> WrapWords(string text, float maxWidth, float fontSize)
    {
        int cap = Math.Max(10, (int)(maxWidth / (fontSize * AvgGlyph)));
        if (text.Length <= cap) { yield return text; yield break; }

        var cur = new StringBuilder(cap + 30);
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            bool overflows = cur.Length > 0 && cur.Length + 1 + word.Length > cap;
            if (overflows)
            {
                yield return cur.ToString();
                cur.Clear();
            }
            else if (cur.Length > 0)
                cur.Append(' ');
            cur.Append(word);
        }
        if (cur.Length > 0) yield return cur.ToString();
    }

    // ── Step 3: Emit a valid PDF 1.4 binary ──────────────────────────────────
    //
    // Object numbering:
    //   1            = Catalog
    //   2            = Pages (parent dict)
    //   3 .. 2+P     = Page objects (P = number of pages)
    //   3+P .. 2+2P  = Content stream objects (one per page)
    //   3+2P         = Font F1 (Helvetica)
    //   4+2P         = Font F2 (Helvetica-Bold)
    //   5+2P         = Info dict
    //
    private static byte[] AssemblePdf(List<List<RLine>> pages, string title)
    {
        int p        = pages.Count;
        int catObj   = 1;
        int pagesObj = 2;
        int pageBase = 3;            // first page object
        int strmBase = pageBase + p; // first content-stream object
        int f1Obj    = strmBase + p;
        int f2Obj    = f1Obj + 1;
        int infoObj  = f2Obj + 1;
        int total    = infoObj;

        var offsets = new long[total + 1]; // 1-based
        using var ms = new MemoryStream(64 * 1024);

        // File header (the 4 high-bit bytes mark this as a binary file)
        Emit(ms, "%PDF-1.4\n%\xe2\xe3\xcf\xd3\n");

        // ── Catalog
        offsets[catObj] = ms.Position;
        EmitObj(ms, catObj, $"<< /Type /Catalog /Pages {pagesObj} 0 R >>");

        // ── Pages
        offsets[pagesObj] = ms.Position;
        var kids = string.Join(" ", Enumerable.Range(pageBase, p).Select(i => $"{i} 0 R"));
        EmitObj(ms, pagesObj, $"<< /Type /Pages /Kids [{kids}] /Count {p} >>");

        // ── Page objects
        for (int i = 0; i < p; i++)
        {
            offsets[pageBase + i] = ms.Position;
            EmitObj(ms, pageBase + i,
                $"<< /Type /Page /Parent {pagesObj} 0 R " +
                $"/MediaBox [0 0 {PageWidth:F2} {PageHeight:F2}] " +
                $"/Contents {strmBase + i} 0 R " +
                $"/Resources << /Font << " +
                $"/{FontRegular} {f1Obj} 0 R " +
                $"/{FontBold} {f2Obj} 0 R " +
                $">> >> >>");
        }

        // ── Content streams (one per page)
        for (int i = 0; i < p; i++)
        {
            offsets[strmBase + i] = ms.Position;
            EmitStream(ms, strmBase + i, BuildPageStream(pages[i]));
        }

        // ── Fonts
        offsets[f1Obj] = ms.Position;
        EmitObj(ms, f1Obj,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica " +
            "/Encoding /WinAnsiEncoding >>");

        offsets[f2Obj] = ms.Position;
        EmitObj(ms, f2Obj,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold " +
            "/Encoding /WinAnsiEncoding >>");

        // ── Info
        offsets[infoObj] = ms.Position;
        EmitObj(ms, infoObj,
            $"<< /Title ({EscapeString(title)}) /Creator (IQFlowAgent) >>");

        // ── Cross-reference table (each entry is exactly 20 bytes)
        long xrefPos = ms.Position;
        Emit(ms, $"xref\n0 {total + 1}\n");
        Emit(ms, "0000000000 65535 f\r\n");         // free-list head (obj 0)
        for (int i = 1; i <= total; i++)
            Emit(ms, $"{offsets[i]:D10} 00000 n\r\n");

        // ── Trailer
        Emit(ms, "trailer\n");
        Emit(ms, $"<< /Size {total + 1} /Root {catObj} 0 R /Info {infoObj} 0 R >>\n");
        Emit(ms, "startxref\n");
        Emit(ms, $"{xrefPos}\n");
        Emit(ms, "%%EOF\n");

        return ms.ToArray();
    }

    // Build the content stream for one page (BT...ET text commands)
    private static string BuildPageStream(List<RLine> lines)
    {
        if (lines.Count == 0) return "BT ET\n";

        var sb       = new StringBuilder(lines.Count * 70);
        var curFont  = string.Empty;
        float curSz  = -1f;

        sb.Append("BT\n");
        foreach (var ln in lines)
        {
            var font = ln.IsBold ? FontBold : FontRegular;
            // Only emit Tf when the font or size changes
            if (font != curFont || Math.Abs(ln.Sz - curSz) > 0.01f)
            {
                sb.Append($"/{font} {ln.Sz:F1} Tf\n");
                curFont = font;
                curSz   = ln.Sz;
            }
            // Use Tm (text matrix) for absolute positioning — 1 0 0 1 x y Tm
            sb.Append($"1 0 0 1 {ln.X:F2} {ln.Y:F2} Tm\n");
            sb.Append($"({EscapeString(ln.Text)}) Tj\n");
        }
        sb.Append("ET\n");

        return sb.ToString();
    }

    // Escape a string for use in a PDF literal string ( ... ).
    // Parentheses and backslashes must be escaped with a backslash.
    // Characters outside printable ASCII are normalised to their Latin-1 / WinAnsi
    // equivalents or replaced with a plain ASCII substitute.
    private static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '(':  sb.Append("\\("); break;
                case ')':  sb.Append("\\)"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\u2013': sb.Append('-');   break;  // en dash  → hyphen
                case '\u2014': sb.Append('-');   break;  // em dash  → hyphen
                case '\u2018':
                case '\u2019': sb.Append('\'');  break;  // smart single quotes
                case '\u201C':
                case '\u201D': sb.Append('"');   break;  // smart double quotes
                case '\u2022': sb.Append('-');   break;  // bullet   → hyphen
                case '\u00A0': sb.Append(' ');   break;  // non-breaking space
                default:
                    // Printable ASCII and WinAnsi extended range (0x80-0xFF)
                    if (c >= 0x20 && c < 0x100)
                        sb.Append(c);
                    else
                        sb.Append(' '); // other Unicode → space
                    break;
            }
        }
        return sb.ToString();
    }

    // ── Low-level PDF emission helpers ────────────────────────────────────────

    private static void Emit(Stream s, string text)
    {
        // Latin-1 encoding maps the string bytes 1-to-1 into PDF raw bytes.
        var bytes = Encoding.Latin1.GetBytes(text);
        s.Write(bytes);
    }

    private static void EmitObj(Stream s, int n, string dict)
    {
        Emit(s, $"{n} 0 obj\n{dict}\nendobj\n");
    }

    private static void EmitStream(Stream s, int n, string content)
    {
        // The /Length value must equal the byte count of the stream data,
        // NOT including the final newline before "endstream".
        var bytes = Encoding.Latin1.GetBytes(content);
        Emit(s, $"{n} 0 obj\n<< /Length {bytes.Length} >>\nstream\n");
        s.Write(bytes);
        Emit(s, "\nendstream\nendobj\n");
    }
}
