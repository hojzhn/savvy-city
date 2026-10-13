using System.ComponentModel;

namespace YouWillDate.Sim.Life;

// Shapes of Data/life.yaml (spec section 4). Property names map to snake_case keys.

public sealed class LifeFile
{
    public List<StageDto> Stages { get; set; } = new();
    /// <summary>Accumulator channels (spec 6.2) that readings and effects write to.</summary>
    public List<string> Channels { get; set; } = new();
    /// <summary>Softmax temperature for forks: low follows traits closely, high adds noise.</summary>
    [DefaultValue(1.0)]
    public double ForkTemperature { get; set; } = 1.0;
    /// <summary>Softmax temperature for event readings (spec section 5).</summary>
    [DefaultValue(1.0)]
    public double ReadingTemperature { get; set; } = 1.0;
    /// <summary>Relationships as involvements: per-involvement fields and the derived label.</summary>
    public InvolvementDto? Involvement { get; set; }
    public List<LifeItemDto> Items { get; set; } = new();
}

/// <summary>
/// A pawn can be involved with several people at once. Each involvement has its
/// own fields (inv.&lt;id&gt;), such as its agreement. Relationship labels are not
/// stored: state.relationship is derived from the involvements by the label rules.
/// </summary>
public sealed class InvolvementDto
{
    public string? Note { get; set; }
    public List<InvolvementFieldDto> Fields { get; set; } = new();
    /// <summary>Derives state.relationship whenever an involvement starts, changes or dissolves.</summary>
    public InvolvementLabelDto Label { get; set; } = new();
    /// <summary>How involved the two are (inv.level, 0–1): it settles by the agreements and drains after a dissolve.</summary>
    public InvolvementLevelDto Level { get; set; } = new();
}

/// <summary>
/// Attraction spec 8.3, coarsely: the level stands in for how far the pattern is
/// from the origin. While the involvement holds, it moves each year toward where
/// its agreements anchor it. After a dissolve, it drains week by week toward 0 but
/// never ends: past partners stay involvements, at or near 0.
/// </summary>
public sealed class InvolvementLevelDto
{
    public string? Note { get; set; }
    /// <summary>The field whose value sets where the level settles (for example agreement).</summary>
    public string? Field { get; set; }
    /// <summary>Where the level settles for each value of the field. Missing values settle at 1.</summary>
    public Dictionary<string, double> SettlesAt { get; set; } = new();
    /// <summary>Share of the gap to where it settles closed each year.</summary>
    [DefaultValue(0.5)]
    public double Approach { get; set; } = 0.5;
    /// <summary>After a dissolve: share of the remaining level lost each week (8.3: 10%).</summary>
    [DefaultValue(0.1)]
    public double DrainPerWeek { get; set; } = 0.1;
    /// <summary>state.involvements counts the involvements at or above this level.</summary>
    [DefaultValue(0.05)]
    public double InvolvedAt { get; set; } = 0.05;
}

public sealed class InvolvementFieldDto
{
    public string Id { get; set; } = "";
    public string? Note { get; set; }
    public List<string> Values { get; set; } = new();
    /// <summary>Value when the involvement starts (defaults to the first value).</summary>
    public string? Initial { get; set; }
}

public sealed class InvolvementLabelDto
{
    public List<string> Values { get; set; } = new();
    /// <summary>First matching rule wins. Use any_inv.&lt;field&gt; and state.involvements.</summary>
    public List<RuleDto> Rules { get; set; } = new();
}

public sealed class StageDto
{
    public string Id { get; set; } = "";
    public int From { get; set; }
    /// <summary>Last age in the stage (inclusive). The final stage runs to the present.</summary>
    public int To { get; set; }
    /// <summary>Weight of this stage's channel deltas in the adult profile (spec 6.2).</summary>
    [DefaultValue(1.0)]
    public double Weight { get; set; } = 1;
}

public sealed class LifeItemDto
{
    public string Id { get; set; } = "";
    public string? Note { get; set; }
    /// <summary>condition, event or fork.</summary>
    public string Type { get; set; } = "";
    /// <summary>
    /// Conditions only. state: changed only by effects. number: numeric, changed by effects.
    /// roll: weighted roll on its schedule. drift: moves one step up or down on its schedule.
    /// derive: first matching rule, every year. counter: +1 each year while its conditions hold.
    /// </summary>
    public string? Kind { get; set; }

