using System.Globalization;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;
using YouWillDate.Sim.Life;
using YouWillDate.Sim.Relations;

namespace YouWillDate.Sim.Population;

/// <summary>
/// Builds a city of linked pawns from a world seed. Each pawn still runs its own
/// life simulation; the city pairs pawns whose current relationships line up
/// (pool matching), so both partners are real residents, then links past
/// partners and close friends to residents who fit them. Finally it builds the
/// runtime's starting state (spec section 9).
/// </summary>
public sealed class CityGenerator(GameData data)
{
    readonly PawnGenerator _gen = new(data);
    readonly CityDto _c = data.Relations?.City ?? throw new InvalidOperationException("a city needs relationships.yaml");

    /// <param name="size">Residents to generate before searching for missing partners. The final count can differ slightly.</param>
    public City Generate(ulong worldSeed, int size)
    {
        var world = new World(data, worldSeed);
        var pawns = new List<Pawn>();
        var partner = new Dictionary<int, int>();
        var open = new List<int>();
        int made = 0, discarded = 0;

        Pawn Next() => _gen.Generate(Rng.SubSeed(worldSeed, "pawn", (made++).ToString(CultureInfo.InvariantCulture)));

        // 1. Fill the city. A partnered pawn pairs with the best waiting pawn, or waits.
        while (pawns.Count < size)
        {
            var p = Next();
            int index = pawns.Count;
            pawns.Add(p);
            if (!Partnered(p)) continue;
            int match = BestMatch(p, open, pawns);
            if (match < 0) open.Add(index);
            else Pair(match, index);
        }

        // 2. Search for the partners still missing. Candidates that fit nobody are discarded.
        int budget = (int)(size * _c.OverflowFactor);
        for (int tries = 0; tries < budget && open.Count > 0; tries++)
        {
            var p = Next();
            int match = Partnered(p) ? BestMatch(p, open, pawns) : -1;
            if (match < 0)
            {
                discarded++;
                continue;
            }
            pawns.Add(p);
            Pair(match, pawns.Count - 1);
        }

        // 3. Partnered pawns nobody fits leave the city: every current partner must be a resident.
        var dropped = open.ToHashSet();
        var ids = new Dictionary<int, int>();
        var residents = new List<Resident>();
        for (int i = 0; i < pawns.Count; i++)
        {
            if (dropped.Contains(i)) continue;
            ids[i] = residents.Count;
            residents.Add(new Resident { Id = residents.Count, Pawn = pawns[i] });
        }

        var couples = new List<Couple>();
        foreach (var (a, b) in partner.Where(kv => kv.Key < kv.Value))
        {
            var ra = residents[ids[a]];
            var rb = residents[ids[b]];
            var couple = new Couple(couples.Count, ra.Id, rb.Id, ra.Relationship,
                (int)Math.Round((ra.RelationshipYears + rb.RelationshipYears) / 2.0, MidpointRounding.AwayFromZero),
                Math.Max(ra.Children, rb.Children), Cost(ra.Pawn, rb.Pawn));
            couples.Add(couple);
            ra.PartnerId = rb.Id;
            rb.PartnerId = ra.Id;
            ra.CoupleId = rb.CoupleId = couple.Id;
        }

        foreach (var r in residents)
        {
            r.X = world.ProfileVector(r.Pawn);
            r.Aura = world.AuraOf(r.X, r.Pawn.Seed);
            r.Taste = world.TasteOf(r.X, r.Pawn.Seed);
            r.U = world.DesireDirection(r.X);
            r.PartnerLinks = new int?[r.Pawn.Partners.Count];
            r.FriendLinks = new int?[r.Friends.Count];
            if (r.PartnerId is int pid) r.PartnerLinks[^1] = pid;
        }

        var households = BuildHouseholds(residents, couples);
        int exLinks = LinkExes(residents, worldSeed);
        int friendLinks = LinkFriends(residents, worldSeed);
        var stats = new CityStats(made, discarded, dropped.Count, exLinks, friendLinks);

        var city = new City
        {
            Seed = worldSeed, World = world, Residents = residents, Couples = couples, Households = households, Stats = stats,
            Start = StartingState.Empty,
        };
        return new City
        {
            Seed = worldSeed, World = world, Residents = residents, Couples = couples, Households = households, Stats = stats,
            Start = StartingState.Build(data, city),
        };

        void Pair(int a, int b)
        {
            partner[a] = b;
            partner[b] = a;
            open.Remove(a);
        }
    }

