using System.Runtime.CompilerServices;

namespace MeshRegistration.Core.Numerics;

/// <summary>
/// A symmetric 2x2 matrix <c>[[A, B], [B, C]]</c>, used to represent the Weingarten map (shape
/// operator) of a surface in a tangent frame.
/// </summary>
/// <remarks>
/// Its eigenvalues are the principal curvatures and its eigenvectors the principal directions.
/// </remarks>
public readonly record struct Sym2x2(double A, double B, double C)
{
    public static Sym2x2 Zero => default;

    public double Trace => A + C;

    public double Determinant => (A * C) - (B * B);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Sym2x2 operator +(Sym2x2 x, Sym2x2 y) => new(x.A + y.A, x.B + y.B, x.C + y.C);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Sym2x2 operator *(Sym2x2 x, double s) => new(x.A * s, x.B * s, x.C * s);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Sym2x2 operator *(double s, Sym2x2 x) => x * s;

    public bool IsFinite => double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C);

    /// <summary>
    /// Eigen-decomposition of the matrix.
    /// </summary>
    /// <returns>
    /// <c>EigenMax</c> and <c>EigenMin</c> (with <c>EigenMax &gt;= EigenMin</c>), and
    /// <c>AngleMax</c>, the angle in radians from the frame's first tangent axis to the
    /// eigenvector belonging to <c>EigenMax</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is <b>total</b>: it returns finite values for every finite input, including
    /// the two degenerate cases that matter most in practice.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     On a <b>plane</b> the shape operator is exactly zero.
    ///   </item>
    ///   <item>
    ///     On a <b>sphere</b> of radius R it is <c>(1/R)·I</c>, i.e. <c>A == C</c> and
    ///     <c>B == 0</c> — every point is umbilic.
    ///   </item>
    /// </list>
    /// <para>
    /// In both cases every tangent direction is a principal direction, so the eigenvector is
    /// genuinely undefined rather than merely hard to compute. The classical branch — pick
    /// <c>-B / (A - λ)</c> or <c>(λ - A) / B</c> depending on which denominator is larger —
    /// evaluates <c>0 / 0</c> for exactly these inputs and yields NaN, which then propagates
    /// silently into the direction field. This is the single defect that broke curvature on flat
    /// and spherical patches in the previous implementation.
    /// </para>
    /// <para>
    /// The formulation used here avoids the division entirely. The eigenvector angle of a
    /// symmetric 2x2 matrix satisfies <c>tan(2θ) = 2B / (A - C)</c>, and
    /// <see cref="Math.Atan2(double, double)"/> is defined at the origin
    /// (<c>Atan2(0, 0) == 0</c>). Degenerate input therefore produces an arbitrary but finite
    /// and deterministic direction instead of NaN.
    /// </para>
    /// <para>
    /// Producing a finite number is necessary but not sufficient: that arbitrary direction still
    /// carries no information. Callers must consult
    /// <c>CurvatureFlags.Umbilic</c> / <c>CurvatureFlags.Planar</c>, which classify the point
    /// from the dimensionless eigenvalue gap, before using the direction for anything.
    /// </para>
    /// </remarks>
    public (double EigenMax, double EigenMin, double AngleMax) Eigen()
    {
        double mean = (A + C) * 0.5;
        double halfDifference = (A - C) * 0.5;

        // Hypot avoids overflow and underflow in the intermediate squares.
        double radius = double.Hypot(halfDifference, B);

        // The double-angle relation tan(2θ) = 2B / (A - C). Atan2 is total, so the umbilic case
        // (B == 0 and A == C) falls through to angle 0 rather than dividing zero by zero.
        double angleMax = 0.5 * Math.Atan2(2.0 * B, A - C);

        return (mean + radius, mean - radius, angleMax);
    }

    /// <summary>
    /// Half the difference between the principal curvatures, <c>(kMax - kMin) / 2</c>.
    /// </summary>
    /// <remarks>
    /// This is the curvature deviation. It is zero exactly at umbilic points, so scaling it by a
    /// length gives the dimensionless anisotropy used to classify degeneracy. Computing it
    /// directly avoids forming both eigenvalues when only their gap is needed.
    /// </remarks>
    public double CurvatureDeviation => double.Hypot((A - C) * 0.5, B);

    /// <summary>
    /// Re-expresses the operator in a tangent basis rotated by <paramref name="angle"/> radians
    /// about the surface normal.
    /// </summary>
    /// <remarks>
    /// The congruence <c>S' = Rᵀ S R</c> written with double angles, which is both cheaper and
    /// better conditioned than multiplying the three matrices out. Needed when shape operators
    /// computed in per-vertex frames are averaged in a common frame.
    /// </remarks>
    public Sym2x2 RotatedBy(double angle)
    {
        (double sin2, double cos2) = Math.SinCos(2.0 * angle);

        double mean = (A + C) * 0.5;
        double halfDifference = (A - C) * 0.5;

        double rotatedHalfDifference = (halfDifference * cos2) + (B * sin2);
        double rotatedB = (B * cos2) - (halfDifference * sin2);

        return new Sym2x2(mean + rotatedHalfDifference, rotatedB, mean - rotatedHalfDifference);
    }

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[[{A:G6}, {B:G6}], [{B:G6}, {C:G6}]]");
}
