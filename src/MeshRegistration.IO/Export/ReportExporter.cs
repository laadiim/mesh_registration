using System.Text.Json;
using System.Text.Json.Serialization;
using MeshRegistration.Algorithms.Tracing;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.IO.Export;

/// <summary>Summary of a tracing run, written alongside the geometry.</summary>
/// <param name="Topology">What mesh repair found and did.</param>
/// <param name="Tracing">What the tracer produced.</param>
public sealed record RunReport(MeshDiagnostics Topology, TracingReport Tracing);

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
        double totalLength = 0;

        foreach (TracedLine line in lines)
        {
            totalSamples += line.SampleCount;
            totalLength += line.Length;
            degenerate += line.DegenerateSampleCount;

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
