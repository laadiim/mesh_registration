using System.Globalization;
using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Algorithms.Tracing;
using MeshRegistration.Core.Geometry;

namespace MeshRegistration.IO.Export;

/// <summary>
/// Writes traced lines in forms MeshLab can display.
/// </summary>
/// <remarks>
/// Two representations, because they answer different questions:
/// <list type="bullet">
///   <item>
///     A <b>polyline</b> file is exact and tiny, and is the right thing to feed to another tool.
///     MeshLab imports OBJ <c>l</c> elements as edges, which only render in wireframe modes.
///   </item>
///   <item>
///     A <b>tube</b> file turns each line into actual triangle geometry, so it shows up in any
///     shading mode, at a visible thickness, and can carry a colour per line or per sample. This
///     is the one to open when the question is "what did the tracer do".
///   </item>
/// </list>
/// Both are offset slightly along the surface normal so they do not z-fight with the mesh they
/// were traced on.
/// </remarks>
public static class LineExporter
{
    /// <summary>Options controlling how lines are drawn.</summary>
    public sealed record Options
    {
        /// <summary>How far to lift the geometry off the surface, in multiples of the mean edge length.</summary>
        public double SurfaceOffset { get; init; } = 0.25;

        /// <summary>Tube radius, in multiples of the mean edge length.</summary>
        public double TubeRadius { get; init; } = 0.2;

        /// <summary>Cross-section resolution of a tube.</summary>
        public int TubeSides { get; init; } = 6;

        /// <summary>What to colour lines by.</summary>
        public ColorBy ColorBy { get; init; } = ColorBy.Line;
    }

    /// <summary>
    /// Writes the lines as OBJ polylines (<c>v</c> plus <c>l</c>), with per-vertex colours.
    /// </summary>
    public static void WritePolylines(
        string path,
        IReadOnlyList<TracedLine> lines,
        double averageEdgeLength,
        Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        options ??= new Options();

        double offset = averageEdgeLength * options.SurfaceOffset;
        SampleColorSource colors = SampleColorSource.Create(lines, options.ColorBy);

        using FileStream stream = File.Create(path);
        using ObjTextWriter writer = new(stream);

        writer.Comment($"{lines.Count} traced curvature line(s) as polylines");
        writer.Comment($"coloured by {options.ColorBy}");
        writer.BlankLine();

        int nextIndex = 0;
        List<int[]> lineIndices = new(lines.Count);

        for (int l = 0; l < lines.Count; l++)
        {
            TracedLine line = lines[l];
            int[] indices = new int[line.SampleCount];

            for (int s = 0; s < line.SampleCount; s++)
            {
                LineSample sample = line.Samples[s];
                writer.Vertex(sample.Position + (sample.Normal * offset), colors.Get(l, line, sample));
                indices[s] = nextIndex++;
            }

            lineIndices.Add(indices);
        }

        writer.BlankLine();

        for (int l = 0; l < lineIndices.Count; l++)
        {
            writer.Group($"line_{lines[l].Id:D4}");
            writer.Polyline(lineIndices[l]);
        }
    }

    /// <summary>
    /// Writes the lines as tubes of triangles, with a material library giving each line its own
    /// colour.
    /// </summary>
    public static void WriteTubes(
        string path,
        IReadOnlyList<TracedLine> lines,
        double averageEdgeLength,
        Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        options ??= new Options();

        string materialFileName = Path.GetFileNameWithoutExtension(path) + ".mtl";
        string materialPath = Path.Combine(Path.GetDirectoryName(path) ?? ".", materialFileName);

        double offset = averageEdgeLength * options.SurfaceOffset;
        double radius = averageEdgeLength * options.TubeRadius;
        int sides = Math.Max(3, options.TubeSides);

        SampleColorSource colors = SampleColorSource.Create(lines, options.ColorBy);

        // A material per line keeps the file small when colouring by line. Per-sample colouring
        // additionally writes vertex colours, which MeshLab shows once vertex colour is enabled.
        List<ColorRgb> materials = [];

        using (FileStream stream = File.Create(path))
        using (ObjTextWriter writer = new(stream))
        {
            writer.Comment($"{lines.Count} traced curvature line(s) as tubes");
            writer.Comment($"coloured by {options.ColorBy}");
            writer.MaterialLibrary(materialFileName);
            writer.BlankLine();

            int nextIndex = 0;
            List<(int Start, int RingCount, int Material)> tubes = new(lines.Count);

            for (int l = 0; l < lines.Count; l++)
            {
                TracedLine line = lines[l];
                if (line.SampleCount < 2)
                {
                    continue;
                }

                int start = nextIndex;

                for (int s = 0; s < line.SampleCount; s++)
                {
                    LineSample sample = line.Samples[s];
                    ColorRgb color = colors.Get(l, line, sample);

                    // The ring lies in the plane perpendicular to the line. Because the line's
                    // direction is tangent to the surface, the surface normal is already
                    // perpendicular to it and makes a natural, non-twisting first axis.
                    Vec3 axisA = sample.Normal;
                    Vec3 axisB = sample.Direction.Cross(sample.Normal);

                    if (!axisB.IsUsableDirection)
                    {
                        axisB = Vec3.OrthogonalTo(axisA);
                    }
                    else
                    {
                        axisB = axisB.Normalized();
                    }

                    Vec3 centre = sample.Position + (sample.Normal * offset);

                    for (int k = 0; k < sides; k++)
                    {
                        double angle = 2 * Math.PI * k / sides;
                        (double sin, double cos) = Math.SinCos(angle);
                        writer.Vertex(centre + (((axisA * cos) + (axisB * sin)) * radius), color);
                        nextIndex++;
                    }
                }

                materials.Add(colors.MaterialFor(l, line));
                tubes.Add((start, line.SampleCount, materials.Count - 1));
            }

            writer.BlankLine();

            foreach ((int start, int ringCount, int material) in tubes)
            {
                writer.Group($"tube_{material:D4}");
                writer.UseMaterial($"line{material:D4}");

                for (int ring = 0; ring + 1 < ringCount; ring++)
                {
                    int a = start + (ring * sides);
                    int b = start + ((ring + 1) * sides);

                    for (int k = 0; k < sides; k++)
                    {
                        int next = (k + 1) % sides;

                        writer.Triangle(a + k, b + k, b + next);
                        writer.Triangle(a + k, b + next, a + next);
                    }
                }
            }
        }

        WriteMaterialLibrary(materialPath, materials);
    }

