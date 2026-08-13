using System.Runtime.CompilerServices;

namespace MeshRegistration.Core.Mesh;

/// <summary>
/// A triangle given by three vertex indices, wound counter-clockwise when seen from the
/// outward-facing side.
/// </summary>
public readonly record struct Triangle(int V0, int V1, int V2)
{
    public int this[int corner]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => corner switch
        {
            0 => V0,
            1 => V1,
            2 => V2,
            _ => throw new ArgumentOutOfRangeException(nameof(corner)),
        };
    }

    /// <summary>True when the same vertex appears twice, which makes the triangle degenerate.</summary>
    public bool HasRepeatedVertex => V0 == V1 || V1 == V2 || V2 == V0;

    /// <summary>Returns the triangle with reversed winding.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Triangle Flipped() => new(V0, V2, V1);

    /// <summary>
    /// Returns the three vertex indices sorted ascending — a winding-independent identity, used
    /// to detect duplicate faces.
    /// </summary>
    public (int A, int B, int C) SortedKey()
    {
        int a = V0;
        int b = V1;
        int c = V2;

        if (a > b)
        {
            (a, b) = (b, a);
        }

        if (b > c)
        {
            (b, c) = (c, b);
        }

        if (a > b)
        {
            (a, b) = (b, a);
        }

        return (a, b, c);
    }
}
