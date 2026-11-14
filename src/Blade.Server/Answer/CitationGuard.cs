using System.Text.RegularExpressions;
using Blade.Core;

namespace Blade.Server.Answer;

public sealed record GuardResult(string Text, List<Citation> Citations, List<string> Unverified);

/// <summary>
/// Every identifier the model writes is checked against the set of records it was actually given.
/// Anything else is flagged in the answer ("the AI cannot cite a record it was not shown").
/// </summary>
public static partial class CitationGuard
{
    [GeneratedRegex(@"\b\d{2}-\d{6}\b|\bFI-\d{2}-\d{5}\b|\bB\d{2}-\d{6}\b|\b(?:PT|SC)-[A-Z]+-\d{5}\b")]
    public static partial Regex RecordIdRx();

    [GeneratedRegex(@"(?<=Policy\s+|§\s*|\[)(\d{3,4}\.\d{1,2}(?:\.\d{1,2})?)\b")]
    public static partial Regex PolicySectionRx();

    public static string KindOf(string id) => id switch
    {
        _ when id.StartsWith("FI-") => "FIELD_INTERVIEW",
        _ when id.StartsWith('B') => "BOOKING",
        _ when id.StartsWith("PT-") || id.StartsWith("SC-") => "PAWN",
        _ when id.Contains('.') => "POLICY",
        _ => "CASE",
    };

    public static HashSet<string> ExtractRecordIds(string text) => RecordIdRx().Matches(text).Select(m => m.Value).ToHashSet();

    /// <summary>Wraps record identifiers in square brackets (for prompts), leaving already-bracketed ones alone.</summary>
    public static string Bracket(string text) => RecordIdRx().Replace(text, m =>
        m.Index > 0 && text[m.Index - 1] == '[' ? m.Value : $"[{m.Value}]");

    public static GuardResult Check(string answer, IReadOnlySet<string> allowedRecords, IReadOnlySet<string>? allowedPolicySections = null)
    {
        var citations = new Dictionary<string, Citation>();
        var unverified = new List<string>();

        var text = RecordIdRx().Replace(answer, m =>
        {
            var ok = allowedRecords.Contains(m.Value);
            citations.TryAdd(m.Value, new Citation(m.Value, KindOf(m.Value), ok));
            if (ok) return m.Value;
            unverified.Add(m.Value);
            return $"{m.Value} ⚠UNVERIFIED";
        });

        if (allowedPolicySections is not null)
            text = PolicySectionRx().Replace(text, m =>
            {
                var ok = allowedPolicySections.Contains(m.Value);
                citations.TryAdd(m.Value, new Citation(m.Value, "POLICY", ok));
                if (ok) return m.Value;
                unverified.Add(m.Value);
                return $"{m.Value} ⚠UNVERIFIED";
            });

        return new GuardResult(text, citations.Values.ToList(), unverified.Distinct().ToList());
    }
}

/// <summary>
/// Output/log PII filter. The POC demonstrates the hook with driver's-license and SSN patterns; production would use
/// a configurable policy (juvenile identities, victim information under state law, medical details, CI identities).
/// </summary>
public static partial class PiiRedactor
{
    [GeneratedRegex(@"\bK\d{8}\b")] private static partial Regex DlRx();
    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b")] private static partial Regex SsnRx();

    public static (string Text, int Redactions) Redact(string text)
    {
        var n = 0;
        text = DlRx().Replace(text, _ => { n++; return "[DL# REDACTED]"; });
        text = SsnRx().Replace(text, _ => { n++; return "[SSN REDACTED]"; });
        return (text, n);
    }
}
