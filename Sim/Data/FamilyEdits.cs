using YouWillDate.Sim.Adult;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Data;

/// <summary>
/// Edits to the data files that have to touch more than one place, such as
/// renames that must update every condition referring to the renamed thing,
/// in family.yaml, life.yaml and adult.yaml. All methods change the objects in place;
/// callers work on copies.
/// </summary>
public static class FamilyEdits
{
    /// <summary>Schema key for a family item: "parent.education" or "household.wealth".</summary>
    public static string ItemKey(GroupDto group, ItemDto item) =>
        group.PerParent ? $"parent.{item.Id}" : $"{group.Name}.{item.Id}";

    public static (GroupDto Group, ItemDto Item)? FindItem(FamilyFile family, string schemaKey)
    {
        foreach (var g in family.Groups)
            foreach (var i in g.Items)
                if (ItemKey(g, i) == schemaKey) return (g, i);
        return null;
    }

    /// <summary>An id not yet used in the file, like "new_trait_2".</summary>
    public static string UniqueItemId(FamilyFile family, string stem)
    {
        var used = family.Groups.SelectMany(g => g.Items).Select(i => i.Id).ToHashSet();
        if (!used.Contains(stem)) return stem;
        for (int n = 2; ; n++)
            if (!used.Contains($"{stem}_{n}")) return $"{stem}_{n}";
    }

    public static void RenameItem(FamilyFile family, GroupDto group, ItemDto item, string newId,
                                  LifeFile? life = null, AdultFile? adult = null)
    {
        if (newId == item.Id) return;
        string old = item.Id;
        item.Id = newId;
        if (group.PerParent)
        {
            foreach (var prefix in new[] { "self.", "mother.", "father.", "any_parent.", "parents.same." })
                RenameKeys(family, life, adult, prefix + old, prefix + newId);
        }
        else
        {
            RenameKeys(family, life, adult, $"{group.Name}.{old}", $"{group.Name}.{newId}");
        }
    }

    public static void RenameGroup(FamilyFile family, GroupDto group, string newName,
                                   LifeFile? life = null, AdultFile? adult = null)
    {
        if (newName == group.Name) return;
        string old = group.Name;
        group.Name = newName;
        if (group.PerParent) return; // per-parent keys use mother./father., not the group name
        foreach (var item in group.Items)
            RenameKeys(family, life, adult, $"{old}.{item.Id}", $"{newName}.{item.Id}");
    }

    /// <summary>Rename a value in an item and in every condition and weight that tests that item.</summary>
    public static void RenameValue(FamilyFile family, GroupDto group, ItemDto item, string oldValue, string newValue,
                                   LifeFile? life = null, AdultFile? adult = null)
    {
        if (oldValue == newValue) return;
        int i = item.Values.IndexOf(oldValue);
        if (i < 0) return;
        item.Values[i] = newValue;
        if (item.Weights is not null) RenameKey(item.Weights, oldValue, newValue);
        if (item.Default == oldValue) item.Default = newValue;
        foreach (var mod in item.Mods) RenameKey(mod.Mul, oldValue, newValue);

        string target = ItemKey(group, item);
        foreach (var (_, conditions) in AllConditions(family, life, adult))
            foreach (var key in conditions.Keys.ToList())
                if (GameData.ConditionSchemaKey(key) == target)
                    conditions[key] = RenameInSpec(conditions[key], oldValue, newValue);
        foreach (var weights in LifeWeightMaps(life).Concat(AdultWeightMaps(adult)))
            foreach (var (key, spec) in weights)
                if (GameData.ConditionSchemaKey(key) == target && spec is Dictionary<object, object> perValue
                    && perValue.Remove(oldValue, out var w))
                    perValue[newValue] = w;
    }

    /// <summary>Remove a value from an item, along with its weight and every mod factor for it.</summary>
    public static void RemoveValue(ItemDto item, string value)
    {
        item.Values.Remove(value);
        item.Weights?.Remove(value);
        foreach (var mod in item.Mods) mod.Mul.Remove(value);
        if (item.Default == value) item.Default = item.Values.FirstOrDefault();
    }

