# SolidBooleanBenchmark

`SolidBooleanPipeline` (mesh_ops/SolidBooleanPipeline.cs) on procedural triangle soups (no mesh files): every input is an
unwelded copy of a box, sphere, torus or cylinder (three vertices per triangle, like an STL read). Per case: the triangle
counts after the exact boolean and after the reduction to a fraction of the boolean result, the time per stage, the contacts,
the verdict of `MeshRobustRepair.NotCleanReason` (`""` is clean; it is not `null`), the stop reason, and, for the cost,
`MeshRobustRepair.Apply()` on the same soup (the prophylactic route; it does a union-like resolve and reduces nothing, so for
an intersection or a difference only its time is comparable).

```
dotnet run -c Release --project benchmarks/SolidBooleanBenchmark [fractions]   # the table below (default 0.25,0.1)
dotnet run -c Release --project benchmarks/SolidBooleanBenchmark -- repeated   # a chain of booleans
dotnet run -c Release --project benchmarks/SolidBooleanBenchmark -- open       # open input and the three repair modes
dotnet run -c Release --project benchmarks/SolidBooleanBenchmark -- limit      # what contacts do to a reduction
dotnet run -c Release --project benchmarks/SolidBooleanBenchmark -- plain      # the Reducer alone, MinCollapseTriangleQuality 0 / 0.02
dotnet run -c Release --project benchmarks/SolidBooleanBenchmark -- pair       # PairContacts off and on, the touching cases
```

Linux container, 4 cores, `net10.0`, Release, one process, no warm-up (the first row includes the JIT: the 361 ms of the
first boolean of 24 triangles are start-up). The timings vary by 10-20 % between runs.
Run 2026-11-17, `SolidBooleanPipeline` with its defaults (`MinTriangleQuality = 0.02`, `PreventSelfIntersections`, no
`MaxDeviation`); the reduction target is the fraction of the boolean result (`TargetFraction`).

## Result (2026-11-17)

17 cases (overlapping, touching, tangent and linked pairs; union, intersection, difference; three inputs), each reduced to
25 % and 10 % of the boolean result:

| case | in tris | boolean tris | boolean ms | contacts | 25 %: tris, reduce ms, contacts, result, stop | 10 %: tris, reduce ms, contacts, result, stop | RobustRepair of the soup: ms, tris, result |
|---|---|---|---|---|---|---|---|
| box+box overlap U | 24 | 36 | 361 | 0 | 8 (target 9), 44, 0, clean, TargetReached | 4 (target 4), 1, 0, clean, TargetReached | 122, 36, clean |
| box+box face-touch U | 24 | 20 | 21 | 0 | 4 (target 5), 0, 0, clean, TargetReached | 4 (target 4), 0, 0, clean, TargetReached | 35, 20, clean |
| box+box edge-touch U | 24 | 24 | 6 | 2 | 24 (target 6), 4, 2, self-intersections, SelfIntersectionGuard | 24 (target 4), 0, 2, self-intersections, SelfIntersectionGuard | 25, 24, clean |
| sphere+sphere overlap U | 12696 | 10942 | 166 | 0 | 2736 (target 2736), 339, 0, clean, TargetReached | 1094 (target 1095), 268, 0, clean, TargetReached | 1163, 10942, clean |
| sphere&sphere overlap I | 12696 | 3254 | 61 | 0 | 814 (target 814), 22, 0, clean, TargetReached | 326 (target 326), 35, 0, clean, TargetReached | 454, 10942, clean |
| sphere-sphere D | 12696 | 8602 | 31 | 0 | 2150 (target 2151), 64, 0, clean, TargetReached | 860 (target 861), 76, 0, clean, TargetReached | 389, 10574, clean |
| sphere+sphere tangent-ish U | 5400 | 5400 | 15 | 0 | 1350 (target 1350), 36, 0, clean, TargetReached | 540 (target 540), 44, 0, clean, TargetReached | 90, 5400, clean |
| box-cylinder D | 204 | 208 | 5 | 0 | 52 (target 52), 2, 0, clean, TargetReached | 20 (target 21), 2, 0, clean, TargetReached | 47, 400, clean |
| torus+cylinder U | 3264 | 3508 | 34 | 0 | 876 (target 877), 27, 0, clean, TargetReached | 350 (target 351), 32, 0, clean, TargetReached | 75, 3508, clean |
| torus+torus linked U | 6144 | 6048 | 86 | 0 | 1512 (target 1512), 52, 0, clean, TargetReached | 604 (target 605), 61, 0, clean, TargetReached | 199, 6048, clean |
| fine box edge-touch U | 5400 | 5400 | 46 | 16 | 1350 (target 1350), 767, 16, self-intersections, TargetReached | 540 (target 540), 887, 16, self-intersections, TargetReached | 211, 5408, clean |
| fine box vertex-touch U | 5400 | 5400 | 19 | 1 | 1350 (target 1350), 205, 1, self-intersections, TargetReached | 540 (target 540), 277, 1, self-intersections, TargetReached | 108, 5400, clean |
| sphere on box face (pole touch) U | 9612 | 9614 | 37 | 1 | 2404 (target 2404), 236, 0, clean, TargetReached | 962 (target 962), 303, 0, clean, TargetReached | 167, 9614, clean |
| two cylinders line-touch U | 512 | 512 | 2 | 0 | 128 (target 128), 7, 0, clean, TargetReached | 52 (target 52), 7, 0, clean, TargetReached | 35, 512, clean |
| cylinder on box, partial face-touch U | 2956 | 2866 | 35 | 0 | 716 (target 717), 93, 0, clean, TargetReached | 286 (target 287), 117, 0, clean, TargetReached | 95, 2866, clean |
| 3 spheres U | 12996 | 9160 | 68 | 0 | 2290 (target 2290), 73, 0, clean, TargetReached | 916 (target 916), 75, 0, clean, TargetReached | 214, 9160, clean |
| box+sphere+cyl U | 6488 | 5972 | 30 | 0 | 1492 (target 1493), 40, 0, clean, TargetReached | 598 (target 598), 56, 0, clean, TargetReached | 124, 5972, clean |

