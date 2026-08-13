using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.Algorithms.Tracing;

/// <summary>
/// Picks the points to grow curvature lines from.
/// </summary>
/// <remarks>
/// <para>
/// Two rules. A seed must sit where the principal direction actually exists — never on a flat or
/// spherical patch, an unusable fit, or a boundary — and seeds must be spread across the model
/// rather than clustered on its single most anisotropic feature.
/// </para>
/// <para>
/// The previous implementation seeded uniformly at random over triangles. On a scan that is
/// mostly smooth, most of those seeds land where the direction field is undefined, so most lines
/// were meaningless from their very first step.
/// </para>
/// <para>
/// Selection is deterministic: candidates are ranked by anisotropy weighted by fit confidence,
/// with the vertex index breaking ties, and accepted greedily subject to a spacing constraint.
/// No random number generator is involved, so repeated runs agree exactly.
/// </para>
/// </remarks>
public static class SeedSelector
{
    /// <summary>Chooses seed points on <paramref name="mesh"/>.</summary>
    public static List<SurfacePoint> Select(
        TriangleMesh mesh,
        MeshTopology topology,
        ShapeOperatorField curvature,
        TracingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(curvature);
        options ??= new TracingOptions();

        List<(double Score, int Vertex)> candidates = [];

        for (int v = 0; v < mesh.VertexCount; v++)
        {
            CurvatureSample sample = curvature.AtVertex(v);

            // A seed needs a direction to start along, so umbilic, planar, unusable and boundary
            // vertices are all excluded.
            if (!sample.HasUsableDirection || topology.IsBoundary(v) || topology.IsIsolated(v))
            {
                continue;
            }

            // Rank by how far the point is from umbilic, discounted by how much the fit is worth
            // trusting. Made dimensionless by the neighbourhood radius so the ranking means the
            // same thing on any model.
            double anisotropy = sample.CurvatureDeviation * curvature.NeighbourhoodRadius;
            double score = anisotropy * sample.Confidence;

            if (score > 0)
            {
                candidates.Add((score, v));
            }
        }

        // Descending by score; vertex index breaks ties so the order is total and reproducible.
        candidates.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : a.Vertex.CompareTo(b.Vertex);
        });

        double spacing = mesh.DiagonalLength * options.SeedSpacing;
        double spacingSquared = spacing * spacing;

        List<SurfacePoint> seeds = [];
        List<Vec3> acceptedPositions = [];

        foreach ((double _, int vertex) in candidates)
        {
            if (seeds.Count >= options.MaxLines)
            {
                break;
            }

            Vec3 position = mesh.Position(vertex);

            bool tooClose = false;
            foreach (Vec3 accepted in acceptedPositions)
            {
                if (accepted.DistanceSquaredTo(position) < spacingSquared)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
            {
                continue;
            }

            if (TryPlaceOnSurface(mesh, topology, vertex, out SurfacePoint seed))
            {
                seeds.Add(seed);
                acceptedPositions.Add(position);
            }
        }

        return seeds;
    }

    /// <summary>
    /// Converts a vertex into a surface point placed just inside one of its incident triangles.
    /// </summary>
    /// <remarks>
    /// Seeding exactly on a vertex would put the walk on a triangle corner, where the exit test
    /// is ambiguous — several barycentric weights are zero at once. Nudging the seed towards the
    /// triangle's centroid removes the ambiguity while moving it by a fraction of one edge.
    /// </remarks>
    private static bool TryPlaceOnSurface(
        TriangleMesh mesh,
        MeshTopology topology,
        int vertex,
        out SurfacePoint seed)
    {
        int corner = topology.IncidentCorner(vertex);
        if (corner < 0)
        {
            seed = default;
            return false;
        }

        const double towardsCentroid = 0.05;

        int triangle = MeshTopology.TriangleOf(corner);
        int localIndex = corner % 3;

        // Barycentric weights: mostly the seed vertex, with a little of the other two.
        Span<double> weights = [towardsCentroid, towardsCentroid, towardsCentroid];
        weights[localIndex] = 1.0 - (2 * towardsCentroid);

        seed = new SurfacePoint(triangle, weights[0], weights[1]);
        return true;
    }
}
