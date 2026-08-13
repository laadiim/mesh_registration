using System.Globalization;
using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Algorithms.Tracing;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;
using MeshRegistration.IO;
using MeshRegistration.IO.Export;
using Xunit;

namespace MeshRegistration.Algorithms.Tests;

/// <summary>
/// Tests that the MeshLab exports are well formed and lossless enough to read back.
/// </summary>
public sealed class ExportTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("meshreg-export-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private string Path(string name) => System.IO.Path.Combine(_directory, name);

    private static (MeshBuildResult Build, ShapeOperatorField Curvature, TracedLine[] Lines) Trace()
    {
        (Vec3[] positions, Triangle[] triangles) = AnalyticSurfaces.Torus(3.0, 1.0, around: 120, through: 60);
        MeshBuildResult build = MeshBuilder.Build(positions, triangles);
        ShapeOperatorField curvature = ShapeOperatorField.Compute(build.Mesh, build.Topology);

        TracingOptions options = new() { MaxLines = 8 };
        List<SurfacePoint> seeds = SeedSelector.Select(build.Mesh, build.Topology, curvature, options);
        TracedLine[] lines = new LineTracer(build.Mesh, build.Topology, curvature, options).TraceAll(seeds);

        Assert.NotEmpty(lines);
        return (build, curvature, lines);
    }

    [Fact]
    public void PolylineExport_IsReadableAndHasOneVertexPerSample()
    {
        (MeshBuildResult build, _, TracedLine[] lines) = Trace();
        string path = Path("lines.obj");

        LineExporter.WritePolylines(path, lines, build.Mesh.AverageEdgeLength);

        string[] text = File.ReadAllLines(path);
        int vertexLines = text.Count(l => l.StartsWith("v ", StringComparison.Ordinal));
        int polylineLines = text.Count(l => l.StartsWith("l ", StringComparison.Ordinal));

        Assert.Equal(lines.Sum(line => line.SampleCount), vertexLines);
        Assert.Equal(lines.Length, polylineLines);

        // No BOM: the first byte must be the '#' of the header comment.
        Assert.Equal((byte)'#', File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public void TubeExport_ProducesValidGeometryAndAMaterialPerLine()
    {
        (MeshBuildResult build, _, TracedLine[] lines) = Trace();
        string path = Path("tubes.obj");

        LineExporter.WriteTubes(path, lines, build.Mesh.AverageEdgeLength);

        // The tube mesh must itself be a loadable OBJ.
        (Vec3[] positions, Triangle[] triangles) = ObjReader.Read(path);
        Assert.NotEmpty(positions);
        Assert.NotEmpty(triangles);

        foreach (Vec3 p in positions)
        {
            Assert.True(p.IsFinite);
        }

        // Each tube is a closed strip, so the geometry is manifold apart from the open end caps.
        MeshBuildResult tubeBuild = MeshBuilder.Build(positions, triangles);
        Assert.Equal(0, tubeBuild.Diagnostics.NonManifoldEdgeCount);
        Assert.Equal(lines.Length, tubeBuild.Diagnostics.ConnectedComponentCount);

        string materialPath = Path("tubes.mtl");
        Assert.True(File.Exists(materialPath), "The tube export did not write its material library.");

        int materialCount = File.ReadAllLines(materialPath)
            .Count(l => l.StartsWith("newmtl ", StringComparison.Ordinal));
        Assert.Equal(lines.Length, materialCount);
    }

    [Fact]
    public void CurvatureMeshExport_CarriesOneColouredVertexPerMeshVertex()
    {
        (MeshBuildResult build, ShapeOperatorField curvature, _) = Trace();
        string path = Path("curvature.obj");

        CurvatureMeshExporter.Write(path, build.Mesh, curvature, ColorBy.Flags);

        string[] text = File.ReadAllLines(path);
        string[] vertexLines = [.. text.Where(l => l.StartsWith("v ", StringComparison.Ordinal))];

        Assert.Equal(build.Mesh.VertexCount, vertexLines.Length);
        Assert.Equal(build.Mesh.TriangleCount, text.Count(l => l.StartsWith("f ", StringComparison.Ordinal)));

        // Each vertex line carries position and colour: "v x y z r g b".
        foreach (string line in vertexLines)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(7, parts.Length);

            for (int i = 4; i < 7; i++)
            {
                double channel = double.Parse(parts[i], CultureInfo.InvariantCulture);
                Assert.InRange(channel, 0.0, 1.0);
            }
        }
    }

    [Theory]
    [InlineData(ColorBy.Flags)]
    [InlineData(ColorBy.Anisotropy)]
    [InlineData(ColorBy.KMin)]
    [InlineData(ColorBy.KMax)]
    [InlineData(ColorBy.Mean)]
    [InlineData(ColorBy.Gaussian)]
    [InlineData(ColorBy.Confidence)]
    public void CurvatureMeshExport_WorksForEveryColourMode(ColorBy mode)
    {
        (MeshBuildResult build, ShapeOperatorField curvature, _) = Trace();
        string path = Path($"curvature-{mode}.obj");

        CurvatureMeshExporter.Write(path, build.Mesh, curvature, mode);

        (Vec3[] positions, Triangle[] triangles) = ObjReader.Read(path);
        Assert.Equal(build.Mesh.VertexCount, positions.Length);
        Assert.Equal(build.Mesh.TriangleCount, triangles.Length);
    }

    [Fact]
    public void CsvExport_HasOneRowPerSampleAndNoNonFiniteValues()
    {
        (_, _, TracedLine[] lines) = Trace();
        string path = Path("samples.csv");

        SampleCsvExporter.Write(path, lines);

        string[] text = File.ReadAllLines(path);
        Assert.Equal(lines.Sum(line => line.SampleCount) + 1, text.Length);

        Assert.StartsWith("lineId,sampleIndex,arcLength", text[0], StringComparison.Ordinal);

        // The whole point of the rebuilt curvature stage is that this never appears.
        foreach (string line in text)
        {
            Assert.DoesNotContain("NaN", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Infinity", line, StringComparison.OrdinalIgnoreCase);
        }

        // Every numeric column must round-trip.
        string[] fields = text[1].Split(',');
        Assert.Equal(15, fields.Length);
        for (int i = 0; i < 13; i++)
        {
            Assert.True(
                double.TryParse(fields[i], CultureInfo.InvariantCulture, out _),
                $"Column {i} did not parse: '{fields[i]}'");
        }
    }

    [Fact]
    public void Report_RecordsTheRunAndConfirmsNoNonFiniteSamples()
    {
        (MeshBuildResult build, _, TracedLine[] lines) = Trace();
        string path = Path("report.json");

        TracingReport tracing = TracingReport.From(lines, lines.Length, build.Mesh.AverageEdgeLength);
        ReportExporter.Write(path, new RunReport(build.Diagnostics, tracing));

        string json = File.ReadAllText(path);

        Assert.Contains("\"NonFiniteSamples\": 0", json, StringComparison.Ordinal);
        Assert.Contains("\"ConnectedComponentCount\": 1", json, StringComparison.Ordinal);

        // Enums are written as names, so the file is readable without this source.
        Assert.Contains("\"NonManifoldEdgePolicy\": \"Cut\"", json, StringComparison.Ordinal);

        Assert.Equal(0, tracing.NonFiniteSamples);
        Assert.Equal(lines.Sum(line => line.SampleCount), tracing.TotalSamples);
    }
}
