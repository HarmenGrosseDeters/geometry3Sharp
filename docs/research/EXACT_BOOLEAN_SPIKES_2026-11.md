# Exact 3D Boolean Next-Phase Pre-Implementation Spikes & Z3 Proofs (2026-11-28)

Empirical results of all four pre-implementation spikes from [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (`4C-0`, `4A-2-0`, `3-0`, `2B-0`) plus formal Z3 verification (`tools/Z3Pilot/boolean_next_phase_spikes.py`, pinned `z3-solver==4.15.4.0`). All spikes ran on `master` (post-`#1744` `RegionClipping` default-on, post-`#1720` rounding separating axes) with **zero library code changes** in `mesh_ops/` or `math/`.

## Executive Verdict Matrix

| Spike | Gate Question | Empirical Result | Z3 Verification | Verdict |
| :--- | :--- | :--- | :--- | :--- |
| **`4C-0`** (`abc-chained` post-`#1744`) | Does `4C-1` (self-join skip) clear $\ge 15\%$ of chained wall time? | `ours`: **32/36** all-steps-clean (0 crashes) vs Geogram **3/36** (21 crashes), CGAL **1/36**. Median `ours/geogram` = **`0.49×`** across 77 shared steps (`0.36×` / `0.66×` / `1.58×` median, `0.66×` total wall time on Step 3). **`intersections.pairs` (cross + self combined) is only `2.9%` on Step 3 (`4.6%` on Step 2).** | N/A (runtime telemetry) | **`4C-1` NO-GO** (fails 15% bar by >5×) |
| **`4A-2-0`** (`UnionMany` density predictor) | Can a cheap overlap predictor route dense vs sparse unions without regressing sparse families? | `batch16` is **6.4×–11.0× faster** on dense packings. Naive AABB fails `lattice_216` (`1.81×` slower); AABB & 4×4×4 sub-box both fail thin `diag_rods_144` (`2.05×` slower). **`Sphere ∧ PCA-OBB`** achieves **24/24 perfect separation** (`max sparse = 6.0` vs `min dense = 11.6`, `1–38 ms`). | **3 UNSAT + 5 SAT** (conservative filter: zero missed overlaps + mutants) | **`4A-2` GO** (`N >= 24 && deg >= 11.0`) |
| **`3-0`** (Differentiable volumetric CSG) | Do $M_1, M_2, SE(3)$ match central FD? Does untouched-tri fast path cut overhead `< 20%`? | Central FD agreement **`1e-10..1e-8`** (`identity`, `rotated`, `scaled_2e-10`), `4.9e-7..1.45e-5` on `UTM_1e7_rot` ($h = 10^{-3}$). **92.4%–95.8%** kept tris untouched → `fastV` drops from `137%–171%` to **`1.9%–2.4%` (`~88×` faster)**; full `V + M1 + M2` = **`13.5%–16.4%`**; `SkipRounding` step **`2.2×–3.9×`** faster. | **4 UNSAT + 4 SAT** (`TriSq`, `TriCube`, `TriInt2`, `TriInt3` exact simplex identities + mutants) | **`3` STRONG GO** |
| **`2B-0`** (`4A-1` DFS + `2B` coupled rounding) | Is `4A-1` exact & fast? Does Sheet-Decomposed Coupled Rounding clear $\ge 98\%$ clean? | **Part A (`4A-1`):** `mis = 0, unv = 0` on all **861,508 pieces** at 192 balls; DFS = **`50.9 ms`**, but unfiltered `BigInteger` `radial_order` on 119k deduped degree-4 edges = `5,246 ms` → needs filtered degree-4 `Orient3d` fast path.<br>**Part B (`2B`):** Policy R1 pinned seams stalls at `21/54` (`38.9%`). **Policy R2 coupled seam moves** achieves **`48/48` (`100.0%`)** clean on all resolvable 3-, 4-, and 8-material junctions (incl. `0.1°` acute & rotated UTM `5.5e6`). Only 3 leftovers (`3mat_1ulp_coplanar`) are `shared=3` collapsed double-faces. | **2 UNSAT + 8 SAT** (degree-4 `Orient3d` fast path: 1 test flat, 4 tests non-flat crease necessary & sufficient; pentagram double-wrap SAT at $n=5$)<br>**1 UNSAT + 2 SAT** (`shared=3` impossibility vs `shared=2` separability) | **`4A-1 + 2B` FULL GO** (with 2 locked design fixes) |

---

## 1. Spike `4C-0`: Post-`#1744` `abc-chained` Re-Measurement (`4C-1` NO-GO)

- **Command / Workflow:** GitHub Actions `abc-chained.yml`, run [`46138598811`](https://github.com/HarmenGrosseDeters/geometry3Sharp/actions/runs/46138598811) on `master` (`2959dfaa`), `chunk=0000`, `chains_per_variant=12`, `methods=ours,geogram,cgal_epeck`, `threads=all` (4 cores). Result rows published on branch `runs/abc-chained` (`run-46138598811/`).
- **Cleanliness & Speed Summary (36 chains across `ucd`, `dui`, `iud`):**

| Variant | Method | Steps run (1/2/3) | Clean or empty (1/2/3) | All steps clean | Unclean / failure reasons | Median ms, ok results (1/2/3) |
| :--- | :--- | :---: | :---: | :---: | :--- | :---: |
| `ucd` | `ours` | 12/12/12 | 11/9/11 | **9/12** | pinches 5 | 297 / 364 / 1454 |
| `ucd` | `geogram` | 12/12/12 | 10/6/1 | 1/12 | crash 6, degen 9, not closed 3, pinches 1 | 691 / 686 / 2759 |
| `ucd` | `cgal_epeck` | 12/11/9 | 9/5/1 | 1/12 | degen 14, failed 3 | 66 / 98 / 1532 |
| `dui` | `ours` | 12/12/12 | 12/12/12 | **12/12** | – | 218 / 309 / 1506 |
| `dui` | `geogram` | 12/12/7 | 12/6/0 | 0/12 | crash 9, degen 3, not closed 1 | 721 / 317 / 430 |
| `dui` | `cgal_epeck` | 12/12/12 | 12/6/0 | 0/12 | degen 6, not closed 6, rejected 6 | 32 / 98 / 1182 |
| `iud` | `ours` | 12/11/11 | 12/11/11 | **11/12** | – (1 empty early) | 112 / 210 / 1319 |
| `iud` | `geogram` | 12/11/8 | 12/5/2 | 2/12 | crash 6, degen 3, not closed 3 | 455 / 217 / 306 |
| `iud` | `cgal_epeck` | 12/11/11 | 11/4/1 | 0/12 | degen 7, failed 3, not closed 3, rejected 5 | 17 / 61 / 1165 |

- **`ours` vs. Geogram (`threads=all`, 77/77 volumes agree within `3.4e-11`):**
  - Step 1 (`n=36`): median **`0.36×`**, p90 `0.57×`, max `1.04×`.
  - Step 2 (`n=27`): median **`0.66×`**, p90 `1.76×`, max `2.85×`.
  - Step 3 (`n=14`): median `1.58×` (p90 `3.63×`, max `5.16×`), yet **total wall time `ours = 18,865 ms` vs. `geogram = 28,641 ms` (`0.66×` faster in sum)**; bimodal (`0.17×–0.44×` on 6/14 chains; `1.38×–5.16×` on 8/14 chains where Geogram skips safe rounding).
  - All 77 shared steps combined: median **`0.49×`** (2× faster overall).
- **Per-phase breakdown of `ours` (`run-46138598811/results.jsonl`, chosen op):**

| Phase / Sub-phase | Step 1 (`8,515 ms`) | Step 2 (`24,779 ms`) | Step 3 (`69,225 ms`) |
| :--- | :---: | :---: | :---: |
| `rounding` | 24.1% (`2,048 ms`) | 23.1% (`5,716 ms`) | **33.2%** (`22,989 ms`) |
| `intersections` (total) | 39.6% (`3,372 ms`) | **47.2%** (`11,702 ms`) | **31.4%** (`21,718 ms`) |
| └─ `intersections.intersect` | 7.9% (`675 ms`) | **31.1%** (`7,716 ms`) | **18.7%** (`12,937 ms`) |
| └─ `intersections.planes` | 9.8% (`834 ms`) | 7.5% (`1,848 ms`) | 7.8% (`5,399 ms`) |
| └─ `intersections.pairs` *(cross + self-join)* | **7.8%** (`664 ms`) | **4.6%** (`1,138 ms`) | **2.9%** (`1,982 ms`) |
| └─ `intersections.trees` | 11.6% (`990 ms`) | 3.5% (`860 ms`) | 1.4% (`977 ms`) |
| `classification` | 7.4% (`626 ms`) | 10.7% (`2,645 ms`) | **15.4%** (`10,679 ms`) |
| `triangulation` | 2.8% (`240 ms`) | 3.0% (`731 ms`) | 7.9% (`5,453 ms`) |
| `merge` | 5.7% (`489 ms`) | 6.0% (`1,493 ms`) | 7.6% (`5,267 ms`) |
| `assembly` | 9.7% (`826 ms`) | 5.5% (`1,367 ms`) | 2.7% (`1,851 ms`) |

- **Key finding:** On Step 3, `rounding_degenerate` jumps `5 → 16,897 → 175,462` and `rounding_removed_components` hits `816` (`107,394` sub-ulp `BelowResolution` / `ZeroVolume` sliver triangles on `iud`, where both Geogram and CGAL crash). Meanwhile, **`intersections.pairs` (where `4C-1` self-join skip operates) is only `2.9%` on Step 3 and `4.6%` on Step 2** — failing the `>= 15%` stop rule by >5×. **`4C-1` is decisively dropped (`NO-GO`).** Conversely, `classification` (`15.4%` on Step 3, `18.3%` on the 14 shared chains) directly benefits from `4A-1`.

---

## 2. Spike `4A-2-0`: `UnionMany` Overlap-Degree Predictor (`4A-2` GO)

- **Environment:** `net10.0` Release, 2 vCPUs Intel Xeon 2.30 GHz; 24 procedural cases across 8 families (dense spheres `dense104`/`dense103`, dense boxes, dense cylinders, sparse `helix`, sparse cubic `lattice_216`, adversarial thin diagonal rods `diag_rods_144`, and `disjoint_boxes`).
- **Performance (`batch16`: Morton-ordered groups of 16 + balanced tree vs. 1-pass `ExactMeshBoolean.Union`):**
  - Dense packings: **6.4×–11.0× faster**, 100% clean, volume relative error `< 1e-10`:
    - `dense104_192`: `20,081 ms → 3,115 ms` (`0.16×`)
    - `dense_cyls_96`: `13,818 ms → 1,696 ms` (`0.12×`)
    - `dense_boxes_144`: `30,364 ms → 2,758 ms` (`0.09×`)
    - `dense103_96`: `3,399 ms → 1,039 ms` (`0.31×`)
  - Sparse packings: 1-pass wins (`batch16` is `1.28×` slower on `helix_192`, `1.81×` slower on `lattice_216`, `2.05×` slower on `diag_rods_144`).
- **Adversarial predictor audit:**
  - Naive AABB overlap degree fails `lattice_216` (`aabb_deg = 18.0`, yet `batch16` is `1.81×` slower).
  - BOTH AABB and 4×4×4 sub-box decomposition fail `diag_rods_144` (`aabb_deg = subbox_deg = 140.0` because 1-segment rod side triangles span the diagonal cube, yet `batch16` is `2.05×` slower).
  - **`Sphere ∧ PCA-OBB`** (tight bounding sphere at `ContOrientedBox3.Box.Center` + 15-axis SAT on `ContOrientedBox3`) achieves **24 / 24 perfect separation** in `1–38 ms`:
    - Max sparse / small: `dense104_12 = 6.0`, `diag_rods_144 = 5.1`, `lattice_216 = 5.0`, `helix_192 = 2.8`, `disjoint = 0.0`.
    - Min dense (`batch16` wins): `dense_boxes_24 = 11.6`, `dense103_48 = 12.4`, `dense104_48 = 14.2`, `dense_cyls_96 = 25.0`, `dense104_192 = 33.9`.
- **Locked routing rule for `4A-2`:** `N >= 24 && meanDegree(Sphere ∧ PCA-OBB) >= 11.0`.

---

## 3. Spike `3-0`: Differentiable Volumetric CSG (`3` STRONG GO)

- **Exact Hadamard $V, M_1, M_2, SE(3)$ finite-difference verification:**
  - Central differences across `Union`, `Intersection`, `Difference` on overlapping spheres/boxes/cylinders match analytical gradients to **`1e-10..1e-8`** on `identity`, `rotated`, and `scaled_2e-10`, and **`4.9e-7..1.45e-5`** on `UTM_1e7_rot` (at $h = 10^{-3}$, where `1 ulp ≈ 9.3e-10` at `5.5e6`). Zero missed intersection-curve terms across all continuous volumetric moments.
- **Untouched-triangle fast path + `SkipRounding` benchmark (`49k + 49k` spheres, 5 reps median):**
  - **92.4%–95.8%** of kept arrangement pieces are untouched input triangles.
  - Current `BooleanVolumeGradient` overhead: **`137%–171%`** (`676–689 ms` vs. `402–412 ms` boolean) because it calls `BigInteger` `arr.Exact(id)` on every untouched triangle vertex.
  - Untouched-triangle fast path (`fastV`): **`5.8–9.7 ms`** (**`1.9%–2.4%`** of boolean — **~88× faster**).
  - Full `V + M1 + M2` (`fastVM1M2`): **`37.3–66.1 ms`** (**`13.5%–16.4%`** of boolean — **11× faster** than current volume-only gradient).
  - Full optimization step (`SkipRounding` + `fastV`): **`2.2×–3.9×`** faster per iteration.

---

## 4. Spike `2B-0`: `4A-1` Cell-Graph DFS + `2B` Coupled Multi-Material Rounding (`FULL GO`)

- **Part A (`4A-1` Patch-Level Cell-Graph DFS on `dense104` 12..192 balls):**
  - **Exact agreement:** `mis = 0, unv = 0` across all **861,508 pieces** at 192 balls.
  - **Iterative DFS propagation:** **`50.9 ms`** (single `int[N]` state, zero $O(C \cdot N)$ memory).
  - Curve-level dedup reduced `radial_order` calls from `265,756 → 119,200` (`5,297 ms` vs. `13,574 ms` old winding = `2.6×` faster).
  - **Bottleneck identified:** Unfiltered `BigInteger` `ExactMeshBoolean.radial_order` on those 119k degree-4 edges takes `5,246 ms` (99% of `4A-1`). A **filtered degree-4 `Orient3d` fast path** (formally proved in Section 5 below) unlocks the full ~20–30× speedup.
- **Part B (`2B` Sheet-Decomposed Coupled Rounding — 9 adversarial fixtures × 6 frames = 54 cases, audited with exact `ExactTriangleIntersection`):**
  - **RAW double rounding:** `21 / 54` clean (`38.9%`).
  - **Policy R1 (pinned-seam per-sheet `MeshRoundingRepair` with `FixedHard = true`):** `21 / 54` (`38.9%`) — fails alone because triple-junction defects sit at the seam vertices.
  - **Policy R2 (coupled seam-vertex moves across all incident sheets):** **`48 / 48` (`100.0%`) CLEAN** on every resolvable 3-, 4-, and 8-material junction — including `0.1°` acute dihedral seams, `4mat_quad_spheres` (960 tris, 5 quad corners: `33d/69x → 0d/0x` in 41 ms), `3mat_nested_cut`, `8mat_dense_boxes` (33 sheets, 329 triple edges, 48 quad corners), and rotated UTM `5.5e6`. Overall: `51 / 54` (`94.4%`).
  - **Root cause of the only 3 leftovers (`3mat_1ulp_coplanar@s2e-20`, `@s2e+20`, `@UTM_rot`):** Exact dumps proved all 3 contain **`shared = 3` collapsed double-faces** (two sheets sharing the exact same 3 vertex IDs from a 1-ulp slab collapsing to zero thickness under double rounding). Combinatorially unresolvable by *any* vertex move (proved in Z3 below); requires sub-ulp thin-cell collapse or honest `RoundingNotClean` refusal with `shared=3` witness.

---

## 5. Formal Z3 Verification (`tools/Z3Pilot/boolean_next_phase_spikes.py`)

- **Command / CI run:** `.github/workflows/z3-pilot.yml` (`z3-solver==4.15.4.0`), Actions run [`46141765132`](https://github.com/HarmenGrosseDeters/geometry3Sharp/actions/runs/46141765132) — **PASS in 11 s** (all 30 obligations inside the 10 s per-suite fast gate: **10 UNSAT + 20 SAT**, 0 UNKNOWN, every UNSAT obligation paired with SAT non-vacuity and controlled SAT mutants):
  1. **`4A-1` Degree-4 Transverse `Orient3d` Fast Path (`2 UNSAT + 8 SAT`):**
     - Flat surface cut (`a2 = -a1, b2 = -b1`): **1** `Orient3d(u, v, a1, b1) > 0` proves exact CCW cyclic order $a_1 \to b_1 \to a_2 \to b_2$ (`UNSAT`; reversed-sign mutant `SAT`).
     - Arbitrary non-flat dihedral creases along edge $e$: **4** consecutive `Orient3d > 0` tests (`cross(a1,b1) > 0`, `cross(b1,a2) > 0`, `cross(a2,b2) > 0`, `cross(b2,a1) > 0`) are **both necessary and sufficient** (`UNSAT` against an independent non-circular half-plane total order; dropping *any* single one of the 4 admits a `SAT` counterexample!).
     - Theorem boundary: for $n = 5$ rays, 5 consecutive `Orient3d > 0` **fails** via a pentagram double-wrap (`SAT`), proving why degree $\ge 5$ must fall back to full `radial_order`.
  2. **`3-0` Exact Hadamard Moment Polynomial Identities (`4 UNSAT + 4 SAT`):**
     - Proved `TriSq`, `TriCube`, `TriInt2`, and `TriInt3` equal the exact analytical simplex averages $\frac{2!\,i!\,j!\,k!}{(i+j+k+2)!}$ for all reals (`UNSAT`), with 4 coefficient-typo mutants (`SAT`).
  3. **`2B-0` Combinatorial `shared=3` Impossibility vs `shared=2` Separability (`1 UNSAT + 2 SAT`):**
     - Proved no plane can strictly separate two distinct-sheet triangles sharing all 3 vertex IDs $(a, b, c)$ under *any* vertex displacement in $\mathbb{R}^9$ (`UNSAT`), whereas a `shared=2` seam hinge $(a, b, c_1)$ vs $(a, b, c_2)$ is separable (`SAT`).
  4. **`4A-2-0` `Sphere ∧ OBB` Conservative Overlap Filter (`3 UNSAT + 5 SAT`):**
     - Proved both the bounding-sphere triangle-inequality check $|c_A - c_B|^2 \le (r_A + r_B)^2$ and the OBB separating-axis projection bound never miss a true overlapping pair (`UNSAT`; tighter-radius $r_A^2 + r_B^2$ and dropped-axis mutants `SAT`).
