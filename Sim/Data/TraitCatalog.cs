using System.Globalization;

namespace YouWillDate.Sim.Data;

public enum TraitKind { Seed, Temperament, Context, Table, Derived, Life, Adult }

/// <summary>One trait a pawn can have, as defined by the data files.</summary>
public sealed class TraitInfo
{
    /// <summary>Schema key: "year", "pawn.reward_drive", "context.urbanicity", "parent.education", "household.wealth".</summary>
    public required string Key { get; init; }
    public required string Section { get; init; }
    public required string Label { get; init; }
    public required TraitKind Kind { get; init; }
    /// <summary>Possible values, or null for a numeric trait.</summary>
    public IReadOnlyList<string>? Values { get; init; }
    /// <summary>One-paragraph explanation of how the value is produced.</summary>
    public required string HowDetermined { get; init; }
    public string? Note { get; init; }
    /// <summary>Keys of the pawn facts holding this trait ("mother.education" and "father.education" for one per-parent trait).</summary>
    public required IReadOnlyList<string> FactKeys { get; init; }
    /// <summary>Schema keys this trait's rules read.</summary>
    public IReadOnlyList<string> Reads { get; init; } = [];
    /// <summary>Schema keys of traits whose rules read this one.</summary>
    public List<string> ReadBy { get; } = new();

    // Table traits only: where the definition lives in family.yaml.
    public int GroupIndex { get; init; } = -1;
    public int ItemIndex { get; init; } = -1;
}

/// <summary>Every trait in the data, in generation order, with dependency links.</summary>
public sealed partial class TraitCatalog
{
    public IReadOnlyList<TraitInfo> Traits { get; }
    readonly Dictionary<string, TraitInfo> _byKey;

    public TraitInfo? Get(string key) => _byKey.GetValueOrDefault(key);

