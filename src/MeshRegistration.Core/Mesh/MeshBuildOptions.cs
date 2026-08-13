namespace MeshRegistration.Core.Mesh;

/// <summary>
/// How to resolve an edge shared by three or more triangles.
/// </summary>
/// <remarks>
/// Such an edge is genuinely non-manifold: the surface has no well-defined two-sided
/// neighbourhood there, so no policy is "correct" — the question is which approximation serves
/// the downstream algorithms best. Refusing the mesh outright, as the previous implementation
/// did, is the one option that is never useful on scanner data.
/// </remarks>
public enum NonManifoldEdgePolicy
{
    /// <summary>
    /// Treat the edge as a boundary for every incident corner, splitting the surface into
    /// manifold patches.
    /// </summary>
    /// <remarks>
    /// The default. Curvature estimation and line tracing already handle boundaries correctly,
    /// so the rest of the pipeline needs no special case: lines simply stop at the singularity
    /// exactly as they stop at a real border. Nothing is discarded — every triangle survives,
    /// only the adjacency across the singular edge does not.
    /// </remarks>
    Cut,

    /// <summary>
    /// Keep the two corners whose dihedral angle is closest to a straight continuation and cut
    /// the rest.
    /// </summary>
    /// <remarks>
    /// Preserves tracing continuity across the singularity, at the cost of a heuristic choice.
    /// Useful when the non-manifold edges are artefacts of a merge and the "real" surface passes
    /// straight through.
    /// </remarks>
    PairBestContinuation,

    /// <summary>
    /// Reject the mesh with a <see cref="NonManifoldMeshException"/>.
    /// </summary>
    /// <remarks>Reproduces the previous fail-fast behaviour, for callers that want it.</remarks>
    Strict,
}

/// <summary>
/// Raised when a mesh contains non-manifold edges and the policy is
/// <see cref="NonManifoldEdgePolicy.Strict"/>.
/// </summary>
public sealed class NonManifoldMeshException : Exception
{
    public NonManifoldMeshException(string message)
        : base(message)
    {
    }

    public NonManifoldMeshException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public NonManifoldMeshException()
        : base("The mesh is non-manifold.")
    {
    }
}

/// <summary>
/// Tuning for <see cref="MeshBuilder.Build"/>.
/// </summary>
/// <remarks>
/// Every tolerance here is a <em>fraction</em> of a mesh-derived length, never an absolute
/// distance. The bundled sample data spans bounding box diagonals from 0.14 to 256.6, so an
/// absolute tolerance that behaves sensibly on one file is meaningless on another.
/// </remarks>
public sealed record MeshBuildOptions
{
    /// <summary>How to resolve edges shared by three or more triangles.</summary>
    public NonManifoldEdgePolicy NonManifoldEdges { get; init; } = NonManifoldEdgePolicy.Cut;

    /// <summary>
    /// Merge vertices closer together than <see cref="WeldTolerance"/>. Off by default because
    /// properly indexed files need it and it costs a spatial hash pass.
    /// </summary>
    /// <remarks>
    /// Essential for files where every face carries its own copy of its vertices: without
    /// welding, such a mesh is topologically shattered into isolated triangles, every vertex is
    /// a boundary vertex with a two-triangle neighbourhood, and curvature estimation fails
    /// everywhere for lack of neighbours.
    /// </remarks>
    public bool WeldVertices { get; init; }

    /// <summary>
    /// Welding distance, as a fraction of the bounding box diagonal. The default is about one
    /// part in a million — tight enough to only merge vertices meant to be identical.
    /// </summary>
    public double WeldTolerance { get; init; } = 1e-6;

    /// <summary>
    /// A triangle is degenerate when its area falls below this fraction of the mean face area.
    /// </summary>
    public double DegenerateAreaFraction { get; init; } = 1e-10;

    /// <summary>Drop faces that repeat an earlier face's vertex set, regardless of winding.</summary>
    public bool RemoveDuplicateFaces { get; init; } = true;

    /// <summary>
    /// Propagate a consistent triangle winding across each connected component, and orient
    /// closed components outwards.
    /// </summary>
    public bool RepairOrientation { get; init; } = true;
}
