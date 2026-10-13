using YouWillDate.Sim.Adult;
using YouWillDate.Sim.Life;
using YouWillDate.Sim.Relations;

namespace YouWillDate.Sim.Data;

// Loading and checking aura.yaml and relationships.yaml (attraction spec 7.2 and section 8, spec section 9).
public sealed partial class GameData
{
    /// <summary>Past-partner records, built at the end of the life simulation. Null without relationships.yaml.</summary>
    public PastPartnerTables? PastPartners { get; }
    /// <summary>Relationship patterns, city linking and the starting state. Null without relationships.yaml and adult.yaml.</summary>
    public RelationTables? Relations { get; }
    /// <summary>The profile vector, aura and taste. Null without aura.yaml and adult.yaml.</summary>
    public AuraTables? Aura { get; }

    Lazy<ProfileNorms>? _profileNorms;

    /// <summary>
    /// Mean and SD of each profile vector entry, measured on a calibration batch the
    /// first time a city is generated (attraction spec 7.2: x is z-scored).
    /// </summary>
    public ProfileNorms ProfileNorms => _profileNorms?.Value ?? throw new InvalidOperationException("no aura.yaml loaded");

    NoisyFormula CompileFormula(FormulaDto f, string where)
    {
        if (f.Sigma < 0) throw new DataException($"{where}: sigma can't be negative");
        var pseudo = new GroupDto { Name = "relationships" };
        return new NoisyFormula(new Formula
        {
            Base = f.Base, Terms = CompileTerms(f.Weights, where), Mods = CompileAddMods(f.Mods, where, pseudo),
        }, f.Sigma);
    }

    /// <summary>Compiled right after life.yaml, so its formulas can only read the starting point and the life simulation.</summary>
    PastPartnerTables CompilePastPartners(RelationshipsFile f)
    {
        const string where = "relationships.yaml: past_partners";
        var p = f.PastPartners;
        if (p.AgeGapSd < 0 || p.StatusSd < 0) throw new DataException($"{where}: SDs can't be negative");
        if (p.StatusMatch is < -1 or > 1) throw new DataException($"{where}: status_match must be between -1 and 1");
        return new PastPartnerTables
        {
            Source = p,
            OwnStatus = CompileFormula(p.OwnStatus, where + " own_status"),
            Availability = CompileFormula(p.Availability, where + " availability"),
        };
    }

