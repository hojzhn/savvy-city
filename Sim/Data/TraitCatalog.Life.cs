using System.Globalization;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Data;

// Catalog entries for the life simulation: its state, events and forks, channels, and clock.
public sealed partial class TraitCatalog
{
    static void AddLife(List<TraitInfo> list, GameData data)
    {
        var life = data.Life!;
        var source = data.LifeSource!;
        string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // Who writes each state and channel, for "changed by" text.
        var writers = new Dictionary<string, List<string>>();
        void Wrote(string key, string by)
        {
            if (!writers.TryGetValue(key, out var l)) writers[key] = l = new();
            if (!l.Contains(by)) l.Add(by);
        }
        foreach (var item in life.Items)
            foreach (var e in EffectsOf(item))
            {
                foreach (var (k, _) in e.Set) Wrote(k, item.Id);
                foreach (var (k, _) in e.Add) Wrote(k, item.Id);
                foreach (var k in e.Reset.Concat(e.RecordFirstAge)) Wrote(k, item.Id);
                foreach (var (c, _) in e.Channels) Wrote($"channel.{c}", item.Id);
            }
        string ChangedBy(string key) =>
            writers.TryGetValue(key, out var l) ? " Changed by: " + string.Join(", ", l) + "." : "";

        list.Add(new TraitInfo
        {
            Key = "age", Section = "Life clock", Label = "age", Kind = TraitKind.Life, FactKeys = ["age"],
            HowDetermined = "The year of life being simulated, from 0 to the pawn's current age.",
        });
        list.Add(new TraitInfo
        {
            Key = "cal_year", Section = "Life clock", Label = "calendar year", Kind = TraitKind.Life, FactKeys = ["cal_year"],
            HowDetermined = "Birth year + age. Base rates (rate.<name>) are looked up by this year in eras.yaml.",
        });
        list.Add(new TraitInfo
        {
            Key = "stage", Section = "Life clock", Label = "stage", Kind = TraitKind.Life, FactKeys = ["stage"],
            Values = life.Stages.Select(s => s.Id).ToList(),
            HowDetermined = "Life stage for the age: " + string.Join(", ",
                life.Stages.Select(s => $"{s.Id} {s.From}–{(s.To >= 120 ? "now" : s.To.ToString(CultureInfo.InvariantCulture))}")) + ".",
        });
        foreach (var (field, values) in ContextSchema.Values)
            list.Add(new TraitInfo
            {
                Key = $"place.{field}", Section = "Life clock", Label = $"place {field}", Kind = TraitKind.Life,
                FactKeys = [$"place.{field}"], Values = values, Reads = ["state.location", "cal_year"],
                HowDetermined = "Where the pawn lives in the year being simulated: the location table for state.location " +
                                "and the calendar year. At the end it describes the current city now.",
            });
        list.Add(new TraitInfo
        {
            Key = "place.location", Section = "Life clock", Label = "place", Kind = TraitKind.Life, FactKeys = ["place.location"],
            Values = data.Locations.Select(l => l.Id).ToList(), Reads = ["state.location"],
            HowDetermined = "Same as state.location for the year being simulated.",
        });

        list.Add(new TraitInfo
        {
            Key = "state.location", Section = "Life state", Label = "location", Kind = TraitKind.Life,
            FactKeys = ["state.location"], Values = data.Locations.Select(l => l.Id).ToList(),
            Reads = ["pawn.birth_location"],
            HowDetermined = "Built in. Starts at the birth location." + ChangedBy("state.location"),
        });
        list.Add(new TraitInfo
        {
            Key = "state.lives_in_current_city", Section = "Life state", Label = "lives in current city", Kind = TraitKind.Life,
            FactKeys = ["state.lives_in_current_city"], Values = ["yes", "no"], Reads = ["state.location", "pawn.current_city"],
            HowDetermined = "Built in: 'yes' once state.location is the game's city.",
        });

        foreach (var (item, dto) in life.Items.Zip(source.Items))
        {
            var reads = ReadsOf(item);
            if (item.Type == LifeItemType.Condition)
            {
                list.Add(new TraitInfo
                {
                    Key = item.Key, Section = "Life state", Label = item.Id, Kind = TraitKind.Life, Note = item.Note,
                    FactKeys = [item.Key], Values = item.IsNumeric ? null : item.Values, Reads = reads,
                    HowDetermined = DescribeCondition(item) + ChangedBy(item.Key) + " Value shown is the one at the pawn's current age.",
                });
                continue;
            }
            list.Add(new TraitInfo
            {
                Key = $"life.{item.Id}", Section = item.Type == LifeItemType.Event ? "Life events" : "Life choices",
                Label = item.Id, Kind = TraitKind.Life, Note = item.Note, FactKeys = [$"life.{item.Id}"],
                Values = item.SummaryValues, Reads = reads,
                HowDetermined = (item.Type == LifeItemType.Event ? DescribeEvent(item, Num) : DescribeFork(item)) + " " + Schedule(item, life),
            });
        }

        foreach (var c in life.Channels)
            list.Add(new TraitInfo
            {
                Key = $"channel.{c}", Section = "Life channels", Label = c, Kind = TraitKind.Life, FactKeys = [$"channel.{c}"],
                HowDetermined = "Accumulator channel (spec 6.2): the raw sum of deltas written by events, outcomes and readings. " +
                                "The adult profile weights each delta by its stage." + ChangedBy($"channel.{c}"),
            });
    }

