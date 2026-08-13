using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;
using Xunit;

namespace MeshRegistration.Core.Tests;

/// <summary>
/// Tests for topology repair, using hand-built meshes that isolate one defect each.
/// </summary>
public sealed class MeshRepairTests
{
    /// <summary>
    /// Two triangles meeting at a single shared vertex. Every <em>edge</em> is perfectly
    /// manifold, which is why edge-level checks miss this configuration entirely — yet the shared
    /// vertex has two disjoint fans and no single one-ring.
    /// </summary>
    private static (Vec3[] Positions, Triangle[] Triangles) BowTie() =>
    (
        [
            new Vec3(0, 0, 0),   // 0: the pinch point
            new Vec3(1, 0, 0),   // 1
            new Vec3(1, 1, 0),   // 2
            new Vec3(-1, 0, 0),  // 3
            new Vec3(-1, -1, 0), // 4
        ],
        [
            new Triangle(0, 1, 2),
            new Triangle(0, 3, 4),
        ]
    );

    /// <summary>Three triangles sharing one edge: a genuinely non-manifold edge.</summary>
    private static (Vec3[] Positions, Triangle[] Triangles) ThreeFanEdge() =>
    (
        [
            new Vec3(0, 0, 0),  // 0: edge start
            new Vec3(1, 0, 0),  // 1: edge end
            new Vec3(0.5, 1, 0),
            new Vec3(0.5, -1, 0),
            new Vec3(0.5, 0, 1),
        ],
        [
            new Triangle(0, 1, 2),
            new Triangle(1, 0, 3),
            new Triangle(0, 1, 4),
        ]
    );

    [Fact]
    public void BowTieVertex_IsSplitIntoOneVertexPerFan()
    {
        (Vec3[] positions, Triangle[] triangles) = BowTie();
        MeshBuildResult result = MeshBuilder.Build(positions, triangles);

        Assert.Equal(1, result.Diagnostics.NonManifoldVerticesFound);
        Assert.Equal(1, result.Diagnostics.VerticesAddedBySplitting);
        Assert.Equal(6, result.Mesh.VertexCount);

        // Geometry is untouched: the copy sits exactly on the original.
        Assert.Equal(2, result.Mesh.Positions.ToArray().Count(p => p == new Vec3(0, 0, 0)));

        // Both triangles survive, and no vertex is shared between them any more.
        Assert.Equal(2, result.Mesh.TriangleCount);
        Triangle a = result.Mesh.Face(0);
        Triangle b = result.Mesh.Face(1);
        int[] setA = [a.V0, a.V1, a.V2];
        int[] setB = [b.V0, b.V1, b.V2];
        Assert.Empty(setA.Intersect(setB));
    }

    [Fact]
    public void BowTieVertex_LeavesEveryVertexWithASingleFan()
    {
        (Vec3[] positions, Triangle[] triangles) = BowTie();
        MeshBuildResult result = MeshBuilder.Build(positions, triangles);

        // After splitting, walking the fan from the stored incident corner must reach every
        // corner that sits at the vertex.
        Span<int> fan = stackalloc int[16];

        for (int v = 0; v < result.Mesh.VertexCount; v++)
        {
            int start = result.Topology.IncidentCorner(v);
            Assert.True(start >= 0, $"Vertex {v} has no incident corner.");

            int fanSize = result.Topology.GatherFan(start, fan);
            Assert.True(fanSize > 0);

            int cornersAtVertex = 0;
            for (int c = 0; c < result.Topology.CornerCount; c++)
            {
                if (result.Mesh.Face(c / 3)[c % 3] == v)
                {
                    cornersAtVertex++;
                }
            }

            Assert.Equal(cornersAtVertex, fanSize);
        }
    }

    [Fact]
    public void NonManifoldEdge_WithCutPolicy_KeepsEveryTriangle()
    {
        (Vec3[] positions, Triangle[] triangles) = ThreeFanEdge();
        MeshBuildResult result = MeshBuilder.Build(
            positions, triangles, new MeshBuildOptions { NonManifoldEdges = NonManifoldEdgePolicy.Cut });

        Assert.Equal(1, result.Diagnostics.NonManifoldEdgeCount);
        Assert.Equal(NonManifoldEdgePolicy.Cut, result.Diagnostics.NonManifoldEdgePolicy);

        // Nothing is discarded; only the adjacency across the singular edge is.
        Assert.Equal(3, result.Mesh.TriangleCount);

        // All three corners facing the singular edge became boundary corners.
        Assert.Equal(3, result.Diagnostics.AdjacenciesCutAtNonManifoldEdges);
        for (int c = 0; c < result.Topology.CornerCount; c++)
        {
            Triangle t = result.Mesh.Face(c / 3);
            int a = t[(c + 1) % 3];
            int b = t[(c + 2) % 3];
            bool facesSingularEdge = (a == 0 && b == 1) || (a == 1 && b == 0);

            if (facesSingularEdge)
            {
                Assert.Equal(-1, result.Topology.Opposite(c));
            }
        }
    }

