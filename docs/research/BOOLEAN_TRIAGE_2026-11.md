# Boolean findings triage (2026-11)

Diagnosis only: no library change here. It covers the issues collected for the robustness round after 3.0
([roadmap](../roadmap/exact-boolean.md), "Robustness round after 3.0"): #985, #993, #994, #1010 and #1011. The bugfix round
adds the owner's CsCheck / MeshFuzzPilot findings and then fixes one root cause per PR.

Method: #985 was reproduced procedurally (master 00784cfc). #993 and #994 were reproduced on master by fetching Thingi10K at
run time and placing the pairs with `ContactTransforms.Build`, the same code the `thingi-contact` class uses. #1010 and
#1011 were classified from the raw rows on the data branch `runs/abc-chained` (`run-43994376178/results.jsonl`,
`chains.csv`), and the abc-chained mechanisms were reproduced procedurally where possible. No ABC file was fetched in
this container.

## Summary

| root cause | issues (cases) | where | kind | needs exact review / mutation gate |
|---|---|---|---|---|
| [RC1](#rc1-saferounding-removes-the-whole-result-of-an-anisotropic-solid) SafeRounding removes a whole thin component, here the whole result, and reports Success and clean | #985 (all) | `mesh_ops/MeshRoundingRepair.cs` | library defect | no exact code; the test it changes is a geometric bound, so review it like one; no mutation gate |
| [RC2](#rc2-edge-contacts-of-the-exact-solid-read-as-not-closed-by-the-benchmark-eval) Edge contacts of the exact solid read as "not closed" / self-intersecting by the benchmark eval | #994 (2 of 2), #993 (7 of 8 differences, plus the p0011 intersection), #1010 (8 of the 10 "not closed"), #1011 (7 of 12) | `benchmarks/BooleanComparison/Program.cs` (eval) | oracle artefact | no |
| [RC3](#rc3-inside-out-from-a-floating-point-volume-sign-of-sub-ulp-slivers) "Inside-out" from a floating-point volume sign of sub-ulp slivers | #1010 (iud p0017) | `mesh_ops/MeshPostRepairCleanup.cs` (`FindInvertedComponents`, used by `NotCleanReason`) | oracle false positive; **owner decision** (NotCleanReason semantics) | adds an error bound: yes, mutation gate |
| [RC4](#rc4-the-rounding-repair-leaves-defects-roundingclean--false-on-near-coincident-surfaces) The rounding repair leaves defects (`RoundingClean == false`, Status Success) | #1011 (5 of 12), #1010 (4 of 19), #993 (p0011 difference) | `MeshRoundingRepair` | documented limit, real defects; **not diagnosed further** (needs a runner rerun) | depends on the fix |

The remaining "bowties" results of #993 / #1010 / #1011 are vertex contacts of the exact solid with the rounding clean.
They are expected for solids that touch at a vertex, like RC2 at a point, and no case was found that is not.

The volume-identity errors above the class tolerance come from components that `SafeRounding` removed: #993 p0024 9.4e-6
(40 removed), #993 p0001 5.2e-7 (160 removed), #1010 dui p0014 s3 6.0e-6 (169 removed), #1010 ucd p0024 s2 4.0e-7 (16
removed). That loss is by design (owner decision 2026-11-16) and is not a separate root cause.

## RC1: SafeRounding removes the whole result of an anisotropic solid

**Issues:** #985 (both flattenings, 2^-46 and 2^+46, all six families).

**Stage:** SafeRounding (`MeshRoundingRepair.Apply`). The arrangement, classification and assembly are right: with
`SafeRounding = false` the union is the exact, closed tetrahedron A (4 triangles, every vertex an input point).

**Mechanism** (nested family, measured on master):
1. The flattened input A is already "degenerate triangles" by `NotCleanReason` (area below 1e-12 of the squared
   diagonal). Its two faces perpendicular to the flattened axis have an area of about 4.5e-13, against a threshold of
   about 1.3e-10. The unrounded result is that same tetrahedron, so it gets the same verdict.
2. The repair treats those exact triangles as rounding defects. Input vertices are only preferred, not hard fixed
   (`is_fixed`, `MeshRoundingRepair.cs:1793`; `FixedHard` is false for the boolean), so the repair moves input vertices to
   fix them: 1 move at 2^-46 (MaxDrift 6.4e-11), 2 moves at 2^+46 (MaxDrift 2251, where `maxLength` is 5.6e9); no
   contraction, flip or zip at either scale. Each move goes through `try_move` (`:2610`), which adds the vertex to
   `touched` (`:2630`). That is how `touched` becomes non-empty at both scales; `bad` is non-empty too (2 initial
   degenerate triangles at 2^-46, 4 at 2^+46).
3. `remove_tiny_components` (`MeshRoundingRepair.cs:2940`, called at `:676`) now sees a closed component with a
   degenerate triangle and a touched vertex. The test at `:2974` decides, and a different branch of it fires at each scale
   (measured on the input A, which is the unrounded result):
   - **2^-46** (diagonal 11.3, area 64, V 1.21e-12): `tiny` is false (area 64 ≥ 1e4 · `sizeArea` = 1.28e-6), and `thin`
     is true (2|V| = 2.4e-12 < `maxLength` · area = 1.13e-4 · 64 = 7.2e-3). The volume guard (`|V| >= 1e-3 A^1.5`,
     `:2980`; here 0.51) does not save it, because a slab has little volume for its area. Removed as **thin**.
   - **2^+46** (diagonal 5.63e14, area 7.69e15, V 6.0e15): `tiny` is true (area 7.69e15 < 1e4 · `sizeArea` = 1e4 · 1e-12
     · diagonal² = 3.17e21), because a needle has little area for the square of its length. `thin` is true as well
     (1.2e16 < 4.3e25). The guard only applies to components that are not tiny, so it is removed as **tiny**.

   Either way the component removed is the whole result.
4. Nothing is left to be degenerate or to intersect, so `RoundingClean` is true and `Compute()` returns Success with
   0 triangles (`ExactMeshBoolean.cs:707`).

Both tests use the largest component as the scale reference (`sizeAreaC`, `maxLengthC`, `MeshRoundingRepair.cs:810`).
In the nested family that is the very component that gets removed, so the result is empty. Its thickness is about 64
ulps at 2^-46 and about 128 ulps (8 units at coordinates near 5.6e14) at 2^+46, so it can be represented in doubles at
both scales. It is thin or tiny only relative to an isotropic bound. **The step-by-step mechanism above is shown for the
nested family only.** The other five families end the same way (0 triangles, per the issue's probe), but which edits
make their component bad or touched, and which branch removes it, was not traced.

**Procedural repro** (as in the issue; measured: `tris=0 removedComp=1 moves=1 (2^-46) / 2 (2^+46), RoundingClean=True`):

```csharp
static DMesh3 Tet(double[][] v) {
    var m = new DMesh3();
    foreach (var p in v) m.AppendVertex(new Vector3d(p[0], p[1], p[2]));
    m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(0, 3, 2); m.AppendTriangle(1, 2, 3);
    return m;
}
foreach (double s in new[] { Math.Pow(2, -46), Math.Pow(2, 46) }) {
    DMesh3 a = Tet(new[] { new[] { 0.0, 0, 0 }, new[] { 8.0, 0, 0 }, new[] { 0.0, 8, 0 }, new[] { 0.0, 0, 8 } });
    DMesh3 b = Tet(new[] { new[] { 1.0, 1, 1 }, new[] { 3.0, 1, 1 }, new[] { 1.0, 3, 1 }, new[] { 1.0, 1, 3 } });
    foreach (var m in new[] { a, b }) foreach (int v in m.VertexIndices()) { var p = m.GetVertex(v); p.x *= s; m.SetVertex(v, p); }
    var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
    Assert.True(op.Compute());
    // today: Status Success, Result.TriangleCount == 0, RoundingClean == true, Rounding.RemovedComponents == 1
    // expected: the closed tetrahedron a (or a result that is not reported clean)
}
```

**Proposed fix (one PR, an owner decision on the output).** Options, smallest first:
- (a) Never remove the component that sets the scale (the largest one), and never remove *all* components. If the thin
  test fires there, keep the component and count it in `ScaleDisparity`, so the result is not clean. This stops the
  "Success, clean, empty" violation in every family.
- (b) Do not treat as rounding defects the triangles whose three vertices are exact input points (`ExactVertexF`): they
  are exact triangles of the arrangement, and their degeneracy belongs to the input. They would count in
  `RemainingDegenerate` but not trigger edits or removal. This fixes the nested and shared families completely, but not
  the families with new vertices.
- (c) Make the thin test anisotropic: compare the thickness 2|V|/A with the drift the edits actually caused in that
  component (`MaxDrift` of the component), not with `maxLength`.

(a) is the minimal guarantee fix. (b) and (c) change which components survive on real data, so they need the Thingi10K
and abc-chained before/after counts. The roadmap counts the flattened case as matching the thin-shell decision and the
2^+46 case as a real bug. The mechanism is the same for both, so (a) changes both, which is the owner's call.

**Risk:** (a) can turn today's "clean" empty results into non-clean kept ones, which changes `RoundingClean` counts in the
benchmarks. (b) and (c) change outputs (owner-reserved). `MeshRoundingRepair.cs` is not in the exact-code list, but the
thin test is a geometric bound: review it like an error bound. No mutation gate applies by the rule (not
`math/Exact*`, not an error bound of the exact predicates).

## RC2: edge contacts of the exact solid read as "not closed" by the benchmark eval

**Issues:** #994 (p0012 and p0021: union); #993 (p0001, p0009, p0014, p0020, p0021, p0023, p0024: difference;
p0011 intersection); #1010 (8 of the 10 "not closed"; ucd p0007 and iud p0020 are RC4); #1011 (ucd p0005 s1 union and difference, ucd p0019 s3 union, ucd p0004
s2 difference, iud p0003 s1 difference, dui p0000 s3 and dui p0007 s3 difference). #993 p0011's *difference* is RC4.

**Stage:** none in the library. The result is right; the oracle misreads it.

**Mechanism.** Where the exact solid touches itself along an edge (two wedges meeting at a line), the exact result has
a non-manifold edge. `ExactMeshBoolean` splits it into manifold pairs with duplicated vertices, by design (class
comment; `NonManifoldEdges`, `Contacts`). The eval in `benchmarks/BooleanComparison/Program.cs` then:
1. welds by exact position (`:341-346`), so the 4 triangles at the contact edge share one edge;
2. rebuilds a `DMesh3`. `AppendTriangle` refuses the third and fourth triangle on that edge, and the fallback appends
   them with their own vertices (`:368-371`), which makes boundary edges;
3. calls `NotCleanReason(mesh, false)` on that rebuilt mesh (`:390`), which returns "not closed".
`MeshRepairMetrics.SelfIntersections` (`:387`) counts pairs that share a *position* but not a vertex *id* as intersecting.
That is every pair touching at a contact, plus the loose duplicates.

Evidence, for every case listed above:
- the row has `wclosed = true` (every welded edge used equally often in both directions), `contacts > 0`,
  `manifold_closed = false`, and `rounding_clean = true`; the RC4 rows are the exceptions, and the issues list them separately;
- re-run on master (#993 p0009, p0021; #994 p0012, p0021): the result has no boundary edges, no coincident triangles
  (so it is not a doubled coplanar interface, which was the guess in #994) and no welded degenerate triangles. The
  welded edges with more than two triangles equal `NonManifoldEdges` (101, 76, 8 for #993 p0009, #994 p0012, p0021);
- every pair `SelfIntersectionPairs` reports shares 1 or 2 positions (p0012: 1581 / 304 / 0 / 0 by 0 / 1 / 2 / 3
  shared positions), and the exact check of the rounding, which knows the contacts (`SharedPositionsTouch`), finds
  0 intersections;
- the same results with `SafeRounding = false` are identical (nothing was edited).

**Procedural repros** (measured on master: Success, closed, `NonManifoldEdges == 1`, `RoundingClean`, and the eval's rebuild
gives "not closed"):

```csharp
// prism over a ccw polygon, z in [z0, z1] (any triangulation of the caps)
DMesh3 A = Prism(new[] { (0.0, 0.0), (2, 0), (2, 1), (1, 1), (1, 2), (0, 2) }, 0, 1);   // L-shape
DMesh3 B = Prism(new[] { (0.0, 0.0), (1, 0), (1, 1), (2, 1), (2, 2), (0, 2) }, 1, 2);   // L-shape on top
// #994: face contact on z = 1 (the contact region is [0,1] x [0,2]); along y = 1, x in [1,2], A is below on the y < 1
// side and B above on the y > 1 side: the union touches itself along that segment
var u = new ExactMeshBoolean(A, B, ExactMeshBoolean.Operations.Union);
u.Compute();   // Success, 38 triangles, closed, NonManifoldEdges 1, Contacts 1, RoundingClean

// #993: A = box [0,4]x[0,2]x[2,4] united with box [2,3]x[2,4]x[1,4] (clean); B = A mirrored across x = 3
// (x' = 6 - x, orientation reversed back to outward); A - B = [0,2]x[0,2]x[2,4] and [2,3]x[2,4]x[1,4],
// which touch along the edge x = 2, y = 2, z in [2,4]
var d = new ExactMeshBoolean(A2, Mirror(A2, 3.0), ExactMeshBoolean.Operations.Difference);
d.Compute();   // Success, 28 triangles, closed, NonManifoldEdges 1, Contacts 2, RoundingClean
// the eval (weld by position, DMesh3 rebuild, NotCleanReason) says "not closed" for both
```

**Proposed fix (benchmark only, one PR).** In the eval, count the welded edges used by 2k > 2 triangles that are balanced
in direction (`edge_contacts`), and report "edge contacts" as the reason before `NotCleanReason` when the welded mesh is
winding-closed. Do not rebuild a `DMesh3` with loose triangles; run `NotCleanReason` on a rebuild that keeps the
duplicated vertices of the contact edges (as the method returned them, where the method's output has them). Count only
self-intersections beyond the shared positions. In `analyze.py`, rank "edge contacts" with "bowties" (expected for
touching solids, not judged). Re-run the reports of the `thingi-contact` and `abc-chained` classes from the stored rows
where possible. Otherwise the next runs pick it up.

**Risk:** low, benchmark code only. It changes the counts in `benchmarks/BooleanComparison/README.md`, and manifold and
CGAL rows get the same treatment. No library or exact code.

## RC3: "inside-out" from a floating-point volume sign of sub-ulp slivers

**Issues:** #1010 iud p0017 s3 difference (19 removed, `rounding_clean = true`, no self-intersections). The other
"inside-out" case, iud p0029, has `rounding_clean = false` and is RC4.

**Stage:** the oracle `NotCleanReason`, through `MeshPostRepairCleanup.FindInvertedComponents`
(`mesh_ops/MeshPostRepairCleanup.cs:315`, volume at `:333`; called at `MeshRobustRepair.cs:1698`). On the library side,
SafeRounding keeps a sub-ulp sliver when it is untouched.

**Mechanism.** C is A shifted by one ulp (`ulp-shift`), so `R - A` leaves closed slivers one ulp thick between A and C. The
rounding removes the ones it touched or that have degenerate triangles (by design). A sliver whose vertices are all exact
and whose triangles are not degenerate is not "bad" (`remove_tiny_components`, the `hasBad` test at
`MeshRoundingRepair.cs:2959`), so it stays. It is a valid, outward-oriented exact solid. `FindInvertedComponents` sums
its volume in doubles relative to the *box centre of the whole mesh*. For a component of volume 3e-21 at a distance of
about 0.5 from that centre, the cancellation error (about area · distance · eps = 2.5e-20) exceeds the volume, and the sign
comes out negative.

**Procedural repro** (measured on master: the result is 2 components, the 8-triangle sliver has float volume 3.16e-21
and an exact (BigInteger) volume sign of +1, `FindInvertedComponents` returns it, `NotCleanReason` says
"inside-out components", `RoundingClean` is true, 6 components were removed):

```csharp
// the abc-chained ucd chain on a procedural model; ContactTransforms is benchmarks/BooleanComparison/ContactTransforms.cs
DMesh3 m = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
foreach (int v in m.VertexIndices()) { var p = m.GetVertex(v); m.SetVertex(v, new Vector3d((float)p.x, (float)p.y, (float)p.z)); }
var c = ContactTransforms.BuildChain(new DMesh3(m), new DMesh3(m), new DMesh3(m),
    ContactVariant.PartialOverlap, 2, 1, 1, ContactVariant.UlpShift, 1, -1, 0);   // C = A shifted -1 ulp in y
// Union / Difference: new ExactMeshBoolean(x, y, op) with the defaults (SafeRounding on), Compute(), Result
DMesh3 r1 = Union(c.A, c.B), r2 = Difference(r1, c.C);                         // steps 1 and 2
var d = new ExactMeshBoolean(r2, c.A, ExactMeshBoolean.Operations.Difference);  // step 3, the difference
d.Compute();   // NotCleanReason(d.Result) == "inside-out components", d.RoundingClean == true
```

The same chain with C shifted along +y gives the same verdict (7 removed). With the shift along x or z, the step-3
difference was clean, but that is one probe (one sphere, one tessellation, 6 chains), not a property of the axes.

**Proposed fix (owner decision: `NotCleanReason` semantics).** In `FindInvertedComponents`, sum each component's volume
relative to one of its own vertices, and decide the sign exactly when |V| is below a forward error bound (an exact sum of
the per-triangle determinants, as `ExactMeshBoolean`'s own winding code does). A component of exactly zero volume is not
inside-out. A second, separate owner question for SafeRounding: should a closed component thinner than an ulp-scale bound
be removed even when untouched, for consistency with the slivers it does remove? Today the outcome depends on whether
rounding touched the sliver.

**Risk:** the oracle change can only remove false "inside-out" verdicts (true inversions have a large |V|). It touches
`NotCleanReason`, which is public and owner-reserved, and adds an error bound, so it needs the exact-code review of the
bound and the incremental mutation gate. The SafeRounding question changes outputs (owner-reserved).

## RC4: the rounding repair leaves defects (`RoundingClean == false`) on near-coincident surfaces

**Issues and cases:**

| issue | case | reason | placement of C | removed |
|---|---|---|---|---|
| #1011 | iud p0005 s3 union / intersection | not closed / degenerate | ulp-shift (C = A + 1 ulp) | 0 |
| #1011 | iud p0021 s3 union / intersection | not closed / degenerate | ulp-shift | 0 |
| #1011 | iud p0022 s3 difference | not closed | mirror of A | 0 |
| #1010 | iud p0029 s3 difference | inside-out | ulp-shift | 115 |
| #1010 | iud p0020 s3 difference | not closed | face-contact | 3 |
| #1010 | dui p0029 s3 difference | self-intersections | partial-overlap | 24 |
| #1010 | ucd p0007 s3 difference | not closed | partial-overlap | 66 |
| #993 | p0011 difference | not closed | (mirror pair) | 36 |

**Stage:** SafeRounding: the repair stops with defects left. All of them are winding-closed. Their initial rounding
defects are large: iud p0005 union has 17788 intersecting pairs and 129688 degenerate triangles at the start of the repair,
from 266k pieces, and 5314 false contacts; iud p0021 is similar. This is the documented behaviour (`Compute()` succeeds
with `RoundingClean == false`; `StrictRounding` turns it into a failure), so it is not a guarantee violation. But the
defects are real. Counted per operation result (a step has three, and two of the same step can both fail), they are 9 of
the 39 non-clean `ours` results with clean inputs: #1011 has iud p0005 s3 (union and intersection, 2), iud p0021 s3
(union and intersection, 2) and iud p0022 s3 (difference, 1), which is 5; #1010 has dui p0029, iud p0020, iud p0029 and
ucd p0007 (each step 3, difference), which is 4; 5 + 4 = 9. The 39 come from 36 rows (chain steps). Their "not closed" label is again
the RC2 eval: it rests on the contacts these results also have, which here include contacts the rounding made.

**Not diagnosed further, and why.** The rows hold only the initial counts (`rounding_intersections`,
`rounding_degenerate`, `rounding_false_contacts`), not the remaining ones or why the repair stopped
(`EvaluationLimitReached` at `RoundingMaxPairTests` = 5M, an edit rejected by the bound: `SmallestRejectedDrift`,
`ScaleDisparity`). Telling "budget" from "bound" from "no edit exists" needs those fields. The models are ABC files (runner
only), and the chains reach pair 29, so a rerun is the full 30-chain job of `abc-chained.yml` (up to 240 minutes). I did
not start it for a diagnosis-only task.

Procedurally, 54 chains gave no `RoundingClean == false` case. The probe was a scratch program, not committed; the
parameters are given here so it can be rebuilt. Each model is snapped to float32 per coordinate:
- shapes: `Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 12 }`;
  `CappedCylinderGenerator { BaseRadius = 1, TopRadius = 0.7f, Height = 2, Slices = 24 }`; and
  `GridBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.4)), EdgeVertices = 4 }`, rotated by 17°
  about (1, 2, 3) normalized around the origin before the snap;
- placements: `ContactTransforms.BuildChain(m, m, m, PartialOverlap, (axC + 1) % 3, 1, 1, UlpShift, axC, sgC, 0)` for
  axC in {0, 1, 2} and sgC in {+1, -1};
- chains: the `PLANS` of `data/abc_chained.py` (ucd, dui, iud), with all three operations computed at each step on
  default settings and the chain continuing with the plan's operation.

That is 3 shapes × 6 placements × 3 chains = 54. The CAD models' many large coplanar faces next to a 1-ulp copy seem to
be needed. That is a guess, not a measured result.

**Proposed next step (first commit of the RC4 PR, not a fix yet):** add `rounding_remaining_intersections`,
`rounding_remaining_degenerate`, `rounding_remaining_false_contacts`, `rounding_evaluation_limit`,
`rounding_smallest_rejected_drift` and `rounding_max_drift` to the rows (`benchmarks/BooleanComparison/Program.cs`), and a
`chains` filter input to `abc-chained.yml` (for example `iud:0005,iud:0021`) for a run of a few minutes. Then decide:
budget (raise or scale `RoundingMaxPairTests` with the pieces), bound (near-coincident surfaces need moves larger than
the ulp term), or no local edit (a candidate for removing the 1-ulp slab pairs as RC1/RC3's thin components). **Risk / gate:**
unknown until then. A change to `MeshRoundingRepair`'s bounds is reviewed like an error bound. The exact code is not
involved: the arrangement and classification of these cases are not suspected, and all results are winding-closed.

## Not found

No case points at the arrangement, the classification or the assembly. Every listed result is winding-closed. Where
re-run (#993 p0009 / p0021, #994 p0012 / p0021), the results with and without `SafeRounding` are identical. In the
abc-chained rows the volume identities hold to 8.5e-10 or better wherever the rounding removed nothing.

## Comparison with Manifold's conventions (coordinator request, 2026-11-17)

Manifold (Apache-2.0; wiki "Manifold-Library", section "Mesh Boolean") was used as a design reference only; no code was
read or taken from it.

**1. Touching and coplanar cases.** Manifold breaks exact ties by symbolic perturbation: the first operand is pushed
outward for a union and inward for a difference or intersection. As a result, touching solids merge in a union, an
equal-height difference gives a through-hole, and A - A is empty. Our boolean computes the exact *regularized* result
instead (the closure of the interior). On the cases where the two definitions agree, it gives the same answers
(measured on master with boxes):

| case | expected | ours |
|---|---|---|
| A - A | empty | empty |
| A ∪ A | A | A (12 triangles, V 8) |
| two cubes touching in a face, union | one box, V 16 | one closed component, V 16, no contacts |
| a cube on part of a face, union | one solid, V 10 | one closed component, V 10 |
| two cubes touching in a face, intersection | empty | empty |
| equal-height difference (box minus a box of the same height) | through-hole, V 6, genus 1 | closed, V 6, Euler characteristic 0 |
| flush pocket (B's top coplanar with A's top) | V 7 | closed, V 7, clean |

The one difference is an **edge or vertex contact**. There the regularized solid really is pinched, for example two
cubes touching along an edge, or the RC2 repros. We return the pinch exactly: one non-manifold edge, split into two
components with duplicated vertices. Manifold's perturbation would decide it by operation: merge across a zero-width
bridge in a union, or keep the pieces apart.

Per issue:
- **#985:** no coplanar classification is involved. With `SafeRounding = false` all six families give the correct
  closed union. The bug is in the rounding (RC1).
- **#993:** the exact regularized A - mirror(A) has edge pinches on and near the mirror plane. We return them split,
  with no coincident or doubled faces, and the volume identities hold where nothing was removed. That is no deviation in
  the coplanar classification; only the representation of the pinch differs from what perturbation would produce.
- **#994:** the shared face is removed (no coincident triangles), as both conventions require. The remaining
  non-manifold edges are true pinches at the edge of the contact region (RC2). The coplanar patches are classified
  correctly.

**2. Pinches versus holes.** Classification of every non-clean `ours` result, from the stored rows (abc-chained: clean
inputs only):

| run | edge pinch ("not closed" by the eval) | vertex pinch ("bowties") | rounding not clean (RC4) | inside-out (RC3) | real hole (not winding-closed) |
|---|---|---|---|---|---|
| abc-chained, 39 results | 15 | 14 | 9 | 1 | **0** |
| thingi-contact, all 5 variants, 29 results | 10 | 16 | 3 | 0 | **0** |

None of the failures is a hole. Every result is winding-closed.

Would splitting pinch vertices and edges make them clean? Our output already splits them: `Contacts` counts the
positions with duplicated vertices, and non-manifold edges are split into manifold pairs, as in Manifold. They are
reported as not clean for two reasons, both outside the library's result:
- the eval welds them back by position (RC2);
- on the unwelded output, `NotCleanReason`'s self-intersection test counts triangles that touch at a duplicated
  position, because it compares vertex ids and not positions (`MeshRepairMetrics.SelfIntersections`).

  This was measured on the unwelded boolean output of the two RC2 procedural repros (master). The eval itself calls
  `NotCleanReason(mesh, false)`, which skips the self-intersection test, so the eval's verdict alone does not show this.

  | repro | `NotCleanReason(r, true)` | `NotCleanReason(r, false)` | `SelfIntersections` | pairs sharing 0 / 1 / 2 / 3 positions |
  |---|---|---|---|---|
  | L-prisms union | "self-intersections" | "" | 16 | 0 / 12 / 4 / 0 |
  | A2 − mirror(A2, x = 3) | "self-intersections" | "" | 36 | 0 / 32 / 4 / 0 |

  Every reported pair shares a position with its partner, and the rounding's contact-aware check finds none. So
  the split pinch output is clean by every check of `NotCleanReason` except the self-intersection test. That test is
  what makes it not clean, and it is the open owner question below.

The rounding's exact check knows the contacts (`SharedPositionsTouch`), and by that check the 29 pinch results of
abc-chained and the 26 of thingi-contact are clean. That is 29 of 39 and 26 of 29 non-clean results. Whether
`NotCleanReason` should accept such contacts is an owner decision, and this triage leaves it unchanged. The benchmark eval
can report them as "edge contacts" / "bowties" without that change (RC2's fix).
