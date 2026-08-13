using MeshRegistration.Core.Numerics;
using Xunit;

namespace MeshRegistration.Core.Tests;

/// <summary>
/// Unit tests for the symmetric 2x2 eigensolver.
/// </summary>
/// <remarks>
/// The degenerate cases here are the root cause of the flat- and spherical-surface failure. The
/// classical eigenvector branch — choose <c>-B / (A - λ)</c> or <c>(λ - A) / B</c> by comparing
/// the denominators — evaluates <c>0 / 0</c> whenever <c>A == C</c> and <c>B == 0</c>, which is
/// precisely the shape operator of a plane (all zero) and of a sphere (a multiple of the
/// identity).
/// </remarks>
public sealed class Sym2x2Tests
{
    [Fact]
    public void Eigen_OnZeroOperator_IsFiniteAndZero()
    {
        // The shape operator of a plane.
        (double max, double min, double angle) = Sym2x2.Zero.Eigen();

        Assert.True(double.IsFinite(max));
        Assert.True(double.IsFinite(min));
        Assert.True(double.IsFinite(angle));
        Assert.Equal(0.0, max);
        Assert.Equal(0.0, min);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(-0.1)]
    [InlineData(1e-9)]
    [InlineData(1e9)]
    public void Eigen_OnIsotropicOperator_IsFiniteAndEqual(double curvature)
    {
        // The shape operator of a sphere of radius 1 / curvature.
        (double max, double min, double angle) = new Sym2x2(curvature, 0, curvature).Eigen();

        Assert.True(double.IsFinite(angle));
        Assert.Equal(curvature, max, Math.Abs(curvature) * 1e-12);
        Assert.Equal(curvature, min, Math.Abs(curvature) * 1e-12);
    }

    [Fact]
    public void Eigen_NearUmbilic_StaysFinite()
    {
        // Perturbations far below any meaningful signal must not produce NaN. The direction they
        // yield is arbitrary — that is why the caller classifies such points as umbilic — but it
        // must be a number.
        double[] perturbations = [0, 1e-18, -1e-18, 1e-12, double.Epsilon];

        foreach (double b in perturbations)
        {
            foreach (double gap in perturbations)
            {
                (double max, double min, double angle) = new Sym2x2(0.1, b, 0.1 + gap).Eigen();

                Assert.True(double.IsFinite(max));
                Assert.True(double.IsFinite(min));
                Assert.True(double.IsFinite(angle));
            }
        }
    }

    [Fact]
    public void Eigen_OnDiagonalOperator_RecoversAxisAlignedDirections()
    {
        // A cylinder-like operator: distinct eigenvalues along the frame axes.
        (double max, double min, double angle) = new Sym2x2(2.0, 0.0, 1.0).Eigen();

        Assert.Equal(2.0, max, 1e-12);
        Assert.Equal(1.0, min, 1e-12);

        // The larger eigenvalue sits on the first axis, so the angle from it is zero.
        Assert.Equal(0.0, angle, 1e-12);
    }

    [Fact]
    public void Eigen_WhenSecondAxisDominates_ReturnsQuarterTurn()
    {
        (double max, double min, double angle) = new Sym2x2(1.0, 0.0, 2.0).Eigen();

        Assert.Equal(2.0, max, 1e-12);
        Assert.Equal(1.0, min, 1e-12);
        Assert.Equal(Math.PI / 2, Math.Abs(angle), 1e-12);
    }

    [Fact]
    public void Eigen_OnPureShear_ReturnsDiagonalDirection()
    {
        (double max, double min, double angle) = new Sym2x2(0.0, 1.0, 0.0).Eigen();

        Assert.Equal(1.0, max, 1e-12);
        Assert.Equal(-1.0, min, 1e-12);
        Assert.Equal(Math.PI / 4, angle, 1e-12);
    }

    [Fact]
    public void Eigen_MatchesTraceAndDeterminant()
    {
        Sym2x2[] operators =
        [
            new(3, 1, 2),
            new(-1, 0.5, 4),
            new(0.001, -0.002, 0.003),
            new(-5, -7, -11),
        ];

        foreach (Sym2x2 s in operators)
        {
            (double max, double min, _) = s.Eigen();

            Assert.Equal(s.Trace, max + min, 1e-9);
            Assert.Equal(s.Determinant, max * min, 1e-9);
            Assert.True(max >= min);
        }
    }

    [Fact]
    public void RotatedBy_PreservesEigenvaluesAndShiftsTheAngle()
    {
        Sym2x2 original = new(3.0, 1.0, -2.0);
        (double max, double min, double angle) = original.Eigen();

        const double rotation = 0.7;
        Sym2x2 rotated = original.RotatedBy(rotation);
        (double rotatedMax, double rotatedMin, double rotatedAngle) = rotated.Eigen();

        // A change of basis cannot change the eigenvalues.
        Assert.Equal(max, rotatedMax, 1e-12);
        Assert.Equal(min, rotatedMin, 1e-12);

        // Expressed in a basis turned by +rotation, the eigenvector's angle drops by the same
        // amount. Principal directions are axes rather than vectors, so compare modulo pi.
        double delta = NormaliseToHalfTurn(rotatedAngle - (angle - rotation));
        Assert.Equal(0.0, delta, 1e-12);
    }

    [Fact]
    public void RotatedBy_FullTurnIsIdentity()
    {
        Sym2x2 original = new(1.5, -0.25, 0.75);
        Sym2x2 round = original.RotatedBy(Math.PI).RotatedBy(Math.PI);

        Assert.Equal(original.A, round.A, 1e-12);
        Assert.Equal(original.B, round.B, 1e-12);
        Assert.Equal(original.C, round.C, 1e-12);
    }

    [Fact]
    public void CurvatureDeviation_IsHalfTheEigenvalueGap()
    {
        Sym2x2[] operators = [new(3, 1, 2), new(0, 0, 0), new(5, 0, 5), new(-2, 4, 7)];

        foreach (Sym2x2 s in operators)
        {
            (double max, double min, _) = s.Eigen();
            Assert.Equal((max - min) * 0.5, s.CurvatureDeviation, 1e-12);
        }
    }

    private static double NormaliseToHalfTurn(double angle)
    {
        double wrapped = Math.IEEERemainder(angle, Math.PI);
        return wrapped;
    }
}
