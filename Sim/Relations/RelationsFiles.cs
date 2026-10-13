using System.ComponentModel;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Relations;

// Shapes of Data/aura.yaml and Data/relationships.yaml (attraction spec 7.2 and
// section 8, and spec section 9). Property names map to snake_case keys.

/// <summary>Data/aura.yaml: the aura and taste (attraction spec 7.2).</summary>
public sealed class AuraFile
{
    /// <summary>K: harmonics per aura.</summary>
    [DefaultValue(6)]
    public int Harmonics { get; set; } = 6;
    /// <summary>SD of the per-pawn noise ε added to the projection, so siblings don't share an aura.</summary>
    public double AuraNoise { get; set; } = 0.3;
    /// <summary>SD of the per-pawn noise η in each taste offset.</summary>
    public double TasteNoise { get; set; } = 0.5;
    /// <summary>Pawns generated to z-score the profile vector and measure the mean amplitude.</summary>
    [DefaultValue(400)]
    public int CalibrationSize { get; set; } = 400;
    /// <summary>Numeric facts making up the profile vector x. "logit(key)" takes the logit of a 0–1 value.</summary>
    public List<string> ProfileVector { get; set; } = new();
}

/// <summary>Data/relationships.yaml.</summary>
public sealed class RelationshipsFile
{
    /// <summary>K_r: values in a relationship pattern (8.3).</summary>
    [DefaultValue(4)]
    public int PatternSize { get; set; } = 4;
    /// <summary>Behaviors read out of a pattern (8.3): value = scale × σ(r · P).</summary>
    public List<ReadoutDto> Readouts { get; set; } = new();
    public TendenciesDto Tendencies { get; set; } = new();
    /// <summary>8.4 t_view: how much of a partner's relational life elsewhere each monogamy view tolerates.</summary>
    public Dictionary<string, double> Tolerance { get; set; } = new();
    public PastPartnersDto PastPartners { get; set; } = new();
    public CityDto City { get; set; } = new();
    public StartDto Start { get; set; } = new();
}

public sealed class ReadoutDto
{
    public string Id { get; set; } = "";
    [DefaultValue(1.0)]
    public double Scale { get; set; } = 1;
}

