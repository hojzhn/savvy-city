using YouWillDate.Sim.Data;

namespace YouWillDate.Sim;

/// <summary>Evaluates conditions and term keys against facts, resolving self., any_parent. and any_inv.</summary>
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
        if (c.Key.StartsWith("any_inv."))
        {
            string rest = c.Key["any_inv.".Length..];
            return InvolvementKeys(rest, facts).Any(k => TestKey(c, facts, k));
        }
        return TestKey(c, facts, c.Key);
    }

    static bool TestKey(Condition c, Facts facts, string key) =>
        c.IsNumeric ? c.TestNumeric(facts.Num(key)) : c.TestCategorical(facts.Cat(key));

    /// <summary>"inv0.&lt;field&gt;", "inv1.&lt;field&gt;", ... for every involvement, past and present.</summary>
    static IEnumerable<string> InvolvementKeys(string field, Facts facts)
    {
        for (int i = 0; facts.Contains($"inv{i}.years"); i++) yield return $"inv{i}.{field}";
    }

    /// <summary>Categorical values of a key; any_parent. gives both parents' values, any_inv. every ongoing involvement's.</summary>
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
        if (key.StartsWith("any_inv."))
        {
            foreach (var v in InvolvementKeys(key["any_inv.".Length..], facts).Select(facts.Cat).OfType<string>().Distinct())
                yield return v;
            yield break;
        }
        if (facts.Cat(key) is string v1) yield return v1;
    }
}
