using System.Globalization;
using System.Text;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim;

/// <summary>Evaluates weighted terms and additive mods against a pawn's facts.</summary>
static class TermMath
{
    /// <summary>
    /// One term's contribution. Numeric facts give weight × value (temperament
    /// percentiles centred to -1..1); categorical facts give the weight listed for their value.
    /// </summary>
    public static double Value(Term term, Facts facts, out string label)
    {
        label = term.Key;
        if (term.IsNumeric)
        {
            double x = facts.Num(term.Key) ?? 0;
            if (term.Centered) x = (x - 0.5) * 2;
            return term.Weight * x;
        }
        double v = 0;
        foreach (var value in FactTest.Values(term.Key, facts))
            if (term.ValueWeights!.TryGetValue(value, out var w))
            {
                v += w;
                label = $"{term.Key}={value}";
            }
        return v;
    }

    /// <summary>base + Σ terms + Σ fired mods. When why is given, each non-zero part is appended to it.</summary>
    public static double Sum(double baseValue, Term[] terms, AddMod[] mods, Facts facts, StringBuilder? why = null)
    {
        double sum = baseValue;
        if (why is not null && baseValue != 0) why.Append(string.Create(CultureInfo.InvariantCulture, $"{baseValue:0.##}"));
        foreach (var term in terms)
        {
            double v = Value(term, facts, out var label);
            if (v == 0) continue;
            sum += v;
            why?.Append(string.Create(CultureInfo.InvariantCulture, $" {(v >= 0 ? "+" : "−")}{Math.Abs(v):0.##} {label}"));
        }
        foreach (var mod in mods)
        {
            if (!mod.When.All(c => FactTest.Test(c, facts))) continue;
            sum += mod.Add;
            why?.Append(string.Create(CultureInfo.InvariantCulture,
                $" {(mod.Add >= 0 ? "+" : "−")}{Math.Abs(mod.Add):0.##} [{string.Join(" and ", mod.When.Select(c => c.Text))}]"));
        }
        return sum;
    }
}
