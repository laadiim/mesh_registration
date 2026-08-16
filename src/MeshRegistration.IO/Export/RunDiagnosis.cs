using System.Globalization;
using MeshRegistration.Core.Mesh;

namespace MeshRegistration.IO.Export;

/// <summary>
/// Turns a finished run's numbers into an explanation of why it produced few or no lines.
/// </summary>
/// <remarks>
/// <para>
/// A run that yields nothing is not necessarily a failure — a featureless model genuinely has no
/// principal directions to follow. But the numbers already distinguish the possible causes, and
/// leaving the user to work out which one applies wastes information the tool is holding.
/// </para>
/// <para>
/// This exists because an earlier version guessed. Handed a mesh that stored a private copy of
/// each vertex per face, it reported "every candidate point is flat or spherical" while its own
/// report said the mesh was 100% <c>Unusable</c> and 0% planar or umbilic. The mesh was neither
/// flat nor spherical: it was shattered into 70 323 disconnected triangles, so no vertex had
/// enough neighbours to fit anything. The fix — <c>--weld</c> — was implemented and documented,
/// but the message pointed away from it.
/// </para>
/// </remarks>
public static class RunDiagnosis
{
    /// <summary>
    /// A mesh whose component count approaches its triangle count is not merely fragmented, it is
    /// unwelded: every face is its own island.
    /// </summary>
    private const double ShatteredComponentRatio = 0.5;

    /// <summary>Fraction of vertices that must be degenerate before it is worth naming a cause.</summary>
    private const double DominantFraction = 0.5;

    /// <summary>
    /// Explains a run that produced fewer lines than asked for. Returns an empty list when there
    /// is nothing worth saying.
    /// </summary>
    /// <param name="topology">Repair report for the mesh.</param>
    /// <param name="curvature">Curvature classification for the mesh.</param>
    /// <param name="tracing">What the tracer produced.</param>
    /// <param name="requestedLines">The line budget the run was given.</param>
    public static IReadOnlyList<string> Explain(
        MeshDiagnostics topology,
        CurvatureReport curvature,
        TracingReport tracing,
        int requestedLines)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(curvature);
        ArgumentNullException.ThrowIfNull(tracing);

        List<string> notes = [];

        bool producedNothing = tracing.LineCount == 0;
        bool producedFew = tracing.LineCount < requestedLines / 4;

        if (!producedNothing && !producedFew)
        {
            return notes;
        }

        int vertexCount = Math.Max(1, curvature.VertexCount);
        double unusable = curvature.UnusableVertices / (double)vertexCount;
        double degenerate = (curvature.PlanarVertices + curvature.UmbilicVertices) / (double)vertexCount;

        // Cause 1: the mesh was never welded, so it has no connectivity to walk along.
        bool shattered =
            topology.WeldedVertices == 0 &&
            topology.OutputTriangleCount > 0 &&
            topology.ConnectedComponentCount >= topology.OutputTriangleCount * ShatteredComponentRatio;

        if (shattered)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"The mesh is not welded: {topology.ConnectedComponentCount} connected components for " +
                $"{topology.OutputTriangleCount} triangles, and {topology.ManifoldEdgeCount} shared edges. " +
                $"Every face is a separate island, so no vertex has a neighbourhood to fit curvature in."));
            notes.Add(
                "Re-run with --weld. Files that store a private copy of each vertex per face need it; " +
                "the geometry is unchanged, only the connectivity is restored.");
            return notes;
        }

        // Cause 2: connectivity is fine, but the fit fails anyway.
        if (unusable > DominantFraction)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"{unusable:P1} of vertices have no usable curvature fit. The mesh is connected, so " +
                $"this is about the fit itself rather than the topology."));
            notes.Add(
                "Try a wider fitting neighbourhood (--nbhood 12 or more). If the mesh is very " +
                "irregularly triangulated, that is the usual cause.");
            return notes;
        }

        // Cause 3: the surface really is featureless.
        if (degenerate > DominantFraction)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"{degenerate:P1} of vertices are flat or spherical, so no principal direction exists " +
                $"there. This is a property of the model, not a failure."));
            notes.Add(
                "Lower --umbilic-threshold to admit more marginal directions, but not below about " +
                "0.02: past that the directions are noise. Inspect the exported curvature mesh " +
                "(--color-by flags) to see which regions are affected.");
            return notes;
        }

        // Cause 4: plenty of usable surface, but the seeds cannot be placed or the lines die early.
        if (producedNothing)
        {
            notes.Add(
                "No seed could be placed even though most of the surface is usable. This is " +
                "unexpected; please report it.");
            return notes;
        }

        bool endsOnBoundary =
            tracing.EndReasons.TryGetValue("Boundary", out int boundaryEnds) &&
            boundaryEnds > tracing.LineCount;

        if (endsOnBoundary || topology.ConnectedComponentCount > 20)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"The model is fragmented ({topology.ConnectedComponentCount} components, " +
                $"{topology.BoundaryEdgeCount} boundary edges), so lines reach an edge quickly and " +
                $"few seeds fit at the default spacing."));
            notes.Add("Try --lines 100 --seed-spacing 0.02.");
        }

        return notes;
    }
}
