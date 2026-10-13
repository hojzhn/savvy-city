using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;
using YouWillDate.Sim.Population;
using YouWillDate.Sim.Relations;

namespace YouWillDate.Sim.Tests;

public class CityTests
{
    static readonly GameData Data = TestData.Load();
    static readonly Lazy<City> Town = new(() => new CityGenerator(Data).Generate(3, 200));
    static readonly Lazy<List<Pawn>> Pawns = new(() =>
    {
        var gen = new PawnGenerator(Data);
        return Enumerable.Range(0, 2000).Select(i => gen.Generate((ulong)(50_000 + i), new GenerateOptions { SkipAdult = true })).ToList();
    });

    static (double Mean, double Sd) Stats(IEnumerable<double> values)
    {
        var v = values.ToList();
        double mean = v.Average();
        return (mean, Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / v.Count));
    }

    // ------------------------------------------------------------ pawn fields

    [Fact]
    public void BodyFieldsAreInheritedOnTheZScale()
    {
        foreach (var b in Data.Config.Body.Traits)
        {
            var (mean, sd) = Stats(Pawns.Value.Select(p => p.Facts.Num($"pawn.{b}_z")!.Value));
            Assert.InRange(mean, -0.1, 0.1);
            Assert.InRange(sd, 0.85, 1.15);
            // Taller parents, taller children.
            var high = Pawns.Value.Where(p => p.Facts.Num($"mother.{b}_z") + p.Facts.Num($"father.{b}_z") > 1).ToList();
            Assert.True(high.Average(p => p.Facts.Num($"pawn.{b}_z")!.Value) > 0.4);
        }
    }

    [Fact]
    public void SocialInsightIsATemperamentTrait()
    {
        var pawn = new PawnGenerator(Data).Generate(5);
        Assert.InRange(pawn.Facts.Num("pawn.social_insight")!.Value, 0, 1);
        Assert.NotNull(pawn.Facts.Num("adult.social_insight"));
    }

    [Fact]
    public void PartnersMatchOrientation()
    {
        double straight = Pawns.Value.Count(p => p.Facts.Cat("identity.orientation") == "straight") / (double)Pawns.Value.Count;
        Assert.InRange(straight, 0.8, 0.95);
        foreach (var p in Pawns.Value)
        {
            string orientation = p.Facts["identity.orientation"];
            foreach (var partner in p.Partners)
            {
                if (orientation == "straight") Assert.NotEqual(p.Sex, partner.Sex);
                if (orientation == "gay") Assert.Equal(p.Sex, partner.Sex);
            }
        }
    }

    [Fact]
    public void PastPartnersFeedRevealedPreferences()
    {
        var gen = new PawnGenerator(Data);
        for (ulong seed = 0; seed < 200; seed++)
        {
            var p = gen.Generate(seed);
            int ended = p.Partners.Count(x => x.Ended);
            Assert.Equal(ended, p.Facts.Num("hist.ex_count"));
            if (ended == 0) Assert.Equal(0, p.Facts.Num("hist.ex_status"));
            Assert.All(p.Partners, x => Assert.InRange(x.Availability, 0, 1));
        }
    }

    [Fact]
    public void CloseFriendRecordsFollowTheCount()
    {
        // Social circle and health 6: one record per close friend, shares sum to 1, years known <= age - 5.
        var gen = new PawnGenerator(Data);
        var sources = Data.Adult!.Items.Single(i => i.Type == Adult.AdultType.FriendSources).Sources;
        for (ulong seed = 0; seed < 200; seed++)
        {
            var p = gen.Generate(seed);
            Assert.Equal(p.Facts.Num("adult.close_friends"), p.Adult!.Friends.Count);
            Assert.Equal(1, sources.Sum(s => p.Facts.Num($"adult.friend_sources.{s.Id}")!.Value), 6);
            Assert.All(p.Adult.Friends, f =>
            {
                Assert.InRange(f.YearsKnown, 0, p.Age - 5);
                Assert.InRange(f.Closeness, 0, 1);
            });
        }
    }

    // ------------------------------------------------------------ the city

    [Fact]
    public void EveryCurrentPartnerIsAResidentWhoPointsBack()
    {
        var c = Town.Value;
        Assert.InRange(c.Residents.Count, 150, 260);
        foreach (var r in c.Residents)
        {
            if (r.Relationship == "single")
            {
                Assert.Null(r.PartnerId);
                continue;
            }
            var p = c.Residents[r.PartnerId!.Value];
            Assert.Equal(r.Id, p.PartnerId);
            Assert.Equal(r.Relationship, p.Relationship);
            Assert.Equal(r.CurrentPartner!.Sex, p.Sex);
            Assert.InRange(Math.Abs(r.RelationshipYears - p.RelationshipYears), 0, Data.Relations!.City.YearsTolerance);
            Assert.Equal(p.Id, r.PartnerLinks[r.CurrentPartnerIndex]);
        }
        Assert.Equal(c.Residents.Count(r => r.PartnerId is not null), 2 * c.Couples.Count);
    }

    [Fact]
    public void HouseholdsHoldEveryResidentOnce()
    {
        var c = Town.Value;
        var members = c.Households.SelectMany(h => h.Members).ToList();
        Assert.Equal(c.Residents.Count, members.Count);
        Assert.Equal(c.Residents.Count, members.Distinct().Count());
        foreach (var r in c.Residents) Assert.Contains(r.Id, c.Households[r.HouseholdId].Members);
        foreach (var couple in c.Couples.Where(x => x.Status is "cohabiting" or "married"))
            Assert.Equal(c.Residents[couple.A].HouseholdId, c.Residents[couple.B].HouseholdId);
    }

    [Fact]
    public void ExAndFriendLinksAreMutual()
    {
        var c = Town.Value;
        foreach (var r in c.Residents)
        {
            for (int i = 0; i < r.PartnerLinks.Length; i++)
            {
                if (r.PartnerLinks[i] is not int other) continue;
                var o = c.Residents[other];
                Assert.Contains((int?)r.Id, o.PartnerLinks);
                Assert.Equal(r.Pawn.Partners[i].Sex, o.Sex);
            }
            for (int i = 0; i < r.FriendLinks.Length; i++)
            {
                if (r.FriendLinks[i] is not int other) continue;
                var o = c.Residents[other];
                Assert.Contains((int?)r.Id, o.FriendLinks);
                Assert.Equal(r.Friends[i].Sex, o.Sex);
            }
        }
        Assert.True(c.Stats.ExLinks > 0);
        Assert.True(c.Stats.FriendLinks > 0);
    }

    [Fact]
    public void SameSeedSameCity()
    {
        var gen = new CityGenerator(Data);
        var a = gen.Generate(11, 40);
        var b = gen.Generate(11, 40);
        Assert.Equal(a.Residents.Select(r => r.Pawn.Seed), b.Residents.Select(r => r.Pawn.Seed));
        Assert.Equal(a.Couples, b.Couples);
        Assert.Equal(a.Residents.SelectMany(r => r.FriendLinks), b.Residents.SelectMany(r => r.FriendLinks));
        Assert.Equal(a.Start.Relationships.SelectMany(r => r.Pattern), b.Start.Relationships.SelectMany(r => r.Pattern));
        Assert.NotEqual(a.World.M[0, 0], new World(Data, 12).M[0, 0]);
    }

    [Fact]
    public void AuraTasteAndDesireAreWellFormed()
    {
        var c = Town.Value;
        int k = Data.Aura!.Harmonics;
        foreach (var r in c.Residents)
        {
            Assert.Equal(Data.Aura.ProfileVector.Length, r.X.Length);
            Assert.Equal(k, r.Aura.Amp.Length);
            Assert.All(r.Aura.Phase, p => Assert.InRange(p, -Math.PI, Math.PI));
            Assert.All(r.Taste.Offset, o => Assert.InRange(o, 0, Math.PI));
            Assert.Equal(1, r.Taste.Weight.Sum(), 9);
            Assert.Equal(1, Math.Sqrt(r.U.Sum(u => u * u)), 9);
        }
        // ā makes the population mean amplitude about 1.
        Assert.InRange(c.Residents.Average(r => r.Aura.Amp.Average()), 0.85, 1.15);
    }

    // ------------------------------------------------------------ starting state (spec section 9)

    [Fact]
    public void StartingStateFollowsTheInvariants()
    {
        var c = Town.Value;
        var s = c.Start;
        double cap = Data.Relations!.Start.Source.HiddenResentmentCap;
        Assert.Equal(c.Couples.Count, s.Relationships.Count(r => r.Origin == "couple"));
        Assert.Equal(c.Residents.Count, s.Pawns.Count);
        foreach (var r in s.Relationships)
        {
            Assert.All(r.Pattern, v => Assert.InRange(v, -1, 1));
            Assert.InRange(r.Power, -1, 1);
            Assert.True(r.StartedDay < 0);
            Assert.All(r.Sides, side =>
            {
                Assert.InRange(side.HiddenResentment, 0, cap);
                Assert.InRange(side.Trust, 0, 1);
                Assert.InRange(side.Investment, 0, 1);
                Assert.True(side.Grudge >= 0);
            });
            Assert.All(r.Agreements, a => Assert.InRange(a.MadeDay, r.StartedDay, 0));
        }
        foreach (var couple in c.Couples.Where(x => x.Status == "married"))
        {
            var record = s.Relationships.Single(r => r.Origin == "couple" && r.A == couple.A && r.B == couple.B);
            Assert.Contains(record.Agreements, a => a.Op == "formalize");
        }
        foreach (var p in s.Pawns)
        {
            Assert.InRange(p.Mood, -1, 1);
            Assert.InRange(p.Threat, 0, 1);
            Assert.InRange(s.Readiness(p.Pawn), 0, 1);
            // Pawns only know about people they have a record with.
            var known = s.Of(p.Pawn).Select(r => r.Other(p.Pawn)).ToHashSet();
            Assert.All(p.Knowledge, f => Assert.Contains(f.Subject, known));
        }
    }

    [Fact]
    public void PartnersKnowEachOtherOnSight()
    {
        var s = Town.Value.Start;
        foreach (var r in s.Relationships.Where(r => r.Origin == "couple"))
        {
            Assert.Contains(s.Pawns[r.A].Knowledge, f => f.Subject == r.B && f.Field == "pawn.sex");
            Assert.Contains(s.Pawns[r.B].Knowledge, f => f.Subject == r.A && f.Field == "age");
        }
    }

    // ------------------------------------------------------------ validation

    [Theory]
    [InlineData("exclusivity: { strict: 1.0,", "exclusivity: { nope: 1.0, strict: 1.0,", "not an adult.monogamy_view value")]
    [InlineData("    cheated_on: 0.8", "    vanished: 0.8", "not a way a relationship ends")]
    [InlineData("goal_pressure_goals: [marriage, children]", "goal_pressure_goals: [world_peace]", "not a goal in adult.yaml")]
    public void BadRelationshipsDataIsRejected(string find, string replace, string message)
    {
        string yaml = TestData.ReadFile("relationships.yaml");
        Assert.Contains(find, yaml);
        var e = Assert.Throws<DataException>(() => TestData.WithRelations(yaml.Replace(find, replace), TestData.ReadFile("aura.yaml")));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void CategoricalProfileEntriesAreRejected()
    {
        string aura = TestData.ReadFile("aura.yaml").Replace("  - age\n", "  - adult.attachment_style\n").Replace("  - age\r\n", "  - adult.attachment_style\r\n");
        var e = Assert.Throws<DataException>(() => TestData.WithRelations(TestData.ReadFile("relationships.yaml"), aura));
        Assert.Contains("categorical", e.Message);
    }
}
