using YouWillDate.Sim.Data;

namespace YouWillDate.Sim;

/// <summary>Evaluates conditions and term keys against facts, resolving self. and any_parent.</summary>
static class FactTest
{
    /// <param name="role">"mother" or "father" when rolling a per-parent item, otherwise null.</param>
    public static bool Test(Condition c, Facts facts, string? role = null)
    {
        if (c.Key.StartsWith("self."))
            return TestKey(c, facts, $"{role}.{c.Key[5..]}");
        if (c.Key.StartsWith("any_parent."))
        {
            string rest = c.Key["any_parent.".Length..];
            return TestKey(c, facts, $"mother.{rest}") || TestKey(c, facts, $"father.{rest}");
        }
        return TestKey(c, facts, c.Key);
    }

    static bool TestKey(Condition c, Facts facts, string key) =>
        c.IsNumeric ? c.TestNumeric(facts.Num(key)) : c.TestCategorical(facts.Cat(key));

    /// <summary>Categorical values of a key; any_parent. gives both parents' values.</summary>
    public static IEnumerable<string> Values(string key, Facts facts)
    {
        if (key.StartsWith("any_parent."))
        {
            string rest = key["any_parent.".Length..];
            var m = facts.Cat($"mother.{rest}");
            var f = facts.Cat($"father.{rest}");
            if (m is not null) yield return m;
            if (f is not null && f != m) yield return f;
            yield break;
        }
        if (facts.Cat(key) is string v) yield return v;
    }
}
