# 2D meshing: the failure catalogue

The F cases of [MESHING2D_SPEC.md](MESHING2D_SPEC.md), section 2, as procedural fixtures, one state per row. Rows now: 16 covered, 99 pending, 8 deferred by the owner's scope, 3 blocked.
The table is generated from `benchmarks/Meshing2Benchmark/Oracle/Catalogue.cs` by `dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- catalogue`; a test keeps it in sync.

- **covered**: runs now through the oracle (or the named test), on the M1 kernel of PR #200 until the production mesher replaces it.
- **pending**: the fixture exists and its input is checked now, including its area against the specification's value. `Meshing2CatalogueTests.Row_IsInItsState`
  fails as soon as the row's feature is registered in `Candidates/CandidateRegistry.cs` while the row still says pending, and another test fails when the production type
  appears without its feature. So nothing stays silently skipped.
- **deferred**: out of the first version by a numbered decision of the owner (section 5 of the specification).
- **blocked**: cannot run here, for the stated reason.

Inputs are integer or dyadic, without trigonometry, so many fixtures are dyadic analogs of the spec's decimal values; the note says where the difference matters. Rows marked
as beyond this brief (`m4.*`) wait for grid, wedge and Steiner post-processing, which #200's stage list calls M4 and nobody has approved yet.

