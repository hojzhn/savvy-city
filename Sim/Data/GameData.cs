namespace YouWillDate.Sim.Data;

/// <summary>
/// All data tables, loaded and checked. Loading fails with a <see cref="DataException"/>
/// naming the file and entry when anything is inconsistent, so bad data never
/// reaches the generator.
/// </summary>
public sealed class GameData
{
    public Config Config { get; }
    public IReadOnlyList<Location> Locations { get; }
    public IReadOnlyList<EraEntry> Eras { get; }
    public IReadOnlyList<TableGroup> Family { get; }

    /// <summary>
    /// Every fact the tables can produce, mapped to its allowed values (null for numeric).
    /// Per-parent facts appear once as "parent.&lt;id&gt;".
    /// </summary>
    public IReadOnlyDictionary<string, HashSet<string>?> Schema => _schema;

    /// <summary>Lower-triangular factor of the temperament correlation matrix.</summary>
    internal double[,] TemperamentCholesky { get; }

    public int MinBirthYear => Config.PresentYear - Config.AgeMax;
    public int MaxBirthYear => Config.PresentYear - Config.AgeMin;

    readonly Dictionary<string, HashSet<string>?> _schema = new();

    public static GameData Load(string dataDir)
    {
        string Read(string file) => File.ReadAllText(Path.Combine(dataDir, file));
        return FromYaml(Read("config.yaml"), Read("locations.yaml"), Read("eras.yaml"), Read("family.yaml"));
    }

    public static GameData FromYaml(string config, string locations, string eras, string family) =>
        new(YamlIO.Parse<Config>("config.yaml", config),
            YamlIO.Parse<LocationsFile>("locations.yaml", locations),
            YamlIO.Parse<ErasFile>("eras.yaml", eras),
            YamlIO.Parse<FamilyFile>("family.yaml", family));

    /// <summary>Build from already-parsed files, such as edited copies. The objects are kept, not copied.</summary>
    public static GameData FromFiles(Config config, LocationsFile locations, ErasFile eras, FamilyFile family) =>
        new(config, locations, eras, family);

    // The parsed files this data came from, for editors to copy and change.
    public LocationsFile LocationsSource { get; }
    public ErasFile ErasSource { get; }
    public FamilyFile FamilySource { get; }

    GameData(Config config, LocationsFile locations, ErasFile eras, FamilyFile family)
    {
        Config = config;
        LocationsSource = locations;
        ErasSource = eras;
        FamilySource = family;
        Locations = locations.Locations;
        Eras = eras.EconomicEras;

        ValidateConfig();
        TemperamentCholesky = BuildCholesky();
        ValidateEras();
        ValidateLocations();
        DeclareBuiltInFacts();
        Family = family.Groups.Select(CompileGroup).ToList();
    }

    public string EconomicEra(int year, LocationPeriod period) =>
        period.EconomicEra ?? Eras.Last(e => e.From <= year).Era;

    public Location LocationById(string id) =>
        Locations.FirstOrDefault(l => l.Id == id) ?? throw new KeyNotFoundException($"no location '{id}'");

    /// <summary>Schema entry for a fact key: "mother.education" maps to "parent.education".</summary>
    public static string SchemaKey(string factKey) =>
        factKey.StartsWith("mother.") || factKey.StartsWith("father.")
            ? "parent." + factKey[(factKey.IndexOf('.') + 1)..]
            : factKey;

    /// <summary>
    /// Schema entry a condition key reads: "self.x", "mother.x", "father.x" and
    /// "any_parent.x" all read "parent.x".
    /// </summary>
    public static string ConditionSchemaKey(string conditionKey)
    {
        foreach (var prefix in new[] { "self.", "mother.", "father.", "any_parent." })
            if (conditionKey.StartsWith(prefix))
                return "parent." + conditionKey[prefix.Length..];
        return conditionKey;
    }

    /// <summary>Allowed values for a categorical fact, or null for a numeric one.</summary>
    public IReadOnlyCollection<string>? AllowedValues(string factKey) =>
        _schema.TryGetValue(SchemaKey(factKey), out var v)
            ? v
            : throw new KeyNotFoundException($"unknown fact '{factKey}'");