(`target N` is the count asked for; the result is at most N except where the stop reason says otherwise. Results with a
count slightly below N: the last collapse of a pair removes two triangles.)

- **Share of `NotCleanReason == ""`: 26 of 26** reduced results whose boolean result has no contacts (the 13 cases without
  contacts at two targets), **28 of 34** over all of them. The six that are not clean are the boxes and the fine boxes that
  touch along an edge or at a vertex: they keep their contacts (duplicated vertices of one position, a valid manifold
  result), and `NotCleanReason` reports every touching point as "self-intersections". The pole touch of a sphere on a box
  face gave clean results because the reduction moved the touching vertex off the face (`ContactsResult` 0). The reduction
  never created a contact: `ContactsResult <= ContactsBoolean` in every row (tested).
- **Boolean result and reduction.** The boolean takes 2-170 ms on these inputs (4 cores, the first row aside); the reduction to
  25 % takes 2-340 ms (the `Reducer` documents 2-3 times its plain time for the exact self-intersection check; not
  re-measured here). The exceptions are the fine boxes that touch along an edge (770-890 ms for 5400 triangles) and, less so,
  at a vertex (205-277 ms): flat regions of coplanar triangles, where the exact predicates of the pairs take the time.
- **Against `MeshRobustRepair` of the soup** (the route the pipeline avoids): 25-1160 ms. It is faster than the boolean plus
  the reduction on the fine touching boxes (211 against 813 ms, 108 against 224 ms) and slower on the large smooth cases
  (sphere+sphere 1163 against 505 ms, 454 against 83 ms for the intersection); it does not reduce. The comparison is of cost only: it resolves the soup (a union), gives the same number of triangles
  as the boolean, and the reduction is the pipeline's extra.
- **No prophylactic repair.** All 17 inputs are closed in the winding number sense (soups included), so no input was
  repaired. An open input is repaired (below) only when it needs it.

## What the reduction needs: `MinTriangleQuality` (the `plain` mode)

