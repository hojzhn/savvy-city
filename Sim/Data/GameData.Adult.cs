using YouWillDate.Sim.Adult;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Data;

// Loading and checking adult.yaml (spec section 6).
public sealed partial class GameData
{
    /// <summary>The compiled adult profile, or null when there is no adult.yaml.</summary>
    public AdultTables? Adult { get; }
    public AdultFile? AdultSource { get; }

    Lazy<IReadOnlyDictionary<string, (double Mean, double Sd)>>? _adultNorms;

    /// <summary>
    /// Raw mean and SD of each z-scale adult trait, measured on a calibration batch
    /// the first time an adult profile is generated (spec 6.1).
    /// </summary>
    public IReadOnlyDictionary<string, (double Mean, double Sd)> AdultNorms =>
        _adultNorms?.Value ?? throw new InvalidOperationException("no adult.yaml loaded");

    /// <summary>Facts written by the adult profile step; they can't be overridden.</summary>
    public static bool IsAdultKey(string key) =>
        key.StartsWith("adult.") || key.StartsWith("z.") || key.StartsWith("wchannel.") || key.StartsWith("hist.");

    AdultTables CompileAdult(AdultFile f)
    {
        if (Life is null) throw new DataException("adult.yaml: needs life.yaml (adult traits read the life history)");
        if (f.CalibrationSize < 50) throw new DataException("adult.yaml: calibration_size must be at least 50");

        // Inputs the adult step prepares before any item runs.
        foreach (var t in Config.Temperament.Traits) _schema[$"z.{t}"] = null;
        foreach (var c in Life.Channels) _schema[$"wchannel.{c}"] = null;
        _schema["hist.partner_count"] = null;
        _schema["hist.longest_relationship"] = null;
        _schema["hist.moved_as_adult"] = ["yes", "no"];
        _schema["hist.ex_count"] = null;
        _schema["hist.ex_status"] = null;
        _schema["hist.ex_availability"] = null;

        var items = new List<AdultItem>();
        var groups = new List<(string, string, string?)>();
        var ids = new HashSet<string>();
        foreach (var g in f.Groups)
        {
            if (!LifeId.IsMatch(g.Id)) throw new DataException($"adult.yaml: group id '{g.Id}' must use a-z, 0-9 and _");
            groups.Add((g.Id, g.Title ?? g.Id, g.Note));
            foreach (var dto in g.Items)
            {
                string where = $"adult.yaml: {dto.Id}";
                if (!LifeId.IsMatch(dto.Id)) throw new DataException($"{where}: ids use a-z, 0-9 and _");
                if (!ids.Add(dto.Id)) throw new DataException($"{where}: defined twice");
                var item = CompileAdultItem(dto, g, items);
                // Declared after compiling: an item can only read items above it.
                foreach (var (key, values) in item.Outputs)
                {
                    if (_schema.ContainsKey(key)) throw new DataException($"{where}: '{key}' is already defined");
                    _schema[key] = values?.ToHashSet();
                }
                items.Add(item);
            }
        }
        if (items.Count(i => i.Type == AdultType.InnerLife) > 1)
            throw new DataException("adult.yaml: only one inner_life item is allowed");

        _adultNorms = new(() => AdultProfiler.Calibrate(this));
        return new AdultTables { Items = items.ToArray(), CalibrationSize = f.CalibrationSize, Groups = groups };
    }