    public TraitCatalog(GameData data)
    {
        var list = new List<TraitInfo>();
        var cfg = data.Config;
        string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        list.Add(new TraitInfo
        {
            Key = "year", Section = "Seed", Label = "birth year", Kind = TraitKind.Seed, FactKeys = ["year"],
            HowDetermined = $"Random, spread evenly over {data.MinBirthYear}–{data.MaxBirthYear}. " +
                            $"Set by present_year ({cfg.PresentYear}) minus age_max ({cfg.AgeMax}) and age_min ({cfg.AgeMin}) in config.yaml.",
        });
        var locationIds = data.Locations.Select(l => l.Id).ToList();
        var city = data.CurrentCity;
        list.Add(new TraitInfo
        {
            Key = "pawn.current_city", Section = "Seed", Label = "current city", Kind = TraitKind.Seed,
            FactKeys = ["pawn.current_city"], Values = locationIds,
            HowDetermined = $"Not random: every pawn lives in the city the game takes place in, {city.Name} " +
                            "(current_city in config.yaml).",
        });
        list.Add(new TraitInfo
        {
            Key = "pawn.birth_location", Section = "Seed", Label = "birth location", Kind = TraitKind.Seed,
            FactKeys = ["pawn.birth_location"], Values = locationIds, Reads = ["pawn.current_city"],
            HowDetermined = city.Origins is null
                ? $"Random, weighted by each location's population, because {city.Name} has no origins table in locations.yaml. " +
                  "Add one to say where people living there were born."
                : $"Random, weighted by where people living in {city.Name} were born (its origins table in locations.yaml). " +
                  "The birth location sets the birth context below.",
        });
        list.Add(new TraitInfo
        {
            Key = "pawn.born_locally", Section = "Seed", Label = "born locally", Kind = TraitKind.Derived,
            FactKeys = ["pawn.born_locally"], Values = ["yes", "no"],
            Reads = ["pawn.birth_location", "pawn.current_city"],
            HowDetermined = "Derived: 'yes' when the birth location is the current city.",
        });
        list.Add(new TraitInfo
        {
            Key = "pawn.sex", Section = "Seed", Label = "sex", Kind = TraitKind.Seed,
            FactKeys = ["pawn.sex"], Values = ContextSchema.Sexes,
            HowDetermined = $"Random: male with chance {Num(cfg.MaleBirthRate)} (male_birth_rate in config.yaml).",
        });

        foreach (var t in cfg.Temperament.Traits)
        {
            var corr = cfg.Temperament.Correlations
                .Where(c => c.A == t || c.B == t)
                .Select(c => $"{(c.A == t ? c.B : c.A)} {Num(c.R)}")
                .ToList();
            list.Add(new TraitInfo
            {
                Key = $"pawn.{t}", Section = "Temperament", Label = t, Kind = TraitKind.Temperament,
                FactKeys = [$"pawn.{t}"],
                HowDetermined = "Random 0–1 population percentile (0.7 = higher than 70% of people), drawn jointly with " +
                                "the other temperament traits. " +
                                (corr.Count == 0 ? "Not correlated with any other trait." : "Correlated with: " + string.Join(", ", corr) + "."),
            });
        }

        foreach (var (field, values) in ContextSchema.Values)
        {
            string how = field switch
            {
                "urbanicity" => "Looked up from the birth location in locations.yaml (where the pawn grew up, not where they live now).",
                "economic_era" => "Looked up by birth year in eras.yaml, unless the birth location's period sets its own economic_era.",
                _ => "Looked up from the birth location's period covering the birth year in locations.yaml (where the pawn grew up, not where they live now).",
            };
            list.Add(new TraitInfo
            {
                Key = $"context.{field}", Section = "Birth context", Label = field, Kind = TraitKind.Context,
                FactKeys = [$"context.{field}"], Values = values, HowDetermined = how,
                Reads = field == "economic_era" ? ["pawn.birth_location", "year"] : ["pawn.birth_location"],
            });
        }

        var source = data.FamilySource.Groups;
        for (int g = 0; g < data.Family.Count; g++)
        {
            var group = data.Family[g];
            for (int i = 0; i < group.Items.Count; i++)
            {
                var item = group.Items[i];
                var dto = source[g].Items[i];
                var conditions = item.OnlyIf.Concat(item.Mods.SelectMany(m => m.When));
                list.Add(new TraitInfo
                {
                    Key = group.PerParent ? $"parent.{item.Id}" : $"{group.Name}.{item.Id}",
                    Section = group.Name, Label = item.Id, Kind = TraitKind.Table,
                    Values = item.Values, Note = dto.Note,
                    FactKeys = group.PerParent ? [$"mother.{item.Id}", $"father.{item.Id}"] : [$"{group.Name}.{item.Id}"],
                    Reads = conditions.Select(c => GameData.ConditionSchemaKey(c.Key)).Distinct().ToList(),
                    HowDetermined = Describe(group, item),
                    GroupIndex = g, ItemIndex = i,
                });
            }

            if (group.PerParent)
                foreach (var item in group.Items)
                    list.Add(new TraitInfo
                    {
                        Key = $"parents.same.{item.Id}", Section = "parents match", Label = item.Id, Kind = TraitKind.Derived,
                        Values = ["yes", "no"], FactKeys = [$"parents.same.{item.Id}"],
                        Reads = [$"parent.{item.Id}"],
                        HowDetermined = $"Derived: 'yes' when the mother's and father's {item.Id} match.",
                    });
        }

        if (data.Life is not null) AddLife(list, data);
        if (data.Adult is not null) AddAdult(list, data);

        Traits = list;
        _byKey = list.ToDictionary(t => t.Key);
        // Built-in, not listed: which parent a per-parent roll is for.
        _byKey["parent.role"] = new TraitInfo
        {
            Key = "parent.role", Section = "parents", Label = "role", Kind = TraitKind.Derived,
            Values = ["mother", "father"], FactKeys = [],
            HowDetermined = "Built in: which parent a per-parent item is being rolled for (mother or father).",
        };
        foreach (var t in list)
            foreach (var r in t.Reads)
                if (_byKey.TryGetValue(r, out var target) && !target.ReadBy.Contains(t.Key))
                    target.ReadBy.Add(t.Key);
    }

    static string Describe(TableGroup group, TableItem item)
    {
        var parts = new List<string>
        {
            group.PerParent ? "Weighted random roll, once for the mother and once for the father." : "Weighted random roll.",
            item.BaseFrom is null
                ? "Base weights come from the item's weights."
                : $"Base weights come from the birth location's '{item.BaseFrom}' shares in locations.yaml.",
            item.Mods.Length == 0
                ? "No modifiers."
                : $"{item.Mods.Length} modifier(s) multiply the weights when their conditions hold.",
        };
        if (item.OnlyIf.Length > 0)
            parts.Add($"Only rolled when {string.Join(" and ", item.OnlyIf.Select(c => c.Text))}; otherwise '{item.Default}'.");
        if (item.Assort > 0)
            parts.Add($"The father copies the mother's value with chance {item.Assort.ToString("0.##", CultureInfo.InvariantCulture)} (assortative pairing).");
        return string.Join(" ", parts);
    }
}
