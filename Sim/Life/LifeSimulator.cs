using System.Globalization;
using System.Text;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Sim.Life;

/// <summary>One fired event, resolved fork, or logged condition change (spec 4.10).</summary>
public sealed class HistoryEntry
{
    public required int Id { get; init; }
    public required string Stage { get; init; }
    public required int Age { get; init; }
    public required int Year { get; init; }
    public required string ItemId { get; init; }
    public required LifeItemType Type { get; init; }
    /// <summary>Event outcome id ("fired" without outcomes), fork option, or "old → new" for a condition.</summary>
    public required string Outcome { get; init; }
    /// <summary>How the pawn read the event (spec section 5), if it has readings.</summary>
    public string? Reading { get; init; }
    public required IReadOnlyList<(string Key, string Value)> Changes { get; init; }
    public required IReadOnlyDictionary<string, double> Channels { get; init; }
    /// <summary>Ids of earlier entries that wrote the facts this one read.</summary>
    public required IReadOnlyList<int> Causes { get; init; }
    /// <summary>Σ|channel deltas| × stage weight × valence factor (spec 6.9).</summary>
    public double Impact { get; init; }
    public bool Negative { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>Index of the involvement (partner) this entry happened to, if any.</summary>
    public int? Involvement { get; init; }
    /// <summary>How the roll came out, step by step. Only filled when life details are requested.</summary>
    public string? Explain { get; init; }
}

/// <summary>
/// One involvement in the life history: every partner the pawn was ever involved
/// with. An involvement never ends; EndAge is when it was dissolved (its agreements
/// removed), after which its level drains toward 0. Partners are records, not pawns:
/// the seed lets the game promote one to a full pawn later, and the city links a
/// partner to a resident when one fits.
/// </summary>
public sealed record Partner(int StartAge, int? EndAge, string? EndReason, bool Married)
{
    /// <summary>How involved the two are now, 0–1 (attraction spec 8.3, coarsely). Dissolved ones drain toward 0.</summary>
    public double Level { get; init; } = 1;
    /// <summary>Weeks from the dissolve to now; null while the involvement holds.</summary>
    public double? WeeksSinceDissolved { get; init; }
    /// <summary>The involvement's fields (inv.&lt;field&gt;) when it ended, or now.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
    /// <summary>"male" or "female", from the involvement's partner_sex field.</summary>
    public string Sex { get; init; } = "";
    /// <summary>Partner's age minus the pawn's.</summary>
    public double AgeGap { get; init; }
    /// <summary>Partner's social standing, z-scale. Feeds the revealed-preference history pull (spec 6.8).</summary>
    public double Status { get; init; }
    /// <summary>How emotionally available the partner was, 0–1.</summary>
    public double Availability { get; init; } = 0.5;
    /// <summary>Derived seed for promoting this partner to a full pawn.</summary>
    public ulong Seed { get; init; }
    /// <summary>Dissolved: its agreements are gone and it is draining (or has drained) toward 0.</summary>
    public bool Ended => EndAge is not null;
    public int Years(int currentAge) => Math.Max(0, (EndAge ?? currentAge) - StartAge);
}

public sealed class LifeResult
{
    public required IReadOnlyList<HistoryEntry> History { get; init; }
    public required IReadOnlyList<Partner> Partners { get; init; }
}

/// <summary>
/// Spec section 4: runs a pawn from birth to its current age, one year at a
/// time. Each year, items run in file order: an item sees changes made by items
/// above it this year, and by items below it as of last year.
/// </summary>
public sealed class LifeSimulator
{
    const double PositiveValence = 0.6;

    readonly GameData _data;
    readonly LifeTables _t;
    readonly ulong _seed;
    readonly Facts _f;
    readonly int _birthYear, _currentAge;
    readonly bool _explain;

    readonly List<HistoryEntry> _history = new();
    readonly Dictionary<string, int> _lastWriter = new();
    readonly Dictionary<string, int> _counts = new();
    readonly Dictionary<string, string> _lastChoice = new();
    readonly Dictionary<string, HashSet<int>> _scheduled = new();

