using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Sim.Relations;

/// <summary>
/// Mean and SD of each profile vector entry, measured on a calibration batch
/// (attraction spec 7.2: every entry of x is z-scored). The batch's vectors are
/// kept so each world can measure its own mean aura amplitude.
/// </summary>
public sealed class ProfileNorms
{
    public required double[] Mean { get; init; }
    public required double[] Sd { get; init; }
    /// <summary>Calibration pawns: seed and z-scored vector.</summary>
    public required IReadOnlyList<(ulong Seed, double[] X)> Samples { get; init; }

    internal static ProfileNorms Calibrate(GameData data)
    {
        var aura = data.Aura!;
        var gen = new PawnGenerator(data);
        var entries = aura.ProfileVector;
        var raw = Enumerable.Range(0, aura.CalibrationSize).Select(i =>
        {
            var pawn = gen.Generate((ulong)(9_500_000 + i));
            return (pawn.Seed, X: entries.Select(e => e.Read(pawn.Facts)).ToArray());
        }).ToList();

        int d = entries.Length;
        var mean = new double[d];
        var sd = new double[d];
        for (int j = 0; j < d; j++)
        {
            mean[j] = raw.Average(r => r.X[j]);
            double m = mean[j];
            sd[j] = Math.Max(Math.Sqrt(raw.Sum(r => (r.X[j] - m) * (r.X[j] - m)) / raw.Count), 1e-6);
        }
        var norms = new ProfileNorms { Mean = mean, Sd = sd, Samples = [] };
        return new ProfileNorms { Mean = mean, Sd = sd, Samples = raw.Select(r => (r.Seed, norms.Normalize(r.X))).ToList() };
    }

    double[] Normalize(double[] raw) => raw.Select((v, j) => Math.Clamp((v - Mean[j]) / Sd[j], -4, 4)).ToArray();

    /// <summary>The pawn's profile vector x, z-scored.</summary>
    public double[] VectorOf(Facts facts, ProfileEntry[] entries) => Normalize(entries.Select(e => e.Read(facts)).ToArray());
}

/// <summary>A pawn's aura: K harmonics, each an amplitude (population mean 1) and a phase in [−π, π].</summary>
public sealed record Aura(double[] Amp, double[] Phase);

/// <summary>A pawn's taste: per harmonic, the phase offset it wants and how much that harmonic matters (weights sum to 1).</summary>
public sealed record Taste(double[] Offset, double[] Weight);

/// <summary>
/// The fixed matrices and vectors the world seed draws at world start
/// (attraction spec 7.2 and 8.3): M, T and W for the aura and taste, Q for the
/// direction of desire, and one readout vector per behavior. Every pawn in a
/// world uses the same ones, so nobody authors what a harmonic or a pattern means.
/// </summary>
public sealed class World
{
    public ulong Seed { get; }
    /// <summary>D: profile vector length.</summary>
    public int D { get; }
    /// <summary>K: aura harmonics.</summary>
    public int K { get; }
    /// <summary>K_r: values in a relationship pattern.</summary>
    public int PatternSize { get; }
    public double[,] M { get; }
    public double[,] T { get; }
    public double[,] W { get; }
    public double[,] Q { get; }
    public IReadOnlyDictionary<string, double[]> Readouts { get; }
    /// <summary>ā: mean raw amplitude on the calibration batch, so amp_k has population mean 1.</summary>
    public double MeanAmplitude { get; }

    readonly GameData _data;
    readonly AuraTables _aura;

    public World(GameData data, ulong seed)
    {
        _data = data;
        _aura = data.Aura ?? throw new InvalidOperationException("a world needs aura.yaml");
        var rel = data.Relations ?? throw new InvalidOperationException("a world needs relationships.yaml");
        Seed = seed;
        D = _aura.ProfileVector.Length;
        K = _aura.Harmonics;
        PatternSize = rel.PatternSize;

        double sd = 1 / Math.Sqrt(D);
        M = Fill("M", 2 * K, D, sd);
        T = Fill("T", K, D, sd);
        W = Fill("W", K, D, sd);
        Q = Fill("Q", PatternSize, D, sd);
        Readouts = rel.Readouts.ToDictionary(r => r.Id, r =>
        {
            var rng = Rng.For(seed, "world", "readout", r.Id);
            return Enumerable.Range(0, PatternSize).Select(_ => rng.NextNormal()).ToArray();
        });

        var samples = data.ProfileNorms.Samples;
        MeanAmplitude = samples.Count == 0 ? 1
            : samples.Average(s => RawAmplitudes(s.X, s.Seed).Average());
    }