    [Fact]
    public void NonManifoldEdge_WithPairBestPolicy_KeepsTheFlattestContinuation()
    {
        (Vec3[] positions, Triangle[] triangles) = ThreeFanEdge();
        MeshBuildResult result = MeshBuilder.Build(
            positions,
            triangles,
            new MeshBuildOptions { NonManifoldEdges = NonManifoldEdgePolicy.PairBestContinuation });

        Assert.Equal(1, result.Diagnostics.NonManifoldEdgeCount);
        Assert.Equal(3, result.Mesh.TriangleCount);

        // One adjacency survives, so exactly one pair of corners is joined across the edge.
        Assert.Equal(1, result.Diagnostics.AdjacenciesCutAtNonManifoldEdges);

        int joinedPairs = 0;
        for (int c = 0; c < result.Topology.CornerCount; c++)
        {
            if (result.Topology.Opposite(c) >= 0)
            {
                joinedPairs++;
            }
        }

        Assert.Equal(2, joinedPairs); // two corners, one pair
    }

    [Fact]
    public void NonManifoldEdge_WithStrictPolicy_Throws()
    {
        (Vec3[] positions, Triangle[] triangles) = ThreeFanEdge();

        NonManifoldMeshException exception = Assert.Throws<NonManifoldMeshException>(() =>
            MeshBuilder.Build(
                positions,
                triangles,
                new MeshBuildOptions { NonManifoldEdges = NonManifoldEdgePolicy.Strict }));

        // The message must name the offending edge, unlike the previous bare
        // "Non-manifold mesh at the input."
        Assert.Contains("(0, 1)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InconsistentWinding_IsRepaired()
    {
        // Both triangles traverse the shared edge 0 -> 1 in the same direction, so one of them
        // faces the wrong way.
        Vec3[] positions =
        [
            new(0, 0, 0),
            new(1, 0, 0),
            new(0.5, 1, 0),
            new(0.5, -1, 0),
        ];
        Triangle[] triangles = [new(0, 1, 2), new(0, 1, 3)];

        MeshBuildResult result = MeshBuilder.Build(positions, triangles);

        Assert.Equal(1, result.Diagnostics.ReorientedFaces);
        Assert.Equal(1, result.Diagnostics.ManifoldEdgeCount);
        Assert.Equal(0, result.Diagnostics.NonOrientableEdgeCount);

        // With consistent winding the two face normals now agree.
        Assert.True(result.Mesh.FaceNormals[0].Dot(result.Mesh.FaceNormals[1]) > 0.99);
    }

    [Fact]
    public void ClosedComponent_IsOrientedOutward()
    {
        // A tetrahedron wound inward; repair must flip the whole component.
        Vec3[] positions =
        [
            new(0, 0, 0),
            new(1, 0, 0),
            new(0, 1, 0),
            new(0, 0, 1),
        ];

        // Consistently wound, but every face normal points at the centroid.
        Triangle[] inwardWinding =
        [
            new(0, 1, 2),
            new(0, 3, 1),
            new(0, 2, 3),
            new(1, 3, 2),
        ];
        Triangle[] outwardWinding = [.. inwardWinding.Select(t => t.Flipped())];

        MeshBuildResult repaired = MeshBuilder.Build(positions, inwardWinding);
        Assert.Equal(1, repaired.Diagnostics.OutwardFlippedComponents);

        // Winding was already consistent, so no individual face needed reorienting; only the
        // component as a whole was turned inside out.
        Assert.Equal(0, repaired.Diagnostics.ReorientedFaces);

        // A mesh that already faces outward must be left alone.
        MeshBuildResult untouched = MeshBuilder.Build(positions, outwardWinding);
        Assert.Equal(0, untouched.Diagnostics.OutwardFlippedComponents);

        Vec3 centroid = new(0.25, 0.25, 0.25);
        foreach (MeshBuildResult result in new[] { repaired, untouched })
        {
            for (int f = 0; f < result.Mesh.TriangleCount; f++)
            {
                (Vec3 p0, Vec3 p1, Vec3 p2) = result.Mesh.FaceVertices(f);
                Vec3 faceCentre = (p0 + p1 + p2) / 3.0;
                Assert.True(
                    result.Mesh.FaceNormals[f].Dot(faceCentre - centroid) > 0,
                    $"Face {f} points inward.");
            }
        }
    }

    [Fact]
    public void IsolatedVertex_IsFlaggedRatherThanSilentlyMisindexed()
    {
        Vec3[] positions = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(5, 5, 5)];
        Triangle[] triangles = [new(0, 1, 2)];

        MeshBuildResult result = MeshBuilder.Build(positions, triangles);

        Assert.Equal(1, result.Diagnostics.IsolatedVertexCount);
        Assert.True(result.Topology.IsIsolated(3));

        // The previous implementation left the incident corner at 0, which pointed at an
        // unrelated triangle and silently produced that triangle's neighbourhood.
        Assert.Equal(-1, result.Topology.IncidentCorner(3));
        Assert.Empty(result.Topology.VertexNeighbours(3).ToArray());
    }

    [Fact]
    public void DegenerateAndDuplicateFaces_AreRemoved()
    {
        Vec3[] positions = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)];
        Triangle[] triangles =
        [
            new(0, 1, 2),
            new(0, 1, 1), // repeated vertex
            new(2, 0, 1), // same vertex set as the first, different winding
        ];

