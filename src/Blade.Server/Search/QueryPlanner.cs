using System.Text.Json;
using System.Text.Json.Nodes;
using Blade.Core;
using Blade.Server.Ai;

namespace Blade.Server.Search;

public sealed record PlanResult(SearchFilter Filter, string RawOutput, LlmResult? Llm, List<string> Warnings);

/// <summary>
/// Natural language → structured filter. The model is constrained by a JSON schema whose enums come from
/// <see cref="Vocabulary"/>, so it can only emit values that exist in the data. Lessons from benchmarking an 8B model:
/// it returns relative periods (code computes dates), list lengths are capped, and output is validated and de-duplicated.
/// </summary>
public sealed class QueryPlanner(ILlmClient llm)
{
    public static readonly string SystemPrompt = """
        You convert law-enforcement search questions into a JSON search filter for a records system.
        Use only the allowed enum values. Leave arrays empty when the question does not mention that dimension.
        Rules:
        - lookback_months: months back for relative periods ("last 18 months" = 18, "past year" = 12, "past 2 years" = 24). 0 if none.
        - since_month: calendar month 1-12 for "since <month>"; "this year" = 1. 0 if none.
        - hour_from / hour_to: 24-hour integers. "after 10pm" = 22 and 6 (overnight). "overnight" = 22 and 6. "before 6am" = 0 and 6.
          "during the day" = 7 and 18. Use -1 for both when no time of day is mentioned.
        - Synonyms: "car break-in", "vehicle burglary", "smash and grab" = THEFT_FROM_VEHICLE. "break-in" of a building = BURGLARY.
          "shoplifting" = THEFT. "stolen car" = AUTO_THEFT. "domestic", "DV", "family violence" = DOMESTIC. "drugs" = NARCOTICS.
          "business", "store", "commercial building" = COMMERCIAL. "home", "house", "residence" = RESIDENTIAL.
          "kicked in", "forced entry" = FORCED_ENTRY. "pried", "pry bar", "crowbar" = PRY_TOOL. "back door" = REAR_ENTRY.
          "catalytic converter" = CATALYTIC_CONVERTER. "knife" = WEAPON_KNIFE. "gun", "handgun", "firearm" = WEAPON_FIREARM.
        - semantic_query: 3-10 words describing the method of operation and key details, for similarity search.
        Example: "residential burglaries in west last 6 months between 1am and 5am with a pry bar" ->
        {"offense_types":["BURGLARY"],"districts":["WEST"],"lookback_months":6,"since_month":0,"hour_from":1,"hour_to":5,"premise_types":["RESIDENTIAL"],"mo_tags":["PRY_TOOL","FORCED_ENTRY"],"semantic_query":"pry bar forced entry home overnight"}
        """;

