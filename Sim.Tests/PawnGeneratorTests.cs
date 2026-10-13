using YouWillDate.Sim.Generation;

namespace YouWillDate.Sim.Tests;

public class PawnGeneratorTests
{
    static readonly PawnGenerator Gen = new(TestData.Load());

    static readonly Lazy<List<Pawn>> Population =
        new(() => Enumerable.Range(0, 20000).Select(i => Gen.Generate((ulong)i)).ToList());

    [Fact]
    public void SameSeedGivesSamePawn()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var a = Gen.Generate(seed);
            var b = Gen.Generate(seed);
            Assert.Equal(a.Facts.Categorical, b.Facts.Categorical);
            Assert.Equal(a.Facts.Numeric, b.Facts.Numeric);
        }
    }

    [Fact]
    public void DifferentSeedsGiveDifferentPawns()
    {
        var descriptions = Enumerable.Range(0, 100)
            .Select(i => PawnText.Describe(Gen.Generate((ulong)i)).Split('\n', 2)[1])
            .ToHashSet();
        Assert.Equal(100, descriptions.Count);
    }

    [Fact]
    public void EveryValueIsInTheSchema()
    {
        var schema = TestData.Load().Schema;
        foreach (var p in Population.Value.Take(2000))
        foreach (var (key, value) in p.Facts.Categorical)
        {
            string canonical = key.StartsWith("mother.") || key.StartsWith("father.")
                ? "parent." + key[(key.IndexOf('.') + 1)..]
                : key;
            Assert.True(schema.TryGetValue(canonical, out var allowed), $"unknown fact {key}");
            Assert.True(allowed!.Contains(value), $"{key} = {value} is not allowed");
        }
    }

    [Fact]
    public void Invariants()
    {
        foreach (var p in Population.Value)
        {
            var f = p.Facts;
            bool onlyChild = f["household.siblings"] == "0";
            Assert.Equal(onlyChild, f["household.birth_order"] == "only");
            Assert.Equal(onlyChild, f["household.age_gaps"] == "none");
            if (f["household.siblings"] == "1") Assert.NotEqual("middle", f["household.birth_order"]);
            if (f["household.birth_order"] == "only") Assert.NotEqual("sibling_care", f["climate.caregiving_duties"]);
            if (f["household.politics_leaning"] == "apolitical") Assert.Equal("none", f["household.politics_engagement"]);

            foreach (var role in new[] { "mother", "father" })
            {
                Assert.Equal(f[$"{role}.religion"] == "none", f[$"{role}.practice"] == "none");
                bool stable = f[$"{role}.mental_health_severity"] == "stable";
                Assert.Equal(stable, f[$"{role}.mental_health_type"] == "none");
                Assert.Equal(stable, f[$"{role}.mental_health_treatment"] == "none");
            }

            Assert.InRange(p.Age, 21, 45);
            Assert.InRange(p.Temperament["threat_sensitivity"], 0, 1);
            Assert.InRange(p.Temperament["self_control"], 0, 1);
        }
    }

    [Fact]
    public void SexRatioMatchesBaseRate()
    {
        double male = Population.Value.Count(p => p.Sex == "male") / (double)Population.Value.Count;
        Assert.InRange(male, 0.49, 0.535);
    }

    [Fact]
    public void TemperamentIsPercentileWithConfiguredCorrelations()
    {
        var pop = Population.Value;
        double meanThreat = pop.Average(p => p.Temperament["threat_sensitivity"]);
        Assert.InRange(meanThreat, 0.48, 0.52);

        // Threat sensitivity and self-control lean slightly against each other.
        double r = Correlation(pop.Select(p => p.Temperament["threat_sensitivity"]).ToArray(),
                               pop.Select(p => p.Temperament["self_control"]).ToArray());
        Assert.InRange(r, -0.28, -0.12);
    }

    [Fact]
    public void CommonOutcomesHavePlausibleShares()
    {
        var pop = Population.Value;
        double Share(string key, string value) => pop.Count(p => p.Facts[key] == value) / (double)pop.Count;

        Assert.InRange(Share("household.shape", "two_parents"), 0.5, 0.85);
        Assert.InRange(Share("household.siblings", "0"), 0.1, 0.35);
        Assert.InRange(Share("household.twin", "single"), 0.95, 0.99);
    }

    static double Correlation(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Length; i++)
        {
            sxy += (x[i] - mx) * (y[i] - my);
            sxx += (x[i] - mx) * (x[i] - mx);
            syy += (y[i] - my) * (y[i] - my);
        }
        return sxy / Math.Sqrt(sxx * syy);
    }
}
