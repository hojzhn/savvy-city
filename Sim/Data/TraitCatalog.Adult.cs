using System.Globalization;
using YouWillDate.Sim.Adult;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Data;

// Catalog entries for the adult profile (spec section 6).
public sealed partial class TraitCatalog
{
    static void AddAdult(List<TraitInfo> list, GameData data)
    {
        var adult = data.Adult!;
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        foreach (var t in data.Config.Temperament.Traits)
            list.Add(new TraitInfo
            {
                Key = $"z.{t}", Section = "Adult inputs", Label = $"{t} (z)", Kind = TraitKind.Adult,
                FactKeys = [$"z.{t}"], Reads = [$"pawn.{t}"],
                HowDetermined = $"The temperament percentile pawn.{t} as a z-score, the trait scale of spec 6.1.",
            });
        foreach (var c in data.Life!.Channels)
            list.Add(new TraitInfo
            {
                Key = $"wchannel.{c}", Section = "Adult inputs", Label = $"{c} (weighted)", Kind = TraitKind.Adult,
                FactKeys = [$"wchannel.{c}"], Reads = [$"channel.{c}"],
                HowDetermined = "Sum of this channel's deltas over the life history, each times its stage weight (spec 6.2): " +
                                string.Join(", ", data.Life.Stages.Select(s => $"{s.Id} {N(s.Weight)}")) + ".",
            });
        list.Add(new TraitInfo
        {
            Key = "hist.partner_count", Section = "Adult inputs", Label = "partner count", Kind = TraitKind.Adult,
            FactKeys = ["hist.partner_count"], HowDetermined = "Number of partners in the relationship history.",
        });
        list.Add(new TraitInfo
        {
            Key = "hist.longest_relationship", Section = "Adult inputs", Label = "longest relationship", Kind = TraitKind.Adult,
            FactKeys = ["hist.longest_relationship"], HowDetermined = "Years of the longest relationship in the history (an ongoing one counts to now).",
        });
        list.Add(new TraitInfo
        {
            Key = "hist.ex_count", Section = "Adult inputs", Label = "past partners", Kind = TraitKind.Adult,
            FactKeys = ["hist.ex_count"], HowDetermined = "Number of partners in the history whose relationship ended.",
        });
        list.Add(new TraitInfo
        {
            Key = "hist.ex_status", Section = "Adult inputs", Label = "past partners' standing", Kind = TraitKind.Adult,
            FactKeys = ["hist.ex_status"],
            HowDetermined = "Mean social standing (z-scale) of past partners, weighted by years together (spec 6.8 history pull). " +
                            "Each partner's standing is drawn around the pawn's own (relationships.yaml past_partners). 0 without past partners.",
        });
        list.Add(new TraitInfo
        {
            Key = "hist.ex_availability", Section = "Adult inputs", Label = "past partners' availability", Kind = TraitKind.Adult,
            FactKeys = ["hist.ex_availability"],
            HowDetermined = "Mean emotional availability of past partners minus 0.5, weighted by years together (spec 6.8 history pull). " +
                            "Anxious, inconsistently raised pawns draw less available partners. 0 without past partners.",
        });
        list.Add(new TraitInfo
        {
            Key = "hist.moved_as_adult", Section = "Adult inputs", Label = "moved as an adult", Kind = TraitKind.Adult,
            FactKeys = ["hist.moved_as_adult"], Values = ["yes", "no"],
            HowDetermined = "'yes' if the pawn moved to a new city at 18 or later.",
        });

        foreach (var item in adult.Items)
        {
            var reads = AdultReads(item);
            string how = DescribeAdult(item, N);
            foreach (var (key, values) in item.Outputs)
            {
                string sub = key == item.Key ? item.Id : key.StartsWith(item.Key + ".") ? key[(item.Key.Length + 1)..] : key[6..];
                list.Add(new TraitInfo
                {
                    Key = key, Section = item.GroupTitle, Label = item.Outputs.Count > 1 && key != item.Key ? $"{item.Id}: {sub}" : item.Id,
                    Kind = TraitKind.Adult, Note = item.Note, FactKeys = [key], Values = values, Reads = reads,
                    HowDetermined = how,
                });
            }
        }
    }