| Case | Title | State | Waits for or run by | Note |
|---|---|---|---|---|
| F-1/a | Three crossing segments | pending | m2.noding |  |
| F-1/b | Three crossing segments | pending | m2.noding |  |
| F-1/c | Three crossing segments | pending | m2.noding |  |
| F-1/random | Random crossing segments, one seed of many | pending | m2.noding |  |
| F-2 | Five crossing segments, formerly a stack overflow | pending | m2.noding |  |
| F-3/a | Crossing segments, exception | pending | m2.noding |  |
| F-3/b | Near-duplicate point (14th digit) on a segment, hull domain | pending | m2.noding | dyadic analog of the spec's coordinates |
| F-3/c | The same at 20 degrees | pending | m3.refine |  |
| F-4/a | 40 random segments at 25 degrees | pending | m3.refine | the spec runs 20 seeds; the benchmark sweeps them |
| F-4/b | 100 segments in a 20 x 20 square at 20 degrees | pending | m3.refine |  |
| F-4/c | 200 segments on a 50-grid at 20 degrees | pending | m3.refine |  |
| F-5 | 40 random segments at 30 degrees | pending | m3.refine | the spec's seeds belong to another generator: this seed does not reproduce them |
| F-6/a | Segment through input points | pending | m2.cdt |  |
| F-6/b | Two diagonals and a point just off them, 20 degrees | pending | m3.refine | epsilon is a power of two here |
| F-6/c | Two diagonals and a point just off them, 20 degrees | pending | m3.refine | epsilon is a power of two here |
| F-6/d | Two diagonals and a point just off them, 20 degrees | pending | m3.refine | epsilon is a power of two here |
| F-7/a | Overlapping collinear segments | pending | m2.noding |  |
| F-7/b | A contour running back along itself | pending | m2.noding |  |
| F-7/c | A spike | pending | m2.noding |  |
| F-7/d | Duplicate and reversed duplicate segments | pending | m2.cdt |  |
| F-7/e | Bowtie contour | pending | m2.noding |  |
| F-7/f | All of F-7 at 20 degrees | pending | m3.refine |  |
| F-8 | Zero-length segments mixed with crossings | pending | m2.noding |  |
| F-9 | Noding off must fail with a status | pending | m2.noding |  |
| F-10/a | Vertex nearly on a segment, 20 degrees | pending | m2.snap |  |
| F-10/b | Vertex nearly on a segment, 20 degrees | pending | m2.snap |  |
| F-10/c | Vertex nearly on a segment, 20 degrees | pending | m2.snap |  |
| F-11/a | Nearly duplicate vertices, 20 degrees | pending | m2.snap |  |
| F-11/b | Nearly duplicate vertices, 20 degrees | pending | m2.snap |  |
| F-11/c | Nearly duplicate vertices, 20 degrees | pending | m2.snap |  |
| F-11/d | Nearly duplicate vertices, 20 degrees | pending | m2.snap |  |
| F-12/a | Double-digitised border, 2^-40 | pending | m2.snap | the wavy border is a fixed table, not sin(i) |
| F-12/b | Double-digitised border, 2^-20: the limit stops it | pending | m2.snap |  |
| F-13 | Micro-rings with coordinate noise | pending | m2.snap |  |
| F-14/a | 40 stacked nearly identical squares | pending | m2.snap |  |
| F-14/b | 40 stacked nearly identical squares | pending | m2.snap |  |
| F-14/c | 40 stacked nearly identical squares | pending | m2.snap |  |
| F-14/d | 40 stacked nearly identical squares | pending | m2.snap |  |
| F-14/e | 40 stacked nearly identical squares | pending | m2.snap |  |
| F-15 | Snap cycle, tolerance 11/1024 | pending | m2.snap | shifted from the origin into a frame of area 4; dyadic analog |
| F-16 | Near-collinear pair with a third segment close to the shared end | pending | m2.snap | dyadic analog at UTM scale |
| F-17 | Snap tolerance larger than a hole | pending | m2.snap |  |
| F-18/a | Building wall 4 mm along a parcel border, tolerance merges it | pending | m2.snap | dyadic analog: 2^-8 instead of 5 mm |
| F-18/b | Wall 62 mm away is kept | pending | m2.snap |  |
| F-19 | Thin wedges, wedge tolerance | pending | m4.wedge | O13: beyond this brief; the fixture is the first of the spec's five |
| F-20 | Long edges cross a dense polyline at tiny angles, procedural analog | pending | m2.snap | the public input CDT issue-211 is not fetched here; this is an analog built from its description |
| F-21 | Long parallel stripes: scaling check | pending | m2.noding | the time target needs 20 000 stripes, run by the benchmark, not in the oracle's quadratic segment check |
| F-22/a | Back-and-forth border, no snapping: limit stops it | pending | m3.refine |  |
| F-22/b | The same with tolerance 2^-29: sliver gone | pending | m2.snap |  |
| F-23/angle-0 | Max area 1/2, min angle 0 | pending | m3.refine |  |
| F-23/angle-20 | Max area 1/2, min angle 20 | pending | m3.refine |  |
| F-23/angle-30 | Max area 1/2, min angle 30 | pending | m3.refine |  |
| F-24/a | Region area limits on two sides of a diagonal | pending | m2.regions |  |
| F-24/b | Inner square as a second region | pending | m2.regions |  |
| F-25/a | No segment splitting: rectangle 10 x 20 | pending | m3.locks |  |
| F-25/b | No segment splitting: rectangle 10 x 30 | pending | m3.locks |  |
| F-26 | Precision limit at needle regions | pending | m3.refine |  |
| F-27 | Needle refinement scaling (performance row) | pending | m3.refine | time target: over 50 000 vertices in about 2 s; the time is measured by the benchmark |
| F-28 | A max angle alone is ignored | deferred |  | Q3: no maximum angle bound in scope |
| F-29 | Refining again with a max angle crashes | deferred |  | Q3: no maximum angle bound in scope |
| F-30/a | Invalid minimum angle next to a max area | pending | m3.refine | O3: the invalid value must not disable the area limit. Reject or ignore it: decision D12 |
| F-30/b | Invalid maximum angle | deferred |  | Q3: no maximum angle bound in scope |
| F-31/a | Min angle and area with locked outer boundary | pending | m3.locks |  |
| F-31/b | The same with a max angle | deferred |  | Q3: no maximum angle bound in scope |
| F-32 | Extreme edge lengths in refinement | pending | m3.refine |  |
| F-33 | Per-triangle area limits on a point-set mesh | pending | m3.refine-again | I8. The input to refine again is the mesh of this point set; session 2 adds the area map to the fixture when the API exists |
| F-34 | Area-only refinement (performance row) | pending | m3.refine | time target about 14 ms; the time is measured by the benchmark |
| F-35 | Refining a point-set mesh keeps the hull | pending | m3.refine |  |
| F-36 | Crossing locked breaklines: no vertex outside | pending | m3.locks |  |
| F-37 | Parallel breaklines 2^-13 apart must terminate | pending | m3.refine |  |
| F-38/a | Triangle with a small input angle, 30 degrees | pending | m3.refine | the spec asks for termination and validity here |
| F-38/b | Triangle with a small input angle, 30 degrees | pending | m3.refine | the spec asks for termination and validity here |
| F-38/c | Triangle with a small input angle, 30 degrees | pending | m3.refine | the spec asks for termination and validity here |
| F-38/d | Fan of 20 segments at 1/1024 rad | pending | m3.refine |  |
| F-38/e | Star spikes down to 2^-50 rad | pending | m3.refine |  |
| F-39 | High-degree input vertex | pending | m3.refine | 1105 gives 108 lattice points; 32045 gives 324 for the spec's size |
| F-40/angle-33.75 | High minimum angle | pending | m3.refine |  |
| F-40/angle-34.5 | High minimum angle | pending | m3.refine |  |
| F-40/angle-40 | High minimum angle | pending | m3.refine |  |
| F-41/a | Thin rectangle, 20 degrees | pending | m3.refine |  |
| F-41/b | The same with a max angle | deferred |  | Q3: no maximum angle bound in scope |
| F-42 | Bounded memory in refinement | pending | m3.refine | bounded by the limit; memory is measured by the benchmark |
| F-43/a | Touching holes, 25 degrees | pending | m2.domain |  |
| F-43/b | A hole on the outer boundary with a seed | pending | m2.domain |  |
| F-43/c | A hole with its seed on its own boundary | pending | m2.domain | ring semantics (Q7): the ring removes it, the seed changes nothing |
| F-43/d | A thin hole | pending | m2.domain |  |
| F-44 | Hole in a hole, seeds outside and inside holes | pending | m2.regions |  |
| F-45 | Two seeds in one region: the last wins | pending | m2.regions |  |
| F-46/a | A segment inside a hole does not stop the removal (Q7) | pending | m2.domain |  |
| F-46/b | A crossing breakline opens no leak | pending | m2.domain |  |
| F-47/a | Concentric circles, inner a hole | pending | m2.domain |  |
| F-47/b | Concentric circles, inner a constraint | pending | m2.cdt |  |
| F-48/a | Degenerate hole ring, three collinear points | pending | m2.domain |  |
| F-48/b | Degenerate hole ring, back and forth | pending | m2.domain |  |
| F-48/c | Degenerate hole ring, folded | pending | m2.domain |  |
| F-49 | Triangular hole at UTM scale | pending | m2.domain | with ring semantics there is no seed to misplace; the helper of I4 is F-50 |
| F-50 | Interior point of thin or small contours | blocked |  | needs the API of the helper of I4 (decision D10); session 2 writes the fixture with it. Feature m2.ring-interior |
| F-51/a | Tiny coordinates | covered | m1/tiny-64 |  |
| F-51/b | Huge coordinates | covered | m1/huge-64 |  |
| F-51/c | Mixed magnitudes | covered | m1/mixed-48 |  |
| F-51/d | Subnormal coordinates | covered | m1/subnormal-32 |  |
| F-51/e | Signs of the production predicates against the oracle, 1e-300 to 1e60 | covered | Meshing2PredicateTests.PredicateSigns_MatchTheOracle |  |
| F-52/a | Non-finite and out-of-range coordinates are argument errors | covered | Meshing2CandidateTests.InvalidCoordinates_AreArgumentErrors |  |
| F-52/b | Nearly collinear points | covered | m1/near-collinear-64 |  |
| F-53 | 1 000 published near-degenerate cases each | blocked |  | public fixtures are not committed and not fetched here (CLAUDE.md, licence check open); PredicateDifferentialTests and F-51/e and F-54 cover the same kind of input |
| F-54 | Near-cocircular signs at UTM scale and near zero | covered | Meshing2PredicateTests.NearCocircular_MatchesTheOracle | 20 000 cases, not the spec's 200 000, to keep the suite short |
| F-55/a | Millimetre features at 2^17 | pending | m3.refine |  |
| F-55/b | Millimetre features at 2^23 | pending | m3.refine |  |
| F-55/c | Millimetre features at 2^30 | pending | m3.refine |  |
| F-56/a | Exact Delaunay on a circle | covered | m1/lattice-circle-1105 | lattice points instead of random points: exactly cocircular |
| F-56/b | The same, one ulp off | covered | m1/near-cocircular |  |
| F-57 | Identical points | covered | m1/identical-5 |  |
| F-58/a | Parallel runs equal sequential runs (point sets) | covered | Meshing2CandidateTests.ParallelRuns_EqualSerial |  |
| F-58/b | 4 threads of near-cocircular predicates | covered | Meshing2PredicateTests.Predicates_InParallel_MatchTheOracle |  |
| F-58/c | 200 parallel quality runs | pending | m3.refine | the test harness runs it in parallel when the feature exists |
| F-59/a | Crossings create vertices, input ids stay | pending | m2.noding | Q8: input i stays vertex i, new vertices at n or above. Caller ids: decision D2 |
| F-59/b | Duplicate inputs map to one vertex | covered | m1/duplicates-signed-zero |  |
| F-60 | Results are independent of each other and of the input array | covered | Meshing2CandidateTests.Results_AreIndependentOfTheInput | the API has no vertex objects to share, so only this remains of the case |
| F-61 | +0 and -0 are one point | covered | m1/duplicates-signed-zero |  |
| F-62 | Point location on interior edges | pending | m1.api | the oracle probes vertices, edge midpoints and centroids through the location of the result |
| F-63 | Terrain simplification bound | deferred |  | Q5: it belongs to TerrainGreedyTIN (roadmap C2(D)) |
| F-64/a | Smoothing a finished mesh | deferred |  | Q4: O16 is out of the first version |
| F-64/b | Bounded Voronoi after smoothing | deferred |  | Q1: Voronoi output is out of the first version |
| F-64/c | Steiner point smoothing stays valid at 2^20 | pending | m4.steiner-postprocess | O15: beyond this brief |
| F-65 | Grid: rounding flips nothing | pending | m4.grid | O12: beyond this brief |
| F-66 | Public polygon fixtures (earcut, delaunator, poly2tri) | blocked |  | no mesh files in the repository; needs a fetch script pinned by URL and hash and a licence check per fixture. GOLDEN_FORMAT section 2 has the slot for them |
