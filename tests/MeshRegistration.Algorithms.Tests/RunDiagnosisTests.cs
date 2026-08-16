using MeshRegistration.Core.Mesh;
using MeshRegistration.IO.Export;
using Xunit;

namespace MeshRegistration.Algorithms.Tests;

/// <summary>
/// Tests that a short run is explained by the cause that actually applies.
/// </summary>
/// <remarks>
/// These guard against the failure mode that prompted the code: reporting one cause while the
/// numbers indicate another. A wrong explanation is worse than none, because it sends the user
/// away from the fix.
/// </remarks>
public sealed class RunDiagnosisTests
{
    private static TracingReport Tracing(int lines, Dictionary<string, int>? endReasons = null) =>
        new()
        {
            SeedCount = lines,
            LineCount = lines,
            EndReasons = endReasons ?? [],
        };

    [Fact]
    public void UnweldedMesh_IsIdentifiedAndPointedAtWeld()
    {
        // Every face its own island: the shape of Head_2.obj, which stores a private copy of each
        // vertex per face.
        MeshDiagnostics topology = new()
        {
            OutputVertexCount = 210984,
            OutputTriangleCount = 70323,
            ConnectedComponentCount = 70323,
            ManifoldEdgeCount = 0,
            WeldedVertices = 0,
        };

        // Note what this is NOT: nothing is planar or umbilic. Everything failed for want of
        // neighbours.
        CurvatureReport curvature = new()
        {
            VertexCount = 210984,
            PlanarVertices = 0,
            UmbilicVertices = 0,
            UnusableVertices = 210984,
        };

        IReadOnlyList<string> notes = RunDiagnosis.Explain(topology, curvature, Tracing(0), 50);

        Assert.NotEmpty(notes);
        Assert.Contains(notes, n => n.Contains("not welded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notes, n => n.Contains("--weld", StringComparison.Ordinal));

        // The old message blamed flatness. It must not come back.
        Assert.DoesNotContain(notes, n => n.Contains("flat or spherical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenuinelyFeaturelessMesh_IsNotBlamedOnWelding()
    {
        // Connected, well formed, but mostly flat and spherical — an architectural model.
        MeshDiagnostics topology = new()
        {
            OutputVertexCount = 750742,
            OutputTriangleCount = 1480240,
            ConnectedComponentCount = 47,
            ManifoldEdgeCount = 2_200_000,
            WeldedVertices = 0,
        };

        CurvatureReport curvature = new()
        {
            VertexCount = 750742,
            PlanarVertices = 327_699,
            UmbilicVertices = 223_120,
            UnusableVertices = 75,
        };

        IReadOnlyList<string> notes = RunDiagnosis.Explain(topology, curvature, Tracing(0), 50);

        Assert.NotEmpty(notes);
        Assert.Contains(notes, n => n.Contains("flat or spherical", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notes, n => n.Contains("umbilic-threshold", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, n => n.Contains("--weld", StringComparison.Ordinal));
    }

    [Fact]
    public void PoorFitOnAConnectedMesh_SuggestsAWiderNeighbourhood()
    {
        MeshDiagnostics topology = new()
        {
            OutputVertexCount = 10000,
            OutputTriangleCount = 19000,
            ConnectedComponentCount = 3,
            ManifoldEdgeCount = 28000,
        };

        CurvatureReport curvature = new()
        {
            VertexCount = 10000,
            PlanarVertices = 10,
            UmbilicVertices = 20,
            UnusableVertices = 9000,
        };

        IReadOnlyList<string> notes = RunDiagnosis.Explain(topology, curvature, Tracing(0), 50);

        Assert.Contains(notes, n => n.Contains("--nbhood", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, n => n.Contains("--weld", StringComparison.Ordinal));
    }

    [Fact]
    public void FragmentedModel_SuggestsMoreAndCloserSeeds()
    {
        // The geb1.obj shape: connected enough to trace, but lines hit rims almost immediately.
        MeshDiagnostics topology = new()
        {
            OutputVertexCount = 57033,
            OutputTriangleCount = 106930,
            ConnectedComponentCount = 47,
            BoundaryEdgeCount = 7058,
            ManifoldEdgeCount = 150000,
        };

        CurvatureReport curvature = new()
        {
            VertexCount = 57033,
            PlanarVertices = 3068,
            UmbilicVertices = 16002,
            UnusableVertices = 40,
        };

        IReadOnlyList<string> notes = RunDiagnosis.Explain(
            topology, curvature, Tracing(11, new Dictionary<string, int> { ["Boundary"] = 19 }), 50);

        Assert.Contains(notes, n => n.Contains("--seed-spacing", StringComparison.Ordinal));
    }

    [Fact]
    public void HealthyRun_SaysNothing()
    {
        MeshDiagnostics topology = new()
        {
            OutputVertexCount = 28393,
            OutputTriangleCount = 56134,
            ConnectedComponentCount = 1,
            ManifoldEdgeCount = 83876,
        };

        CurvatureReport curvature = new()
        {
            VertexCount = 28393,
            PlanarVertices = 89,
            UmbilicVertices = 2202,
            UnusableVertices = 0,
        };

        // 48 of 50 lines: nothing to explain.
        Assert.Empty(RunDiagnosis.Explain(topology, curvature, Tracing(48), 50));
    }
}
