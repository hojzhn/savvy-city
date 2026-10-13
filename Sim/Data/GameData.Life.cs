using System.Globalization;
using System.Text.RegularExpressions;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Data;

// Loading and checking life.yaml (spec section 4).
public sealed partial class GameData
{
    /// <summary>The compiled life simulation, or null when there is no life.yaml.</summary>
    public LifeTables? Life { get; }

    static readonly Regex LifeId = new("^[a-z0-9_]+$");
    static readonly string[] ReservedState = ["location", "lives_in_current_city"];

    /// <summary>Facts written by the life simulation; they can't be overridden.</summary>
    public static bool IsLifeKey(string key) =>
        key.StartsWith("state.") || key.StartsWith("channel.") || key.StartsWith("place.") || key.StartsWith("life.")
        || key is "age" or "cal_year" or "stage";

    /// <summary>A base rate for a calendar year: the place's override, else the latest eras.yaml entry that sets it.</summary>
    public double BaseRate(string name, int calendarYear, LocationPeriod? place)
    {
        if (place?.Rates is { } local && local.TryGetValue(name, out var r)) return r;
        for (int i = ErasSource.BaseRates.Count - 1; i >= 0; i--)
        {
            var entry = ErasSource.BaseRates[i];
            if (entry.From <= calendarYear && entry.Rates.TryGetValue(name, out var v)) return v;
        }
        throw new InvalidOperationException($"eras.yaml has no base rate '{name}' for {calendarYear}");
    }

    LifeTables CompileLife(LifeFile f)
    {
        var stages = CompileStages(f);
        var channels = f.Channels.ToArray();
        if (channels.Distinct().Count() != channels.Length) throw new DataException("life.yaml: duplicate channels");
        foreach (var c in channels)
            if (!LifeId.IsMatch(c)) throw new DataException($"life.yaml: channel '{c}' must use a-z, 0-9 and _");

        for (int i = 1; i < ErasSource.BaseRates.Count; i++)
            if (ErasSource.BaseRates[i].From <= ErasSource.BaseRates[i - 1].From)
                throw new DataException("eras.yaml: base_rates must be in increasing year order");

        // Declare every life fact first: an item may read any state, because
        // what it reads is "as of now" (items above it this year, the rest as of last year).
        _schema["age"] = null;
        _schema["cal_year"] = null;
        _schema["stage"] = stages.Select(s => s.Id).ToHashSet();
        var locationIds = Locations.Select(l => l.Id).ToHashSet();
        _schema["place.location"] = locationIds;
        foreach (var (k, v) in ContextSchema.Values) _schema[$"place.{k}"] = v.ToHashSet();
        _schema["state.location"] = locationIds;
        _schema["state.lives_in_current_city"] = ["yes", "no"];
        foreach (var c in channels) _schema[$"channel.{c}"] = null;

        var ids = new HashSet<string>();
        foreach (var d in f.Items)
        {
            string where = $"life.yaml: {d.Id}";
            if (!LifeId.IsMatch(d.Id)) throw new DataException($"{where}: ids use a-z, 0-9 and _");
            if (!ids.Add(d.Id)) throw new DataException($"{where}: defined twice");
            if (d.Type != "condition") continue;
            if (ReservedState.Contains(d.Id)) throw new DataException($"{where}: '{d.Id}' is built in");
            var kind = ParseKind(d, where);
            if (kind is ConditionKind.Number or ConditionKind.Counter)
            {
                if (d.Values.Count > 0) throw new DataException($"{where}: {d.Kind} conditions are numeric and take no values");
                _schema[$"state.{d.Id}"] = null;
            }
            else
            {
                if (d.Values.Count == 0) throw new DataException($"{where}: needs values");
                if (d.Values.Distinct().Count() != d.Values.Count) throw new DataException($"{where}: duplicate values");
                _schema[$"state.{d.Id}"] = d.Values.ToHashSet();
            }
        }

        var items = f.Items.Select(d => CompileLifeItem(d, stages, channels)).ToArray();
        // Summaries of events and forks are written at the end of the simulation:
        // later steps (adult traits) can read them, life items can't.
        foreach (var item in items.Where(i => i.Type != LifeItemType.Condition))
            _schema[$"life.{item.Id}"] = item.SummaryValues.ToHashSet();
        return new LifeTables
        {
            Stages = stages, Channels = channels, Items = items,
            ReadingTemperature = f.ReadingTemperature > 0 ? f.ReadingTemperature
                : throw new DataException("life.yaml: reading_temperature must be above 0"),
        };
    }