    AdultItem CompileAdultItem(AdultItemDto d, AdultGroupDto group, List<AdultItem> earlier)
    {
        string where = $"adult.yaml: {d.Id}";
        var pseudo = new GroupDto { Name = "adult" };
        var type = d.Type switch
        {
            "score" => AdultType.Score, "count" => AdultType.Count, "blend" => AdultType.Blend,
            "roll" => AdultType.Roll, "choice" => AdultType.Choice, "derive" => AdultType.Derive,
            "list" => AdultType.List, "goals" => AdultType.Goals, "issues" => AdultType.Issues,
            "dealbreakers" => AdultType.Dealbreakers, "inner_life" => AdultType.InnerLife, "friend_sources" => AdultType.FriendSources,
            _ => throw new DataException($"{where}: type must be score, count, blend, roll, choice, derive, list, goals, issues, dealbreakers, inner_life or friend_sources"),
        };

        List<AddModDto> AddMods(List<AdultModDto> mods, string w) => mods.Select((m, i) => m.Mul is null
            ? new AddModDto { Note = m.Note, When = m.When, Add = m.Add }
            : throw new DataException($"{w} mod {i + 1}: use add here (mul is for roll items)")).ToList();
        Formula F(double b, Dictionary<string, object>? weights, List<AdultModDto> mods, string w) => new()
        {
            Base = b, Terms = CompileTerms(weights, w), Mods = CompileAddMods(AddMods(mods, w), w, pseudo),
        };
        Formula? Opt(RateLikeDto? r, string w) => r is null ? null : F(r.Base, r.Weights, r.Mods, w);
        void Need(bool ok, string message)
        {
            if (!ok) throw new DataException($"{where}: {message}");
        }

        string key = $"adult.{d.Id}";
        var common = new AdultItem { Id = d.Id, Note = d.Note, Type = type, Group = group.Id, GroupTitle = group.Title ?? group.Id };
        switch (type)
        {
            case AdultType.Score:
            {
                var scale = d.Scale switch
                {
                    null or "z" => ScoreScale.Z, "z_raw" => ScoreScale.ZRaw, "unit" => ScoreScale.Unit,
                    "signed" => ScoreScale.Signed, "clamp01" => ScoreScale.Clamp01,
                    _ => throw new DataException($"{where}: scale must be z, z_raw, unit, signed or clamp01"),
                };
                Need(d.Sigma >= 0, "sigma can't be negative");
                return common with
                {
                    Formula = F(d.Base, d.Weights, d.Mods, where), Sigma = d.Sigma, Scale = scale, Outputs = [(key, null)],
                };
            }
            case AdultType.Count:
                Need(d.Cap > 0, "a count needs a cap above 0");
                return common with
                {
                    Formula = F(d.Base, d.Weights, d.Mods, where), Sigma = d.Sigma, Cap = d.Cap, Outputs = [(key, null)],
                };
            case AdultType.Blend:
                Need(d.Stated is not null && _schema.TryGetValue(d.Stated, out var sv) && sv is null,
                     "stated must name an earlier numeric adult trait");
                Need(_schema.ContainsKey("adult.self_insight"), "blend needs adult.self_insight defined above it");
                return common with
                {
                    Formula = F(d.Base, d.Weights, d.Mods, where), Sigma = d.Sigma, Stated = d.Stated, Outputs = [(key, null)],
                };
            case AdultType.Roll:
            {
                Need(d.Values.Count > 0, "a roll needs values");
                foreach (var k in d.Inherit ?? [])
                    Need(_schema.TryGetValue(GameData.ConditionSchemaKey(k), out var iv) && iv is not null, $"inherit '{k}' is not a categorical fact");
                var table = CompileItem(pseudo, new ItemDto
                {
                    Id = d.Id, Values = d.Values, Weights = d.Odds,
                    Mods = d.Mods.Select((m, i) => m.Mul is not null && m.Add == 0
                        ? new ModDto { Note = m.Note, When = m.When, Mul = m.Mul }
                        : throw new DataException($"{where} mod {i + 1}: roll mods use mul, not add")).ToList(),
                }, "adult.yaml");
                return common with
                {
                    Values = d.Values.ToArray(), Table = table, Inherit = d.Inherit?.ToArray() ?? [],
                    InheritWeight = d.InheritWeight, Outputs = [(key, d.Values)],
                };
            }
            case AdultType.Choice:
            {
                Need(d.Options is { Count: > 0 }, "a choice needs options");
                Need(d.Options!.All(o => o.Effects is null), "choice options can't have effects");
                var options = CompileChoices(d.Options, where + " option", pseudo, []);
                var values = options.Select(o => o.Id).ToList();
                double t = d.Temperature ?? 1;
                Need(t > 0, "temperature must be above 0");
                return common with { Options = options, Temperature = t, Values = values.ToArray(), Outputs = [(key, values)] };
            }
            case AdultType.Derive:
                Need(d.Values.Count > 0, "derive needs values");
                Need(d.Rules is { Count: > 0 }, "derive needs rules");
                return common with
                {
                    Values = d.Values.ToArray(), Rules = CompileRules(d.Rules, where + " rules", pseudo, d.Values.ToHashSet()),
                    Outputs = [(key, d.Values)],
                };
            case AdultType.List:
            {
                Need(d.Entries is { Count: > 0 }, "a list needs entries");
                Need(d.Levels is { Count: > 0 }, "a list needs levels (low to high)");
                Need(d.Max > 0, "a list needs max above 0");
                var levels = d.Levels!.Prepend("none").ToList();
                var entries = d.Entries!.Select(e =>
                {
                    string w = $"{where} entry '{e.Id}'";
                    if (!LifeId.IsMatch(e.Id)) throw new DataException($"{w}: ids use a-z, 0-9 and _");
                    if (e.Prevalence is <= 0 or >= 1) throw new DataException($"{w}: prevalence must be between 0 and 1");
                    if (e.Sex is not (null or "any" or "female" or "male")) throw new DataException($"{w}: sex must be any, female or male");
                    if (e.Stigma is < 0 or > 1) throw new DataException($"{w}: stigma must be between 0 and 1");
                    return new ListEntry(e.Id, e.Note, e.Prevalence, CompileTerms(e.Weights, w), CompileAddMods(AddMods(e.Mods, w), w, pseudo),
                        e.Sex is "any" ? null : e.Sex, Opt(e.Intensity, w + " intensity") ?? Formula.Zero, e.Stigma, e.Visible);
                }).ToArray();
                Need(entries.Select(e => e.Id).Distinct().Count() == entries.Length, "duplicate entry ids");
                return common with
                {
                    Entries = entries, Max = d.Max, Levels = d.Levels!.ToArray(),
                    Disclosure = Opt(d.Disclosure, where + " disclosure") ?? Formula.Zero,
                    Outputs = entries.Select(e => ($"adult.{d.Id}.{e.Id}", (IReadOnlyList<string>?)levels)).ToList(),
                };
            }
            case AdultType.Goals:
            {
                Need(d.Goals is { Count: > 0 }, "goals needs goals");
                Need(0 <= d.PickMin && d.PickMin <= d.PickMax && d.PickMax <= d.Goals!.Count, "need 0 <= pick_min <= pick_max <= number of goals");
                var goals = d.Goals!.Select(g =>
                {
                    string w = $"{where} goal '{g.Id}'";
                    if (!LifeId.IsMatch(g.Id)) throw new DataException($"{w}: ids use a-z, 0-9 and _");
                    return new Goal(g.Id, g.Note, F(g.Base, g.Weights, g.Mods, w), g.Deadline,
                        CompileTerms(g.DeadlineWeights, w + " deadline"), ParseConditions(g.Achieved, pseudo, w + " achieved"));
                }).ToArray();
                Need(goals.Select(g => g.Id).Distinct().Count() == goals.Length, "duplicate goal ids");
                string[] statuses = ["none", "active", "achieved", "missed"];
                return common with
                {
                    Goals = goals, PickMin = d.PickMin, PickMax = d.PickMax,
                    Outputs = goals.Select(g => ($"adult.{d.Id}.{g.Id}", (IReadOnlyList<string>?)statuses)).ToList(),
                };
            }
            case AdultType.Issues:
            {
                Need(d.Issues is { Count: > 0 }, "issues needs issues");
                Need(d.Consistency is not null && _schema.TryGetValue(d.Consistency, out var cv) && cv is null,
                     "consistency must name an earlier numeric adult trait (0-1)");
                var issues = d.Issues!.Select(i =>
                {
                    string w = $"{where} issue '{i.Id}'";
                    if (!LifeId.IsMatch(i.Id)) throw new DataException($"{w}: ids use a-z, 0-9 and _");
                    return new Issue(i.Id, i.Note, CompileTerms(i.Axes, w + " axes"), i.BaseSalience,
                        i.Stakes.Select((s, n) => new Stake(s.Note, ParseConditions(s.When, pseudo, $"{w} stake {n + 1}"), s.Shift, s.Salience)).ToArray());
                }).ToArray();
                return common with
                {
                    Issues = issues, Consistency = d.Consistency, Noise = d.Noise,
                    Outputs = issues.SelectMany(i => new (string, IReadOnlyList<string>?)[]
                        { ($"adult.{d.Id}.{i.Id}", null), ($"adult.{d.Id}.{i.Id}_salience", null) }).ToList(),
                };
            }
            case AdultType.Dealbreakers:
            {
                Need(d.Dealbreakers is { Count: > 0 }, "dealbreakers needs rules");
                var rules = d.Dealbreakers!.Select(r =>
                {
                    string w = $"{where} '{r.Id}'";
                    if (!LifeId.IsMatch(r.Id)) throw new DataException($"{w}: ids use a-z, 0-9 and _");
                    return new Dealbreaker(r.Id, r.Rule, ParseConditions(r.When, pseudo, w + " when"), F(r.Hardness.Base, r.Hardness.Weights, r.Hardness.Mods, w + " hardness"));
                }).ToArray();
                string[] levels = ["none", "soft", "hard"];
                return common with
                {
                    Dealbreakers = rules, HardAt = d.HardAt,
                    Outputs = rules.Select(r => ($"adult.{d.Id}.{r.Id}", (IReadOnlyList<string>?)levels)).ToList(),
                };
            }
            case AdultType.FriendSources:
            {
                bool Numeric(string? k) => k is not null && k.StartsWith("adult.") && _schema.TryGetValue(k, out var v) && v is null;
                Need(Numeric(d.Count), "count must name an earlier numeric adult trait (the number of close friends)");
                Need(Numeric(d.Status), "status must name an earlier numeric adult trait (the circle's standing)");
                Need(d.Sources is { Count: > 0 }, "friend_sources needs sources");
                Need(d.SameSex is >= 0 and <= 1, "same_sex must be between 0 and 1");
                Need(d.StatusSd >= 0, "status_sd can't be negative");
                Need(d.Concentration > 0, "concentration must be above 0");
                var sources = d.Sources!.Select(s =>
                {
                    string w = $"{where} source '{s.Id}'";
                    if (!LifeId.IsMatch(s.Id)) throw new DataException($"{w}: ids use a-z, 0-9 and _");
                    if (s.Weight <= 0) throw new DataException($"{w}: weight must be above 0");
                    if (s.MetFrom < 5) throw new DataException($"{w}: met_from must be at least 5 (no friend is known longer than age − 5)");
                    if (s.MetTo < s.MetFrom) throw new DataException($"{w}: met_to is before met_from");
                    return new FriendSource(s.Id, s.Note, s.Weight, F(0, s.Weights, s.Mods, w), s.MetFrom, s.MetTo);
                }).ToArray();
                Need(sources.Select(s => s.Id).Distinct().Count() == sources.Length, "duplicate source ids");
                Need(sources.Any(s => s.MetFrom <= Config.AgeMin), $"at least one source must be open by age_min ({Config.AgeMin})");
                return common with
                {
                    Sources = sources, CountKey = d.Count, StatusKey = d.Status, StatusSd = d.StatusSd, SameSex = d.SameSex,
                    Concentration = d.Concentration, Closeness = Opt(d.Closeness, where + " closeness") ?? Formula.Zero,
                    Outputs = sources.Select(s => ($"adult.{d.Id}.{s.Id}", (IReadOnlyList<string>?)null))
                        .Append(("adult.friend_closeness", null)).ToList(),
                };
            }
            default: // InnerLife
            {
                var events = Life!.Items.Where(i => i.Type == LifeItemType.Event).ToList();
                var forks = Life.Items.Where(i => i.Type == LifeItemType.Fork).Select(i => i.Id);
                var readings = Life.Items.SelectMany(i => i.Readings.Select(r => (Item: i.Id, Id: r.Id))).ToList();
                var beliefs = (d.Beliefs ?? []).Select(b =>
                {
                    string? item = b.Reading.Contains(':') ? b.Reading[..b.Reading.IndexOf(':')] : null;
                    string reading = item is null ? b.Reading : b.Reading[(item.Length + 1)..];
                    Need(readings.Any(r => r.Id == reading && (item is null || r.Item == item)), $"belief reading '{b.Reading}' is not a reading in life.yaml");
                    Need(LifeId.IsMatch(b.Belief) && LifeId.IsMatch(b.Want) && LifeId.IsMatch(b.Need), $"belief ids for '{b.Reading}' use a-z, 0-9 and _");
                    return new Belief(item, reading, b.Belief, b.Want, b.Need);
                }).ToArray();
                foreach (var h in (d.Hideable ?? new()).Keys)
                {
                    string item = h.Contains(':') ? h[..h.IndexOf(':')] : h;
                    Need(Life.Items.Any(i => i.Id == item), $"hideable '{h}' is not a life.yaml item");
                }
                var goalIds = earlier.Where(i => i.Type == AdultType.Goals).SelectMany(i => i.Goals.Select(g => g.Id));
                List<string> None(IEnumerable<string> v) => v.Prepend("none").Distinct().ToList();
                return common with
                {
                    WoundThreshold = d.WoundThreshold, RegretThreshold = d.RegretThreshold, MaxSecrets = d.MaxSecrets,
                    Beliefs = beliefs, Hideable = d.Hideable ?? new(),
                    Outputs =
                    [
                        ("adult.core_wound", None(events.Select(e => e.Id))),
                        ("adult.wound_impact", null),
                        ("adult.private_belief", None(beliefs.Select(b => b.Id))),
                        ("adult.want", None(beliefs.Select(b => b.Want))),
                        ("adult.need", None(beliefs.Select(b => b.Need))),
                        ("adult.regret", None(forks.Concat(goalIds))),
                        ("adult.secret_count", null),
                    ],
                };
            }
        }
    }
}