    /// <summary>Throws an <see cref="ArgumentException"/> if the overrides name unknown facts or invalid values.</summary>
    public void CheckOverrides(Facts overrides)
    {
        foreach (var (key, value) in overrides.Categorical)
        {
            if (key.EndsWith(".role") || key.StartsWith("parents.same."))
                throw new ArgumentException($"'{key}' is derived and can't be overridden");
            if (!_schema.TryGetValue(SchemaKey(key), out var allowed))
                throw new ArgumentException($"unknown fact '{key}'");
            if (allowed is null)
                throw new ArgumentException($"'{key}' is numeric; override it with a number");
            if (!allowed.Contains(value))
                throw new ArgumentException($"'{key}' has no value '{value}'; allowed: {string.Join(", ", allowed)}");
        }
        foreach (var (key, value) in overrides.Numeric)
        {
            if (!_schema.TryGetValue(key, out var allowed))
                throw new ArgumentException($"unknown fact '{key}'");
            if (allowed is not null)
                throw new ArgumentException($"'{key}' is categorical; override it with one of: {string.Join(", ", allowed)}");
            if (key == "year" && (value < MinBirthYear || value > MaxBirthYear || value != Math.Floor(value)))
                throw new ArgumentException($"year must be a whole number from {MinBirthYear} to {MaxBirthYear}");
            if (key.StartsWith("pawn.") && value is < 0 or > 1)
                throw new ArgumentException($"'{key}' must be between 0 and 1");
        }
    }

    // ---------- config, eras, locations ----------

    void ValidateConfig()
    {
        var c = Config;
        if (c.AgeMin <= 0 || c.AgeMax < c.AgeMin)
            throw new DataException("config.yaml: age_min and age_max must satisfy 0 < age_min <= age_max");
        if (c.MaleBirthRate is <= 0 or >= 1)
            throw new DataException("config.yaml: male_birth_rate must be between 0 and 1");
    }

    double[,] BuildCholesky()
    {
        var traits = Config.Temperament.Traits;
        if (traits.Count == 0)
            throw new DataException("config.yaml: temperament.traits is empty");
        if (traits.Distinct().Count() != traits.Count)
            throw new DataException("config.yaml: temperament.traits has duplicates");

        int n = traits.Count;
        var m = new double[n, n];
        for (int i = 0; i < n; i++) m[i, i] = 1;

        foreach (var c in Config.Temperament.Correlations)
        {
            int a = traits.IndexOf(c.A), b = traits.IndexOf(c.B);
            if (a < 0 || b < 0 || a == b)
                throw new DataException($"config.yaml: correlation {c.A}/{c.B} must name two different traits from: {string.Join(", ", traits)}");
            if (c.R is <= -1 or >= 1)
                throw new DataException($"config.yaml: correlation {c.A}/{c.B} must be between -1 and 1");
            m[a, b] = m[b, a] = c.R;
        }

        var l = new double[n, n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j <= i; j++)
        {
            double sum = m[i, j];
            for (int k = 0; k < j; k++) sum -= l[i, k] * l[j, k];
            if (i == j)
            {
                if (sum <= 0)
                    throw new DataException("config.yaml: temperament correlations are not a valid correlation matrix");
                l[i, i] = Math.Sqrt(sum);
            }
            else l[i, j] = sum / l[j, j];
        }
        return l;
    }

    void ValidateEras()
    {
        if (Eras.Count == 0 || Eras[0].From > MinBirthYear)
            throw new DataException($"eras.yaml: economic_eras must start at or before {MinBirthYear}");
        for (int i = 0; i < Eras.Count; i++)
        {
            if (i > 0 && Eras[i].From <= Eras[i - 1].From)
                throw new DataException($"eras.yaml: entries must be in increasing year order (at {Eras[i].From})");
            RequireContextValue("eras.yaml", "economic_era", Eras[i].Era, $"year {Eras[i].From}");
        }
    }