    /// <summary>
    /// A relationship of any kind: its own fields and level, no label (the label is
    /// derived). It never ends: a dissolve removes its agreements and its level drains.
    /// </summary>
    sealed class Involvement
    {
        public required int Index { get; init; }
        public required int StartAge { get; init; }
        public int? DissolvedAge { get; set; }
        /// <summary>Week of the year (0–51) the dissolve happened.</summary>
        public int DissolvedWeek { get; set; }
        public double LevelAtDissolve { get; set; }
        public string? EndReason { get; set; }
        /// <summary>Years together, until the dissolve.</summary>
        public int Years { get; set; }
        public double Level { get; set; }
        /// <summary>The agreement field was formalized at some point (a marriage), even after a dissolve removed it.</summary>
        public bool Formalized { get; set; }
        public required Dictionary<string, string> Fields { get; init; }
        public Dictionary<string, int> LastWriter { get; } = new();
        public bool Dissolved => DissolvedAge is not null;
    }

    readonly List<Involvement> _involvements = new();
    /// <summary>The involvement inv.&lt;field&gt; refers to right now, if any.</summary>
    Involvement? _current;
    int _age;
    Stage _stage = null!;
    LocationPeriod _place = null!;

    LifeSimulator(GameData data, ulong seed, Facts facts, int birthYear, int currentAge, bool explain)
    {
        _data = data;
        _t = data.Life!;
        _seed = seed;
        _f = facts;
        _birthYear = birthYear;
        _currentAge = currentAge;
        _explain = explain;
    }

    /// <param name="facts">The pawn's facts so far; the simulation adds state., channel., place. and life. facts.</param>
    /// <param name="trace">Receives a trace for each final value, when tracing.</param>
    /// <param name="explain">Write a step-by-step explanation into each history entry.</param>
    public static LifeResult Run(GameData data, ulong seed, Facts facts, int birthYear, int currentAge,
                                 List<RollTrace>? trace, bool explain)
    {
        var sim = new LifeSimulator(data, seed, facts, birthYear, currentAge, explain);
        sim.Simulate();
        sim.WriteSummaries(trace);
        return new LifeResult { History = sim._history, Partners = sim.BuildPartners() };
    }

    // ------------------------------------------------------------ the loop

    void Simulate()
    {
        _f.Set("state.location", _f["pawn.birth_location"]);
        if (_t.Involvement is not null) Relabel(null);
        foreach (var item in _t.Items)
        {
            if (item.Type == LifeItemType.Condition) Initialize(item);
            BuildSchedule(item);
        }

        for (_age = 0; _age <= _currentAge; _age++)
        {
            _stage = _t.StageAt(_age);
            _f.Set("age", _age);
            _f.Set("cal_year", _birthYear + _age);
            _f.Set("stage", _stage.Id);
            RefreshPlace();

            foreach (var item in _t.Items)
            {
                if (!IsScheduled(item)) continue;
                if (item.PerInvolvement)
                {
                    // Once for each involvement not dissolved when the item's turn comes.
                    foreach (var inv in _involvements.Where(i => !i.Dissolved).ToList())
                    {
                        Enter(inv);
                        RollEvent(item);
                        Leave();
                    }
                    continue;
                }
                switch (item.Type)
                {
                    case LifeItemType.Condition: UpdateCondition(item); break;
                    case LifeItemType.Event: RollEvent(item); break;
                    case LifeItemType.Fork: ResolveFork(item); break;
                }
            }

            // Involvements move at the end of the year, after its events.
            if (_t.Involvement is not null)
            {
                foreach (var inv in _involvements) Advance(inv);
                Relabel(null);
            }
        }
    }

    /// <summary>
    /// One year of an involvement. While it holds, it counts a year together and its
    /// level moves toward where its agreements anchor it. After a dissolve, the level
    /// drains week by week from where it was (8.3), toward 0.
    /// </summary>
    void Advance(Involvement inv)
    {
        var level = _t.Involvement!.Level;
        if (!inv.Dissolved)
        {
            inv.Years++;
            inv.Level += level.Approach * (SettlesAt(inv) - inv.Level);
            return;
        }
        double weeks = (_age - inv.DissolvedAge!.Value) * 52 + (52 - inv.DissolvedWeek);
        inv.Level = inv.LevelAtDissolve * Math.Pow(1 - level.DrainPerWeek, weeks);
    }

    double SettlesAt(Involvement inv)
    {
        var level = _t.Involvement!.Level;
        return level.Field is { } field && inv.Fields.TryGetValue(field, out var value)
            ? level.SettlesAt.GetValueOrDefault(value, 1) : 1;
    }

    // --------------------------------------------------------- involvements

