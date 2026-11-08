# Geometry3Sharp: hard geometry problems across fields

**Research report — 3 November 2026.** Pure research: no library changes, no benchmark runs. Experimental budgets below are proposed engineer-hours, not measurements. The task's supplied measurements are accepted as the starting evidence, not independently reproduced.

## Executive judgment

The strongest opportunity is not a universal new rounding algorithm. It is **making a small, checkable invariant survive each local operation**, while spending exact arithmetic only on its threatened boundary.

The most promising first probes are:

1. **Certified clearance with displacement skins** for 3D rounding: distinguish proving free space from merely finding a nearby point, and reuse the proof while its movement budget survives.
2. **Transactional preservation of the input-only planar embedding** for Q17: a vertex pinch is a change of incidence and local sectors, even if no edge overlaps and almost no area disappears.
3. **Small finite feasibility problems for the remaining 3D defect clusters**, with fixed collars, inherited labels, and explicit surface correspondence—not another whole-mesh self-union.

Two important corrections to seductive analogies:

- An approximate closest point gives an **upper** bound on distance. An empty-ball certificate needs a **lower** bound. Confusing these makes the central filter unsound.
- A collision-free endpoint is not an isotopy certificate; positive triangle areas are not a global injectivity certificate; bounded surviving-vertex drift is not a two-sided Hausdorff certificate. These are different obligations.

The report does **not** establish a new unconditional practical 3D snap-rounding theorem, a termination/quality theorem for grid-restricted Q17 refinement, or the requested reference-speed/count targets. The proposals preserve safety by certification and explicit refusal; completeness remains a separate research obligation.

### Scope and evidence discipline

Process tier: **S, research/documentation only**. Any implementation changing exact arithmetic, error bounds, or exact mesh operations would be tier L and require a separate approved brief and bounds review.

- **Known:** the cited source establishes the stated component, with its assumptions.
- **Transfer:** that component is established elsewhere; its proposed integration here is untested.
- **Speculative:** the proposed mechanism or useful performance is not established by the opened sources.
- Citations [S1]–[S22] resolve to the opened-source ledger below. Source chains explicitly distinguish an actual citation from a conceptual connection.
- Paper PDFs were downloaded from public author/institutional hosts and inspected through text extraction. Reading selected sections is not described as reading every page.
- No external implementation source files were inspected. Two license files and the fast-envelope README were read. Papers sometimes contain algorithms or illustrative code; those are not treated as permission to port a software distribution.
- No restricted Triangle/Triangle.NET, owner-quadtree, company, or copyleft implementation was opened. A Shewchuk paper landing page mentioning Triangle was opened; no Triangle implementation or implementation chapter was followed.
- General search returned unusable Google JavaScript pages and unrelated Bing results. Those are recorded as discovery failures, not evidence of an absence of research. Successful discovery used author pages, arXiv, Crossref and HAL.

### Repository findings that constrain the ideas

The following were inspected, selectively where large. They are local evidence, not new literature claims:

- [ROADMAP.md](../ROADMAP.md) and [roadmap/hotspots.md](../roadmap/hotspots.md): open-task connections and existing building blocks.
- [SAFE_ROUNDING.md](../design/SAFE_ROUNDING.md), especially the evaluation and final verdict: A and whole-mesh B are closed as default approaches. The patch timing excludes fitting, global defect discovery, and repeated rounds; it is not a predicted pipeline speedup.
- [MESHING2D_SNAP.md](../design/MESHING2D_SNAP.md), [MESHING2D_DECISIONS.md](../design/MESHING2D_DECISIONS.md), and [MESHING2D_HULL_GRID.md](../design/MESHING2D_HULL_GRID.md): all output vertices are hot; restore vertices belong to input rounding, not refinement; the no-coincident-chain-edge guard does not settle vertex-only pinches. A coarse rule based only on which original links hit a pixel was already tried and over-rejected acute corners.
- [ENVELOPE.md](../design/ENVELOPE.md): the later prototype is more informative than the initial slow clipping result. Its important cost is the facet-pair loop, not frequent arbitrary precision. Its low acceptance is a polyhedron-shape problem. Ordinary subdivision, local-nearest guesses, a hash grid, and repeated-triangle caching are not unexplored solutions.
- [SELF_INTERSECTION_PATCH_CERTIFICATE.md](../design/SELF_INTERSECTION_PATCH_CERTIFICATE.md): an exact projected disk/contour certificate already exists as a stopped, slower prototype. Do not simply propose it again as a new accelerator.
- [REDUCER_SPECULATIVE_CERTIFICATION.md](../design/REDUCER_SPECULATIVE_CERTIFICATION.md): parallel speculative certification was bit-identical but slower and is closed.
- [INSIDE_OUTSIDE_OPEN_INPUTS.md](../design/INSIDE_OUTSIDE_OPEN_INPUTS.md) and [VOLUME_CHECK_SAMPLING.md](../design/VOLUME_CHECK_SAMPLING.md): open-input GWN is not constant in an empty cell; better labeling does not repair a structurally unclosable arrangement.
- [TERRAIN.md](../design/TERRAIN.md): this is the existing terrain document; the requested `docs/TERRAIN.md` was absent. Local output coordinates are part of the guarantee. The local-frame cost discussion points to repeated realization after clipping/support checks, not primarily translation.
- [ALPHA_WRAP_QUALITY.md](../design/ALPHA_WRAP_QUALITY.md) and [ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md): existing guarded quality operations and the low-poly LSCM collapse are not missing basic checks.

The library-building-block search was documentation-based, deliberately not an implementation audit. Useful existing components include exact orientation and triangle-intersection checks, implicit points, generated LPI/TPI predicates, BVHs, displacement bookkeeping, input-only snap images, exact support checks, and the independent meshing oracle.

## 1. Top ideas, ranked

The order is an **ordinal estimate of (chance of success × payoff) / cost**, not a numerical probability model. “Success” means a useful certified improvement on the stated workload, not solving every instance. First-stage budgets intentionally kill weak ideas before implementation projects.

| Rank | Problems | Idea | Classification | Chance / payoff / first-probe cost |
|---|---|---|---|---|
| 1 | 1; also 4, 5, 6 | Certified clearance with displacement skins | Transfer | Medium–high / high / low |
| 2 | 2 | Preserve the input-only embedding transactionally | Speculative mechanism, known invariant | Medium–high for safety / very high / low |
| 3 | 1; also 2 | Fixed-collar, finite lattice feasibility for defect clusters | Speculative | Medium–low / very high / moderate |
| 4 | 4; also 1, 6 | Proof-carrying surface correspondence instead of rediscovering envelope coverage | Transfer + speculative integration | Medium / high / moderate |
| 5 | 2 | Choose a safe grid candidate from a feasible region, not one rounded off-center | Speculative extension of known refinement | Medium / high / moderate |
| 6 | 6 | Pre-certify realization slack before rebuilding the terrain composite | Transfer | Medium–high / medium / low |
| 7 | 7 | Deterministic arithmetic island and canonical decision transcript | Transfer | High for scoped kernels / medium–high / moderate |
| 8 | 8; also 1, 5 | Local untangling plus a separate global degree/boundary certificate | Known components; transfer | High for disk charts / medium / low |
| 9 | 3 | Separate topological obstruction from semantic ambiguity with chain certificates | Transfer | High diagnostic value / medium, limited speed payoff / low |
| 10 | 5 | Search a small cavity before accepting a bad manifold cap | Speculative | Medium–low / medium / moderate |

### 1. Certified clearance with displacement skins

**Problems:** 1, with reusable machinery for 4, 5 and 6.

**Idea in two sentences.** Store a certified lower clearance from a changed primitive or patch to all nonincident geometry, together with how much each side has moved since that certificate was built. Reuse the certificate until its clearance budget is exhausted, and call exact local tests only for unresolved pairs and mandatory shared-feature cases.

