using System.Globalization;
using System.Text;

namespace YouWillDate.Sim.Generation;

/// <summary>Plain-text explanation of one roll, for the CLI and the Godot inspector.</summary>
public static class TraceText
{
    public static string Describe(RollTrace t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{t.Key} = {t.ValueText}   ({SourceText(t.Source)})");
        if (t.Note is not null) sb.AppendLine($"Note: {t.Note}");

        if (t.FailedOnlyIf.Count > 0)
        {
            sb.AppendLine().AppendLine("only_if failed:");
            foreach (var c in t.FailedOnlyIf) sb.AppendLine($"  {c}");
        }

        if (t.Values is null || t.BaseWeights is null || t.FinalWeights is null)
            return sb.ToString();

        sb.AppendLine().AppendLine(t.BaseFrom is null
            ? "Base weights: from the item's weights"
            : $"Base weights: from the location's '{t.BaseFrom}' shares");

        if (t.Mods.Count > 0)
        {
            sb.AppendLine().AppendLine($"Mods ({t.Mods.Count(m => m.Fired)} of {t.Mods.Count} fired):");
            foreach (var m in t.Mods)
            {
                sb.AppendLine($"  [{(m.Fired ? "x" : " ")}] mod {m.Number}: when {m.When}");
                sb.AppendLine($"        {m.Mul}");
            }
        }

        double baseTotal = t.BaseWeights.Sum(), total = t.FinalWeights.Sum();
        int width = t.Values.Max(v => v.Length);
        sb.AppendLine().AppendLine($"{"Value".PadRight(width)}   base   final");
        for (int i = 0; i < t.Values.Count; i++)
        {
            double b = baseTotal > 0 ? t.BaseWeights[i] / baseTotal : 0;
            double f = total > 0 ? t.FinalWeights[i] / total : 0;
            string bar = new string('#', (int)Math.Round(f * 30));
            string mark = t.Values[i] == t.Value ? "  <--" : "";
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{t.Values[i].PadRight(width)}  {b * 100,4:0}%  {f * 100,5:0.0}%  {bar}{mark}"));
        }
        return sb.ToString();
    }

    public static string SourceText(RollSource s) => s switch
    {
        RollSource.Drawn => "rolled",
        RollSource.Lookup => "looked up",
        RollSource.Default => "default: only_if failed",
        RollSource.AssortCopy => "copied from mother",
        RollSource.Derived => "derived",
        RollSource.Override => "OVERRIDDEN",
        _ => s.ToString(),
    };
}
