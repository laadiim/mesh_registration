using System.Runtime.CompilerServices;

namespace MeshRegistration.Core.Mesh;

/// <summary>
/// Per-vertex topological classification.
/// </summary>
[Flags]
public enum VertexFlags
{
    None = 0,

    /// <summary>The vertex lies on a mesh boundary, or on an edge that repair cut open.</summary>
    Boundary = 1,

    /// <summary>No triangle references this vertex. It has no normal and no neighbourhood.</summary>
    Isolated = 2,

    /// <summary>
    /// This vertex was created by splitting a non-manifold ("bow-tie") vertex, or is the
    /// remnant of one. Its position coincides with at least one other vertex.
    /// </summary>
    SplitFromNonManifold = 4,
}

/// <summary>
/// Corner-table connectivity for a triangle mesh, plus a compressed one-ring adjacency list.
/// </summary>
/// <remarks>
/// <para>
/// <b>Corner convention.</b> Corner <c>c</c> belongs to triangle <c>c / 3</c> and sits at local
/// index <c>c % 3</c>, so the vertex <em>at</em> corner <c>c</c> is that triangle's vertex
/// <c>c % 3</c>. The edge <em>opposite</em> corner <c>c</c> joins the vertices at
/// <see cref="Next"/> and <see cref="Previous"/>, and <see cref="Opposite"/> names the corner
/// facing that same edge from the adjacent triangle.
/// </para>
/// <para>
/// This instance is guaranteed <b>manifold by construction</b>: <see cref="MeshBuilder"/>
/// resolves non-manifold edges according to the chosen policy and splits non-manifold vertices
/// into separate copies before the topology is built. Every non-isolated vertex therefore has
/// exactly one fan, so the one-ring walk is well defined and terminates — a property the
/// previous implementation only assumed ("lets assume it is not complex").
/// </para>
/// </remarks>
public sealed class MeshTopology
{
    /// <summary>Guards the fan walk against a corrupt corner table.</summary>
    private const int MaxFanSize = 4096;

    private readonly int[] _opposite;
    private readonly int[] _incidentCorner;
    private readonly VertexFlags[] _vertexFlags;
    private readonly int[] _neighbourOffsets;
    private readonly int[] _neighbourData;

    internal MeshTopology(
        int[] opposite,
        int[] incidentCorner,
        VertexFlags[] vertexFlags,
        int[] neighbourOffsets,
        int[] neighbourData,
        int connectedComponentCount)
    {
        _opposite = opposite;
        _incidentCorner = incidentCorner;
        _vertexFlags = vertexFlags;
        _neighbourOffsets = neighbourOffsets;
        _neighbourData = neighbourData;
        ConnectedComponentCount = connectedComponentCount;
    }

    public int VertexCount => _incidentCorner.Length;

    public int TriangleCount => _opposite.Length / 3;

    public int CornerCount => _opposite.Length;

    public int ConnectedComponentCount { get; }

    /// <summary>The triangle a corner belongs to.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int TriangleOf(int corner) => corner / 3;

    /// <summary>The next corner within the same triangle.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Next(int corner)
    {
        int start = corner - (corner % 3);
        return start + ((corner - start + 1) % 3);
    }

    /// <summary>The previous corner within the same triangle.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Previous(int corner)
    {
        int start = corner - (corner % 3);
        return start + ((corner - start + 2) % 3);
    }

    /// <summary>
    /// The corner facing the same edge from the adjacent triangle, or <c>-1</c> at a boundary or
    /// at an edge that repair cut open.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Opposite(int corner) => _opposite[corner];

    /// <summary>
    /// One corner sitting at <paramref name="vertex"/>, or <c>-1</c> if the vertex is isolated.
    /// </summary>
    /// <remarks>
    /// For a vertex on an open fan this is the corner at one end of the fan, so that swinging
    /// forward from it traverses the whole fan exactly once. The previous implementation stored
    /// whichever triangle happened to be written last and left <c>0</c> for isolated vertices,
    /// which silently walked the fan of an unrelated vertex.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IncidentCorner(int vertex) => _incidentCorner[vertex];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VertexFlags Flags(int vertex) => _vertexFlags[vertex];

    public bool IsBoundary(int vertex) => (_vertexFlags[vertex] & VertexFlags.Boundary) != 0;

    public bool IsIsolated(int vertex) => (_vertexFlags[vertex] & VertexFlags.Isolated) != 0;

    /// <summary>
    /// The one-ring neighbours of a vertex, as a slice of the shared adjacency array.
    /// </summary>
    /// <remarks>
    /// Stored in compressed sparse row form — one offsets array plus one flat data array — so a
    /// neighbourhood query is a contiguous read with no indirection and no per-vertex allocation.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> VertexNeighbours(int vertex)
    {
        int start = _neighbourOffsets[vertex];
        return _neighbourData.AsSpan(start, _neighbourOffsets[vertex + 1] - start);
    }

    /// <summary>
    /// Rotates one triangle around the vertex at <paramref name="corner"/>, in the direction of
    /// the triangle winding. Returns <c>-1</c> at the end of an open fan.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Swing(int corner)
    {
        int across = _opposite[Next(corner)];
        return across < 0 ? -1 : Next(across);
    }

    /// <summary>
    /// Rotates one triangle around the vertex at <paramref name="corner"/>, against the triangle
    /// winding. Returns <c>-1</c> at the end of an open fan.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Unswing(int corner)
    {
        int across = _opposite[Previous(corner)];
        return across < 0 ? -1 : Previous(across);
    }

    /// <summary>
    /// Writes the corners of the fan around the vertex at <paramref name="startCorner"/> into
    /// <paramref name="destination"/>, starting at <paramref name="startCorner"/> itself.
    /// </summary>
    /// <returns>
    /// The number of corners written, or <c>-1</c> when <paramref name="destination"/> is too
    /// small.
    /// </returns>
    public int GatherFan(int startCorner, Span<int> destination)
    {
        int count = 0;
        int corner = startCorner;

        do
        {
            if (count >= destination.Length || count >= MaxFanSize)
            {
                return -1;
            }

            destination[count++] = corner;
            corner = Swing(corner);
        }
        while (corner >= 0 && corner != startCorner);

        return count;
    }
}