**Source chain.** Milenkovic–Sacks [S1] establishes feature separation as the basis for safe rounding; IPC [S3] and conservative inclusion CCD [S4] supply the collision-safety perspective. The reuse mechanism is a transfer from Verlet neighbor-list skins documented in LAMMPS [S6]; [S4] actually cites interval root finding, while its later scalable development [S5] explicitly builds on [S4]. LAMMPS is a conceptual connection, not a claimed citation in the rounding paper.

**Classification:** known separation reasoning; **transfer** of a movement-expiring certificate. The proposed payoff in SafeRounding is speculative.

**Why it can retain the guarantee.** Suppose a complete primitive-pair query certifies a lower bound $L\le d(A,B)$. If the correspondence of old and new primitives bounds every point's displacement by $\Delta_A$ and $\Delta_B$, then

$$d(A',B')\ge L-\Delta_A-\Delta_B.$$

For fixed-connectivity triangles, the maximum vertex displacement is a valid bound for the whole triangle, by barycentric interpolation. A positive right side rules out an intersection. This is a sufficient test, not a claim that a failed test implies collision.

The tempting closest-point shortcut needs two repairs:

1. A point returned on the target proves $d(x,M)\le\|x-q\|$, not the reverse. A lower bound requires a completed certified BVH search or conservative bounds for **all** unvisited nodes.
2. An empty ball around a triangle vertex or centroid does not contain the whole triangle. If a patch is contained in a ball of radius $r$ around $c$, only a lower bound greater than $r+\Delta_A+\Delta_B$ clears it. Large thin triangles can make this weak; primitive-pair or directional certificates may be better.

Use outward-rounded boxes and validated distance comparisons, not a blanket epsilon. Include motion of the supposedly static side; invalidate after topology changes, new triangles, relabeling, or changes to the exclusion set. Incident faces require their existing beyond-shared-feature checks: their ordinary distance is zero. Existing contacts cannot acquire a positive clearance certificate by wishful thinking.

**Expected speed effect:** fewer repeated candidate searches and narrow-phase checks on well-separated regions; no expected benefit at contact clusters. First measure whether complete-query bookkeeping costs more than the saved tests. This differs from the stopped projected-patch prototype and from the failed approximate-local-nearest envelope spike.

**Code/license:** Tight-Inclusion's top-level MIT license was checked [L2]; no code read. LAMMPS documentation only; its implementation is not a source. No IPC code license was checked, so implementation would be paper-derived.

**Main risk:** useful clearance is scarce, and a certified lower bound is as expensive as the check it replaces.

**Cheapest falsification — 2–4 hours:** replay a small existing rounding trace in an external diagnostic harness; record conservative clearance, consumed movement, full rebuild cost, and certificate survival. Include near-coplanar adjacent sheets and simultaneous two-sided motion. Compare measured saved work with certificate cost; abandon if the optimistic avoided-work bound cannot beat baseline. One false clearance on an exact adversarial pair kills the proposed bound immediately.

### 2. Preserve the input-only embedding transactionally, including vertex sectors

**Problem:** 2, Q17.

**Idea in two sentences.** Treat the arrangement produced by input-only snap rounding **including restore** as the immutable topological reference, and allow refinement only as a subdivision and embedding-preserving deformation of that reference. Before committing a candidate hot pixel, validate the complete local rerouting transaction—including induced reroutes—against incidence, cyclic order, boundary order and local face sectors, rather than checking only overlapping edges.

**Source chain.** Hershberger [S8, §§2, 5–7] revisits ordinary and stable snap rounding and documents an actual circuit-verification failure involving unnoticed incidences and pinch-offs. This gives a strong cross-field warning, not a direct solution to Q17; combine it with certifying-algorithm architecture [S12] and the repository's input-only image contract. The earlier stable-rounding work is cited by [S8]; its magnet/pin distinction is described there, but its complete original proof was not independently read.

**Classification:** **speculative** incremental certificate. Known background: snap rounding's collapse-permitting guarantee is weaker than this owner's no-additional-collapse rule.

**Why it could retain the guarantee.** For a candidate move, cut out a disk neighborhood with a fixed boundary and compare old and proposed embedded complexes after suppressing pure subdivision vertices. A sufficient acceptance criterion is an explicit piecewise-linear homeomorphism relative to that boundary, preserving source-chain identity, distinct pre-existing vertices, order along chains, and face labels. A new common vertex between previously separate branches fails, even with zero lost area and no common output edge.

The practical check may represent this by a local rotation system, edge/face incidence and exact geometric embedding tests. **Rotation order alone is not enough**: it does not detect all changes of connectivity, hole nesting, or multiple preimages. The local neighborhood must include the dependency closure of hot-pixel rerouting; a fixed number of rings is not a completeness proof. If that closure is too large or cannot be certified, refuse the candidate rather than silently interpreting it as an input collapse.

This is stronger than the owner's minimum rule and may reject harmless deformations. It therefore proves safety only after the local-to-global gluing argument is established, and does not prove quality or availability of a legal split. Use existing grid-blocked/unsplittable semantics only where their actual definitions apply; otherwise return not-certified rather than inventing an exemption.

**Expected speed effect:** small incidence comparisons for local operations instead of constructing a full dual image every time; possible savings from preventing repair cascades. More refusals can increase triangle count or stall quality improvement. Neither reference speed nor the count target follows from this invariant.

**Code/license:** papers and local specification only. No Triangle, Triangle.NET, owner quadtree, or VLSI implementation is a source.

**Main risk:** induced rerouting is not local, or the strong criterion over-rejects acute corners just as the earlier coarse pixel-owner test did.

**Cheapest falsification — 3–5 hours:** minimize one of the remaining vertex-pinches, then enumerate candidate pixels and insertion orders around it using the independent oracle's input-only restore baseline. Include two sides sharing a distant tip, holes, and half-open pixel-corner contacts. Reject the design if a locally accepted move changes the global embedded reference, or if it reduces to the already-discarded coarse link-ownership rule. Measure safe alternatives and blocked cases separately from eliminated pinches.

### 3. Fixed-collar finite lattice feasibility for the remaining 3D clusters

**Problem:** primarily 1; a related finite-candidate viewpoint helps 2.

**Idea in two sentences.** Replace repeated full-mesh rounding/resolving by a small search over allowed representable vertex positions inside one defect cluster, with an unchanged collar and fixed inherited arrangement labels. Use cheap proposed separators and integer/dyadic candidate coordinates to generate solutions, but accept only after exact local/global interface checks and an explicit surface-correspondence certificate.

**Source chain.** Milenkovic–Sacks [S1] supplies separation constraints; Devillers–Lazard–Lenhart [S2] clarifies that a guaranteed grid construction may require changing the complex and allowing collapses. IRIS, Deits–Tedrake [S7], supplies the transfer of replacing a nonconvex collision-free search by certified convex safe regions; [S12] supplies producer/checker separation. This is a conceptual synthesis, not an algorithm claimed by any of those authors.

**Classification:** **speculative**.

**Why it could retain the guarantee.** Freeze boundary positions, vertex identities and source-side labels. For genuinely disjoint primitive pairs, a proposed rational plane can be checked by evaluating its signed affine form at every vertex of both primitives; strict separation of the convex hulls certifies nonintersection. Fixed separators give linear sufficient constraints on candidate positions. They may be much more restrictive than the true feasible set, so failure is not geometric infeasibility.

Use a finite set of doubles or dyadic lattice points inside the drift budget; verify representability, not merely integer membership in an abstract grid. A rotated lattice generally does not map to exactly representable doubles. Plane offsets, strict margins and drift comparisons need exact/validated arithmetic. No global LLL reduction is necessary for an axis-aligned grid with independent coordinate rounding—the difficulty lies in coupled topology constraints.

For an unchanged triangulation, a verified nondegenerate, collision-free deformation and vertex correspondence provides a surface displacement bound. For remeshing/re-resolving, retain a **covering correspondence in both directions** between old and new patches; bounding only surviving vertices misses removed sheets. A local B-style resolve is eligible only if it preserves the collar and inherited face labels and passes this coverage test. Recomputing a self-union on an open patch is not allowed to choose a different solid.

Check the replacement against every potentially intersecting exterior primitive, not just the collar. A fixed boundary does not prevent a new interior sheet from crossing distant geometry inside the same spatial box.

