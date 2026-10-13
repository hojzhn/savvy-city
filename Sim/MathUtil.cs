namespace YouWillDate.Sim;

static class MathUtil
{
    public static double Sigmoid(double x) => 1 / (1 + Math.Exp(-x));

    public static double Logit(double p)
    {
        p = Math.Clamp(p, 1e-6, 1 - 1e-6);
        return Math.Log(p / (1 - p));
    }

    /// <summary>Standard normal CDF (Abramowitz–Stegun 7.1.26, error under 1.5e-7).</summary>
    public static double NormalCdf(double z)
    {
        double x = Math.Abs(z) / Math.Sqrt(2);
        double t = 1 / (1 + 0.3275911 * x);
        double erf = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return z >= 0 ? 0.5 * (1 + erf) : 0.5 * (1 - erf);
    }

    /// <summary>Inverse standard normal CDF (Acklam's approximation, relative error under 1.2e-9).</summary>
    public static double Probit(double p)
    {
        p = Math.Clamp(p, 1e-9, 1 - 1e-9);
        double[] a = [-39.69683028665376, 220.9460984245205, -275.9285104469687, 138.3577518672690, -30.66479806614716, 2.506628277459239];
        double[] b = [-54.47609879822406, 161.5858368580409, -155.6989798598866, 66.80131188771972, -13.28068155288572];
        double[] c = [-0.007784894002430293, -0.3223964580411365, -2.400758277161838, -2.549732539343734, 4.374664141464968, 2.938163982698783];
        double[] d = [0.007784695709041462, 0.3224671290700398, 2.445134137142996, 3.754408661907416];
        const double low = 0.02425;
        if (p < low)
        {
            double q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p > 1 - low)
        {
            double q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        double r = p - 0.5, s = r * r;
        return (((((a[0] * s + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r
             / (((((b[0] * s + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1);
    }

    /// <summary>Gamma(shape, 1) draw (Marsaglia–Tsang; shapes below 1 use the boost u^(1/shape)).</summary>
    public static double Gamma(Rng rng, double shape)
    {
        if (shape <= 0) return 0;
        if (shape < 1) return Gamma(rng, shape + 1) * Math.Pow(Math.Max(rng.NextDouble(), 1e-300), 1 / shape);
        double d = shape - 1.0 / 3, c = 1 / Math.Sqrt(9 * d);
        while (true)
        {
            double x = rng.NextNormal(), v = 1 + c * x;
            if (v <= 0) continue;
            v = v * v * v;
            double u = rng.NextDouble();
            if (u < 1 - 0.0331 * x * x * x * x || Math.Log(Math.Max(u, 1e-300)) < 0.5 * x * x + d * (1 - v + Math.Log(v)))
                return d * v;
        }
    }

    /// <summary>Poisson draw (Knuth for small means, normal approximation above 30).</summary>
    public static int Poisson(Rng rng, double mean)
    {
        if (mean <= 0) return 0;
        if (mean > 30) return Math.Max(0, (int)Math.Round(mean + Math.Sqrt(mean) * rng.NextNormal()));
        double limit = Math.Exp(-mean), product = rng.NextDouble();
        int k = 0;
        while (product > limit)
        {
            k++;
            product *= rng.NextDouble();
        }
        return k;
    }
}