    /// <summary>Point inv.&lt;field&gt; at this involvement.</summary>
    void Enter(Involvement inv)
    {
        _current = inv;
        foreach (var (field, value) in inv.Fields) _f.Set($"inv.{field}", value);
        _f.Set("inv.years", inv.Years);
        _f.Set("inv.level", inv.Level);
        _f.Set("inv.dissolved", "no");
        foreach (var field in inv.Fields.Keys)
        {
            if (inv.LastWriter.TryGetValue(field, out int w)) _lastWriter[$"inv.{field}"] = w;
            else _lastWriter.Remove($"inv.{field}");
        }
    }

    /// <summary>Copy inv.&lt;field&gt; back into the current involvement.</summary>
    void Store()
    {
        var inv = _current!;
        foreach (var field in inv.Fields.Keys.ToList())
        {
            inv.Fields[field] = _f.Cat($"inv.{field}") ?? inv.Fields[field];
            if (_lastWriter.TryGetValue($"inv.{field}", out int w)) inv.LastWriter[field] = w;
        }
        inv.Formalized |= inv.Fields.GetValueOrDefault("agreement") == "formalized";
    }

    /// <summary>Store the current involvement, clear inv.&lt;field&gt;, and re-derive the label.</summary>
    void Leave(int? entry = null)
    {
        Store();
        foreach (var field in _current!.Fields.Keys)
        {
            _f.Remove($"inv.{field}");
            _lastWriter.Remove($"inv.{field}");
        }
        _f.Remove("inv.years");
        _f.Remove("inv.level");
        _f.Remove("inv.dissolved");
        _current = null;
        Relabel(entry);
    }

    /// <summary>A new involvement starting now, with each field at its initial value; inv.&lt;field&gt; points at it.</summary>
    void StartInvolvement()
    {
        var inv = new Involvement
        {
            Index = _involvements.Count, StartAge = _age,
            Fields = _t.Involvement!.Fields.ToDictionary(f => f.Id, f => f.Initial),
        };
        inv.Level = SettlesAt(inv);
        _involvements.Add(inv);
        Enter(inv);
    }

    /// <summary>8.3 Dissolve: the agreements go, the level starts draining from where it is this week. The involvement stays.</summary>
    void Dissolve(Involvement inv)
    {
        inv.DissolvedAge = _age;
        inv.DissolvedWeek = Rng.For(_seed, "life", "dissolve", inv.Index.ToString(CultureInfo.InvariantCulture)).NextInt(0, 52);
        inv.LevelAtDissolve = inv.Level;
    }

    /// <summary>
    /// Write every involvement, past and present, as inv0.*, inv1.*, ... (read by
    /// any_inv.), how many are at or above involved_at, the longest undissolved one,
    /// and derive state.relationship from the label rules.
    /// </summary>
    void Relabel(int? entry)
    {
        var inv = _t.Involvement!;
        foreach (var i in _involvements)
        {
            foreach (var (field, value) in i.Fields) _f.Set($"inv{i.Index}.{field}", value);
            _f.Set($"inv{i.Index}.years", i.Years);
            _f.Set($"inv{i.Index}.level", i.Level);
            _f.Set($"inv{i.Index}.dissolved", i.Dissolved ? "yes" : "no");
        }
        var holding = _involvements.Where(i => !i.Dissolved).ToList();
        _f.Set("state.involvements", _involvements.Count(i => i.Level >= inv.Level.InvolvedAt));
        _f.Set("state.relationship_years", holding.Count == 0 ? 0 : holding.Max(i => i.Years));

        string? old = _f.Cat("state.relationship");
        string label = inv.LabelRules.First(r => r.When.All(c => FactTest.Test(c, _f))).Value;
        _f.Set("state.relationship", label);
        if (entry is int e && old != label) _lastWriter["state.relationship"] = e;
    }

    void RefreshPlace()
    {
        var loc = _data.LocationById(_f["state.location"]);
        int year = _birthYear + _age;
        _place = loc.PeriodFor(year);
        _f.Set("place.location", loc.Id);
        _f.Set("place.urbanicity", loc.Urbanicity);
        _f.Set("place.local_politics", _place.LocalPolitics);
        _f.Set("place.diversity", _place.Diversity);
        _f.Set("place.economic_era", _data.EconomicEra(year, _place));
        _f.Set("place.norms_sex", _place.NormsSex);
        _f.Set("place.norms_gender", _place.NormsGender);
        _f.Set("place.norms_family", _place.NormsFamily);
        _f.Set("state.lives_in_current_city", loc.Id == _data.CurrentCity.Id ? "yes" : "no");
    }