**Expected speed effect:** moving work from whole-mesh iteration to small defect components could help substantially, but candidate search can be exponential and exterior checks may dominate. The previously measured patch timing is only motivation, not an achievable speed factor.

**Code/license:** paper-only mechanism. No LP/SAT/MILP package selected; no solver license assumed. A diagnostic enumerator can use exact rational arithmetic without becoming library code.

**Main risk:** no representable safe configuration exists within the permitted displacement and fixed combinatorics, or the necessary collar expands to most of the mesh. Existing coincident features and degenerate cells cannot be resolved by strict separation constraints alone.

**Cheapest falsification — 4–8 hours:** take a minimized rounding-only failure and enumerate nearby representable positions for its smallest independent cluster. Test fixed-collar compatibility and two-sided patch coverage before timing anything. Exhausting this limited candidate set kills that search neighborhood, not all possible algorithms; record exactly which constraint blocks every candidate. Stop early if the active component size makes exhaustive probing infeasible.

### 4. Proof-carrying surface correspondence, not repeated envelope rediscovery

**Problems:** 4, with consequences for 1 and 6.

**Idea in two sentences.** Let the construction or simplification step carry a subdivision of each changed triangle plus a target reference triangle for each piece. Verify this explicit coverage witness with convex distance bounds, using the existing general containment machinery only when the cheap witness cannot be certified.

**Source chain.** Wang et al. [S9, theorem 3.2] certifies coverage of a triangle by a union of convex polyhedra without constructing the union. Sacht–Jacobson [S10, §3.2] uses convexity and triangle-to-triangle upper bounds; it explicitly cites [S9], providing a forward citation from envelope work. [S12] motivates emitting witnesses instead of rerunning the producer's search.

**Classification:** **transfer** of known convexity/certificate facts; **speculative** workload-specific witness generation and reuse.

**Why it could retain the guarantee.** Let convex pieces $Q_j$ cover query triangle $T$, and let $S_j$ be reference triangles. Distance to a convex set is convex, so

$$\max_{x\in Q_j} d(x,S_j)\le \max_{v\in\operatorname{vertices}(Q_j)} d(v,S_j).$$

If each vertex distance is certified at most $\varepsilon$, every piece is inside the Euclidean envelope of the reference mesh. The checker must verify **complete coverage** of the query, exact membership in each target triangle, and outward error bounds; a list of near sample points is not enough. A valid target point suffices for an upper distance bound—unlike the clearance filter, it need not be globally nearest.

Possible witnesses are a barycentric subdivision inherited from a local collapse or a small projected patch overlay; target assignment is heuristic, acceptance is not. Avoid approximate clipping holes by storing rational barycentric boundaries and checking the partition. A convex combination of points on different reference triangles need not lie on the reference; it is not a valid single-piece witness without another proof.

**Contract distinction:** this certifies membership in the **Euclidean** epsilon-envelope. It need not reproduce the accepted set of the narrower fixed polyhedral envelope. If that particular union is the requested contract, use polyhedron membership witnesses instead and retain its conservative acceptance. Neither directed test establishes the reverse Hausdorff bound or preserves topology.

**What is actually new here:** neither adaptive subdivision nor the same-reference-triangle bound is new, and the repository already uses related bounds. The hypothesis worth testing is that the operation can cheaply **supply a good partition and target assignment**, avoiding the facet-pair search or repeated nearest discovery. If it cannot, this is just a costly rewrite of `IsWithin` and should stop.

**Expected speed effect:** fewer coverage-discovery predicates on large reduced triangles if a small witness exists. No generic speedup is justified; coplanar terrain is a stronger candidate than folds or thin disconnected bridges.

**Code/license:** fast-envelope's MIT license and README verified [L1, S9b]; bundled predicates/dependencies were not inspected. Sacht–Jacobson implementation license not checked; paper-only transfer.

**Main risk:** witness complexity tracks the full reference triangulation and costs more than the existing adaptive checker.

**Cheapest falsification — 4–6 hours:** on saved Reducer query triangles, construct the cheapest available provenance partition and count pieces, bound evaluations and construction time against the existing query mode. Include triangles spanning creases, holes and disconnected thin bridges. Reject if the witness is not small or if exact coverage checking removes its potential advantage. This is not permission to reopen the stopped polyhedral-envelope implementation without a new measured result.

### 5. Safe-grid candidate regions instead of one rounded off-center

**Problem:** 2, especially grading after Q17 is enforced.

**Idea in two sentences.** Form a small region of geometrically useful Steiner positions, then search its legal grid points in a deterministic order that favors large insertion radius and low additional splitting. Intersect that region with the transactional topology constraints of idea 2, rather than rounding one analytic optimum and declaring the triangle blocked if it fails.

**Source chain.** Üngör [S11] derives off-centers with size/quality guarantees in its continuous-placement setting and explicitly uses local-feature-size reasoning. Shewchuk [S13] unifies Ruppert/Chew refinement through insertion-radius flow graphs and explains diametral lenses and deletion; the transfer is to treat topology as another feasibility constraint on placement, not as a quality heuristic.

**Classification:** **speculative** grid/topology extension of known methods.

**Why it could retain the guarantee.** Exact candidate validation preserves the CDT property on accepted vertices and the original snapped chains. The proposed search changes which valid candidate is selected, not what may collapse. Preserving a packing-style lower bound on insertion radius is the potential route to grading analysis, but grid projection can destroy that bound. A proof would have to derive a discrete admissible-region lemma and re-establish the radius inequalities with rounding and candidate exclusion included.

Finite unused lattice points prove only that an insert-only process cannot insert forever in a bounded region. They do not prove requested quality, practical count, or termination when deletion/reinsertion is allowed. If lens-based refinement deletes free vertices, record a separate well-founded progress argument or a proven no-cycle rule; never claim the insert-only argument still applies. Reference-level triangle count is an empirical gate, not a consequence of asymptotic size optimality—[S13] explicitly notes that its theoretical constants are not practical count guarantees.

**Expected speed effect:** fewer cascades and fewer rejected single-candidate splits may reduce both triangle count and work. Searching many lattice points can erase the gain, so bound the candidate set before prototyping a complete mesher.

**Code/license:** only the permitted papers; no reference mesher code. The off-center paper's proof is not being transplanted as a grid proof.

**Main risk:** the useful region has no legal grid point, especially at acute corners or narrow corridors; stronger topology exposes a genuine representational limit.

**Cheapest falsification — 3–6 hours:** for the remaining fuzz pinches and a small city subset, enumerate unused grid points in the relevant circumdisk/segment span and classify each by geometry, hot-pixel routing, insertion radius and topology. If even exhaustive local candidates cannot improve the offending region under the unchanged rules, stop tuning encroachment lenses and produce an unsatisfiable local fixture for the expert question below. If candidates exist, measure whether a bounded deterministic subset finds them before undertaking a termination proof.

### 6. Pre-certify realization slack before rebuilding terrain composites

**Problem:** 6.

**Idea in two sentences.** Before expensive composite assembly, bound the clipping and realization error on the small changed slope region and choose the admissible initial slack from that bound. Retain the exact final certificate and all current budget caps, but avoid rebuilding the same large composite merely to discover a local support error.

**Source chain.** Local-frame behavior and repeated realization are repository evidence, not a literature discovery. The transfer is the certifying producer/checker split [S12], combined with filtered bounds as used in [S4] and the exact-distance witness reasoning of [S10]; no claim is made that these papers implemented this terrain pipeline.

**Classification:** **transfer**, with a speculative placement of the preflight in the pipeline.

**Why it could retain the guarantee.** Keep the existing tolerance budget, including its reserved rounding allowance. Bound the actual clipping/realization expression with intervals or exact residuals on local coordinates; accept the selected slack only if the sum of all allocated errors remains within the existing budget and the final support/tolerance tests pass. Account for slope/normal error separately from position error: a small vertex displacement alone does not control a skinny triangle's gradient.

