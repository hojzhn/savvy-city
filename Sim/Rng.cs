namespace YouWillDate.Sim;

/// <summary>
/// Deterministic random source (SplitMix64). We avoid System.Random so the
/// same seed gives the same pawn on every .NET version and platform.
/// </summary>
public sealed class Rng
{
    ulong _state;

    public Rng(ulong seed) => _state = seed;

    /// <summary>
    /// Rng for one roll, seeded by hash(seed, parts...). Each roll owns its
    /// sub-seed, so adding or removing an item leaves all other rolls unchanged.
    /// </summary>
    public static Rng For(ulong seed, params string[] parts) => new(SubSeed(seed, parts));

    public static ulong SubSeed(ulong seed, params string[] parts)
    {
        ulong h = Mix(seed);
        foreach (var part in parts)
        {
            foreach (char c in part)
                h = (h ^ c) * 0x100000001B3UL;
            h = Mix(h ^ 0xFF);
        }
        return h;
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform integer in [min, max).</summary>
    public int NextInt(int min, int max) => min + (int)(NextUInt64() % (ulong)(max - min));

    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Standard normal draw (Box-Muller).</summary>
    public double NextNormal()
    {
        double u1 = 1.0 - NextDouble();
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Index drawn in proportion to weights. Returns -1 if all weights are zero.</summary>
    public int PickWeighted(ReadOnlySpan<double> weights)
    {
        double total = 0;
        foreach (var w in weights) total += w;
        if (total <= 0) return -1;

        double r = NextDouble() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            r -= weights[i];
            if (r < 0) return i;
        }
        // Floating-point leftovers land on the last non-zero weight.
        for (int i = weights.Length - 1; i >= 0; i--)
            if (weights[i] > 0) return i;
        return -1;
    }

    static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
