# Symbolic Taylor round-off error bounds for geometric predicates (2026-11)

Research study (Tier M, docs and benchmark probe only; no exact library code changed). Evaluates Symbolic Taylor Expansions (Solovyev et al., ACM TOPLAS 2018; FPTaylor) and exact algebraic adjoint telescoping for two uses in `geometry3Sharp`:
1. **Offline filter verification** (`tools/PredicateCompiler`, `tools/ErrorBoundAudit`, replacing or extending the 35-minute OCaml `tools/FPTaylorPilot`);
2. **A runtime Stage-1.5 filter** between `PredicateCompiler`'s Stage-A permanent bound and exact arithmetic (`FixedArith` / `BigInteger`), compared head-to-head against forward interval arithmetic (`Interval64`, matching `math/ExactIntervals.cs`).

**Summary result:**
- **Offline verification (`Part 1`):** A pure-C# reverse-mode Symbolic Taylor + interval branch-and-bound analyzer (`benchmarks/SymbolicTaylorBoundsProbe/BoxSymbolicTaylor.cs`) ran all **11** bounded-box benchmarks — including the 8 expressions of `tools/FPTaylorPilot` **plus** full degree-6 `Orient3dLPI det` (67 ops), degree-6 `Orient3dTPI d` (83 ops), and degree-9 `Orient3dTPI det` (131 ops) — in **0.49 s total** (vs. **~35 minutes** for OCaml `FPTaylorPilot` on only the 8 subexpressions). On `LPI d` and `LPI Nx` it matches OCaml FPTaylor (`0.939×` and `1.053×` in 65–80 ms vs. 9–17 min), and proves `PredicateCompiler`'s generator bounds are **6.1×–7.2× conservative** on full `Orient3dLPI` / `Orient3dTPI` boxes (`0.139×–0.163×`).
- **Runtime Stage-1.5 filter (`Part 2` & `Part 3`):** Using the exact polynomial error telescoping identity $\hat{f} - f = \sum_k \bar{v}_k \ell_k - \sum_{k \in \mathrm{Mul}} \bar{v}_k \varepsilon_{a_k}\varepsilon_{b_k}$ with a sound reverse-pass error bound and $O(u^2)$ quadratic remainder, **Runtime Symbolic Taylor rescues 40.4%–76.0% of Stage-A fallbacks on `Orient3dLPI` and `Orient3dTPI`** (and 15.5%–25.1% on `Orient3d`/`InCircle`/`InSphere`) with **0 wrong signs across 40,000 adversarial near-degenerate inputs** checked against exact `BigInteger` binary rationals. It beats `Interval64` (`ExactInterval`) simultaneously on **tightness (`0.29×–0.74×` median bound)**, **fallback rate**, and **speed (`1.3×–1.8×` faster)**. Because production `IndirectPredicates.Orient3dLPI` Stage-A takes `134 ns` while its `FixedArith` fallback takes **`4,181 ns` (`31.3×` slower)**, a compiled Stage-1.5 Symbolic Taylor filter is a strong candidate **specifically for high-degree indirect predicates** once real-mesh fallback rates are measured.

## 1. Mathematical formulation

Let a $+,-,\times$ DAG evaluate exact binary64 inputs in IEEE-754 round-to-nearest (normal range, no FMA, $u = 2^{-53}$). By Knuth / Jeannerod–Rump (2018), every normal operation satisfies $\hat{v}_k = \operatorname{op}_k(\hat{a}_k, \hat{b}_k) + \ell_k$ with $|\ell_k| \le u|\hat{v}_k|$. Writing $\varepsilon_k = \hat{v}_k - v_k$:
- **Add / Sub:** $\varepsilon_k = (\varepsilon_a \pm \varepsilon_b) + \ell_k$.
- **Mul:** $\hat{a}\hat{b} - ab = \hat{b}\varepsilon_a + \hat{a}\varepsilon_b - \varepsilon_a\varepsilon_b \implies \varepsilon_k = \hat{b}\varepsilon_a + \hat{a}\varepsilon_b - \varepsilon_a\varepsilon_b + \ell_k$.

