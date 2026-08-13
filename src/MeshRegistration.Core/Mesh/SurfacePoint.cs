using System.Runtime.CompilerServices;
using MeshRegistration.Core.Geometry;

namespace MeshRegistration.Core.Mesh;

/// <summary>
/// A point anywhere on the surface, given by a triangle and barycentric coordinates.
/// </summary>
/// <param name="Triangle">Index of the containing triangle.</param>
/// <param name="U">Weight of the triangle's first vertex.</param>
/// <param name="V">Weight of the triangle's second vertex.</param>
/// <remarks>
/// The convention is <c>P = U·V0 + V·V1 + (1 − U − V)·V2</c>; the third weight is implicit.
/// </remarks>
public readonly record struct SurfacePoint(int Triangle, double U, double V)
{
    /// <summary>The implicit third barycentric weight.</summary>
    public double W
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 1.0 - U - V;
    }

    /// <summary>True when the coordinates lie inside the triangle, within a small tolerance.</summary>
    public bool IsInsideTriangle(double tolerance = 1e-9) =>
        U >= -tolerance && V >= -tolerance && W >= -tolerance;

    /// <summary>Interpolates the three corner positions.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3 Position(TriangleMesh mesh)
    {
        (Vec3 p0, Vec3 p1, Vec3 p2) = mesh.FaceVertices(Triangle);
        return (p0 * U) + (p1 * V) + (p2 * W);
    }

    /// <summary>
    /// Interpolates the three corner normals and renormalises, falling back to the flat face
    /// normal when the blend cancels out.
    /// </summary>
    public Vec3 Normal(TriangleMesh mesh)
    {
        Core.Mesh.Triangle t = mesh.Face(Triangle);
        ReadOnlySpan<Vec3> normals = mesh.VertexNormals;

        Vec3 blended = (normals[t.V0] * U) + (normals[t.V1] * V) + (normals[t.V2] * W);
        return blended.IsUsableDirection ? blended.Normalized() : mesh.FaceNormals[Triangle];
    }

    /// <summary>
    /// Computes the barycentric coordinates of an arbitrary point with respect to a triangle.
    /// </summary>
    /// <remarks>
    /// Closed form via the Gram matrix of the two triangle edges — about twenty floating point
    /// operations. The previous implementation assembled a 4x3 matrix and called a general
    /// least-squares solver for this, once per tracing step, which was both far slower and less
    /// accurate than the direct formula.
    /// <para>
    /// Points off the triangle plane are projected onto it, which is exactly what the tracer
    /// needs when it lands a step slightly off-plane.
    /// </para>
    /// </remarks>
    public static SurfacePoint FromPoint(TriangleMesh mesh, int triangle, Vec3 point)
    {
        (Vec3 p0, Vec3 p1, Vec3 p2) = mesh.FaceVertices(triangle);

        Vec3 edge1 = p1 - p0;
        Vec3 edge2 = p2 - p0;
        Vec3 offset = point - p0;

        double d11 = edge1.Dot(edge1);
        double d12 = edge1.Dot(edge2);
        double d22 = edge2.Dot(edge2);
        double dp1 = offset.Dot(edge1);
        double dp2 = offset.Dot(edge2);

        double determinant = (d11 * d22) - (d12 * d12);

        // A zero determinant means the triangle is degenerate and has no interior to coordinatise.
        if (determinant == 0 || !double.IsFinite(determinant))
        {
            return new SurfacePoint(triangle, 1.0 / 3.0, 1.0 / 3.0);
        }

        double inverse = 1.0 / determinant;
        double v = ((d22 * dp1) - (d12 * dp2)) * inverse;
        double w = ((d11 * dp2) - (d12 * dp1)) * inverse;

        // v and w weight p1 and p2; this type's U weights p0.
        return new SurfacePoint(triangle, 1.0 - v - w, v);
    }
}
