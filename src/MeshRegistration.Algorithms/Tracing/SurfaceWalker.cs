using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.Algorithms.Tracing;

/// <summary>How a single walk step ended.</summary>
public enum WalkStatus
{
    /// <summary>The step completed and landed inside a triangle.</summary>
    Landed,

    /// <summary>The step ran into a mesh border, or an edge repair cut open.</summary>
    HitBoundary,

    /// <summary>The step could not make progress within the crossing budget.</summary>
    Stuck,
}

/// <summary>Outcome of one walk step.</summary>
/// <param name="Point">Where the step ended.</param>
/// <param name="Direction">
/// The travel direction, parallel-transported into the plane of the triangle the step ended in.
/// </param>
/// <param name="DistanceTravelled">How far the step actually got, which is short of the request when a boundary intervened.</param>
/// <param name="Status">How the step ended.</param>
public readonly record struct WalkResult(
    SurfacePoint Point,
    Vec3 Direction,
    double DistanceTravelled,
    WalkStatus Status);

/// <summary>
/// Moves a point across the surface in a straight line, unfolding triangles as it crosses edges.
/// </summary>
/// <remarks>
/// <para>
/// The path this traces is a "straightest geodesic": within each triangle it is a straight
/// segment, and at each edge the direction is carried into the next triangle by the rotation that
/// unfolds one face onto the other. Arc length is preserved exactly, which is what makes samples
/// taken at a fixed step comparable between two meshes.
/// </para>
/// <para>
/// Two corrections relative to the previous implementation:
/// </para>
/// <list type="bullet">
///   <item>
///     The exit edge is the one hit <b>first</b> — the smallest positive ray parameter. The
///     previous code kept the <em>largest</em> valid parameter, which selects the far side of the
///     triangle whenever more than one edge test passes, sending the walk into the wrong
///     neighbour.
///   </item>
///   <item>
///     Crossings are bounded. The previous loop was <c>while (true)</c> with no escape, so a
///     configuration that bounced between two triangles hung.
///   </item>
/// </list>
/// <para>
/// The exit test works in barycentric coordinates. Barycentric weights are affine along a
/// straight path, so the ray leaves across the edge opposite vertex <c>i</c> exactly when weight
/// <c>i</c> reaches zero. That reduces "which edge do I cross, and where" to three linear
/// solves, with no separate line-line intersection to go singular.
/// </para>
/// </remarks>
public sealed class SurfaceWalker(TriangleMesh mesh, MeshTopology topology)
{
    /// <summary>
    /// Maximum triangles a single step may cross before the walk is declared stuck.
    /// </summary>
    /// <remarks>
    /// A step of about one edge length crosses one or two triangles; anything approaching this
    /// bound means the geometry or the direction has gone wrong.
    /// </remarks>
    private const int MaxCrossingsPerStep = 256;

    private readonly TriangleMesh _mesh = mesh;
    private readonly MeshTopology _topology = topology;

    /// <summary>
    /// Walks <paramref name="distance"/> from <paramref name="start"/> in
    /// <paramref name="direction"/>.
    /// </summary>
    /// <param name="start">Where to start.</param>
    /// <param name="direction">
    /// Travel direction. Need not lie exactly in the starting triangle's plane; it is projected.
    /// </param>
    /// <param name="distance">Arc length to cover.</param>
    public WalkResult Step(SurfacePoint start, Vec3 direction, double distance)
    {
        int triangle = start.Triangle;
        Vec3 position = start.Position(_mesh);

        Vec3 travel = ProjectIntoTriangle(direction, triangle);
        if (!travel.IsUsableDirection)
        {
            return new WalkResult(start, direction, 0, WalkStatus.Stuck);
        }

        travel = travel.Normalized();
        double remaining = distance;
        double travelled = 0;

        for (int crossing = 0; crossing < MaxCrossingsPerStep; crossing++)
        {
            if (!TryFindExit(triangle, position, travel, out int exitCorner, out double exitDistance))
            {
                // No edge lies ahead, so the whole remaining step fits inside this triangle.
                Vec3 landing = position + (travel * remaining);
                return new WalkResult(
                    SurfacePoint.FromPoint(_mesh, triangle, landing),
                    travel,
                    travelled + remaining,
                    WalkStatus.Landed);
            }

            if (remaining < exitDistance)
            {
                Vec3 landing = position + (travel * remaining);
                return new WalkResult(
                    SurfacePoint.FromPoint(_mesh, triangle, landing),
                    travel,
                    travelled + remaining,
                    WalkStatus.Landed);
            }

            position += travel * exitDistance;
            remaining -= exitDistance;
            travelled += exitDistance;

            int across = _topology.Opposite((3 * triangle) + exitCorner);
            if (across < 0)
            {
                // A border, or an edge cut while resolving a non-manifold configuration. Either
                // way the surface ends here.
                return new WalkResult(
                    SurfacePoint.FromPoint(_mesh, triangle, position),
                    travel,
                    travelled,
                    WalkStatus.HitBoundary);
            }

            int nextTriangle = MeshTopology.TriangleOf(across);
            travel = UnfoldAcrossEdge(travel, triangle, nextTriangle, exitCorner);

            if (!travel.IsUsableDirection)
            {
                return new WalkResult(
                    SurfacePoint.FromPoint(_mesh, triangle, position),
                    travel,
                    travelled,
                    WalkStatus.Stuck);
            }

            triangle = nextTriangle;
        }

        return new WalkResult(
            SurfacePoint.FromPoint(_mesh, triangle, position),
            travel,
            travelled,
            WalkStatus.Stuck);
    }

