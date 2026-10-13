namespace YouWillDate.Sim.Data;

/// <summary>Allowed values for the context step (spec section 2).</summary>
public static class ContextSchema
{
    public static readonly IReadOnlyDictionary<string, string[]> Values = new Dictionary<string, string[]>
    {
        ["urbanicity"] = ["urban_core", "inner_suburb", "outer_suburb", "small_town", "rural"],
        ["local_politics"] = ["strongly_left", "left_leaning", "mixed", "right_leaning", "strongly_right"],
        ["diversity"] = ["homogeneous", "mostly_homogeneous", "mixed", "highly_diverse"],
        ["economic_era"] = ["boom", "steady_growth", "stagnation", "recession", "crisis"],
        ["norms_sex"] = ["restrictive", "conservative", "moderate", "permissive"],
        ["norms_gender"] = ["traditional", "mixed", "egalitarian"],
        ["norms_family"] = ["extended", "nuclear", "individualist"],
    };

    public static readonly string[] Sexes = ["female", "male"];
}