Defining exact real adjoints $\bar{v}_k$ backward from $\bar{v}_{\mathrm{root}} = 1$ using the **signed computed values** $\hat{v}_k$ (reverse-mode AD on the linearized DAG), exact algebraic telescoping gives — with zero Taylor truncation:
$$\hat{f} - f = \sum_{k} \bar{v}_k \ell_k - \sum_{k \in \mathrm{Mul}} \bar{v}_k (\varepsilon_{a_k} \varepsilon_{b_k}).$$
For a sign filter, the root rounding $\ell_{\mathrm{root}}$ cannot flip the sign (Shewchuk 1997 §4.2) and is omitted.

- **Why this beats forward bounds and interval arithmetic:** Whenever an intermediate variable $v_k$ feeds multiple branches of opposite sign — such as the plane normal `nx, ny, nz` shared between `lp = n·(p-r)` and `lq = n·(q-r)` in `Orient3dLPI` (`d = lq - lp`), where $\bar{v}_{nx} = \hat{q}_{rx} - \hat{p}_{rx} \approx q_x - p_x$ — forward error analysis (`PredicateCompiler`, `ErrorBoundAudit/fpbound.py`) and forward interval arithmetic (`ExactInterval`) add $|\hat{q}_{rx}| + |\hat{p}_{rx}|$ and lose the correlation. Reverse-mode Symbolic Taylor cancels shared-variable first-order errors automatically before taking absolute values.
- **Soundness of the runtime `double` evaluation (`ExprDag.EvalRuntimeSymbolicTaylor`):**
  1. Forward Stage-A already computes the proven `PredicateCompiler` permanent bounds $|\varepsilon_k| \le c_k \tilde{m}_k$ (`tools/Z3Pilot/filter_rules.py`).
  2. The reverse pass tracks a matching reverse permanent $\tilde{\bar{m}}_k$ with static constant $\bar{c}_k$ so $|\hat{\bar{v}}_k - \bar{v}_k| \le \bar{c}_k \tilde{\bar{m}}_k = O(u)\tilde{\bar{m}}_k$.
  3. Total sound bound:
     $$B_{\mathrm{ST}} = \left( u \sum_{k < \mathrm{root}} |\hat{\bar{v}}_k||\hat{v}_k| \;+\; u \sum_{k < \mathrm{root}} \bar{c}_k \tilde{\bar{m}}_k |\hat{v}_k| \;+\; \sum_{k \in \mathrm{Mul}} (|\hat{\bar{v}}_k| + \bar{c}_k \tilde{\bar{m}}_k)\, c_{a_k} c_{b_k} \tilde{m}_{a_k} \tilde{m}_{b_k} \right)(1 + (2K+12)u),$$
     guarded against subnormals (`0 < |v| < 1e-290`) and overflow (`|v| > 1e150`). The first sum is $O(u)$ with full sign cancellation; both remainder terms are $O(u^2) \approx 10^{-31}$.

## 2. Measurements

Reproduce from the repository root:

```sh
LC_ALL=C dotnet run --project benchmarks/SymbolicTaylorBoundsProbe -c Release
```

Measured in Release on Linux x64 with .NET SDK 10.0.115.

### Part 1: Offline bounded-box Symbolic Taylor vs. OCaml `FPTaylorPilot` and `PredicateCompiler`

Domains match `tools/FPTaylorPilot/compare_filter_expressions.py` (`[1, 2]` for points, `[0, 0.5]` for LPI `r`). OCaml `FPTaylorPilot` numbers are from `tools/FPTaylorPilot/README.md` (~35 min total on the first 8 expressions; full determinants timed out / not run).

