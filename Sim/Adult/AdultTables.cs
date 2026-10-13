using YouWillDate.Sim.Data;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Adult;

public enum AdultType { Score, Count, Blend, Roll, Choice, Derive, List, Goals, Issues, Dealbreakers, InnerLife, FriendSources }
public enum ScoreScale { Z, ZRaw, Unit, Signed, Clamp01 }

/// <summary>base + Σ weights + Σ mods, read from the pawn's facts.</summary>
public sealed class Formula
{
    public static readonly Formula Zero = new() { Terms = [], Mods = [] };
    public double Base { get; init; }
    public required Term[] Terms { get; init; }
    public required AddMod[] Mods { get; init; }
}

public sealed record ListEntry(string Id, string? Note, double Prevalence, Term[] Terms, AddMod[] Mods,
                               string? Sex, Formula Intensity, double Stigma, bool Visible);
public sealed record Goal(string Id, string? Note, Formula Score, double Deadline, Term[] DeadlineTerms, Condition[] Achieved);
public sealed record Stake(string? Note, Condition[] When, double Shift, double Salience);
public sealed record Issue(string Id, string? Note, Term[] Axes, double BaseSalience, Stake[] Stakes);
public sealed record Dealbreaker(string Id, string Rule, Condition[] When, Formula Hardness);
public sealed record Belief(string? Item, string Reading, string Id, string Want, string Need);
public sealed record FriendSource(string Id, string? Note, double Weight, Formula Formula, int MetFrom, int MetTo);

public sealed record AdultItem
{
    public required string Id { get; init; }
    public string? Note { get; init; }
    public required AdultType Type { get; init; }
    public required string Group { get; init; }
    public required string GroupTitle { get; init; }
    /// <summary>Fact for single-value traits: "adult.&lt;id&gt;".</summary>
    public string Key => $"adult.{Id}";

    public Formula? Formula { get; init; }
    public double Sigma { get; init; }
    public ScoreScale Scale { get; init; }
    public int Cap { get; init; }
    public string? Stated { get; init; }

    public string[] Values { get; init; } = [];
    public TableItem? Table { get; init; }
    public string[] Inherit { get; init; } = [];
    public double InheritWeight { get; init; }
    public Choice[] Options { get; init; } = [];
    public double Temperature { get; init; } = 1;
    public DeriveRule[] Rules { get; init; } = [];

    public ListEntry[] Entries { get; init; } = [];
    public int Max { get; init; }
    public string[] Levels { get; init; } = [];
    public Formula? Disclosure { get; init; }

    public Goal[] Goals { get; init; } = [];
    public int PickMin { get; init; }
    public int PickMax { get; init; }

    public Issue[] Issues { get; init; } = [];
    public string? Consistency { get; init; }
    public double Noise { get; init; }

    public Dealbreaker[] Dealbreakers { get; init; } = [];
    public double HardAt { get; init; }

    public double WoundThreshold { get; init; }
    public double RegretThreshold { get; init; }
    public int MaxSecrets { get; init; }
    public Belief[] Beliefs { get; init; } = [];
    public IReadOnlyDictionary<string, double> Hideable { get; init; } = new Dictionary<string, double>();

    public FriendSource[] Sources { get; init; } = [];
    public string? CountKey { get; init; }
    public string? StatusKey { get; init; }
    public double StatusSd { get; init; }
    public double SameSex { get; init; }
    public double Concentration { get; init; }
    public Formula? Closeness { get; init; }

    /// <summary>Every fact this item writes, with allowed values (null for numeric).</summary>
    public IReadOnlyList<(string Key, IReadOnlyList<string>? Values)> Outputs { get; init; } = [];
}

public sealed class AdultTables
{
    public required AdultItem[] Items { get; init; }
    public required int CalibrationSize { get; init; }
    public required IReadOnlyList<(string Id, string Title, string? Note)> Groups { get; init; }
}
