using System.Globalization;
using YouWillDate.Sim.Data;

namespace YouWillDate.Sim.Generation;

/// <summary>
/// Generates pawns in the spec's fixed order: seed, context, family and household.
/// Each step reads only from the steps before it, and each roll uses its own
/// sub-seed, so the same seed and data files always give the same pawn.
/// </summary>
public sealed class PawnGenerator(GameData data)
{
    static readonly string[] Roles = ["mother", "father"];

    public GameData Data => data;

    /// <param name="options">Overrides to force values, and whether to record a trace.</param>
    /// <exception cref="ArgumentException">An override names an unknown fact or invalid value.</exception>
    public Pawn Generate(ulong seed, GenerateOptions? options = null)
    {
        if (options?.Overrides is { } overrides) data.CheckOverrides(overrides);
        var run = new Run(seed, options?.Overrides, options?.Trace == true ? new List<RollTrace>() : null);
        var cfg = data.Config;

        // 1. Seed: four independent draws.
        int birthYear = (int)run.Num("year",
            () => Rng.For(seed, "seed", "birth_year").NextInt(data.MinBirthYear, data.MaxBirthYear + 1),
            $"even spread over {data.MinBirthYear}–{data.MaxBirthYear}");
        var location = data.LocationById(run.Cat("pawn.location",
            () => PickLocation(Rng.For(seed, "seed", "location")).Id, RollSource.Drawn, "weighted by location population"));
        string sex = run.Cat("pawn.sex",
            () => Rng.For(seed, "seed", "sex").Chance(cfg.MaleBirthRate) ? "male" : "female",
            RollSource.Drawn, $"male birth rate {cfg.MaleBirthRate}");

        var drawn = DrawTemperament(Rng.For(seed, "seed", "temperament"));
        var temperament = new Dictionary<string, double>();
        var traits = cfg.Temperament.Traits;
        for (int i = 0; i < traits.Count; i++)
        {
            int index = i;
            temperament[traits[i]] = run.Num($"pawn.{traits[i]}", () => drawn[index],
                "percentile; drawn jointly with the other traits (overriding one doesn't adjust the rest)");
        }

        // 2. Context: table lookups, no random draws.
        var period = location.PeriodFor(birthYear);
        string where = $"{location.Name}, period from {period.From}";
        run.Cat("context.urbanicity", () => location.Urbanicity, RollSource.Lookup, location.Name);
        run.Cat("context.local_politics", () => period.LocalPolitics, RollSource.Lookup, where);
        run.Cat("context.diversity", () => period.Diversity, RollSource.Lookup, where);
        run.Cat("context.economic_era", () => data.EconomicEra(birthYear, period), RollSource.Lookup,
            period.EconomicEra is null ? $"eras.yaml, {birthYear}" : $"{where} (overrides eras.yaml)");
        run.Cat("context.norms_sex", () => period.NormsSex, RollSource.Lookup, where);
        run.Cat("context.norms_gender", () => period.NormsGender, RollSource.Lookup, where);
        run.Cat("context.norms_family", () => period.NormsFamily, RollSource.Lookup, where);

        // 3. Family and household.
        foreach (var group in data.Family)
        {
            if (!group.PerParent)
            {
                foreach (var item in group.Items)
                    RollItem(run, item, period, role: null, $"{group.Name}.{item.Id}");
                continue;
            }

            foreach (var role in Roles)
            {
                run.Facts.Set($"{role}.role", role);
                foreach (var item in group.Items)
                    RollItem(run, item, period, role, $"{role}.{item.Id}");
            }
            foreach (var item in group.Items)
            {
                string m = run.Facts[$"mother.{item.Id}"], f = run.Facts[$"father.{item.Id}"];
                run.Derived($"parents.same.{item.Id}", m == f ? "yes" : "no", $"mother {m}, father {f}");
            }
        }

        return new Pawn
        {
            Seed = seed,
            BirthYear = birthYear,
            Age = cfg.PresentYear - birthYear,
            BirthLocation = location,
            Sex = sex,
            Temperament = temperament,
            Facts = run.Facts,
            Trace = run.Trace,
        };
    }

    Location PickLocation(Rng rng)
    {
        var weights = data.Locations.Select(l => l.Population).ToArray();
        return data.Locations[rng.PickWeighted(weights)];
    }

    /// <summary>
    /// Correlated standard normals (via the Cholesky factor of the configured
    /// correlations), mapped through the normal CDF to percentiles in 0–1.
    /// </summary>
    double[] DrawTemperament(Rng rng)
    {
        var l = data.TemperamentCholesky;
        int n = l.GetLength(0);
        var e = new double[n];
        for (int i = 0; i < n; i++) e[i] = rng.NextNormal();

        var p = new double[n];
        for (int i = 0; i < n; i++)
        {
            double z = 0;
            for (int j = 0; j <= i; j++) z += l[i, j] * e[j];
            p[i] = NormalCdf(z);
        }
        return p;
    }