    Stage[] CompileStages(LifeFile f)
    {
        if (f.Stages.Count == 0) throw new DataException("life.yaml: no stages");
        var stages = f.Stages.Select(s => new Stage(s.Id, s.From, s.To, s.Weight)).ToArray();
        if (stages[0].From != 0) throw new DataException("life.yaml: the first stage must start at age 0");
        for (int i = 0; i < stages.Length; i++)
        {
            var s = stages[i];
            if (!LifeId.IsMatch(s.Id)) throw new DataException($"life.yaml: stage id '{s.Id}' must use a-z, 0-9 and _");
            if (s.To < s.From) throw new DataException($"life.yaml: stage '{s.Id}' ends before it starts");
            if (i > 0 && s.From != stages[i - 1].To + 1)
                throw new DataException($"life.yaml: stage '{s.Id}' must start the year after '{stages[i - 1].Id}' ends");
            if (s.Weight < 0) throw new DataException($"life.yaml: stage '{s.Id}' weight is negative");
        }
        if (stages.Select(s => s.Id).Distinct().Count() != stages.Length) throw new DataException("life.yaml: duplicate stage ids");
        if (stages[^1].To < Config.AgeMax)
            throw new DataException($"life.yaml: the last stage must run to at least age_max ({Config.AgeMax})");
        return stages;
    }

    static ConditionKind ParseKind(LifeItemDto d, string where) => d.Kind switch
    {
        "state" => ConditionKind.State,
        "number" => ConditionKind.Number,
        "roll" => ConditionKind.Roll,
        "drift" => ConditionKind.Drift,
        "derive" => ConditionKind.Derive,
        "counter" => ConditionKind.Counter,
        _ => throw new DataException($"{where}: kind must be state, number, roll, drift, derive or counter"),
    };

