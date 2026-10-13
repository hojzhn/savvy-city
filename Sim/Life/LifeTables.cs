using YouWillDate.Sim.Data;

namespace YouWillDate.Sim.Life;

public enum LifeItemType { Condition, Event, Fork }
public enum ConditionKind { State, Number, Roll, Drift, Derive, Counter }
public enum Frequency { Once, PerStage, PerYear }

public sealed record Stage(string Id, int From, int To, double Weight);

/// <summary>
/// One weighted input to a rate or utility. Numeric facts add weight × x, where
/// temperament percentiles are centred to -1..1 and other numbers are used as-is.
/// Categorical facts add the weight listed for their current value.
/// </summary>
public sealed class Term
{
    public required string Key { get; init; }
    public double Weight { get; init; }
    public bool Centered { get; init; }
    public IReadOnlyDictionary<string, double>? ValueWeights { get; init; }
    public bool IsNumeric => ValueWeights is null;
}

public sealed class AddMod(int number, Condition[] when, double add, string? note)
{
    public int Number { get; } = number;
    public Condition[] When { get; } = when;
    public double Add { get; } = add;
    public string? Note { get; } = note;
}

public sealed class Rate
{
    public double? BaseValue { get; init; }
    public string? BaseKey { get; init; }
    public required Term[] Terms { get; init; }
    public required AddMod[] Mods { get; init; }
}

public sealed class Effects
{
    public static readonly Effects None = new();
    public (string Key, string Value)[] Set { get; init; } = [];
    public (string Key, double Amount)[] Add { get; init; } = [];
    public string[] Reset { get; init; } = [];
    public string[] RecordFirstAge { get; init; } = [];
    public (string Channel, double Delta)[] Channels { get; init; } = [];
    public bool StartInvolvement { get; init; }
    public bool EndInvolvement { get; init; }
}

public sealed record InvolvementField(string Id, string[] Values, string Initial);

/// <summary>Compiled involvement section: per-involvement fields and the rules that derive state.relationship.</summary>
public sealed class InvolvementTables
{
    public required InvolvementField[] Fields { get; init; }
    public required string[] LabelValues { get; init; }
    public required DeriveRule[] LabelRules { get; init; }
}

/// <summary>Fork option, event outcome, or reading.</summary>
public sealed class Choice
{
    public required string Id { get; init; }
    public string? Note { get; init; }
    public double Weight { get; init; } = 1;
    public required Term[] Terms { get; init; }
    public required AddMod[] Mods { get; init; }
    public required Condition[] Requires { get; init; }
    public required Effects Effects { get; init; }
}

public sealed record DeriveRule(Condition[] When, string Value);

public sealed class LifeItem
{
    public required string Id { get; init; }
    public string? Note { get; init; }
    public required LifeItemType Type { get; init; }
    public ConditionKind Kind { get; init; }
    /// <summary>Fact a condition writes: "state.&lt;id&gt;".</summary>
    public string Key => $"state.{Id}";
    public string[] Values { get; init; } = [];
    public bool IsNumeric => Kind is ConditionKind.Number or ConditionKind.Counter;

    // Schedule
    public int WindowFrom { get; init; }
    public int WindowTo { get; init; }
    public int[]? Ages { get; init; }
    public Frequency Frequency { get; init; }
    public int Max { get; init; }
    public bool MustHappen { get; init; }
    public bool PerInvolvement { get; init; }
    public Condition[] Requires { get; init; } = [];

    // Conditions
    public string? Initial { get; init; }
    public string? InitialValue { get; init; }
    public DeriveRule[] InitialRules { get; init; } = [];
    public TableItem? Table { get; init; }
    public Rate? Up { get; init; }
    public Rate? Down { get; init; }
    public DeriveRule[] Rules { get; init; } = [];
    public Condition[] While { get; init; } = [];
    public bool Log { get; init; }

    // Events
    public Rate? Rate { get; init; }
    public Choice[] Outcomes { get; init; } = [];
    public Effects Effects { get; init; } = Effects.None;
    public Choice[] Readings { get; init; } = [];
    public bool Negative { get; init; }
    public string[] Tags { get; init; } = [];

    // Forks
    public Choice[] Options { get; init; } = [];
    public double Temperature { get; init; } = 1;
    public string? StickyKey { get; init; }
    public double StickyBonus { get; init; }
    public string[] StickyExcept { get; init; } = [];

    /// <summary>Values of the "life.&lt;id&gt;" summary fact for events and forks.</summary>
    public string[] SummaryValues { get; init; } = [];
}

public sealed class LifeTables
{
    public required Stage[] Stages { get; init; }
    public required string[] Channels { get; init; }
    public required LifeItem[] Items { get; init; }
    public double ReadingTemperature { get; init; } = 1;
    /// <summary>Null when life.yaml has no involvement section.</summary>
    public InvolvementTables? Involvement { get; init; }

    public Stage StageAt(int age) => Stages.LastOrDefault(s => s.From <= age) ?? Stages[0];

    public static readonly string[] EventSummaryValues = ["never", "once", "twice", "3_plus"];
}