    // ----------------------------------------------------------- schedule

    void BuildSchedule(LifeItem item)
    {
        // Open-ended windows stop at age_max, so a once-per-stage roll in the
        // present stage lands somewhere in the ages pawns can actually have.
        int lastAge = Math.Min(Math.Min(item.WindowTo, _t.Stages[^1].To), _data.Config.AgeMax);
        var ages = new HashSet<int>();
        if (item.Ages is not null)
        {
            ages.UnionWith(item.Ages);
        }
        else if (item.Frequency == Frequency.PerStage)
        {
            foreach (var s in _t.Stages)
            {
                int lo = Math.Max(s.From, item.WindowFrom), hi = Math.Min(s.To, lastAge);
                if (lo <= hi) ages.Add(lo + Rng.For(_seed, "life", "schedule", item.Id, s.Id).NextInt(0, hi - lo + 1));
            }
        }
        else if (item.Frequency == Frequency.Once)
        {
            if (item.WindowFrom <= lastAge)
                ages.Add(item.WindowFrom + Rng.For(_seed, "life", "schedule", item.Id).NextInt(0, lastAge - item.WindowFrom + 1));
        }
        else
        {
            return; // per_year: every age in the window
        }
        _scheduled[item.Id] = ages;
    }

    bool IsScheduled(LifeItem item)
    {
        if (item.Ages is null && (_age < item.WindowFrom || _age > item.WindowTo)) return false;
        if (item.Max > 0 && _counts.GetValueOrDefault(item.Id) >= item.Max) return false;
        if (item.Type == LifeItemType.Condition && item.Kind is ConditionKind.State or ConditionKind.Number) return false;
        return !_scheduled.TryGetValue(item.Id, out var ages) || ages.Contains(_age);
    }

    Rng RngFor(LifeItem item) => _current is { } inv
        ? Rng.For(_seed, "life", _stage.Id, _age.ToString(CultureInfo.InvariantCulture), item.Id, inv.Index.ToString(CultureInfo.InvariantCulture))
        : Rng.For(_seed, "life", _stage.Id, _age.ToString(CultureInfo.InvariantCulture), item.Id);

    // ---------------------------------------------------------- conditions

    void Initialize(LifeItem item)
    {
        if (item.IsNumeric)
        {
            double? n = item.Initial is not null ? _f.Num(item.Initial)
                : item.InitialValue is not null ? double.Parse(item.InitialValue, CultureInfo.InvariantCulture)
                : item.Kind == ConditionKind.Counter ? 0 : null;
            if (n is double v) _f.Set(item.Key, v);
            return;
        }

        string? value = item.Initial is not null ? FactTest.Values(item.Initial, _f).FirstOrDefault() : item.InitialValue;
        foreach (var rule in item.InitialRules)
            if (rule.When.All(c => FactTest.Test(c, _f)))
            {
                value = rule.Value;
                break;
            }
        if (value is not null) _f.Set(item.Key, value);
    }

