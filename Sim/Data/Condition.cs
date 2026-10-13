using System.Globalization;

namespace YouWillDate.Sim.Data;

/// <summary>
/// One test against a fact, parsed from a <c>when</c> or <c>only_if</c> entry.
/// <list type="bullet">
/// <item><c>key: [a, b]</c> — value is one of a, b</item>
/// <item><c>key: a</c> — value is a</item>
/// <item><c>key: "!a"</c> — value is anything but a</item>
/// <item><c>key: "&gt;=0.7"</c>, <c>"&lt;1990"</c>, <c>"1990..1999"</c> — numeric comparisons</item>
/// </list>
/// Key prefixes: <c>self.</c> is the parent being rolled, <c>any_parent.</c>
/// passes if either parent passes.
/// </summary>
public sealed class Condition
{
    public string Key { get; }
    public bool IsNumeric { get; }
    /// <summary>Readable form, such as "self.education in [masters, doctorate]".</summary>
    public string Text { get; private set; } = "";
    /// <summary>Categorical values named by the condition (for validation).</summary>
    public IReadOnlyCollection<string> Values => _values;

    readonly HashSet<string> _values = new();
    readonly bool _negate;
    readonly double _min = double.NegativeInfinity, _max = double.PositiveInfinity;
    readonly bool _minExclusive, _maxExclusive;

    Condition(string key, bool numeric, HashSet<string> values, bool negate)
    {
        Key = key; IsNumeric = numeric; _values = values; _negate = negate;
    }

    Condition(string key, double min, bool minExclusive, double max, bool maxExclusive)
    {
        Key = key; IsNumeric = true;
        _min = min; _minExclusive = minExclusive; _max = max; _maxExclusive = maxExclusive;
    }

    public static Condition Parse(string key, object? spec)
    {
        var c = ParseSpec(key, spec);
        c.Text = spec switch
        {
            List<object> list => $"{key} in [{string.Join(", ", list)}]",
            string s when s.StartsWith('!') => $"{key} != {s[1..]}",
            string s when s.StartsWith('<') || s.StartsWith('>') => $"{key} {s}",
            string s when s.Contains("..") => $"{key} in {s}",
            _ => $"{key} = {spec}",
        };
        return c;
    }

    static Condition ParseSpec(string key, object? spec)
    {
        switch (spec)
        {
            case List<object> list:
                return new Condition(key, false, list.Select(o => o?.ToString() ?? "").ToHashSet(), false);
            case string s when s.StartsWith('!'):
                return new Condition(key, false, new HashSet<string> { s[1..] }, true);
            case string s when s.StartsWith(">=") : return new(key, Num(s[2..]), false, double.PositiveInfinity, false);
            case string s when s.StartsWith("<=") : return new(key, double.NegativeInfinity, false, Num(s[2..]), false);
            case string s when s.StartsWith('>')  : return new(key, Num(s[1..]), true, double.PositiveInfinity, false);
            case string s when s.StartsWith('<')  : return new(key, double.NegativeInfinity, false, Num(s[1..]), true);
            case string s when s.Contains(".."):
                var parts = s.Split("..");
                return new Condition(key, Num(parts[0]), false, Num(parts[1]), false);
            case string s:
                return new Condition(key, false, new HashSet<string> { s }, false);
            default:
                throw new DataException($"condition on '{key}' has an unsupported value: {spec}");
        }
    }

    public bool TestCategorical(string? value) =>
        value is not null && _values.Contains(value) != _negate;

    public bool TestNumeric(double? value)
    {
        if (value is not double v) return false;
        bool aboveMin = _minExclusive ? v > _min : v >= _min;
        bool belowMax = _maxExclusive ? v < _max : v <= _max;
        return aboveMin && belowMax;
    }

    static double Num(string s)
    {
        if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new DataException($"'{s}' is not a number");
        return v;
    }
}
