using System.Globalization;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Life;
using YouWillDate.Sim.Population;

namespace YouWillDate.Sim.Relations;

/// <summary>An agreement: an anchor in pattern space with a mass (attraction spec 8.3).</summary>
public sealed record Agreement(string Op, double[] Anchor, double Mass, int MadeDay, double Exclusivity);

/// <summary>One side's view of a relationship (8.1).</summary>
public sealed class RelationshipSide
{
    public required int Pawn { get; init; }
    /// <summary>D: where this side wants the pattern. Recomputed by the runtime each week.</summary>
    public double[] Desire { get; set; } = [];
    public double Drive { get; set; }
    public double Tolerance { get; set; }
    /// <summary>H (8.3): swallowed tension. The partner never sees it.</summary>
    public double HiddenResentment { get; set; }
    /// <summary>D̂: this side's estimate of the other's desire (8.6).</summary>
    public double[] EstimateOfOther { get; set; } = [];
    public double Investment { get; set; }
    public double Trust { get; set; }
    public double Resentment { get; set; }
    public double Grudge { get; set; }
}

/// <summary>
/// The relationship record (8.1). It has no relationship type: the pattern, the
/// agreements and each side's desire are separate values. Origin only says what
/// in the life histories the record was built from; the runtime never reads it.
/// </summary>
public sealed class RelationshipRecord
{
    public required int Id { get; init; }
    public required int A { get; init; }
    public required int B { get; init; }
    public required string Origin { get; init; }
    /// <summary>Day 0 is the start of play; earlier days are negative.</summary>
    public required int StartedDay { get; init; }
    public required double[] Pattern { get; set; }
    public required double[] Velocity { get; set; }
    public int WeeksStable { get; set; }
    public List<Agreement> Agreements { get; } = new();
    /// <summary>−1 to 1, positive favors A.</summary>
    public double Power { get; set; }
    /// <summary>0 = public, 1 = nobody outside knows.</summary>
    public double Secrecy { get; set; }
    public int SexEncounters { get; set; }
    public int? SexLastDay { get; set; }
    /// <summary>True after a Dissolve: no anchors, and the pattern drains toward the origin.</summary>
    public bool Dissolved { get; set; }
    public required RelationshipSide[] Sides { get; init; }

    public RelationshipSide Side(int pawn) => Sides[0].Pawn == pawn ? Sides[0] : Sides[1];
    public int Other(int pawn) => pawn == A ? B : A;
}

/// <summary>Something a pawn knows about another (8.1 knowledge model). Values are read from the subject; nothing at the start is a lie.</summary>
public sealed record KnownFact(int Subject, string Field, int Day, string Source);

/// <summary>A pawn's grudge toward a past partner, who may be a resident or only a record.</summary>
public sealed record ExFeeling(int PartnerIndex, int? Resident, string? EndReason, double MonthsSince, double Grudge);

/// <summary>A pawn's state at the start of play.</summary>
public sealed class PawnState
{
    public required int Pawn { get; init; }
    /// <summary>−1 to 1.</summary>
    public double Mood { get; init; }
    public double Loneliness { get; init; }
    /// <summary>θ (8.2), 0 to 1.</summary>
    public double Threat { get; init; }
    public double? MonthsSinceBreakup { get; init; }
    /// <summary>recency = exp(−months since the last breakup / 3), 0 without one (7.6).</summary>
    public double Recency => MonthsSinceBreakup is double m ? Math.Exp(-m / 3) : 0;
    /// <summary>Largest goal pressure among the marriage and children goals (7.6).</summary>
    public double GoalPressure { get; init; }
    public IReadOnlyList<ExFeeling> Exes { get; init; } = [];
    public List<KnownFact> Knowledge { get; } = new();
}

/// <summary>Attachment tendencies (8.2), derived from anxiety and avoidance, never stored.</summary>
public readonly record struct Tendencies(double Voice, double Sensitivity, double ExitFloor, double Reaction)
{
    public static Tendencies Of(Facts f, TendenciesDto t)
    {
        double x = f.Num("adult.anxiety") ?? 0, y = f.Num("adult.avoidance") ?? 0;
        double Lin(AxesDto a) => a.Base + a.X * x + a.Y * y;
        return new Tendencies(MathUtil.Sigmoid(Lin(t.Voice)), MathUtil.Sigmoid(Lin(t.Sensitivity)), Lin(t.ExitFloor), Lin(t.Reaction));
    }
}

