using System.Text;
using System.Text.RegularExpressions;

namespace Blade.DataGen.Policies;

public sealed record PolicyDoc(string Number, string Title, string? Effective, string? Revised, string FileName, List<PolicySection> Sections);

public sealed record PolicySection(string PolicyNumber, string SectionNumber, string Heading, int Level, string? ParentSection, int Ordinal, string Text);

/// <summary>
/// Splits the markdown policy manual into citable sections (## and ###). Each section is one retrieval chunk,
/// so every answer can cite an exact section number such as "Policy 300.3.6".
/// </summary>
public static partial class PolicyParser
{
    [GeneratedRegex(@"^#\s+Policy\s+(\d+)\s+[—-]\s+(.+)$")] private static partial Regex TitleRx();
    [GeneratedRegex(@"^(#{2,3})\s+(\d+(?:\.\d+)+)\s+(.+)$")] private static partial Regex SectionRx();
    [GeneratedRegex(@"Effective\s+(\d{4}-\d{2}-\d{2})")] private static partial Regex EffectiveRx();
    [GeneratedRegex(@"Revised\s+(\d{4}-\d{2}-\d{2})")] private static partial Regex RevisedRx();

    public static List<PolicyDoc> LoadDirectory(string dir) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.md").Select(Parse).OrderBy(p => int.Parse(p.Number)).ToList()
            : [];

    public static PolicyDoc Parse(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        string number = "", title = "";
        string? effective = null, revised = null;
        var sections = new List<PolicySection>();
        string? curNum = null, curHeading = null, parentL2 = null;
        var curLevel = 0;
        var body = new StringBuilder();

        void Flush()
        {
            if (curNum is null) return;
            var text = body.ToString().Trim();
            if (text.Length > 0)
                sections.Add(new PolicySection(number, curNum, curHeading!, curLevel, curLevel == 3 ? parentL2 : null, sections.Count + 1, text));
            body.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (TitleRx().Match(line) is { Success: true } t) { number = t.Groups[1].Value; title = t.Groups[2].Value.Trim(); continue; }
            if (line.StartsWith('>'))
            {
                effective ??= EffectiveRx().Match(line) is { Success: true } e ? e.Groups[1].Value : null;
                revised ??= RevisedRx().Match(line) is { Success: true } r ? r.Groups[1].Value : null;
                continue;
            }
            if (SectionRx().Match(line) is { Success: true } s)
            {
                Flush();
                curLevel = s.Groups[1].Value.Length;
                curNum = s.Groups[2].Value;
                curHeading = s.Groups[3].Value.Trim();
                if (curLevel == 2) parentL2 = curNum;
                continue;
            }
            if (curNum is not null) body.AppendLine(line);
        }
        Flush();
        if (number.Length == 0) throw new InvalidDataException($"Policy title line not found in {Path.GetFileName(path)}");
        return new PolicyDoc(number, title, effective, revised, Path.GetFileName(path), sections);
    }
}
