using System.Runtime.CompilerServices;
using MeshRegistration.Core.Geometry;

namespace MeshRegistration.Core.Mesh;

/// <summary>
/// An immutable triangle mesh together with the per-vertex quantities every downstream stage
/// needs: normals, areas, and the two characteristic lengths that make thresholds scale free.
/// </summary>
/// <remarks>
/// <para>
/// Vertex data is stored array-of-structs (<see cref="Vec3"/><c>[]</c>) rather than as three
/// parallel coordinate arrays. Both the curvature estimator and the line tracer access vertices
/// through scattered neighbour indices, so all three coordinates of one vertex are wanted
/// together and arrive on a single cache line. A struct-of-arrays layout would only pay off for
/// streaming passes, which here are bandwidth bound anyway.
/// </para>
/// <para>
/// The mesh is immutable by construction. The previous implementation let the line tracer append
/// visualisation points to the very mesh it was walking, which desynchronised the vertex array
/// from the per-vertex weight array. Visualisation geometry is now built by the exporters, from
/// a separate copy.
/// </para>
/// </remarks>
public sealed class TriangleMesh
{
    private readonly Vec3[] _positions;
    private readonly Triangle[] _triangles;
    private readonly Vec3[] _vertexNormals;
    private readonly Vec3[] _faceNormals;
    private readonly double[] _vertexAreas;

    /// <summary>
    /// Wraps vertex positions and triangles, computing normals, areas and the characteristic
    /// lengths in a single pass.
    /// </summary>
    /// <param name="positions">Vertex positions. Taken by reference; must not be mutated afterwards.</param>
    /// <param name="triangles">Triangles. Taken by reference; must not be mutated afterwards.</param>
    /// <exception cref="ArgumentException">The vertex array is empty.</exception>
    /// <remarks>
    /// Expects a repaired, consistently oriented mesh — normally the output of
    /// <see cref="MeshBuilder.Build"/>. Constructing one directly is supported for tests, but
    /// nothing here checks or fixes topology.
    /// </remarks>
    public TriangleMesh(Vec3[] positions, Triangle[] triangles)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(triangles);

        if (positions.Length == 0)
        {
            throw new ArgumentException("A mesh must have at least one vertex.", nameof(positions));
        }

        _positions = positions;
        _triangles = triangles;

        Bounds = BoundingBox.FromPoints(positions);

        _faceNormals = new Vec3[triangles.Length];
        _vertexNormals = new Vec3[positions.Length];
        _vertexAreas = new double[positions.Length];

        AverageEdgeLength = ComputeDerivedQuantities();
    }

    public ReadOnlySpan<Vec3> Positions => _positions;

    public ReadOnlySpan<Triangle> Triangles => _triangles;

    /// <summary>Unit vertex normals, angle-weighted from the incident face normals.</summary>
    public ReadOnlySpan<Vec3> VertexNormals => _vertexNormals;

    /// <summary>Unit face normals. Zero for degenerate faces.</summary>
    public ReadOnlySpan<Vec3> FaceNormals => _faceNormals;

    /// <summary>
    /// Barycentric vertex areas: each vertex receives one third of the area of every incident
    /// triangle. Used as the base weight of the curvature least-squares fit.
    /// </summary>
    public ReadOnlySpan<double> VertexAreas => _vertexAreas;

    public int VertexCount => _positions.Length;

    public int TriangleCount => _triangles.Length;

    public BoundingBox Bounds { get; }

    /// <summary>
    /// Mean triangle edge length — the mesh's sampling resolution, and the unit in which
    /// neighbourhood radii and tracing steps are expressed.
    /// </summary>
    public double AverageEdgeLength { get; }

    /// <summary>
    /// The model's overall size, and the unit in which line lengths and welding tolerances are
    /// expressed.
    /// </summary>
    public double DiagonalLength => Bounds.DiagonalLength;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3 Position(int vertex) => _positions[vertex];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Triangle Face(int triangle) => _triangles[triangle];

    /// <summary>Returns the three corner positions of a triangle.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (Vec3 P0, Vec3 P1, Vec3 P2) FaceVertices(int triangle)
    {
        Triangle t = _triangles[triangle];
        return (_positions[t.V0], _positions[t.V1], _positions[t.V2]);
    }

    /// <summary>Area of a single triangle.</summary>
    public double FaceArea(int triangle)
    {
        (Vec3 p0, Vec3 p1, Vec3 p2) = FaceVertices(triangle);
        return 0.5 * (p1 - p0).Cross(p2 - p0).Length;
    }

    /// <summary>
    /// Computes face normals, angle-weighted vertex normals, barycentric vertex areas and the
    /// mean edge length in a single pass over the faces.
    /// </summary>
    /// <returns>The mean edge length.</returns>
    private double ComputeDerivedQuantities()
    {
        double edgeLengthSum = 0;
        int edgeCount = 0;

        for (int f = 0; f < _triangles.Length; f++)
        {
            Triangle t = _triangles[f];
            Vec3 p0 = _positions[t.V0];
            Vec3 p1 = _positions[t.V1];
            Vec3 p2 = _positions[t.V2];

            Vec3 e0 = p1 - p0;
            Vec3 e1 = p2 - p1;
            Vec3 e2 = p0 - p2;

            edgeLengthSum += e0.Length + e1.Length + e2.Length;
            edgeCount += 3;

            Vec3 doubleAreaNormal = e0.Cross(p2 - p0);
            double doubleArea = doubleAreaNormal.Length;

            if (doubleArea <= 0 || !double.IsFinite(doubleArea))
            {
                // Degenerate face: no normal, no area contribution. Repair normally removes
                // these beforehand, but a mesh may be constructed directly in tests.
                _faceNormals[f] = Vec3.Zero;
                continue;
            }

            Vec3 faceNormal = doubleAreaNormal / doubleArea;
            _faceNormals[f] = faceNormal;

            double thirdOfArea = doubleArea / 6.0;
            _vertexAreas[t.V0] += thirdOfArea;
            _vertexAreas[t.V1] += thirdOfArea;
            _vertexAreas[t.V2] += thirdOfArea;

            // Angle weighting (Thurmer & Wuthrich). Unlike uniform or area weighting it is
            // insensitive to how a neighbourhood happens to be triangulated, which matters here
            // because the curvature fit differences these normals against each other.
            double angle0 = Vec3.AngleBetween(p1 - p0, p2 - p0);
            double angle1 = Vec3.AngleBetween(p2 - p1, p0 - p1);
            double angle2 = Vec3.AngleBetween(p0 - p2, p1 - p2);

            _vertexNormals[t.V0] += faceNormal * angle0;
            _vertexNormals[t.V1] += faceNormal * angle1;
            _vertexNormals[t.V2] += faceNormal * angle2;
        }

        for (int v = 0; v < _vertexNormals.Length; v++)
        {
            Vec3 normal = _vertexNormals[v];

            // Isolated vertices and vertices surrounded only by degenerate faces have no defined
            // normal. A deterministic unit vector keeps every downstream computation finite; such
            // vertices are flagged in the topology and excluded from curvature and seeding.
            _vertexNormals[v] = normal.IsUsableDirection ? normal.Normalized() : Vec3.UnitZ;
        }

        return edgeCount > 0 ? edgeLengthSum / edgeCount : 0;
    }
}
