using MeshRegistration.Algorithms.Curvature;
using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;
using Xunit;

namespace MeshRegistration.Algorithms.Tests;

/// <summary>
/// Validates the curvature estimator against surfaces with closed-form curvature.
/// </summary>
public sealed class CurvatureTests
{
    /// <summary>
    /// Builds a mesh and its curvature field, and returns the samples at vertices away from any
    /// boundary — boundary vertices have one-sided neighbourhoods and are legitimately less
    /// accurate.
    /// </summary>
    private static (ShapeOperatorField Field, MeshBuildResult Build, List<int> InteriorVertices) Analyse(
        (Vec3[] Positions, Triangle[] Triangles) surface,
        CurvatureOptions? options = null)
    {
        MeshBuildResult build = MeshBuilder.Build(surface.Positions, surface.Triangles);
        ShapeOperatorField field = ShapeOperatorField.Compute(build.Mesh, build.Topology, options);

        List<int> interior = [];
        for (int v = 0; v < build.Mesh.VertexCount; v++)
        {
            if (!build.Topology.IsBoundary(v) && !build.Topology.IsIsolated(v))
            {
                interior.Add(v);
            }
        }

        return (field, build, interior);
    }

    // ---------------------------------------------------------------------------------------
    // The two regression cases. Both are entirely umbilic, and both produced NaN principal
    // directions in the previous implementation: for A == C and B == 0 its eigenvector branch
    // evaluated (lambda - A) / B, i.e. 0 / 0.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Plane_IsFlatAndFinite()
    {
        (ShapeOperatorField field, _, List<int> interior) = Analyse(AnalyticSurfaces.Plane(1.0, 40));

        Assert.NotEmpty(interior);

        foreach (int v in interior)
        {
            CurvatureSample sample = field.AtVertex(v);

            Assert.True(sample.IsFinite, $"Vertex {v} produced a non-finite curvature sample.");
            Assert.True(sample.IsPlanar, $"Vertex {v} on a plane was not classified planar.");
            Assert.True(sample.IsUmbilic, $"Vertex {v} on a plane was not classified umbilic.");
            Assert.False(sample.HasUsableDirection, $"Vertex {v} on a plane offered a principal direction.");

            Assert.Equal(0.0, sample.KMin, 6);
            Assert.Equal(0.0, sample.KMax, 6);
        }
    }