    // ---- schedule ----
    /// <summary>[stage] or [first_stage, last_stage]. Empty means the whole life.</summary>
    public List<string> Window { get; set; } = new();
    public int? FromAge { get; set; }
    public int? ToAge { get; set; }
    /// <summary>Exact ages to roll at (instead of window and frequency).</summary>
    public List<int>? Ages { get; set; }
    /// <summary>once, per_stage or per_year.</summary>
    public string? Frequency { get; set; }
    /// <summary>Most times an event can fire (0 = no limit; "once" implies 1).</summary>
    public int Max { get; set; }
    /// <summary>Force the event in its last simulated year if it has not fired by then.</summary>
    public bool MustHappen { get; set; }
    /// <summary>Events only: roll once a year for each ongoing involvement, which inv.&lt;field&gt; then reads and sets.</summary>
    public bool PerInvolvement { get; set; }
    public Dictionary<string, object>? Requires { get; set; }

    // ---- conditions ----
    public List<string> Values { get; set; } = new();
    /// <summary>Fact to copy as the starting value (for example household.wealth).</summary>
    public string? Initial { get; set; }
    public string? InitialValue { get; set; }
    public List<RuleDto>? InitialRules { get; set; }
    /// <summary>roll: base weights per value (missing values weigh 1).</summary>
    public Dictionary<string, double>? Weights { get; set; }
    /// <summary>roll: multiplier mods, as in family.yaml.</summary>
    public List<Data.ModDto> Mods { get; set; } = new();
    /// <summary>roll: value used when requires fails.</summary>
    public string? Default { get; set; }
    public RateDto? Up { get; set; }
    public RateDto? Down { get; set; }
    public List<RuleDto>? Rules { get; set; }
    public Dictionary<string, object>? While { get; set; }
    /// <summary>Write a history entry whenever the value changes.</summary>
    public bool Log { get; set; }

    // ---- events ----
    public RateDto? Rate { get; set; }
    public List<ChoiceDto>? Outcomes { get; set; }
    public EffectsDto? Effects { get; set; }
    /// <summary>Spec section 5: how the pawn reads the event, weighted by temperament.</summary>
    public List<ChoiceDto>? Readings { get; set; }
    /// <summary>negative or positive; sets the impact score's valence factor.</summary>
    public string? Valence { get; set; }
    public List<string>? Tags { get; set; }

    // ---- forks ----
    public List<ChoiceDto>? Options { get; set; }
    public double? Temperature { get; set; }
    /// <summary>State the fork keeps re-choosing (for example state.career). Staying is favoured by sticky_bonus.</summary>
    public string? StickyKey { get; set; }
    public double StickyBonus { get; set; }
    public List<string>? StickyExcept { get; set; }
}

/// <summary>p = sigmoid(logit(base) + Σ weights + Σ mods).</summary>
public sealed class RateDto
{
    /// <summary>A probability, or "rate.&lt;name&gt;" to look it up in eras.yaml base_rates for the calendar year.</summary>
    public string Base { get; set; } = "";
    /// <summary>
    /// fact: number (numeric facts; temperament is centred to -1..1), or
    /// fact: { value: number } (categorical: added when the fact has that value).
    /// </summary>
    public Dictionary<string, object>? Weights { get; set; }
    public List<AddModDto> Mods { get; set; } = new();
}

public sealed class AddModDto
{
    public string? Note { get; set; }
    public Dictionary<string, object> When { get; set; } = new();
    /// <summary>Added to the logit (or utility) when every condition holds.</summary>
    public double Add { get; set; }
}

/// <summary>A fork option, event outcome, or reading. Chance ∝ weight × exp(utility / temperature).</summary>
public sealed class ChoiceDto
{
    public string Id { get; set; } = "";
    public string? Note { get; set; }
    [DefaultValue(1.0)]
    public double Weight { get; set; } = 1;
    public Dictionary<string, object>? Weights { get; set; }
    public List<AddModDto> Mods { get; set; } = new();
    public Dictionary<string, object>? Requires { get; set; }
    public EffectsDto? Effects { get; set; }
}

public sealed class EffectsDto
{
    /// <summary>Categorical state to set. "$current_city" means the game's city.</summary>
    public Dictionary<string, string>? Set { get; set; }
    /// <summary>Numbers to add to numeric state.</summary>
    public Dictionary<string, double>? Add { get; set; }
    /// <summary>Numeric state set to 0.</summary>
    public List<string>? Reset { get; set; }
    /// <summary>Numeric state set to the current age, only the first time.</summary>
    public List<string>? RecordFirstAge { get; set; }
    /// <summary>Deltas added to accumulator channels.</summary>
    public Dictionary<string, double>? Channels { get; set; }
    /// <summary>Start a new involvement; inv.&lt;field&gt; sets in the same event apply to it.</summary>
    public bool StartInvolvement { get; set; }
    /// <summary>Per-involvement events: 8.3 Dissolve. The agreements go and the level drains; the involvement never ends. The outcome id is how it was dissolved.</summary>
    public bool Dissolve { get; set; }
}

public sealed class RuleDto
{
    public Dictionary<string, object>? When { get; set; }
    public string Value { get; set; } = "";
}
