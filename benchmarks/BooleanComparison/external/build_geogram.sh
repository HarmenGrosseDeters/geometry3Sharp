#!/usr/bin/env bash
# Builds external/geogram_bool.cpp against Geogram (BSD-3, Inria) on a GitHub-hosted runner, for the comparison of
# README.md, "Geogram". Never run in a development container: geogram is fetched, built and run on the runner only.
#   build_geogram.sh <work-dir>      -> <work-dir>/geogram_bool, <work-dir>/geogram_version.txt
# Needs cmake and libtbb-dev: geogram's library target links tbb on Linux for the parallel STL, whatever GEOGRAM_WITH_TBB says.
#   build_geogram.sh --check <dir>   only the path check of step 3 on <dir> (exit 1 and the paths if one is found; test_geogram.py)
#   build_geogram.sh --sparse <repo-url> <sha> <dir>   only the checkout of step 1 (test_geogram.py, from a local stand-in repository)
# Licence boundary (owner 2026-11-17): of geogram, only its own files under src/lib/geogram are built, plus the bundled
# permissive libraries its library target compiles in (amgcl, libMeshb, rply, OpenNL, zlib, Shewchuk's public-domain
# predicates, PoissonRecon, xatlas). Shewchuk's Triangle (src/lib/geogram/third_party/triangle, delaunay/delaunay_triangle.*)
# and every TetGen path (third_party/tetgen, delaunay/delaunay_tetgen.*) are excluded three ways:
#   1. a sparse, blob-less clone: their blobs are never downloaded, so the files are not on the runner at all;
#   2. GEOGRAM_WITH_TRIANGLE=OFF and GEOGRAM_WITH_TETGEN=OFF (geogram's own switches; its sources only include them under
#      #ifdef GEOGRAM_WITH_TRIANGLE / GEOGRAM_WITH_TETGEN);
#   3. checks that fail the build: no such path in the checkout, no such object file in the build, no Triangle or TetGen
#      symbol (triangulate, tetrahedralize, tetgenio) in the library.
set -euo pipefail
GEOGRAM_REPO=https://github.com/BrunoLevy/geogram.git
GEOGRAM_SHA=16440c45c6ec5015b50f4a430c5ea1a300c2542f   # the commit of docs/research/GEOGRAM_SURVEY_2026-11.md
here=$(cd "$(dirname "$0")" && pwd)

# the paths that must never be on the runner (sparse-checkout patterns, non-cone mode). mesh_tetrahedralize.cpp is geogram's own
# file, but it includes delaunay/delaunay_tetgen.h unconditionally, so it cannot be compiled without a TetGen path; the boolean
# does not use it (a tet mesher; nothing else in the library calls it, which the link of geogram_bool checks: an undefined
# symbol of libgeogram.so fails it)
EXCLUDED=(
  '/src/lib/geogram/third_party/triangle/'
  '/src/lib/geogram/third_party/tetgen/'
  '/src/lib/geogram/delaunay/delaunay_triangle.*'
  '/src/lib/geogram/delaunay/delaunay_tetgen.*'
  '/src/lib/geogram/mesh/mesh_tetrahedralize.cpp'
  # and, unanchored, any other copy of them anywhere in the tree (so that the download prevention is as wide as the check below)
  '**/third_party/triangle/'
  '**/*tetgen*'
  '**/*TetGen*'
  '**/*Tetgen*'
  '**/delaunay_triangle.*'
)

forbidden_paths() {   # prints every path below $1 that is Triangle or TetGen (case-insensitive)
  find "$1" -path "$1/.git" -prune -o \( -ipath '*tetgen*' -o -ipath '*third_party/triangle*' -o -iname 'delaunay_triangle*' \) -print
}

sparse_checkout() {   # <repo-url> <sha> <dir>: the commit without the blobs of EXCLUDED (they are never fetched)
  rm -rf "$3"
  git init -q "$3"
  git -C "$3" remote add origin "$1"
  git -C "$3" config core.sparseCheckout true
  git -C "$3" config core.sparseCheckoutCone false
  { echo '/*'; for p in "${EXCLUDED[@]}"; do echo "!$p"; done; } > "$3/.git/info/sparse-checkout"
  git -C "$3" fetch -q --depth 1 --filter=blob:none origin "$2"
  git -C "$3" checkout -q FETCH_HEAD
}

