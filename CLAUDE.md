# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

A .NET 10 tool for **globally registering pairs of triangle meshes** — aligning two partially
overlapping scans without an initial guess. The method traces integral curves of the principal
curvature direction field on each mesh, samples a signature `(kMin, kMax, κ_g)` along them at a
constant arc-length step, aligns those signatures as sequences, derives a rigid transform from
each alignment, and takes the densest cluster of candidate transforms.

This is a rewrite of `../vasa-projekt` (C#, .NET Framework 4.8, Windows-only, SlimDX/WPF viewer).
That repository is **reference only** — do not edit it. Its `Data.zip` supplies the test meshes.

**Only phase 1 is implemented**: loading, topology repair, curvature estimation, line tracing, and
the MeshLab exports. Matching, transform clustering and ICP are not written yet; `README.md` ends
with notes on what they will need.

## Layout

```
src/MeshRegistration.Core/         Vec3, BoundingBox, TangentFrame; Sym2x2, Sym3x3Solver;
                                   Triangle, TriangleMesh, SurfacePoint, MeshTopology,
                                   MeshBuilder (topology repair), MeshDiagnostics
src/MeshRegistration.IO/           ObjReader; Export/ (polylines, tubes, vertex-coloured mesh,
                                   CSV, JSON report)
src/MeshRegistration.Algorithms/   Curvature/ (ShapeOperatorField), Tracing/ (SurfaceWalker,
                                   LineTracer, SeedSelector)
src/MeshRegistration.Cli/          System.CommandLine entry point; `inspect` and `trace`
tests/MeshRegistration.Core.Tests/         Sym2x2, mesh repair, OBJ reader
tests/MeshRegistration.Algorithms.Tests/   analytic surfaces, curvature, tracing, exports
```

Dependency order is `Core <- IO <- Cli` and `Core <- Algorithms <- IO`. IO references Algorithms
because the exporters serialise `TracedLine` and `CurvatureSample`.

**No third-party numerics.** The 2x2 eigensolver and 3x3 LDL solver are hand-written. Do not
reintroduce MathNet — the previous code called `DenseMatrix.Svd()` per candidate in a hot loop.

## Build and test

```bash
dotnet build -c Release
```

```bash
dotnet test
```

```bash
dotnet run -c Release --project src/MeshRegistration.Cli -- trace data/kac1.obj --out out
```

Test data is not in the repository. Unpack it with:

```bash
mkdir -p data && unzip -o ../vasa-projekt/Data.zip -d data
```

`data/` and `out/` are gitignored.

## Invariants that must not regress

These are the reasons the rewrite exists. Breaking one silently undoes the work.

1. **No NaN or infinity may ever leave curvature estimation.** On a plane the shape operator is
   zero and on a sphere it is a multiple of the identity; the classical eigenvector branch
   computes `0/0` for both. `Sym2x2.Eigen` avoids the division entirely via
   `θ = ½·atan2(2B, A − C)`, which is total. Tests `Plane_IsFlatAndFinite` and
   `Sphere_HasUniformCurvatureAndIsUmbilic` pin this down.

2. **A finite direction is not a usable direction.** Because the eigensolver always returns a
   number, callers must check `CurvatureSample.HasUsableDirection` (i.e. the `Umbilic` flag), never
   just whether the vector is non-zero. On real scans 8–26% of vertices are umbilic. `LineTracer`
   and `SeedSelector` both depend on this.

3. **Loading must repair, never refuse.** `MeshBuilder` handles degenerate and duplicate faces,
   inconsistent winding, non-manifold edges and bow-tie vertices, and reports everything in
   `MeshDiagnostics`. `NonManifoldEdgePolicy.Strict` exists only for callers who explicitly ask for
   the old fail-fast behaviour.

4. **Every threshold is dimensionless.** Lengths are multiples of `TriangleMesh.AverageEdgeLength`
   or fractions of `BoundingBox.DiagonalLength`; curvature thresholds are curvature times the
   neighbourhood radius. The sample data spans diagonals from 0.14 to 256.6, so an absolute
   constant anywhere is a bug. If you find yourself typing a bare number with units, stop.

5. **Determinism.** No random number generator anywhere. Seeds are ranked deterministically with
   the vertex index as tie-break; parallel work writes results by index. Two runs must produce
   byte-identical output (`TraceAll_IsDeterministic`).

6. **The mesh is immutable.** `TriangleMesh` exposes `ReadOnlySpan`. Never let a stage mutate the
   mesh it is reading — the previous tracer appended visualisation points to the mesh it was
   walking, desynchronising it from the per-vertex weight array. Visualisation geometry belongs in
   the exporters.

## Conventions

**Corner table.** Corner `c` belongs to triangle `c / 3` at local index `c % 3`. The vertex *at*
corner `c` is that triangle's vertex `c % 3`; the edge *opposite* corner `c` joins the vertices at
`Next(c)` and `Previous(c)`. `Opposite(c)` is the corner facing that same edge from the neighbour,
or `-1`. Fans are traversed with `Swing` / `Unswing`.

After `MeshBuilder`, topology is **manifold by construction**: every non-isolated vertex has
exactly one fan. Code may rely on this.

**Barycentric.** `SurfacePoint` uses `P = U·V0 + V·V1 + (1 − U − V)·V2`; the third weight is
implicit in `W`.

**Curvature sign.** The shape operator is `dN`, so with outward normals a convex surface has
positive curvature and a sphere of radius R has `kMin = kMax = 1/R`. `MeshBuilder` orients closed
components outward, so this is well defined.

**Geodesic curvature** is measured after projecting both chords into the tangent plane. Omitting
the projection gives the curve's *spatial* curvature, which is a different quantity — on a cylinder
the principal circles are geodesics (κ_g = 0) but their spatial curvature is 1/R.

**Principal directions are a line field.** Each is defined only up to sign, and the min/max labels
exchange wherever the two curvatures cross. Never follow `DirMax` by name across a surface; pick
whichever of `±DirMin`, `±DirMax` best continues the transported previous direction, as
`LineTracer.ChooseContinuation` does.

## Analyzer settings that trip people up

`TreatWarningsAsErrors` is on with `AnalysisLevel latest-recommended`, so these fail the build:

- **XML docs are all-or-nothing per member.** Documenting one `<param>` requires documenting every
  parameter (CS1573). Put prose that describes only some parameters into `<remarks>` instead.
- **`stackalloc` inside a loop** is CA2014. Hoist it above the loop.
- **CA1859** wants concrete collection types on private members; **CA1822** wants static where no
  instance state is touched.
- `.editorconfig` already suppresses CA1711 (`[Flags]` enums genuinely should end in `Flags`) and,
  for `tests/**`, CA1707 (the `Subject_Behaviour` naming convention needs underscores).
- `TextWriter.WriteLine(IFormatProvider, ...)` does not exist. Use
  `writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"..."))`.

## Testing approach

Correctness is established against **surfaces with closed-form curvature**, generated by
`tests/MeshRegistration.Algorithms.Tests/AnalyticSurfaces.cs`: plane, icosphere, cylinder, torus,
quadric. Prefer adding a case there over asserting on real scan data.

Two subtleties when tightening a tolerance:

- The estimator averages over its neighbourhood, so on a surface whose curvature varies (the
  quadric, the torus) the fitted value is biased towards the neighbourhood mean. Narrow
  `NeighbourhoodWidth` rather than loosening the tolerance, or the test stops measuring the
  estimator and starts measuring the smoothing window.
- Boundary vertices have one-sided neighbourhoods and are legitimately less accurate; exclude them,
  as the `Analyse` helper does.

Topology tests use hand-built meshes isolating one defect each (bow-tie, three-fan edge,
inconsistent winding, isolated vertex, per-face duplicated vertices).

Real-data expectations, useful as a cross-check: `hip1.obj` has 2 bow-tie and 4 isolated vertices;
`hea1.obj` has 3 isolated; `cha1.obj` has 32 non-manifold edges, 221 bow-ties, 23 isolated, 4257
degenerate faces; `cha2m.obj` has 70 non-manifold edges and 499 bow-ties.

## Documentation

- `README.md` — design rationale, the two defects the rewrite fixes, and what phase 2 needs.
- `MANUAL.md` — Czech-language operating manual: commands, how to read the output, common
  parameter adjustments. Written for the user, who works in Czech.
- `docs/` — Czech-language technical documentation, one file per stage: architecture and
  invariants, loading and topology, the curvature mathematics, tracing, output formats, a full
  parameter reference, the testing strategy, measured results over all 24 sample meshes, and the
  phase 2 design. `docs/README.md` is the index.

When changing behaviour, update the affected `docs/` file in the same commit — the measured tables
in `docs/08-vysledky.md` in particular go stale silently.

Code comments and identifiers are English. When explaining *why* something differs from the
previous implementation, name the concrete defect rather than saying "improved" — that context is
the main reason a reader will not undo it.