    public static void RenameTemperamentTrait(Config config, FamilyFile family, string oldName, string newName,
                                              LifeFile? life = null, AdultFile? adult = null)
    {
        if (oldName == newName) return;
        var traits = config.Temperament.Traits;
        int i = traits.IndexOf(oldName);
        if (i < 0) return;
        traits[i] = newName;
        foreach (var c in config.Temperament.Correlations)
        {
            if (c.A == oldName) c.A = newName;
            if (c.B == oldName) c.B = newName;
        }
        RenameKeys(family, life, adult, $"pawn.{oldName}", $"pawn.{newName}");
        RenameKeys(family, life, adult, $"z.{oldName}", $"z.{newName}");
    }

    /// <summary>Rules anywhere that read the given schema key, as "where: key".</summary>
    public static List<string> ReferencesTo(FamilyFile family, string schemaKey, LifeFile? life = null, AdultFile? adult = null)
    {
        bool Reads(string key) =>
            GameData.ConditionSchemaKey(key) == schemaKey
            || (key.StartsWith("parents.same.") && "parent." + key[13..] == schemaKey);

        var found = new List<string>();
        foreach (var (owner, conditions) in AllConditions(family, life, adult))
            foreach (var key in conditions.Keys)
                if (Reads(key)) found.Add($"{owner}: {key}");
        if (adult is not null)
            foreach (var item in adult.Groups.SelectMany(g => g.Items))
            {
                foreach (var weights in AdultWeightMaps(item))
                    foreach (var key in weights.Keys)
                        if (Reads(key)) found.Add($"adult.yaml {item.Id}: {key}");
                foreach (var key in AdultKeyFields(item))
                    if (Reads(key)) found.Add($"adult.yaml {item.Id}: {key}");
            }
        if (life is not null)
            foreach (var item in life.Items)
            {
                foreach (var weights in LifeWeightMaps(item))
                    foreach (var key in weights.Keys)
                        if (Reads(key)) found.Add($"life.yaml {item.Id}: {key}");
                if (item.Initial is not null && Reads(item.Initial)) found.Add($"life.yaml {item.Id}: initial {item.Initial}");
            }
        return found.Distinct().ToList();
    }

    // ---------- helpers ----------

    /// <summary>Every condition map in both files, with a label for where it lives.</summary>
    static IEnumerable<(string Owner, Dictionary<string, object> Conditions)> AllConditions(FamilyFile family, LifeFile? life,
                                                                                          AdultFile? adult = null)
    {
        foreach (var g in family.Groups)
            foreach (var item in g.Items)
            {
                string owner = ItemKey(g, item);
                if (item.OnlyIf is not null) yield return (owner, item.OnlyIf);
                foreach (var mod in item.Mods) yield return (owner, mod.When);
            }
        if (life is not null)
            foreach (var item in life.Items)
            {
                string owner = $"life.yaml {item.Id}";
                foreach (var map in LifeConditionMaps(item)) yield return (owner, map);
            }
        if (adult is not null)
            foreach (var item in adult.Groups.SelectMany(g => g.Items))
            {
                string owner = $"adult.yaml {item.Id}";
                foreach (var map in AdultConditionMaps(item)) yield return (owner, map);
            }
    }

    static IEnumerable<Dictionary<string, object>> AdultConditionMaps(AdultItemDto item)
    {
        IEnumerable<AdultModDto> mods = item.Mods
            .Concat(item.Disclosure?.Mods ?? [])
            .Concat((item.Entries ?? []).SelectMany(e => e.Mods.Concat(e.Intensity?.Mods ?? [])))
            .Concat((item.Goals ?? []).SelectMany(g => g.Mods))
            .Concat((item.Dealbreakers ?? []).SelectMany(d => d.Hardness.Mods));
        foreach (var m in mods) yield return m.When;
        foreach (var r in item.Rules ?? []) if (r.When is not null) yield return r.When;
        foreach (var o in item.Options ?? [])
        {
            if (o.Requires is not null) yield return o.Requires;
            foreach (var m in o.Mods) yield return m.When;
        }
        foreach (var g in item.Goals ?? []) if (g.Achieved is not null) yield return g.Achieved;
        foreach (var i in item.Issues ?? []) foreach (var stake in i.Stakes) yield return stake.When;
        foreach (var d in item.Dealbreakers ?? []) yield return d.When;
    }

    static IEnumerable<Dictionary<string, object>> AdultWeightMaps(AdultFile? adult) =>
        adult is null ? [] : adult.Groups.SelectMany(g => g.Items).SelectMany(AdultWeightMaps);