If changing slack changes combinatorics, the preflight is no longer a certificate for the new construction. It must use a bound valid across that range or invalidate and recompute. Warm-start prediction can suggest a slack value, but cannot authorize spending more than the fixed budget. Results remain in the certified local frame; translating them back to large coordinates is a new rounding operation requiring a new certificate.

**Expected speed effect:** potentially removes repeat assembly/realization work; little benefit if the first pass already succeeds. It targets the cost identified by the terrain document, not the comparatively small translation pass.

**Code/license:** paper-derived arithmetic; no external implementation selected.

**Main risk:** obtaining the preflight requires most of the expensive assembly, or its bound is so pessimistic that it spends too much tolerance and forces finer meshing.

**Cheapest falsification — 2–4 hours:** inspect existing per-stage timing and residual output for the one-edge and L-shaped cases plus a rotated small-scale input. Determine the earliest stage at which a valid support bound can be known, and the work strictly downstream that a retry repeats. Stop if that removable work is small or if the bound cannot be made valid before the costly stage. Compare residual coverage against final exact checks; do not infer a general bound from fitted samples.

### 7. Deterministic arithmetic island plus canonical decision transcript

**Problem:** 7.

**Idea in two sentences.** Make branch-affecting geometry decisions depend on canonical exact predicates, stable identifiers and explicitly specified arithmetic, while allowing implementation-dependent filters to return only “proved sign” or “unknown.” Use exact integer accumulators for genuinely order-independent reductions and compare a canonical decision transcript across runtimes before comparing final mesh bytes.

**Source chain.** Neal [S14] establishes exact summation of binary floating-point inputs with superaccumulators and order-independent results. Demmel–Nguyen [S15, bibliographic metadata only] and Demmel's public BLAS discussion corroborate reproducibility as a numerical-systems research direction; the certificate architecture comes from [S12].

**Classification:** **transfer**.

**Why it could retain the guarantee.** An exact sum of identical binary inputs is independent of reduction order. But identical sums do not repair different inputs: platform-specific transcendental results, FMA contraction, intermediate overflow/underflow, input parsing or different vertex construction can change terms before accumulation. Exact dot products require unrounded products or a suitable wider/exact product representation; summing already-rounded products is a different specification.

Define the supported arithmetic domain and behavior for NaNs, infinities, signed zero and subnormals. For branch-sensitive operations, use exact dyadic comparisons or proven filters whose bounds match the executed instruction graph. A filter that succeeds on one platform and falls back on another is fine if both produce the same exact sign. Floating constructions and tie-breaks must also be canonical if identical vertices are promised. Parallel “first valid result wins” is disallowed; commit in a stable order.

A decision transcript can contain operation IDs, exact signs, chosen candidates, source IDs and budget decisions. Hashing only sorted triangles can hide a nondeterministic decision path and is not a proof. Conversely, deterministic output is not proof of geometric correctness; keep independent validators.

**Expected speed effect:** mostly a reliability feature, not a free optimization. Exact reductions can enable reproducible parallelism for suitable bulk kernels, but short reductions and construction-heavy code may slow down. Neal's published summation timing is not a C#/ARM prediction.

**Code/license:** Neal paper only, no xsum distribution inspected or licensed for porting here. No external numeric library selected.

**Main risk:** the first divergence is in construction or optimization rather than summation, making a superaccumulator irrelevant.

**Cheapest falsification — 3–6 hours if both machines/runtimes are available:** run a tiny existing cross-runtime fixture set and identify the first divergent branch/constructed coordinate, with fixed ordering and stable serialization. Include cancellation, exact ties and subnormal-scale inputs. Stop the accumulator idea for any kernel whose divergence precedes reduction; if ARM is unavailable, report that validation unavailable rather than claiming cross-platform determinism from one machine.

### 8. Local untangling plus a separate global injectivity certificate

**Problem:** 8; the certificate concept also helps 1 and 5.

**Idea in two sentences.** Use the queued Garanzha untangler to obtain a candidate map, but certify a disk chart's global embedding separately through its boundary and local orientation/degree conditions. Cut or chart non-disk topology explicitly; do not ask a local energy to turn an uncut torus into a planar embedding.

**Source chain.** Garanzha et al. [S16] explicitly distinguishes local from global injectivity and points back to convex-boundary/Tutte-style initialization. The repository's projected-patch certificate gives a closely related local-to-global argument already explored here; [S12] supplies the independent-checker architecture. Original Tutte/Floater papers were not separately opened, so this report attributes their role to [S16], not to a fresh proof review.

**Classification:** **known** distinction and methods; **transfer** of a strict final certificate.

**Why it can retain the guarantee.** For a correctly triangulated oriented disk, a nondegenerate orientation-preserving PL map with a compatible simple one-to-one boundary admits the standard degree argument: an interior regular value has degree one and all preimages contribute positively, so it cannot have multiple sheets. Implementation must check disk topology, consistency across edges, vertex stars, boundary simplicity and orientation; an exact overlap checker is a useful independent gate while validating those hypotheses. Positive per-face signed areas by themselves do not establish these conditions.

For an annulus, check both boundary loops and their relative embedding and degree, or cut to a disk. A closed torus has no injective continuous map into the plane; topology must change in the parameter domain through cuts/charts or the target must change. The numerical untangler's convergence is not a substitute for these facts.

**Expected speed effect:** a boundary/degree certificate may be cheaper than repeated global pairwise overlap tests on suitable charts; it does not by itself fix low-poly LSCM's minimizer. The principal payoff is an honest globally-injective result contract.

**Code/license:** Garanzha paper inspected; accompanying code license not verified, so no code port recommended.

**Main risk:** arbitrary fixed boundaries or non-disk charts violate the easy theorem's hypotheses; certifying merely exposes failure without generating a good map.

**Cheapest falsification — 2–4 hours:** take the low-poly cylinder and torus fixtures, plus a disk whose triangles preserve orientation but whose boundary winds or overlaps. Verify that the proposed checker rejects the latter and that chart cuts make the topology admissible. Run the already-queued candidate generator only after this certificate distinction is pinned; compare global overlap, not just fold count.

### 9. Chain certificates: distinguish impossible closure from ambiguous inside

**Problem:** 3.

**Idea in two sentences.** Before expensive inside/outside fallbacks, classify whether the available oriented patches can satisfy the exact edge-boundary and region-jump equations at all. Return an explicit obstruction when they cannot, and keep any choice of completion or uncertain region labeling separate from this geometric feasibility certificate.

**Source chain.** Feng–Gillespie–Crane [S17] separates bounding and nonbounding curve components using differential/Hodge ideas; Feng–Crane [S18] explicitly produces an implicit completion of imperfect geometry. The proposed discrete chain/integer obstruction test is a **transfer**, not a claim that the surface-curve PDE already solves 3D mesh completion; [S12] supplies witness-based diagnosis.

**Classification:** **transfer** for certification/triage; speculative speed payoff.

**Why it could retain the guarantee.** On a fixed oriented patch complex, write $\partial_2 z=0$ for cancellation of selected patch boundaries. If all prescribed faces must remain and the exact edge-incidence sum is nonzero, no labeling alone can make that prescribed surface closed. More generally, a fixed set of proposed integer jumps admits a region potential only when the sums around every dual cycle vanish; a violating cycle is a compact obstruction witness.

Neither condition is sufficient for a clean solid: bowtie vertices, multiplicities and embedding must still be checked. If patch selection is variable, the trivial empty selection is not an acceptable “solution”; preserve required faces/labels and the original operation's semantics. A closing cap requires a completion policy. For an open sheet, two different completions can give opposite classifications while agreeing with every observed face; exact arithmetic cannot recover unobserved intent.

**Expected speed effect:** skip known-impossible labeling attempts and distinguish structural failures early. It is not a new route to the task's optimistic open-input upper bound and does not negate the already-measured GWN no-go result. If current diagnostics already prove the same obstruction, no new work is justified.

**Code/license:** paper-only transfer; no PDE or topology library code inspected.

**Main risk:** the obstruction check merely repeats existing exact edge diagnostics, or selecting a useful completion is the expensive part after all.

