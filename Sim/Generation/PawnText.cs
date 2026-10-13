using System.Globalization;
using System.Text;

namespace YouWillDate.Sim.Generation;

/// <summary>Plain-text dump of a pawn, for the debug view and the CLI.</summary>
public static class PawnText
{
    static readonly (string Prefix, string Title)[] Sections =
    [
        ("context", "Context"),
        ("mother", "Mother"),
        ("father", "Father"),
        ("household", "Household"),
        ("surroundings", "Surroundings"),
        ("climate", "Home climate"),
    ];

    public static IReadOnlySet<string> SectionTitles { get; } = Sections.Select(s => s.Title).ToHashSet();

    public static string Describe(Pawn p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Seed {p.Seed}");
        sb.AppendLine($"{Cap(p.Sex)}, age {p.Age}, born {p.BirthYear} in {p.BirthLocation.Name}");
        sb.AppendLine("Temperament: " + string.Join(", ", p.Temperament.Select(kv =>
            string.Create(CultureInfo.InvariantCulture, $"{Pretty(kv.Key)} {kv.Value:0.00}"))));

        foreach (var (prefix, title) in Sections)
        {
            var rows = p.Facts.Categorical
                .Where(kv => kv.Key.StartsWith(prefix + ".") && !kv.Key.EndsWith(".role"))
                .ToList();
            if (rows.Count == 0) continue;

            sb.AppendLine().AppendLine(title);
            foreach (var (key, value) in rows)
                sb.AppendLine($"  {Pretty(key[(prefix.Length + 1)..])}: {Pretty(value)}");
        }
        return sb.ToString();
    }

    static string Pretty(string id) => id.Replace('_', ' ');
    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