    void UpdateCondition(LifeItem item)
    {
        if (!item.Requires.All(c => FactTest.Test(c, _f))) return;
        var rng = RngFor(item);
        string? old = _f.Cat(item.Key);
        var why = _explain ? new StringBuilder() : null;
        var read = new List<string>();

        switch (item.Kind)
        {
            case ConditionKind.Roll:
            {
                var table = item.Table!;
                if (!table.OnlyIf.All(c => FactTest.Test(c, _f)))
                {
                    SetCondition(item, old, table.Default!, read, why?.Append("requires failed, so the default"));
                    return;
                }
                var weights = (double[])table.BaseWeights.Clone();
                foreach (var mod in table.Mods)
                    if (mod.When.All(c => FactTest.Test(c, _f)))
                    {
                        foreach (var (index, factor) in mod.Mul) weights[index] *= factor;
                        read.AddRange(mod.When.Select(c => c.Key));
                    }
                int pick = rng.PickWeighted(weights);
                if (pick < 0) return;
                if (why is not null)
                {
                    double total = weights.Sum();
                    why.Append("rolled: ").AppendJoin(", ", table.Values.Select((v, i) =>
                        string.Create(CultureInfo.InvariantCulture, $"{v} {100 * weights[i] / total:0}%")));
                }
                SetCondition(item, old, table.Values[pick], read, why);
                return;
            }
            case ConditionKind.Drift:
            {
                int index = Array.IndexOf(item.Values, old);
                if (index < 0) return;
                var up = item.Up is null ? default : EvalRate(item.Up, read, why?.Append("up: "));
                var down = item.Down is null ? default : EvalRate(item.Down, read, why?.Append("\ndown: "));
                double pu = up.P, pd = down.P;
                if (pu + pd > 1) { double s = pu + pd; pu /= s; pd /= s; }
                double r = rng.NextDouble();
                int next = r < pu ? index + 1 : r < pu + pd ? index - 1 : index;
                next = Math.Clamp(next, 0, item.Values.Length - 1);
                why?.Append(string.Create(CultureInfo.InvariantCulture, $"\nroll {r:0.000}: {(next > index ? "up" : next < index ? "down" : "no change")}"));
                SetCondition(item, old, item.Values[next], read, why);
                return;
            }
            case ConditionKind.Derive:
                foreach (var rule in item.Rules)
                    if (rule.When.All(c => FactTest.Test(c, _f)))
                    {
                        read.AddRange(rule.When.Select(c => c.Key));
                        SetCondition(item, old, rule.Value, read, why?.Append("first matching rule: ")
                            .AppendJoin(" and ", rule.When.Select(c => c.Text)));
                        return;
                    }
                return;
            case ConditionKind.Counter:
                double count = _f.Num(item.Key) ?? 0;
                _f.Set(item.Key, item.While.All(c => FactTest.Test(c, _f)) ? count + 1 : 0);
                return;
        }
    }

    void SetCondition(LifeItem item, string? old, string value, List<string> read, StringBuilder? why)
    {
        if (old == value) return;
        _f.Set(item.Key, value);
        if (!item.Log) return;
        int id = _history.Count;
        AddEntry(new HistoryEntry
        {
            Id = id, Stage = _stage.Id, Age = _age, Year = _birthYear + _age, ItemId = item.Id,
            Type = LifeItemType.Condition, Outcome = $"{old ?? "-"} → {value}",
            Changes = [(item.Key, value)], Channels = new Dictionary<string, double>(),
            Causes = CausesOf(read, id), Explain = why?.ToString(),
        });
        _lastWriter[item.Key] = id;
    }

    // --------------------------------------------------------------- events

    void RollEvent(LifeItem item)
    {
        if (!item.Requires.All(c => FactTest.Test(c, _f))) return;
        var rng = RngFor(item);
        var why = _explain ? new StringBuilder() : null;
        var read = item.Requires.Select(c => c.Key).ToList();

        var rate = EvalRate(item.Rate!, read, why);
        double roll = rng.NextDouble();
        bool forced = item.MustHappen && _age == Math.Min(item.WindowTo, _currentAge) && _counts.GetValueOrDefault(item.Id) == 0;
        bool fired = roll < rate.P || forced;
        if (!fired) return;
        why?.Append(string.Create(CultureInfo.InvariantCulture, $"\nroll {roll:0.000} → "))
            .Append(roll < rate.P ? "fired" : "forced (must happen by now)");

        var outcome = Choose(item.Outcomes, 1.0, rng, read, why, "outcome");
        var reading = Choose(item.Readings, _t.ReadingTemperature, rng, read, why, "reading");

        int id = _history.Count;
        var changes = new List<(string, string)>();
        var channels = new Dictionary<string, double>();
        bool started = item.Effects.StartInvolvement;
        if (started) StartInvolvement();
        Apply(item.Effects, id, changes, channels);
        if (outcome is not null) Apply(outcome.Effects, id, changes, channels);
        if (reading is not null) Apply(reading.Effects, id, changes, channels);
        var involvement = _current;
        if (involvement is not null)
        {
            if (involvement.Dissolved) involvement.EndReason ??= outcome?.Id ?? item.Id;
            Store();
            Relabel(id);
        }

        _counts[item.Id] = _counts.GetValueOrDefault(item.Id) + 1;
        _lastChoice[item.Id] = outcome?.Id ?? "fired";
        AddEntry(new HistoryEntry
        {
            Id = id, Stage = _stage.Id, Age = _age, Year = _birthYear + _age, ItemId = item.Id,
            Type = LifeItemType.Event, Outcome = outcome?.Id ?? "fired", Reading = reading?.Id,
            Changes = changes, Channels = channels, Causes = CausesOf(read, id),
            Impact = channels.Values.Sum(Math.Abs) * _stage.Weight * (item.Negative ? 1.0 : PositiveValence),
            Negative = item.Negative, Tags = item.Tags, Involvement = involvement?.Index, Explain = why?.ToString(),
        });
        if (started) Leave(id);
    }

