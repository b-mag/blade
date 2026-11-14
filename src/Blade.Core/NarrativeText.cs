using System.Text.RegularExpressions;

namespace Blade.Core;

public static partial class NarrativeText
{
    private static readonly string[] Boilerplate =
    [
        "body-worn camera", "Case forwarded to", "forwarded to CID", "Case remains open", "No further leads at this time",
        "no workable leads", "Case suspended", "booked without incident", "transported and booked", "Scene photographs were taken",
        "I photographed the", "no crime occurred", "declined prosecution",
    ];

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z])")] private static partial Regex SentenceRx();

    /// <summary>
    /// Drops the dispatch opening sentence and administrative boilerplate every report shares, so embeddings and
    /// prompts focus on M.O., property, vehicles and witness details instead of report formatting.
    /// </summary>
    public static string Content(string narrative)
    {
        var kept = SentenceRx().Split(narrative)
            .Where((s, idx) => !(idx == 0 && s.StartsWith("On ")))
            .Where(s => !Boilerplate.Any(b => s.Contains(b, StringComparison.OrdinalIgnoreCase)));
        return string.Join(' ', kept);
    }

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..s.LastIndexOf(' ', max)] + " …";
}
