using System.Globalization;
using System.Text;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Adult;

public sealed record ListPick(string List, string Id, string Level, double Intensity, double Disclosure);
public sealed record GoalPick(string Id, double Score, double Deadline, string Status);
public sealed record IssuePick(string Id, double Position, double Salience);
public sealed record DealbreakerPick(string Id, string Rule, double Hardness, bool Hard);
/// <summary>Source is "history:&lt;entry id&gt;" or "&lt;list&gt;.&lt;entry&gt;".</summary>
public sealed record Secret(string Source, string Label, double DiscoveryDifficulty);

public sealed class InnerLife
{
    public int? CoreWoundEntry { get; init; }
    public double WoundImpact { get; init; }
    public string? Belief { get; init; }
    public string? Want { get; init; }
    public string? Need { get; init; }
    public IReadOnlyList<Secret> Secrets { get; init; } = [];
    /// <summary>A fork id (the path not taken) or a missed goal id.</summary>
    public string? Regret { get; init; }
    public int? RegretEntry { get; init; }
    public double Doubt { get; init; }
}

/// <summary>The structured parts of the adult profile. Single values live in the pawn's facts as adult.*.</summary>
public sealed class AdultProfile
{
    public IReadOnlyList<ListPick> Picks { get; init; } = [];
    public IReadOnlyList<GoalPick> Goals { get; init; } = [];
    public IReadOnlyList<IssuePick> Issues { get; init; } = [];
    public IReadOnlyList<DealbreakerPick> Dealbreakers { get; init; } = [];
    public InnerLife InnerLife { get; init; } = new();
}

/// <summary>
/// Spec section 6: computes the adult profile in one pass after the life
/// simulation. Items run in file order and read base traits (as z-scores),
/// stage-weighted channels, the history, and the items above them.
/// </summary>
public sealed class AdultProfiler
{
    readonly GameData _data;
    readonly AdultTables _t;
    readonly ulong _seed;
    readonly Facts _f;
    readonly LifeResult _life;
    readonly int _age;
    readonly List<RollTrace>? _trace;
    readonly IReadOnlyDictionary<string, (double Mean, double Sd)> _norms;

    readonly List<ListPick> _picks = new();
    readonly List<GoalPick> _goals = new();
    readonly List<IssuePick> _issues = new();
    readonly List<DealbreakerPick> _dealbreakers = new();
    InnerLife _inner = new();

    AdultProfiler(GameData data, ulong seed, Facts facts, LifeResult life, int age, List<RollTrace>? trace,
                  IReadOnlyDictionary<string, (double, double)> norms)
    {
        _data = data;
        _t = data.Adult!;
        _seed = seed;
        _f = facts;
        _life = life;
        _age = age;
        _trace = trace;
        _norms = norms;
    }

    public static AdultProfile Run(GameData data, ulong seed, Facts facts, LifeResult life, int age, List<RollTrace>? trace)
    {
        var p = new AdultProfiler(data, seed, facts, life, age, trace, data.AdultNorms);
        p.Prepare();
        foreach (var item in data.Adult!.Items) p.Apply(item);
        return p.Result();
    }

    /// <summary>
    /// Spec 6.1 normalizing: generate a calibration batch, and for each z-scale
    /// trait in order, measure the raw mean and SD before normalizing it, so later
    /// traits see normalized inputs.
    /// </summary>
    internal static IReadOnlyDictionary<string, (double Mean, double Sd)> Calibrate(GameData data)
    {
        var gen = new PawnGenerator(data);
        var norms = new Dictionary<string, (double Mean, double Sd)>();
        var batch = Enumerable.Range(0, data.Adult!.CalibrationSize).Select(i =>
        {
            var pawn = gen.Generate((ulong)(9_000_000 + i), new GenerateOptions { SkipAdult = true });
            var life = new LifeResult { History = pawn.History, Partners = pawn.Partners };
            var p = new AdultProfiler(data, pawn.Seed, pawn.Facts, life, pawn.Age, null, norms);
            p.Prepare();
            return p;
        }).ToList();

        foreach (var item in data.Adult.Items)
        {
            if (item.Type == AdultType.Score && item.Scale == ScoreScale.Z)
            {
                var raws = batch.Select(p => p.RawScore(item, null)).ToList();
                double mean = raws.Average();
                double sd = Math.Sqrt(raws.Sum(r => (r - mean) * (r - mean)) / raws.Count);
                norms[item.Key] = (mean, Math.Max(sd, 1e-6));
            }
            foreach (var p in batch) p.Apply(item);
        }
        return norms;
    }