    static IEnumerable<Dictionary<string, object>> AdultWeightMaps(AdultItemDto item)
    {
        var maps = new List<Dictionary<string, object>?> { item.Weights, item.Disclosure?.Weights };
        maps.AddRange((item.Entries ?? []).SelectMany(e => new[] { e.Weights, e.Intensity?.Weights }));
        maps.AddRange((item.Goals ?? []).SelectMany(g => new[] { g.Weights, g.DeadlineWeights }));
        maps.AddRange((item.Issues ?? []).Select(i => i.Axes));
        maps.AddRange((item.Dealbreakers ?? []).Select(d => d.Hardness.Weights));
        maps.AddRange((item.Options ?? []).Select(o => o.Weights));
        return maps.OfType<Dictionary<string, object>>();
    }

    /// <summary>Fields that hold a single fact key.</summary>
    static IEnumerable<string> AdultKeyFields(AdultItemDto item) =>
        (item.Inherit ?? []).Concat(new[] { item.Stated, item.Consistency }.OfType<string>());

    static IEnumerable<Dictionary<string, object>> LifeConditionMaps(LifeItemDto item)
    {
        if (item.Requires is not null) yield return item.Requires;
        if (item.While is not null) yield return item.While;
        foreach (var mod in item.Mods) yield return mod.When;
        foreach (var rule in (item.InitialRules ?? []).Concat(item.Rules ?? []))
            if (rule.When is not null) yield return rule.When;
        foreach (var rate in new[] { item.Rate, item.Up, item.Down })
            if (rate is not null)
                foreach (var mod in rate.Mods) yield return mod.When;
        foreach (var choice in Choices(item))
        {
            if (choice.Requires is not null) yield return choice.Requires;
            foreach (var mod in choice.Mods) yield return mod.When;
        }
    }

    static IEnumerable<Dictionary<string, object>> LifeWeightMaps(LifeFile? life) =>
        life is null ? [] : life.Items.SelectMany(LifeWeightMaps);

    static IEnumerable<Dictionary<string, object>> LifeWeightMaps(LifeItemDto item)
    {
        foreach (var rate in new[] { item.Rate, item.Up, item.Down })
            if (rate?.Weights is not null) yield return rate.Weights;
        foreach (var choice in Choices(item))
            if (choice.Weights is not null) yield return choice.Weights;
    }

    static IEnumerable<ChoiceDto> Choices(LifeItemDto item) =>
        (item.Outcomes ?? []).Concat(item.Readings ?? []).Concat(item.Options ?? []);

    static void RenameKeys(FamilyFile family, LifeFile? life, AdultFile? adult, string oldKey, string newKey)
    {
        foreach (var (_, conditions) in AllConditions(family, life, adult)) RenameKey(conditions, oldKey, newKey);
        foreach (var weights in LifeWeightMaps(life).Concat(AdultWeightMaps(adult))) RenameKey(weights, oldKey, newKey);
        if (life is not null)
            foreach (var item in life.Items)
                if (item.Initial == oldKey) item.Initial = newKey;
        if (adult is not null)
            foreach (var item in adult.Groups.SelectMany(g => g.Items))
            {
                if (item.Inherit is { } inherit)
                    for (int i = 0; i < inherit.Count; i++) if (inherit[i] == oldKey) inherit[i] = newKey;
                if (item.Stated == oldKey) item.Stated = newKey;
                if (item.Consistency == oldKey) item.Consistency = newKey;
            }
    }

    /// <summary>Rename a dictionary key in place, keeping the entry order.</summary>
    static void RenameKey<T>(Dictionary<string, T> map, string oldKey, string newKey)
    {
        if (!map.ContainsKey(oldKey)) return;
        var entries = map.ToList();
        map.Clear();
        foreach (var (k, v) in entries) map[k == oldKey ? newKey : k] = v;
    }

    static object RenameInSpec(object spec, string oldValue, string newValue) => spec switch
    {
        List<object> list => list.Select(o => (object)(o?.ToString() == oldValue ? newValue : o?.ToString() ?? "")).ToList(),
        string s when s == oldValue => newValue,
        string s when s == "!" + oldValue => "!" + newValue,
        _ => spec,
    };
}
