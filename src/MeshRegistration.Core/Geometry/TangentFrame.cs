using System.Runtime.CompilerServices;

namespace MeshRegistration.Core.Geometry;

/// <summary>
/// A right-handed orthonormal frame <c>(E1, E2, Normal)</c> spanning the tangent plane at a
/// surface point.
/// </summary>
/// <remarks>
/// The curvature estimator expresses every neighbour offset and every neighbour normal in this
/// basis, so that the 3D fitting problem collapses to a 2x2 symmetric shape operator.
/// <para>
/// The choice of <see cref="E1"/> within the tangent plane is arbitrary — curvature values are
/// invariant to it, and principal directions are recovered as an angle measured from it. What
/// matters is that the construction is <em>total</em>: it must never produce a NaN or a
/// zero-length axis, whatever normal it is handed.
/// </para>
/// </remarks>
public readonly record struct TangentFrame(Vec3 E1, Vec3 E2, Vec3 Normal)
{
    /// <summary>
    /// Builds an orthonormal frame around a unit <paramref name="normal"/>, choosing the tangent
    /// axes deterministically.
    /// </summary>
    public static TangentFrame FromNormal(Vec3 normal)
    {
        Vec3 e1 = Vec3.OrthogonalTo(normal);
        Vec3 e2 = normal.Cross(e1);
        return new TangentFrame(e1, e2, normal);
    }

    /// <summary>
    /// Builds an orthonormal frame around a unit <paramref name="normal"/>, aligning
    /// <see cref="E1"/> as closely as possible with <paramref name="preferredTangent"/>.
    /// </summary>
    /// <remarks>
    /// Used when a frame must stay coherent with a neighbouring one — for example when
    /// transporting shape operators between vertices — so that the induced 2x2 rotation stays
    /// small and well conditioned. Falls back to the arbitrary construction when the preferred
    /// tangent is parallel to the normal and therefore carries no in-plane information.
    /// </remarks>
    public static TangentFrame FromNormalAligned(Vec3 normal, Vec3 preferredTangent)
    {
        // Project the preference into the tangent plane (Gram-Schmidt against the normal).
        Vec3 projected = preferredTangent - (normal * preferredTangent.Dot(normal));

        // A preference that is (nearly) parallel to the normal projects to (nearly) nothing and
        // its direction is dominated by round-off, so it must not be used.
        if (projected.LengthSquared < 1e-24)
        {
            return FromNormal(normal);
        }

        Vec3 e1 = projected.Normalized();
        Vec3 e2 = normal.Cross(e1);
        return new TangentFrame(e1, e2, normal);
    }

    /// <summary>Projects a world-space vector onto the tangent plane, in frame coordinates.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (double U, double V) ProjectToPlane(Vec3 worldVector) =>
        (worldVector.Dot(E1), worldVector.Dot(E2));

    /// <summary>Lifts a tangent-plane coordinate pair back into world space.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3 FromPlane(double u, double v) => (E1 * u) + (E2 * v);

    /// <summary>Lifts a tangent-plane direction given as an angle measured from <see cref="E1"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec3 FromPlaneAngle(double angle)
    {
        (double sin, double cos) = Math.SinCos(angle);
        return (E1 * cos) + (E2 * sin);
    }
}
