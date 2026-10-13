using System.ComponentModel;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Adult;

// Shapes of Data/adult.yaml (spec section 6 and "Social circle and health").

public sealed class AdultFile
{
    /// <summary>Pawns generated to measure each z-scale trait's raw mean and SD (spec 6.1).</summary>
    [DefaultValue(600)]
    public int CalibrationSize { get; set; } = 600;
    public List<AdultGroupDto> Groups { get; set; } = new();
}

public sealed class AdultGroupDto
{
    public string Id { get; set; } = "";
    public string? Title { get; set; }
    public string? Note { get; set; }
    public List<AdultItemDto> Items { get; set; } = new();
}

/// <summary>
/// One adult trait. The type decides which fields apply:
/// score, count, blend, roll, choice, derive, list, goals, issues, dealbreakers, inner_life.
/// </summary>
public sealed class AdultItemDto
{
    public string Id { get; set; } = "";
    public string? Note { get; set; }
    public string Type { get; set; } = "";

    // score / count / blend: value from base + weights + mods + noise
    public double Base { get; set; }
    /// <summary>fact: number, or fact: { value: number }.</summary>
    public Dictionary<string, object>? Weights { get; set; }
    /// <summary>score/count/choice: { when, add }. roll: { when, mul }.</summary>
    public List<AdultModDto> Mods { get; set; } = new();
    /// <summary>Standard deviation of the normal noise added to the raw value.</summary>
    public double Sigma { get; set; }
    /// <summary>score: z (normalized, clamped to ±3), z_raw (clamped only), unit (sigmoid), signed (tanh), clamp01.</summary>
    public string? Scale { get; set; }
    /// <summary>count: highest value.</summary>
    public int Cap { get; set; }
    /// <summary>blend: the stated value (an earlier adult trait) that revealed preference starts from.</summary>
    public string? Stated { get; set; }

    // roll / choice / derive
    public List<string> Values { get; set; } = new();
    /// <summary>roll: base weight per value (missing values weigh 1).</summary>
    public Dictionary<string, double>? Odds { get; set; }
    /// <summary>roll: facts whose current value gets inherit_weight added (for example a parent's religion).</summary>
    public List<string>? Inherit { get; set; }
    public double InheritWeight { get; set; }
    public List<ChoiceDto>? Options { get; set; }
    public double? Temperature { get; set; }
    public List<RuleDto>? Rules { get; set; }

    // list (kinks, health conditions)
    public List<ListEntryDto>? Entries { get; set; }
    public int Max { get; set; }
    /// <summary>Labels for intensity thirds, low to high (for example curious, enjoys, needs).</summary>
    public List<string>? Levels { get; set; }
    public RateLikeDto? Disclosure { get; set; }

    // goals
    public List<GoalDto>? Goals { get; set; }
    public int PickMin { get; set; }
    public int PickMax { get; set; }

    // issues
    public List<IssueDto>? Issues { get; set; }
    /// <summary>issues: trait (0-1) that keeps positions close to the axes.</summary>
    public string? Consistency { get; set; }
    public double Noise { get; set; }

    // dealbreakers
    public List<DealbreakerDto>? Dealbreakers { get; set; }
    public double HardAt { get; set; }

    // inner_life
    public double WoundThreshold { get; set; }
    public double RegretThreshold { get; set; }
    public int MaxSecrets { get; set; }
    public List<BeliefDto>? Beliefs { get; set; }
    /// <summary>History items (or item:outcome) a pawn might hide, with their stigma (0-1).</summary>
    public Dictionary<string, double>? Hideable { get; set; }
}

public sealed class AdultModDto
{
    public string? Note { get; set; }
    public Dictionary<string, object> When { get; set; } = new();
    public double Add { get; set; }
    public Dictionary<string, double>? Mul { get; set; }
}

/// <summary>base + weights + mods, without noise: used for intensities and disclosure.</summary>
public sealed class RateLikeDto
{
    public double Base { get; set; }
    public Dictionary<string, object>? Weights { get; set; }
    public List<AdultModDto> Mods { get; set; } = new();
}

/// <summary>An entry in a list trait: one kink, one health condition.</summary>
public sealed class ListEntryDto
{
    public string Id { get; set; } = "";
    public string? Note { get; set; }
    /// <summary>Base rate in the population.</summary>
    public double Prevalence { get; set; }
    public Dictionary<string, object>? Weights { get; set; }
    public List<AdultModDto> Mods { get; set; } = new();
    /// <summary>any, female or male.</summary>
    public string? Sex { get; set; }
    public RateLikeDto? Intensity { get; set; }
    /// <summary>How much people hide it (0-1). Lowers disclosure.</summary>
    public double Stigma { get; set; }
    /// <summary>Seen by anyone on a first date (disclosure 1).</summary>
    public bool Visible { get; set; }
}

public sealed class GoalDto
{
    public string Id { get; set; } = "";
    public string? Note { get; set; }
    public double Base { get; set; }
    public Dictionary<string, object>? Weights { get; set; }
    public List<AdultModDto> Mods { get; set; } = new();
    /// <summary>Typical age by which the pawn wants it.</summary>
    public double Deadline { get; set; }
    public Dictionary<string, object>? DeadlineWeights { get; set; }
    /// <summary>The goal counts as reached when these hold.</summary>
    public Dictionary<string, object>? Achieved { get; set; }
}

public sealed class IssueDto
{
    public string Id { get; set; } = "";
    public string? Note { get; set; }
    /// <summary>Weight of each political axis (adult traits) on this issue.</summary>
    public Dictionary<string, object>? Axes { get; set; }
    public double BaseSalience { get; set; }
    public List<StakeDto> Stakes { get; set; } = new();
}

/// <summary>Personal stake: when the pawn's history touches the issue, its position and salience move.</summary>
public sealed class StakeDto
{
    public string? Note { get; set; }
    public Dictionary<string, object> When { get; set; } = new();
    public double Shift { get; set; }
    public double Salience { get; set; }
}

public sealed class DealbreakerDto
{
    public string Id { get; set; } = "";
    /// <summary>What the rule requires of a partner, in words.</summary>
    public string Rule { get; set; } = "";
    public Dictionary<string, object> When { get; set; } = new();
    public RateLikeDto Hardness { get; set; } = new();
}

/// <summary>Maps how a wound was read to the belief it left, what the pawn chases, and what would heal it.</summary>
public sealed class BeliefDto
{
    /// <summary>Reading id, or item:reading for one event only.</summary>
    public string Reading { get; set; } = "";
    public string Belief { get; set; } = "";
    public string Want { get; set; } = "";
    public string Need { get; set; } = "";
}
