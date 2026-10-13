using System.Globalization;
using System.Text;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Generation;

/// <summary>Which of the three parts of a pawn a fact belongs to.</summary>
public enum PawnPart { StartingPoint, Life, Adult }

public static class PawnParts
{
    public static readonly (PawnPart Part, string Title)[] All =
        [(PawnPart.StartingPoint, "Starting point"), (PawnPart.Life, "Life"), (PawnPart.Adult, "Adult traits")];

    public static PawnPart Of(string key) =>
        GameData.IsAdultKey(key) ? PawnPart.Adult
        : GameData.IsLifeKey(key) || key.StartsWith("history:") ? PawnPart.Life
        : PawnPart.StartingPoint;
}

/// <summary>Plain-text dump of a pawn, for the debug view and the CLI.</summary>
public static class PawnText
{
    static readonly (string Prefix, string Title)[] StartSections =
    [
        ("context", "Birth context"),
        ("mother", "Mother"),
        ("father", "Father"),
        ("household", "Household"),
        ("surroundings", "Surroundings"),
        ("climate", "Home climate"),
    ];

    public static string Describe(Pawn p, GameData? data = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Seed {p.Seed}");
        sb.AppendLine($"{Cap(p.Sex)}, age {p.Age}, lives in {p.CurrentCity.Name}, born {p.BirthYear} in "
            + (p.BirthLocation == p.CurrentCity ? "the same city" : p.BirthLocation.Name));

        Part(sb, "STARTING POINT");
        sb.AppendLine("Temperament: " + string.Join(", ", p.Temperament.Select(kv =>
            string.Create(CultureInfo.InvariantCulture, $"{Pretty(kv.Key)} {kv.Value:0.00}"))));
        foreach (var (prefix, title) in StartSections)
            Section(sb, title, Rows(p, k => k.StartsWith(prefix + ".") && !k.EndsWith(".role"), prefix.Length + 1));

        if (p.History.Count > 0 || p.Facts.Categorical.Keys.Any(k => k.StartsWith("state.")))
        {
            Part(sb, "LIFE");
            Section(sb, "Now", Rows(p, k => k.StartsWith("state."), 6));
            if (p.History.Count > 0)
            {
                sb.AppendLine().AppendLine("Life story");
                foreach (var e in p.History) sb.AppendLine("  " + Line(e));
            }
            if (p.Partners.Count > 0)
            {
                sb.AppendLine().AppendLine("Partners");
                foreach (var partner in p.Partners)
                    sb.AppendLine($"  {partner.StartAge}–{partner.EndAge?.ToString(CultureInfo.InvariantCulture) ?? "now"}"
                        + (partner.Married ? ", married" : "")
                        + (partner.EndReason is null ? "" : $", ended: {Pretty(partner.EndReason)}"));
            }
        }

        if (p.Adult is not null && data?.Adult is { } adult)
        {
            Part(sb, "ADULT TRAITS");
            foreach (var (groupId, title, _) in adult.Groups)
            {
                var keys = adult.Items.Where(i => i.Group == groupId).SelectMany(i => i.Outputs.Select(o => o.Key)).ToHashSet();
                Section(sb, title, Rows(p, keys.Contains, 6)
                    .Where(r => r.Value != "none").ToList());
            }
            var inner = p.Adult.InnerLife;
            if (inner.Secrets.Count > 0)
            {
                sb.AppendLine().AppendLine("Secrets");
                foreach (var s in inner.Secrets)
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {s.Label} (hard to discover: {s.DiscoveryDifficulty:0.##})"));
            }
        }
        return sb.ToString();
    }

    static List<(string Label, string Value)> Rows(Pawn p, Func<string, bool> include, int strip) =>
        p.Facts.Categorical.Where(kv => include(kv.Key)).Select(kv => (kv.Key, Value: Pretty(kv.Value)))
            .Concat(p.Facts.Numeric.Where(kv => include(kv.Key))
                .Select(kv => (kv.Key, Value: kv.Value.ToString("0.##", CultureInfo.InvariantCulture))))
            .Select(r => (Pretty(r.Key[strip..]), r.Value))
            .ToList();

    static void Section(StringBuilder sb, string title, List<(string Label, string Value)> rows)
    {
        if (rows.Count == 0) return;
        sb.AppendLine().AppendLine(title);
        foreach (var (label, value) in rows) sb.AppendLine($"  {label}: {value}");
    }

    static void Part(StringBuilder sb, string title) =>
        sb.AppendLine().AppendLine(new string('=', 12) + " " + title + " " + new string('=', 12));

    /// <summary>One history entry as a line, like "age 9: parental divorce (mother custody), read as: people leave".</summary>
    public static string Line(HistoryEntry e)
    {
        string what = e.Type == LifeItemType.Condition
            ? $"{Pretty(e.ItemId)}: {Pretty(e.Outcome)}"
            : e.Outcome == "fired" ? Pretty(e.ItemId) : $"{Pretty(e.ItemId)} ({Pretty(e.Outcome)})";
        return $"age {e.Age}: {what}" + (e.Reading is null ? "" : $", read as: {Pretty(e.Reading)}");
    }

    static string Pretty(string id) => id.Replace('_', ' ');
    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