    void ValidateLocations()
    {
        if (Locations.Count == 0) throw new DataException("locations.yaml: no locations");
        var ids = new HashSet<string>();
        foreach (var loc in Locations)
        {
            string where = $"location '{loc.Id}'";
            if (!ids.Add(loc.Id)) throw new DataException($"locations.yaml: duplicate id '{loc.Id}'");
            if (loc.Population <= 0) throw new DataException($"locations.yaml: {where} needs population > 0");
            RequireContextValue("locations.yaml", "urbanicity", loc.Urbanicity, where);

            if (loc.Periods.Count == 0 || loc.Periods[0].From > MinBirthYear)
                throw new DataException($"locations.yaml: {where} periods must start at or before {MinBirthYear}");

            for (int i = 0; i < loc.Periods.Count; i++)
            {
                var p = loc.Periods[i];
                string pw = $"{where}, period {p.From}";
                if (i > 0 && p.From <= loc.Periods[i - 1].From)
                    throw new DataException($"locations.yaml: {where} periods must be in increasing year order");
                RequireContextValue("locations.yaml", "local_politics", p.LocalPolitics, pw);
                RequireContextValue("locations.yaml", "diversity", p.Diversity, pw);
                RequireContextValue("locations.yaml", "norms_sex", p.NormsSex, pw);
                RequireContextValue("locations.yaml", "norms_gender", p.NormsGender, pw);
                RequireContextValue("locations.yaml", "norms_family", p.NormsFamily, pw);
                if (p.EconomicEra is not null)
                    RequireContextValue("locations.yaml", "economic_era", p.EconomicEra, pw);
                if (p.Groups.Count == 0 || p.Religions.Count == 0)
                    throw new DataException($"locations.yaml: {pw} needs groups and religions");
            }
        }
    }

    static void RequireContextValue(string file, string field, string value, string where)
    {
        if (!ContextSchema.Values[field].Contains(value))
            throw new DataException($"{file}: {where} has {field} '{value}'; allowed: {string.Join(", ", ContextSchema.Values[field])}");
    }

    // ---------- family tables ----------

    void DeclareBuiltInFacts()
    {
        _schema["year"] = null;
        _schema["pawn.location"] = Locations.Select(l => l.Id).ToHashSet();
        _schema["pawn.sex"] = ContextSchema.Sexes.ToHashSet();
        foreach (var t in Config.Temperament.Traits) _schema[$"pawn.{t}"] = null;
        foreach (var (k, v) in ContextSchema.Values) _schema[$"context.{k}"] = v.ToHashSet();
    }

    TableGroup CompileGroup(GroupDto g)
    {
        if (string.IsNullOrEmpty(g.Name)) throw new DataException("family.yaml: a group has no name");
        if (g.PerParent) _schema["parent.role"] = ["mother", "father"];

        var items = new List<TableItem>();
        foreach (var dto in g.Items)
        {
            var item = CompileItem(g, dto);
            string key = g.PerParent ? $"parent.{item.Id}" : $"{g.Name}.{item.Id}";
            if (_schema.ContainsKey(key))
                throw new DataException($"family.yaml: '{key}' is defined twice");
            // Declared after compiling, so an item can only read items above it.
            _schema[key] = item.Values.ToHashSet();
            items.Add(item);
        }

        if (g.PerParent)
            foreach (var item in items) _schema[$"parents.same.{item.Id}"] = ["yes", "no"];

        return new TableGroup(g.Name, g.PerParent, items);
    }

