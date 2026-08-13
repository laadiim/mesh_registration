# mesh_registration

Modernised rewrite of the curvature-line mesh registration tool, on **.NET 10**.

Given two triangle meshes with partial overlap, the method aligns them without an initial guess:
it traces integral curves of the principal curvature direction field on each mesh, samples a
signature `(kMin, kMax, κ_g)` along them at a constant arc-length step, aligns those signatures as
sequences, derives a rigid transform from each alignment, and takes the densest cluster of
candidate transforms.

**This phase implements loading, topology, curvature and line tracing.** The output is a set of
traced lines viewable in MeshLab, plus a CSV that the later stages consume. Matching, transform
clustering and ICP are not implemented yet.

## Documentation

| document | contents |
|---|---|
| this file | why the rewrite exists, the two defects it fixes |
| [MANUAL.md](MANUAL.md) | operating guide — commands, reading the output, common adjustments (Czech) |
| [docs/](docs/README.md) | detailed technical documentation — mathematics, algorithms, measurements (Czech) |
| [CLAUDE.md](CLAUDE.md) | guidance for Claude Code working in this repository |

## Building and running

```bash
dotnet build -c Release
```

```bash
dotnet test
```

Inspect a mesh's topology without running any analysis:

```bash
dotnet run -c Release --project src/MeshRegistration.Cli -- inspect data/hip1.obj
```

Trace curvature lines and export them:

```bash
dotnet run -c Release --project src/MeshRegistration.Cli -- trace data/kac1.obj --out out --color-by flags
```

`--help` on either command lists the options. Everything is tunable in dimensionless units — see
"Scale invariance" below.

## Output files

| file | what it is |
|---|---|
| `<n>_lines_tube.obj` + `.mtl` | each line as a thin tube of triangles, one colour per line. **Open this one first** — it renders in any MeshLab shading mode |
| `<n>_lines.obj` | the same lines as exact OBJ polylines (`v` + `l`); compact, best for feeding other tools |
| `<n>_curvature.obj` | the input mesh with per-vertex colours. With `--color-by flags`: grey = planar, blue = umbilic, orange = boundary, red = no usable fit, green = usable |
| `<n>_samples.csv` | one row per sample: `lineId, sampleIndex, arcLength, x,y,z, nx,ny,nz, kMin, kMax, kappaG, confidence, flags, triangle`. This is the handover to the matching stage |
| `<n>_report.json` | topology repair report and tracing statistics |

## Layout

```
src/MeshRegistration.Core/         geometry, numerics, mesh, topology repair
src/MeshRegistration.IO/           OBJ reader, MeshLab exporters
src/MeshRegistration.Algorithms/   curvature estimation, seed selection, line tracing
src/MeshRegistration.Cli/          the `meshreg` command line
tests/                             analytic-surface and topology tests
```

No third-party numerics. The 2x2 eigensolver and the 3x3 LDL solver are written directly
against the problem, which removes a dependency and the per-vertex allocations a general matrix
library would incur in these loops.

## The two problems this rewrite exists to fix

### 1. Non-manifold meshes failed to load

The previous corner-table builder threw `"Non-manifold mesh at the input."` as soon as a directed
edge repeated. In the bundled sample data, `cha1.obj` and `cha2m.obj` trip this — they have 32 and
70 non-manifold edges respectively.

Worse were the defects that passed *silently*. A bow-tie vertex — two surface patches touching at
a single point — has perfectly manifold edges, so edge-level checks miss it, but it has two
disjoint fans and the old code stored one arbitrary incident corner per vertex, so neighbourhood
walks covered only one fan. Isolated vertices kept incident corner `0`, which walked the fan of an
unrelated triangle. Both occur throughout the dataset: 221 bow-ties in `cha1.obj`, 499 in
`cha2m.obj`, 2 in `hip1.obj`; 23, 52 and 4 isolated vertices respectively.

`MeshBuilder` now **repairs and reports** instead of refusing:

- degenerate and duplicate faces are dropped;
- edges are bucketed by a packed `ulong` key and sorted, which exposes buckets of any size
  (replacing a `Dictionary<Edge,int>` whose hash `v1 + 10000*v2` collided catastrophically above
  ten thousand vertices);
- winding is propagated across each component and closed components are oriented outward;
- non-manifold edges are resolved by a switchable policy: `cut` (default — the surface splits into
  manifold patches and tracing simply stops there, as at a real border), `pair-best` (keep the
  flattest continuation), or `strict` (the old fail-fast behaviour, now with the offending edges
  named);
- bow-tie vertices are split into one vertex per fan, so every vertex has exactly one well-defined
  one-ring — the property curvature estimation and tracing both assume;
- everything found is reported in `MeshDiagnostics`.

