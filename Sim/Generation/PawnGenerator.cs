using YouWillDate.Sim.Adult;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Generation;

/// <summary>
/// Generates pawns in three parts, each reading only from the parts before it:
/// <list type="number">
/// <item>Starting point (spec 1–3): seed, birth context, family and household.</item>
/// <item>Life (spec 4–5): year-by-year events, choices and how the pawn read them.</item>
/// <item>Adult traits (spec 6): personality, attachment, health, beliefs, sexuality, social circle, wants, inner life.</item>
/// </list>
/// Each roll uses its own sub-seed, so the same seed and data files always give the same pawn.
/// </summary>
public sealed class PawnGenerator(GameData data)
{
    readonly StartingPointGenerator _start = new(data);

    public GameData Data => data;

    /// <param name="options">Overrides to force values, and whether to record a trace.</param>
    /// <exception cref="ArgumentException">An override names an unknown fact or invalid value.</exception>
    public Pawn Generate(ulong seed, GenerateOptions? options = null)
    {
        if (options?.Overrides is { } overrides) data.CheckOverrides(overrides);
        var run = new GenerationRun(seed, options?.Overrides, options?.Trace == true ? new List<RollTrace>() : null);
        int age(StartingPoint s) => data.Config.PresentYear - s.BirthYear;

        // 1. Starting point.
        var start = _start.Generate(run);

        // 2. Life, birth to the present.
        var life = data.Life is null ? null
            : LifeSimulator.Run(data, seed, run.Facts, start.BirthYear, age(start), run.Trace, options?.LifeDetails == true);

        // 3. Adult traits.
        var adult = data.Adult is null || life is null || options?.SkipAdult == true ? null
            : AdultProfiler.Run(data, seed, run.Facts, life, age(start), run.Trace);

        return new Pawn
        {
            Seed = seed,
            BirthYear = start.BirthYear,
            Age = age(start),
            BirthLocation = start.BirthLocation,
            CurrentCity = start.CurrentCity,
            Sex = start.Sex,
            Temperament = start.Temperament,
            Facts = run.Facts,
            Trace = run.Trace,
            History = life?.History ?? [],
            Partners = life?.Partners ?? [],
            Adult = adult,
        };
    }
}
