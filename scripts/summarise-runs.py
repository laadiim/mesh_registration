#!/usr/bin/env python3
"""Aggregates the per-mesh reports written by `meshreg trace` into one table.

Reads the structured `<name>_report.json` files rather than scraping console output, so the
summary cannot drift out of step with the tool's formatting. Also re-checks the invariant that no
non-finite value reaches the sample CSVs.

Called by scripts/run-all.sh; usable on its own against an existing output directory.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys

# Exit code the CLI uses when a non-finite value reached the output.
NON_FINITE_EXIT = 2


def read_int(path: pathlib.Path, default: int = -1) -> int:
    try:
        return int(path.read_text().strip())
    except (OSError, ValueError):
        return default


def scan_csv_for_non_finite(path: pathlib.Path) -> int:
    """Counts rows containing NaN or Infinity. Independent of what the tool reported."""
    if not path.exists():
        return -1

    hits = 0
    with path.open(encoding="utf-8") as handle:
        for line in handle:
            lowered = line.lower()
            if "nan" in lowered or "infinity" in lowered:
                hits += 1
    return hits


def collect(out_dir: pathlib.Path, log_dir: pathlib.Path) -> list[dict]:
    rows = []

    for report_path in sorted(out_dir.glob("*_report.json")):
        name = report_path.name[: -len("_report.json")]

        try:
            report = json.loads(report_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            rows.append({"model": name, "status": f"unreadable report ({error})"})
            continue

        topology = report.get("Topology", {})
        curvature = report.get("Curvature", {})
        tracing = report.get("Tracing", {})

        vertices = topology.get("OutputVertexCount", 0) or 1
        diagonal = topology.get("DiagonalLength", 0.0) or float("nan")

        exit_code = read_int(log_dir / f"{name}.exit")
        elapsed_ms = read_int(log_dir / f"{name}.ms")
        csv_hits = scan_csv_for_non_finite(out_dir / f"{name}_samples.csv")

        rows.append(
            {
                "model": name,
                "exit": exit_code,
                "ms": elapsed_ms,
                "vertices": topology.get("OutputVertexCount", 0),
                "triangles": topology.get("OutputTriangleCount", 0),
                "diagonal": diagonal,
                "avg_edge": topology.get("AverageEdgeLength", 0.0),
                "components": topology.get("ConnectedComponentCount", 0),
                "nonmanifold_edges": topology.get("NonManifoldEdgeCount", 0),
                "bowties": topology.get("NonManifoldVerticesFound", 0),
                "isolated": topology.get("IsolatedVertexCount", 0),
                "degenerate_faces": topology.get("DegenerateFacesRemoved", 0),
                "reoriented": topology.get("ReorientedFaces", 0),
                "planar_pct": 100.0 * curvature.get("PlanarVertices", 0) / vertices,
                "umbilic_pct": 100.0 * curvature.get("UmbilicVertices", 0) / vertices,
                "unusable_pct": 100.0 * curvature.get("UnusableVertices", 0) / vertices,
                "usable_pct": 100.0 * curvature.get("UsableFraction", 0.0),
                "seeds": tracing.get("SeedCount", 0),
                "lines": tracing.get("LineCount", 0),
                "samples": tracing.get("TotalSamples", 0),
                "mean_len": tracing.get("MeanLineLength", 0.0),
                "len_over_diag": (tracing.get("MeanLineLength", 0.0) / diagonal) if diagonal else float("nan"),
                "degenerate_samples": tracing.get("DegenerateSamples", 0),
                "non_finite_reported": tracing.get("NonFiniteSamples", 0),
                "non_finite_in_csv": csv_hits,
                "end_reasons": ";".join(
                    f"{k}={v}" for k, v in sorted(tracing.get("EndReasons", {}).items())
                ),
            }
        )

    return rows


def find_missing(out_dir: pathlib.Path, log_dir: pathlib.Path) -> list[tuple[str, int]]:
    """Meshes that were attempted but produced no report — i.e. crashed or were rejected."""
    missing = []
    for exit_file in sorted(log_dir.glob("*.exit")):
        name = exit_file.stem
        if not (out_dir / f"{name}_report.json").exists():
            missing.append((name, read_int(exit_file)))
    return missing


def print_table(rows: list[dict]) -> None:
    header = (
        f"{'MODEL':<10} {'VERTS':>8} {'TRIS':>8} {'DIAG':>9} {'CMP':>5} "
        f"{'NMe':>4} {'BOW':>4} {'ISO':>4} {'PLAN%':>6} {'UMB%':>6} "
        f"{'LINES':>6} {'SAMP':>6} {'LEN/DIAG':>9} {'NaN':>4} {'ms':>6}"
    )
    print(header)
    print("-" * len(header))

    for row in rows:
        if "status" in row:
            print(f"{row['model']:<10} {row['status']}")
            continue

        flag = "!" if (row["non_finite_reported"] or row["non_finite_in_csv"]) else "."
        print(
            f"{row['model']:<10} {row['vertices']:>8} {row['triangles']:>8} "
            f"{row['diagonal']:>9.4g} {row['components']:>5} "
            f"{row['nonmanifold_edges']:>4} {row['bowties']:>4} {row['isolated']:>4} "
            f"{row['planar_pct']:>6.2f} {row['umbilic_pct']:>6.2f} "
            f"{row['lines']:>6} {row['samples']:>6} {row['len_over_diag']:>9.3f} "
            f"{flag:>4} {row['ms']:>6}"
        )


def print_aggregates(rows: list[dict], elapsed_ms: int | None) -> None:
    usable = [r for r in rows if "status" not in r]
    if not usable:
        return

    diagonals = [r["diagonal"] for r in usable if r["diagonal"] == r["diagonal"]]

    # Meshes that traced nothing would contribute a ratio of 0 and stretch the band downwards,
    # hiding the very thing it is there to show.
    traced = [r for r in usable if r["lines"] > 0]
    ratios = [r["len_over_diag"] for r in traced if r["len_over_diag"] == r["len_over_diag"]]

    print()
    print(f"  meshes                {len(usable)}")
    print(f"  triangles total       {sum(r['triangles'] for r in usable):,}")

    # The per-mesh times sum to more than the elapsed time because the runs overlap; reporting
    # only the sum would misrepresent how long the batch actually takes.
    summed = sum(r["ms"] for r in usable) / 1000
    if elapsed_ms is not None:
        print(f"  elapsed               {elapsed_ms / 1000:.1f} s  (sum of per-mesh: {summed:.1f} s)")
    else:
        print(f"  sum of per-mesh times {summed:.1f} s")

    if diagonals:
        print(
            f"  scale span            {min(diagonals):.4g} .. {max(diagonals):.4g}"
            f"  ({max(diagonals) / min(diagonals):.0f}x)"
        )
    if ratios:
        # The point of the dimensionless thresholds: this band must stay narrow even though the
        # models above span three orders of magnitude.
        suffix = "" if len(traced) == len(usable) else f"  (over {len(traced)} mesh(es) that traced)"
        print(f"  line length / diagonal {min(ratios):.3f} .. {max(ratios):.3f}{suffix}")

    print(f"  lines traced          {sum(r['lines'] for r in usable)}")
    print(f"  samples               {sum(r['samples'] for r in usable):,}")
    print(f"  degenerate samples    {sum(r['degenerate_samples'] for r in usable):,}")
    print()
    print(f"  meshes with non-manifold edges  {sum(1 for r in usable if r['nonmanifold_edges'])}")
    print(f"  meshes with bow-tie vertices    {sum(1 for r in usable if r['bowties'])}")
    print(f"  meshes with isolated vertices   {sum(1 for r in usable if r['isolated'])}")
    print(f"  meshes needing reorientation    {sum(1 for r in usable if r['reoriented'])}")


def write_csv(rows: list[dict], path: pathlib.Path) -> None:
    import csv

    usable = [r for r in rows if "status" not in r]
    if not usable:
        return

    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(usable[0].keys()))
        writer.writeheader()
        writer.writerows(usable)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default="out", type=pathlib.Path)
    parser.add_argument("--logs", default=None, type=pathlib.Path)
    parser.add_argument("--csv", default=None, type=pathlib.Path)
    parser.add_argument(
        "--elapsed-ms",
        default=None,
        type=int,
        help="Wall-clock duration of the batch, which the runner knows and this script cannot.",
    )
    args = parser.parse_args()

    log_dir = args.logs or (args.out / "logs")
    csv_path = args.csv or (args.out / "summary.csv")

    if not args.out.is_dir():
        print(f"Output directory '{args.out}' does not exist.", file=sys.stderr)
        return 66

    rows = collect(args.out, log_dir)
    if not rows:
        print(f"No *_report.json found in '{args.out}'.", file=sys.stderr)
        return 66

    print_table(rows)
    print_aggregates(rows, args.elapsed_ms)
    write_csv(rows, csv_path)
    print()
    print(f"  summary written to    {csv_path}")

    # Verdict.
    failures = [(name, code) for name, code in find_missing(args.out, log_dir)]
    non_finite = [
        r for r in rows
        if "status" not in r and (r["non_finite_reported"] or r["non_finite_in_csv"] > 0)
    ]
    unreadable = [r for r in rows if "status" in r]

    # A mesh that ran cleanly but traced nothing is not an error — a featureless model genuinely
    # has no lines to find — but it must not disappear into an "OK" summary either. The per-mesh
    # log carries the tool's explanation of why.
    empty = [r for r in rows if "status" not in r and r["lines"] == 0]

    print()
    if empty:
        print(f"  WARNING: {len(empty)} mesh(es) produced no lines:")
        for row in empty:
            print(f"    {row['model']:<12} see {log_dir / (row['model'] + '.log')}")
        print()

    if failures:
        print(f"  FAILED: {len(failures)} mesh(es) produced no report:")
        for name, code in failures:
            hint = " (non-finite values)" if code == NON_FINITE_EXIT else ""
            print(f"    {name}  exit {code}{hint}")
    if non_finite:
        print(f"  FAILED: {len(non_finite)} mesh(es) contain non-finite values:")
        for row in non_finite:
            print(
                f"    {row['model']}  reported={row['non_finite_reported']} "
                f"csv_rows={row['non_finite_in_csv']}"
            )
    if unreadable:
        print(f"  FAILED: {len(unreadable)} report(s) could not be read.")

    if failures or non_finite or unreadable:
        return 1

    print("  OK: every mesh processed, no non-finite values anywhere.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
