using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.Algorithms.Tests;

/// <summary>
/// Generators for surfaces whose curvature is known in closed form.
/// </summary>
/// <remarks>
/// These are the ground truth for the curvature estimator. The plane and the sphere are the two
/// that matter most: both are entirely umbilic, and both made the previous implementation emit
/// NaN principal directions.
/// <para>
/// Grid surfaces are wound so that the parametric normal <c>∂P/∂u × ∂P/∂v</c> points outward,
/// which under the convention "shape operator = dN" makes a convex surface positively curved.
/// </para>
/// </remarks>
internal static class AnalyticSurfaces
{
    /// <summary>A flat rectangle in the z = 0 plane. Both principal curvatures are zero.</summary>
    public static (Vec3[] Positions, Triangle[] Triangles) Plane(double size, int divisions) =>
        Grid(
            divisions,
            divisions,
            (u, v) => new Vec3((u - 0.5) * size, (v - 0.5) * size, 0),
            wrapU: false,
            wrapV: false);

    /// <summary>
    /// A sphere built by subdividing an icosahedron. Every point is umbilic with
    /// <c>kMin == kMax == 1 / radius</c>.
    /// </summary>
    /// <remarks>
    /// An icosphere rather than a latitude/longitude sphere: the latter has degenerate triangles
    /// and wildly varying vertex density at the poles, which would confound a curvature test with
    /// a tessellation artefact.
    /// </remarks>
    public static (Vec3[] Positions, Triangle[] Triangles) Sphere(double radius, int subdivisions)
    {
        const double phi = 1.618033988749895; // golden ratio

        List<Vec3> positions =
        [
            new(-1, phi, 0), new(1, phi, 0), new(-1, -phi, 0), new(1, -phi, 0),
            new(0, -1, phi), new(0, 1, phi), new(0, -1, -phi), new(0, 1, -phi),
            new(phi, 0, -1), new(phi, 0, 1), new(-phi, 0, -1), new(-phi, 0, 1),
        ];

        List<Triangle> triangles =
        [
            new(0, 11, 5), new(0, 5, 1), new(0, 1, 7), new(0, 7, 10), new(0, 10, 11),
            new(1, 5, 9), new(5, 11, 4), new(11, 10, 2), new(10, 7, 6), new(7, 1, 8),
            new(3, 9, 4), new(3, 4, 2), new(3, 2, 6), new(3, 6, 8), new(3, 8, 9),
            new(4, 9, 5), new(2, 4, 11), new(6, 2, 10), new(8, 6, 7), new(9, 8, 1),
        ];

        for (int level = 0; level < subdivisions; level++)
        {
            Dictionary<(int, int), int> midpoints = [];
            List<Triangle> refined = new(triangles.Count * 4);

            foreach (Triangle t in triangles)
            {
                int a = MidPoint(positions, midpoints, t.V0, t.V1);
                int b = MidPoint(positions, midpoints, t.V1, t.V2);
                int c = MidPoint(positions, midpoints, t.V2, t.V0);

                refined.Add(new Triangle(t.V0, a, c));
                refined.Add(new Triangle(t.V1, b, a));
                refined.Add(new Triangle(t.V2, c, b));
                refined.Add(new Triangle(a, b, c));
            }

            triangles = refined;
        }

        Vec3[] scaled = new Vec3[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            scaled[i] = positions[i].Normalized() * radius;
        }

        return (scaled, [.. triangles]);

        static int MidPoint(List<Vec3> positions, Dictionary<(int, int), int> cache, int i, int j)
        {
            (int, int) key = i < j ? (i, j) : (j, i);
            if (cache.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int index = positions.Count;
            positions.Add(((positions[i] + positions[j]) * 0.5).Normalized());
            cache[key] = index;
            return index;
        }
    }

    /// <summary>
    /// An open cylinder about the z axis. <c>kMax == 1 / radius</c> circumferentially and
    /// <c>kMin == 0</c> along the axis, so it is the reference case for principal
    /// <b>directions</b>: unlike the sphere it is strongly anisotropic everywhere.
    /// </summary>
    public static (Vec3[] Positions, Triangle[] Triangles) Cylinder(
        double radius,
        double height,
        int around,
        int along) =>
        Grid(
            around,
            along,
            (u, v) =>
            {
                double angle = u * 2 * Math.PI;
                return new Vec3(radius * Math.Cos(angle), radius * Math.Sin(angle), (v - 0.5) * height);
            },
            wrapU: true,
            wrapV: false);

    /// <summary>
    /// A torus of major radius <paramref name="major"/> and minor radius
    /// <paramref name="minor"/>. Curvature varies over the surface, so it tests that the
    /// estimator tracks a changing shape operator rather than a constant one.
    /// </summary>
    /// <remarks>
    /// At the point with tube angle θ the principal curvatures are <c>1 / minor</c> and
    /// <c>cos θ / (major + minor·cos θ)</c>.
    /// </remarks>
    public static (Vec3[] Positions, Triangle[] Triangles) Torus(
        double major,
        double minor,
        int around,
        int through) =>
        Grid(
            around,
            through,
            (u, v) =>
            {
                double bigAngle = u * 2 * Math.PI;
                double smallAngle = v * 2 * Math.PI;
                double ring = major + (minor * Math.Cos(smallAngle));
                return new Vec3(
                    ring * Math.Cos(bigAngle),
                    ring * Math.Sin(bigAngle),
                    minor * Math.Sin(smallAngle));
            },
            wrapU: true,
            wrapV: true);

    /// <summary>
    /// The quadric <c>z = (a·x² + c·y²) / 2</c>. At the origin, with the upward normal, the
    /// principal curvatures are <c>-a</c> and <c>-c</c>.
    /// </summary>
    /// <remarks>
    /// Opposite signs give a saddle, which is maximally anisotropic and therefore must never be
    /// classified umbilic.
    /// </remarks>
    public static (Vec3[] Positions, Triangle[] Triangles) Quadric(
        double a,
        double c,
        double size,
        int divisions) =>
        Grid(
            divisions,
            divisions,
            (u, v) =>
            {
                double x = (u - 0.5) * size;
                double y = (v - 0.5) * size;
                return new Vec3(x, y, ((a * x * x) + (c * y * y)) * 0.5);
            },
            wrapU: false,
            wrapV: false);

    /// <summary>
    /// Tessellates a parametric patch over the unit square, optionally wrapping in either
    /// direction.
    /// </summary>
    private static (Vec3[] Positions, Triangle[] Triangles) Grid(
        int stepsU,
        int stepsV,
        Func<double, double, Vec3> surface,
        bool wrapU,
        bool wrapV)
    {
        int countU = wrapU ? stepsU : stepsU + 1;
        int countV = wrapV ? stepsV : stepsV + 1;

        Vec3[] positions = new Vec3[countU * countV];
        for (int i = 0; i < countU; i++)
        {
            for (int j = 0; j < countV; j++)
            {
                positions[(i * countV) + j] = surface((double)i / stepsU, (double)j / stepsV);
            }
        }

        // Either way there are stepsU by stepsV quads; wrapping only changes whether the last
        // row of quads closes back onto index 0 or onto a distinct final row of vertices.
        List<Triangle> triangles = new(stepsU * stepsV * 2);

        for (int i = 0; i < stepsU; i++)
        {
            for (int j = 0; j < stepsV; j++)
            {
                int i1 = (i + 1) % countU;
                int j1 = (j + 1) % countV;

                int v00 = (i * countV) + j;
                int v10 = (i1 * countV) + j;
                int v11 = (i1 * countV) + j1;
                int v01 = (i * countV) + j1;

                // Wound so the face normal agrees with dP/du x dP/dv.
                triangles.Add(new Triangle(v00, v10, v11));
                triangles.Add(new Triangle(v00, v11, v01));
            }
        }

        return (positions, [.. triangles]);
    }
}
