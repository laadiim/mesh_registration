using System.Text.Json;
using System.Text.Json.Serialization;
using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Algorithms.Tracing;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.IO.Export;

/// <summary>Summary of a tracing run, written alongside the geometry.</summary>
/// <param name="Topology">What mesh repair found and did.</param>
/// <param name="Curvature">How much of the surface carries a usable principal direction.</param>
/// <param name="Tracing">What the tracer produced.</param>
public sealed record RunReport(
    MeshDiagnostics Topology,
    CurvatureReport Curvature,
    TracingReport Tracing);

/// <summary>
/// How the curvature stage classified the mesh.
/// </summary>
/// <remarks>
/// The degenerate fractions are the headline number of this stage: they say how much of the
/// surface has no defined principal direction, and therefore where no line can be seeded and no
/// direction may be read. On the sample data they range from under 2% to over 70%.
/// </remarks>
public sealed record CurvatureReport
{
    public int VertexCount { get; init; }

    /// <summary>Vertices on a flat patch: neither curvature values nor directions are informative.</summary>
    public int PlanarVertices { get; init; }

    /// <summary>
    /// Vertices on a spherical patch: the curvature values hold, but no principal direction
    /// exists. Counted separately from <see cref="PlanarVertices"/>, which are umbilic as well.
    /// </summary>
    public int UmbilicVertices { get; init; }

    /// <summary>Vertices where no shape operator could be fitted at all.</summary>
    public int UnusableVertices { get; init; }

    /// <summary>Vertices with a one-sided neighbourhood.</summary>
    public int BoundaryVertices { get; init; }

    /// <summary>Radius of the fitting neighbourhood, in model units.</summary>
    public double NeighbourhoodRadius { get; init; }

    /// <summary>Fraction of vertices offering a usable principal direction.</summary>
    public double UsableFraction => VertexCount > 0
        ? 1.0 - ((PlanarVertices + UmbilicVertices + UnusableVertices) / (double)VertexCount)
        : 0;

    /// <summary>Classifies every vertex of a mesh.</summary>
    public static CurvatureReport From(TriangleMesh mesh, ShapeOperatorField curvature)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(curvature);

        int planar = 0;
        int umbilic = 0;
        int unusable = 0;
        int boundary = 0;

        for (int v = 0; v < mesh.VertexCount; v++)
        {
            // Planar and umbilic follow from the eigenvalues, so the operator must be decomposed;
            // the flags stored by the fit alone can never carry them.
            CurvatureFlags flags = curvature.AtVertex(v).Flags;

            if ((flags & CurvatureFlags.Boundary) != 0)
            {
                boundary++;
            }

            if ((flags & CurvatureFlags.Unusable) != 0)
            {
                unusable++;
            }
            else if ((flags & CurvatureFlags.Planar) != 0)
            {
                planar++;
            }
            else if ((flags & CurvatureFlags.Umbilic) != 0)
            {
                umbilic++;
            }
        }

        return new CurvatureReport
        {
            VertexCount = mesh.VertexCount,
            PlanarVertices = planar,
            UmbilicVertices = umbilic,
            UnusableVertices = unusable,
            BoundaryVertices = boundary,
            NeighbourhoodRadius = curvature.NeighbourhoodRadius,
        };
    }
}

/// <summary>Aggregate statistics over a set of traced lines.</summary>
public sealed record TracingReport
{
    public int SeedCount { get; init; }

    public int LineCount { get; init; }

    public int TotalSamples { get; init; }

    /// <summary>Arc length between samples, in model units.</summary>
    public double StepLength { get; init; }

    public double MeanLineLength { get; init; }

    public double MeanSamplesPerLine { get; init; }

    /// <summary>
    /// Samples that fell in a flat or spherical patch and were carried through by parallel
    /// transport rather than by following a principal direction.
    /// </summary>
    public int DegenerateSamples { get; init; }

    /// <summary>How many lines ended for each reason.</summary>
    public Dictionary<string, int> EndReasons { get; init; } = [];

    /// <summary>Samples whose direction came from the maximum-curvature field.</summary>
    public int SamplesOnMaxField { get; init; }

    /// <summary>Samples whose direction came from the minimum-curvature field.</summary>
    public int SamplesOnMinField { get; init; }

    /// <summary>
    /// Fraction of directed samples that were on the maximum field.
    /// </summary>
    /// <remarks>
    /// The seeding option fixes only the first step. Because the min/max labels exchange wherever
    /// the two curvatures cross, and the tracer follows the curve rather than the label, a line
    /// seeded on one field can spend most of its length on the other. This measures it.
    /// </remarks>
    public double MaxFieldFraction => (SamplesOnMaxField + SamplesOnMinField) > 0
        ? SamplesOnMaxField / (double)(SamplesOnMaxField + SamplesOnMinField)
        : 0;

    /// <summary>
    /// Non-finite values found anywhere in the output. Must be zero; retained as an explicit
    /// check because NaN leaking out of curvature estimation was the original defect.
    /// </summary>
    public int NonFiniteSamples { get; init; }

    public static TracingReport From(IReadOnlyList<TracedLine> lines, int seedCount, double stepLength)
    {
        ArgumentNullException.ThrowIfNull(lines);

        Dictionary<string, int> endReasons = [];
        int totalSamples = 0;
        int degenerate = 0;
        int nonFinite = 0;
        int onMax = 0;
        int onMin = 0;
        double totalLength = 0;

        foreach (TracedLine line in lines)
        {
            totalSamples += line.SampleCount;
            totalLength += line.Length;
            degenerate += line.DegenerateSampleCount;

            (int lineMax, int lineMin, int _) = line.FieldUsage();
            onMax += lineMax;
            onMin += lineMin;

            foreach (LineSample sample in line.Samples)
            {
                if (!sample.IsFinite)
                {
                    nonFinite++;
                }
            }

            string key = line.EndReason.ToString();
            endReasons[key] = endReasons.GetValueOrDefault(key) + 1;

            string startKey = line.StartReason.ToString();
            endReasons[startKey] = endReasons.GetValueOrDefault(startKey) + 1;
        }

        return new TracingReport
        {
            SeedCount = seedCount,
            LineCount = lines.Count,
            TotalSamples = totalSamples,
            StepLength = stepLength,
            MeanLineLength = lines.Count > 0 ? totalLength / lines.Count : 0,
            MeanSamplesPerLine = lines.Count > 0 ? (double)totalSamples / lines.Count : 0,
            DegenerateSamples = degenerate,
            EndReasons = endReasons,
            NonFiniteSamples = nonFinite,
            SamplesOnMaxField = onMax,
            SamplesOnMinField = onMin,
        };
    }
}

/// <summary>Writes the run report as JSON.</summary>
public static class ReportExporter
{
    public static void Write(string path, RunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, report, ReportJsonContext.Default.RunReport);
    }
}

/// <summary>
/// Source-generated JSON metadata.
/// </summary>
/// <remarks>
/// Generating the serialiser at compile time avoids reflection at run time, which keeps the tool
/// trim- and AOT-compatible should it ever be published that way.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(RunReport))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;
