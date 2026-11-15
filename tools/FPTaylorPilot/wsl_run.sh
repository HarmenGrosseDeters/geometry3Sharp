#!/usr/bin/env bash
# Run the FPTaylor pilot end to end inside a Linux x86-64 environment (WSL, or any Linux host).
#
# usage: wsl_run.sh <repository-root> <installation-directory> [--full]
#
# The repository root must be a Linux-side copy with LF line endings (wsl_setup.ps1 makes one). The FPTaylor
# toolchain is installed once into the installation directory (install.sh refuses an existing directory, so an
# existing installation is reused as is). Then the smoke run, the Python tests and the ux and nx comparisons run.
# --full adds the generated expressions of compare_filter_expressions.py (about 35 minutes).
set -euo pipefail
if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then
    echo "usage: $0 <repository-root> <installation-directory> [--full]" >&2
    exit 2
fi
root=$(cd "$1" && pwd)
install=$2
full=${3:-}
if [ -n "$full" ] && [ "$full" != "--full" ]; then
    echo "unknown option: $full" >&2
    exit 2
fi
tools="$root/tools/FPTaylorPilot"
py=${PYTHON:-python3}
base="$install/FPTaylor-b5a77cae348400f21f83512210d9f43c4bffb381"

if [ ! -x "$base/fptaylor" ]; then
    if [ -e "$install" ]; then
        echo "installation directory exists but FPTaylor is missing: $install (use a fresh directory)" >&2
        exit 2
    fi
    JOBS=${JOBS:-4} bash "$tools/install.sh" "$install"
else
    echo "reusing FPTaylor installation: $base"
fi

bash "$tools/run.sh" "$base" "$tools/smoke.txt" > /dev/null
echo "smoke run: ok"

if [ -z "${WSL_RUN_SKIP_UNIT_TESTS:-}" ]; then
    (cd "$tools" && "$py" -m unittest discover -s . -p 'test_*.py')
fi
"$py" "$tools/compare_ux.py" "$base"
"$py" "$tools/compare_nx.py" "$base"
if [ "$full" = "--full" ]; then
    "$py" "$tools/compare_filter_expressions.py" "$base"
fi
echo "wsl_run: all steps passed"