    public static JsonObject Schema()
    {
        static JsonObject EnumArray(string[] values) => new()
        {
            ["type"] = "array",
            ["maxItems"] = Math.Min(4, values.Length),
            ["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) },
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["offense_types"] = EnumArray(Vocabulary.OffenseTypes),
                ["districts"] = EnumArray(Vocabulary.Districts),
                ["lookback_months"] = new JsonObject { ["type"] = "integer" },
                ["since_month"] = new JsonObject { ["type"] = "integer" },
                ["hour_from"] = new JsonObject { ["type"] = "integer" },
                ["hour_to"] = new JsonObject { ["type"] = "integer" },
                ["premise_types"] = EnumArray(Vocabulary.PremiseTypes),
                ["mo_tags"] = EnumArray(Vocabulary.MoTags),
                ["semantic_query"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("offense_types", "districts", "lookback_months", "since_month", "hour_from", "hour_to", "premise_types", "mo_tags", "semantic_query"),
        };
    }

    public async Task<PlanResult> PlanAsync(string question, DateOnly today, CancellationToken ct = default)
    {
        var res = await llm.CompleteAsync(new LlmRequest(SystemPrompt, question, Schema(), Temperature: 0, MaxTokens: 300), ct);
        var warnings = new List<string>();
        SearchFilter raw;
        try
        {
            raw = Parse(res.Text);
        }
        catch (Exception)
        {
            warnings.Add("The query planner returned invalid output; falling back to keyword search across all incidents.");
            raw = new SearchFilter { SemanticQuery = question };
        }
        return new PlanResult(Normalize(raw, today), res.Text, res, warnings);
    }

    public static SearchFilter Parse(string json)
    {
        var n = JsonNode.Parse(json)!.AsObject();
        List<string> Arr(string k) => n[k] is JsonArray a ? a.Select(x => x?.GetValue<string>() ?? "").ToList() : [];
        int Int(string k, int d) => n[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : d;
        return new SearchFilter
        {
            OffenseTypes = Arr("offense_types"),
            Districts = Arr("districts"),
            PremiseTypes = Arr("premise_types"),
            MoTags = Arr("mo_tags"),
            LookbackMonths = Int("lookback_months", 0),
            SinceMonth = Int("since_month", 0),
            HourFrom = Int("hour_from", -1),
            HourTo = Int("hour_to", -1),
            SemanticQuery = n["semantic_query"]?.GetValue<string>() ?? "",
        };
    }

    /// <summary>Validates against the vocabulary, removes duplicates, clamps values and computes the date range.</summary>
    public static SearchFilter Normalize(SearchFilter f, DateOnly today)
    {
        static List<string> Clean(IEnumerable<string> v, string[] allowed) =>
            v.Select(x => x.Trim().ToUpperInvariant()).Where(allowed.Contains).Distinct().ToList();

        var lookback = Math.Clamp(f.LookbackMonths, 0, 120);
        var since = Math.Clamp(f.SinceMonth, 0, 12);
        DateOnly? from = lookback > 0 ? today.AddMonths(-lookback)
            : since > 0 ? new DateOnly(since <= today.Month ? today.Year : today.Year - 1, since, 1)
            : null;

        var hf = f.HourFrom is >= 0 and <= 23 ? f.HourFrom : -1;
        var ht = f.HourTo is >= 0 and <= 24 ? f.HourTo % 24 : -1;
        if (hf < 0 || ht < 0 || hf == ht) { hf = -1; ht = -1; }

        return f with
        {
            OffenseTypes = Clean(f.OffenseTypes, Vocabulary.OffenseTypes),
            Districts = Clean(f.Districts, Vocabulary.Districts),
            PremiseTypes = Clean(f.PremiseTypes, Vocabulary.PremiseTypes),
            MoTags = Clean(f.MoTags, Vocabulary.MoTags),
            LookbackMonths = lookback,
            SinceMonth = since,
            HourFrom = hf,
            HourTo = ht,
            SemanticQuery = f.SemanticQuery.Trim(),
            DateFrom = f.DateFrom ?? from,
            DateTo = f.DateTo ?? today,
        };
    }

    public static List<FilterChip> Chips(SearchFilter f)
    {
        static string Pretty(string s) => string.Join(' ', s.Split('_').Select(w => w[..1] + w[1..].ToLowerInvariant()));
        var chips = new List<FilterChip>();
        if (f.OffenseTypes.Count > 0) chips.Add(new("Offense", string.Join(", ", f.OffenseTypes.Select(Pretty))));
        if (f.Districts.Count > 0) chips.Add(new("District", string.Join(", ", f.Districts.Select(Pretty))));
        if (f.PremiseTypes.Count > 0) chips.Add(new("Premises", string.Join(", ", f.PremiseTypes.Select(Pretty))));
        if (f.MoTags.Count > 0) chips.Add(new("M.O.", string.Join(", ", f.MoTags.Select(Pretty))));
        chips.Add(new("Dates", $"{(f.DateFrom is { } d ? d.ToString("yyyy-MM-dd") : "all records")} → {f.DateTo:yyyy-MM-dd}"));
        if (f.HourFrom >= 0) chips.Add(new("Time of day", $"{f.HourFrom:00}:00 – {f.HourTo:00}:00"));
        if (f.SemanticQuery.Length > 0) chips.Add(new("Similar to", f.SemanticQuery));
        return chips;
    }
}