/// <summary>
/// Spec section 9: the bridge from life histories to the runtime. Current
/// relationships become patterns with agreements; linked friendships and recent
/// breakups with resident exes become records too. Each pawn gets its mood,
/// threat, grudges and what it knows about the people in its circle.
/// </summary>
public sealed class StartingState
{
    public IReadOnlyList<RelationshipRecord> Relationships { get; private init; } = [];
    public IReadOnlyList<PawnState> Pawns { get; private init; } = [];

    public static readonly StartingState Empty = new();

    const int DaysPerYear = 365;

    public IEnumerable<RelationshipRecord> Of(int pawn) => Relationships.Where(r => r.A == pawn || r.B == pawn);

    /// <summary>diss = sensitivity × |D − P| for one side (8.3), with no overlap pain at the start.</summary>
    public static double Dissatisfaction(RelationshipRecord r, int pawn, double sensitivity) =>
        sensitivity * Distance(r.Side(pawn).Desire, r.Pattern);

    /// <summary>
    /// R_A (7.6): readiness for anyone new. Derived from stored state, never stored.
    /// open = clamp(1 − 0.8 × excl_max × (1 − diss_max), 0.05, 1).
    /// </summary>
    public double Readiness(int pawn)
    {
        var state = Pawns[pawn];
        double sensitivity = _tendencies.TryGetValue(pawn, out var t) ? t.Sensitivity : 0.5;
        double excl = 0, diss = 0;
        foreach (var r in Of(pawn).Where(r => !r.Dissolved && r.Agreements.Count > 0))
        {
            excl = Math.Max(excl, r.Agreements.Max(a => a.Exclusivity));
            diss = Math.Max(diss, Math.Clamp(Dissatisfaction(r, pawn, sensitivity), 0, 1));
        }
        double open = Math.Clamp(1 - 0.8 * excl * (1 - diss), 0.05, 1);
        return Math.Clamp(0.5 + 0.25 * state.Loneliness + 0.15 * state.GoalPressure + 0.10 * state.Mood - 0.30 * state.Recency, 0, 1) * open;
    }

    readonly Dictionary<int, Tendencies> _tendencies = new();

