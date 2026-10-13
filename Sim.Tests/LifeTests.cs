using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;
using YouWillDate.Sim.Life;

namespace YouWillDate.Sim.Tests;

public class LifeTests
{
    static readonly GameData Data = TestData.Load();
    static readonly PawnGenerator Gen = new(Data);
    static readonly Lazy<List<Pawn>> Population =
        new(() => Enumerable.Range(0, 4000).Select(i => Gen.Generate((ulong)i)).ToList());

    static double Share(Func<Pawn, bool> f) => Population.Value.Count(f) / (double)Population.Value.Count;

    [Fact]
    public void LifeDataLoads()
    {
        Assert.NotNull(Data.Life);
        Assert.Equal("early_childhood", Data.Life!.StageAt(3).Id);
        Assert.Equal("present", Data.Life.StageAt(40).Id);
    }

    [Fact]
    public void SameSeedGivesSameLife()
    {
        for (ulong seed = 0; seed < 50; seed++)
        {
            var a = Gen.Generate(seed);
            var b = Gen.Generate(seed, new GenerateOptions { Trace = true, LifeDetails = true });
            Assert.Equal(a.History.Select(PawnText.Line), b.History.Select(PawnText.Line));
            Assert.Equal(a.Facts.Categorical, b.Facts.Categorical);
        }
    }

    [Fact]
    public void HistoryIsInOrderAndCausesPointBackwards()
    {
        foreach (var p in Population.Value.Take(1000))
        {
            for (int i = 0; i < p.History.Count; i++)
            {
                var e = p.History[i];
                Assert.Equal(i, e.Id);
                Assert.InRange(e.Age, 0, p.Age);
                if (i > 0) Assert.True(e.Age >= p.History[i - 1].Age);
                Assert.All(e.Causes, c => Assert.InRange(c, 0, i - 1));
            }
        }
    }

    [Fact]
    public void SpecInvariantsHold()
    {
        foreach (var p in Population.Value)
        {
            var f = p.Facts;
            int Age(string item) => p.History.FirstOrDefault(e => e.ItemId == item)?.Age ?? -1;

            // 4.11: a divorce requires married (together) parents; at most once.
            Assert.True(p.History.Count(e => e.ItemId == "parental_divorce") <= 1);
            if (f["household.marriage_quality"] == "separated" || f["household.shape"] is "single_mother" or "single_father")
                Assert.Equal(-1, Age("parental_divorce"));

            // 4.11: a remarriage requires an earlier divorce or parent death (or a separation before birth).
            foreach (var r in p.History.Where(e => e.ItemId == "remarriage"))
                Assert.True(f["household.marriage_quality"] == "separated"
                    || p.History.Any(e => e.Id < r.Id && (e.ItemId == "parental_divorce"
                        || (e.ItemId == "family_death" && e.Outcome.Contains("while_child")))));

            // 4.11: military service starts at or after the minimum age.
            var enlisted = p.History.FirstOrDefault(e => e.ItemId == "military_service" && e.Outcome == "enlist");
            if (enlisted is not null) Assert.True(enlisted.Age >= 17);

            // 4.11: income is none when the pawn has no career.
            if (f["state.career"] == "none") Assert.Equal("none", f["state.income"]);

            // Everyone lives in the game's city by now.
            Assert.Equal(Data.CurrentCity.Id, f["state.location"]);

            // Orphaned children get a guardian.
            var orphaned = p.History.FirstOrDefault(e => e.Outcome == "last_parent_while_child");
            if (orphaned is not null && orphaned.Age < 17)
                Assert.Contains(p.History, e => e.ItemId == "guardianship");

            if (f.Num("state.age_first_sex") is double sex) Assert.InRange(sex, 13, p.Age);
            Assert.Equal(f["state.relationship"] == "single" ? 0 : f.Num("state.relationship_years") ?? 0,
                         f.Num("state.relationship_years") ?? 0);
            Assert.True(f.Num("state.children") >= 0);
        }
    }

    [Fact]
    public void PartnersMatchTheRelationshipState()
    {
        foreach (var p in Population.Value.Take(1500))
        {
            foreach (var partner in p.Partners)
                if (partner.EndAge is int end) Assert.True(end >= partner.StartAge);
            bool inRelationship = p.Facts["state.relationship"] != "single";
            Assert.Equal(inRelationship, p.Partners.Count > 0 && p.Partners[^1].EndAge is null);
        }
    }

    [Fact]
    public void PopulationRatesArePlausible()
    {
        Assert.InRange(Share(p => p.Facts["life.parental_divorce"] != "never"), 0.12, 0.35);
        Assert.InRange(Share(p => p.Facts["state.relationship"] == "married"), 0.30, 0.65);
        Assert.InRange(Share(p => p.Facts["state.education"] is "bachelors" or "masters" or "doctorate_professional"), 0.20, 0.45);
        Assert.InRange(Share(p => p.Facts["state.education"] == "some_secondary"), 0.04, 0.16);
        Assert.InRange(Share(p => p.Facts["life.military_service"] == "enlist"), 0.02, 0.09);
        Assert.InRange(Share(p => p.Facts["state.career"] == "unemployed"), 0.02, 0.12);

        var firstSex = Population.Value.Select(p => p.Facts.Num("state.age_first_sex")).OfType<double>().Order().ToList();
        Assert.InRange(firstSex[firstSex.Count / 2], 15, 20);
        var leftHome = Population.Value.Select(p => p.Facts.Num("state.age_left_home")).OfType<double>().Order().ToList();
        Assert.InRange(leftHome[leftHome.Count / 2], 18, 23);
    }