**Cheapest falsification — 2–3 hours:** replay the recorded structurally blocked fallback cases and compare exact obstruction witnesses with current failure reasons. Count only work demonstrably avoided before fallback, not the entire fallback time. Stop if no earlier actionable distinction appears. Use an open disk with two opposite completions as a semantic counterexample to any proposed universally correct labeler.

### 10. Search a small cavity before accepting a bad manifold cap

**Problem:** 5.

**Idea in two sentences.** When the manifold repair has no valid single-vertex candidate, search alternative local triangulations and small multi-vertex insertions inside a fixed enclosing cavity. Make “best certified feasible cavity” the decision unit, rather than asking one cap move to satisfy incompatible separation and quality constraints.

**Source chain.** Portaneri et al. [S19] combines carving with on-demand offset-surface refinement and establishes strict enclosure and termination for its stated algorithm. IRIS [S7] suggests searching within certified safe regions; Garanzha [S16] motivates adding degrees of freedom rather than expecting a local optimizer to fix an infeasible configuration. The cavity search itself is not established by those sources.

**Classification:** **speculative**.

**Why it could retain the guarantee.** Freeze the cavity boundary; verify manifold links, consistent orientation, nondegenerate faces, no intersections with the exterior, and the required offset/envelope constraints. **Strict enclosure is an independent gate:** a closed near-input mesh can exclude part of the input. One sufficient strategy is to operate only in a certified input-free cavity and preserve a verified embedding relative to its boundary, so no input point changes side; otherwise perform the relevant exact containment checks.

Additional insertions alter the termination argument of the original alpha-wrap process. Use a bounded search with rollback for the experiment, not a claim of unconditional improvement. If no feasible candidate is certified, retain the existing safe result and report the unmet quality target. A reference's observed facet size is not automatically a theorem bounding every output facet.

**Expected speed effect:** likely more work per difficult cap; potential improvement in facet size and angle only. It may be cheaper than global remeshing if failures are isolated, but this is unmeasured.

**Code/license:** alpha-wrap paper only. Its CGAL implementation remains unread and is not a port source; no IRIS or untangling code license assumed.

**Main risk:** topology or available geometric clearance makes the desired quality impossible without expanding the cavity or using more offset than permitted.

**Cheapest falsification — 4–6 hours:** isolate one remaining cap and enumerate a small bounded set of two-/three-vertex local alternatives, preserving the collar and enclosure tests. Plot feasibility against cavity radius and available offset. If all improvements require changing the prescribed offset or losing enclosure, record that local infeasibility and stop smoothing/priority-order tuning for that fixture.

## 2. Connections between problems

### A. One inequality, two opposite kinds of certificate

Distance is Lipschitz, but the direction of the bound determines its use:

| Certificate | What it proves | Reuse rule | Problems |
|---|---|---|---|
| Lower clearance $L$ | No contact with excluded geometry | Subtract movement of both sides | 1, 5 |
| Upper deviation $U$ | A point/patch remains within tolerance | Add movement, or recheck a correspondence | 1, 4, 6 |
| Input-only embedding | No refinement-created identification/pinch | Verify each complete local transaction | 2 |
| Boundary/degree certificate | Local map is globally embedded under explicit hypotheses | Invalidate on boundary/topology change | 1, 5, 8 |
| Exact chain/cycle obstruction | A proposed closure/labeling is impossible on a fixed complex | Recompute after incidence/selection change | 3 |

An envelope certificate and a collision certificate must remain separate outputs, even if they share a BVH or exact arithmetic. A patch can be close to the reference and still self-intersect.

### B. A certified pipeline needs correspondences, not just a scalar drift counter

For a Boolean result $E$, rounded surface $R$, and simplified surface $S$, directed bounds compose by the triangle inequality when the intermediate sets really are the ones being certified. For example,

$$h(S,E)\le h(S,R)+h(R,E).$$

Two-sided Hausdorff requires both directions at every relevant stage, or a separate final proof. Deleted components have no surviving vertices to debit. Therefore every topology-changing patch operation must either retain a covering correspondence, prove the deleted region remains covered, or explicitly fail that guarantee.

The same “reference identity + validity region + spent error budget” record can support ideas 1, 3, 4 and 6. This is a conceptual shared contract, not a recommendation to build a new general framework before two consumers show value.

### C. Physics contributes trajectories and slack—not physical simulation

The useful transfer from contact dynamics is a conservative path/collision certificate, not an elastic energy minimization of the whole mesh. The useful transfer from molecular dynamics is an expiring neighbor certificate, not the assumption that geometry moves slowly. The useful robotics transfer is a locally sufficient safe region, not a global completeness guarantee from convex optimization.

### D. Topology must be carried as data

Q17 source-chain identity, 3D patch labels, parameterization cuts, and open-input completion policies all encode information absent from raw coordinates. Discarding it and reconstructing it after rounding is exactly where a small coordinate change can become a large semantic change.

## 3. Dead ends, negative results and limits

These are scoped conclusions, not universal declarations that an entire research field is useless.

| Direction checked | Evidence and reason not to pursue unchanged | What would justify reopening it |
|---|---|---|
| Full Milenkovic expansion or whole-mesh Valque–Lazard default | Task evidence and local SafeRounding verdict already reject them. Repetition is not new research. | A genuinely different locality/interface result with preserved labels and two-sided coverage. |
| Nearest-point radius used directly as an empty ball | Logical counterexample: a returned point may not be nearest. It gives an upper distance bound. | Complete conservative search / independently checked lower bound. |
| Vertex-only or centroid-only collision clearance | A long triangle's interior or an edge pair can collide while its sampled points remain distant. | Bound the whole primitive or use complete primitive-pair tests. |
| IPC barrier energy alone | [S3–S5] make conservative collision handling central. A barrier does not fix initially invalid geometry or prove existence within a rounding budget. | A valid starting configuration, explicit movement bounds and conservative CCD/static certificates. |
| Exact root parity as a universal no-collision test | The CCD project [S4b] explicitly warns that even parity cannot distinguish zero from two roots. | A separately proved single-root restriction or conservative inclusion method. |
| General-purpose 3D snap construction as a practical shortcut | [S2] gives a collapse-permitting continuous construction with very high worst-case complexity. Its abstract is not a practical implementation theorem. | A special-case complexity bound or demonstrably bounded local construction. |
| Lattice reduction as the missing ingredient | Basis reduction metadata [S20] was checked; no direct snap-rounding theorem was obtained. On a rectangular dyadic grid the unconstrained nearest lattice point is easy; coupled topology, representability and drift are the hard part. | Evidence that an actual cluster reduces to a small, useful integer feasibility problem, as in idea 3. |
| Preserve Euler characteristic / Betti numbers only | They cannot establish the required embedding, source identity or absence of self-intersections. A move can change geometry while preserving those counts. | A complete local embedding/degree certificate, not one global statistic. |
| Persistence as permission to remove small features | The persistence stability paper [S21] was resolved bibliographically, not read in full. Even a valid stability theorem would control a chosen filtration, not authorize extra Q17 collapses. | Owner explicitly changes the guarantee to a persistence-scale contract; not proposed here. |
| Stable rounding pins substituted silently for refinement hot pixels | [S8] shows a different rounding model, and documents how relaxed contact handling caused unnoticed incidences. The current contract makes every output vertex hot. | A separately approved change of rounding semantics with a new oracle; not an optimization under the existing contract. |
| Lens encroachment alone fixes Q17 | [S13] addresses refinement quality/termination under stated placement/deletion rules, not new-hot-pixel topological identification. | A proof for the additional discrete topology constraint. |
| Broad forbidden-pixel masks from original links | Local meshing notes already show acute-corner over-rejection and missed shared-tip cases. | Transaction-aware geometry and incidence, not the same mask with another threshold. |
| Faster exact arithmetic solves envelope performance | Local envelope result says facet-pair enumeration dominates and exact fallback is rare. Acceptance is separately limited by envelope shape. | Profiling demonstrates a changed workload/kernel bottleneck. |
| Repeat-triangle cache, arbitrary local nearest guess, hash grid, ordinary subdivision | Already measured or implemented in the envelope notes; repeated queries are rare, nonnearest guesses loosen bounds, and subdivision does not change the narrow envelope's accepted set. | A new source of exact coverage/provenance information, not more tuning of those levers. |
| Generic projected patch certificate / speculative parallel Reducer checks | Both have stopped local prototypes with negative timings. | A new amortization or workload argument, measured before another implementation. |
| Better winding number evaluation closes structural cracks | Local spike already separates this limit; [S17–S18] do not infer unique missing geometry. | A specified completion policy or a new feasible patch set, with the changed semantics stated. |
| Positive Jacobians alone imply a globally injective UV map | [S16] explicitly targets local injectivity; boundary and domain topology remain independent obligations. | Valid disk/boundary/degree hypotheses or exact global embedding tests. |
| Local-frame computation followed by unchecked UTM export | The final addition is another rounding operation and can exceed the local certified slack. | Keep local output, or certify the exported coordinates under an explicit budget. |
| Superaccumulators make an entire pipeline deterministic | [S14] proves an exact sum for identical terms, not identical term generation or topology decisions. | A scoped arithmetic and ordering specification plus cross-platform evidence. |