    static List<string> AdultReads(AdultItem item)
    {
        var keys = new List<string>();
        void Formula(Formula? f)
        {
            if (f is null) return;
            keys.AddRange(f.Terms.Select(t => t.Key));
            foreach (var m in f.Mods) keys.AddRange(m.When.Select(c => c.Key));
        }
        Formula(item.Formula);
        Formula(item.Disclosure);
        if (item.Table is { } t)
            foreach (var m in t.Mods) keys.AddRange(m.When.Select(c => c.Key));
        keys.AddRange(item.Inherit);
        foreach (var o in item.Options)
        {
            keys.AddRange(o.Terms.Select(x => x.Key));
            keys.AddRange(o.Requires.Select(c => c.Key));
            foreach (var m in o.Mods) keys.AddRange(m.When.Select(c => c.Key));
        }
        foreach (var r in item.Rules) keys.AddRange(r.When.Select(c => c.Key));
        foreach (var e in item.Entries)
        {
            keys.AddRange(e.Terms.Select(x => x.Key));
            foreach (var m in e.Mods) keys.AddRange(m.When.Select(c => c.Key));
            Formula(e.Intensity);
        }
        foreach (var g in item.Goals)
        {
            Formula(g.Score);
            keys.AddRange(g.DeadlineTerms.Select(x => x.Key));
            keys.AddRange(g.Achieved.Select(c => c.Key));
        }
        foreach (var i in item.Issues)
        {
            keys.AddRange(i.Axes.Select(x => x.Key));
            foreach (var s in i.Stakes) keys.AddRange(s.When.Select(c => c.Key));
        }
        if (item.Consistency is not null) keys.Add(item.Consistency);
        if (item.Stated is not null) keys.AddRange([item.Stated, "adult.self_insight"]);
        foreach (var d in item.Dealbreakers)
        {
            keys.AddRange(d.When.Select(c => c.Key));
            Formula(d.Hardness);
        }
        foreach (var s in item.Sources) Formula(s.Formula);
        Formula(item.Closeness);
        if (item.CountKey is not null) keys.Add(item.CountKey);
        if (item.StatusKey is not null) keys.Add(item.StatusKey);
        var own = item.Outputs.Select(o => o.Key).ToHashSet();
        return keys.Select(GameData.ConditionSchemaKey).Where(k => !own.Contains(k)).Distinct().ToList();
    }

    static string DescribeAdult(AdultItem item, Func<double, string> n) => item.Type switch
    {
        AdultType.Score => $"Score: raw = {n(item.Formula!.Base)} + {item.Formula.Terms.Length} weight(s) + {item.Formula.Mods.Length} mod(s)"
            + (item.Sigma > 0 ? $" + noise (SD {n(item.Sigma)})" : "") + ". " + item.Scale switch
            {
                ScoreScale.Z => "Normalized to the population z-scale (mean 0, SD 1) on a calibration batch, clamped to ±3 (spec 6.1).",
                ScoreScale.ZRaw => "Kept on the z-scale as is, clamped to ±3.",
                ScoreScale.Unit => "Value = sigmoid(raw), from 0 to 1.",
                ScoreScale.Signed => "Value = tanh(raw), from -1 to 1.",
                _ => "Clamped to 0–1.",
            },
        AdultType.Count => $"Count: Poisson draw with log mean = {n(item.Formula!.Base)} + {item.Formula.Terms.Length} weight(s), plus noise (SD {n(item.Sigma)}) for overdispersion; capped at {item.Cap}.",
        AdultType.Blend => $"Revealed preference (spec 6.8): starts from {item.Stated} and moves toward a pull from history " +
                           "by (1 − sigmoid(self_insight)), so pawns with little self-insight pick very differently from what they say.",
        AdultType.Roll => "Weighted roll over its values" + (item.Inherit.Length > 0 ? $", favouring the values of {string.Join(" and ", item.Inherit)}" : "")
                          + $", with {item.Table!.Mods.Length} modifier(s).",
        AdultType.Choice => $"Choice among {item.Options.Length} options: chance ∝ weight × exp(utility / {n(item.Temperature)}).",
        AdultType.Derive => $"Derived from the first of {item.Rules.Length} rules that matches.",
        AdultType.List => $"Each entry fires with p = sigmoid(logit(prevalence) + weights); at most {item.Max} are kept (the most likely). " +
                          $"Kept entries get an intensity ({string.Join(" / ", item.Levels)}) and a disclosure: how readily the pawn reveals it, lowered by stigma.",
        AdultType.Goals => $"Life goals (spec 6.8): {item.PickMin}–{item.PickMax} goals picked by Gumbel-top-k on their scores. " +
                           "Each gets a deadline age; a deadline before the current age marks it missed unless it was achieved.",
        AdultType.Issues => "Political Alignment: position = weighted political axes + personal stakes + noise × (1 − consistency), " +
                            "from -1 (progressive) to +1 (traditional). Salience rises with personal stakes.",
        AdultType.Dealbreakers => $"Dealbreakers (spec 6.8): a rule applies when its source condition holds. Hardness at or above {n(item.HardAt)} ends a date when the attribute is revealed; below that it lowers attraction.",
        AdultType.FriendSources => $"Friend sources (social circle and health 3.4): shares drawn from a Dirichlet with weight × exp(weights) × {n(item.Concentration)} per source. " +
                                   $"Then one record per close friend ({item.CountKey}): a source by share (only sources the pawn was old enough for), years known, " +
                                   $"closeness, standing around {item.StatusKey}, and sex (same as the pawn's with chance {n(item.SameSex)}). " +
                                   "friend_closeness is the friends' mean closeness.",
        _ => "Inner life (spec 6.9): the core wound is the negative event with the highest impact; its reading maps to a private belief, a want and a need. " +
             "Secrets come from hideable events and rarely disclosed list entries. Regret is the past choice whose road not taken looks best to the adult, or a missed goal.",
    };
}