    public static StartingState Build(GameData data, City city)
    {
        var rel = data.Relations!;
        var s = rel.Start;
        var src = s.Source;
        var world = city.World;
        var residents = city.Residents;
        var tend = residents.ToDictionary(r => r.Id, r => Tendencies.Of(r.Pawn.Facts, rel.Tendencies));
        var records = new List<RelationshipRecord>();
        var pawns = new List<PawnState>();
        Rng R(params string[] parts) => Rng.For(city.Seed, ["start", .. parts]);
        string Id(int n) => n.ToString(CultureInfo.InvariantCulture);

        double Satisfaction(Resident r) => MathUtil.Sigmoid(s.Satisfaction.Eval(r.Pawn.Facts, R("satisfaction", Id(r.Id))));
        double Trust(Resident r) => Math.Clamp(s.Trust.Eval(r.Pawn.Facts, R("trust", Id(r.Id))), 0, 1);
        double Standing(Resident r) => s.Standing.Eval(r.Pawn.Facts);
        double Investment(double years, int children) =>
            Math.Clamp(1 - Math.Exp(-years / src.InvestmentYears) + src.InvestmentPerChild * children, 0, 1);
        double Excl(Resident r) => src.Exclusivity.GetValueOrDefault(r.Pawn.Facts.Cat("adult.monogamy_view") ?? "", 1);

        // Goal pressure first: drive reads it.
        var pressure = residents.ToDictionary(r => r.Id, r => (r.Pawn.Adult?.Goals ?? [])
            .Where(g => g.Status == "active" && src.GoalPressureGoals.Contains(g.Id))
            .Select(g => Math.Clamp(1 - (g.Deadline - r.Age) / 5, 0, 1)).DefaultIfEmpty(0).Max());

        double Drive(Resident r, double power)
        {
            double x = r.Pawn.Facts.Num("adult.anxiety") ?? 0, y = r.Pawn.Facts.Num("adult.avoidance") ?? 0;
            double lonely = r.Pawn.Facts.Num("adult.loneliness") ?? 0;
            // 8.3 drive, without the known × emo term until attraction (section 7) exists.
            return MathUtil.Sigmoid(0.8 * x + 0.5 * pressure[r.Id] + 0.5 * lonely - 0.6 * y - 1) * (1 + 0.5 * power);
        }

        double Tolerance(Resident r, double exclusivity)
        {
            double y = r.Pawn.Facts.Num("adult.avoidance") ?? 0;
            double view = rel.Tolerance.GetValueOrDefault(r.Pawn.Facts.Cat("adult.monogamy_view") ?? "", 0.3);
            return Math.Clamp(view + 0.10 * y - 0.3 * tend[r.Id].Sensitivity + 0.15, 0, 1) * (1 - 0.8 * exclusivity);
        }

        // A's estimate of B's desire: projection from A's own direction, fading into observation over the years (8.6).
        double[] Estimate(Resident a, double[] desireB, double years)
        {
            double insight = a.Pawn.Facts.Num("adult.social_insight") ?? 0;
            double projection = 0.5 * (1 - MathUtil.Sigmoid(insight)) * Math.Exp(-years / src.EstimateYears);
            double size = Norm(desireB);
            return desireB.Select((d, i) => projection * size * a.U[i] + (1 - projection) * d).ToArray();
        }

        RelationshipRecord NewRecord(Resident a, Resident b, string origin, int startedDay, double[] pattern, double power) => new()
        {
            Id = records.Count, A = a.Id, B = b.Id, Origin = origin, StartedDay = startedDay,
            Pattern = pattern, Velocity = new double[pattern.Length], WeeksStable = Math.Max(1, -startedDay / 7), Power = power,
            Sides = [new RelationshipSide { Pawn = a.Id }, new RelationshipSide { Pawn = b.Id }],
        };

        // ---- current relationships
        foreach (var c in city.Couples)
        {
            var a = residents[c.A];
            var b = residents[c.B];
            var rng = R("couple", Id(c.Id));
            int startedDay = -(c.Years * DaysPerYear + rng.NextInt(7, DaysPerYear));
            double years = -startedDay / (double)DaysPerYear;
            double satA = Satisfaction(a), satB = Satisfaction(b);
            double[] dA = Scale(a.U, Math.Clamp(src.DesireBase + src.DesireFromSatisfaction * satA, 0, 1));
            double[] dB = Scale(b.U, Math.Clamp(src.DesireBase + src.DesireFromSatisfaction * satB, 0, 1));

            // Satisfaction places P between the two desires: each side's gap is (1 − satisfaction) / sensitivity (8.3 diss).
            double gA = (1 - satA) / Math.Max(tend[a.Id].Sensitivity, 0.05), gB = (1 - satB) / Math.Max(tend[b.Id].Sensitivity, 0.05);
            double t = gA + gB <= 0 ? 0.5 : gA / (gA + gB);
            var pattern = dA.Select((v, i) => Math.Clamp(v + t * (dB[i] - v), -1, 1)).ToArray();

            double power = Math.Tanh(src.PowerScale * (Standing(a) - Standing(b)));
            var record = NewRecord(a, b, "couple", startedDay, pattern, power);
            double exclusivity = (Excl(a) + Excl(b)) / 2;
            if (c.Status != "dating" || c.Years >= src.DefineAfterYears)
                record.Agreements.Add(new Agreement("define", (double[])pattern.Clone(), src.DefineMass,
                    startedDay + Math.Min(-startedDay / 2, 180), exclusivity));
            if (c.Status == "married")
                record.Agreements.Add(new Agreement("formalize", (double[])pattern.Clone(), src.FormalizeMass,
                    MarriedDay(a, startedDay, rng), exclusivity));

            if (world.Readouts.ContainsKey("sexrate"))
            {
                double rate = world.Readout("sexrate", pattern);
                record.SexEncounters = (int)Math.Round(record.WeeksStable * rate);
                if (record.SexEncounters > 0) record.SexLastDay = -1 - rng.NextInt(0, 14);
            }

            foreach (var (self, other, sat, desire, otherDesire, sign) in new[]
                     { (a, b, satA, dA, dB, 1.0), (b, a, satB, dB, dA, -1.0) })
            {
                var side = record.Side(self.Id);
                var tn = tend[self.Id];
                side.Desire = desire;
                side.Drive = Drive(self, sign * power);
                side.Tolerance = Tolerance(self, record.Agreements.Count > 0 ? exclusivity : 0);
                side.Investment = Investment(years, c.Children);
                side.Trust = Trust(self);
                side.Resentment = src.ResentmentFromDissatisfaction * (1 - sat);
                // H built up week by week as in 8.3 (H ← 0.98 H + (1 − voice) τ), then capped.
                double tau = side.Drive * Distance(desire, pattern);
                side.HiddenResentment = Math.Min(src.HiddenResentmentCap,
                    (1 - tn.Voice) * tau * (1 - Math.Pow(0.98, record.WeeksStable)) / 0.02);
                side.EstimateOfOther = Estimate(self, otherDesire, years);
            }
            records.Add(record);
        }

        // ---- friendships between residents
        foreach (var a in residents)
            for (int i = 0; i < a.FriendLinks.Length; i++)
            {
                if (a.FriendLinks[i] is not int bid || bid < a.Id) continue;
                var b = residents[bid];
                int j = Array.IndexOf(b.FriendLinks, (int?)a.Id);
                var fa = a.Friends[i];
                var fb = j >= 0 ? b.Friends[j] : fa;
                double years = (fa.YearsKnown + fb.YearsKnown) / 2.0;
                double closeness = (fa.Closeness + fb.Closeness) / 2;
                var rng = R("friends", Id(a.Id), Id(b.Id));
                int startedDay = -(int)Math.Max(30, years * DaysPerYear + rng.NextInt(0, DaysPerYear));
                double[] dA = Scale(a.U, fa.Closeness * src.FriendDesire), dB = Scale(b.U, fb.Closeness * src.FriendDesire);
                var pattern = dA.Select((v, k) => (v + dB[k]) / 2).ToArray();
                var record = NewRecord(a, b, "friends", startedDay, pattern, Math.Tanh(src.PowerScale * (Standing(a) - Standing(b))));
                foreach (var (self, other, desire, otherDesire, sign) in new[] { (a, b, dA, dB, 1.0), (b, a, dB, dA, -1.0) })
                {
                    var side = record.Side(self.Id);
                    side.Desire = desire;
                    side.Drive = Drive(self, sign * record.Power);
                    side.Tolerance = Tolerance(self, 0);
                    side.Investment = Math.Clamp(closeness * (1 - Math.Exp(-years / src.InvestmentYears)), 0, 1);
                    side.Trust = Trust(self);
                    side.EstimateOfOther = Estimate(self, otherDesire, years);
                }
                records.Add(record);
            }

        // ---- involvements running alongside both residents' primary ones
        foreach (var a in residents)
            foreach (int i in a.AlongsideIndexes)
            {
                if (a.PartnerLinks[i] is not int bid || bid < a.Id) continue;
                var b = residents[bid];
                int j = Array.IndexOf(b.PartnerLinks, (int?)a.Id);
                if (j < 0 || b.Pawn.Partners[j].Ended) continue;
                var pa = a.Pawn.Partners[i];
                var pb = b.Pawn.Partners[j];
                double years = (pa.Years(a.Age) + pb.Years(b.Age)) / 2.0;
                int startedDay = -(int)Math.Max(14, years * DaysPerYear + R("alongside", Id(a.Id), Id(b.Id)).NextInt(0, DaysPerYear));
                double[] dA = Scale(a.U, pa.Level), dB = Scale(b.U, pb.Level);
                var pattern = dA.Select((v, k) => Math.Clamp((v + dB[k]) / 2, -1, 1)).ToArray();
                var record = NewRecord(a, b, "alongside", startedDay, pattern, Math.Tanh(src.PowerScale * (Standing(a) - Standing(b))));
                if (pa.Fields.GetValueOrDefault("agreement") == "defined" && pb.Fields.GetValueOrDefault("agreement") == "defined")
                    record.Agreements.Add(new Agreement("define", (double[])pattern.Clone(), src.DefineMass, startedDay / 2, (Excl(a) + Excl(b)) / 2));
                // Hidden from a partner who has an agreement with either of them (8.4).
                bool Anchored(Resident r) => r.CurrentPartner is { } p && p.Fields.GetValueOrDefault("agreement") != "none";
                record.Secrecy = Anchored(a) || Anchored(b) ? 1 : 0.3;
                foreach (var (self, desire, otherDesire, sign) in new[] { (a, dA, dB, 1.0), (b, dB, dA, -1.0) })
                {
                    var side = record.Side(self.Id);
                    side.Desire = desire;
                    side.Drive = Drive(self, sign * record.Power);
                    side.Tolerance = Tolerance(self, record.Agreements.Count > 0 ? record.Agreements[0].Exclusivity : 0);
                    side.Investment = Investment(years, 0);
                    side.Trust = Trust(self);
                    side.EstimateOfOther = Estimate(self, otherDesire, years);
                }
                records.Add(record);
            }

        // ---- grudges toward every dissolved involvement, and a dissolved record for each one with a resident
        var exes = new Dictionary<int, List<ExFeeling>>();
        double? lastBreakup(Resident r) => exes[r.Id].Count == 0 ? null : exes[r.Id].Min(e => e.MonthsSince);
        foreach (var r in residents)
        {
            var list = exes[r.Id] = new List<ExFeeling>();
            double neuroticism = r.Pawn.Facts.Num("adult.neuroticism") ?? 0;
            double halfLife = Math.Max(1, 3 * (1 + 0.5 * neuroticism));
            for (int i = 0; i < r.Pawn.Partners.Count; i++)
            {
                var p = r.Pawn.Partners[i];
                if (!p.Ended) continue;
                double months = (p.WeeksSinceDissolved ?? 0) * 12 / 52;
                string reason = p.EndReason ?? "";
                // 8.5: grudge = harm × blame, halving every 3 × (1 + 0.5 × neuroticism) months.
                double grudge = src.Harm.GetValueOrDefault(reason, src.DefaultHarm) * src.Blame.GetValueOrDefault(reason, src.DefaultBlame)
                                * Math.Pow(0.5, months / halfLife);
                list.Add(new ExFeeling(i, r.PartnerLinks.Length > i ? r.PartnerLinks[i] : null, p.EndReason, months, grudge));
            }
        }
        foreach (var a in residents)
            foreach (var feel in exes[a.Id])
            {
                if (feel.Resident is not int bid || bid < a.Id) continue;
                var b = residents[bid];
                var theirs = exes[bid].FirstOrDefault(e => e.Resident == a.Id);
                if (theirs is null) continue;
                var pa = a.Pawn.Partners[feel.PartnerIndex];
                var pb = b.Pawn.Partners[theirs.PartnerIndex];
                double months = Math.Min(feel.MonthsSince, theirs.MonthsSince);
                int startedDay = -(int)((months / 12 + pa.Years(a.Age)) * DaysPerYear);
                // The pattern they had, drained since the dissolve (8.3): its size is the involvement's level now.
                double level = (pa.Level + pb.Level) / 2;
                var mid = a.U.Select((v, k) => v + b.U[k]).ToArray();
                double norm = Math.Max(Norm(mid), 1e-9);
                var pattern = mid.Select(v => v / norm * level).ToArray();
                var record = NewRecord(a, b, "ex", startedDay, pattern, Math.Tanh(src.PowerScale * (Standing(a) - Standing(b))));
                record.Dissolved = true;
                record.WeeksStable = Math.Max(1, (int)(months * 52 / 12));
                foreach (var (self, mine, other) in new[] { (a, feel, b), (b, theirs, a) })
                {
                    var side = record.Side(self.Id);
                    side.Desire = new double[pattern.Length];
                    side.Tolerance = Tolerance(self, 0);
                    side.Investment = Investment(pa.Years(a.Age), 0) * Math.Pow(0.5, months / 12);
                    side.Trust = Math.Clamp(Trust(self) - 0.3 * src.Blame.GetValueOrDefault(mine.EndReason ?? "", src.DefaultBlame), 0, 1);
                    side.Grudge = mine.Grudge;
                    side.EstimateOfOther = new double[pattern.Length];
                }
                records.Add(record);
            }

        // ---- each pawn's own state
        foreach (var r in residents)
        {
            double? months = lastBreakup(r);
            double recency = months is double m ? Math.Exp(-m / 3) : 0;
            double lastYearThreat = r.Pawn.History.Where(e => e.Age == r.Age).Sum(e => Math.Max(0, e.Channels.GetValueOrDefault("threat_shift")));
            pawns.Add(new PawnState
            {
                Pawn = r.Id,
                Mood = Math.Tanh(s.Mood.Eval(r.Pawn.Facts, R("mood", Id(r.Id))) - src.MoodFromBreakup * recency),
                Loneliness = r.Pawn.Facts.Num("adult.loneliness") ?? 0,
                Threat = Math.Clamp(src.ThreatFromLastYear * lastYearThreat, 0, 1),
                MonthsSinceBreakup = months,
                GoalPressure = pressure[r.Id],
                Exes = exes[r.Id],
            });
        }

        // ---- what each pawn knows about the people it has a record with
        foreach (var record in records)
        {
            double years = -record.StartedDay / (double)DaysPerYear;
            if (record.Dissolved) years = residents[record.A].Pawn.Partners
                .Where((p, i) => residents[record.A].PartnerLinks[i] == record.B).Select(p => (double)p.Years(residents[record.A].Age)).DefaultIfEmpty(1).First();
            foreach (var (knower, subject) in new[] { (record.A, record.B), (record.B, record.A) })
            {
                double closeness = record.Origin == "friends"
                    ? residents[knower].Friends.Where((f, i) => residents[knower].FriendLinks[i] == subject).Select(f => f.Closeness).DefaultIfEmpty(0.5).First()
                    : 1;
                Learn(pawns[knower], residents[subject], record.StartedDay, years, closeness,
                    R("know", Id(knower), Id(subject)), s);
            }
        }

        var state = new StartingState { Relationships = records, Pawns = pawns };
        foreach (var (id, t) in tend) state._tendencies[id] = t;
        return state;
    }