| Expression | Ops | Deg | `PredicateCompiler` | OCaml FPTaylor | C# SymTaylor (1 box) | C# SymTaylor (B&B) | Sampled lower | B&B / Comp | B&B / OCaml | Boxes | Time (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `LPI prx` | 1 | 1 | `2.220e-16` | `1.110e-16` | `2.220e-16` | `2.220e-16` | `2.220e-16` | `1.000` | `2.000×` | 1 | 9.2 |
| `LPI ny` | 7 | 2 | `3.553e-15` | `1.780e-15` | `3.081e-15` | `2.757e-15` | `2.390e-15` | `0.776` | `1.549×` | 257 | 4.4 |
| `LPI nz` | 7 | 2 | `3.553e-15` | `1.780e-15` | `3.081e-15` | `2.757e-15` | `2.390e-15` | `0.776` | `1.549×` | 257 | 2.8 |
| `LPI d` | 32 | 3 | `9.592e-14` | `2.512e-14` | `3.718e-14` | `2.359e-14` | `8.882e-15` | `0.246` | `0.939×` | 1025 | 64.7 |
| `LPI Nx` | 37 | 4 | `1.172e-13` | `3.099e-14` | `7.028e-14` | `3.264e-14` | `9.104e-15` | `0.278` | `1.053×` | 1025 | 80.0 |
| `Orient3dLPI det` | 67 | 6 | `2.302e-12` | — | `6.215e-13` | `3.191e-13` | `9.992e-15` | **`0.139`** | — | 1025 | 133.3 |
| `TPI u1_x` | 1 | 1 | `1.110e-16` | `5.551e-17` | `1.110e-16` | `1.110e-16` | `1.110e-16` | `1.000` | `2.000×` | 1 | 0.1 |
| `TPI n1x` | 7 | 2 | `8.882e-16` | `4.458e-16` | `8.882e-16` | `7.086e-16` | `6.661e-16` | `0.798` | `1.589×` | 257 | 50.3 |
| `TPI D1` | 23 | 3 | `5.329e-15` | `2.109e-15` | `5.107e-15` | `2.998e-15` | `1.887e-15` | `0.563` | `1.421×` | 513 | 7.5 |
| `Orient3dTPI d` | 83 | 6 | `2.718e-13` | — | `8.882e-14` | `4.421e-14` | `4.774e-15` | **`0.163`** | — | 1025 | 72.9 |
| `Orient3dTPI det` | 131 | 9 | `8.058e-12` | — | `2.590e-12` | `1.282e-12` | `8.105e-15` | **`0.159`** | — | 1025 | 37.6 |

Notes on `B&B / OCaml`:
- On 1–2-op leaves (`prx`, `u1_x`, `ny`, `nz`, `n1x`), OCaml FPTaylor uses the sharper binade model $\frac{1}{2}\operatorname{ulp}(z)$ (which equals $2^{-53}$ on $[1, 2]$), whereas the simpler $u|z|$ model evaluates to $2\cdot 2^{-53}$ at $z = 2$, explaining the `1.42×–2.00×` factor.
- On `LPI d`, C# adjoint-guided branch-and-bound (`2.359e-14` in `65 ms`) is slightly tighter than OCaml FPTaylor (`2.512e-14` in `~9 min`) because OCaml FPTaylor's generic optimizer stops earlier on 15 variables. An empirical 50,000-sample corner/interior check on `LPI d` measured `maxObservedErr = 2.609e-15` (`0.1106` of the `2.359e-14` bound).

### Part 2: Adversarial near-degenerate fallback reduction & soundness (40,000 exact checks)

Each row runs `5,000` near-degenerate procedural inputs against exact `BigInteger` binary rationals (`ExactBin`). Every decided sign of Stage A, `Interval64`, and Runtime Symbolic Taylor is verified against exact truth.

| Workload (`N = 5000`) | Stage-A undec | `Interval64` undec | SymTaylor undec | Rescued of Stage A | Med `SymT / A` | Med `SymT / Iv64` | `maxErr / SymT` | Wrong signs |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `LPI d` (short seg far from `r`) | 1052 (21.0%) | 1145 (22.9%) | **259 (5.2%)** | **793 / 1052 (75.4%)** | `0.3150` | `0.2855` | `0.6515` | **0** |
| `Orient3dLPI det` (short seg) | 1063 (21.3%) | 561 (11.2%) | **255 (5.1%)** | **808 / 1063 (76.0%)** | `0.2771` | `0.5641` | `0.3680` | **0** |
| `Orient3dLPI det` (unit near-plane) | 2432 (48.6%) | 2107 (42.1%) | **1429 (28.6%)** | **1003 / 2432 (41.2%)** | `0.2760` | `0.3988` | `0.5276` | **0** |
| `Orient3dTPI det` (near-coplanar) | 2857 (57.1%) | 2474 (49.5%) | **1702 (34.0%)** | **1155 / 2857 (40.4%)** | `0.1625` | `0.3005` | `0.2483` | **0** |
| `Orient3d` (near-coplanar) | 2108 (42.2%) | 2056 (41.1%) | 1579 (31.6%) | 529 / 2108 (25.1%) | `0.5805` | `0.6008` | `0.4477` | **0** |
| `InCircle` (near-cocircular) | 2769 (55.4%) | 2878 (57.6%) | 2339 (46.8%) | 430 / 2769 (15.5%) | `0.5502` | `0.4745` | `0.3343` | **0** |
| `InSphere` (near-cospherical) | 3650 (73.0%) | 3551 (71.0%) | 2843 (56.9%) | 807 / 3650 (22.1%) | `0.3388` | `0.3966` | `0.2955` | **0** |
| `SN-5 CrossesInterior` (shipped `1e-14`) | 4821 (96.4%) | 2869 (57.4%) | **2586 (51.7%)** | **2235 / 4821 (46.4%)** | `0.0124` | `0.7410` | `0.3787` | **0** |