### 2. Curvature was NaN on flat and spherical surfaces

On a plane the shape operator is exactly zero; on a sphere it is a multiple of the identity. In
both cases `A == C` and `B == 0`, and the previous eigensolver's branch

```csharp
if (Math.Abs(b) < Math.Abs(a - e1)) v1 = -b / (a - e1);
else                                v2 = (e1 - a) / b;   // 0 / 0 when a == c and b == 0
```

evaluated `0 / 0`. The accompanying singularity test inspected the moment matrix, not the
resulting operator, and a plane's moment matrix is perfectly conditioned — so nothing caught it.
The NaN propagated into the tracer, whose own guard `moveVector.X == double.NaN` can never fire,
since NaN does not equal itself.

Two things were needed, and the fix supplies both.

**Stay finite.** `Sym2x2.Eigen` uses the double-angle relation `θ = ½·atan2(2B, A − C)`.
`Atan2(0, 0)` is defined, so degenerate input yields an arbitrary but finite, deterministic
direction instead of NaN.

**Know when the answer is meaningless.** A finite number is not a usable direction: at an umbilic
point *every* tangent direction is principal, so none is distinguished. Points are classified by
two dimensionless quantities, curvature scaled by the neighbourhood radius:

```
aniso = (kMax − kMin)/2 · r      < 0.05  →  Umbilic  (direction meaningless; values still valid)
curv  = max(|kMax|,|kMin|) · r   < 0.02  →  Planar   (values meaningless too)
```

This matters at scale: on the sample data **8% to 26% of vertices are umbilic** and up to 3% are
planar. Downstream, `SeedSelector` never starts a line at such a point, and `LineTracer` stops
consulting the direction field there and continues by parallel transport instead — tracing a
geodesic that bridges short degenerate patches while keeping arc-length parameterisation intact,
and cutting the line when the run gets long enough that continuing would be guesswork.

`--color-by flags` writes a mesh that shows exactly where these regions are.

## Scale invariance

The sample data spans bounding box diagonals from 0.14 to 256.6 — three orders of magnitude. Every
absolute threshold in the previous code was therefore meaningless on some of it. The clearest case:
`length = 30` gave an 11-sample stub on `brd1.obj` and attempted about 43 000 steps across a model
214 times smaller on `hea1.obj`.

Every tolerance here is a ratio: neighbourhood radius and tracing step in multiples of the mean
edge length, line length and seed spacing as fractions of the bounding box diagonal, degeneracy
thresholds as curvature times radius. Mean traced line length now lands between 0.12 and 0.34 of
the diagonal across the whole dataset.

## Other corrections worth knowing about

- **Exit edge.** The walker takes the *smallest* positive ray parameter, i.e. the first edge
  reached. The previous code kept the *largest* valid one, which selects the far side of the
  triangle whenever more than one edge test passes.
- **Geodesic curvature.** `κ_g` is measured after projecting both chords into the tangent plane, so
  it captures only bending *within* the surface. The previous code took the turn between the raw
  3-D chords, which is the curve's spatial curvature — a different quantity. On a cylinder the
  principal circles are geodesics: correct answer 0, previous answer 1/R.
- **Direction field continuity.** Principal directions form a line field, and the min/max labels
  exchange wherever the two curvatures cross. Following `DirMax` by name jumps between different
  integral curves, so the tracer picks whichever of `±DirMin`, `±DirMax` best continues the
  transported previous direction.
- **Handedness.** The OBJ reader no longer negates Z unconditionally. That silently reversed the
  handedness of the coordinate system, and with it the effective winding of every face and the
  sign of every curvature. It is now `--flip-z`, off by default.
- **Determinism.** No random number generator is involved: seeds are ranked deterministically and
  parallel tracing writes results by index. Repeated runs are byte-identical. The previous code's
  "deterministic" mode created N generators all seeded identically, so every thread produced the
  same sequence.
- **Cost.** Corner tables and curvature fields are built once, not per line; curvature at a surface
  point interpolates precomputed vertex operators instead of re-running a flood fill and a
  least-squares fit at every step; nothing logs inside a hot loop. The 120 MB `eie1.obj`
  (1.48 M triangles) loads in about 0.7 s and completes the whole pipeline in about 5.5 s.

## Next phase

The `TracedLine` / `LineSample` model and `<n>_samples.csv` are the stable interface for what
follows: windowing the signature sequences, Smith–Waterman alignment between meshes, Kabsch
transform estimation per match, density clustering in transform space, and ICP refinement. Two
changes are already known to be needed there — the signature channels must be standardised and
confidence-weighted rather than compared against an absolute threshold, and candidate transforms
need a conditioning test so that rank-deficient correspondences (segments lying on a sphere or a
plane, or nearly collinear points) are down-weighted rather than trusted.
