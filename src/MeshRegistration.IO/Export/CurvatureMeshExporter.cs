using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.IO.Export;

/// <summary>
/// Writes the input mesh with a per-vertex colour encoding a curvature quantity.
/// </summary>
/// <remarks>
/// With <see cref="ColorBy.Flags"/> this is the direct visual check on the degeneracy handling:
/// opening the result shows which regions of the model are flat, which are spherical, and which
/// carry a usable principal direction. Regions painted grey or blue are exactly the ones no line
/// is seeded in and no direction is read from — the failure that used to surface only as NaNs
/// deep inside the tracer becomes something you can look at.
/// </remarks>
public static class CurvatureMeshExporter
{
    public static void Write(
        string path,
        TriangleMesh mesh,
        ShapeOperatorField curvature,
        ColorBy colorBy = ColorBy.Flags)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(curvature);

        CurvatureSample[] samples = new CurvatureSample[mesh.VertexCount];
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            samples[v] = curvature.AtVertex(v);
        }

        Func<CurvatureSample, ColorRgb> paint = BuildPainter(samples, curvature, colorBy);

        using FileStream stream = File.Create(path);
        using ObjTextWriter writer = new(stream);

        writer.Comment($"input mesh coloured by {colorBy}");
        if (colorBy == ColorBy.Flags)
        {
            writer.Comment("grey = planar, blue = umbilic (spherical), orange = boundary,");
            writer.Comment("red = no usable fit, green = usable principal direction");
        }

        writer.BlankLine();

        for (int v = 0; v < mesh.VertexCount; v++)
        {
            writer.Vertex(mesh.Position(v), paint(samples[v]));
        }

        writer.BlankLine();

        for (int f = 0; f < mesh.TriangleCount; f++)
        {
            Triangle t = mesh.Face(f);
            writer.Triangle(t.V0, t.V1, t.V2);
        }
    }

    private static Func<CurvatureSample, ColorRgb> BuildPainter(
        CurvatureSample[] samples,
        ShapeOperatorField curvature,
        ColorBy colorBy)
    {
        if (colorBy == ColorBy.Flags)
        {
            return static sample => ColorRamp.ForFlags(sample.Flags);
        }

        double radius = curvature.NeighbourhoodRadius;
        Func<CurvatureSample, double> scalar = colorBy switch
        {
            ColorBy.KMin => static s => s.KMin,
            ColorBy.KMax => static s => s.KMax,
            ColorBy.Mean => static s => s.MeanCurvature,
            ColorBy.Gaussian => static s => s.GaussianCurvature,
            ColorBy.Confidence => static s => s.Confidence,

            // Reported dimensionless, so the same colours mean the same thing on any model.
            _ => s => s.CurvatureDeviation * radius,
        };

        // Unusable samples carry no value worth mapping, so they are excluded from the range and
        // painted a fixed colour.
        (double low, double high) = ColorRamp.RobustRange(
            samples.Where(s => !s.IsUnusable).Select(scalar));

        bool signed = colorBy is ColorBy.KMin or ColorBy.KMax or ColorBy.Mean or ColorBy.Gaussian;
        double extent = Math.Max(Math.Abs(low), Math.Abs(high));

        return sample =>
        {
            if (sample.IsUnusable)
            {
                return new ColorRgb(200, 40, 40);
            }

            double value = scalar(sample);

            return signed
                ? ColorRamp.Diverging(extent > 0 ? value / extent : 0)
                : ColorRamp.Sequential((value - low) / (high - low));
        };
    }
}
