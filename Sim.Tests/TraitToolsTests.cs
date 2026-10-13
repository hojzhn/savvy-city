using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Sim.Tests;

public class TraitToolsTests
{
    [Fact]
    public void FamilyFileRoundTripsThroughTheWriter()
    {
        var data = TestData.Load();
        string written = YamlIO.Write(data.FamilySource, YamlIO.LeadingComments(TestData.ReadFile("family.yaml")));
        var reloaded = TestData.WithFamily(written);

        var a = new PawnGenerator(data);
        var b = new PawnGenerator(reloaded);
        for (ulong seed = 0; seed < 300; seed++)
            Assert.Equal(a.Generate(seed).Facts.Categorical, b.Generate(seed).Facts.Categorical);

        Assert.StartsWith("# Family and household tables", written);
    }

    [Fact]
    public void ConfigRoundTripsThroughTheWriter()
    {
        var data = TestData.Load();
        string written = YamlIO.Write(data.Config);
        var config = YamlIO.Parse<Config>("config.yaml", written);
        Assert.Equal(data.Config.Temperament.Traits, config.Temperament.Traits);
        Assert.Equal(data.Config.Temperament.Correlations.Select(c => c.R), config.Temperament.Correlations.Select(c => c.R));
        Assert.Equal(data.Config.MaleBirthRate, config.MaleBirthRate);
    }

    [Fact]
    public void InlineMapsRoundTrip()
    {
        var when = YamlIO.ParseInlineMap<object>("self.education: [masters, bachelors], year: \">=1995\", household.siblings: \"!0\"");
        Assert.Equal(3, when.Count);
        string text = YamlIO.FormatInlineMap(when);
        var again = YamlIO.ParseInlineMap<object>(text);
        Assert.Equal(">=1995", again["year"]);
        Assert.Equal("!0", again["household.siblings"]);
        Assert.Equal(new object[] { "masters", "bachelors" }, (List<object>)again["self.education"]);

        var mul = YamlIO.ParseInlineMap<double>("poor: 2, rich: 0.5");
        Assert.Equal(0.5, mul["rich"]);
        Assert.Throws<DataException>(() => YamlIO.ParseInlineMap<double>("poor: lots"));
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var data = TestData.Load();
        var copy = YamlIO.Clone(data.FamilySource);
        copy.Groups[0].Items[0].Values.Add("martian");
        Assert.DoesNotContain("martian", data.FamilySource.Groups[0].Items[0].Values);
    }

    [Fact]
    public void CatalogLinksDependenciesBothWays()
    {
        var catalog = new TraitCatalog(TestData.Load());
        var wealth = catalog.Get("household.wealth")!;
        Assert.Contains("parent.occupation", wealth.Reads);
        Assert.Contains("household.wealth", catalog.Get("parent.occupation")!.ReadBy);
        Assert.Equal(["mother.education", "father.education"], catalog.Get("parent.education")!.FactKeys);
        Assert.Contains(catalog.Traits, t => t.Key == "pawn.threat_sensitivity");

        // Everything a rule reads is a known trait.
        foreach (var t in catalog.Traits)
            foreach (var r in t.Reads)
                Assert.NotNull(catalog.Get(r));
    }

    [Fact]
    public void PopulationStatsCountEveryPawn()
    {
        var gen = new PawnGenerator(TestData.Load());
        var stats = PopulationStats.Run(gen, 500);
        Assert.Equal(500, stats["household.wealth"].Counts.Values.Sum());
        Assert.Equal(500, stats["pawn.reward_drive"].Numbers.Count);

        var shape = stats["household.shape"];
        Assert.True(shape.Rolled > 0);
        Assert.All(shape.ModFired, f => Assert.InRange(f, 0, shape.Rolled));
    }
}
