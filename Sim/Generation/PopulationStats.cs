namespace YouWillDate.Sim.Generation;

/// <summary>Counts for one fact key across a sample of pawns.</summary>
public sealed class FactStats
{
    public Dictionary<string, int> Counts { get; } = new();
    /// <summary>Raw values of a numeric fact.</summary>
    public List<double> Numbers { get; } = new();
    /// <summary>Pawns where the item went through its weights (not default or override).</summary>
    public int Rolled { get; internal set; }
    /// <summary>Per modifier (by position), how many of the rolled pawns it fired for.</summary>
    public int[] ModFired { get; internal set; } = [];
    public int Total { get; internal set; }

    public double Share(string value) => Total == 0 ? 0 : Counts.GetValueOrDefault(value) / (double)Total;
}

/// <summary>
/// Generates seeds 0..n-1 and tallies every fact. Using fixed seeds means two
/// runs over different data differ only because of the data change.
/// </summary>
public static class PopulationStats
{
    public static Dictionary<string, FactStats> Run(
        PawnGenerator generator, int count, Facts? overrides = null, CancellationToken cancel = default)
    {
        var stats = new Dictionary<string, FactStats>();
        var options = new GenerateOptions { Overrides = overrides, Trace = true };

        for (int i = 0; i < count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var pawn = generator.Generate((ulong)i, options);
            foreach (var t in pawn.Trace!)
            {
                if (!stats.TryGetValue(t.Key, out var s)) stats[t.Key] = s = new FactStats();
                s.Total++;
                if (t.Value is not null) s.Counts[t.Value] = s.Counts.GetValueOrDefault(t.Value) + 1;
                if (t.Number is double n) s.Numbers.Add(n);

                if (t.Mods.Count > 0 && t.Source is RollSource.Drawn or RollSource.AssortCopy)
                {
                    if (s.ModFired.Length != t.Mods.Count) s.ModFired = new int[t.Mods.Count];
                    s.Rolled++;
                    for (int m = 0; m < t.Mods.Count; m++)
                        if (t.Mods[m].Fired) s.ModFired[m]++;
                }
                else if (t.Values is not null && t.Source is RollSource.Drawn or RollSource.AssortCopy)
                {
                    s.Rolled++;
                }
            }
        }
        return stats;
    }
}
