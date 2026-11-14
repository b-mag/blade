using Blade.Core;
using Blade.Server;
using Blade.Server.Ai;
using Blade.Server.Answer;
using Blade.Server.Data;
using Blade.Server.Graph;
using Blade.Server.Search;

namespace Blade.Tests;

public sealed class StreamingRedactorTests
{
    [Fact]
    public void A_licence_number_split_across_chunks_never_reaches_the_screen()
    {
        var r = new StreamingRedactor();
        var shown = "";
        foreach (var chunk in new[] { "Driver lic", "ense K12", "345", "678 was ", "listed in [25-0021", "36]." })
            shown += r.Push(chunk);
        shown += r.Flush();

        Assert.DoesNotContain("K12", shown);
        Assert.Equal("Driver license [DL# REDACTED] was listed in [25-002136].", shown);
    }

    [Fact]
    public void Only_whole_words_are_released_until_the_end()
    {
        var r = new StreamingRedactor();
        Assert.Equal("", r.Push("Hel"));
        Assert.Equal("Hello ", r.Push("lo wor"));
        Assert.Equal("", r.Push("ld"));
        Assert.Equal("world", r.Flush());
    }

    [Fact]
    public void Streamed_text_matches_redacting_the_whole_answer()
    {
        const string answer = "SSN 123-45-6789 appears once.\n- **Key** DL K87654321, case [26-001925]";
        var r = new StreamingRedactor();
        var shown = string.Concat(answer.Chunk(3).Select(c => r.Push(new string(c)))) + r.Flush();
        Assert.Equal(PiiRedactor.Redact(answer).Text, shown);
    }

    [Fact]
    public void Reset_starts_a_new_answer()
    {
        var r = new StreamingRedactor();
        r.Push("first draft ");
        r.Reset();
        Assert.Equal("second ", r.Push("second "));
    }
}

public sealed class AskStreamingTests(DataGenFixture fx) : IClassFixture<DataGenFixture>
{
    /// <summary>Stands in for the model: streams a canned, cited answer in small chunks.</summary>
    private sealed class FakeLlm(string answer) : ILlmClient
    {
        public string Provider => "fake";
        public string Model => "fake";
        public bool IsLocal => true;
        public int Calls { get; private set; }

        public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new LlmResult(answer, Provider, Model, true, 100, 50, 1));
        }

        public async Task<LlmResult> StreamAsync(LlmRequest request, Func<string, Task> onDelta, CancellationToken ct = default)
        {
            Calls++;
            foreach (var c in answer.Chunk(5)) await onDelta(new string(c));
            return new LlmResult(answer, Provider, Model, true, 100, 50, 1);
        }
    }

    private static readonly SearchFilter Burglaries = new()
    {
        OffenseTypes = ["BURGLARY"], Districts = ["NORTH"], PremiseTypes = ["COMMERCIAL"], MoTags = ["FORCED_ENTRY"],
        LookbackMonths = 18, HourFrom = 22, HourTo = 6,
    };

    private AskService Service(ILlmClient llm)
    {
        var data = new BladeData(fx.DbPath);
        var graph = new EntityGraph(data);
        // Port 9 is never an Ollama server: the embedder returns null and search falls back to keyword + structured.
        var embedder = new QueryEmbedder(new HttpClient { Timeout = TimeSpan.FromSeconds(2) }, new AiOptions { OllamaUrl = "http://127.0.0.1:9" });
        return new AskService(data, llm, embedder, new IncidentSearch(data), new LinkAnalyzer(graph, data), new QueryPlanner(llm), new GraphViews(graph, data));
    }

    [Fact]
    public async Task Stream_sends_context_first_then_answer_text_that_matches_the_final_answer()
    {
        const string answer = "A possible series was found [25-002136] and [26-001925]. Contact DL K12345678 for details.";
        var llm = new FakeLlm(answer);
        var events = new List<StreamEvent<AskResponse>>();
        var outcome = await Service(llm).AskAsync(new AskRequest("burglaries", Burglaries), Roles.Analyst, default,
            e => { events.Add(e); return Task.CompletedTask; });

        var context = events[0];
        Assert.Equal(StreamEventTypes.Context, context.Type);
        Assert.NotNull(context.Response);
        Assert.NotEmpty(context.Response!.Results);
        Assert.NotEmpty(context.Response.Links);         // connections are shown before the model writes
        Assert.Equal("", context.Response.Answer);

        var deltas = events.Skip(1).ToList();
        Assert.All(deltas, e => Assert.Equal(StreamEventTypes.Delta, e.Type));
        var streamed = string.Concat(deltas.Select(e => e.Text));
        Assert.DoesNotContain("K1234", streamed);       // PII never streamed
        Assert.Equal(1, llm.Calls);
        Assert.Equal(PiiRedactor.Redact(answer).Text, streamed);

        Assert.Contains(outcome.Response.Citations, c => c is { Id: "25-002136", Verified: true });
        Assert.Contains("[DL# REDACTED]", outcome.Response.Answer);
    }

    [Fact]
    public async Task An_uncited_draft_is_reset_and_regenerated_while_streaming()
    {
        var llm = new FakeLlm("There were some burglaries with no citations at all.");
        var events = new List<StreamEvent<AskResponse>>();
        await Service(llm).AskAsync(new AskRequest("burglaries", Burglaries), Roles.Analyst, default,
            e => { events.Add(e); return Task.CompletedTask; });

        Assert.Equal(2, llm.Calls);
        Assert.Single(events, e => e.Type == StreamEventTypes.Reset);
        var reset = events.FindIndex(e => e.Type == StreamEventTypes.Reset);
        Assert.Contains(events.Skip(reset + 1), e => e.Type == StreamEventTypes.Delta);
    }

    [Fact]
    public async Task Without_a_stream_handler_the_answer_is_not_streamed()
    {
        var llm = new FakeLlm("Series [25-002136] and [26-001925].");
        var outcome = await Service(llm).AskAsync(new AskRequest("burglaries", Burglaries), Roles.Analyst);
        Assert.Equal(1, llm.Calls);
        Assert.Contains(outcome.Response.Citations, c => c.Verified);
    }
}
