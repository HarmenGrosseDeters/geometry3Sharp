# JOB-2: fTetWild as a black box on the 40-mesh Thingi10K subset (owner 2026-11-09)

Purpose: compare our tet meshing targets (100 % valid, min dihedral >= 10 degrees, time in the same order of magnitude)
with a measured fTetWild run instead of the paper's histograms. Same runner rules as JOB-1.

**Licence rule (binding):** fTetWild is MPL-2.0. Build and run the binary only. Do not open, read, copy or quote any
source file of fTetWild (README build instructions are fine). Nothing of it goes into our repository: only our own CSV and report.

1. Prerequisite: JOB-1 finished (the 40 meshes are in `<mesh dir>`); same machine, nothing else running (timing).
2. Our evaluator: clone geometry3Sharp, `git checkout claude/tet-fetwild-eval` (or master once it has `TetFiles.cs`),
   `dotnet build benchmarks/TetMeshBenchmark -c Release` (0 errors). Self-check of the evaluator (must print `yes`
   in the valid column and 2269 tets): `dotnet <dll> export <mesh dir>/100026.stl /tmp/rt.msh && dotnet <dll> eval /tmp/rt.msh rt 0 1`.
3. Build fTetWild: `git clone --recursive https://github.com/wildmeshing/fTetWild`, then follow its README build steps
   (cmake, Release, make -j). Note the commit hash (`git rev-parse HEAD`) and the binary path (`FloatTetwild_bin`).
   Do not read source files. If the build fails, report the error output (build tool messages only) and stop.
4. Settings. Variant `default`: `-i <mesh> -o <out>` and nothing else (its own defaults). Variant `paper`: the paper
   states envelope epsilon = 1e-3 * d and ideal edge length d / 20 (d = bounding box diagonal) and stop at max AMIPS energy 10 / 80 iterations;
   get the option names from `FloatTetwild_bin --help` and set `PAPER_FLAGS` to the options for ideal edge length
   relative 1/20 (0.05), envelope relative 1e-3, and threads = 8 (or as many as the machine has; note the number). Run `default` first.
   If an option is not clear from `--help`, do not guess: report the `--help` text to the coordinator.
5. Run: `bash jobs/run-ftetwild.sh <repo> <mesh dir> <binary> <work dir> results-job2-default.csv default <threads>`;
   then, with `PAPER_FLAGS` exported, `... results-job2-paper.csv paper <threads>`.
   Output per mesh (columns of `TetFiles.Header`): name, time_s (wall clock), threads, tets, vertices, inverted_fixed
   (tets the evaluator had to re-orient), valid (the complex is a closed valid tet mesh: positive tets, matched facets,
   manifold derived boundary; `yes` or the reason), radius-edge worst / 5 % / median / share above 2, min dihedral worst / 1 % / 5 % /
   median, share of tets below 10 degrees, `dih_ge10_all` (every tet of the mesh >= 10 degrees). TIMEOUT / NO_OUTPUT rows for failures.
6. Push both CSVs, `uname -a`, `nproc`, the fTetWild commit hash, the exact command lines and the `--help` text into
   `REPORT-2.md` on branch `runs/tet-meshing`. No meshes, no fTetWild output files.
7. One-line report to the coordinator: meshes valid per variant, median time_s, how many meshes have every tet >= 10 degrees
   (`dih_ge10_all`), median of the per-mesh worst min dihedral.

Evaluator note: it reads MEDIT `.mesh` and Gmsh MSH 2.x ASCII only. If fTetWild's output is another variant (MSH 4, binary),
report the first lines of the file; do not convert by hand.