    LifeItem CompileLifeItem(LifeItemDto d, Stage[] stages, string[] channels)
    {
        string where = $"life.yaml: {d.Id}";
        var type = d.Type switch
        {
            "condition" => LifeItemType.Condition,
            "event" => LifeItemType.Event,
            "fork" => LifeItemType.Fork,
            _ => throw new DataException($"{where}: type must be condition, event or fork"),
        };

        // Schedule
        int from = 0, to = int.MaxValue;
        if (d.Window.Count is 1 or 2)
        {
            from = StageById(d.Window[0]).From;
            to = StageById(d.Window[^1]).To;
        }
        else if (d.Window.Count > 2) throw new DataException($"{where}: window is [stage] or [first_stage, last_stage]");
        if (d.FromAge is int fa) from = Math.Max(from, fa);
        if (d.ToAge is int ta) to = Math.Min(to, ta);
        if (to < from) throw new DataException($"{where}: the window is empty");
        if (d.Ages is { } ages && ages.Any(a => a < 0)) throw new DataException($"{where}: ages can't be negative");

        Frequency freq = d.Frequency switch
        {
            null when type == LifeItemType.Fork && d.Ages is null =>
                throw new DataException($"{where}: a fork needs ages or a frequency"),
            null => d.Kind is "drift" or "roll" ? Frequency.PerStage : Frequency.PerYear,
            "once" => Frequency.Once,
            "per_stage" => Frequency.PerStage,
            "per_year" => Frequency.PerYear,
            _ => throw new DataException($"{where}: frequency must be once, per_stage or per_year"),
        };
        int max = d.Max < 0 ? throw new DataException($"{where}: max can't be negative")
            : freq == Frequency.Once && d.Max == 0 ? 1 : d.Max;

        var pseudo = new GroupDto { Name = "state" };
        Condition[] Conds(Dictionary<string, object>? m, string w) => ParseConditions(m, pseudo, w);
        var requires = Conds(d.Requires, where + " requires");

        Stage StageById(string id) => stages.FirstOrDefault(s => s.Id == id)
            ?? throw new DataException($"{where}: unknown stage '{id}'; stages are {string.Join(", ", stages.Select(s => s.Id))}");

        string[] summary = type switch
        {
            LifeItemType.Event => LifeTables.EventSummaryValues,
            LifeItemType.Fork => (d.Options ?? []).Select(o => o.Id).Append("not_reached").ToArray(),
            _ => [],
        };

        if (type == LifeItemType.Condition)
            return CompileCondition(d, where, requires, from, to, freq, max, pseudo);

        if (d.Values.Count > 0 || d.Initial is not null || d.Kind is not null)
            throw new DataException($"{where}: values, initial and kind are for conditions only");

        if (type == LifeItemType.Event)
        {
            if (d.Rate is null) throw new DataException($"{where}: an event needs a rate");
            return new LifeItem
            {
                Id = d.Id, Note = d.Note, Type = type, WindowFrom = from, WindowTo = to, Ages = d.Ages?.ToArray(),
                Frequency = freq, Max = max, MustHappen = d.MustHappen, Requires = requires,
                Rate = CompileRate(d.Rate, where + " rate", pseudo),
                Outcomes = CompileChoices(d.Outcomes, where + " outcome", pseudo, channels),
                Effects = CompileEffects(d.Effects, where + " effects", channels),
                Readings = CompileChoices(d.Readings, where + " reading", pseudo, channels),
                Negative = d.Valence switch
                {
                    null or "negative" => true,
                    "positive" => false,
                    _ => throw new DataException($"{where}: valence must be negative or positive"),
                },
                Tags = d.Tags?.ToArray() ?? [],
                SummaryValues = summary,
            };
        }

        // Fork
        if (d.Options is null || d.Options.Count == 0) throw new DataException($"{where}: a fork needs options");
        if (d.StickyKey is not null && !(d.StickyKey.StartsWith("state.") && _schema.TryGetValue(d.StickyKey, out var sv) && sv is not null))
            throw new DataException($"{where}: sticky_key must be a categorical state, like state.career");
        double temperature = d.Temperature ?? LifeSource!.ForkTemperature;
        if (temperature <= 0) throw new DataException($"{where}: temperature must be above 0");
        if (d.Rate is not null || d.Outcomes is not null || d.Readings is not null)
            throw new DataException($"{where}: forks use options, not rate/outcomes/readings");
        return new LifeItem
        {
            Id = d.Id, Note = d.Note, Type = type, WindowFrom = from, WindowTo = to, Ages = d.Ages?.ToArray(),
            Frequency = freq, Max = max, Requires = requires,
            Options = CompileChoices(d.Options, where + " option", pseudo, channels),
            Effects = CompileEffects(d.Effects, where + " effects", channels),
            Temperature = temperature,
            StickyKey = d.StickyKey, StickyBonus = d.StickyBonus, StickyExcept = d.StickyExcept?.ToArray() ?? [],
            Tags = d.Tags?.ToArray() ?? [],
            Negative = d.Valence == "negative",
            SummaryValues = summary,
        };
    }

