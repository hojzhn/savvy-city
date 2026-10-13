using YouWillDate.Sim.Adult;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Sim.Tests;

public class AdultTests
{
    static readonly GameData Data = TestData.Load();
    static readonly PawnGenerator Gen = new(Data);
    static readonly Lazy<List<Pawn>> Population =
        new(() => Enumerable.Range(0, 4000).Select(i => Gen.Generate((ulong)i)).ToList());

    static double Share(Func<Pawn, bool> f) => Population.Value.Count(f) / (double)Population.Value.Count;
    static double Num(Pawn p, string key) => p.Facts.Num(key) ?? double.NaN;

    [Fact]
    public void AdultDataLoadsAndCalibrates()
    {
        Assert.NotNull(Data.Adult);
        Assert.True(Data.AdultNorms.ContainsKey("adult.neuroticism"));
        Assert.NotNull(Gen.Generate(1).Adult);
    }

    [Fact]
    public void SameSeedGivesSameAdult()
    {
        for (ulong seed = 0; seed < 30; seed++)
        {
            var a = Gen.Generate(seed);
            var b = Gen.Generate(seed, new GenerateOptions { Trace = true, LifeDetails = true });
            Assert.Equal(a.Facts.Categorical.Where(kv => kv.Key.StartsWith("adult.")),
                         b.Facts.Categorical.Where(kv => kv.Key.StartsWith("adult.")));
            Assert.Equal(a.Facts.Numeric.Where(kv => kv.Key.StartsWith("adult.")),
                         b.Facts.Numeric.Where(kv => kv.Key.StartsWith("adult.")));
        }
    }

    [Theory]
    [InlineData("adult.neuroticism")]
    [InlineData("adult.extraversion")]
    [InlineData("adult.conscientiousness")]
    [InlineData("adult.anxiety")]
    [InlineData("adult.avoidance")]
    public void ZTraitsAreNormalized(string key)
    {
        // Spec 6.13: each adult trait has mean 0 and SD 1 within a tolerance.
        var values = Population.Value.Select(p => Num(p, key)).ToList();
        double mean = values.Average();
        double sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
        Assert.InRange(mean, -0.1, 0.1);
        Assert.InRange(sd, 0.85, 1.15);
    }

    [Fact]
    public void ProfileInvariantsHold()
    {
        // Spec 6.12, plus "Social circle and health" section 6.
        var z = Data.Adult!.Items.Where(i => i.Type == AdultType.Score && i.Scale is ScoreScale.Z or ScoreScale.ZRaw).Select(i => i.Key).ToList();
        var unit = Data.Adult.Items.Where(i => i.Type == AdultType.Score && i.Scale is ScoreScale.Unit or ScoreScale.Clamp01).Select(i => i.Key).ToList();
        foreach (var p in Population.Value)
        {
            var f = p.Facts;
            foreach (var k in z) Assert.InRange(Num(p, k), -3, 3);
            foreach (var k in unit) Assert.InRange(Num(p, k), 0, 1);

            // Attachment style matches the axes and the cutoff.
            bool anx = Num(p, "adult.anxiety") >= 0.5, avo = Num(p, "adult.avoidance") >= 0.5;
            string style = anx && avo ? "fearful_avoidant" : anx ? "anxious" : avo ? "avoidant" : "secure";
            Assert.Equal(style, f["adult.attachment_style"]);

            // Inner life points at real history entries; no wound means no belief, want or need.
            var inner = p.Adult!.InnerLife;
            if (inner.CoreWoundEntry is int w) Assert.Equal(p.History[w].ItemId, f["adult.core_wound"]);
            else
            {
                Assert.Equal("none", f["adult.private_belief"]);
                Assert.Equal("none", f["adult.want"]);
                Assert.Equal("none", f["adult.need"]);
            }
            foreach (var s in inner.Secrets.Where(s => s.Source.StartsWith("history:")))
                Assert.InRange(int.Parse(s.Source[8..]), 0, p.History.Count - 1);

            // A missed goal's deadline is earlier than the current age.
            foreach (var g in p.Adult.Goals.Where(g => g.Status == "missed")) Assert.True(g.Deadline < p.Age);

            // Coping by drinking needs heavy drinking; type and treatment are none when stable.
            if (f["adult.coping"] == "drinking") Assert.Contains(f["adult.drinking"], new[] { "heavy", "dependent" });
            if (f["adult.coping"] == "smoking") Assert.Contains(f["adult.smoking"], new[] { "occasional", "daily" });
            bool stable = f["adult.mental_health_severity"] == "stable";
            Assert.Equal(stable, f["adult.mental_health_type"] == "none");
            Assert.Equal(stable, f["adult.mental_health_treatment"] == "none");

            // Sex-specific conditions match the pawn's sex.
            if (p.Sex == "male")
            {
                Assert.Equal("none", f["adult.health_conditions.endometriosis"]);
                Assert.Equal("none", f["adult.health_conditions.pcos"]);
            }
            Assert.True(p.Adult.Picks.Count(x => x.List == "kinks") <= 3);
        }
    }

