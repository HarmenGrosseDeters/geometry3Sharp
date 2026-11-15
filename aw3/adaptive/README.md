# Adaptive alpha for MeshAlphaWrap: an upper-bound study

Measurement only, no library change. Question: how much smaller (and so faster) could the wrap of the repair pipeline be
with a locally varying alpha, at a bounded loss against today's uniform wrap? This is the upper-bound check that comes
before any design (AGENT_WORKFLOW.md section 8).

## Method

`AdaptProbe.cs.txt` (source as text; build it against the library like the benchmarks, run `AdaptProbe <mesh.stl> 3`).
Per mesh of the 50 (`normal30.txt`, `tail20.txt`, from `benchmarks/MeshRepairBenchmark/data/sample800_2611`):

- Reference wrap W0 at alpha0 = diag / 300, offset = diag / 3000 (the pipeline's defaults); coarse wraps W_L at
  2^L alpha0, L = 1..3, with the same offset.
- Oracle level per W0 triangle t: the largest L such that every W_l (l ≤ L) is within tau of t, both ways (t's samples to
  W_l, and W_l samples whose nearest W0 triangle is t back to W0). tau = 1, 2 or 10 offsets (10 offsets = alpha0).
- Estimated adaptive size: the sum over t of area(t) times the triangle density of W_{L(t)}.
- Graded: the local alpha smoothed to a Lipschitz function of constant g (0.5, 0.25) over the triangle adjacency, by a
  multi-source Dijkstra. A real adaptive wrap must grade, because the Delaunay refinement cannot jump from alpha to 8 alpha
  between neighbouring triangles. The graded numbers are the realistic ones, the ungraded ones a ceiling.
- A computable predictor (without W0): coarse where the coarse wrap stays within offset + tau of the input.
  "False area" is the share of W0's area where the predictor goes coarser than the oracle allows.
- Time: wrap time scaled by size (time of W0 times estimated size / W0 size). The wrap's time is about linear in its output
  size; the measured times of the 4 levels per mesh are in `adapt.jsonl` (`ms`).

All runs: 4 cores, branch of lever (B) part 1 (rounds), `adapt.jsonl` raw output.

## Results (W0 size / estimated adaptive size; higher is better)

| | normal 30: median (min) | tail 20: median (min) |
|---|---:|---:|
| oracle, tau = 1 offset, ungraded | 6.52 (2.79) | 4.13 (1.16) |
| graded g = 0.5, tau = 1 | 3.95 (1.74) | 2.94 (1.12) |
| graded g = 0.25, tau = 1 | 2.97 | 2.19 |
| graded g = 0.5, tau = 2 | 5.01 (1.81) | 3.33 (1.12) |
| graded g = 0.5, tau = 10 | 8.47 (3.38) | 4.23 (1.13) |
| predictor, tau = 1: false area median (max) | 1.1 % (5.0 %) | 28.1 % (48.5 %) |
| predictor, tau = 1: worst deviation there, median (max) | 40 (146) offsets | 138 (616) offsets |

Estimated wrap time (sum / max over the stratum, from the W0 times):

| | normal 30 | tail 20 |
|---|---:|---:|
| uniform (measured) | 191 s / 8.9 s | 556 s / 101.3 s |
| graded g = 0.5, tau = 1 | 54 s / 3.9 s (3.5x) | 275 s / 90.4 s (2.0x) |
| graded g = 0.5, tau = 10 | 28 s / 2.1 s (6.8x) | 228 s / 90.0 s (2.4x) |

## Findings

1. **The tail's worst case does not shrink.** On 40118 (the tail maximum, ~100 s) 86 % of the area needs alpha0 at
   tau = 1 offset, graded gain 1.12x. 111959, 860049 and 51909: 56-66 % of the area at alpha0, gains 1.2-1.4x. The tail
   is the tail because its surface is detailed almost everywhere, and an adaptive alpha keeps that detail. The max over
   the tail stays at about 90 s even at tau = 10 offsets (the wrap then changes visibly, about one alpha0).
2. **The normal stratum gains 3.5-5x** (graded, tau 1-2 offsets), the tail sum 2x. Ungraded ceilings are 6.5x / 4x;
   grading costs a third to a half of it.
3. **A cheap predictor is not safe where it matters.** "The coarse wrap is close to the input" is wrong on 28 % of the
   tail's area (median), by up to 616 offsets: the coarse wrap closes cavities and thin gaps and stays close to the input
   from outside, while the fine wrap goes into them. Which regions may be coarse is a property of the fine wrap itself
   (cavity-aware), so a predictor needs at least a cavity test (eg the fine carve's gates per region), not a distance test.

## Guarantees (what an adaptive alpha would keep or lose)

- Termination: unchanged if the local alpha is bounded below by alpha_min (here alpha0); the Steiner points then stay
  separated by the same bound as today.
- Enclosure and the offset band: the offset stays uniform, so the result stays outside the input and within the offset
  band locally; topology guarantees (alpha-dependent: which gaps close) become local, a gap closes where the local alpha
  exceeds it.
- The angle bound of the quality refinement weakens with the grading: with local alpha a(x) Lipschitz of constant g, the
  bound holds with a constant that degrades in g (a fixed delta per level change); g = 0.5 is a guess, not a proof.
- Determinism and invariance: unchanged if the local alpha is a function of the geometry (it scales with 2^k).

## Literature

- Dai, Wang, Yan (CAGD 2024), an adaptive variant of alpha wrapping: the text could not be read here (publisher blocked),
  so its method is not compared; to be checked before a design.
- Liu et al., "Shrink wrap meshing via graph-based optimization", Sci Rep (2026, s41598-025-34284-w): feature-aware
  shrink wrapping with a graph optimization; a uniform target size, not an adaptive alpha.
- Portaneri et al. 2022 (Alpha wrapping with an offset): uniform alpha.

## Conclusion

Adaptive alpha is worth about 3.5x on typical meshes and 2x on the tail sum, but not on the tail maximum (1.1x), which is
what the 10x question was about. Together with lever (B) (1.47x on the tail) that is about 3x on the tail sum and about
1.4x on its worst mesh. It needs a cavity-aware level test (finding 3), a grading-aware refinement and a weakened
angle bound. The worst case only shrinks with a budget (a triangle cap, already in the pipeline) or with more cores.