    AdultProfile Result() => new()
    {
        Picks = _picks, Goals = _goals, Issues = _issues, Dealbreakers = _dealbreakers, InnerLife = _inner,
    };

    // ------------------------------------------------------------ inputs

    void Prepare()
    {
        foreach (var t in _data.Config.Temperament.Traits)
            SetNum($"z.{t}", MathUtil.Probit(_f.Num($"pawn.{t}") ?? 0.5), "temperament percentile as a z-score (spec 6.1 trait scale)");

        var weights = _data.Life!.Stages.ToDictionary(s => s.Id, s => s.Weight);
        foreach (var c in _data.Life.Channels)
        {
            double sum = _life.History.Sum(e => e.Channels.GetValueOrDefault(c) * weights.GetValueOrDefault(e.Stage, 1));
            SetNum($"wchannel.{c}", sum, "stage-weighted channel sum (spec 6.2)");
        }

        SetNum("hist.partner_count", _life.Partners.Count, "partners in the relationship history");
        SetNum("hist.longest_relationship", _life.Partners.Count == 0 ? 0
            : _life.Partners.Max(p => (p.EndAge ?? _age) - p.StartAge), "years, longest relationship");
        SetCat("hist.moved_as_adult",
            _life.History.Any(e => e.Age >= 18 && e.Changes.Any(c => c.Key == "state.location")) ? "yes" : "no",
            "moved to a new city at 18 or later");
    }

    // ------------------------------------------------------------ items

    void Apply(AdultItem item)
    {
        switch (item.Type)
        {
            case AdultType.Score: ApplyScore(item); break;
            case AdultType.Count: ApplyCount(item); break;
            case AdultType.Blend: ApplyBlend(item); break;
            case AdultType.Roll: ApplyRoll(item); break;
            case AdultType.Choice: ApplyChoice(item); break;
            case AdultType.Derive: ApplyDerive(item); break;
            case AdultType.List: ApplyList(item); break;
            case AdultType.Goals: ApplyGoals(item); break;
            case AdultType.Issues: ApplyIssues(item); break;
            case AdultType.Dealbreakers: ApplyDealbreakers(item); break;
            case AdultType.InnerLife: ApplyInnerLife(item); break;
        }
    }

    Rng RngFor(params string[] parts) => Rng.For(_seed, ["profile", .. parts]);

    StringBuilder? Why() => _trace is null ? null : new StringBuilder();

    double Eval(Formula f, StringBuilder? why = null) => TermMath.Sum(f.Base, f.Terms, f.Mods, _f, why);

    double RawScore(AdultItem item, StringBuilder? why)
    {
        double raw = Eval(item.Formula!, why);
        if (item.Sigma > 0)
        {
            double noise = item.Sigma * RngFor(item.Id).NextNormal();
            raw += noise;
            why?.Append(string.Create(CultureInfo.InvariantCulture, $" {(noise >= 0 ? "+" : "−")}{Math.Abs(noise):0.##} noise"));
        }
        return raw;
    }

    void ApplyScore(AdultItem item)
    {
        var why = Why();
        double raw = RawScore(item, why);
        double value = item.Scale switch
        {
            ScoreScale.Z => _norms.TryGetValue(item.Key, out var n) ? Math.Clamp((raw - n.Mean) / n.Sd, -3, 3) : raw,
            ScoreScale.ZRaw => Math.Clamp(raw, -3, 3),
            ScoreScale.Unit => MathUtil.Sigmoid(raw),
            ScoreScale.Signed => Math.Tanh(raw),
            _ => Math.Clamp(raw, 0, 1),
        };
        if (why is not null)
        {
            why.Insert(0, "raw = ");
            why.Append(string.Create(CultureInfo.InvariantCulture, $" = {raw:0.##}"));
            why.Append(item.Scale switch
            {
                ScoreScale.Z when _norms.TryGetValue(item.Key, out var n) =>
                    string.Create(CultureInfo.InvariantCulture, $"; z = (raw − {n.Mean:0.##}) / {n.Sd:0.##}, clamped to ±3"),
                ScoreScale.ZRaw => "; clamped to ±3",
                ScoreScale.Unit => "; value = sigmoid(raw)",
                ScoreScale.Signed => "; value = tanh(raw)",
                ScoreScale.Clamp01 => "; clamped to 0–1",
                _ => "",
            });
        }
        SetNum(item.Key, value, why?.ToString());
    }

