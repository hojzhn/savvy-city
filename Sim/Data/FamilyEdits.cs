namespace YouWillDate.Sim.Data;

/// <summary>
/// Edits to the data files that have to touch more than one place, such as
/// renames that must update every condition referring to the renamed thing.
/// All methods change the objects in place; callers work on copies.
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

    public static void RenameItem(FamilyFile family, GroupDto group, ItemDto item, string newId)
    {
        if (newId == item.Id) return;
        string old = item.Id;
        item.Id = newId;
        if (group.PerParent)
        {
            foreach (var prefix in new[] { "self.", "mother.", "father.", "any_parent.", "parents.same." })
                RenameConditionKeys(family, prefix + old, prefix + newId);
        }
        else
        {
            RenameConditionKeys(family, $"{group.Name}.{old}", $"{group.Name}.{newId}");
        }
    }

    public static void RenameGroup(FamilyFile family, GroupDto group, string newName)
    {
        if (newName == group.Name) return;
        string old = group.Name;
        group.Name = newName;
        if (group.PerParent) return; // per-parent keys use mother./father., not the group name
        foreach (var item in group.Items)
            RenameConditionKeys(family, $"{old}.{item.Id}", $"{newName}.{item.Id}");
    }

    /// <summary>Rename a value in an item and in every condition that tests that item.</summary>
    public static void RenameValue(FamilyFile family, GroupDto group, ItemDto item, string oldValue, string newValue)
    {
        if (oldValue == newValue) return;
        int i = item.Values.IndexOf(oldValue);
        if (i < 0) return;
        item.Values[i] = newValue;
        if (item.Weights is not null) item.Weights = RenameKey(item.Weights, oldValue, newValue);
        if (item.Default == oldValue) item.Default = newValue;
        foreach (var mod in item.Mods) mod.Mul = RenameKey(mod.Mul, oldValue, newValue);

        string target = ItemKey(group, item);
        foreach (var (owner, conditions) in AllConditions(family))
            foreach (var key in conditions.Keys.ToList())
                if (GameData.ConditionSchemaKey(key) == target)
                    conditions[key] = RenameInSpec(conditions[key], oldValue, newValue);
    }

    /// <summary>Remove a value from an item, along with its weight and every mod factor for it.</summary>
    public static void RemoveValue(ItemDto item, string value)
    {
        item.Values.Remove(value);
        item.Weights?.Remove(value);
        foreach (var mod in item.Mods) mod.Mul.Remove(value);
        if (item.Default == value) item.Default = item.Values.FirstOrDefault();
    }

    public static void RenameTemperamentTrait(Config config, FamilyFile family, string oldName, string newName)
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
        RenameConditionKeys(family, $"pawn.{oldName}", $"pawn.{newName}");
    }

    /// <summary>Condition keys anywhere in the file that read the given schema key, as "item: key".</summary>
    public static List<string> ReferencesTo(FamilyFile family, string schemaKey)
    {
        var found = new List<string>();
        foreach (var (owner, conditions) in AllConditions(family))
            foreach (var key in conditions.Keys)
                if (GameData.ConditionSchemaKey(key) == schemaKey || (key.StartsWith("parents.same.") && "parent." + key[13..] == schemaKey))
                    found.Add($"{ItemKey(owner.Group, owner.Item)}: {key}");
        return found.Distinct().ToList();
    }

    // ---------- helpers ----------

    record struct Owner(GroupDto Group, ItemDto Item);

    static IEnumerable<(Owner Owner, Dictionary<string, object> Conditions)> AllConditions(FamilyFile family)
    {
        foreach (var g in family.Groups)
            foreach (var item in g.Items)
            {
                var owner = new Owner(g, item);
                if (item.OnlyIf is not null) yield return (owner, item.OnlyIf);
                foreach (var mod in item.Mods) yield return (owner, mod.When);
            }
    }

    static void RenameConditionKeys(FamilyFile family, string oldKey, string newKey)
    {
        foreach (var g in family.Groups)
            foreach (var item in g.Items)
            {
                if (item.OnlyIf is not null) item.OnlyIf = RenameKey(item.OnlyIf, oldKey, newKey);
                foreach (var mod in item.Mods) mod.When = RenameKey(mod.When, oldKey, newKey);
            }
    }

    /// <summary>Rename a dictionary key, keeping the entry order.</summary>
    static Dictionary<string, T> RenameKey<T>(Dictionary<string, T> map, string oldKey, string newKey)
    {
        if (!map.ContainsKey(oldKey)) return map;
        var result = new Dictionary<string, T>();
        foreach (var (k, v) in map) result[k == oldKey ? newKey : k] = v;
        return result;
    }

    static object RenameInSpec(object spec, string oldValue, string newValue) => spec switch
    {
        List<object> list => list.Select(o => (object)(o?.ToString() == oldValue ? newValue : o?.ToString() ?? "")).ToList(),
        string s when s == oldValue => newValue,
        string s when s == "!" + oldValue => "!" + newValue,
        _ => spec,
    };
}
