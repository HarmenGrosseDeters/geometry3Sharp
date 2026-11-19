# P4(a) local alpha: spike report (2026-11-19, data branch runs/tet-local-alpha, never merged)

Setup: 39 of the 40 meshes of TET_MESHING.md 10.1 (120628, 126 MB, skipped; fetched at run time, not committed) plus 7 procedural inputs (exact unions of
boxes: comb with 0.03 gaps, U channel, L bracket, plates with a thin gap; sphere, box, thin plate). Offset diagonal / 600, one process at a time, 4-core
container (read times as orders of magnitude). Deviation = input to boundary at 20000 area-weighted input samples, relative to the diagonal.
Rows and scripts: `results/`. Code: `Bound.cs`, `Local.cs`; the wrap hook is commit 3ec740da (`MeshAlphaWrap.AlphaField`, spike only).

## 1. Upper bound (not small)
- Where the distance is (alpha / 40 wrap, 39 meshes): the area with distance > alpha/2 is a median 3.3 % of the input (p10 0.5 %, p90 17.6 %). Of those samples
  (32 meshes with > 1 %): 49 % lie in narrow gaps (free distance along the outward normal < 2 alpha), 26 % in thin parts only, the rest open (creases).
  Irreducible ("buried": distance does not fall at alpha / 160): median 0, p90 27 %, above 25 % on 5 meshes.