    TableItem CompileItem(GroupDto g, ItemDto dto)
    {
        string where = $"family.yaml: {g.Name}.{dto.Id}";
        if (string.IsNullOrEmpty(dto.Id)) throw new DataException($"family.yaml: an item in '{g.Name}' has no id");
        if (dto.Values.Count == 0) throw new DataException($"{where}: no values");
        if (dto.Values.Distinct().Count() != dto.Values.Count) throw new DataException($"{where}: duplicate values");

        var values = dto.Values.ToArray();
        int IndexOf(string v, string context) =>
            Array.IndexOf(values, v) is var i and >= 0
                ? i
                : throw new DataException($"{where}: {context} names '{v}', which is not one of its values");

        var weights = Enumerable.Repeat(1.0, values.Length).ToArray();
        if (dto.Weights is not null && dto.BaseFrom is not null)
            throw new DataException($"{where}: use either weights or base_from, not both");
        if (dto.Weights is not null)
            foreach (var (v, w) in dto.Weights)
            {
                if (w < 0) throw new DataException($"{where}: weight for '{v}' is negative");
                weights[IndexOf(v, "weights")] = w;
            }

        if (dto.BaseFrom is not null)
        {
            foreach (var loc in Locations)
            foreach (var p in loc.Periods)
            {
                var shares = p.Shares(dto.BaseFrom)
                    ?? throw new DataException($"{where}: base_from '{dto.BaseFrom}' is unknown; use groups or religions");
                foreach (var v in shares.Keys) IndexOf(v, $"location '{loc.Id}' {dto.BaseFrom}");
            }
        }

        if (dto.Assort != 0 && !g.PerParent)
            throw new DataException($"{where}: assort only applies to per-parent groups");
        if (dto.Assort is < 0 or > 1)
            throw new DataException($"{where}: assort must be between 0 and 1");

        var onlyIf = ParseConditions(dto.OnlyIf, g, where + " only_if");
        if (onlyIf.Length > 0)
        {
            if (dto.Default is null) throw new DataException($"{where}: only_if needs a default");
            IndexOf(dto.Default, "default");
        }

        var mods = dto.Mods.Select((m, i) =>
        {
            string mw = $"{where} mod {i + 1}";
            if (m.When.Count == 0) throw new DataException($"{mw}: when is empty");
            var mul = m.Mul.Select(kv =>
            {
                if (kv.Value < 0) throw new DataException($"{mw}: factor for '{kv.Key}' is negative");
                return (IndexOf(kv.Key, $"mod {i + 1}"), kv.Value);
            }).ToArray();
            return new Modifier(i + 1, ParseConditions(m.When, g, mw), mul);
        }).ToArray();

        return new TableItem
        {
            Id = dto.Id, Values = values, BaseWeights = weights, BaseFrom = dto.BaseFrom,
            OnlyIf = onlyIf, Default = dto.Default, Assort = dto.Assort, Mods = mods,
        };
    }

    Condition[] ParseConditions(Dictionary<string, object>? specs, GroupDto g, string where)
    {
        if (specs is null) return [];
        return specs.Select(kv =>
        {
            Condition c;
            try { c = Condition.Parse(kv.Key, kv.Value); }
            catch (DataException e) { throw new DataException($"{where}: {e.Message}"); }

            string key = CanonicalKey(kv.Key, g.PerParent, where);
            if (!_schema.TryGetValue(key, out var allowed))
                throw new DataException($"{where}: '{kv.Key}' is unknown or not generated yet (items can only read items above them)");
            if (allowed is null && !c.IsNumeric)
                throw new DataException($"{where}: '{kv.Key}' is numeric; use a comparison like \">=0.7\" or \"1990..1999\"");
            if (allowed is not null && c.IsNumeric)
                throw new DataException($"{where}: '{kv.Key}' is categorical; list its values instead of comparing");
            if (allowed is not null)
                foreach (var v in c.Values)
                    if (!allowed.Contains(v))
                        throw new DataException($"{where}: '{kv.Key}' has no value '{v}'; allowed: {string.Join(", ", allowed)}");
            return c;
        }).ToArray();
    }

    static string CanonicalKey(string key, bool perParent, string where)
    {
        static bool Has(string k, string prefix, out string rest)
        {
            rest = k.StartsWith(prefix) ? k[prefix.Length..] : "";
            return rest.Length > 0;
        }

        if (Has(key, "self.", out var r))
            return perParent ? $"parent.{r}" : throw new DataException($"{where}: 'self.' only works inside a per-parent group");
        if (Has(key, "mother.", out r) || Has(key, "father.", out r) || Has(key, "any_parent.", out r))
            return !perParent ? $"parent.{r}" : throw new DataException($"{where}: use 'self.' inside a per-parent group");
        return key;
    }
}
