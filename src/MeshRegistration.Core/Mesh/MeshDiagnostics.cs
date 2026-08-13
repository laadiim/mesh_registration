namespace MeshRegistration.Core.Mesh;

/// <summary>
/// What <see cref="MeshBuilder.Build"/> found and what it did about it.
/// </summary>
/// <remarks>
/// This report exists because the interesting topological defects are the <em>silent</em> ones.
/// A hard failure at least announces itself; an isolated vertex or a bow-tie vertex used to slip
/// through and quietly corrupt the neighbourhood of a handful of points, which then produced
/// meaningless curvature and, downstream, meaningless candidate transforms. Both defects occur
/// in the bundled sample data.
/// </remarks>
public sealed record MeshDiagnostics
{
    /// <summary>Vertices in the file, before any welding or splitting.</summary>
    public int InputVertexCount { get; init; }

    /// <summary>Triangles in the file, before any faces were dropped.</summary>
    public int InputTriangleCount { get; init; }

    public int OutputVertexCount { get; init; }

    public int OutputTriangleCount { get; init; }

    /// <summary>Vertices merged because they coincided within the welding tolerance.</summary>
    public int WeldedVertices { get; init; }

    /// <summary>Faces dropped for repeating a vertex index or having negligible area.</summary>
    public int DegenerateFacesRemoved { get; init; }

    /// <summary>Faces dropped for duplicating an earlier face's vertex set.</summary>
    public int DuplicateFacesRemoved { get; init; }

    /// <summary>Edges used by exactly one triangle: the real border of the surface.</summary>
    public int BoundaryEdgeCount { get; init; }

    /// <summary>Edges used by exactly two triangles and paired normally.</summary>
    public int ManifoldEdgeCount { get; init; }

    /// <summary>Edges used by three or more triangles.</summary>
    public int NonManifoldEdgeCount { get; init; }

    /// <summary>How <see cref="NonManifoldEdgeCount"/> edges were resolved.</summary>
    public NonManifoldEdgePolicy NonManifoldEdgePolicy { get; init; }

    /// <summary>
    /// Adjacencies discarded while resolving non-manifold edges. Each one turns a pair of
    /// corners into boundary corners.
    /// </summary>
    public int AdjacenciesCutAtNonManifoldEdges { get; init; }

    /// <summary>
    /// Edges whose two triangles still disagreed on winding after orientation propagation.
    /// </summary>
    /// <remarks>
    /// A non-zero count means the component is non-orientable (a Mobius-like configuration), not
    /// that repair failed. Such edges are cut, because a consistent normal field does not exist
    /// across them.
    /// </remarks>
    public int NonOrientableEdgeCount { get; init; }

    /// <summary>Triangles whose winding was reversed to agree with their component.</summary>
    public int ReorientedFaces { get; init; }

    /// <summary>Closed components whose winding was reversed to face outwards.</summary>
    public int OutwardFlippedComponents { get; init; }

    /// <summary>
    /// Bow-tie vertices found: vertices whose incident triangles form more than one fan.
    /// </summary>
    /// <remarks>
    /// Such a vertex has no single well-defined one-ring. The previous implementation stored one
    /// arbitrary incident corner per vertex, so the neighbourhood walk covered only one of the
    /// fans and silently returned a partial neighbourhood.
    /// </remarks>
    public int NonManifoldVerticesFound { get; init; }

    /// <summary>Extra vertex copies created to give each fan its own vertex.</summary>
    public int VerticesAddedBySplitting { get; init; }

    /// <summary>Vertices no triangle refers to.</summary>
    public int IsolatedVertexCount { get; init; }

    /// <summary>Vertices on a boundary, including boundaries created by cutting.</summary>
    public int BoundaryVertexCount { get; init; }

    /// <summary>Connected components of the repaired surface.</summary>
    public int ConnectedComponentCount { get; init; }

    /// <summary>Mean triangle edge length: the mesh's sampling resolution.</summary>
    public double AverageEdgeLength { get; init; }

    /// <summary>Bounding box diagonal: the model's overall scale.</summary>
    public double DiagonalLength { get; init; }

    /// <summary>True when nothing needed repairing.</summary>
    public bool IsClean =>
        WeldedVertices == 0 &&
        DegenerateFacesRemoved == 0 &&
        DuplicateFacesRemoved == 0 &&
        NonManifoldEdgeCount == 0 &&
        NonOrientableEdgeCount == 0 &&
        ReorientedFaces == 0 &&
        NonManifoldVerticesFound == 0 &&
        IsolatedVertexCount == 0;

    /// <summary>A short human-readable summary for the console.</summary>
    public string ToSummary()
    {
        System.Text.StringBuilder builder = new();
        builder.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"{OutputVertexCount} vertices, {OutputTriangleCount} triangles, ");
        builder.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"{ConnectedComponentCount} component(s), diagonal {DiagonalLength:G4}, avg edge {AverageEdgeLength:G4}");

        if (IsClean)
        {
            builder.Append("; no repairs needed");
            return builder.ToString();
        }

        AppendIfNonZero(builder, WeldedVertices, "welded vertices");
        AppendIfNonZero(builder, DegenerateFacesRemoved, "degenerate faces removed");
        AppendIfNonZero(builder, DuplicateFacesRemoved, "duplicate faces removed");
        AppendIfNonZero(builder, NonManifoldEdgeCount, "non-manifold edges");
        AppendIfNonZero(builder, NonOrientableEdgeCount, "non-orientable edges");
        AppendIfNonZero(builder, ReorientedFaces, "faces reoriented");
        AppendIfNonZero(builder, NonManifoldVerticesFound, "bow-tie vertices split");
        AppendIfNonZero(builder, IsolatedVertexCount, "isolated vertices");

        return builder.ToString();
    }

    private static void AppendIfNonZero(System.Text.StringBuilder builder, int value, string label)
    {
        if (value != 0)
        {
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"; {value} {label}");
        }
    }
}
