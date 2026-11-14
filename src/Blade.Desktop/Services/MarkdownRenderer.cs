using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Blade.Desktop.Services;

/// <summary>
/// Minimal markdown → FlowDocument renderer for model answers (paragraphs, bullets, headings, bold/italic/code).
/// Record identifiers and policy sections become clickable links; unverified citations are shown in red.
/// </summary>
public static partial class MarkdownRenderer
{
    public static event Action<string, string>? CitationClicked; // (kind, id)

    [GeneratedRegex(@"\*\*(?<b>.+?)\*\*|(?<![\*\w])\*(?<i>[^*\n]+?)\*(?!\*)|`(?<c>[^`]+)`|Policy\s+(?<pol>\d{3,4}\.\d{1,2}(?:\.\d{1,2})?)(?<pu>\s*⚠UNVERIFIED)?|(?<id>\b\d{2}-\d{6}\b|\bFI-\d{2}-\d{5}\b|\bB\d{2}-\d{6}\b|\b(?:PT|SC)-[A-Z]+-\d{5}\b)(?<u>\s*⚠UNVERIFIED)?")]
    private static partial Regex InlineRx();

    [GeneratedRegex(@"^(?<indent>\s*)(?:[-*•]|\d+\.)\s+(?<text>.*)$")] private static partial Regex BulletRx();
    [GeneratedRegex(@"^#{1,4}\s+(?<text>.*)$")] private static partial Regex HeadingRx();

    /// <param name="interactive">False while an answer is still streaming: identifiers are highlighted but not yet clickable,
    /// because the citation guard has not checked them.</param>
    public static FlowDocument Render(string markdown, Brush foreground, Brush accent, Brush danger, bool interactive = true)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = foreground,
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            LineHeight = 21,
        };
        List? list = null;
        foreach (var raw in (markdown ?? "").Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0) { list = null; continue; }

            if (BulletRx().Match(line) is { Success: true } b)
            {
                if (list is null)
                {
                    list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(b.Groups["indent"].Length > 1 ? 34 : 18, 0, 0, 6), Padding = new Thickness(0) };
                    doc.Blocks.Add(list);
                }
                var p = new Paragraph { Margin = new Thickness(0, 1, 0, 3) };
                AddInlines(p.Inlines, b.Groups["text"].Value, accent, danger, interactive);
                list.ListItems.Add(new ListItem(p));
                continue;
            }

            list = null;
            if (HeadingRx().Match(line) is { Success: true } h)
            {
                var hp = new Paragraph { FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 8, 0, 4) };
                AddInlines(hp.Inlines, h.Groups["text"].Value, accent, danger, interactive);
                doc.Blocks.Add(hp);
                continue;
            }
            var para = new Paragraph { Margin = new Thickness(0, line.TrimStart().StartsWith("**") ? 8 : 0, 0, 6) };
            AddInlines(para.Inlines, line.Trim(), accent, danger, interactive);
            doc.Blocks.Add(para);
        }
        return doc;
    }

    private static void AddInlines(InlineCollection target, string text, Brush accent, Brush danger, bool interactive)
    {
        var pos = 0;
        foreach (Match m in InlineRx().Matches(text))
        {
            if (m.Index > pos) target.Add(new Run(text[pos..m.Index]));
            if (m.Groups["b"].Success)
            {
                var bold = new Bold();
                AddInlines(bold.Inlines, m.Groups["b"].Value, accent, danger, interactive);
                target.Add(bold);
            }
            else if (m.Groups["i"].Success)
            {
                var it = new Italic();
                AddInlines(it.Inlines, m.Groups["i"].Value, accent, danger, interactive);
                target.Add(it);
            }
            else if (m.Groups["c"].Success)
                target.Add(new Run(m.Groups["c"].Value) { FontFamily = new FontFamily("Consolas") });
            else if (m.Groups["pol"].Success)
                target.Add(Link("POLICY", m.Groups["pol"].Value, $"Policy {m.Groups["pol"].Value}", m.Groups["pu"].Success, accent, danger, interactive));
            else if (m.Groups["id"].Success)
                target.Add(Link(KindOf(m.Groups["id"].Value), m.Groups["id"].Value, m.Groups["id"].Value, m.Groups["u"].Success, accent, danger, interactive));
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) target.Add(new Run(text[pos..]));
    }

    private static string KindOf(string id) =>
        id.StartsWith("FI-") ? "FIELD_INTERVIEW" : id.StartsWith('B') ? "BOOKING" : id.StartsWith("PT-") || id.StartsWith("SC-") ? "PAWN" : "CASE";

    private static Inline Link(string kind, string id, string text, bool unverified, Brush accent, Brush danger, bool interactive)
    {
        if (!interactive)
            return new Run(text) { Foreground = accent, FontWeight = FontWeights.SemiBold, ToolTip = "Citation checks run when the answer is complete" };
        if (unverified)
            return new Run($"{text} ⚠ unverified") { Foreground = danger, FontWeight = FontWeights.SemiBold, ToolTip = "The model cited an identifier it was not given. Treat as unreliable." };
        var link = new Hyperlink(new Run(text)) { Foreground = accent, TextDecorations = null, Cursor = System.Windows.Input.Cursors.Hand, FontWeight = FontWeights.SemiBold };
        link.ToolTip = kind switch { "CASE" => "Open report", "POLICY" => "Show policy section", _ => "Show in Link Explorer" };
        link.Click += (_, _) => CitationClicked?.Invoke(kind, id);
        return link;
    }
}