- Model (`Bound.cs`: per sample the coarsest level whose uniform wrap meets the target, tets of each uniform mesh attributed by nearest sample, transition band of
  one alpha / 40; ignores grading): to reach the max deviation of uniform /80, tets = 0.38x (strict) / 0.45x (dilated) of /80's, median; to reach /160's max: 0.45x / 0.77x of /80
  (0.20x of /160's). A p95 target is at the offset floor (2/600) on most meshes, so it asks for fine alpha almost everywhere: no gain there (1.2x).
  Dense-detail shapes (comb) gain nothing: 0.8-1.1x.

## 2. Library
No spatial alpha or offset exists: `Alpha`, `Offset` are scalars; `MaxEstimatedTriangles` / `MaxAlphaCoarsening` apply one factor to the whole input. alpha enters carving at four
gate tests (`carve_serial` stale test, `evaluate`, `add_push`, `push_gate`: Phi(f) > alpha) and in the Steiner separation min(offset, alpha - offset); `MeshTetrahedralization`
uses alpha only through the wrap (floor and slide cap are offset based). Paper section 8: alpha and delta global, spatial variation is future work.
Guarantees: input inside, closed, manifold come from the rules 1 / 2 and the manifold step, which do not read alpha; alpha only decides which facets are gates. Termination and
the separation need alpha_local >= 3 offset (field clamped there). Not a bound: the wrap's "no ball of radius alpha fits" meaning becomes "Phi(f) <= alpha(facet centroid)".
Checked on every run: exact validity, boundary free of self-intersection, boundary vs input trees no intersection: 39/39 + 7/7 for all variants.

## 3. Prototype (alpha field = graded size function, alpha(x) = min_j (alpha_j + L |x - p_j|), L = 0.25; Persson 2006 gradient limiting)
Estimators: **post** = one extra pass, alpha_j = alpha40 * T / d40(p_j) clamped to [alpha/160, alpha/40] (a posteriori indicator from the /40 wrap; T = uniform /80's p95 floored at 2 offsets),
post2 = same rule on post's distances; **lfs** = a priori, alpha_j = g_j / 3, g_j = free distance along the outward normal (ray estimate of local feature size: Amenta, Bern 1999; Shapira et al. 2008);
oracle = levels from the uniform runs (not a method). post_160: T = uniform /160's p95.

Medians over 39 meshes (ratios per mesh, then median; "eq" = tets divided by the tets of the log-log interpolated uniform alpha with the same max deviation):
| variant | tets / u80 | max dev / u80 | p95 dev / u80 | time / u80 (incl. pass 1) | tets / eq | tets, max dev (abs) | every tet >= 10 deg |
|---|---:|---:|---:|---:|---:|---|---:|
| uniform 40 | 0.37 | 1.55 | 2.21 | 0.41 | | 7.0k, 0.0268 | 39 |
| uniform 80 | 1 | 1 | 1 | 1 | | 18.7k, 0.0148 | 39 |
| uniform 160 | 3.75 | 0.60 | 0.58 | 5.55 | | 68k, 0.0085 | 39 |
| post (T /80) | 0.65 | 0.99 | 1.00 | 1.45 | 0.51 | 10.5k, 0.0135 | 37 (8.1, 8.8) |
| post2 (T /80) | 0.73 | 0.87 | 0.88 | 2.67 | 0.44 | 12.8k, 0.0119 | 39 |
| post (T /160) | 0.75 | 0.81 | 0.91 | 1.86 | 0.42 | 13.5k, 0.0107 | 38 |
| post2 (T /160) | 0.89 | 0.81 | 0.81 | 3.31 | 0.46 | 16.0k, 0.0107 | 38 (9.78) |
| lfs (one pass) | 0.64 | 1.00 | 1.18 | 1.13 | 0.58 | 11.1k, 0.0201 | 39 |
| oracle | 0.85 | 0.83 | 0.80 | 1.41 | 0.50 | 14.3k, 0.0113 | 39 |
Per mesh: post has dev <= u80 and fewer tets on 26 of 39 (post2 29, lfs 14). L = 0.5 is worse (10 meshes: dev 1.10x at 0.52x tets, tets/eq 0.75 vs 0.45); L = 0.125: 0.82x tets, no better.
Procedural: u channel / L bracket: max dev 0.010 / 0.0074 at 16k / 9k tets vs /80 0.012 / 0.011 at 46k / 24k; plates_gap (gap 1.6 alpha80): only post_160 and lfs enter it (55k / 58k tets, max 0.0075 / 0.0081 vs /160 170k, 0.0060); comb: no gain; sphere / box / plate: unchanged, as it should.
Time: pass 1 is the whole /40 tet run here (the wrap alone is cheaper, not measured); the field costs 10-70 % of the local run (grid ball query, ~2 us per call; an octree or lattice would cut it).
Floor: size jumps cost the 10 degree floor on 3 of 39 (post 8.1, 8.8; post2_160 9.78) and plates_gap post2_160 7.1; not tuned.

## 4. Leaning: go, opt-in, narrow
Fidelity of uniform /80 at about 0.65-0.75x its tets (about 0.45x the interpolated uniform tets), or 0.81x its deviation at 0.75-0.9x tets; /160-level max dev (0.0085) is not reached with a floor at /160 (0.0107). Not a time gain (1.0-1.5x). Gap vs fTetWild (0.001) stays ~10x.
Options: (A) library PR, opt-in `MaxSurfaceDeviation` on `MeshTetrahedralization` with post2-style two passes + the `AlphaField` hook (additive; 1-1.5 weeks, not 2-3: guarantees hold by construction; needs invariance / reuse / determinism tests, floor fix, a cheaper field, wrap-only pass 1);
(B) a-priori lfs only: cheap (+0.15 s), tets/eq 0.58, no second wrap, but 14 of 39 wins, only gap regions; (C) no-go: the gain is a third of the tets, the floor regresses, the time does not improve. Leaning A if tets (FEM size) matter more than time; owner decision (new option, default unchanged).

## 5. Floor and field cost (2026-11-19, owner go for option A, step 1)
**Floor: fixed, by turning the wrap's own surface-angle refinement off on the local-alpha path** (`SurfaceMinAngleDegrees = 0`; default 15 for uniform alpha stays).
The 4 failing cases were 49884 post 8.81, 103538 post 8.11, 44041 post2_160 9.78, plates_gap post2_160 7.06. More improver passes did nothing
(`ImprovePasses` 200, `RescuePasses` 600: same angles), so the misses are structural (the wrap's quality refinement adds short surface edges where alpha jumps). With 0:
all 4 reach >= 10 (10.01, 10.01, 10.007, 10.006); post and post2 (T /80) on all 39 + 7: 0 misses, all valid, no self-intersection, boundary vs input
trees disjoint (`results/*surfmin0*`); post_160 / post2_160 / postLhalf checked on 44041 and the 7 procedural inputs (all >= 10). A bonus: fewer tets
(post 0.58x of /80's, was 0.65x; post2 0.72x, was 0.74x; max deviation unchanged 0.99x / 0.87x; /80 here is the surfmin-0 uniform run).
Not covered: post_160 / post2_160 on the other 38 Thingi meshes with surfmin 0 (not rerun). The library must still report a missed target through `TetTargetReport` (it does today: `Targets.MinDihedralMet`); no silent path.
**Field cost: not made cheaper.** Measured: 2.1 us per call, 0.24 s CPU median (max 1.27 s) per local run = 37 % of its wall time (CPU summed over threads, so an upper bound).
Tried: ring-wise early-out of the ball query (same results, 40 of 40 identical) but 7x slower (the dictionary grid pays per cell); thinning to one sample per alpha_min / 2 cell does nothing at 20000 samples. Not tried: a lattice or octree with interpolation
(changes results, needs a bound on the interpolation error), fewer field samples. For the library: pass 1 can be the wrap alone (no tets), and the field build can reuse the wrap's AABB tree; both are engineering, not research.