    void ApplyCount(AdultItem item)
    {
        var why = Why();
        double logMean = Eval(item.Formula!, why);
        var rng = RngFor(item.Id);
        double mean = Math.Exp(logMean + item.Sigma * rng.NextNormal());
        int value = Math.Min(item.Cap, MathUtil.Poisson(rng, mean));
        why?.Insert(0, "log mean = ").Append(string.Create(CultureInfo.InvariantCulture,
            $" → mean {Math.Exp(logMean):0.#} (with noise {mean:0.#}) → Poisson draw, capped at {item.Cap}"));
        SetNum(item.Key, value, why?.ToString());
    }

    void ApplyBlend(AdultItem item)
    {
        var why = Why();
        double stated = _f.Num(item.Stated!) ?? 0;
        double pull = Eval(item.Formula!, why);
        if (item.Sigma > 0) pull += item.Sigma * RngFor(item.Id).NextNormal();
        double insight = _f.Num("adult.self_insight") ?? 0;
        double gap = 1 - MathUtil.Sigmoid(insight);
        double value = stated + gap * (pull - stated);
        why?.Insert(0, "pull = ").Append(string.Create(CultureInfo.InvariantCulture,
            $"\nrevealed = stated {stated:0.##} + (1 − sigmoid(self_insight {insight:0.##})) × (pull {pull:0.##} − stated) = {value:0.##}"));
        SetNum(item.Key, value, why?.ToString());
    }

    void ApplyRoll(AdultItem item)
    {
        var table = item.Table!;
        var weights = (double[])table.BaseWeights.Clone();
        foreach (var key in item.Inherit)
            foreach (var v in FactTest.Values(key, _f))
                if (Array.IndexOf(table.Values, v) is var i and >= 0) weights[i] += item.InheritWeight;
        var baseWeights = (double[])weights.Clone();
        var mods = new List<ModTrace>();
        foreach (var mod in table.Mods)
        {
            bool fired = mod.When.All(c => FactTest.Test(c, _f));
            if (fired) foreach (var (index, factor) in mod.Mul) weights[index] *= factor;
            if (_trace is not null)
                mods.Add(new ModTrace(mod.Number, string.Join(" and ", mod.When.Select(c => c.Text)),
                    string.Join(", ", mod.Mul.Select(m => string.Create(CultureInfo.InvariantCulture, $"{table.Values[m.Index]} x{m.Factor}"))), fired));
        }
        int pick = RngFor(item.Id).PickWeighted(weights);
        string value = pick >= 0 ? table.Values[pick] : table.Values[0];
        _f.Set(item.Key, value);
        _trace?.Add(new RollTrace
        {
            Key = item.Key, Source = RollSource.Drawn, Value = value, Values = table.Values,
            BaseWeights = baseWeights, FinalWeights = weights, Mods = mods,
            Note = item.Inherit.Length > 0 ? $"base weights plus {item.InheritWeight.ToString("0.##", CultureInfo.InvariantCulture)} for the value of {string.Join(", ", item.Inherit)}" : null,
        });
    }

    void ApplyChoice(AdultItem item)
    {
        var eligible = item.Options.Where(o => o.Weight > 0 && o.Requires.All(c => FactTest.Test(c, _f))).ToArray();
        if (eligible.Length == 0) return;
        var scores = eligible.Select(o => Math.Log(o.Weight) + TermMath.Sum(0, o.Terms, o.Mods, _f) / item.Temperature).ToArray();
        double max = scores.Max();
        var probs = scores.Select(s => Math.Exp(s - max)).ToArray();
        int pick = RngFor(item.Id).PickWeighted(probs);
        _f.Set(item.Key, eligible[pick].Id);
        if (_trace is null) return;
        var all = item.Options.Select(o => Array.IndexOf(eligible, o) is var i and >= 0 ? probs[i] : 0).ToArray();
        _trace.Add(new RollTrace
        {
            Key = item.Key, Source = RollSource.Drawn, Value = eligible[pick].Id, Values = item.Values,
            BaseWeights = item.Options.Select(o => o.Weight).ToArray(), FinalWeights = all,
            Note = $"chance ∝ weight × exp(utility / {item.Temperature.ToString("0.##", CultureInfo.InvariantCulture)})",
        });
    }

