namespace MeshRegistration.Core.Numerics;

/// <summary>
/// Outcome of a symmetric 3x3 solve.
/// </summary>
/// <param name="Solution">The solution vector; meaningless unless <paramref name="Succeeded"/>.</param>
/// <param name="Succeeded">False when the matrix is not usably positive definite.</param>
/// <param name="PivotRatio">
/// Ratio of the smallest to the largest LDL pivot of the trace-normalised matrix, in
/// <c>[0, 1]</c>. A dimensionless conditioning proxy: values near 1 indicate a well-balanced
/// system, values near 0 a near-singular one.
/// </param>
public readonly record struct Sym3x3SolveResult(
    (double X, double Y, double Z) Solution,
    bool Succeeded,
    double PivotRatio);

/// <summary>
/// Direct solver for small symmetric positive definite systems.
/// </summary>
/// <remarks>
/// The curvature estimator assembles one 3x3 normal-equation system per vertex, so this runs
/// millions of times per mesh. It is written as a closed-form LDL factorisation over locals:
/// no allocation, no indirection, fully inlineable.
/// <para>
/// It replaces the previous Cramer's-rule solve, which had two problems. It computed a
/// determinant ratio with no conditioning information, and the accompanying singularity test
/// compared raw determinants against hard-coded absolute constants (<c>1e-10</c>, <c>1e-6</c>).
/// Those constants are not scale invariant: the same geometry expressed in millimetres and in
/// metres produced different singularity verdicts. <see cref="Solve"/> normalises by the trace
/// first, so the <c>minimumPivotRatio</c> threshold is dimensionless and transfers across models
/// of any size.
/// </para>
/// </remarks>
public static class Sym3x3Solver
{
    /// <summary>
    /// Solves <c>M x = rhs</c> for a symmetric positive definite <c>M</c> given by its upper
    /// triangle, using an LDL factorisation.
    /// </summary>
    /// <param name="m00">Row 0, column 0.</param>
    /// <param name="m01">Row 0, column 1 (equals row 1, column 0).</param>
    /// <param name="m02">Row 0, column 2 (equals row 2, column 0).</param>
    /// <param name="m11">Row 1, column 1.</param>
    /// <param name="m12">Row 1, column 2 (equals row 2, column 1).</param>
    /// <param name="m22">Row 2, column 2.</param>
    /// <param name="rhs">The right-hand side.</param>
    /// <param name="minimumPivotRatio">
    /// Smallest acceptable ratio between the smallest and largest pivot of the trace-normalised
    /// matrix. Systems below this are reported as failed rather than solved inaccurately.
    /// </param>
    public static Sym3x3SolveResult Solve(
        double m00, double m01, double m02,
        double m11, double m12,
        double m22,
        (double X, double Y, double Z) rhs,
        double minimumPivotRatio = 1e-9)
    {
        // Normalise by the mean diagonal entry so that the pivot ratio below is a pure number.
        // The solution is recovered by scaling the right-hand side identically, which leaves x
        // unchanged: (M/s) x = (b/s).
        double scale = (m00 + m11 + m22) / 3.0;
        if (!(scale > 0) || !double.IsFinite(scale))
        {
            return new Sym3x3SolveResult(default, Succeeded: false, PivotRatio: 0);
        }

        double inverseScale = 1.0 / scale;
        double a00 = m00 * inverseScale;
        double a01 = m01 * inverseScale;
        double a02 = m02 * inverseScale;
        double a11 = m11 * inverseScale;
        double a12 = m12 * inverseScale;
        double a22 = m22 * inverseScale;

        double b0 = rhs.X * inverseScale;
        double b1 = rhs.Y * inverseScale;
        double b2 = rhs.Z * inverseScale;

        // LDL factorisation: M = L D Lᵀ with unit lower-triangular L.
        double d0 = a00;
        if (!(d0 > 0))
        {
            return new Sym3x3SolveResult(default, Succeeded: false, PivotRatio: 0);
        }

        double l10 = a01 / d0;
        double l20 = a02 / d0;

        double d1 = a11 - (l10 * l10 * d0);
        if (!(d1 > 0))
        {
            return new Sym3x3SolveResult(default, Succeeded: false, PivotRatio: 0);
        }

        double l21 = (a12 - (l20 * d0 * l10)) / d1;

        double d2 = a22 - (l20 * l20 * d0) - (l21 * l21 * d1);
        if (!(d2 > 0))
        {
            return new Sym3x3SolveResult(default, Succeeded: false, PivotRatio: 0);
        }

        double smallestPivot = Math.Min(d0, Math.Min(d1, d2));
        double largestPivot = Math.Max(d0, Math.Max(d1, d2));
        double pivotRatio = smallestPivot / largestPivot;

        if (pivotRatio < minimumPivotRatio)
        {
            return new Sym3x3SolveResult(default, Succeeded: false, pivotRatio);
        }

        // Forward substitution: L y = b.
        double y0 = b0;
        double y1 = b1 - (l10 * y0);
        double y2 = b2 - (l20 * y0) - (l21 * y1);

        // Diagonal solve: D z = y.
        double z0 = y0 / d0;
        double z1 = y1 / d1;
        double z2 = y2 / d2;

        // Back substitution: Lᵀ x = z.
        double x2 = z2;
        double x1 = z1 - (l21 * x2);
        double x0 = z0 - (l10 * x1) - (l20 * x2);

        if (!double.IsFinite(x0) || !double.IsFinite(x1) || !double.IsFinite(x2))
        {
            return new Sym3x3SolveResult(default, Succeeded: false, pivotRatio);
        }

        return new Sym3x3SolveResult((x0, x1, x2), Succeeded: true, pivotRatio);
    }
}