    static void RollItem(Run run, TableItem item, LocationPeriod period, string? role, string key)
    {
        var facts = run.Facts;
        string? forced = run.Override(key);
        var failed = item.OnlyIf.Where(c => !Test(c, facts, role)).ToList();

        if (failed.Count > 0 && forced is null)
        {
            facts.Set(key, item.Default!);
            run.Add(new RollTrace
            {
                Key = key, Source = RollSource.Default, Value = item.Default,
                FailedOnlyIf = failed.Select(c => c.Text).ToList(),
            });
            return;
        }

        var baseWeights = (double[])item.BaseWeights.Clone();
        if (item.BaseFrom is not null)
        {
            var shares = period.Shares(item.BaseFrom)!;
            for (int i = 0; i < baseWeights.Length; i++)
                baseWeights[i] = shares.GetValueOrDefault(item.Values[i]);
        }

        var weights = (double[])baseWeights.Clone();
        var modTraces = run.Tracing ? new List<ModTrace>() : null;
        foreach (var mod in item.Mods)
        {
            bool fired = mod.When.All(c => Test(c, facts, role));
            if (fired)
                foreach (var (index, factor) in mod.Mul)
                    weights[index] *= factor;
            modTraces?.Add(new ModTrace(mod.Number,
                string.Join(" and ", mod.When.Select(c => c.Text)),
                string.Join(", ", mod.Mul.Select(m => string.Create(CultureInfo.InvariantCulture, $"{item.Values[m.Index]} x{m.Factor}"))),
                fired));
        }

        string value;
        RollSource source;
        string? note = null;
        if (forced is not null)
        {
            value = forced;
            source = RollSource.Override;
            if (failed.Count > 0) note = $"only_if failed, so this would normally be '{item.Default}'";
            else if (weights[Array.IndexOf(item.Values, forced)] == 0) note = "this value has zero weight here; it could never be rolled";
        }
        else
        {
            var rng = Rng.For(run.Seed, "family", key);
            // Assortative pairing: the father may share the mother's value,
            // as long as his own weights allow it.
            bool copy = rng.Chance(item.Assort);
            int m = role == "father" && copy ? Array.IndexOf(item.Values, facts[$"mother.{item.Id}"]) : -1;
            if (m >= 0 && weights[m] > 0)
            {
                value = item.Values[m];
                source = RollSource.AssortCopy;
                note = $"copied the mother's value (assort {item.Assort.ToString(CultureInfo.InvariantCulture)})";
            }
            else
            {
                int pick = rng.PickWeighted(weights);
                if (pick < 0)
                    throw new InvalidOperationException(
                        $"family.yaml: every weight for '{key}' is zero for seed {run.Seed}; check its mods");
                value = item.Values[pick];
                source = RollSource.Drawn;
            }
        }

        facts.Set(key, value);
        run.Add(new RollTrace
        {
            Key = key, Source = source, Value = value, Note = note,
            Values = item.Values, BaseWeights = baseWeights, FinalWeights = weights,
            BaseFrom = item.BaseFrom, Mods = modTraces ?? [],
            FailedOnlyIf = failed.Select(c => c.Text).ToList(),
        });
    }

    static bool Test(Condition c, Facts facts, string? role)
    {
        if (c.Key.StartsWith("self."))
            return TestKey(c, facts, $"{role}.{c.Key[5..]}");
        if (c.Key.StartsWith("any_parent."))
        {
            string rest = c.Key["any_parent.".Length..];
            return TestKey(c, facts, $"mother.{rest}") || TestKey(c, facts, $"father.{rest}");
        }
        return TestKey(c, facts, c.Key);
    }

    static bool TestKey(Condition c, Facts facts, string key) =>
        c.IsNumeric ? c.TestNumeric(facts.Num(key)) : c.TestCategorical(facts.Cat(key));

    /// <summary>Standard normal CDF (Abramowitz–Stegun 7.1.26, error under 1.5e-7).</summary>
    static double NormalCdf(double z)
    {
        double x = Math.Abs(z) / Math.Sqrt(2);
        double t = 1 / (1 + 0.3275911 * x);
        double erf = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return z >= 0 ? 0.5 * (1 + erf) : 0.5 * (1 - erf);
    }

    /// <summary>State for one Generate call: facts so far, overrides, and the trace.</summary>
    sealed class Run(ulong seed, Facts? overrides, List<RollTrace>? trace)
    {
        public ulong Seed { get; } = seed;
        public Facts Facts { get; } = new();
        public List<RollTrace>? Trace { get; } = trace;
        public bool Tracing => Trace is not null;

        public string? Override(string key) => overrides?.Cat(key);

        public void Add(RollTrace t) => Trace?.Add(t);

        public string Cat(string key, Func<string> compute, RollSource source, string? note = null)
        {
            string? forced = overrides?.Cat(key);
            string value = forced ?? compute();
            Facts.Set(key, value);
            Add(new RollTrace { Key = key, Source = forced is null ? source : RollSource.Override, Value = value, Note = note });
            return value;
        }

        public double Num(string key, Func<double> compute, string? note = null)
        {
            double? forced = overrides?.Num(key);
            double value = forced ?? compute();
            Facts.Set(key, value);
            Add(new RollTrace { Key = key, Source = forced is null ? RollSource.Drawn : RollSource.Override, Number = value, Note = note });
            return value;
        }

        public void Derived(string key, string value, string note)
        {
            Facts.Set(key, value);
            Add(new RollTrace { Key = key, Source = RollSource.Derived, Value = value, Note = note });
        }
    }
}