The `Reducer` alone leaves zero-area triangles on the creases of flat-faced solids ("degenerate triangles" in
`NotCleanReason`, also with `PreventSelfIntersections`). `MinCollapseTriangleQuality = 0.02` (the pipeline's default) removes
that at the same triangle count:

```
plain fine box 16 quality=0 f=0.25: 674 of 2700 tris, degenerate triangles
plain fine box 16 quality=0 f=0.1: 270 of 2700 tris, degenerate triangles
plain fine box 16 quality=0.02 f=0.25: 674 of 2700 tris, clean
plain fine box 16 quality=0.02 f=0.1: 270 of 2700 tris, clean
plain sphere 25 quality=0 f=0.25: 1728 of 6912 tris, clean
plain sphere 25 quality=0 f=0.1: 690 of 6912 tris, clean
plain sphere 25 quality=0.02 f=0.25: 1728 of 6912 tris, clean
plain sphere 25 quality=0.02 f=0.1: 690 of 6912 tris, clean
```

## Contacts limit how far a reduction goes (the `limit` mode)

Where solids touch, the result has two vertices at one position. The `Reducer` treats them as different vertices and its
self-intersection check (exact, shared vertices by id) counts the sheet of the twin as a triangle that the new triangles must
not touch, so a collapse that keeps the surface on the contact is refused. A contact vertex can move off the contact (the
pole touch above), a contact line cannot be collapsed. This blocks a reduction only far below the targets above (two boxes
asked for 4 triangles):

| boxes (EdgeVertices) | placement | boolean tris | result tris (target 4) | contacts left | stop reason | blocked edges: guard / quality, at contacts |
|---|---|---|---|---|---|---|
| 4 | apart | 216 | 8 | 0 | Exhausted | 12 / 0, 0 |
| 4 | vertex touch | 216 | 18 | 1 | SelfIntersectionGuard | 18 / 9, 9 |
| 4 | edge touch | 216 | 48 | 4 | SelfIntersectionGuard | 53 / 19, 36 |
| 8 | apart | 1176 | 8 | 0 | Exhausted | 12 / 0, 0 |
| 8 | vertex touch | 1176 | 18 | 1 | SelfIntersectionGuard | 19 / 8, 9 |
| 8 | edge touch | 1176 | 96 | 8 | Contacts | 115 / 29, 76 |
| 12 | apart | 2904 | 8 | 0 | Exhausted | 12 / 0, 0 |
| 12 | vertex touch | 2904 | 18 | 1 | SelfIntersectionGuard | 19 / 8, 9 |
| 12 | edge touch | 2904 | 144 | 12 | Contacts | 179 / 37, 116 |

A contact line of m positions holds about 12 m triangles (4, 8, 12 positions: 48, 96, 144), a contact point 18 in all.
That is 8 % of 1176 and 5 % of 2904, so a 10 % target is reached (the fine box rows above); a contact line of m vertices
blocks a target below about 12 m triangles, whatever the input size. The pipeline does not freeze or move contact vertices
(that would change the result's guarantees); it reports the stop (`StopReason.Contacts`, `BlockedAtContacts`,
`StopDetail`). Options if a caller needs more: `PairContacts` (below: the two twin edges of a contact line collapse together, built), or separate the touching sheets by a tiny gap before the
reduction (changes the exact result's semantics: solids that touch no longer do).

## Paired collapse of contact edges (`PairContacts`, the `pair` mode)

`PairContacts = true` (default off; the result is bit for bit the old one when off, hash tests) collapses each edge of a contact
line together with its twin edge (`Reducer.PairCoincidentVertices`): both sheets move to the same new position, so the contact
stays exactly coincident (no gap, no crossing). The guards run for both sheets (the exact self-intersection check with the twins'
shared position counted as a shared vertex, `MaxDeviation`, fold-over, `MinTriangleQuality`). A contact vertex without a twin
edge (a contact point) is only a collapse target, never moved. Same machine and settings as above, run 2026-11-18.

**Upper bound** (before the implementation: what the boolean result offers a paired collapse; `limit` mode, second part):

```
Edges at contacts in the boolean result (the upper bound of what a paired collapse can take): vertices at contacts, edges with an end at one, of those the paired ones (both ends with one twin, twin edge exists), contact positions with 3 or more vertices
box+box face-touch U: boolean 20 tris, 0 vertices at contacts, 0 edges at them, 0 paired, 0 positions with 3+ vertices
box+box edge-touch U: boolean 24 tris, 4 vertices at contacts, 16 edges at them, 2 paired, 0 positions with 3+ vertices
sphere+sphere tangent-ish U: boolean 5400 tris, 0 vertices at contacts, 0 edges at them, 0 paired, 0 positions with 3+ vertices
fine box edge-touch U: boolean 5400 tris, 32 vertices at contacts, 156 edges at them, 30 paired, 0 positions with 3+ vertices
fine box vertex-touch U: boolean 5400 tris, 2 vertices at contacts, 9 edges at them, 0 paired, 0 positions with 3+ vertices
sphere on box face (pole touch) U: boolean 9614 tris, 2 vertices at contacts, 10 edges at them, 0 paired, 0 positions with 3+ vertices
two cylinders line-touch U: boolean 512 tris, 0 vertices at contacts, 0 edges at them, 0 paired, 0 positions with 3+ vertices
cylinder on box, partial face-touch U: boolean 2866 tris, 0 vertices at contacts, 0 edges at them, 0 paired, 0 positions with 3+ vertices
```

Only contact lines have paired edges (fine box edge touch: 16 positions, 30 paired edges = 15 segments x 2 sheets); a contact
point has none, so a pair cannot help there.

**Before / after, the contacts limit** (two boxes asked for 4 triangles; "off" is the table of the `limit` section above):

```
| boxes (EdgeVertices) | placement | boolean tris | result tris (target 4) | contacts left | stop reason | blocked edges: guard / quality, at contacts | with PairContacts: result tris, contacts left, stop reason, paired collapses, result |
|---|---|---|---|---|---|---|---|
| 4 | apart | 216 | 8 | 0 | Exhausted | 12 / 0, 0 | 8, 0, Exhausted, 0, clean |
| 4 | vertex touch | 216 | 18 | 1 | SelfIntersectionGuard | 18 / 9, 9 | 8, 1, Exhausted, 0, self-intersections |
| 4 | edge touch | 216 | 48 | 4 | SelfIntersectionGuard | 53 / 19, 36 | 8, 2, Exhausted, 2, self-intersections |
| 8 | apart | 1176 | 8 | 0 | Exhausted | 12 / 0, 0 | 8, 0, Exhausted, 0, clean |
| 8 | vertex touch | 1176 | 18 | 1 | SelfIntersectionGuard | 19 / 8, 9 | 8, 1, Exhausted, 0, self-intersections |
| 8 | edge touch | 1176 | 96 | 8 | Contacts | 115 / 29, 76 | 8, 2, Exhausted, 6, self-intersections |
| 12 | apart | 2904 | 8 | 0 | Exhausted | 12 / 0, 0 | 8, 0, Exhausted, 0, clean |
| 12 | vertex touch | 2904 | 18 | 1 | SelfIntersectionGuard | 19 / 8, 9 | 8, 1, Exhausted, 0, self-intersections |
| 12 | edge touch | 2904 | 144 | 12 | Contacts | 179 / 37, 116 | 8, 2, Exhausted, 10, self-intersections |
```

(last column: result triangles, contacts left, stop reason, paired collapses, `NotCleanReason`). An edge-touching pair now reaches
8 triangles (one tetrahedron per solid, `Exhausted`) instead of 48 / 96 / 144; the two ends of the contact line remain contacts.

**The touching cases of the 17-case table** (`pair` mode; cases without contacts give the same result, checked by equality of the
triangle counts and by a hash test):

```
| case | boolean tris | contacts | target | tris off | contacts off | result off | stop off | ms off | tris PairContacts | contacts | result | stop | paired collapses | ms | closed |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| box+box face-touch U | 20 | 0 | (no contacts: the same result, 4 = 4 tris: True) |
| box+box edge-touch U | 24 | 2 | 6 | 24 | 2 | self-intersections | SelfIntersectionGuard | 6 | 8 | 2 | self-intersections | Exhausted | 0 | 3 | True |
| box+box edge-touch U | 24 | 2 | 4 | 24 | 2 | self-intersections | SelfIntersectionGuard | 0 | 8 | 2 | self-intersections | Exhausted | 0 | 0 | True |
| sphere+sphere tangent-ish U | 5400 | 0 | (no contacts: the same result, 1350 = 1350 tris: True) |
| fine box edge-touch U | 5400 | 16 | 1350 | 1350 | 16 | self-intersections | TargetReached | 983 | 1350 | 16 | self-intersections | TargetReached | 0 | 376 | True |
| fine box edge-touch U | 5400 | 16 | 540 | 540 | 16 | self-intersections | TargetReached | 876 | 540 | 5 | self-intersections | TargetReached | 11 | 418 | True |
| fine box vertex-touch U | 5400 | 1 | 1350 | 1350 | 1 | self-intersections | TargetReached | 184 | 1350 | 1 | self-intersections | TargetReached | 0 | 236 | True |
| fine box vertex-touch U | 5400 | 1 | 540 | 540 | 1 | self-intersections | TargetReached | 273 | 540 | 1 | self-intersections | TargetReached | 0 | 311 | True |
| sphere on box face (pole touch) U | 9614 | 1 | 2404 | 2404 | 0 | clean | TargetReached | 214 | 2404 | 1 | self-intersections | TargetReached | 0 | 279 | True |
| sphere on box face (pole touch) U | 9614 | 1 | 962 | 962 | 0 | clean | TargetReached | 275 | 962 | 1 | self-intersections | TargetReached | 0 | 323 | True |
| two cylinders line-touch U | 512 | 0 | (no contacts: the same result, 128 = 128 tris: True) |
| cylinder on box, partial face-touch U | 2866 | 0 | (no contacts: the same result, 716 = 716 tris: True) |
```

- The fine edge-touching boxes at 10 %: the contacts go from 16 to 5 at the same 540 triangles (paired collapses: see the table); at 25 % the
  target is reached without touching the contact line, so nothing changes.
- **`NotCleanReason` is not changed**: every result that keeps a contact (`contacts` > 0) is still reported as "self-intersections"
  at it, also when the sheets touch exactly; the check is not altered. All results are closed and have no crossing (tests check it
  with an exact pairwise test where vertices of one position count as one).
- **A contact point is kept.** In the pole-touch case (a sphere touching a box face) the off result is clean because the
  reduction moved the touching vertex off the face (the contact was removed, a gap appears); with `PairContacts` the contact vertex
  stays, so the result keeps one contact and `NotCleanReason` says "self-intersections". That is why the option stays opt-in
  (the pipeline's output contract is "NotCleanReason the same or better" only without contacts).
- Time of the reduction: within the run-to-run variation, lower where the contact line is collapsed (see the ms columns).
- Not combined with `MaxDeviationTwoSided` (`InvalidInput` in the pipeline, `InvalidOperationException` in the `Reducer`).

## Repeated booleans (the `repeated` mode)

The result of each step is the next step's input, united with a shifted sphere soup (4332 triangles), reduced to 50 %
each time:

```
| step | input tris | boolean tris | boolean ms | reduced tris (50 %) | reduce ms | result |
| 1 | 4332 + 4332 | 6952 | 459 | 3476 | 219 | clean, TargetReached |
| 2 | 3476 + 4332 | 6540 | 81 | 3270 | 140 | clean, TargetReached |
| 3 | 3270 + 4332 | 6456 | 80 | 3228 | 138 | clean, TargetReached |
| 4 | 3228 + 4332 | 6498 | 101 | 3248 | 123 | clean, TargetReached |
| 5 | 3248 + 4332 | 6588 | 80 | 3294 | 85 | clean, TargetReached |
| 6 | 3294 + 4332 | 6654 | 75 | 3326 | 87 | clean, TargetReached |
```

Without the reduction the same chain grows from 6952 to 20052 triangles in six steps (every step adds the sphere's 4332
minus what it covers); with it the count stays at 3.2-3.5k.

## Open input (the `open` mode)

A sphere with three triangles removed, united with a closed sphere soup, reduced to 25 %:

```
Refuse: OpenInput an input is not closed (InputRepair is Refuse): input meshes must be closed (every edge used equally often in both directions) | repaired:  | tris 0 -> 0, clean, total 60 ms
AutoRepair: Success  | repaired: input 0 by MeshAutoRepair 4329->4332 in 186 ms | tris 7380 -> 1844, clean, total 997 ms
AutoRepairThenRobust: Success  | repaired: input 0 by MeshAutoRepair 4329->4332 in 56 ms | tris 7380 -> 1844, clean, total 444 ms
```

(`AutoRepairThenRobust` uses `MeshRobustRepair` only if `MeshAutoRepair` does not close the input; the tests have an input
for that, one triangle. The first row's 186 ms include the JIT.)