    /// <summary>
    /// Finds the edge the ray leaves through and how far away it is.
    /// </summary>
    /// <remarks>
    /// Barycentric weight <c>i</c> is an affine function along the ray, so it vanishes at
    /// <c>s = b[i] / -db[i]</c> whenever it is decreasing. The exit is the smallest such
    /// <c>s</c> — the first edge reached. Weights that are constant or increasing along the ray
    /// belong to edges the ray never crosses.
    /// </remarks>
    private bool TryFindExit(
        int triangle,
        Vec3 position,
        Vec3 direction,
        out int exitCorner,
        out double exitDistance)
    {
        SurfacePoint here = SurfacePoint.FromPoint(_mesh, triangle, position);
        SurfacePoint ahead = SurfacePoint.FromPoint(_mesh, triangle, position + direction);

        Span<double> weight = [here.U, here.V, here.W];
        Span<double> rate = [ahead.U - here.U, ahead.V - here.V, ahead.W - here.W];

        // The rates scale as 1 / triangle size, so the "is it really decreasing" test has to be
        // relative to them rather than an absolute epsilon.
        double largestRate = 0;
        for (int i = 0; i < 3; i++)
        {
            largestRate = Math.Max(largestRate, Math.Abs(rate[i]));
        }

        if (largestRate <= 0)
        {
            exitCorner = -1;
            exitDistance = 0;
            return false;
        }

        double decreasingThreshold = -1e-9 * largestRate;

        exitCorner = -1;
        exitDistance = double.PositiveInfinity;

        for (int i = 0; i < 3; i++)
        {
            if (rate[i] >= decreasingThreshold)
            {
                continue;
            }

            // Clamp a slightly negative weight — the walk sitting a hair outside the triangle
            // after a previous crossing — to zero rather than reporting negative distance.
            double distance = Math.Max(0, weight[i]) / -rate[i];

            if (distance < exitDistance)
            {
                exitDistance = distance;
                exitCorner = i;
            }
        }

        return exitCorner >= 0;
    }

    /// <summary>
    /// Carries a direction from one triangle into its neighbour across their shared edge.
    /// </summary>
    /// <remarks>
    /// Rotates about the shared edge by the signed dihedral angle between the two face normals.
    /// That single operation is exactly the unfolding: it preserves both the length of the
    /// direction and the angle it makes with the shared edge, with no case analysis. The previous
    /// implementation rebuilt the direction from the edge vector and an angle recovered with
    /// <c>acos</c>, and had to guess the sign.
    /// <para>
    /// Taking the axis from the edge rather than from the cross product of the normals matters
    /// for the folded-back case, where the two normals are antiparallel and their cross product
    /// gives no usable axis.
    /// </para>
    /// </remarks>
    private Vec3 UnfoldAcrossEdge(Vec3 direction, int fromTriangle, int toTriangle, int exitCorner)
    {
        Triangle face = _mesh.Face(fromTriangle);
        Vec3 edgeStart = _mesh.Position(face[(exitCorner + 1) % 3]);
        Vec3 edgeEnd = _mesh.Position(face[(exitCorner + 2) % 3]);

        Vec3 axis = (edgeEnd - edgeStart).Normalized();
        if (!axis.IsUsableDirection)
        {
            return ProjectIntoTriangle(direction, toTriangle).Normalized();
        }

        Vec3 fromNormal = _mesh.FaceNormals[fromTriangle];
        Vec3 toNormal = _mesh.FaceNormals[toTriangle];

        double dihedral = Vec3.SignedAngle(fromNormal, toNormal, axis);
        Vec3 rotated = Vec3.Rotate(direction, axis, dihedral);

        // Re-project and renormalise so that round-off cannot accumulate across a long walk.
        return ProjectIntoTriangle(rotated, toTriangle).Normalized();
    }

    /// <summary>Removes the component of a vector normal to a triangle.</summary>
    private Vec3 ProjectIntoTriangle(Vec3 v, int triangle)
    {
        Vec3 normal = _mesh.FaceNormals[triangle];
        return v - (normal * v.Dot(normal));
    }
}