/// <summary>Coefficients on anxiety (x) and avoidance (y).</summary>
public sealed class AxesDto
{
    public double Base { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Attachment tendencies (8.2).</summary>
public sealed class TendenciesDto
{
    public AxesDto Voice { get; set; } = new() { X = 0.8, Y = -1.0 };
    public AxesDto Sensitivity { get; set; } = new() { X = 1.0, Y = -0.4 };
    public AxesDto ExitFloor { get; set; } = new() { Base = 0.35, X = -0.1, Y = -0.1 };
    public AxesDto Reaction { get; set; } = new() { X = 0.6, Y = -0.6 };
}

/// <summary>base + weights + mods + N(0, sigma), read from facts.</summary>
public sealed class FormulaDto
{
    public double Base { get; set; }
    public Dictionary<string, object>? Weights { get; set; }
    public List<AddModDto> Mods { get; set; } = new();
    public double Sigma { get; set; }
}

/// <summary>Records for the partners in a pawn's relationship history (spec 6.8 history pull).</summary>
public sealed class PastPartnersDto
{
    /// <summary>Mean age gap for opposite-sex partners, with the man older.</summary>
    public double MaleOlderYears { get; set; } = 2;
    public double AgeGapSd { get; set; } = 4;
    /// <summary>The pawn's own standing (z-scale) that partners' standing is drawn around.</summary>
    public FormulaDto OwnStatus { get; set; } = new();
    /// <summary>How closely partners match the pawn's standing.</summary>
    public double StatusMatch { get; set; } = 0.5;
    public double StatusSd { get; set; } = 0.8;
    /// <summary>Logit of how emotionally available partners were (0–1).</summary>
    public FormulaDto Availability { get; set; } = new();
}

/// <summary>Linking pawns into one city.</summary>
public sealed class CityDto
{
    /// <summary>Largest gap in relationship years between two pawns paired as a couple.</summary>
    [DefaultValue(2)]
    public int YearsTolerance { get; set; } = 2;
    /// <summary>Largest match cost for a couple.</summary>
    public double MaxCost { get; set; } = 3;
    /// <summary>Cost per year of relationship-length gap, per year of age away from the expected gap, per child of difference.</summary>
    public double YearsCost { get; set; } = 1;
    public double AgeCost { get; set; } = 0.25;
    public double ChildrenCost { get; set; } = 0.7;
    /// <summary>Extra candidates generated to find partners, as a multiple of the city size.</summary>
    public double OverflowFactor { get; set; } = 3;
    /// <summary>Chance a past partner who fits a resident is that resident.</summary>
    public double ExLinkChance { get; set; } = 0.5;
    /// <summary>Chance a close friend who fits a resident is that resident.</summary>
    public double FriendLinkChance { get; set; } = 0.4;
    /// <summary>Largest age gap between friends, by friend source (others use default_friend_age_gap).</summary>
    public Dictionary<string, double> FriendAgeGap { get; set; } = new();
    public double DefaultFriendAgeGap { get; set; } = 12;
}

/// <summary>Spec section 9: turning life histories into the runtime's starting state.</summary>
public sealed class StartDto
{
    /// <summary>Logit of each side's starting satisfaction with a current relationship.</summary>
    public FormulaDto Satisfaction { get; set; } = new();
    /// <summary>Provisional size of desire, |D| = base + satisfaction × this, until attraction (7) is built.</summary>
    public double DesireBase { get; set; } = 0.4;
    public double DesireFromSatisfaction { get; set; } = 0.5;
    /// <summary>Agreement exclusivity each monogamy view wants; a couple's level is the mean of the two.</summary>
    public Dictionary<string, double> Exclusivity { get; set; } = new();
    /// <summary>Dating couples together at least this long have had the talk (a Define agreement).</summary>
    [DefaultValue(1)]
    public int DefineAfterYears { get; set; } = 1;
    public double DefineMass { get; set; } = 1;
    public double FormalizeMass { get; set; } = 3;
    /// <summary>investment = 1 − exp(−years / investment_years) + investment_per_child × children.</summary>
    public double InvestmentYears { get; set; } = 2;
    public double InvestmentPerChild { get; set; } = 0.1;
    /// <summary>Starting trust in a partner or friend (clamped to 0–1).</summary>
    public FormulaDto Trust { get; set; } = new();
    /// <summary>Open resentment = this × (1 − satisfaction).</summary>
    public double ResentmentFromDissatisfaction { get; set; } = 0.2;
    /// <summary>Goals whose deadlines create goal pressure (7.6).</summary>
    public List<string> GoalPressureGoals { get; set; } = new();
    /// <summary>Hidden resentment is built up week by week as in 8.3, then capped so play doesn't open with everyone leaving.</summary>
    public double HiddenResentmentCap { get; set; } = 0.9;
    /// <summary>Years after which a pawn's estimate of a partner's desire is mostly observation rather than projection.</summary>
    public double EstimateYears { get; set; } = 2;
    /// <summary>The pawn's standing (z-scale), for power: power = tanh(power_scale × (standing A − standing B)).</summary>
    public FormulaDto Standing { get; set; } = new();
    public double PowerScale { get; set; } = 0.5;
    /// <summary>A friendship's desired pattern: |D| = closeness × this.</summary>
    public double FriendDesire { get; set; } = 0.3;
    /// <summary>Breakups with resident exes this recent keep a dissolved relationship record.</summary>
    public double RecentBreakupYears { get; set; } = 2;
    /// <summary>Harm and blame by how a relationship ended (8.5 grudge = harm × blame).</summary>
    public Dictionary<string, double> Harm { get; set; } = new();
    public Dictionary<string, double> Blame { get; set; } = new();
    public double DefaultHarm { get; set; } = 0.2;
    public double DefaultBlame { get; set; } = 0.3;
    /// <summary>Current mood, −1 to 1: tanh(this − mood_from_breakup × recency), recency = exp(−months since the last breakup / 3).</summary>
    public FormulaDto Mood { get; set; } = new();
    public double MoodFromBreakup { get; set; } = 0.5;
    /// <summary>Threat at the start: this × the threat_shift written in the last year.</summary>
    public double ThreatFromLastYear { get; set; } = 1;
    public KnowledgeDto Knowledge { get; set; } = new();
}

/// <summary>What pawns know about the people in their circle at the start.</summary>
public sealed class KnowledgeDto
{
    /// <summary>Disclosed facts: p = 1 − exp(−rate × weight × years × closeness × openness of the teller).</summary>
    [DefaultValue(1.0)]
    public double Rate { get; set; } = 1;
    /// <summary>Logit of how openly a pawn talks about itself (0–1).</summary>
    public FormulaDto Openness { get; set; } = new();
    public List<KnownFieldDto> Fields { get; set; } = new();
    /// <summary>Adult list traits (kinks, health conditions) known by each pick's disclosure.</summary>
    public List<string> Lists { get; set; } = new();
    /// <summary>Secrets: p = (1 − discovery difficulty) × (1 − exp(−secret_rate × years × closeness)).</summary>
    public double SecretRate { get; set; } = 0.3;
}

public sealed class KnownFieldDto
{
    public string Key { get; set; } = "";
    /// <summary>visible (known on sight) or disclosed (learned over time).</summary>
    public string Kind { get; set; } = "disclosed";
    [DefaultValue(1.0)]
    public double Weight { get; set; } = 1;
}
