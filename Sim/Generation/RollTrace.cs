namespace YouWillDate.Sim.Generation;

public enum RollSource
{
    /// <summary>Random draw (seed step, or a weighted table roll).</summary>
    Drawn,
    /// <summary>Read from a data table without randomness (context step).</summary>
    Lookup,
    /// <summary>The item's only_if failed, so it took its default.</summary>
    Default,
    /// <summary>The father copied the mother's value (assort).</summary>
    AssortCopy,
    /// <summary>Computed from other facts.</summary>
    Derived,
    /// <summary>Set by the caller; the roll was skipped.</summary>
    Override,
}

/// <summary>How one fact got its value. Collected when <see cref="GenerateOptions.Trace"/> is on.</summary>
public sealed class RollTrace
{
    public required string Key { get; init; }
    public required RollSource Source { get; init; }
    public string? Value { get; init; }
    public double? Number { get; init; }
    public string? Note { get; init; }

    // Table rolls only. Weights are filled in even when the value was overridden,
    // so you can see what the roll would have been.
    public IReadOnlyList<string>? Values { get; init; }
    public IReadOnlyList<double>? BaseWeights { get; init; }
    public IReadOnlyList<double>? FinalWeights { get; init; }
    public string? BaseFrom { get; init; }
    public IReadOnlyList<ModTrace> Mods { get; init; } = [];
    /// <summary>only_if conditions that failed (empty when the item rolled normally).</summary>
    public IReadOnlyList<string> FailedOnlyIf { get; init; } = [];

    public string ValueText => Value ?? Number?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "";
}

/// <summary>One modifier of a table item and whether all its conditions held.</summary>
public sealed record ModTrace(int Number, string When, string Mul, bool Fired);

public sealed class GenerateOptions
{
    /// <summary>Values to force instead of rolling. Everything downstream reacts to them.</summary>
    public Facts? Overrides { get; init; }
    /// <summary>Record a <see cref="RollTrace"/> for every fact (slower; for debugging).</summary>
    public bool Trace { get; init; }
    /// <summary>Write a step-by-step explanation into each life history entry (slower; for the inspector).</summary>
    public bool LifeDetails { get; init; }
    /// <summary>Stop after the life simulation (used to calibrate adult traits).</summary>
    public bool SkipAdult { get; init; }
}