    static bool Partnered(Pawn p) =>
        p.Facts.Cat("state.relationship") is { } s && s != "single" && p.Partners.Count > 0 && !p.Partners[^1].Ended;

    // ------------------------------------------------------------ couples

    int BestMatch(Pawn p, List<int> open, List<Pawn> pawns)
    {
        int best = -1;
        double bestCost = double.MaxValue;
        foreach (int i in open)
        {
            if (!Compatible(p, pawns[i])) continue;
            double cost = Cost(p, pawns[i]);
            if (cost <= _c.MaxCost && cost < bestCost) (best, bestCost) = (i, cost);
        }
        return best;
    }

    /// <summary>Same status, each the sex the other's history says, and relationship lengths close enough.</summary>
    bool Compatible(Pawn a, Pawn b)
    {
        if (a.Facts.Cat("state.relationship") != b.Facts.Cat("state.relationship")) return false;
        if (a.Partners[^1].Sex != b.Sex || b.Partners[^1].Sex != a.Sex) return false;
        return Math.Abs(Years(a) - Years(b)) <= _c.YearsTolerance;
    }

    double Cost(Pawn a, Pawn b)
    {
        double gap = b.Age - a.Age;
        double age = (Math.Abs(gap - a.Partners[^1].AgeGap) + Math.Abs(-gap - b.Partners[^1].AgeGap)) / 2;
        double cost = _c.YearsCost * Math.Abs(Years(a) - Years(b)) + _c.AgeCost * age;
        if (a.Facts.Cat("state.relationship") is "cohabiting" or "married")
            cost += _c.ChildrenCost * Math.Abs((a.Facts.Num("state.children") ?? 0) - (b.Facts.Num("state.children") ?? 0));
        return cost;
    }

    static double Years(Pawn p) => p.Facts.Num("state.relationship_years") ?? 0;

    static List<Household> BuildHouseholds(List<Resident> residents, List<Couple> couples)
    {
        var households = new List<Household>();
        foreach (var c in couples.Where(c => c.Status is "cohabiting" or "married"))
        {
            var h = new Household(households.Count, [c.A, c.B], c.Children, c.Children > 0 ? "partner_and_children" : "partner");
            households.Add(h);
            residents[c.A].HouseholdId = residents[c.B].HouseholdId = h.Id;
        }
        foreach (var r in residents.Where(r => r.HouseholdId < 0))
        {
            string kind = r.Pawn.Facts.Cat("state.current_household") ?? "alone";
            // A dating pawn lives apart from its partner; a "partner" household would be wrong here.
            if (kind is "partner" or "partner_and_children") kind = r.Children > 0 ? "single_parent" : "alone";
            var h = new Household(households.Count, [r.Id], r.Children, kind);
            households.Add(h);
            r.HouseholdId = h.Id;
        }
        return households;
    }

    // ------------------------------------------------------------ exes

    static readonly HashSet<string> Victim = ["cheated_on", "infidelity"];

    /// <summary>One side cheating needs the other to have been cheated on; two victims or two cheaters never pair.</summary>
    static bool StoriesFit(string? a, string? b)
    {
        bool va = a is not null && Victim.Contains(a), vb = b is not null && Victim.Contains(b);
        bool ca = a == "cheated", cb = b == "cheated";
        if ((va && vb) || (ca && cb)) return false;
        if (ca) return vb;
        if (cb) return va;
        return true;
    }