**Breadth not overstated.** O-minimality, discrete Morse methods, interval Newton/Krawczyk, validated celestial mechanics, lattice Boltzmann and crystal-growth models were considered as analogies, but this pass did not obtain and verify a relevant primary result for them. They are **unresolved leads**, not demonstrated dead ends. No claim of exhaustive novelty is made for the proposed mechanisms.

## 4. Expert questions worth asking

| Question | Closest researchers/groups from opened sources | Why a short answer could change the plan |
|---|---|---|
| Can 3D separation be certified cheaply per patch with a fixed collar, including incident features and near-zero contacts? | Victor Milenkovic / Elisha Sacks [S1]; Olivier Devillers / Sylvain Lazard / William Lenhart [S2] | Distinguishes a useful sufficient certificate from rebuilding full separation checks. |
| What is the minimal invariant preventing a new hot pixel from pinching an input-only SR+restore embedding? Can its dependency closure be bounded? | John Hershberger, Siemens EDA [S8]; Alberto Belussi / Sara Migliorini / Mauro Negri / Giuseppe Pelagatti [S22] | The industrial incidence failure is unusually close in shape to Q17, but the current contracts differ. |
| Is there a discrete admissible-Steiner-region lemma retaining insertion-radius packing after topology exclusions? | Alper Üngör [S11]; Jonathan Shewchuk [S13]; Miller/Pav/Walkington are task-supplied leads, not independently surveyed here | Could establish whether the triangle-count target is merely engineering or conflicts with representability/quality. |
| Can an operation-generated covering correspondence beat repeated envelope discovery on large reduced triangles at nearly full tolerance? | Bolun Wang / Teseo Schneider / Marco Attene / Daniele Panozzo [S9]; Leonardo Sacht / Alec Jacobson [S10] | Tests the actual workload mismatch, not a repeat of published remeshing benchmarks. |
| For broken surfaces, what completion semantics should be exposed separately from guaranteed geometry? | Nicole Feng / Mark Gillespie / Keenan Crane [S17–S18] | Prevents an exact implementation from certifying an arbitrary interpretation as ground truth. |
| Can manifold repair use bounded cavity retriangulation while retaining strict enclosure and a termination measure? | Cédric Portaneri / Mael Rouxel-Labbé / Michael Hemmer / David Cohen-Steiner / Pierre Alliez [S19] | Identifies whether the bad caps are one-candidate artifacts or local feasibility limits. |
| Which reproducible accumulation design is appropriate for short geometry reductions, not large streaming arrays? | Radford Neal [S14]; James Demmel / Hong Diep Nguyen [S15] | Avoids assuming bulk summation results apply to small C# kernels. |
| Which global conditions should accompany Garanzha's local untangler for annuli and multiple boundary loops? | Vladimir Garanzha / Igor Kaporin / Liudmila Kudryavtseva / François Protais / Nicolas Ray / Dmitry Sokolov [S16] | Separates a candidate generator improvement from a true global-map contract. |

### Recommended next action

Start with **three bounded probes, not implementation projects**: clearance-survival accounting (idea 1), a minimized Q17 transaction/embedding counterexample search (idea 2), and fixed-collar representability feasibility on one 3D defect (idea 3). Only a surviving probe earns a tier-L design. The envelope witness probe should follow if its construction can reuse real provenance rather than reproduce an existing nearest/subdivision algorithm.

## 5. Opened-source ledger

### Substantive primary sources

“Selected text” means the source was actually opened and relevant passages inspected; it does not claim exhaustive proof review. Metadata-only sources support bibliographic statements, not algorithmic guarantees.