    LifeItem CompileCondition(LifeItemDto d, string where, Condition[] requires, int from, int to,
                              Frequency freq, int max, GroupDto pseudo)
    {
        var kind = ParseKind(d, where);
        string key = $"state.{d.Id}";
        var allowed = _schema[key];
        bool numeric = allowed is null;

        // Starting value
        if (d.Initial is not null)
        {
            string initKey = CanonicalKey(d.Initial, false, where);
            if (!_schema.TryGetValue(initKey, out var initAllowed))
                throw new DataException($"{where}: initial '{d.Initial}' is not a known fact");
            if (numeric != (initAllowed is null))
                throw new DataException($"{where}: initial '{d.Initial}' must be {(numeric ? "numeric" : "categorical")} like this condition");
            if (!numeric)
                foreach (var v in initAllowed!)
                    if (!allowed!.Contains(v))
                        throw new DataException($"{where}: initial '{d.Initial}' can be '{v}', which is not one of this condition's values");
        }
        if (d.InitialValue is not null)
        {
            if (numeric) ParseNumber(d.InitialValue, where + " initial_value");
            else if (!allowed!.Contains(d.InitialValue))
                throw new DataException($"{where}: initial_value '{d.InitialValue}' is not one of its values");
        }
        var initialRules = CompileRules(d.InitialRules, where + " initial_rules", pseudo, allowed);

        TableItem? table = null;
        Rate? up = null, down = null;
        DeriveRule[] rules = [];
        Condition[] whileConds = [];

        switch (kind)
        {
            case ConditionKind.Roll:
                if (d.Requires is not null && d.Default is null)
                    throw new DataException($"{where}: a roll with requires needs a default (used when requires fails)");
                table = CompileItem(pseudo, new ItemDto
                {
                    Id = d.Id, Values = d.Values, Weights = d.Weights, Mods = d.Mods,
                    OnlyIf = d.Requires, Default = d.Default,
                }, "life.yaml");
                break;
            case ConditionKind.Drift:
                if (d.Values.Count < 2) throw new DataException($"{where}: drift needs at least two ordered values");
                if (d.Up is null && d.Down is null) throw new DataException($"{where}: drift needs up and/or down rates");
                if (d.Initial is null && d.InitialValue is null && d.InitialRules is null)
                    throw new DataException($"{where}: drift needs a starting value");
                up = d.Up is null ? null : CompileRate(d.Up, where + " up", pseudo);
                down = d.Down is null ? null : CompileRate(d.Down, where + " down", pseudo);
                break;
            case ConditionKind.Derive:
                if (d.Rules is null || d.Rules.Count == 0) throw new DataException($"{where}: derive needs rules");
                rules = CompileRules(d.Rules, where + " rules", pseudo, allowed);
                break;
            case ConditionKind.Counter:
                whileConds = ParseConditions(d.While, pseudo, where + " while");
                break;
        }
        if (kind != ConditionKind.Roll && (d.Weights is not null || d.Mods.Count > 0))
            throw new DataException($"{where}: weights and mods are for roll conditions");

        return new LifeItem
        {
            Id = d.Id, Note = d.Note, Type = LifeItemType.Condition, Kind = kind,
            Values = d.Values.ToArray(), WindowFrom = from, WindowTo = to, Ages = d.Ages?.ToArray(),
            Frequency = freq, Max = max, Requires = kind == ConditionKind.Roll ? [] : requires,
            Initial = d.Initial, InitialValue = d.InitialValue, InitialRules = initialRules,
            Table = table, Up = up, Down = down, Rules = rules, While = whileConds, Log = d.Log,
        };
    }

    DeriveRule[] CompileRules(List<RuleDto>? rules, string where, GroupDto pseudo, HashSet<string>? allowed)
    {
        if (rules is null) return [];
        if (allowed is null) throw new DataException($"{where}: rules are for categorical conditions");
        return rules.Select((r, i) =>
        {
            if (!allowed.Contains(r.Value))
                throw new DataException($"{where} {i + 1}: value '{r.Value}' is not one of the condition's values");
            return new DeriveRule(ParseConditions(r.When, pseudo, $"{where} {i + 1}"), r.Value);
        }).ToArray();
    }

    Rate CompileRate(RateDto r, string where, GroupDto pseudo)
    {
        double? value = null;
        string? key = null;
        if (r.Base.StartsWith("rate."))
        {
            key = r.Base[5..];
            var first = ErasSource.BaseRates.FirstOrDefault();
            if (first is null || first.From > MinBirthYear || !ErasSource.BaseRates.Any(e => e.From <= MinBirthYear && e.Rates.ContainsKey(key)))
                throw new DataException($"{where}: base '{r.Base}' needs an eras.yaml base_rates entry from {MinBirthYear} or earlier that sets '{key}'");
        }
        else
        {
            value = ParseNumber(r.Base, where + " base");
            if (value is <= 0 or > 1) throw new DataException($"{where}: base must be a probability above 0 and at most 1");
        }
        return new Rate
        {
            BaseValue = value, BaseKey = key,
            Terms = CompileTerms(r.Weights, where),
            Mods = CompileAddMods(r.Mods, where, pseudo),
        };
    }

    AddMod[] CompileAddMods(List<AddModDto> mods, string where, GroupDto pseudo) =>
        mods.Select((m, i) =>
        {
            if (m.When.Count == 0) throw new DataException($"{where} mod {i + 1}: when is empty");
            return new AddMod(i + 1, ParseConditions(m.When, pseudo, $"{where} mod {i + 1}"), m.Add, m.Note);
        }).ToArray();