    double[,] Fill(string name, int rows, int cols, double sd)
    {
        var rng = Rng.For(Seed, "world", name);
        var m = new double[rows, cols];
        for (int i = 0; i < rows; i++)
        for (int j = 0; j < cols; j++)
            m[i, j] = sd * rng.NextNormal();
        return m;
    }

    static double[] Multiply(double[,] m, double[] x)
    {
        var y = new double[m.GetLength(0)];
        for (int i = 0; i < y.Length; i++)
        for (int j = 0; j < x.Length; j++)
            y[i] += m[i, j] * x[j];
        return y;
    }

    double[] Projection(double[] x, ulong pawnSeed)
    {
        var z = Multiply(M, x);
        var rng = Rng.For(pawnSeed, "attraction", "aura");
        for (int i = 0; i < z.Length; i++) z[i] += _aura.AuraNoise * rng.NextNormal();
        return z;
    }

    double[] RawAmplitudes(double[] x, ulong pawnSeed)
    {
        var z = Projection(x, pawnSeed);
        return Enumerable.Range(0, K).Select(k => Math.Sqrt(z[2 * k] * z[2 * k] + z[2 * k + 1] * z[2 * k + 1])).ToArray();
    }

    /// <summary>x: the pawn's z-scored profile vector.</summary>
    public double[] ProfileVector(Pawn pawn) => _data.ProfileNorms.VectorOf(pawn.Facts, _aura.ProfileVector);

    /// <summary>z = Mx + ε; amp_k = |(z_2k, z_2k+1)| / ā; phase_k = atan2(z_2k+1, z_2k).</summary>
    public Aura AuraOf(double[] x, ulong pawnSeed)
    {
        var z = Projection(x, pawnSeed);
        var amp = new double[K];
        var phase = new double[K];
        for (int k = 0; k < K; k++)
        {
            double re = z[2 * k], im = z[2 * k + 1];
            amp[k] = Math.Sqrt(re * re + im * im) / MeanAmplitude;
            phase[k] = Math.Atan2(im, re);
        }
        return new Aura(amp, phase);
    }

    /// <summary>offset_k = π σ((Tx)_k + η_k); weight = softmax(0.5 (Wx)).</summary>
    public Taste TasteOf(double[] x, ulong pawnSeed)
    {
        var rng = Rng.For(pawnSeed, "attraction", "taste");
        var t = Multiply(T, x);
        var offset = t.Select(v => Math.PI * MathUtil.Sigmoid(v + _aura.TasteNoise * rng.NextNormal())).ToArray();
        var w = Multiply(W, x).Select(v => Math.Exp(0.5 * v)).ToArray();
        double total = w.Sum();
        return new Taste(offset, w.Select(v => v / total).ToArray());
    }

    /// <summary>u = Qx / |Qx|: the form of involvement the pawn's psyche wants (8.3).</summary>
    public double[] DesireDirection(double[] x)
    {
        var q = Multiply(Q, x);
        double norm = Math.Sqrt(q.Sum(v => v * v));
        return norm < 1e-12 ? q.Select(_ => 1 / Math.Sqrt(q.Length)).ToArray() : q.Select(v => v / norm).ToArray();
    }

    /// <summary>A readout of a pattern: scale × σ(r · P) (8.3).</summary>
    public double Readout(string id, double[] pattern)
    {
        var r = Readouts[id];
        double dot = 0;
        for (int i = 0; i < r.Length; i++) dot += r[i] * pattern[i];
        return _data.Relations!.Readouts.First(x => x.Id == id).Scale * MathUtil.Sigmoid(dot);
    }
}
