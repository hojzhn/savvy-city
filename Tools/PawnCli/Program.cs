// Pawn generator CLI, for checking and tuning the data tables.
//
//   PawnCli 42                         print the pawn for seed 42
//   PawnCli 42 5                       print seeds 42..46
//   PawnCli 42 --why household.wealth  explain how a value was rolled (repeatable)
//   PawnCli 42 --set mother.religion=muslim --set pawn.threat_sensitivity=0.9
//                                      force values; everything downstream reacts
//   PawnCli --stats 10000 household.shape [more keys]
//                                      value shares across 10000 pawns (works with --set)
//
// Run with: dotnet run --project Tools/PawnCli -- <args>

using System.Globalization;
using System.Text;
using YouWillDate.Sim;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

Console.OutputEncoding = Encoding.UTF8;

GameData data;
try { data = GameData.Load(FindDataDir()); }
catch (DataException e)
{
    Console.Error.WriteLine($"Data error: {e.Message}");
    return 1;
}
var gen = new PawnGenerator(data);

var positional = new List<string>();
var why = new List<string>();
var overrides = new Facts();
bool stats = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--stats": stats = true; break;
        case "--why" when i + 1 < args.Length: why.Add(args[++i]); break;
        case "--set" when i + 1 < args.Length:
            var parts = args[++i].Split('=', 2);
            if (parts.Length != 2) return Fail($"--set needs key=value, got '{args[i]}'");
            try
            {
                if (data.AllowedValues(parts[0]) is null)
                    overrides.Set(parts[0], double.Parse(parts[1], CultureInfo.InvariantCulture));
                else
                    overrides.Set(parts[0], parts[1]);
            }
            catch (Exception e) when (e is KeyNotFoundException or FormatException) { return Fail(e.Message); }
            break;
        default: positional.Add(args[i]); break;
    }
}
var options = new GenerateOptions { Overrides = overrides, Trace = why.Count > 0 };

try
{
    if (stats) RunStats();
    else RunPawns();
}
catch (ArgumentException e) { return Fail(e.Message); }
return 0;

void RunStats()
{
    int n = positional.Count > 0 ? int.Parse(positional[0]) : 10000;
    var keys = positional.Skip(1).ToArray();
    if (keys.Length == 0) keys = ["household.shape", "household.wealth", "mother.religion"];

    var counts = keys.ToDictionary(k => k, _ => new Dictionary<string, int>());
    for (ulong s = 0; s < (ulong)n; s++)
    {
        var p = gen.Generate(s, options);
        foreach (var k in keys)
        {
            string v = p.Facts.Cat(k) ?? "(missing)";
            counts[k][v] = counts[k].GetValueOrDefault(v) + 1;
        }
    }
    foreach (var (k, c) in counts)
    {
        Console.WriteLine(k);
        foreach (var (v, hits) in c.OrderByDescending(kv => kv.Value))
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {v,-28} {100.0 * hits / n,6:0.0}%"));
    }
}

void RunPawns()
{
    ulong seed = positional.Count >= 1 ? ulong.Parse(positional[0]) : (ulong)Random.Shared.NextInt64();
    int count = positional.Count >= 2 ? int.Parse(positional[1]) : 1;
    for (int i = 0; i < count; i++)
    {
        var p = gen.Generate(seed + (ulong)i, options);
        Console.WriteLine(PawnText.Describe(p));
        foreach (var key in why)
        {
            var t = p.TraceFor(key);
            Console.WriteLine(t is null ? $"No trace for '{key}'." : TraceText.Describe(t));
        }
        Console.WriteLine(new string('-', 60));
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static string FindDataDir()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "Data");
        if (File.Exists(Path.Combine(candidate, "config.yaml"))) return candidate;
    }
    throw new DirectoryNotFoundException("Could not find the Data folder above " + AppContext.BaseDirectory);
}