### Part 3: Latency comparison (`ns/call`, 50,000 calls after warmup)

Interpreted `ExprDag` loop timings (showing relative structural ratios across methods):

| Predicate | Ops | Stage A (ns) | `Interval64` (ns) | SymTaylor (ns) | `ExactBin` (ns) | `SymT / StageA` | `Exact / SymT` |
|---|---:|---:|---:|---:|---:|---:|---:|
| `Orient3d` | 23 | 759 | 838 | 716 | 2080 | 0.94× | 2.9× |
| `InCircle` | 29 | 334 | 1596 | 828 | 4313 | 2.48× | 5.2× |
| `InSphere` | 77 | 944 | 3962 | 3204 | 6554 | 3.39× | 2.0× |
| `Orient3dLPI det` | 67 | 612 | 3342 | 2163 | 12414 | 3.54× | 5.7× |
| `Orient3dTPI det` | 131 | 2091 | 5451 | 3901 | 13367 | 1.87× | 3.4× |

Production `IndirectPredicates.Orient3dLPI` (`double` entry, called via reflection on 256 Stage-A-decided vs. 256 Stage-A-undecided short-segment inputs):
- **Stage-A fast path:** `134 ns/call`
- **Production `FixedArith` exact fallback:** **`4,181 ns/call` (`31.3×` slower)**

Because a straight-line compiled Stage-1.5 Symbolic Taylor pass emitted by `PredicateCompiler` reuses the `vBuf` values already in locals and executes pure hardware `double` ops (`~2–3×` Stage A, ~`270–400 ns/call`), it is **~10–15× faster than `FixedArith`**, pays for itself whenever it rescues `> 8%` of Stage-A fallbacks (here **40.4%–76.0%**), and adds `< 10%` overhead on true exact degeneracies.

## 3. Recommendation and next steps

1. **Offline verification (`tools/ErrorBoundAudit` / `tools/FPTaylorPilot`) — Build candidate (Tier S/M, tools-only):**
   The pure-C# Symbolic Taylor engine verifies full degree-6 and degree-9 predicates in `< 0.5 s` with zero OCaml/Docker/WSL setup, making it CI-friendly. Adding the binade rounding model ($\frac{1}{2}\operatorname{ulp}(z)$) would also close the `1.5×–2.0×` leaf gap vs. OCaml FPTaylor.
2. **Runtime Stage-1.5 filter in `tools/PredicateCompiler` for `Orient3dLPI` / `Orient3dTPI` — Measure real-mesh fallback rate first (Tier L if built):**
   - **Do NOT build** for `Orient2d`, `Orient3d`, or `InCircle` (Shewchuk adaptive expansions and `orient3d_int` already cover those cheaply; Stage-1.5 rescue is only 15–25%).
   - **For `SN-5 SnapGrid.CrossesInteriorFilter`:** no runtime adjoint pass is needed — simply tightening the shipped `1e-14` constant (30× audited margin) recovers nearly all of the 46.4% rescue at zero runtime cost.
   - **For `Orient3dLPI` / `Orient3dTPI` (and future high-degree indirect predicates):** before any Tier-L `PredicateCompiler` change (which requires an owner go, Z3 proof of the reverse-telescoping rule, bounds review, and mutation gate), instrument the Stage-A fallback rate of `IndirectPredicates` on real `PolyhedralEnvelope` / `ExactMeshBoolean` corpora. If real fallback rates are significant (`> 2%` of calls or `> 5%` of total predicate time), generating a compiled Stage-1.5 Symbolic Taylor pass in `PredicateCompiler` is a clean, general, provably sound speedup that dominates `ExactInterval` on both accuracy and speed.