    [Fact]
    public void PopulationTargetsFromTheDocs()
    {
        // Social circle and health 3.2: about 12% with no close friends, median around 3.
        Assert.InRange(Share(p => Num(p, "adult.close_friends") == 0), 0.05, 0.2);
        var friends = Population.Value.Select(p => Num(p, "adult.close_friends")).Order().ToList();
        Assert.InRange(friends[friends.Count / 2], 2, 5);

        // 2.2: self-rated health targets.
        Assert.InRange(Share(p => p.Facts["adult.general_health"] == "excellent"), 0.13, 0.27);
        Assert.InRange(Share(p => p.Facts["adult.general_health"] == "poor"), 0.02, 0.1);

        // 4.3: mental health shares match the parents' table (70 / 18 / 9 / 3).
        Assert.InRange(Share(p => p.Facts["adult.mental_health_severity"] == "stable"), 0.64, 0.77);
        Assert.InRange(Share(p => p.Facts["adult.mental_health_severity"] == "severe"), 0.01, 0.06);

        // Spec 6.5: secure is the most common attachment style.
        Assert.InRange(Share(p => p.Facts["adult.attachment_style"] == "secure"), 0.4, 0.65);
    }

    [Fact]
    public void SocialCircleAffectsMentalHealthOnlySlightly()
    {
        // Social circle and health 4.2: the social term is small next to the others.
        var item = Data.Adult!.Items.Single(i => i.Id == "mental_health_risk");
        double Weight(string key) => item.Formula!.Terms.Single(t => t.Key == key).Weight;
        var social = Population.Value
            .Select(p => Weight("adult.loneliness") * Num(p, "adult.loneliness") + Weight("adult.social_support") * Num(p, "adult.social_support"))
            .Order().ToList();
        double spread = social[(int)(social.Count * 0.9)] - social[(int)(social.Count * 0.1)];
        double sd = Data.AdultNorms["adult.mental_health_risk"].Sd;
        Assert.InRange(spread / sd, 0.08, 0.35);
    }

    [Fact]
    public void HealthFollowsTheMoneyGradient()
    {
        // Social circle and health 7: fair or poor health is more common at the bottom.
        double FairOrPoor(IEnumerable<Pawn> ps) => ps.Average(p => p.Facts["adult.general_health"] is "fair" or "poor" ? 1.0 : 0.0);
        var low = Population.Value.Where(p => p.Facts["state.income"] is "none" or "low");
        var high = Population.Value.Where(p => p.Facts["state.income"] is "high" or "very_high");
        Assert.True(FairOrPoor(low) > 1.3 * FairOrPoor(high));
    }

    [Fact]
    public void LifestyleAndGenderRolesAreRelatedButDistinct()
    {
        var pop = Population.Value;
        double r = Correlation(pop.Select(p => Num(p, "adult.lifestyle")).ToArray(),
                               pop.Select(p => Num(p, "adult.gender_roles")).ToArray());
        Assert.InRange(r, 0.15, 0.8);
        // Some alt pawns still want traditional roles.
        Assert.Contains(pop, p => p.Facts["adult.lifestyle_label"] == "alt" && p.Facts["adult.gender_roles_label"] == "traditional");

        // Alt pawns seek novelty; trad pawns are more religious.
        double Mean(string label, string key) => pop.Where(p => p.Facts["adult.lifestyle_label"] == label).Average(p => Num(p, key));
        Assert.True(Mean("alt", "adult.novelty_seeking") > Mean("trad", "adult.novelty_seeking") + 0.5);
        Assert.True(Mean("trad", "adult.religiosity") > Mean("alt", "adult.religiosity") + 0.1);
    }

    static double Correlation(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Length; i++)
        {
            sxy += (x[i] - mx) * (y[i] - my);
            sxx += (x[i] - mx) * (x[i] - mx);
            syy += (y[i] - my) * (y[i] - my);
        }
        return sxy / Math.Sqrt(sxx * syy);
    }

    [Fact]
    public void AdultTraitsCannotBeOverridden()
    {
        var o = new Facts();
        o.Set("adult.neuroticism", 2.0);
        var e = Assert.Throws<ArgumentException>(() => Gen.Generate(1, new GenerateOptions { Overrides = o }));
        Assert.Contains("adult trait", e.Message);
    }

    [Fact]
    public void TracesExplainAdultTraits()
    {
        var p = Gen.Generate(4, new GenerateOptions { Trace = true });
        Assert.Contains("raw =", p.TraceFor("adult.neuroticism")!.Note);
        Assert.NotNull(p.TraceFor("adult.religion")!.FinalWeights);
        Assert.NotNull(p.TraceFor("z.threat_sensitivity"));
    }

    static string MinimalAdult(string items) => $$"""
        calibration_size: 60
        groups:
          - id: g
            items:
        {{items}}
        """;

    static GameData WithAdult(string yaml) => GameData.FromYaml(
        TestData.ReadFile("config.yaml"), TestData.ReadFile("locations.yaml"), TestData.ReadFile("eras.yaml"),
        TestData.ReadFile("family.yaml"), TestData.ReadFile("life.yaml"), yaml);

    [Theory]
    [InlineData("""
              - id: x
                type: score
                weights: { adult.nope: 1 }
        """, "not a known fact")]
    [InlineData("""
              - id: x
                type: roll
                values: [a, b]
                mods: [{ when: { pawn.sex: male }, add: 1 }]
        """, "roll mods use mul")]
    [InlineData("""
              - id: x
                type: score
                scale: percent
        """, "scale must be")]
    [InlineData("""
              - id: x
                type: list
                levels: [low]
                max: 1
                entries: [{ id: e, prevalence: 1.5 }]
        """, "prevalence must be between 0 and 1")]
    [InlineData("""
              - id: x
                type: score
                weights: { adult.y: 1 }
              - id: y
                type: score
        """, "not a known fact")]
    public void BadAdultDataIsRejected(string items, string message)
    {
        var e = Assert.Throws<DataException>(() => WithAdult(MinimalAdult(items)));
        Assert.Contains(message, e.Message);
    }
}
