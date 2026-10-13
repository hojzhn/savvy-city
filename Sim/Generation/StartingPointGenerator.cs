using System.Globalization;
using YouWillDate.Sim.Data;

namespace YouWillDate.Sim.Generation;

/// <summary>Where a pawn starts: spec steps 1 to 3.</summary>
public sealed record StartingPoint(
    int BirthYear, Location BirthLocation, Location CurrentCity, string Sex,
    IReadOnlyDictionary<string, double> Temperament, LocationPeriod BirthPeriod);

/// <summary>
/// The pawn's starting point, before anything happens to it: seed draws (step 1),
/// the world it is born into (step 2), and its family and household (step 3).
/// </summary>
sealed class StartingPointGenerator(GameData data)
{
    static readonly string[] Roles = ["mother", "father"];

    public StartingPoint Generate(GenerationRun run)
    {
        ulong seed = run.Seed;
        var cfg = data.Config;

        // 1. Seed. Every pawn lives in the game's city; where they were born is
        // drawn from that city's origins.
        var city = data.LocationById(run.Cat("pawn.current_city",
            () => data.CurrentCity.Id, RollSource.Lookup, "the game's city (current_city in config.yaml)"));
        int birthYear = (int)run.Num("year",
            () => Rng.For(seed, "seed", "birth_year").NextInt(data.MinBirthYear, data.MaxBirthYear + 1),
            $"even spread over {data.MinBirthYear}–{data.MaxBirthYear}");
        var location = data.LocationById(run.Cat("pawn.birth_location",
            () => data.Locations[Rng.For(seed, "seed", "birth_location").PickWeighted(data.BirthLocationWeights(city))].Id,
            RollSource.Drawn,
            city.Origins is null
                ? $"{city.Name} has no origins table, so weighted by location population"
                : $"weighted by where people living in {city.Name} were born (its origins in locations.yaml)"));
        run.Derived("pawn.born_locally", location.Id == city.Id ? "yes" : "no",
            $"born in {location.Name}, lives in {city.Name}");
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

        return new StartingPoint(birthYear, location, city, sex, temperament, period);
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
            p[i] = MathUtil.NormalCdf(z);
        }
        return p;
    }

    static void RollItem(GenerationRun run, TableItem item, LocationPeriod period, string? role, string key)
    {
        var facts = run.Facts;
        string? forced = run.Override(key);
        var failed = item.OnlyIf.Where(c => !FactTest.Test(c, facts, role)).ToList();

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
            bool fired = mod.When.All(c => FactTest.Test(c, facts, role));
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

}