    int LinkExes(List<Resident> residents, ulong seed)
    {
        // Ended relationships, indexed by calendar start year.
        var byStart = new Dictionary<int, List<(Resident R, int Index)>>();
        foreach (var r in residents)
            for (int i = 0; i < r.Pawn.Partners.Count; i++)
                if (r.Pawn.Partners[i].Ended)
                {
                    int year = r.Pawn.BirthYear + r.Pawn.Partners[i].StartAge;
                    if (!byStart.TryGetValue(year, out var l)) byStart[year] = l = new();
                    l.Add((r, i));
                }

        int links = 0;
        foreach (var r in residents)
            for (int i = 0; i < r.Pawn.Partners.Count; i++)
            {
                var rec = r.Pawn.Partners[i];
                if (!rec.Ended || r.PartnerLinks[i] is not null) continue;
                if (!Rng.For(seed, "city", "ex", r.Id.ToString(CultureInfo.InvariantCulture), i.ToString(CultureInfo.InvariantCulture))
                        .Chance(_c.ExLinkChance)) continue;

                int start = r.Pawn.BirthYear + rec.StartAge, end = r.Pawn.BirthYear + rec.EndAge!.Value;
                (Resident R, int Index)? best = null;
                double bestCost = double.MaxValue;
                for (int y = start - 1; y <= start + 1; y++)
                {
                    if (!byStart.TryGetValue(y, out var list)) continue;
                    foreach (var (s, j) in list)
                    {
                        if (s == r || s.Id == r.PartnerId || s.PartnerLinks[j] is not null) continue;
                        var other = s.Pawn.Partners[j];
                        int otherEnd = s.Pawn.BirthYear + other.EndAge!.Value;
                        if (rec.Sex != s.Sex || other.Sex != r.Sex || rec.Married != other.Married) continue;
                        if (Math.Abs(otherEnd - end) > 1 || !StoriesFit(rec.EndReason, other.EndReason)) continue;
                        // Already linked to each other through another record.
                        if (r.PartnerLinks.Contains(s.Id)) continue;
                        double gap = s.Age - r.Age;
                        double cost = Math.Abs(y - start) + Math.Abs(otherEnd - end)
                                      + 0.25 * (Math.Abs(gap - rec.AgeGap) + Math.Abs(-gap - other.AgeGap)) / 2;
                        if (cost < bestCost) (best, bestCost) = ((s, j), cost);
                    }
                }
                if (best is not { } b) continue;
                r.PartnerLinks[i] = b.R.Id;
                b.R.PartnerLinks[b.Index] = r.Id;
                links++;
            }
        return links;
    }

    // ------------------------------------------------------------ friends

    int LinkFriends(List<Resident> residents, ulong seed)
    {
        var bySource = new Dictionary<string, List<(Resident R, int Index)>>();
        foreach (var r in residents)
            for (int i = 0; i < r.Friends.Count; i++)
            {
                string source = r.Friends[i].Source;
                if (!bySource.TryGetValue(source, out var l)) bySource[source] = l = new();
                l.Add((r, i));
            }

        int links = 0;
        foreach (var r in residents)
            for (int i = 0; i < r.Friends.Count; i++)
            {
                var rec = r.Friends[i];
                if (r.FriendLinks[i] is not null) continue;
                if (!Rng.For(seed, "city", "friend", r.Id.ToString(CultureInfo.InvariantCulture), i.ToString(CultureInfo.InvariantCulture))
                        .Chance(_c.FriendLinkChance)) continue;

                double maxGap = _c.FriendAgeGap.GetValueOrDefault(rec.Source, _c.DefaultFriendAgeGap);
                // Childhood friends of a pawn who grew up elsewhere live elsewhere.
                bool local = rec.Source == "childhood";
                if (local && r.Pawn.Facts.Cat("pawn.born_locally") != "yes") continue;
                double rStatus = r.Pawn.Facts.Num("adult.circle_status") ?? 0;
                (Resident R, int Index)? best = null;
                double bestCost = double.MaxValue;
                foreach (var (s, j) in bySource[rec.Source])
                {
                    if (s == r || s.Id == r.PartnerId || s.FriendLinks[j] is not null || r.FriendLinks.Contains(s.Id)) continue;
                    var other = s.Friends[j];
                    if (rec.Sex != s.Sex || other.Sex != r.Sex) continue;
                    if (Math.Abs(s.Age - r.Age) > maxGap || Math.Abs(other.YearsKnown - rec.YearsKnown) > 2) continue;
                    if (local && s.Pawn.Facts.Cat("pawn.born_locally") != "yes") continue;
                    double cost = Math.Abs(other.YearsKnown - rec.YearsKnown)
                                  + 0.5 * Math.Abs(rec.StatusZ - (s.Pawn.Facts.Num("adult.circle_status") ?? 0))
                                  + 0.5 * Math.Abs(other.StatusZ - rStatus);
                    if (cost < bestCost) (best, bestCost) = ((s, j), cost);
                }
                if (best is not { } b) continue;
                r.FriendLinks[i] = b.R.Id;
                b.R.FriendLinks[b.Index] = r.Id;
                links++;
            }
        return links;
    }
}
