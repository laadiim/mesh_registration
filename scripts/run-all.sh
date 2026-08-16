#!/usr/bin/env bash
#
# Runs `meshreg trace` over every mesh in a directory, in parallel, and aggregates the results
# into one summary table.
#
#   ./scripts/run-all.sh                          all of data/ into out/
#   ./scripts/run-all.sh --jobs 4                 limit parallelism
#   ./scripts/run-all.sh --data other --out o2    different directories
#   ./scripts/run-all.sh -- --lines 100 --seed-spacing 0.02
#                                                 pass options through to meshreg
#
# Exits non-zero if any mesh failed or if any output contains a non-finite value.

set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

data_dir="data"
out_dir="out"
jobs="$(nproc 2>/dev/null || echo 4)"
command="trace"
extra_args=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --data)    data_dir="$2"; shift 2 ;;
        --out)     out_dir="$2";  shift 2 ;;
        --jobs|-j) jobs="$2";     shift 2 ;;
        --inspect) command="inspect"; shift ;;
        --)        shift; extra_args=("$@"); break ;;
        -h|--help)
            # Print the leading comment block, however long it happens to be.
            awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"
            exit 0 ;;
        *)
            echo "Unknown option: $1 (use -- to pass options through to meshreg)" >&2
            exit 64 ;;
    esac
done

if [[ ! -d "$data_dir" ]]; then
    echo "Input directory '$data_dir' does not exist." >&2
    echo "The sample meshes are not in the repository; unpack them with:" >&2
    echo "    mkdir -p data && unzip -o ../vasa-projekt/Data.zip -d data" >&2
    exit 66
fi

mapfile -t meshes < <(find "$data_dir" -maxdepth 1 -type f -name '*.obj' | sort)

if [[ ${#meshes[@]} -eq 0 ]]; then
    echo "No .obj files found in '$data_dir'." >&2
    exit 66
fi

log_dir="$out_dir/logs"
mkdir -p "$log_dir"

echo "==> Building (Release)"
if ! dotnet build -c Release --nologo -v q; then
    echo "Build failed." >&2
    exit 1
fi

cli="src/MeshRegistration.Cli/bin/Release/net10.0/meshreg"
if [[ ! -x "$cli" ]]; then
    echo "Expected the built CLI at $cli but it is not there." >&2
    exit 1
fi

echo "==> Running '$command' on ${#meshes[@]} mesh(es), $jobs at a time"
[[ ${#extra_args[@]} -gt 0 ]] && echo "    extra options: ${extra_args[*]}"
echo

batch_started=$(date +%s%N)

# One worker per mesh. Each writes its own log and records the exit code alongside it, so the
# aggregation step below never has to guess whether a missing report means a crash.
run_one() {
    local mesh="$1"
    local name
    name="$(basename "$mesh" .obj)"
    local log="$log_dir/$name.log"
    local started
    started=$(date +%s%N)

    "$cli" "$command" "$mesh" --out "$out_dir" "${extra_args[@]}" >"$log" 2>&1
    local status=$?

    local elapsed=$(( ($(date +%s%N) - started) / 1000000 ))
    echo "$status" >"$log_dir/$name.exit"
    echo "$elapsed" >"$log_dir/$name.ms"

    if [[ $status -eq 0 ]]; then
        printf '  %-10s ok      %6d ms\n' "$name" "$elapsed"
    else
        printf '  %-10s FAILED  %6d ms  (exit %d, see %s)\n' "$name" "$elapsed" "$status" "$log"
    fi
}
export -f run_one
export cli command out_dir log_dir
export extra_args_str="${extra_args[*]:-}"

# xargs cannot carry a bash array through, so rebuild it inside the worker from a string.
printf '%s\0' "${meshes[@]}" | xargs -0 -P "$jobs" -I{} bash -c '
    read -ra extra_args <<< "${extra_args_str:-}"
    run_one "$@"
' _ {}

batch_elapsed=$(( ($(date +%s%N) - batch_started) / 1000000 ))

echo
if [[ "$command" == "inspect" ]]; then
    echo "==> inspect writes no files; see $log_dir for the per-mesh reports."
    exit 0
fi

echo "==> Summary"
python3 scripts/summarise-runs.py --out "$out_dir" --logs "$log_dir" --elapsed-ms "$batch_elapsed"
exit $?
