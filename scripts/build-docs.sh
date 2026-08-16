#!/usr/bin/env bash
#
# Generates the API reference and the documentation site into _site/.
#
# DocFX reads the *compiled* assemblies and their XML documentation files rather than the
# sources, so a Release build has to happen first. See the comment in docfx.json for why.
#
#   ./scripts/build-docs.sh          generate
#   ./scripts/build-docs.sh --serve  generate, then serve on http://localhost:8080

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# DocFX is a global dotnet tool and lands here, which is not always on PATH.
export PATH="$PATH:$HOME/.dotnet/tools"

if ! command -v docfx >/dev/null 2>&1; then
    echo "docfx not found. Install it with:" >&2
    echo "    dotnet tool install --global docfx --version 2.77.0" >&2
    echo >&2
    echo "Version 2.77.0 is pinned deliberately: it ships a net8.0 build, while 2.78+ requires" >&2
    echo "the ASP.NET Core 10 runtime, which the .NET SDK alone does not provide." >&2
    exit 1
fi

echo "==> Building the solution (Release), so the XML documentation is current"
dotnet build -c Release --nologo -v q

echo "==> Extracting API metadata from the compiled assemblies"
docfx metadata docfx.json

echo "==> Building the site"
docfx build docfx.json --warningsAsErrors

echo
echo "Documentation written to $repo_root/_site"
echo "Open _site/index.html, or re-run with --serve."

if [[ "${1:-}" == "--serve" ]]; then
    echo
    docfx serve _site --port 8080
fi