    [Fact]
    public void Sphere_HasUniformCurvatureAndIsUmbilic()
    {
        const double radius = 2.5;
        const double expected = 1.0 / radius;

        (ShapeOperatorField field, _, List<int> interior) = Analyse(AnalyticSurfaces.Sphere(radius, 4));

        Assert.NotEmpty(interior);

        foreach (int v in interior)
        {
            CurvatureSample sample = field.AtVertex(v);

            Assert.True(sample.IsFinite, $"Vertex {v} produced a non-finite curvature sample.");

            // The sign is positive because the builder orients closed components outward and the
            // shape operator convention is dN, under which convex means positive.
            Assert.Equal(expected, sample.KMin, expected * 0.05);
            Assert.Equal(expected, sample.KMax, expected * 0.05);

            Assert.True(sample.IsUmbilic, $"Vertex {v} on a sphere was not classified umbilic.");
            Assert.False(sample.IsPlanar, $"Vertex {v} on a sphere was wrongly classified planar.");
            Assert.False(sample.HasUsableDirection, $"Vertex {v} on a sphere offered a principal direction.");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Non-degenerate references: these must produce usable directions.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Cylinder_HasOneZeroCurvatureAndAxisAlignedDirections()
    {
        const double radius = 1.0;
        const double expected = 1.0 / radius;

        (ShapeOperatorField field, _, List<int> interior) =
            Analyse(AnalyticSurfaces.Cylinder(radius, 4.0, around: 120, along: 60));

        Assert.NotEmpty(interior);
        Vec3 axis = Vec3.UnitZ;

        foreach (int v in interior)
        {
            CurvatureSample sample = field.AtVertex(v);

            Assert.True(sample.IsFinite, $"Vertex {v} produced a non-finite curvature sample.");
            Assert.Equal(expected, sample.KMax, expected * 0.05);
            Assert.Equal(0.0, sample.KMin, expected * 0.05);

            // Strongly anisotropic, so the direction must be offered, not suppressed.
            Assert.True(sample.HasUsableDirection, $"Vertex {v} on a cylinder had no usable direction.");
            Assert.False(sample.IsUmbilic);

            // Maximum curvature runs around the tube, minimum along the axis.
            Assert.Equal(0.0, Math.Abs(sample.DirMax.Dot(axis)), 0.02);
            Assert.Equal(1.0, Math.Abs(sample.DirMin.Dot(axis)), 0.02);
        }
    }

    [Fact]
    public void Torus_MatchesClosedFormCurvature()
    {
        const double major = 3.0;
        const double minor = 1.0;

        (ShapeOperatorField field, MeshBuildResult build, List<int> interior) =
            Analyse(AnalyticSurfaces.Torus(major, minor, around: 160, through: 80));

        Assert.NotEmpty(interior);

        foreach (int v in interior)
        {
            CurvatureSample sample = field.AtVertex(v);
            Assert.True(sample.IsFinite, $"Vertex {v} produced a non-finite curvature sample.");

            Vec3 p = build.Mesh.Position(v);

            // Recover the tube angle: the distance from the z axis to the tube centre circle.
            double distanceFromAxis = Math.Sqrt((p.X * p.X) + (p.Y * p.Y));
            double cosTheta = (distanceFromAxis - major) / minor;
            double sinTheta = p.Z / minor;
            double theta = Math.Atan2(sinTheta, cosTheta);

            double expectedTube = 1.0 / minor;
            double expectedRing = Math.Cos(theta) / (major + (minor * Math.Cos(theta)));

            double expectedMax = Math.Max(expectedTube, expectedRing);
            double expectedMin = Math.Min(expectedTube, expectedRing);

            // Looser tolerance than the constant-curvature cases: the fit averages over a
            // neighbourhood across which the true curvature genuinely varies.
            Assert.Equal(expectedMax, sample.KMax, 0.06);
            Assert.Equal(expectedMin, sample.KMin, 0.06);
        }
    }

    [Fact]
    public void Saddle_IsAnisotropicWithOppositeSigns()
    {
        // z = (a x^2 + c y^2) / 2 with a and c of opposite sign. With the upward normal the
        // principal curvatures at the origin are -a and -c.
        const double a = 1.0;
        const double c = -1.0;

        // Unlike the sphere, cylinder and torus, this surface's curvature varies rapidly: along
        // the x axis it falls off as (1 + x^2)^(-3/2). The estimator averages over its
        // neighbourhood, so the fitted value is biased towards the neighbourhood's mean curvature
        // rather than the value at the centre. A narrower neighbourhood keeps that bias below the
        // tolerance; widening it back to the default would legitimately read about 7% low.
        CurvatureOptions options = new() { NeighbourhoodWidth = 3.0 };

        (ShapeOperatorField field, MeshBuildResult build, List<int> interior) =
            Analyse(AnalyticSurfaces.Quadric(a, c, size: 2.0, divisions: 100), options);

        // Only near the origin do the closed-form values hold.
        List<int> nearOrigin = interior
            .Where(v => build.Mesh.Position(v).LengthSquared < 0.01)
            .ToList();

        Assert.NotEmpty(nearOrigin);

        foreach (int v in nearOrigin)
        {
            CurvatureSample sample = field.AtVertex(v);

            Assert.True(sample.IsFinite);
            Assert.Equal(-c, sample.KMax, 0.03);
            Assert.Equal(-a, sample.KMin, 0.03);

            Assert.True(sample.HasUsableDirection, $"Vertex {v} on a saddle had no usable direction.");
            Assert.False(sample.IsUmbilic, $"Vertex {v} on a saddle was wrongly classified umbilic.");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Interpolation off the vertices.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void SurfacePointEvaluation_MatchesVertexValuesOnASphere()
    {
        const double radius = 2.0;
        const double expected = 1.0 / radius;

        (ShapeOperatorField field, MeshBuildResult build, _) = Analyse(AnalyticSurfaces.Sphere(radius, 4));

        // Sample the interior of every hundredth triangle at its centroid.
        for (int t = 0; t < build.Mesh.TriangleCount; t += 100)
        {
            SurfacePoint point = new(t, 1.0 / 3.0, 1.0 / 3.0);
            CurvatureSample sample = field.AtSurfacePoint(point);

            Assert.True(sample.IsFinite, $"Triangle {t} centroid produced a non-finite sample.");
            Assert.Equal(expected, sample.KMax, expected * 0.05);
            Assert.Equal(expected, sample.KMin, expected * 0.05);
            Assert.True(sample.IsUmbilic);
        }
    }

    [Fact]
    public void SurfacePointEvaluation_MatchesVertexValuesOnACylinder()
    {
        const double radius = 1.5;
        const double expected = 1.0 / radius;

        (ShapeOperatorField field, MeshBuildResult build, _) =
            Analyse(AnalyticSurfaces.Cylinder(radius, 3.0, around: 120, along: 60));

        int checkedCount = 0;

        for (int t = 0; t < build.Mesh.TriangleCount; t += 97)
        {
            Triangle triangle = build.Mesh.Face(t);
            if (build.Topology.IsBoundary(triangle.V0) ||
                build.Topology.IsBoundary(triangle.V1) ||
                build.Topology.IsBoundary(triangle.V2))
            {
                continue;
            }

            CurvatureSample sample = field.AtSurfacePoint(new SurfacePoint(t, 1.0 / 3.0, 1.0 / 3.0));

            Assert.True(sample.IsFinite);
            Assert.Equal(expected, sample.KMax, expected * 0.06);
            Assert.Equal(0.0, sample.KMin, expected * 0.06);
            Assert.True(sample.HasUsableDirection);
            checkedCount++;
        }

        Assert.True(checkedCount > 10, "Too few interior triangles were sampled to be meaningful.");
    }
}