if [ "${1:-}" = --sparse ]; then      # only the checkout step, from any repository (test_geogram.py, with a local stand-in)
  sparse_checkout "${2:?}" "${3:?}" "$(realpath -m "${4:?usage: build_geogram.sh --sparse <repo-url> <sha> <dir>}")"
  exit 0
fi

if [ "${1:-}" = --check ]; then
  bad=$(forbidden_paths "$(realpath "${2:?usage: build_geogram.sh --check <dir>}")")
  [ -z "$bad" ] || { echo "$bad"; exit 1; }
  exit 0
fi

work=$(realpath -m "${1:?usage: build_geogram.sh <work-dir>}")
src=$work/geogram-src
mkdir -p "$work"
sparse_checkout "$GEOGRAM_REPO" "$GEOGRAM_SHA" "$src"
# the four submodules the library needs (the other ones belong to the viewer, which is not built)
for m in amgcl libMeshb rply OpenNL; do
  git -C "$src" -c protocol.file.allow=never submodule update -q --init --depth 1 "src/lib/geogram/third_party/$m"
done
bad=$(forbidden_paths "$src")
if [ -n "$bad" ]; then echo "excluded geogram paths in the checkout:"; echo "$bad"; exit 1; fi
echo "checkout: no Triangle or TetGen path"

cmake -S "$src" -B "$work/geogram-build" -DCMAKE_BUILD_TYPE=Release -DVORPALINE_PLATFORM=Linux64-gcc-dynamic \
  -DGEOGRAM_LIB_ONLY=ON -DGEOGRAM_WITH_GRAPHICS=OFF -DGEOGRAM_WITH_TRIANGLE=OFF -DGEOGRAM_WITH_TETGEN=OFF \
  -DGEOGRAM_WITH_LUA=OFF -DGEOGRAM_WITH_HLBFGS=OFF -DGEOGRAM_WITH_LEGACY_NUMERICS=OFF \
  -DGEOGRAM_WITH_FPG=OFF -DGEOGRAM_WITH_TBB=OFF > "$work/geogram-cmake.log" || { tail -n 40 "$work/geogram-cmake.log"; exit 1; }
cmake --build "$work/geogram-build" --target geogram -j "$(nproc)" > "$work/geogram-build.log" || { tail -n 60 "$work/geogram-build.log"; exit 1; }
bad=$(forbidden_paths "$work/geogram-build")
if [ -n "$bad" ]; then echo "excluded geogram objects in the build:"; echo "$bad"; exit 1; fi
lib=$(find "$work/geogram-build" -name 'libgeogram.so*' -type f | head -n 1)
[ -n "$lib" ] || { echo "libgeogram.so not found"; exit 1; }
# the full symbol table (not only the exported symbols: the library is built with -fvisibility=hidden); a failing nm fails the build
nm --defined-only "$lib" > "$work/geogram-symbols.txt" || { echo "nm failed on $lib"; exit 1; }
[ -s "$work/geogram-symbols.txt" ] || { echo "no symbols in $lib (stripped?): the check cannot run"; exit 1; }
if grep -E -i ' (triangulate|tetrahedralize)$|tetgenio|tetgenmesh' "$work/geogram-symbols.txt"; then
  echo "Triangle or TetGen symbols in $lib"; exit 1
fi
echo "library: no Triangle or TetGen object or symbol ($(basename "$lib"))"

libdir=$(dirname "$lib")
g++ -O3 -DNDEBUG -std=c++17 -fopenmp -ffp-contract=off "$here/geogram_bool.cpp" -o "$work/geogram_bool" \
  -I "$src/src/lib" -I "$work/geogram-build/src/lib" -L "$libdir" -Wl,-rpath,"$libdir" -Wl,--no-allow-shlib-undefined -lgeogram -lpthread -ldl
ver=$(sed -n 's/^set(VORPALINE_VERSION_\(MAJOR\|MINOR\|PATCH\) \([0-9]*\))/\2/p' "$src/CMakeLists.txt" | paste -sd.)
echo "geogram $ver commit $GEOGRAM_SHA, built with GEOGRAM_WITH_TRIANGLE=OFF GEOGRAM_WITH_TETGEN=OFF, threads $(nproc)" > "$work/geogram_version.txt"
cat "$work/geogram_version.txt"