- **[S1] Victor Milenkovic and Elisha Sacks. _Geometric rounding and feature separation in meshes._ Computer-Aided Design 108, 2019.** [arXiv record](https://arxiv.org/abs/1805.03252), [author preprint PDF](https://arxiv.org/pdf/1805.03252), [HTML](https://arxiv.org/html/1805.03252v1). Selected text: separation definition, rounding bound, modification/expansion, limitations. Published DOI 10.1016/j.cad.2018.10.003 verified through Crossref; journal version not separately read.
- **[S2] Olivier Devillers, Sylvain Lazard and William J. Lenhart. _3D Snap Rounding._ SoCG / LIPIcs 99, 2018.** [Proceedings page](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.SoCG.2018.30). Abstract and bibliography opened, not full proof. The abstract specifies a continuous map permitting feature collapse and an L-infinity displacement bound, not arbitrary strict preservation of every original feature.
- **[S3] Minchen Li, Zachary Ferguson, Teseo Schneider, Timothy Langlois, Denis Zorin, Daniele Panozzo, Chenfanfu Jiang and Danny M. Kaufman. _Incremental Potential Contact: Intersection- and Inversion-free Large Deformation Dynamics._ ACM TOG 39(4), 2020.** [Author project page](https://ipc-sim.github.io/). Abstract, guarantees as stated by authors, citation; full paper not independently reviewed here.
- **[S4] Bolun Wang, Zachary Ferguson, Teseo Schneider, Xin Jiang, Marco Attene and Daniele Panozzo. _A Large Scale Benchmark and an Inclusion-Based Algorithm for Continuous Collision Detection._ ACM TOG 40(5), 2021.** [Project page](https://continuous-collision-detection.github.io/tight_inclusion/), [paper](https://continuous-collision-detection.github.io/tight_inclusion/CCD-benchmark-paper.pdf). Selected full-text sections: inclusion functions, filtered predicates, minimal separation, comparison caveats.
- **[S4b] Continuous Collision Detection project, research overview.** [Overview](https://continuous-collision-detection.github.io/). Also opened for its summary of **Bolun Wang, Zachary Ferguson, Xin Jiang, Marco Attene, Daniele Panozzo and Teseo Schneider, _Fast and Exact Root Parity for Continuous Collision Detection_, Computer Graphics Forum / Eurographics, 2022**; only project abstract read, including its zero-versus-two-roots warning.
- **[S5] David Belgrod, Bolun Wang, Zachary Ferguson, Xin Zhao, Marco Attene, Daniele Panozzo and Teseo Schneider. _Time of Impact Dataset for Continuous Collision Detection and a Scalable Conservative Algorithm._ arXiv:2112.06300, first submitted 2021; opened record v5, 2025.** [Project](https://continuous-collision-detection.github.io/scalable_ccd/), [arXiv record](https://arxiv.org/abs/2112.06300). Abstract/version history. Project labels it “in submission” and gives a 2023 citation; no journal venue is inferred.
- **[S6] LAMMPS developers. _Neighbor lists_, Developer Guide §4.4.3.** [Documentation](https://docs.lammps.org/Developer_par_neigh.html), accessed 2026-11-03. Selected documentation: skin distance, movement-triggered rebuilding, memory/layout. Documentation only; no implementation read. This is an engineering source, not a dated research-paper claim.
- **[S7] Robin Deits and Russ Tedrake. _Computing Large Convex Regions of Obstacle-Free Space Through Semidefinite Programming._ WAFR 2014; Algorithmic Foundations of Robotics XI, Springer, 2015.** [Author PDF](https://groups.csail.mit.edu/robotics-center/public_papers/Deits14.pdf). Selected text: separating-plane and inscribed-ellipsoid alternation, local region rather than complete free-space coverage. Proceedings year verified through Crossref, DOI 10.1007/978-3-319-16595-0_7.
- **[S8] John Hershberger. _Snap Rounding: A Cautionary Tale._ SoCG / LIPIcs 332, 2025.** [Proceedings page](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.SoCG.2025.57), [paper PDF](https://drops.dagstuhl.de/storage/00lipics/lipics-vol332-socg2025/LIPIcs.SoCG.2025.57/LIPIcs.SoCG.2025.57.pdf). Substantial selected text: ordinary rounding, stable magnets/pins, relaxed rounding, overlooked incidences, industrial requirements. Its references were followed backward conceptually; no claim that their originals were all read.
- **[S9] Bolun Wang, Teseo Schneider, Yixin Hu, Marco Attene and Daniele Panozzo. _Exact and Efficient Polyhedral Envelope Containment Check._ ACM TOG 39(4), 2020.** [Author PDF](https://cims.nyu.edu/gcl/papers/2020-Fast-Envelope.pdf). Selected text: convex polyhedra, theorem 3.2, filtering, acceleration. DOI 10.1145/3386569.3392426 verified through Crossref.
- **[S9b] Same authors, fast-envelope project README.** [README](https://raw.githubusercontent.com/wangbolun300/fast-envelope/master/README.md). Opened for the authoritative paper link, contract, dependency/compiler caveats; no implementation inspected.
- **[S10] Leonardo Sacht and Alec Jacobson. _Cascading upper bounds for triangle soup Pompeiu-Hausdorff distance._ Computer Graphics Forum 43(5) / Symposium on Geometry Processing, 2024.** [arXiv](https://arxiv.org/abs/2406.10357), [HTML](https://arxiv.org/html/2406.10357v1), [PDF](https://arxiv.org/pdf/2406.10357). Selected text: branch-and-bound, convex target bounds and subdivisions, relation to envelope work. DOI 10.1111/cgf.15129 verified through Crossref. No floating-point certification theorem is inferred merely from mathematical upper-bound formulas.
- **[S11] Alper Üngör. _Off-Centers: A New Type of Steiner Points for Computing Size-Optimal Quality-Guaranteed Delaunay Triangulations._ LATIN 2004; later Computational Geometry 42(2), 2009.** [Author paper PDF](https://www.cise.ufl.edu/~ungor/papers/offCenter.pdf), [publication list](https://www.cise.ufl.edu/~ungor/publications.html), [author page](https://www.cise.ufl.edu/~ungor/). Selected conference-paper text: off-center construction, feature-size and packing assumptions. Journal DOI 10.1016/j.comgeo.2008.06.002 and conference DOI 10.1007/978-3-540-24698-5_19 verified through Crossref; journal full text not read.
- **[S12] Ross M. McConnell, Kurt Mehlhorn, Stefan Näher and Pascal Schweitzer. _Certifying algorithms._ Computer Science Review, 2011.** [Author preprint PDF](https://people.mpi-inf.mpg.de/~mehlhorn/ftp/CertifyingAlgorithms.pdf). Introduction and contents inspected, including the producer/witness/checker definition; not all 94 preprint pages read. DOI 10.1016/j.cosrev.2010.09.009 verified through Crossref.
- **[S13] Jonathan Richard Shewchuk. _Delaunay Refinement Algorithms for Triangular Mesh Generation._ Computational Geometry 22(1–3), 2002.** [Author PDF](https://people.eecs.berkeley.edu/~jrs/papers/2dj.pdf), [research-reference page](https://www.cs.cmu.edu/~quake/triangle.research.html), [paper directory](https://people.eecs.berkeley.edu/~jrs/papers/). Selected text: size-optimality caveat, insertion-radius analysis, small-angle and lens/deletion discussion. Some PDF mathematical glyphs extracted poorly; no numerical angle theorem is transcribed from those corrupted formulas.
- **[S14] Radford M. Neal. _Fast exact summation using small and large superaccumulators._ arXiv preprint 1505.05571, 2015.** [Record](https://arxiv.org/abs/1505.05571), [HTML](https://arxiv.org/html/1505.05571v1), [PDF](https://arxiv.org/pdf/1505.05571). Selected text: exact/order-independent sum, fixed-point representation, distinction from exact products. No software distribution read.
- **[S15] James Demmel and Hong Diep Nguyen. _Fast Reproducible Floating-Point Summation._ IEEE ARITH, 2013.** Crossref metadata opened, [DOI](https://doi.org/10.1109/arith.2013.9). [Demmel author page](https://people.eecs.berkeley.edu/~demmel/) opened for its discussion of reproducible BLAS. Original paper not obtained; no detailed theorem attributed to it.
- **[S16] Vladimir Garanzha, Igor Kaporin, Liudmila Kudryavtseva, François Protais, Nicolas Ray and Dmitry Sokolov. _Foldover-free maps in 50 lines of code._ ACM TOG / SIGGRAPH, 2021.** [Record](https://arxiv.org/abs/2102.03069), [HTML](https://arxiv.org/html/2102.03069v1), [preprint PDF](https://arxiv.org/pdf/2102.03069). Selected text: local/global distinction, untangling formulation, scope of guarantees. Published DOI 10.1145/3450626.3459847 verified through Crossref.
- **[S17] Nicole Feng, Mark Gillespie and Keenan Crane. _Winding Numbers on Discrete Surfaces._ ACM TOG / SIGGRAPH, 2023.** [Author project and technical explanation](https://nzfeng.github.io/research/WNoDS/). Abstract, bounding/nonbounding curves, jump functions and Hodge explanation read; full paper not independently read in this pass. The formula typography on a web page is not used as a formal algebraic definition here.
- **[S18] Nicole Feng and Keenan Crane. _A Heat Method for Generalized Signed Distance._ ACM TOG 43(4), 2024.** [Author project](https://nzfeng.github.io/research/SignedHeatMethod/), [author homepage](https://nzfeng.github.io/). Abstract, algorithm explanation and relation to later Points as Tori read; no new claim about that later method's correctness. DOI 10.1145/3658220 appears on the project page.
- **[S19] Cédric Portaneri, Mael Rouxel-Labbé, Michael Hemmer, David Cohen-Steiner and Pierre Alliez. _Alpha Wrapping with an Offset._ ACM TOG 41(4) / SIGGRAPH, 2022.** [HAL record](https://inria.hal.science/hal-03688637v1), [institutional manuscript](https://inria.hal.science/hal-03688637/document). Abstract and selected method sections inspected; no implementation read. DOI 10.1145/3528223.3530152 verified through Crossref.
- **[S20] A. K. Lenstra, H. W. Lenstra and L. Lovász. _Factoring polynomials with rational coefficients._ Mathematische Annalen, 1982.** Crossref metadata opened, [DOI](https://doi.org/10.1007/BF01457454). Full text not obtained; not evidence for a geometry transfer theorem.
- **[S21] David Cohen-Steiner, Herbert Edelsbrunner and John Harer. _Stability of Persistence Diagrams._ Discrete & Computational Geometry, published online 2006, journal volume 2007.** Crossref metadata opened, [DOI](https://doi.org/10.1007/s00454-006-1276-5). Full text not obtained; no detailed stability bound used.
- **[S22] Alberto Belussi, Sara Migliorini, Mauro Negri and Giuseppe Pelagatti. _Snap Rounding with Restore._ ACM Transactions on Spatial Algorithms and Systems, 2016.** Crossref metadata opened, [DOI](https://doi.org/10.1145/2811256). Publisher access failed. The repository/task provides the SRR role; no independently read full-text SRR guarantee is claimed.

### License pages actually opened

- **[L1] fast-envelope LICENSE:** [MIT license](https://raw.githubusercontent.com/wangbolun300/fast-envelope/master/LICENSE), copyright 2019 Wang, Schneider, Hu, Attene, Panozzo. Does not independently license every bundled dependency.
- **[L2] Tight-Inclusion LICENSE:** [MIT license](https://raw.githubusercontent.com/Continuous-Collision-Detection/Tight-Inclusion/main/LICENSE), copyright 2020 named authors. No dependency/source audit performed.

### Additional successful opens and discovery-only records

These are included for completeness, not promoted into supporting citations:

- [NYU Geometric Computing Lab homepage](https://cims.nyu.edu/gcl/) — paper discovery.
- [Alec Jacobson homepage](https://www.cs.toronto.edu/~jacobson/) — Sacht paper discovery and current research contact.
- [Quake papers index](https://www.cs.cmu.edu/~quake/papers.html) — discovery only.
- [Shewchuk 1996 paper title page](https://www.cs.cmu.edu/~quake/tripaper/triangle0.html) — title/venue only; no implementation section followed.
- [arXiv 1505.06471](https://arxiv.org/abs/1505.06471) — **mistaken identifier**, actually _Syntomic complexes and p-adic nearby cycles_; irrelevant, discarded, not a reproducibility source.
- Crossref searches used `https://api.crossref.org/works?rows=1&query.title=<URL-encoded title>` (some initial searches used `rows=2`) for S1, S7, S9–S12, S15–S16, S19–S22 and the title “Globally injective parametrization with arbitrary boundary.” The latter returned **Ofir Weber and Denis Zorin, _Locally injective parametrization with arbitrary fixed boundaries_, ACM TOG, 2014**, DOI [10.1145/2601097.2601227](https://doi.org/10.1145/2601097.2601227); metadata only, not used as a global guarantee.
- The initial two-result Crossref calls also exposed metadata for _Rounding Meshes in 3D_ (2020, DOI 10.1007/s00454-020-00202-2), _Stable snap rounding_ (2013, DOI 10.1016/j.comgeo.2012.02.011), _Exact Polyhedral Machining_ (1999, DOI 10.1007/978-0-387-35392-0_28), _Foldover-Free Image Warping_ (1998, DOI 10.1006/gmip.1998.0454), _Continuous collision detection algorithm for large-scale deformable objects_ (2016, DOI 10.11834/jig.20160708), and _Feature-preserving shrink wrapping with adaptive alpha_ (2024, DOI 10.1016/j.cagd.2024.102321). None was read as a substantive source.
- A Crossref query for “Certified computation with interval arithmetic” returned **J. R. Johnson, _Real algebraic number computation using interval arithmetic_, ISSAC 1992**, DOI 10.1145/143242.143311. Metadata only; no theorem used.
- [HAL API](https://api.archives-ouvertes.fr/search/?q=title_t%3A%22Alpha%20wrapping%20with%20an%20offset%22&fl=title_s,authFullName_s,uri_s,fileMain_s,producedDateY_i&wt=json) — S19 manuscript discovery.
- arXiv title searches actually opened: [geometric rounding](https://arxiv.org/search/?searchtype=title&query=Geometric%20Rounding%20and%20Feature%20Separation%20in%20Meshes), [envelope](https://arxiv.org/search/?searchtype=title&query=Exact%20and%20Efficient%20Polyhedral%20Envelope%20Containment%20Check), [foldover-free maps](https://arxiv.org/search/?searchtype=title&query=Foldover-free%20maps%20in%2050%20lines%20of%20code), [alpha wrap](https://arxiv.org/search/?searchtype=title&query=Alpha%20wrapping%20with%20an%20offset), [floating-point self-intersection resolution](https://arxiv.org/search/?searchtype=title&query=Resolving%20self-intersections%20in%203D%20meshes%20while%20preserving%20floating-point%20coordinates), [fast reproducible summation](https://arxiv.org/search/?searchtype=title&query=Fast%20Reproducible%20Floating-Point%20Summation), [cascading bounds](https://arxiv.org/search/?searchtype=title&query=Cascading%20upper%20bounds), [fast exact summation](https://arxiv.org/search/?searchtype=title&query=Fast%20exact%20summation), and [all-fields geometric rounding](https://arxiv.org/search/?query=geometric%20rounding%20feature%20separation&searchtype=all). Unrelated search hits were not followed. An empty result is only a search outcome.

### Failed or unhelpful access attempts

No paywall/network bypass was attempted. Direct public author manuscripts were used where already discoverable; a blocked source is not listed as read.

| URL or query | Outcome |
|---|---|
| [Google: Hershberger cautionary tale](https://www.google.com/search?q=Hershberger+Snap+rounding+a+cautionary+tale+2025), [geometric rounding](https://www.google.com/search?q=Geometric+rounding+feature+separation+meshes+Milenkovic+Sacks+2019+pdf), [envelope](https://www.google.com/search?q=Exact+Efficient+Polyhedral+Envelope+Containment+Check+Wang+2020) | JavaScript/challenge-like HTML, no usable research results; not pursued. |
| [Bing: geometric rounding](https://www.bing.com/search?q=Geometric+rounding+feature+separation+meshes+Milenkovic+Sacks+2019), [SRR](https://www.bing.com/search?q=Snap+rounding+restore+Belussi+Migliorini+2016) | Unrelated results; not treated as evidence. |
| [Incorrect NYU PDF path](https://cims.nyu.edu/gcl/papers/2020-Envelope.pdf), [paper-list path](https://cims.nyu.edu/gcl/papers.html), [guessed fast-envelope site](https://wangbolun300.github.io/fast-envelope/) | 404; correct paper found through README. |
| [Guessed ACCD path](https://ipc-sim.github.io/ACCD/) | 404; actual CCD project used. |
| [Guessed meshing survey PDF](https://people.eecs.berkeley.edu/~jrs/papers/meshSurvey.pdf) | 404; allowed 2002 paper found through author index. |
| [Guessed signed-heat path](https://nzfeng.github.io/research/2024_SignedHeat/) | 404; correct project found on author homepage. |
| [Guessed reproducibility PDF](https://people.eecs.berkeley.edu/~demmel/papers/reproducible.pdf) | 404. |
| [ReproBLAS site](https://bebop.cs.berkeley.edu/reproblas/) | 403; no bypass. |
| [Persistence publisher](https://link.springer.com/article/10.1007/s00454-006-1276-5), [LLL publisher](https://link.springer.com/article/10.1007/BF01457454) | 406; metadata only. |
| [SRR publisher](https://dl.acm.org/doi/10.1145/2811256) | 403; full text not read. |
| [Alliez legacy homepage](https://www-sop.inria.fr/members/Pierre.Alliez/) | Redirect to a blocked page; paper obtained through HAL metadata, not a bypass. |
| [Guessed Jacobson research directory](https://www.cs.toronto.edu/~jacobson/research/) | 404; homepage used. |

### Citation-tracing coverage and limits

Actual forward links inspected include **Wang envelope 2020 → Sacht–Jacobson 2024**, **Wang CCD 2021 → Belgrod et al. later scalable CCD**, and **earlier snap-rounding work → Hershberger's 2025 industrial retrospective**. Backward bibliographies were inspected in the rounding, CCD, off-center, envelope and mapping papers; promising mechanisms were then checked against the opened primary sources above. This is a targeted citation-chain pass, not a complete cited-by search for every source. In particular, SRR's unavailable full text and the unverified discrete-refinement theorem remain explicit evidence gaps.

## Verification of this deliverable

No empirical performance improvement, city-dataset fix, cross-platform pass, or new theorem has been claimed. The original experiments described above were not run for this research snapshot. A first round of synthetic probes on hand-built fixtures (two-sided clearance, a vertex-only pinch, a five-vertex fan) was reviewed and not kept: they confirmed the stated inequalities on toy models but tested no real trace, and the vertex-only pinch is already a rule of the 2D oracle (RefinementPinch, #367). The experiments above remain open. Library source and unrelated working-tree changes are untouched. Documentation-only research does not require library builds or test runs.