    // ---------------------------------------------------------------- forks

    void ResolveFork(LifeItem item)
    {
        if (!item.Requires.All(c => FactTest.Test(c, _f))) return;
        var rng = RngFor(item);
        var why = _explain ? new StringBuilder() : null;
        var read = item.Requires.Select(c => c.Key).ToList();
        string? current = item.StickyKey is null ? null : _f.Cat(item.StickyKey);

        var option = Choose(item.Options, item.Temperature, rng, read, why, "option", item, current);
        if (option is null) return;

        int id = _history.Count;
        var changes = new List<(string, string)>();
        var channels = new Dictionary<string, double>();
        Apply(item.Effects, id, changes, channels);
        Apply(option.Effects, id, changes, channels);
        _counts[item.Id] = _counts.GetValueOrDefault(item.Id) + 1;
        _lastChoice[item.Id] = option.Id;

        // A sticky fork that keeps its current choice is not news.
        if (item.StickyKey is not null && option.Id == current) return;
        AddEntry(new HistoryEntry
        {
            Id = id, Stage = _stage.Id, Age = _age, Year = _birthYear + _age, ItemId = item.Id,
            Type = LifeItemType.Fork, Outcome = option.Id, Changes = changes, Channels = channels,
            Causes = CausesOf(read, id),
            Impact = channels.Values.Sum(Math.Abs) * _stage.Weight * (item.Negative ? 1.0 : PositiveValence),
            Negative = item.Negative, Tags = item.Tags, Explain = why?.ToString(),
        });
    }

    // ---------------------------------------------------------- the models

    readonly record struct RateResult(double P);

    /// <summary>Spec 4.6: p = sigmoid(logit(base) + Σ w·x).</summary>
    RateResult EvalRate(Rate rate, List<string> read, StringBuilder? why)
    {
        double b = rate.BaseValue ?? _data.BaseRate(rate.BaseKey!, _birthYear + _age, _place);
        b = Math.Clamp(b, 1e-6, 1 - 1e-6);
        double logit = Math.Log(b / (1 - b));
        why?.Append(string.Create(CultureInfo.InvariantCulture,
            $"base {b:0.###}{(rate.BaseKey is null ? "" : $" (rate.{rate.BaseKey}, {_birthYear + _age})")}"));

        logit += Score(rate.Terms, rate.Mods, read, why);
        double p = 1 / (1 + Math.Exp(-logit));
        why?.Append(string.Create(CultureInfo.InvariantCulture, $" → p {p:0.###}"));
        return new RateResult(p);
    }

    /// <summary>Σ terms + Σ fired mods, recording what was read and why.</summary>
    double Score(Term[] terms, AddMod[] mods, List<string> read, StringBuilder? why)
    {
        double sum = 0;
        foreach (var term in terms)
        {
            double v;
            string label;
            if (term.IsNumeric)
            {
                double x = _f.Num(term.Key) ?? 0;
                if (term.Centered) x = (x - 0.5) * 2;
                v = term.Weight * x;
                label = term.Key;
            }
            else
            {
                v = 0;
                label = term.Key;
                foreach (var value in FactTest.Values(term.Key, _f))
                    if (term.ValueWeights!.TryGetValue(value, out var w))
                    {
                        v += w;
                        label = $"{term.Key}={value}";
                    }
            }
            if (v == 0) continue;
            sum += v;
            read.Add(term.Key);
            why?.Append(string.Create(CultureInfo.InvariantCulture, $" {(v >= 0 ? "+" : "−")}{Math.Abs(v):0.##} {label}"));
        }
        foreach (var mod in mods)
        {
            if (!mod.When.All(c => FactTest.Test(c, _f))) continue;
            sum += mod.Add;
            read.AddRange(mod.When.Select(c => c.Key));
            why?.Append(string.Create(CultureInfo.InvariantCulture,
                $" {(mod.Add >= 0 ? "+" : "−")}{Math.Abs(mod.Add):0.##} [{string.Join(" and ", mod.When.Select(c => c.Text))}]"));
        }
        return sum;
    }