    private static void WriteMaterialLibrary(string path, List<ColorRgb> materials)
    {
        using StreamWriter writer = new(path);
        writer.WriteLine("# materials for traced curvature lines");

        for (int i = 0; i < materials.Count; i++)
        {
            (double r, double g, double b) = materials[i].ToUnit();

            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"newmtl line{i:D4}"));
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Kd {r:G6} {g:G6} {b:G6}"));
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Ka {r * 0.3:G6} {g * 0.3:G6} {b * 0.3:G6}"));
            writer.WriteLine("Ks 0.1 0.1 0.1");
            writer.WriteLine("illum 2");
            writer.WriteLine("Ns 16");
            writer.WriteLine();
        }
    }

    /// <summary>
    /// Resolves the colour of a sample, pre-computing any range the chosen scalar needs.
    /// </summary>
    private sealed class SampleColorSource
    {
        private readonly ColorBy _mode;
        private readonly double _low;
        private readonly double _high;

        private SampleColorSource(ColorBy mode, double low, double high)
        {
            _mode = mode;
            _low = low;
            _high = high;
        }

        public static SampleColorSource Create(IReadOnlyList<TracedLine> lines, ColorBy mode)
        {
            if (mode is ColorBy.Line or ColorBy.Flags)
            {
                return new SampleColorSource(mode, 0, 1);
            }

            IEnumerable<double> values = lines
                .SelectMany(line => line.Samples)
                .Select(sample => Scalar(mode, sample));

            (double low, double high) = ColorRamp.RobustRange(values);
            return new SampleColorSource(mode, low, high);
        }

        public ColorRgb Get(int lineIndex, TracedLine line, LineSample sample) => _mode switch
        {
            ColorBy.Line => ColorRamp.Categorical(line.Id),
            ColorBy.Flags => ColorRamp.ForFlags(sample.Flags),
            ColorBy.GeodesicCurvature or ColorBy.KMin or ColorBy.KMax or ColorBy.Mean or ColorBy.Gaussian =>
                ColorRamp.Diverging(SymmetricNormalise(Scalar(_mode, sample))),
            _ => ColorRamp.Sequential(Normalise(Scalar(_mode, sample))),
        };

        public ColorRgb MaterialFor(int lineIndex, TracedLine line) => _mode == ColorBy.Line
            ? ColorRamp.Categorical(line.Id)

            // For per-sample colouring the material is neutral so that vertex colours dominate.
            : new ColorRgb(200, 200, 200);

        private static double Scalar(ColorBy mode, LineSample sample) => mode switch
        {
            ColorBy.KMin => sample.KMin,
            ColorBy.KMax => sample.KMax,
            ColorBy.Mean => (sample.KMin + sample.KMax) * 0.5,
            ColorBy.Gaussian => sample.KMin * sample.KMax,
            ColorBy.Confidence => sample.Confidence,
            ColorBy.GeodesicCurvature => sample.GeodesicCurvature,
            ColorBy.Anisotropy => (sample.KMax - sample.KMin) * 0.5,
            _ => 0,
        };

        private double Normalise(double value) => (value - _low) / (_high - _low);

        /// <summary>Maps a signed value to [-1, 1] with zero staying at zero.</summary>
        private double SymmetricNormalise(double value)
        {
            double extent = Math.Max(Math.Abs(_low), Math.Abs(_high));
            return extent > 0 ? value / extent : 0;
        }
    }
}
