namespace YouWillDate.Sim.Data;

// Shapes of the YAML data files. Property names map to snake_case keys.

public sealed class Config
{
    /// <summary>Location id of the city the game takes place in. Every pawn lives here now.</summary>
    public string CurrentCity { get; set; } = "";
    public int PresentYear { get; set; }
    public int AgeMin { get; set; }
    public int AgeMax { get; set; }
    public double MaleBirthRate { get; set; }
    public TemperamentConfig Temperament { get; set; } = new();
}

public sealed class TemperamentConfig
{
    /// <summary>Trait ids. Each becomes the numeric fact "pawn.&lt;id&gt;" (0–1 percentile).</summary>
    public List<string> Traits { get; set; } = new();
    public List<Correlation> Correlations { get; set; } = new();
}

public sealed class Correlation
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public double R { get; set; }
}

public sealed class LocationsFile
{
    public List<Location> Locations { get; set; } = new();
}

public sealed class Location
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>
    /// Relative population. Used for birth locations when the current city has no origins table.
    /// </summary>
    public double Population { get; set; }
    public string Urbanicity { get; set; } = "";
    /// <summary>
    /// When this is the current city: where the people living here were born,
    /// as relative weights by location id. Missing means "weight by population".
    /// </summary>
    public Dictionary<string, double>? Origins { get; set; }
    /// <summary>Conditions by birth year. Each period applies from its year until the next one.</summary>
    public List<LocationPeriod> Periods { get; set; } = new();

    public LocationPeriod PeriodFor(int year) =>
        Periods.LastOrDefault(p => p.From <= year)
        ?? throw new DataException($"locations.yaml: '{Id}' has no period covering {year}");
}

public sealed class LocationPeriod
{
    public int From { get; set; }
    public string LocalPolitics { get; set; } = "";
    public string Diversity { get; set; } = "";
    public string NormsSex { get; set; } = "";
    public string NormsGender { get; set; } = "";
    public string NormsFamily { get; set; } = "";
    /// <summary>Overrides the national economic era when set.</summary>
    public string? EconomicEra { get; set; }
    /// <summary>Ethnic group shares. Used as base weights for parent ethnicity.</summary>
    public Dictionary<string, double> Groups { get; set; } = new();
    /// <summary>Religious shares. Used as base weights for parent religion.</summary>
    public Dictionary<string, double> Religions { get; set; } = new();
    /// <summary>Optional local overrides of eras.yaml base rates while the pawn lives here.</summary>
    public Dictionary<string, double>? Rates { get; set; }

    public Dictionary<string, double>? Shares(string name) => name switch
    {
        "groups" => Groups,
        "religions" => Religions,
        _ => null,
    };
}

public sealed class ErasFile
{
    public List<EraEntry> EconomicEras { get; set; } = new();
    /// <summary>Base rates for life events by calendar year. Each entry applies until the next one that sets the same rate.</summary>
    public List<BaseRateEntry> BaseRates { get; set; } = new();
}

public sealed class BaseRateEntry
{
    public int From { get; set; }
    public Dictionary<string, double> Rates { get; set; } = new();
}

public sealed class EraEntry
{
    public int From { get; set; }
    public string Era { get; set; } = "";
}

public sealed class FamilyFile
{
    public List<GroupDto> Groups { get; set; } = new();
}

public sealed class GroupDto
{
    public string Name { get; set; } = "";
    /// <summary>Free-text designer note. Kept when the trait editor saves (comments are not).</summary>
    public string? Note { get; set; }
    /// <summary>Roll each item once for the mother and once for the father.</summary>
    public bool PerParent { get; set; }
    public List<ItemDto> Items { get; set; } = new();
}

public sealed class ItemDto
{
    public string Id { get; set; } = "";
    public string? Note { get; set; }
    public List<string> Values { get; set; } = new();
    /// <summary>Base weight per value. Values left out weigh 1.</summary>
    public Dictionary<string, double>? Weights { get; set; }
    /// <summary>Take base weights from a location share map instead ("groups", "religions").</summary>
    public string? BaseFrom { get; set; }
    /// <summary>Roll only when all conditions hold; otherwise take <see cref="Default"/>.</summary>
    public Dictionary<string, object>? OnlyIf { get; set; }
    public string? Default { get; set; }
    /// <summary>Per-parent items: chance the father copies the mother's value.</summary>
    public double Assort { get; set; }
    public List<ModDto> Mods { get; set; } = new();
}

public sealed class ModDto
{
    public string? Note { get; set; }
    public Dictionary<string, object> When { get; set; } = new();
    public Dictionary<string, double> Mul { get; set; } = new();
}