    /// <summary>Softmax choice: chance ∝ weight × exp((utility + sticky bonus) / temperature).</summary>
    Choice? Choose(Choice[] choices, double temperature, Rng rng, List<string> read, StringBuilder? why,
                   string label, LifeItem? sticky = null, string? current = null)
    {
        if (choices.Length == 0) return null;
        var eligible = choices.Where(c => c.Weight > 0 && c.Requires.All(r => FactTest.Test(r, _f))).ToArray();
        if (eligible.Length == 0) return null;

        var scores = new double[eligible.Length];
        var reads = new List<string>[eligible.Length];
        for (int i = 0; i < eligible.Length; i++)
        {
            reads[i] = new List<string>();
            double u = Score(eligible[i].Terms, eligible[i].Mods, reads[i], null);
            if (sticky is not null && eligible[i].Id == current && !sticky.StickyExcept.Contains(current))
                u += sticky.StickyBonus;
            scores[i] = Math.Log(eligible[i].Weight) + u / temperature;
        }
        double max = scores.Max();
        var probs = scores.Select(s => Math.Exp(s - max)).ToArray();
        double total = probs.Sum();
        int pick = rng.PickWeighted(probs);
        var chosen = eligible[pick];
        read.AddRange(chosen.Requires.Select(r => r.Key));
        read.AddRange(reads[pick]);

        why?.Append($"\n{label}: ").AppendJoin(", ", eligible.Select((c, i) =>
            string.Create(CultureInfo.InvariantCulture, $"{c.Id} {100 * probs[i] / total:0}%{(i == pick ? " ✓" : "")}")));
        return chosen;
    }

    void Apply(Effects e, int entryId, List<(string, string)> changes, Dictionary<string, double> channels)
    {
        bool moved = false;
        foreach (var (key, raw) in e.Set)
        {
            string value = raw == "$current_city" ? _data.CurrentCity.Id : raw;
            if (_f.Cat(key) == value) continue;
            _f.Set(key, value);
            changes.Add((key, value));
            _lastWriter[key] = entryId;
            moved |= key == "state.location";
        }
        foreach (var (key, amount) in e.Add)
        {
            _f.Set(key, (_f.Num(key) ?? 0) + amount);
            changes.Add((key, (_f.Num(key) ?? 0).ToString(CultureInfo.InvariantCulture)));
            _lastWriter[key] = entryId;
        }
        foreach (var key in e.Reset)
        {
            _f.Set(key, 0);
            _lastWriter[key] = entryId;
        }
        foreach (var key in e.RecordFirstAge)
        {
            if (_f.Num(key) is not null) continue;
            _f.Set(key, _age);
            changes.Add((key, _age.ToString(CultureInfo.InvariantCulture)));
            _lastWriter[key] = entryId;
        }
        foreach (var (channel, delta) in e.Channels)
        {
            string key = $"channel.{channel}";
            _f.Set(key, (_f.Num(key) ?? 0) + delta);
            channels[channel] = channels.GetValueOrDefault(channel) + delta;
            _lastWriter[key] = entryId;
        }
        if (e.Dissolve && _current is { Dissolved: false } inv)
        {
            Dissolve(inv);
            changes.Add(("involvement", "dissolved"));
        }
        if (moved) RefreshPlace();
    }

    IReadOnlyList<int> CausesOf(List<string> read, int self) =>
        read.Select(k => _lastWriter.TryGetValue(k, out int w) ? w : -1)
            .Where(w => w >= 0 && w != self)
            .Distinct().OrderBy(w => w).ToList();

    void AddEntry(HistoryEntry e) => _history.Add(e);

    // ------------------------------------------------------------- results

