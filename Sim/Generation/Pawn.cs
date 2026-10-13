using YouWillDate.Sim.Data;

namespace YouWillDate.Sim.Generation;

/// <summary>A generated person. Later steps (life simulation, adult profile) add to this.</summary>
public sealed class Pawn
{
    public required ulong Seed { get; init; }
    public required int BirthYear { get; init; }
    public required int Age { get; init; }
    public required Location BirthLocation { get; init; }
    public required string Sex { get; init; }

    /// <summary>
    /// Temperament traits from config.yaml, each a 0–1 population percentile:
    /// 0.7 means higher than 70% of people.
    /// </summary>
    public required IReadOnlyDictionary<string, double> Temperament { get; init; }

    /// <summary>Every generated value, keyed like "context.urbanicity" or "mother.education".</summary>
    public required Facts Facts { get; init; }

    /// <summary>How each fact was produced, in generation order. Null unless tracing was on.</summary>
    public IReadOnlyList<RollTrace>? Trace { get; init; }

    public RollTrace? TraceFor(string key) => Trace?.FirstOrDefault(t => t.Key == key);
}
