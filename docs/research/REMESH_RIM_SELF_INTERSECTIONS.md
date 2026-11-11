# Literature: no new self-intersections at the rim of cones and tapers (issue #621)

Scope: a literature search for ways to stop `Remesher` (with `MinTriangleQuality`) from adding self-intersecting
pairs at the bottom rim, where the flat cap meets the side of a cone or taper. No code was changed. This document
maps what the literature offers onto the diagnosis in the issue and onto the code paths in `mesh/Remesher.cs`.

Status of the sources: "read" means the abstract or an article page was read; "snippet" means only a search result
or a secondary description was seen; "not read" means the work is named by a secondary source and was not checked.
Every claim below that rests on a snippet is marked as such. Nothing here has been verified by an experiment yet.

## 1. The problem as the code and the benchmark describe it

- Acceptance (issue #621): with `MinTriangleQuality = 0.2` the nine cases of `RemeshQualityBenchmark` must have no
  more self-intersecting pairs than with 0 (taper10_x0.15: 12 -> 16, taper16_x0.1: 0 -> 12, taper32_x0.1: 0 -> 12,
  taper32_x0.6: 0 -> 8, cone16_x0.45: 0 -> 10, cone24_x0.15: 0 -> 6, cone24_x0.3: 0 -> 10, cone24_x0.6: 0 -> 31,
  cone32_x0.6: 0 -> 8).
- The README records the cause as "projection moves across the rim (no normal flips)"; which intersections survive the
  last pass is by chance (`benchmarks/RemeshQualityBenchmark/README.md`, lines around 106-112 as cited in the issue).
- The quality guard exists in two forms in `mesh/Remesher.cs`:
  - `MinTriangleQuality` (line 84): with a threshold above 0 it rejects moves that flip or flatten an incident
    triangle. The same section documents that the buffered smoothing and projection passes revert moves until no
    triangle at a reverted vertex flips.
  - `PreventSelfIntersections` (opt-in, default false, documented at about line 107 of the same file): exact check of
    collapses, flips, splits and of the buffered smoothing and projection passes, with reverts until no pair with a
    moved vertex intersects. The issue rules this out as the fix: it costs 6 to 10 times the time.
- `RejectInvertingPasses` (default true) already rolls back a whole pass when a closed component turns inside out.
  This is a per-pass revert, not a per-move guard.
- The existing gap, in short: the flip guard checks triangles for flips, not whether a moved rim vertex ends up on the
  wrong side of the rim. A projection that moves a rim vertex from the side face onto the cap, or past the cap plane,
  can produce a pair that intersects without any triangle flipping in the usual sense.
- The earlier case analysis S2 (`docs/ISSUE_CASE_FINDINGS.md`) already names the same family of causes: smoothing and
  projection without the flip check; fix idea (a) is to turn that check on by default, which regressed other cases in a
  prototype. A rim-specific guard is a narrower change than that default.

## 2. What the literature says, by mechanism

### 2.1 Constrain rim vertices to the rim curve (the most direct match to the diagnosis)

- Ghyselincks, "Isotropic Remeshing with Boundary and Feature Preservation" (UBC course report; snippet via
  [chipnbits.github.io](https://chipnbits.github.io/blog/posts/isotropic-remeshing/index.html)): follows Botsch and
  Kobbelt and treats boundaries and creases explicitly. Tangential relaxation is performed along the feature lines
  only; boundary and feature vertices are placed from their feature neighbours, not from all neighbours.
- Yao, "Adaptive Feature-Preserving Isotropic Remeshing", ACM TOG 36(4), 2019 (snippet via search; the full text was
  not read): builds on Botsch and Kobbelt (2004); feature edges and vertices are identified at the start and kept
  through the pipeline. Its tangential relaxation is constrained to the vertex's tangent plane, which the source
  states stabilises the following projection.
- FOSSIL isotropic remesh guide (read, [szaghi.github.io](https://szaghi.github.io/FOSSIL/guide/advanced/isotropic-remesh)):
  edges with a dihedral angle above 30 degrees are flagged, and their endpoints are locked, so collapses and
  relaxation leave them alone. The guide notes that a hard threshold also smooths subtle creases.
- Relevance: a rim vertex that may only move along the rim curve cannot cross the cap plane during projection. This
  targets the mechanism the README names. Our code already has curve-vertex smoothing with constraints
  (`RemesherPro`, "curve vertices smoothed" per `docs/roadmap/alpha-wrap-and-repair.md`); the question is whether the
  rim vertices of a cone get that treatment, which the benchmark's pinned-tip result suggests they do not in full.
- Gap: none of the sources describes a normal-cone constraint for projection onto a sharp edge. A targeted search
  for "normal cone" with projection and sharp features found only learned or unrelated work (see section 4).

### 2.2 Side-of-plane or half-space checks for a move

- No paper found that states this exact rule for remeshing. It follows from the mechanism in 2.1 and from the
  fold-over literature in 2.5. It is a design option, not a published result.
- Expected behaviour: a rim vertex keeps its side of the cap plane and of the side face plane across a move. The
  check is local (the triangles of the vertex), so its cost is proportional to the rim, not to the mesh.

### 2.3 Per-operation intersection checks restricted to the changed region

- Liu, Ye and Chen, "Fast Intersection-Free Remeshing of Triangular Meshes", IEEE TVCG 2025, DOI
  [10.1109/TVCG.2025.3569926](https://doi.org/10.1109/tvcg.2025.3569926) (abstract read via a search snippet; the
  PubMed record, [PMID 40392639](https://pubmed.ncbi.nlm.nih.gov/40392639/), was not retrievable here, it returned a
  cookie wall). Stated claims: intersection checks on every local operation are the main bottleneck; intersections
  mainly occur in structurally complex regions; the method finds those regions adaptively and checks only operations
  inside them; it reports about an order of magnitude speed-up over checking every operation; it adds a flip-aware
  extension and an adaptive size field. The definition of "structurally complex" and the region step were not seen.
- Gumhold, Borodin and Klein, "Intersection Free Simplification", 2003 (snippet; only academic copies seen, check the
  published version): tests collisions only for the simplices affected by each contraction, with a spatial search
  structure. This is the classic per-operation guard.
- Relevance: our `PreventSelfIntersections` checks all moves of the affected triangle pairs. A check restricted to the
  rim, as the 2025 paper suggests, is the direction the issue's time budget (1.1x) asks for. It would be a cost
  reduction of an existing exact check, not a new guarantee.

### 2.4 Batch check, then revert (what `PreventSelfIntersections` does)

- PaMO, "Parallel Mesh Optimization for Intersection-Free Low-Poly Modeling on the GPU", Pacific Graphics 2025,
  arXiv [2509.05595](https://arxiv.org/pdf/2509.05595) (snippet): collapses edges in independent regions in one
  batch, then checks the result for self-intersections and reverts the collapses that create intersecting triangles.
  The check runs after each iteration, not before each collapse. It also applies the link condition. The paper says a
  prior intersection-aware method took tens of minutes on one mesh.
- Relevance: this is the pattern already used by the buffered passes. The literature supports it as a correct and
  deterministic revert scheme. Its cost is the reason the issue wants a rim-local rule.

### 2.5 Fold-over and untangling (local optimisation view)

- Freitag and Plassmann, "Local optimization-based simplicial mesh untangling and improvement" (2000; snippet via
  search, article not read): maximises the minimum element area in a local submesh; the level sets of the local
  objective are convex, so the local subproblem converges, and starting from a valid mesh the criterion keeps the mesh
  valid. This is the strongest guarantee in the results for a local move.
- Freitag, "Smart Laplacian" smoothing (1997; described in a MyMesh doc, snippet): a move is kept only if the quality of
  the incident elements improves. The description does not mention inversion explicitly, so the protection comes from
  the metric. The same doc warns that plain Laplacian smoothing can invert elements.
- Shontz and Vavasis, IMR 2003 (snippet): a sufficient condition for a local warping to resist inversion.
- Agarwal, Sadri and Yu, "Untangling Triangulations through Local Explorations", SCG 2008 (snippet; 2D only): cut out
  a locally removable region around a tangled seed and re-mesh it; output-sensitive. The idea does not transfer to a
  3D surface with a rim unchanged, but the principle (re-mesh a bounded region and accept only if it stays valid) does.
- Relevance: a local projection of the rim, solved as a constrained optimisation of the minimum area or of the signed
  distance to the cap plane with the rim's current state as the valid start, would give a convergence argument that the
  heuristic guards do not. The cost is an optimiser per rim vertex.

### 2.6 Post-hoc local repair

- Attene, "Direct repair of self-intersecting meshes", 2014 (abstract read via [iris.cnr.it](https://iris.cnr.it/handle/20.500.14243/228607)):
  a fast exact algorithm that removes intersections from a triangle mesh. It uses floating point where it can and
  switches to exact arithmetic only when needed; the switches are rare in practice.
- Liu et al., "Robust and Fast Local Repair for Intersecting Triangle Meshes" (2025, ScienceDirect
  [S0010448525001241](https://www.sciencedirect.com/science/article/abs/pii/S0010448525001241); snippet): repairs each
  intersecting region locally, stitches it back, then remeshes locally.
- Skorkovská et al., GRAPP 2018, "A Simple and Robust Approach to Computation of Meshes Intersection" (snippet): finds
  intersections one at a time and repairs locally, leaving the rest unchanged.
- Zheng et al., "Visual-Guided Mesh Repair", arXiv [2309.00134](https://arxiv.org/pdf/2309.00134) (snippet): checks
  face intersections before each collapse; the authors say local approaches often lack guarantees and can introduce
  new self-intersections during repair.
- Jacobson et al. (2013, robust inside-outside; snippet): repair methods either remesh globally for a guarantee or make
  local changes and give up the original geometry or connectivity in problem areas.
- Relevance: a post-hoc repair changes the output after the remesher's last pass and is the least controllable
  option. It is a fallback, not the first choice: the acceptance is a count that must not rise, and a repair can move the
  rim too.

### 2.7 Quality-driven flips at a crease

- FOSSIL (read, as in 2.1): rejects a collapse that would flip a triangle's normal, create a duplicate vertex or open a
  non-manifold edge, and rejects flips that create a duplicate edge or a degenerate triangle.
- Yao 2019 (snippet, see 2.1) keeps the valence-driven flips and collapses away from features.
- Relevance: the flip and collapse guards of `PreventNormalFlips` and `MinTriangleQuality` already cover the normal
  check. What is missing per the issue is a check of the rim projection itself, not of the flips.

### 2.8 Background on the base method

- Botsch and Kobbelt, isotropic remeshing (2004): split, collapse, flip, tangential relaxation, projection. Not read
  here; the loop is confirmed by the implementations above.
- Hu, Yan, Bommes, Alliez and Benes, "Error-Bounded and Feature Preserving Surface Remeshing with Minimal Angle
  Improvement", IEEE TVCG 2017, DOI [10.1109/tvcg.2016.2632720](https://doi.org/10.1109/tvcg.2016.2632720) (snippet):
  preserves sharp creases within an error bound; a broader method, not a rim fix.
- Dunyach, Barthe and Botsch, Eurographics 2013 (snippet): modifies the tangential relaxation barycentre to keep relative
  edge sizing.

### 2.9 Learned or unrelated

- Soboleva et al., "PRS: Sharp Feature Priors for Resolution-Free Surface Remeshing", arXiv
  [2311.18494](https://arxiv.org/abs/2311.18494) (snippet): snaps vertices to feature curves and flips edges for normal
  alignment, with a learned prior. Not applicable without the learned component.

## 3. Options mapped onto the issue's candidate fixes

The issue lists three candidates, cheapest first. The literature supports them as follows.

| Option | What it does | Literature | Guarantee | Cost | Main risk |
|---|---|---|---|---|---|
| A. Rim vertices move only along the rim curve; projection onto the rim curve | removes the cap-plane crossing by construction | 2.1 (Ghyselincks, Yao, FOSSIL) | none in the cited sources; removes the mechanism | low (curve projection is local) | the cap triangles may then fold instead; needs the S2 check too |
| B. Side-of-plane test on a rim move, against the cap plane and the side plane | rejects a rim move that crosses either plane | no published rule found; follows from 2.1 and 2.5 | local, exact if the test is exact | low (rim triangles only) | rejects too many moves: the rim may stop smoothing |
| C. Per-move intersection check restricted to the rim (structurally complex region) | exact check only where it matters | 2.3 (Liu, Ye, Chen 2025; Gumhold 2003) | exact, as `PreventSelfIntersections` | lower than the existing full check; the target 1.1x is still a question | depends on finding the region correctly |
| D. Batch check, revert per pass | reverts a pass when a pair intersects | 2.4 (PaMO); already in the buffered passes | deterministic | per pass | reverts the whole pass; no rim focus |
| E. Local optimisation (min area or plane distance) of the rim projection | convergence argument | 2.5 (Freitag and Plassmann 2000) | local convergence from a valid mesh | an optimiser per rim vertex | more code; the convexity claim must be checked for our projection |
| F. Post-hoc local repair | repairs after the last pass | 2.6 (Attene 2014; Liu 2025) | depends; floating point with exact fallback | a second pass | can move the rim and add defects (Zheng et al.) |

## 4. Searches that found nothing

- A normal-cone or sharp-edge side constraint for vertex projection (snippets gave learned or unrelated work).
- A published rule that a projected vertex must keep its side of a crease plane.
- A paper on self-intersections specifically at cone or taper rims.
- The original Botsch and Kobbelt (2004) text (not in the results; check it directly).

## 5. Recommended order (for the owner's decision)

1. Diagnose first, as the issue asks: count per pass and per operation which move creates the first pair (the
   literature does not replace the trace). Confirm that the first pair comes from a rim vertex's projection.
2. Try option A (rim vertices on the rim curve only). It targets the stated mechanism, is cheap, and is the
   common practice in the cited work.
3. If A alone fails the zero-new-pairs gate, add option B for the remaining rim moves; it is local.
4. Use option C only if the 1.1x time budget is exceeded by B. It makes the exact check cheaper, not stronger.
5. Keep D (the existing revert) as the safety net. F is the last resort and should be a separate item.
6. Option E is a research item if A and B are not enough; its convergence claim must be checked for our projection.

Caveat: the cited sources are mostly abstracts or snippets. Before a design is chosen, the full texts of Liu, Ye and
Chen 2025, Yao 2019, Freitag and Plassmann 2000 and Gumhold et al. 2003 should be read, and the claims in sections 2.1,
2.3 and 2.5 checked against them.

## 6. Sources

- Liu, Ye, Chen, TVCG 2025, DOI [10.1109/TVCG.2025.3569926](https://doi.org/10.1109/tvcg.2025.3569926)
- PaMO, arXiv [2509.05595](https://arxiv.org/pdf/2509.05595)
- Yao, ACM TOG 2019 (adaptive feature-preserving isotropic remeshing)
- Ghyselincks, UBC report, [chipnbits](https://chipnbits.github.io/blog/posts/isotropic-remeshing/index.html)
- FOSSIL isotropic remesh guide, [szaghi.github.io](https://szaghi.github.io/FOSSIL/guide/advanced/isotropic-remesh)
- Gumhold, Borodin, Klein, 2003, "Intersection Free Simplification"
- Freitag and Plassmann, 2000, "Local optimization-based simplicial mesh untangling and improvement"
- Freitag, 1997, smart Laplacian smoothing (via [MyMesh](https://bu-smbl.github.io/mymesh/theory/laplaciansmooth.html))
- Shontz and Vavasis, IMR 2003 (warping, [PDF](https://people.eecs.ku.edu/~shontz/shontz_vavasis_lwls_warping_imr_2003.pdf))
- Agarwal, Sadri, Yu, SCG 2008, "Untangling Triangulations through Local Explorations"
- Attene, 2014, "Direct repair of self-intersecting meshes" ([iris.cnr.it](https://iris.cnr.it/handle/20.500.14243/228607))
- Liu et al., 2025, "Robust and Fast Local Repair for Intersecting Triangle Meshes" ([ScienceDirect](https://www.sciencedirect.com/science/article/abs/pii/S0010448525001241))
- Skorkovská et al., GRAPP 2018 ([PDF](https://www.cs.purdue.edu/homes/bbenes/papers/Skorkovska18Grapp.pdf))
- Zheng et al., "Visual-Guided Mesh Repair", [arXiv 2309.00134](https://arxiv.org/pdf/2309.00134)
- Jacobson et al., 2013, robust inside-outside ([PDF](https://users.cs.utah.edu/~ladislav/jacobson13robust/jacobson13robust.pdf))
- Hu et al., TVCG 2017, DOI [10.1109/tvcg.2016.2632720](https://doi.org/10.1109/tvcg.2016.2632720)
- Dunyach, Barthe, Botsch, Eurographics 2013
- Soboleva et al., PRS, [arXiv 2311.18494](https://arxiv.org/abs/2311.18494)
- Botsch and Kobbelt, 2004, isotropic remeshing (not read)
