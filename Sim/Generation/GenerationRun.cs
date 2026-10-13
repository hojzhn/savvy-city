namespace YouWillDate.Sim.Generation;

/// <summary>State for one Generate call: facts so far, overrides, and the trace.</summary>
sealed class GenerationRun(ulong seed, Facts? overrides, List<RollTrace>? trace)
{
    public ulong Seed { get; } = seed;
    public Facts Facts { get; } = new();
    public List<RollTrace>? Trace { get; } = trace;
    public bool Tracing => Trace is not null;

    public string? Override(string key) => overrides?.Cat(key);

    public void Add(RollTrace t) => Trace?.Add(t);

    public string Cat(string key, Func<string> compute, RollSource source, string? note = null)
    {
        string? forced = overrides?.Cat(key);
        string value = forced ?? compute();
        Facts.Set(key, value);
        Add(new RollTrace { Key = key, Source = forced is null ? source : RollSource.Override, Value = value, Note = note });
        return value;
    }

    public double Num(string key, Func<double> compute, string? note = null)
    {
        double? forced = overrides?.Num(key);
        double value = forced ?? compute();
        Facts.Set(key, value);
        Add(new RollTrace { Key = key, Source = forced is null ? RollSource.Drawn : RollSource.Override, Number = value, Note = note });
        return value;
    }

    public void Derived(string key, string value, string note)
    {
        Facts.Set(key, value);
        Add(new RollTrace { Key = key, Source = RollSource.Derived, Value = value, Note = note });
    }
}