    static void Learn(PawnState knower, Resident subject, int startedDay, double years, double closeness, Rng rng, StartTables s)
    {
        var src = s.Source.Knowledge;
        var facts = subject.Pawn.Facts;
        double openness = MathUtil.Sigmoid(s.Openness.Eval(facts));
        int span = Math.Max(1, -startedDay);
        int When() => startedDay + rng.NextInt(0, span);

        foreach (var field in s.Fields)
        {
            if (!facts.Contains(field.Key)) continue;
            if (field.Visible)
            {
                knower.Knowledge.Add(new KnownFact(subject.Id, field.Key, startedDay, "seen"));
                continue;
            }
            double p = 1 - Math.Exp(-src.Rate * field.Weight * years * closeness * openness);
            if (rng.NextDouble() < p) knower.Knowledge.Add(new KnownFact(subject.Id, field.Key, When(), "told"));
        }
        foreach (var pick in subject.Pawn.Adult?.Picks ?? [])
        {
            if (!s.Lists.Contains(pick.List)) continue;
            double p = pick.Disclosure * (1 - Math.Exp(-years * closeness));
            if (rng.NextDouble() < p) knower.Knowledge.Add(new KnownFact(subject.Id, $"adult.{pick.List}.{pick.Id}", When(), "told"));
        }
        foreach (var secret in subject.Pawn.Adult?.InnerLife.Secrets ?? [])
        {
            double p = (1 - secret.DiscoveryDifficulty) * (1 - Math.Exp(-src.SecretRate * years * closeness));
            if (rng.NextDouble() < p) knower.Knowledge.Add(new KnownFact(subject.Id, $"secret:{secret.Source}", When(), "told"));
        }
    }

    static int MarriedDay(Resident r, int startedDay, Rng rng)
    {
        var married = r.Pawn.History.LastOrDefault(e => e.Tags.Contains("relationship_married"));
        if (married is null) return startedDay / 2;
        int day = -((r.Age - married.Age) * DaysPerYear + rng.NextInt(0, DaysPerYear));
        return Math.Clamp(day, startedDay, -1);
    }

    static double[] Scale(double[] v, double k) => v.Select(x => x * k).ToArray();
    static double Norm(double[] v) => Math.Sqrt(v.Sum(x => x * x));
    static double Distance(double[] a, double[] b) => Math.Sqrt(a.Select((x, i) => (x - b[i]) * (x - b[i])).Sum());
}
