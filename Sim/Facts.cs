namespace YouWillDate.Sim;

/// <summary>
/// Every value generated for a pawn, keyed by dotted name
/// ("context.urbanicity", "mother.education", "household.wealth").
/// Categorical values are ids; numeric values are traits and years.
/// Iteration follows insertion order, which matches generation order.
/// </summary>
public sealed class Facts
{
    readonly Dictionary<string, string> _cat = new();
    readonly Dictionary<string, double> _num = new();

    public IReadOnlyDictionary<string, string> Categorical => _cat;
    public IReadOnlyDictionary<string, double> Numeric => _num;

    public void Set(string key, string value) => _cat[key] = value;
    public void Set(string key, double value) => _num[key] = value;

    public bool Contains(string key) => _cat.ContainsKey(key) || _num.ContainsKey(key);
    public int Count => _cat.Count + _num.Count;

    public void Remove(string key)
    {
        _cat.Remove(key);
        _num.Remove(key);
    }

    public void Clear()
    {
        _cat.Clear();
        _num.Clear();
    }

    public string? Cat(string key) => _cat.GetValueOrDefault(key);
    public double? Num(string key) => _num.TryGetValue(key, out var v) ? v : null;

    public string this[string key] => _cat[key];
}
