using System.Globalization;
using System.Text;

namespace YouWillDate.Sim.Population;

/// <summary>Plain-text summary of a city, for the CLI and for tuning.</summary>
public static class CityText
{
    public static string Describe(City c)
    {
        var sb = new StringBuilder();
        var r = c.Residents;
        string Pct(double part, double whole) => whole == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{100 * part / whole:0.0}%");
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        sb.AppendLine($"World seed {c.Seed}: {r.Count} residents");
        sb.AppendLine($"  pawns generated {c.Stats.Generated}, discarded while searching for partners {c.Stats.Discarded}, " +
                      $"dropped with no partner found {c.Stats.DroppedUnmatched}");

        sb.AppendLine().AppendLine("Couples");
        foreach (var g in c.Couples.GroupBy(x => x.Status).OrderBy(g => g.Key))
            sb.AppendLine($"  {g.Key,-12} {g.Count(),5}");
        int sameSex = c.Couples.Count(x => r[x.A].Sex == r[x.B].Sex);
        sb.AppendLine($"  same-sex {sameSex} ({Pct(sameSex, c.Couples.Count)})");
        if (c.Couples.Count > 0)
        {
            sb.AppendLine($"  mean match cost {N(c.Couples.Average(x => x.MatchCost))}");
            sb.AppendLine("  relationship-years gap: " + string.Join(", ", c.Couples
                .GroupBy(x => Math.Abs(r[x.A].RelationshipYears - r[x.B].RelationshipYears)).OrderBy(g => g.Key)
                .Select(g => $"{g.Key}: {g.Count()}")));
            sb.AppendLine($"  mean age gap {N(c.Couples.Average(x => Math.Abs(r[x.A].Age - r[x.B].Age)))} years");
        }
        sb.AppendLine($"  partnered residents {Pct(r.Count(x => x.PartnerId is not null), r.Count)}, " +
                      $"married {Pct(r.Count(x => x.Relationship == "married"), r.Count)}");

        sb.AppendLine().AppendLine("Households");
        foreach (var g in c.Households.GroupBy(h => h.Kind).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  {g.Key,-22} {g.Count(),5}");

        int exes = r.Sum(x => x.Pawn.Partners.Count(p => p.Ended));
        int friends = r.Sum(x => x.Friends.Count);
        sb.AppendLine().AppendLine("Links");
        sb.AppendLine($"  dissolved involvements with residents {c.Stats.ExLinks * 2} of {exes} ({Pct(c.Stats.ExLinks * 2, exes)})");
        int alongside = r.Sum(x => x.AlongsideIndexes.Count());
        sb.AppendLine($"  residents with an involvement alongside their primary one {r.Count(x => x.AlongsideIndexes.Any())} ({Pct(r.Count(x => x.AlongsideIndexes.Any()), r.Count)}); " +
                      $"{c.Stats.AlongsideLinks * 2} of those {alongside} involvements are with residents");
        sb.AppendLine($"  close friends who are residents {c.Stats.FriendLinks * 2} of {friends} ({Pct(c.Stats.FriendLinks * 2, friends)})");

        var s = c.Start;
        sb.AppendLine().AppendLine("Starting state (spec section 9)");
        foreach (var g in s.Relationships.GroupBy(x => x.Origin))
            sb.AppendLine($"  {g.Key,-8} records {g.Count(),5}");
        var couples = s.Relationships.Where(x => x.Origin == "couple").ToList();
        if (couples.Count > 0)
        {
            var sides = couples.SelectMany(x => x.Sides).ToList();
            sb.AppendLine($"  couples: mean |D − P| {N(sides.Average(x => Dist(x.Desire, couples.First(cc => cc.Sides.Contains(x)).Pattern)))}, " +
                          $"mean hidden resentment {N(sides.Average(x => x.HiddenResentment))}, " +
                          $"above 0.5 {Pct(sides.Count(x => x.HiddenResentment > 0.5), sides.Count)}");
            sb.AppendLine($"  couples: mean trust {N(sides.Average(x => x.Trust))}, investment {N(sides.Average(x => x.Investment))}, " +
                          $"with agreements {Pct(couples.Count(x => x.Agreements.Count > 0), couples.Count)}");
        }
        sb.AppendLine($"  mood {N(s.Pawns.Average(p => p.Mood))}, loneliness {N(s.Pawns.Average(p => p.Loneliness))}, " +
                      $"threat > 0 {Pct(s.Pawns.Count(p => p.Threat > 0), s.Pawns.Count)}, " +
                      $"readiness {N(Enumerable.Range(0, r.Count).Average(s.Readiness))}");
        sb.AppendLine($"  grudges above 0.1 {Pct(s.Pawns.Count(p => p.Exes.Any(e => e.Grudge > 0.1)), s.Pawns.Count)} of pawns; " +
                      $"known facts per pawn {N(s.Pawns.Average(p => p.Knowledge.Count))}");
        return sb.ToString();
    }

    static double Dist(double[] a, double[] b) => Math.Sqrt(a.Select((x, i) => (x - b[i]) * (x - b[i])).Sum());
}
