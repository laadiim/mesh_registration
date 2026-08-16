using MeshRegistration.Core.Geometry;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.Algorithms.Tests;

/// <summary>
/// Thin adapter over <see cref="AnalyticShapes"/> for the tests.
/// </summary>
/// <remarks>
/// The generators live in the library rather than here, so that the surfaces the tests verify
/// against and the surfaces <c>meshreg --shape</c> draws are literally the same code. This type
/// only unpacks the result into the tuple shape the tests were written around.
/// </remarks>
internal static class AnalyticSurfaces
{
    public static (Vec3[] Positions, Triangle[] Triangles) Plane(double size, int divisions) =>
        Unpack(AnalyticShapes.Plane(size, divisions));

    public static (Vec3[] Positions, Triangle[] Triangles) Sphere(double radius, int subdivisions) =>
        Unpack(AnalyticShapes.Sphere(radius, subdivisions));

    public static (Vec3[] Positions, Triangle[] Triangles) Cylinder(
        double radius, double height, int around, int along) =>
        Unpack(AnalyticShapes.Cylinder(radius, height, around, along));

    public static (Vec3[] Positions, Triangle[] Triangles) Torus(
        double major, double minor, int around, int through) =>
        Unpack(AnalyticShapes.Torus(major, minor, around, through));

    public static (Vec3[] Positions, Triangle[] Triangles) Quadric(
        double a, double c, double size, int divisions) =>
        Unpack(AnalyticShapes.Quadric(a, c, size, divisions));

    public static (Vec3[] Positions, Triangle[] Triangles) ConcentricWaves(
        double amplitude, double wavelength, double size, int divisions) =>
        Unpack(AnalyticShapes.ConcentricWaves(amplitude, wavelength, size, divisions));

    private static (Vec3[] Positions, Triangle[] Triangles) Unpack(AnalyticShapes.Mesh mesh) =>
        (mesh.Positions, mesh.Triangles);
}