    Term[] CompileTerms(Dictionary<string, object>? weights, string where)
    {
        if (weights is null) return [];
        var temperament = Config.Temperament.Traits.Select(t => $"pawn.{t}").ToHashSet();
        return weights.Select(kv =>
        {
            string schemaKey = CanonicalKey(kv.Key, false, where);
            if (!_schema.TryGetValue(schemaKey, out var allowed))
                throw new DataException($"{where}: weight on '{kv.Key}', which is not a known fact");

            switch (kv.Value)
            {
                case string s:
                    if (allowed is not null)
                        throw new DataException($"{where}: '{kv.Key}' is categorical; give weights per value, like {{ {allowed.First()}: 0.5 }}");
                    return new Term { Key = kv.Key, Weight = ParseNumber(s, $"{where} weight on {kv.Key}"), Centered = temperament.Contains(kv.Key) };
                case Dictionary<object, object> map:
                    if (allowed is null)
                        throw new DataException($"{where}: '{kv.Key}' is numeric; give one number");
                    var values = new Dictionary<string, double>();
                    foreach (var (v, w) in map)
                    {
                        string value = v.ToString() ?? "";
                        if (!allowed.Contains(value))
                            throw new DataException($"{where}: '{kv.Key}' has no value '{value}'; allowed: {string.Join(", ", allowed)}");
                        values[value] = ParseNumber(w?.ToString() ?? "", $"{where} weight on {kv.Key}={value}");
                    }
                    return new Term { Key = kv.Key, ValueWeights = values };
                default:
                    throw new DataException($"{where}: weight on '{kv.Key}' must be a number or a map of values to numbers");
            }
        }).ToArray();
    }

    Choice[] CompileChoices(List<ChoiceDto>? choices, string where, GroupDto pseudo, string[] channels)
    {
        if (choices is null) return [];
        var seen = new HashSet<string>();
        return choices.Select(c =>
        {
            string w = $"{where} '{c.Id}'";
            if (!LifeId.IsMatch(c.Id)) throw new DataException($"{w}: ids use a-z, 0-9 and _");
            if (!seen.Add(c.Id)) throw new DataException($"{w}: defined twice");
            if (c.Weight < 0) throw new DataException($"{w}: weight is negative");
            return new Choice
            {
                Id = c.Id, Note = c.Note, Weight = c.Weight,
                Terms = CompileTerms(c.Weights, w),
                Mods = CompileAddMods(c.Mods, w, pseudo),
                Requires = ParseConditions(c.Requires, pseudo, w + " requires"),
                Effects = CompileEffects(c.Effects, w + " effects", channels),
            };
        }).ToArray();
    }

    Effects CompileEffects(EffectsDto? e, string where, string[] channels)
    {
        if (e is null) return Effects.None;

        string NumericState(string key)
        {
            if (!key.StartsWith("state.") || !_schema.TryGetValue(key, out var a) || a is not null)
                throw new DataException($"{where}: '{key}' is not a numeric state (number or counter condition)");
            return key;
        }

        var set = (e.Set ?? new()).Select(kv =>
        {
            if (!kv.Key.StartsWith("state.") || !_schema.TryGetValue(kv.Key, out var allowed))
                throw new DataException($"{where}: can only set state.<condition>, not '{kv.Key}'");
            if (allowed is null) throw new DataException($"{where}: '{kv.Key}' is numeric; use add or reset");
            if (kv.Value == "$current_city")
            {
                if (kv.Key != "state.location") throw new DataException($"{where}: $current_city only works for state.location");
            }
            else if (!allowed.Contains(kv.Value))
                throw new DataException($"{where}: '{kv.Key}' has no value '{kv.Value}'; allowed: {string.Join(", ", allowed)}");
            return (kv.Key, kv.Value);
        }).ToArray();

        return new Effects
        {
            Set = set,
            Add = (e.Add ?? new()).Select(kv => (NumericState(kv.Key), kv.Value)).ToArray(),
            Reset = (e.Reset ?? []).Select(NumericState).ToArray(),
            RecordFirstAge = (e.RecordFirstAge ?? []).Select(NumericState).ToArray(),
            Channels = (e.Channels ?? new()).Select(kv => channels.Contains(kv.Key)
                ? (kv.Key, kv.Value)
                : throw new DataException($"{where}: unknown channel '{kv.Key}'; channels: {string.Join(", ", channels)}")).ToArray(),
        };
    }

    static double ParseNumber(string s, string where) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new DataException($"{where}: '{s}' is not a number");
}
