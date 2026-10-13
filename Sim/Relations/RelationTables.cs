using YouWillDate.Sim.Adult;

namespace YouWillDate.Sim.Relations;

/// <summary>base + weights + mods + N(0, sigma), compiled against the fact schema.</summary>
public sealed record NoisyFormula(Formula Formula, double Sigma)
{
    public double Eval(Facts facts, Rng? rng = null) =>
        TermMath.Sum(Formula.Base, Formula.Terms, Formula.Mods, facts) + (rng is null || Sigma == 0 ? 0 : Sigma * rng.NextNormal());
}

/// <summary>One entry of the profile vector x (attraction spec 7.2).</summary>
public sealed record ProfileEntry(string Key, bool Logit)
{
    public string Label => Logit ? $"logit({Key})" : Key;

    public double Read(Facts facts)
    {
        double v = facts.Num(Key) ?? 0;
        return Logit ? MathUtil.Logit(v) : v;
    }
}

/// <summary>Compiled aura.yaml.</summary>
public sealed class AuraTables
{
    public required int Harmonics { get; init; }
    public required double AuraNoise { get; init; }
    public required double TasteNoise { get; init; }
    public required int CalibrationSize { get; init; }
    public required ProfileEntry[] ProfileVector { get; init; }
}

public sealed record Readout(string Id, double Scale);

public sealed class PastPartnerTables
{
    public required PastPartnersDto Source { get; init; }
    public required NoisyFormula OwnStatus { get; init; }
    public required NoisyFormula Availability { get; init; }
}

public sealed record KnownField(string Key, bool Visible, double Weight);

public sealed class StartTables
{
    public required StartDto Source { get; init; }
    public required NoisyFormula Satisfaction { get; init; }
    public required NoisyFormula Trust { get; init; }
    public required NoisyFormula Standing { get; init; }
    public required NoisyFormula Mood { get; init; }
    public required NoisyFormula Openness { get; init; }
    public required KnownField[] Fields { get; init; }
    public required string[] Lists { get; init; }
}

/// <summary>Compiled relationships.yaml.</summary>
public sealed class RelationTables
{
    public required int PatternSize { get; init; }
    public required Readout[] Readouts { get; init; }
    public required TendenciesDto Tendencies { get; init; }
    public required IReadOnlyDictionary<string, double> Tolerance { get; init; }
    public required PastPartnerTables PastPartners { get; init; }
    public required CityDto City { get; init; }
    public required StartTables Start { get; init; }
}
