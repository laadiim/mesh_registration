using MeshRegistration.Algorithms.Curvature;

namespace MeshRegistration.IO.Export;

/// <summary>An 8-bit RGB colour.</summary>
public readonly record struct ColorRgb(byte R, byte G, byte B)
{
    /// <summary>The components as floats in <c>[0, 1]</c>, the form OBJ vertex colours use.</summary>
    public (double R, double G, double B) ToUnit() => (R / 255.0, G / 255.0, B / 255.0);
}

/// <summary>Which scalar to paint a mesh or a line with.</summary>
public enum ColorBy
{
    /// <summary>Degeneracy classification. The view that shows where curvature is meaningless.</summary>
    Flags,

    /// <summary>Dimensionless anisotropy: how far the point is from umbilic.</summary>
    Anisotropy,

    /// <summary>Smaller principal curvature.</summary>
    KMin,

    /// <summary>Larger principal curvature.</summary>
    KMax,

    /// <summary>Mean curvature.</summary>
    Mean,

    /// <summary>Gaussian curvature.</summary>
    Gaussian,

    /// <summary>Fit confidence.</summary>
    Confidence,

    /// <summary>Signed geodesic curvature of the line. Lines only.</summary>
    GeodesicCurvature,

    /// <summary>A distinct colour per line. Lines only.</summary>
    Line,

    /// <summary>
    /// Which principal direction each sample followed. Lines only.
    /// </summary>
    /// <remarks>
    /// Answers "is this line on the maximum or the minimum field" by measurement rather than by
    /// assumption — a line seeded on one field can continue onto the other wherever the two
    /// curvatures cross and the labels exchange.
    /// </remarks>
    Followed,
}

/// <summary>
/// Colour maps for the MeshLab exports.
/// </summary>
public static class ColorRamp
{
    /// <summary>Colour code for the degeneracy classification.</summary>
    /// <remarks>
    /// This is the view that makes the flat- and spherical-surface handling visible: opening the
    /// exported mesh shows directly which regions have no principal direction, and therefore why
    /// no lines were seeded there.
    /// </remarks>
    public static ColorRgb ForFlags(CurvatureFlags flags)
    {
        if ((flags & CurvatureFlags.Unusable) != 0)
        {
            return new ColorRgb(200, 40, 40); // red: no shape operator at all
        }

        if ((flags & CurvatureFlags.Planar) != 0)
        {
            return new ColorRgb(150, 150, 150); // grey: flat, nothing to measure
        }

        if ((flags & CurvatureFlags.Umbilic) != 0)
        {
            return new ColorRgb(60, 110, 220); // blue: spherical, direction undefined
        }

        if ((flags & CurvatureFlags.Boundary) != 0)
        {
            return new ColorRgb(230, 150, 40); // orange: one-sided neighbourhood
        }

        return new ColorRgb(70, 180, 90); // green: usable
    }

    /// <summary>
    /// A blue-white-red ramp for signed values, with <paramref name="t"/> in <c>[-1, 1]</c>.
    /// </summary>
    public static ColorRgb Diverging(double t)
    {
        t = Math.Clamp(t, -1, 1);

        if (t >= 0)
        {
            return Lerp(new ColorRgb(245, 245, 245), new ColorRgb(200, 30, 30), t);
        }

        return Lerp(new ColorRgb(245, 245, 245), new ColorRgb(30, 60, 200), -t);
    }

    /// <summary>A dark-to-bright ramp for unsigned values, with <paramref name="t"/> in <c>[0, 1]</c>.</summary>
    public static ColorRgb Sequential(double t)
    {
        t = Math.Clamp(t, 0, 1);

        // Three-stop ramp: deep blue, teal, yellow.
        return t < 0.5
            ? Lerp(new ColorRgb(30, 40, 110), new ColorRgb(30, 160, 150), t * 2)
            : Lerp(new ColorRgb(30, 160, 150), new ColorRgb(250, 220, 60), (t - 0.5) * 2);
    }

    /// <summary>Colour code for which principal direction a sample followed.</summary>
    public static ColorRgb ForFollowedDirection(Algorithms.Tracing.FollowedDirection followed) => followed switch
    {
        Algorithms.Tracing.FollowedDirection.Max => new ColorRgb(200, 60, 60),   // red: maximum
        Algorithms.Tracing.FollowedDirection.Min => new ColorRgb(50, 100, 210),  // blue: minimum
        _ => new ColorRgb(160, 160, 160),                                        // grey: transported
    };

    /// <summary>
    /// A distinct, repeatable colour per index, for telling one line from another.
    /// </summary>
    /// <remarks>
    /// Hues advance by the golden angle, which keeps consecutive indices far apart on the colour
    /// wheel and avoids the visible banding of an evenly spaced palette.
    /// </remarks>
    public static ColorRgb Categorical(int index)
    {
        const double goldenAngleDegrees = 137.507764;
        double hue = index * goldenAngleDegrees % 360.0;

        // Alternate saturation and value slightly so that similar hues are still separable.
        double saturation = 0.62 + (0.18 * (index % 3) / 2.0);
        double value = 0.95 - (0.18 * (index % 2));

        return FromHsv(hue, saturation, value);
    }

    private static ColorRgb Lerp(ColorRgb a, ColorRgb b, double t) => new(
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    private static ColorRgb FromHsv(double hue, double saturation, double value)
    {
        double c = value * saturation;
        double h = hue / 60.0;
        double x = c * (1 - Math.Abs((h % 2) - 1));
        double m = value - c;

        (double r, double g, double b) = (int)h switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return new ColorRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    /// <summary>
    /// Finds a robust value range for a scalar field, ignoring the extreme tails.
    /// </summary>
    /// <remarks>
    /// Curvature fields routinely contain a handful of huge outliers at sliver triangles. Scaling
    /// a colour ramp to the true extremes would compress everything else into a single shade, so
    /// the range is taken between the given percentiles instead.
    /// </remarks>
    public static (double Low, double High) RobustRange(
        IEnumerable<double> values,
        double lowPercentile = 0.02,
        double highPercentile = 0.98)
    {
        double[] sorted = [.. values.Where(double.IsFinite).Order()];

        if (sorted.Length == 0)
        {
            return (0, 1);
        }

        double low = sorted[(int)Math.Clamp(sorted.Length * lowPercentile, 0, sorted.Length - 1)];
        double high = sorted[(int)Math.Clamp(sorted.Length * highPercentile, 0, sorted.Length - 1)];

        if (high <= low)
        {
            high = low + 1e-12;
        }

        return (low, high);
    }
}