    void WriteSummaries(List<RollTrace>? trace)
    {
        foreach (var item in _t.Items)
        {
            if (item.Type == LifeItemType.Condition)
            {
                if (trace is null) continue;
                var last = _lastWriter.TryGetValue(item.Key, out int w) ? _history[w] : null;
                string note = last is null
                    ? $"value at age {_currentAge}"
                    : $"value at age {_currentAge}; last changed at age {last.Age} by {last.ItemId}";
                if (_f.Cat(item.Key) is string v)
                    trace.Add(new RollTrace { Key = item.Key, Source = RollSource.Derived, Value = v, Note = note });
                else if (_f.Num(item.Key) is double n)
                    trace.Add(new RollTrace { Key = item.Key, Source = RollSource.Derived, Number = n, Note = note });
                continue;
            }

            int count = _counts.GetValueOrDefault(item.Id);
            string summary = item.Type == LifeItemType.Event
                ? count switch { 0 => "never", 1 => "once", 2 => "twice", _ => "3_plus" }
                : _lastChoice.GetValueOrDefault(item.Id, "not_reached");
            _f.Set($"life.{item.Id}", summary);
            trace?.Add(new RollTrace
            {
                Key = $"life.{item.Id}", Source = RollSource.Derived, Value = summary,
                Note = item.Type == LifeItemType.Event ? $"fired {count} time(s) by age {_currentAge}" : "last choice",
            });
        }

        if (trace is null) return;
        foreach (var c in _t.Channels)
            trace.Add(new RollTrace
            {
                Key = $"channel.{c}", Source = RollSource.Derived, Number = _f.Num($"channel.{c}") ?? 0,
                Note = "raw sum of deltas written by readings and effects (stage weights apply in the adult profile)",
            });
        // Everything else the simulation wrote: where the pawn lives now, and the clock.
        var traced = trace.Select(t => t.Key).ToHashSet();
        foreach (var (key, value) in _f.Categorical)
            if (GameData.IsLifeKey(key) && traced.Add(key))
                trace.Add(new RollTrace { Key = key, Source = RollSource.Derived, Value = value,
                    Note = key.StartsWith("place.") || key.StartsWith("state.") ? "where the pawn lives now" : "at the present" });
        foreach (var (key, value) in _f.Numeric)
            if (GameData.IsLifeKey(key) && traced.Add(key))
                trace.Add(new RollTrace { Key = key, Source = RollSource.Derived, Number = value, Note = "at the present" });
    }

    List<Partner> BuildPartners()
    {
        if (_t.Involvement is not null)
            return _involvements.Select(inv => DescribePartner(
                new Partner(inv.StartAge, inv.DissolvedAge, inv.EndReason, inv.Formalized)
                {
                    Fields = new Dictionary<string, string>(inv.Fields),
                    Level = inv.Level,
                    WeeksSinceDissolved = inv.Dissolved ? (_currentAge - inv.DissolvedAge!.Value) * 52 + (52 - inv.DissolvedWeek) : null,
                },
                inv.Index, inv.Fields.GetValueOrDefault("partner_sex") == "same")).ToList();

        // Without an involvement section: one relationship at a time, from tagged history entries.
        var partners = new List<Partner>();
        Partner? open = null;
        foreach (var e in _history)
        {
            if (e.Tags.Contains("relationship_start"))
            {
                if (open is not null) partners.Add(open with { EndAge = e.Age, EndReason = "overlap" });
                open = DescribePartner(new Partner(e.Age, null, null, false), partners.Count, e.Outcome == "same_sex");
            }
            else if (e.Tags.Contains("relationship_married") && open is not null)
                open = open with { Married = true };
            else if (e.Tags.Contains("relationship_end") && open is not null)
            {
                partners.Add(open with { EndAge = e.Age, EndReason = e.Outcome });
                open = null;
            }
        }
        if (open is not null) partners.Add(open);
        return partners;
    }

    /// <summary>Who the partner was: sex, age gap, standing and availability, drawn around the pawn (relationships.yaml past_partners).</summary>
    Partner DescribePartner(Partner p, int index, bool sameSex)
    {
        string sex = _f["pawn.sex"];
        string other = sex == "male" ? "female" : "male";
        p = p with { Sex = sameSex ? sex : other, Seed = Rng.SubSeed(_seed, "partner", index.ToString(CultureInfo.InvariantCulture)) };
        if (_data.PastPartners is not { } t) return p;

        var src = t.Source;
        var rng = Rng.For(_seed, "life", "partner", index.ToString(CultureInfo.InvariantCulture));
        double gapMean = sameSex ? 0 : sex == "female" ? src.MaleOlderYears : -src.MaleOlderYears;
        return p with
        {
            AgeGap = gapMean + src.AgeGapSd * rng.NextNormal(),
            Status = src.StatusMatch * t.OwnStatus.Eval(_f) + src.StatusSd * rng.NextNormal(),
            Availability = MathUtil.Sigmoid(t.Availability.Eval(_f, rng)),
        };
    }
}