    void ApplyDerive(AdultItem item)
    {
        foreach (var rule in item.Rules)
            if (rule.When.All(c => FactTest.Test(c, _f)))
            {
                SetCat(item.Key, rule.Value, rule.When.Length == 0 ? "no other rule matched"
                    : "first matching rule: " + string.Join(" and ", rule.When.Select(c => c.Text)));
                return;
            }
    }

    // Spec 6.7 kinks, and the condition table in "Social circle and health" 2.3.
    void ApplyList(AdultItem item)
    {
        string sex = _f.Cat("pawn.sex") ?? "";
        var fired = new List<(ListEntry Entry, double P)>();
        var notes = new Dictionary<string, string>();
        foreach (var e in item.Entries)
        {
            if (e.Sex is not null && e.Sex != sex)
            {
                notes[e.Id] = $"only for {e.Sex} pawns";
                continue;
            }
            double p = MathUtil.Sigmoid(MathUtil.Logit(e.Prevalence) + TermMath.Sum(0, e.Terms, e.Mods, _f));
            double roll = RngFor(item.Id, e.Id).NextDouble();
            if (roll < p) fired.Add((e, p));
            notes[e.Id] = string.Create(CultureInfo.InvariantCulture, $"p {p:0.###}, roll {roll:0.###} → {(roll < p ? "fired" : "no")}");
        }
        var kept = fired.OrderByDescending(x => x.P).Take(item.Max).Select(x => x.Entry).ToHashSet();

        foreach (var e in item.Entries)
        {
            string key = $"adult.{item.Id}.{e.Id}";
            if (!kept.Contains(e))
            {
                string why = notes.GetValueOrDefault(e.Id, "") + (fired.Any(x => x.Entry == e) ? $"; dropped (only the {item.Max} most likely are kept)" : "");
                SetCat(key, "none", why);
                continue;
            }
            var rng = RngFor(item.Id, e.Id, "detail");
            double intensity = MathUtil.Sigmoid(Eval(e.Intensity) + 0.8 * rng.NextNormal());
            string level = item.Levels[Math.Min(item.Levels.Length - 1, (int)(intensity * item.Levels.Length))];
            double disclosure = e.Visible ? 1 : MathUtil.Sigmoid(Eval(item.Disclosure!) - 2 * e.Stigma);
            _picks.Add(new ListPick(item.Id, e.Id, level, intensity, disclosure));
            SetCat(key, level, notes[e.Id] + string.Create(CultureInfo.InvariantCulture,
                $"; intensity {intensity:0.##} → {level}; disclosure {disclosure:0.##}{(e.Visible ? " (visible)" : "")}"));
        }
    }

    // Spec 6.8 life goals: Gumbel-top-k on the scores, then deadlines.
    void ApplyGoals(AdultItem item)
    {
        var rng = RngFor(item.Id);
        var scored = item.Goals.Select(g =>
        {
            double score = Eval(g.Score);
            double gumbel = -Math.Log(-Math.Log(Math.Max(rng.NextDouble(), 1e-12)));
            return (Goal: g, Score: score, Key: score + gumbel);
        }).ToList();
        int k = item.PickMin + rng.NextInt(0, item.PickMax - item.PickMin + 1);
        var picked = scored.OrderByDescending(x => x.Key).Take(k).Select(x => x.Goal).ToHashSet();

        foreach (var (g, score, _) in scored)
        {
            string key = $"adult.{item.Id}.{g.Id}";
            if (!picked.Contains(g))
            {
                SetCat(key, "none", string.Create(CultureInfo.InvariantCulture, $"score {score:0.##}; not among the {k} goals picked"));
                continue;
            }
            double deadline = g.Deadline + TermMath.Sum(0, g.DeadlineTerms, [], _f) + 2 * rng.NextNormal();
            string status = g.Achieved.Length > 0 && g.Achieved.All(c => FactTest.Test(c, _f)) ? "achieved"
                : deadline < _age ? "missed" : "active";
            _goals.Add(new GoalPick(g.Id, score, deadline, status));
            SetCat(key, status, string.Create(CultureInfo.InvariantCulture, $"score {score:0.##}; picked; deadline age {deadline:0}"));
        }
    }

