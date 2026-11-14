using Blade.Core;
using Blade.DataGen;

// Usage: dotnet run --project src/Blade.DataGen -- [--out data/blade.db] [--seed 42] [--incidents 1450] [--no-embed]
//        [--policies data/policies] [--ollama http://localhost:11434] [--embed-model nomic-embed-text]

var root = FindRepoRoot();
string Arg(string name, string fallback)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : fallback;
}

var options = new GeneratorOptions
{
    OutputPath = Path.GetFullPath(Arg("--out", Path.Combine(root, "data", "blade.db"))),
    PolicyDirectory = Path.GetFullPath(Arg("--policies", Path.Combine(root, "data", "policies"))),
    Seed = int.Parse(Arg("--seed", "42")),
    BaselineIncidents = int.Parse(Arg("--incidents", "1450")),
    Embed = !args.Contains("--no-embed"),
    OllamaUrl = Arg("--ollama", "http://localhost:11434"),
    EmbedModel = Arg("--embed-model", "nomic-embed-text"),
    Log = Console.WriteLine,
};

Console.WriteLine($"{Agency.ProductName} data generator — {Agency.Name}");
Console.WriteLine($"*** {Agency.SyntheticDataBanner} ***");
var result = await DataGenerator.RunAsync(options);

Console.WriteLine();
Console.WriteLine($"Wrote {options.OutputPath} in {result.Elapsed.TotalSeconds:F1}s");
foreach (var (table, count) in result.TableCounts) Console.WriteLine($"  {table,-22} {count,7:N0}");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Blade.slnx"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