        MeshBuildResult result = MeshBuilder.Build(positions, triangles);

        Assert.Equal(1, result.Diagnostics.DegenerateFacesRemoved);
        Assert.Equal(1, result.Diagnostics.DuplicateFacesRemoved);
        Assert.Equal(1, result.Mesh.TriangleCount);
    }

    [Fact]
    public void Welding_ReconnectsAMeshStoredWithPerFaceVertices()
    {
        // Two triangles that share an edge geometrically but store private copies of every
        // vertex, as exporters that flatten indexing produce.
        Vec3[] positions =
        [
            new(0, 0, 0), new(1, 0, 0), new(0, 1, 0),
            new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
        ];
        Triangle[] triangles = [new(0, 1, 2), new(3, 4, 5)];

        MeshBuildResult unwelded = MeshBuilder.Build(positions, triangles);

        // Without welding the surface is shattered: two islands, every edge a boundary.
        Assert.Equal(2, unwelded.Diagnostics.ConnectedComponentCount);
        Assert.Equal(6, unwelded.Diagnostics.BoundaryEdgeCount);

        MeshBuildResult welded = MeshBuilder.Build(
            positions, triangles, new MeshBuildOptions { WeldVertices = true });

        Assert.Equal(2, welded.Diagnostics.WeldedVertices);
        Assert.Equal(4, welded.Mesh.VertexCount);
        Assert.Equal(1, welded.Diagnostics.ConnectedComponentCount);
        Assert.Equal(1, welded.Diagnostics.ManifoldEdgeCount);
    }

    [Fact]
    public void CornerTable_IsSymmetricAndFansClose()
    {
        // A closed octahedron: every fan is a cycle, every edge is paired.
        Vec3[] positions =
        [
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        ];
        Triangle[] triangles =
        [
            new(0, 2, 4), new(2, 1, 4), new(1, 3, 4), new(3, 0, 4),
            new(2, 0, 5), new(1, 2, 5), new(3, 1, 5), new(0, 3, 5),
        ];

        MeshBuildResult result = MeshBuilder.Build(positions, triangles);
        MeshTopology topology = result.Topology;

        Assert.Equal(0, result.Diagnostics.BoundaryEdgeCount);
        Assert.Equal(12, result.Diagnostics.ManifoldEdgeCount);

        for (int c = 0; c < topology.CornerCount; c++)
        {
            int opposite = topology.Opposite(c);
            Assert.True(opposite >= 0, $"Corner {c} of a closed mesh has no opposite.");
            Assert.Equal(c, topology.Opposite(opposite));
        }

        // Every vertex of an octahedron has four incident triangles, and the fan must close.
        Span<int> fan = stackalloc int[16];

        for (int v = 0; v < result.Mesh.VertexCount; v++)
        {
            int fanSize = topology.GatherFan(topology.IncidentCorner(v), fan);
            Assert.Equal(4, fanSize);
            Assert.Equal(4, topology.VertexNeighbours(v).Length);
        }
    }
}