    // Political Alignment: issue positions from the axes, personal stakes, and noise scaled by inconsistency.
    void ApplyIssues(AdultItem item)
    {
        double consistency = Math.Clamp(_f.Num(item.Consistency!) ?? 0.5, 0, 1);
        foreach (var issue in item.Issues)
        {
            var rng = RngFor(item.Id, issue.Id);
            var why = Why();
            double fromAxes = TermMath.Sum(0, issue.Axes, [], _f, why);
            double shift = 0, salience = issue.BaseSalience;
            foreach (var stake in issue.Stakes.Where(s => s.When.All(c => FactTest.Test(c, _f))))
            {
                shift += stake.Shift;
                salience += stake.Salience;
                why?.Append($"; stake: {stake.Note ?? string.Join(" and ", stake.When.Select(c => c.Text))}");
            }
            double noise = rng.NextNormal() * item.Noise * (1 - consistency);
            double position = Math.Clamp(fromAxes + shift + noise, -1, 1);
            salience = Math.Clamp(salience + 0.05 * rng.NextNormal(), 0, 1);
            _issues.Add(new IssuePick(issue.Id, position, salience));
            why?.Insert(0, "axes:").Append(string.Create(CultureInfo.InvariantCulture,
                $"\nposition = axes {fromAxes:0.##} + stakes {shift:0.##} + noise {noise:0.##} × (1 − consistency {consistency:0.##}) = {position:0.##}"));
            SetNum($"adult.{item.Id}.{issue.Id}", position, why?.ToString());
            SetNum($"adult.{item.Id}.{issue.Id}_salience", salience, "base salience plus personal stakes");
        }
    }

    // Spec 6.8 dealbreakers.
    void ApplyDealbreakers(AdultItem item)
    {
        foreach (var rule in item.Dealbreakers)
        {
            string key = $"adult.{item.Id}.{rule.Id}";
            if (!rule.When.All(c => FactTest.Test(c, _f)))
            {
                SetCat(key, "none", "source condition doesn't hold: " + string.Join(" and ", rule.When.Select(c => c.Text)));
                continue;
            }
            double hardness = Math.Clamp(Eval(rule.Hardness), 0, 1);
            bool hard = hardness >= item.HardAt;
            _dealbreakers.Add(new DealbreakerPick(rule.Id, rule.Rule, hardness, hard));
            SetCat(key, hard ? "hard" : "soft", string.Create(CultureInfo.InvariantCulture,
                $"{rule.Rule}; hardness {hardness:0.##} ({(hard ? "ends a date when revealed" : "lowers attraction")})"));
        }
    }