    [Fact]
    public void WealthDoesNotPolarize()
    {
        Assert.InRange(Share(p => p.Facts["state.wealth"] == "poverty"), 0.04, 0.2);
        Assert.InRange(Share(p => p.Facts["state.wealth"] is "lower_middle" or "upper_middle" or "working_class"), 0.6, 0.95);
    }

    [Fact]
    public void ReadingsFollowTemperament()
    {
        // Anxious pawns read a parental divorce as "people leave" far more often.
        var divorces = Population.Value
            .SelectMany(p => p.History.Where(e => e.ItemId == "parental_divorce").Select(e => (p, e)))
            .ToList();
        double Rate(Func<Pawn, bool> who) =>
            divorces.Where(x => who(x.p)).Average(x => x.e.Reading == "people_leave" ? 1.0 : 0.0);
        Assert.True(Rate(p => p.Temperament["threat_sensitivity"] > 0.8) > Rate(p => p.Temperament["threat_sensitivity"] < 0.2) + 0.2);
    }

    [Fact]
    public void LifeDetailsExplainEachEntry()
    {
        var p = Gen.Generate(7, new GenerateOptions { Trace = true, LifeDetails = true });
        Assert.All(p.History.Where(e => e.Type != LifeItemType.Condition), e => Assert.False(string.IsNullOrEmpty(e.Explain)));
        Assert.Contains(p.Trace!, t => t.Key == "state.career");
        Assert.Contains(p.Trace!, t => t.Key == "life.parental_divorce");
    }

    [Fact]
    public void LifeFactsCannotBeOverridden()
    {
        var o = new Facts();
        o.Set("state.career", "professional");
        var e = Assert.Throws<ArgumentException>(() => Gen.Generate(1, new GenerateOptions { Overrides = o }));
        Assert.Contains("life simulation", e.Message);
    }

    [Fact]
    public void OverridingBirthFactsChangesTheLife()
    {
        // A pawn with a high-conflict, substance-dependent home is more likely to see a divorce.
        var calm = new Facts();
        calm.Set("household.marriage_quality", "warm_stable");
        var rough = new Facts();
        rough.Set("household.marriage_quality", "high_conflict");
        int Count(Facts o) => Enumerable.Range(0, 600).Count(i =>
        {
            var p = Gen.Generate((ulong)i, new GenerateOptions { Overrides = o });
            return p.Facts["life.parental_divorce"] != "never";
        });
        Assert.True(Count(rough) > Count(calm) * 2);
    }

    static string MinimalLife(string items) => $$"""
        stages:
          - { id: childhood, from: 0, to: 17 }
          - { id: adulthood, from: 18, to: 120 }
        channels: [rupture]
        items:
        {{items}}
        """;

    [Theory]
    [InlineData("""
          - id: x
            type: event
            rate: { base: "0.5", weights: { state.nope: 1 } }
        """, "not a known fact")]
    [InlineData("""
          - id: x
            type: event
            rate: { base: "0.5" }
            effects: { set: { household.wealth: poverty } }
        """, "can only set state.")]
    [InlineData("""
          - id: x
            type: event
            rate: { base: "0.5" }
            effects: { channels: { joy: 1 } }
        """, "unknown channel 'joy'")]
    [InlineData("""
          - id: x
            type: event
            rate: { base: rate.nothing }
        """, "base_rates")]
    [InlineData("""
          - id: x
            type: event
            rate: { base: "0.5", weights: { household.wealth: 2 } }
        """, "is categorical")]
    [InlineData("""
          - id: x
            type: condition
            kind: state
            values: [a, b]
            initial_value: c
        """, "initial_value 'c'")]
    [InlineData("""
          - id: x
            type: fork
            options: [{ id: a }]
        """, "needs ages or a frequency")]
    [InlineData("""
          - id: x
            type: event
            window: [teens]
            rate: { base: "0.5" }
        """, "unknown stage 'teens'")]
    public void BadLifeDataIsRejected(string items, string message)
    {
        var e = Assert.Throws<DataException>(() => TestData.WithLife(MinimalLife(items)));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void MustHappenEventsAlwaysHappen()
    {
        var data = TestData.WithLife(MinimalLife("""
          - id: flag
            type: condition
            kind: state
            values: ["no", "yes"]
            initial_value: "no"
          - id: rare
            type: event
            max: 1
            must_happen: true
            rate: { base: "0.0001" }
            effects: { set: { state.flag: "yes" } }
        """));
        var gen = new PawnGenerator(data);
        for (ulong s = 0; s < 100; s++)
            Assert.Equal("yes", gen.Generate(s).Facts["state.flag"]);
    }
}
