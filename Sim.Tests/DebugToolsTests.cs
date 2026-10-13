using YouWillDate.Sim.Generation;

namespace YouWillDate.Sim.Tests;

public class DebugToolsTests
{
    static readonly PawnGenerator Gen = new(TestData.Load());

    static Pawn With(ulong seed, Action<Facts> set)
    {
        var o = new Facts();
        set(o);
        return Gen.Generate(seed, new GenerateOptions { Overrides = o, Trace = true });
    }

    [Fact]
    public void OverrideReplacesValueAndLeavesEarlierFactsAlone()
    {
        for (ulong seed = 0; seed < 30; seed++)
        {
            var plain = Gen.Generate(seed);
            var forced = With(seed, o => o.Set("mother.religion", "muslim"));

            Assert.Equal("muslim", forced.Facts["mother.religion"]);
            Assert.Equal(RollSource.Override, forced.TraceFor("mother.religion")!.Source);
            // Facts rolled before the override can't depend on it.
            Assert.Equal(plain.Facts["mother.ethnicity"], forced.Facts["mother.ethnicity"]);
            Assert.Equal(plain.Facts["context.urbanicity"], forced.Facts["context.urbanicity"]);
            Assert.Equal(plain.Sex, forced.Sex);
        }
    }

    [Fact]
    public void OverrideCascadesDownstream()
    {
        var p = With(1, o => o.Set("household.siblings", "0"));
        Assert.Equal("only", p.Facts["household.birth_order"]);
        Assert.Equal(RollSource.Default, p.TraceFor("household.birth_order")!.Source);
    }

    [Fact]
    public void NumericOverrideFiresTraitMods()
    {
        var p = With(3, o => o.Set("pawn.threat_sensitivity", 0.95));
        var mod = p.TraceFor("climate.parenting_style")!.Mods.Single(m => m.When.Contains("pawn.threat_sensitivity"));
        Assert.True(mod.Fired);

        var q = With(3, o => o.Set("pawn.threat_sensitivity", 0.1));
        Assert.False(q.TraceFor("climate.parenting_style")!.Mods.Single(m => m.When.Contains("pawn.threat_sensitivity")).Fired);
    }

    [Fact]
    public void LocationAndYearOverridesChangeContext()
    {
        var p = With(5, o => { o.Set("pawn.location", "rural_ohio"); o.Set("year", 1985); });
        Assert.Equal("rural_ohio", p.BirthLocation.Id);
        Assert.Equal(1985, p.BirthYear);
        Assert.Equal("rural", p.Facts["context.urbanicity"]);
        Assert.Equal("boom", p.Facts["context.economic_era"]);
    }

    [Theory]
    [InlineData("mother.nonsense", "x", "unknown fact")]
    [InlineData("mother.religion", "pastafarian", "has no value")]
    [InlineData("mother.role", "father", "derived")]
    [InlineData("year", "1990", "numeric")]
    public void BadCategoricalOverridesAreRejected(string key, string value, string message)
    {
        var e = Assert.Throws<ArgumentException>(() => With(1, o => o.Set(key, value)));
        Assert.Contains(message, e.Message);
    }

    [Theory]
    [InlineData("year", 1900, "year must be")]
    [InlineData("pawn.reward_drive", 1.5, "between 0 and 1")]
    [InlineData("household.wealth", 2, "categorical")]
    public void BadNumericOverridesAreRejected(string key, double value, string message)
    {
        var e = Assert.Throws<ArgumentException>(() => With(1, o => o.Set(key, value)));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void TraceCoversEveryFactAndExplainsTheRoll()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var p = Gen.Generate(seed, new GenerateOptions { Trace = true });
            var traced = p.Trace!.Select(t => t.Key).ToHashSet();

            foreach (var key in p.Facts.Categorical.Keys.Concat(p.Facts.Numeric.Keys))
                if (!key.EndsWith(".role")) Assert.Contains(key, traced);

            foreach (var t in p.Trace!.Where(t => t.Source == RollSource.Drawn && t.Values is not null))
            {
                int i = t.Values!.ToList().IndexOf(t.Value!);
                Assert.True(t.FinalWeights![i] > 0, $"{t.Key} rolled '{t.Value}' with zero weight");
            }
        }
    }

    [Fact]
    public void TracingDoesNotChangeThePawn()
    {
        for (ulong seed = 0; seed < 50; seed++)
            Assert.Equal(Gen.Generate(seed).Facts.Categorical,
                         Gen.Generate(seed, new GenerateOptions { Trace = true }).Facts.Categorical);
    }
}