    // Spec 6.9 inner life.
    void ApplyInnerLife(AdultItem item)
    {
        var history = _life.History;

        // Core wound: the negative event with the highest impact, earliest on ties.
        var wound = history.Where(e => e.Negative && e.Type == LifeItemType.Event && e.Impact >= item.WoundThreshold)
            .OrderByDescending(e => e.Impact).ThenBy(e => e.Id).FirstOrDefault();
        Belief? belief = wound?.Reading is null ? null
            : item.Beliefs.FirstOrDefault(b => b.Item == wound.ItemId && b.Reading == wound.Reading)
              ?? item.Beliefs.FirstOrDefault(b => b.Item is null && b.Reading == wound.Reading);
        SetCat("adult.core_wound", wound?.ItemId ?? "none", wound is null
            ? $"no negative event reached impact {item.WoundThreshold.ToString("0.##", CultureInfo.InvariantCulture)}"
            : string.Create(CultureInfo.InvariantCulture, $"{PawnText.Line(wound)}; impact {wound.Impact:0.##}"));
        SetNum("adult.wound_impact", wound?.Impact ?? 0, "impact of the core wound (0 without one)");
        SetCat("adult.private_belief", belief?.Id ?? "none", belief is null ? "no core wound, or its reading has no belief" : $"from reading '{wound!.Reading}'");
        SetCat("adult.want", belief?.Want ?? "none", "what the wound makes the pawn chase");
        SetCat("adult.need", belief?.Need ?? "none", "what would heal the wound");

        // Secrets: hideable history entries, plus list picks the pawn rarely discloses.
        var candidates = new List<(string Source, string Label, double Stigma)>();
        foreach (var e in history)
        {
            double stigma = item.Hideable.TryGetValue($"{e.ItemId}:{e.Outcome}", out var s1) ? s1
                : item.Hideable.TryGetValue(e.ItemId, out var s2) ? s2 : 0;
            if (stigma > 0) candidates.Add(($"history:{e.Id}", PawnText.Line(e), stigma));
        }
        foreach (var p in _picks.Where(p => p.Disclosure < 0.2))
        {
            var entry = _data.Adult!.Items.First(i => i.Id == p.List).Entries.First(x => x.Id == p.Id);
            candidates.Add(($"{p.List}.{p.Id}", $"{p.List}: {p.Id}", Math.Max(entry.Stigma, 0.1)));
        }
        var rng = RngFor("inner_life", "secrets");
        double threat = MathUtil.Sigmoid(_f.Num($"z.{_data.Config.Temperament.Traits.FirstOrDefault()}") ?? 0);
        int count = Math.Min(candidates.Count, rng.NextInt(0, item.MaxSecrets + 1));
        var secrets = new List<Secret>();
        var pool = candidates.ToList();
        for (int i = 0; i < count; i++)
        {
            int pick = rng.PickWeighted(pool.Select(c => c.Stigma * (0.5 + threat)).ToArray());
            if (pick < 0) break;
            var c = pool[pick];
            pool.RemoveAt(pick);
            secrets.Add(new Secret(c.Source, c.Label, Math.Clamp(c.Stigma + 0.15 * rng.NextNormal(), 0, 1)));
        }
        SetNum("adult.secret_count", secrets.Count, secrets.Count == 0 ? "no secrets drawn"
            : "secrets: " + string.Join("; ", secrets.Select(s => s.Label)));

        // Regret: the fork whose road not taken looks best to the adult, or a missed goal.
        string? regret = null;
        int? regretEntry = null;
        double best = item.RegretThreshold;
        var forks = _data.Life!.Items.Where(i => i.Type == LifeItemType.Fork).ToDictionary(i => i.Id);
        // Re-chosen (sticky) forks like a career only count their latest choice.
        var lastSticky = history.Where(e => e.Type == LifeItemType.Fork && forks[e.ItemId].StickyKey is not null)
            .GroupBy(e => e.ItemId).Select(g => g.Last().Id).ToHashSet();
        foreach (var e in history.Where(e => e.Type == LifeItemType.Fork))
        {
            var fork = forks[e.ItemId];
            if (fork.StickyKey is not null && !lastSticky.Contains(e.Id)) continue;
            double Utility(Choice o) => Math.Log(Math.Max(o.Weight, 1e-9)) + TermMath.Sum(0, o.Terms, o.Mods, _f) / fork.Temperature;
            var chosen = fork.Options.FirstOrDefault(o => o.Id == e.Outcome);
            if (chosen is null || fork.Options.Length < 2) continue;
            // Staying where you are has value of its own.
            double stay = fork.StickyKey is not null && _f.Cat(fork.StickyKey) == chosen.Id ? fork.StickyBonus / fork.Temperature : 0;
            double doubt = fork.Options.Where(o => o != chosen).Max(Utility) - Utility(chosen) - stay;
            if (doubt > best) (best, regret, regretEntry) = (doubt, fork.Id, e.Id);
        }
        foreach (var g in _goals.Where(g => g.Status == "missed"))
            if (g.Score > best) (best, regret, regretEntry) = (g.Score, g.Id, null);
        SetCat("adult.regret", regret ?? "none", regret is null
            ? "no road not taken or missed goal stands out"
            : string.Create(CultureInfo.InvariantCulture, $"doubt {best:0.##}")
              + (regretEntry is int r ? $": {PawnText.Line(history[r])}" : ": missed goal"));

        _inner = new InnerLife
        {
            CoreWoundEntry = wound?.Id, WoundImpact = wound?.Impact ?? 0,
            Belief = belief?.Id, Want = belief?.Want, Need = belief?.Need,
            Secrets = secrets, Regret = regret, RegretEntry = regretEntry, Doubt = regret is null ? 0 : best,
        };
    }

    // ------------------------------------------------------------ output

    void SetNum(string key, double value, string? note)
    {
        _f.Set(key, value);
        _trace?.Add(new RollTrace { Key = key, Source = RollSource.Derived, Number = value, Note = note });
    }

    void SetCat(string key, string value, string? note)
    {
        _f.Set(key, value);
        _trace?.Add(new RollTrace { Key = key, Source = RollSource.Derived, Value = value, Note = note });
    }
}