    RelationTables CompileRelations(RelationshipsFile f, PastPartnerTables pastPartners)
    {
        const string file = "relationships.yaml";
        if (f.PatternSize < 1) throw new DataException($"{file}: pattern_size must be at least 1");
        var readouts = f.Readouts.Select(r =>
        {
            if (!LifeId.IsMatch(r.Id)) throw new DataException($"{file}: readout '{r.Id}' must use a-z, 0-9 and _");
            if (r.Scale <= 0) throw new DataException($"{file}: readout '{r.Id}' needs a scale above 0");
            return new Readout(r.Id, r.Scale);
        }).ToArray();
        if (readouts.Select(r => r.Id).Distinct().Count() != readouts.Length) throw new DataException($"{file}: duplicate readouts");

        var c = f.City;
        if (c.YearsTolerance < 0 || c.MaxCost < 0 || c.OverflowFactor < 0)
            throw new DataException($"{file}: city years_tolerance, max_cost and overflow_factor can't be negative");
        if (c.ExLinkChance is < 0 or > 1 || c.FriendLinkChance is < 0 or > 1 || c.AlongsideLinkChance is < 0 or > 1)
            throw new DataException($"{file}: city link chances must be between 0 and 1");

        var s = f.Start;
        string sw = $"{file}: start";
        if (!_schema.TryGetValue("adult.monogamy_view", out var views) || views is null)
            throw new DataException($"{file}: needs adult.monogamy_view in adult.yaml");
        foreach (var (name, map) in new[] { ("start exclusivity", s.Exclusivity), ("tolerance", f.Tolerance) })
        {
            foreach (var (view, level) in map)
            {
                if (!views.Contains(view)) throw new DataException($"{file}: {name}: '{view}' is not an adult.monogamy_view value");
                if (level is < 0 or > 1) throw new DataException($"{file}: {name} for '{view}' must be between 0 and 1");
            }
            foreach (var view in views)
                if (!map.ContainsKey(view)) throw new DataException($"{file}: {name} needs a value for '{view}'");
        }
        var goals = Adult?.Items.Where(i => i.Type == AdultType.Goals).SelectMany(i => i.Goals.Select(g => g.Id)).ToHashSet() ?? [];
        foreach (var g in s.GoalPressureGoals)
            if (!goals.Contains(g)) throw new DataException($"{sw} goal_pressure_goals: '{g}' is not a goal in adult.yaml");
        var reasons = EndReasons;
        foreach (var key in s.Harm.Keys.Concat(s.Blame.Keys))
            if (!reasons.Contains(key))
                throw new DataException($"{sw}: '{key}' is not a way a relationship ends; use one of: {string.Join(", ", reasons)}");
        if (s.InvestmentYears <= 0 || s.EstimateYears <= 0) throw new DataException($"{sw}: investment_years and estimate_years must be above 0");

        var k = s.Knowledge;
        var fields = k.Fields.Select(x =>
        {
            if (!_schema.ContainsKey(SchemaKey(x.Key))) throw new DataException($"{sw} knowledge: '{x.Key}' is not a known fact");
            if (x.Kind is not ("visible" or "disclosed")) throw new DataException($"{sw} knowledge: '{x.Key}' kind must be visible or disclosed");
            if (x.Weight < 0) throw new DataException($"{sw} knowledge: '{x.Key}' weight can't be negative");
            return new KnownField(x.Key, x.Kind == "visible", x.Weight);
        }).ToArray();
        foreach (var list in k.Lists)
            if (Adult?.Items.Any(i => i.Id == list && i.Type == AdultType.List) != true)
                throw new DataException($"{sw} knowledge: '{list}' is not a list item in adult.yaml");

        return new RelationTables
        {
            PatternSize = f.PatternSize, Readouts = readouts, Tendencies = f.Tendencies, Tolerance = f.Tolerance,
            PastPartners = pastPartners, City = c,
            Start = new StartTables
            {
                Source = s,
                Satisfaction = CompileFormula(s.Satisfaction, sw + " satisfaction"),
                Trust = CompileFormula(s.Trust, sw + " trust"),
                Standing = CompileFormula(s.Standing, sw + " standing"),
                Mood = CompileFormula(s.Mood, sw + " mood"),
                Openness = CompileFormula(k.Openness, sw + " knowledge openness"),
                Fields = fields, Lists = k.Lists.ToArray(),
            },
        };
    }

    AuraTables CompileAura(AuraFile f)
    {
        const string file = "aura.yaml";
        if (f.Harmonics < 1) throw new DataException($"{file}: harmonics must be at least 1");
        if (f.AuraNoise < 0 || f.TasteNoise < 0) throw new DataException($"{file}: noise can't be negative");
        if (f.CalibrationSize < 50) throw new DataException($"{file}: calibration_size must be at least 50");
        if (f.ProfileVector.Count == 0) throw new DataException($"{file}: profile_vector is empty");
        var entries = f.ProfileVector.Select(raw =>
        {
            bool logit = raw.StartsWith("logit(") && raw.EndsWith(')');
            string key = logit ? raw[6..^1] : raw;
            if (!_schema.TryGetValue(key, out var allowed))
                throw new DataException($"{file}: profile_vector entry '{raw}' is not a known fact");
            if (allowed is not null)
                throw new DataException($"{file}: profile_vector entry '{raw}' is categorical; the vector needs numbers");
            return new ProfileEntry(key, logit);
        }).ToArray();
        if (entries.Select(e => e.Label).Distinct().Count() != entries.Length) throw new DataException($"{file}: profile_vector has duplicates");
        _profileNorms = new(() => global::YouWillDate.Sim.Relations.ProfileNorms.Calibrate(this));
        return new AuraTables
        {
            Harmonics = f.Harmonics, AuraNoise = f.AuraNoise, TasteNoise = f.TasteNoise,
            CalibrationSize = f.CalibrationSize, ProfileVector = entries,
        };
    }
}