    static IEnumerable<Effects> EffectsOf(LifeItem item) =>
        new[] { item.Effects }.Concat(item.Outcomes.Concat(item.Readings).Concat(item.Options).Select(c => c.Effects));

    static List<string> ReadsOf(LifeItem item)
    {
        var keys = new List<string>();
        void Conds(IEnumerable<Condition> cs) => keys.AddRange(cs.Select(c => c.Key));
        void Rate(Rate? r)
        {
            if (r is null) return;
            keys.AddRange(r.Terms.Select(t => t.Key));
            foreach (var m in r.Mods) Conds(m.When);
        }
        Conds(item.Requires);
        Conds(item.While);
        foreach (var rule in item.Rules.Concat(item.InitialRules)) Conds(rule.When);
        if (item.Table is { } t)
        {
            Conds(t.OnlyIf);
            foreach (var m in t.Mods) Conds(m.When);
        }
        Rate(item.Rate);
        Rate(item.Up);
        Rate(item.Down);
        foreach (var c in item.Outcomes.Concat(item.Readings).Concat(item.Options))
        {
            Conds(c.Requires);
            keys.AddRange(c.Terms.Select(x => x.Key));
            foreach (var m in c.Mods) Conds(m.When);
        }
        if (item.Initial is not null) keys.Add(item.Initial);
        return keys.Select(GameData.ConditionSchemaKey).Where(k => k != item.Key).Distinct().ToList();
    }

    static string DescribeCondition(LifeItem item)
    {
        string start = item.Initial is not null ? $"Starts as {item.Initial}."
            : item.InitialValue is not null ? $"Starts as '{item.InitialValue}'."
            : item.InitialRules.Length > 0 ? "Starting value from its initial rules."
            : "";
        return item.Kind switch
        {
            ConditionKind.State => $"Life state. {start}",
            ConditionKind.Number => $"Number. {start}",
            ConditionKind.Roll => $"Weighted roll with {item.Table!.Mods.Length} modifier(s), re-rolled {Freq(item)}. {start}",
            ConditionKind.Drift => $"Moves one step up or down {Freq(item)}, with logistic up/down rates. {start}",
            ConditionKind.Derive => $"Derived every year from the first of {item.Rules.Length} rules that matches.",
            ConditionKind.Counter => "Counts years; +1 each year while its conditions hold, otherwise back to 0.",
            _ => "",
        };
    }

    static string Freq(LifeItem item) =>
        item.Ages is not null ? "at ages " + string.Join(", ", item.Ages)
        : item.Frequency switch { Frequency.Once => "once", Frequency.PerStage => "once per stage", _ => "every year" };

    static string DescribeEvent(LifeItem item, Func<double, string> num)
    {
        var parts = new List<string>
        {
            "Event: p = sigmoid(logit(base) + weights + mods), with base " +
            (item.Rate!.BaseKey is { } k ? $"rate.{k} from eras.yaml by calendar year" : num(item.Rate.BaseValue!.Value)) +
            $", {item.Rate.Terms.Length} weight(s) and {item.Rate.Mods.Length} mod(s).",
        };
        if (item.Requires.Length > 0) parts.Add("Only when " + string.Join(" and ", item.Requires.Select(c => c.Text)) + ".");
        if (item.MustHappen) parts.Add("Forced in the last simulated year if it hasn't happened.");
        if (item.Outcomes.Length > 0) parts.Add("Outcomes: " + string.Join(", ", item.Outcomes.Select(o => o.Id)) + ".");
        if (item.Readings.Length > 0) parts.Add("Readings (by temperament): " + string.Join(", ", item.Readings.Select(o => o.Id)) + ".");
        return string.Join(" ", parts);
    }

    static string DescribeFork(LifeItem item)
    {
        var parts = new List<string>
        {
            $"Fork: picks one of {item.Options.Length} options, chance ∝ weight × exp(utility / {item.Temperature.ToString("0.##", CultureInfo.InvariantCulture)}).",
        };
        if (item.Requires.Length > 0) parts.Add("Only when " + string.Join(" and ", item.Requires.Select(c => c.Text)) + ".");
        if (item.StickyKey is not null)
            parts.Add($"Keeping the current {item.StickyKey} gets +{item.StickyBonus.ToString("0.##", CultureInfo.InvariantCulture)} utility, so changes are occasional.");
        parts.Add("'not_reached' means its conditions never held.");
        return string.Join(" ", parts);
    }

    static string Schedule(LifeItem item, LifeTables life)
    {
        string window = item.WindowTo >= 120 && item.WindowFrom == 0 ? "any age"
            : item.WindowTo >= 120 ? $"from age {item.WindowFrom}"
            : $"ages {item.WindowFrom}–{item.WindowTo}";
        string max = item.Max > 0 && item.Frequency != Frequency.Once ? $", at most {item.Max} time(s)" : "";
        return item.Ages is not null ? $"Rolls at ages {string.Join(", ", item.Ages)}{max}." : $"Rolls {Freq(item)}, {window}{max}.";
    }
}
