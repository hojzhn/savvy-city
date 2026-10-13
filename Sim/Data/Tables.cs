namespace YouWillDate.Sim.Data;

/// <summary>A group of table items rolled together, such as parents or household.</summary>
public sealed class TableGroup(string name, bool perParent, IReadOnlyList<TableItem> items)
{
    public string Name { get; } = name;
    public bool PerParent { get; } = perParent;
    public IReadOnlyList<TableItem> Items { get; } = items;
}

/// <summary>
/// One categorical item. Weights start from the base (or a location share map)
/// and each modifier whose conditions all hold multiplies some of them.
/// </summary>
public sealed class TableItem
{
    public required string Id { get; init; }
    public required string[] Values { get; init; }
    public required double[] BaseWeights { get; init; }
    public string? BaseFrom { get; init; }
    public required Condition[] OnlyIf { get; init; }
    public string? Default { get; init; }
    public double Assort { get; init; }
    public required Modifier[] Mods { get; init; }
}

public sealed class Modifier(int number, Condition[] when, (int Index, double Factor)[] mul)
{
    /// <summary>1-based position in the item's mods list, for debug output.</summary>
    public int Number { get; } = number;
    public Condition[] When { get; } = when;
    public (int Index, double Factor)[] Mul { get; } = mul;
}
