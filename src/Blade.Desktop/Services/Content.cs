using System.IO;
using System.Text.Json;

namespace Blade.Desktop.Services;

public sealed record ContentItem(string Title, string Detail, string? InPoc);
public sealed record ContentSection(string Heading, List<ContentItem> Items);
public sealed record HomeContent(string Title, string Intro, string PocStatement, List<ContentSection> Sections);
public sealed record SizingTier(string Tier, string Hardware, string Models, string Users, string Notes);
public sealed record ArchitectureContent(string Intro, List<ContentSection> Sections, List<SizingTier> Sizing);
public sealed record Opportunity(
    string Id, string Title, string Tagline, string Problem, List<string> AiApproach, List<string> LocalStack, List<string> DataNeeded,
    List<string> Integration, List<string> Roi, List<string> Risks, string Effort, string EffortDetail, int RoiRank);
public sealed record RoiSummary(string Intro, string Notes);
public sealed record AppContent(HomeContent Home, ArchitectureContent Architecture, List<Opportunity> Opportunities, RoiSummary RoiSummary);

public static class ContentLoader
{
    public static AppContent Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "content.json");
        return JsonSerializer.Deserialize<AppContent>(File.ReadAllText(path), ApiClient.Json)
               ?? throw new InvalidDataException("content.json is empty");
    }
}
