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
public sealed record ReadinessStep(int Number, string Title, string Existing, string Ongoing);
public sealed record ReadinessSource(string Source, string Unlocks, string Readiness, string Wave);
public sealed record ReadinessPhase(string Title, string Duration, string Detail);
public sealed record ReadinessEffort(string SourceType, string Time, string MainWork, string Skills);
public sealed record FaqItem(string Question, string Answer);
public sealed record StorageOption(string Aspect, string Shared, string PerSource);
public sealed record SqlServerPattern(string Intro, List<string> Principles, string Ddl, List<StorageOption> Options, string VersionNote);
public sealed record ModelRating(string Dimension, int Score, string Note);
public sealed record EmbeddingModelInfo(string Name, string Tag, string Facts, string Why, List<ModelRating> Ratings);
public sealed record EmbeddingGuide(string Intro, List<EmbeddingModelInfo> Models, string Shortlist, List<string> Notes);
public sealed record DataReadinessContent(
    string Intro, string ShortAnswer, List<ReadinessStep> Steps, string PrioritizationIntro, List<ReadinessSource> Sources,
    List<ReadinessPhase> Phases, List<ReadinessEffort> Effort, string EffortNote, List<string> ReadyChecks,
    SqlServerPattern SqlServer, EmbeddingGuide EmbeddingModels, List<FaqItem> Faq);
public sealed record AppContent(HomeContent Home, ArchitectureContent Architecture, DataReadinessContent DataReadiness, List<Opportunity> Opportunities, RoiSummary RoiSummary);

public static class ContentLoader
{
    public static AppContent Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "content.json");
        return JsonSerializer.Deserialize<AppContent>(File.ReadAllText(path), ApiClient.Json)
               ?? throw new InvalidDataException("content.json is empty");
    }
}
