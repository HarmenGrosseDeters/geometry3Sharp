# Tets above the radius-edge bound at the fixed surface (diagnosis, 2026-11-24)

Question (coordinator): a demo meshed four procedural shapes with `MeshTetrahedralization` (sphere, torus, box with a cylindrical
hole, twisted bar; 25-30k tets each, status `Success`). On none of them does the result meet the radius-edge ratio <= 2. The
tets left bad (`unrefined_bad_tets`) sit at the boundary, the smallest dihedral angle is about 9-10 degrees, and stage 2 barely
changes the mesh. Is this the algorithm or a defect?

**Verdict: expected behaviour of the design, not a defect.** Every remaining bad tet touches the fixed boundary, about 98 %
have their circumcentre outside the domain, and about 99 % have a vertex inside the diametral ball of a boundary triangle at
that tet, which is exactly the case where Delaunay refinement must split the boundary triangle. The mesher never splits it (the
surface is fixed by design, `docs/design/TET_MESHING.md` 14.8). No priority or encroachment bug was found: no bad tet away from
the boundary was left, and the at most 3 tets per mesh that none of the three explanations covers are consistent with the
stricter conflict test of the full Delaunay triangulation (below). The contract does not promise the bound: the class summary
says such tets stay and are counted, the guide says the mesher *reports* compliance (`Targets`). Two things make it look worse
than it needs to: (1) interior size control finer than the wrap's surface spacing multiplies the count, and (2) stage 2 has no
radius-edge objective at all, so it cannot help.

## 1. Where the code and the contract stand

- `mesh_ops/MeshTetrahedralization.cs`: the alpha wrap's inside cells, refined by circumcentre insertion with Li-Teng candidates.
  `Refiner.try_insert` undoes an insertion whose cavity removes any cell labelled outside (`refused_surface`); there is no
  encroachment rule and no boundary split. `CountBadInside` gives `unrefined_bad_tets` (radius-edge *or* dihedral bad,
  stage 1 only), with `bad_with_k_wrap_vertices` / `bad_with_k_surface_facets`.
- Contract: class summary ("The price: a bad tet whose circumcentre lies beyond the surface, or whose cavity would reach it,
  stays (counted in unrefined_bad_tets); the surface's own quality bounds those tets"); `TargetEdgeLength` ("tets at the fixed
  surface are not refined below its triangles (counted in unrefined_bad_tets)"); `Targets` ("measured, not assumed ... the flags
  can be false"); guide `docs/guides/meshing-2d-and-tet.md` ("Reports target compliance"). Design target T3
  (`TET_MESHING.md` 6) is `UnrefinedBadTets = 0` on *smooth procedural inputs*; 14.8 already records that box, cylinder and tori
  leave bad tets, all with at least two surface vertices. So: no promise is broken; T3 is met on the sphere only.
- Stage 2 (`TetMeshImprover`) works on tets below `SliverDihedralDegrees`; radius-edge is not in its objective. A tet with a
  bad radius-edge but all dihedrals >= 10 degrees is not touched.

## 2. Measurement

Probe: `benchmarks/TetMeshBenchmark/SurfaceProbe.cs`
(`dotnet run -c Release --project benchmarks/TetMeshBenchmark -- surfprobe <relAlpha> <surfAngle> <improve> <slide> all <relH>`,
`relH`: `TargetEdgeLength` = bounding-box diagonal / relH, 0 = off). Inputs are procedural (sphere n=24, torus 64x32 R=1 r=0.35,
box 2x2x1 minus a cylinder r=0.5 by marching cubes, 0.5x0.5x3 bar twisted 180 degrees). For every tet of `Result` with
radius-edge > 2 it records the boundary contact, the centroid's distance to the boundary (in alpha), whether the circumcentre is
outside the boundary (`DMeshAABBTree3.IsInside`), the *facet bound* (for a boundary facet f of the tet every tet on f has
radius-edge >= r_f / l_min(f); above 2 the tet cannot be repaired while f stays), and *encroachment* (a vertex of the tet
strictly inside the diametral ball of a boundary triangle at one of its vertices). One thread, 4-core container; every run
below 5 s.

### 2.1 Defaults (alpha = diagonal / 40, surface angle 15, Improve on), and with interior size control

| input | h | tets | bad (r-e > 2) | worst r-e | min dih | touch boundary (>= 2 bnd verts) | cc outside | encroaching | unexplained | max centroid dist / alpha |
|---|---|---|---|---|---|---|---|---|---|---|
| sphere | off | 4.1k | 0 | 2.00 | 10.0 | - | - | - | - | - |
| torus | off | 6.1k | 24 | 4.80 | 10.0 | 24 | 20 | 24 | 0 | 0.09 |
| box with hole | off | 11.3k | 65 | 4.27 | 10.0 | 65 | 64 | 65 | 0 | 0.17 |
| twisted bar | off | 3.5k | 60 | 4.76 | 10.1 | 60 | 53 | 59 | 1 | 0.16 |
| sphere | diag/40 | 76k | 419 | 4.47 | 10.0 | 419 | 419 | 419 | 0 | 0.16 |
| torus | diag/40 | 32k | 237 | 4.59 | 10.0 | 237 | 237 | 237 | 0 | 0.16 |
| box with hole | diag/40 | 94k | 518 | 4.37 | 10.0 | 514 | 511 | 515 | 3 | 0.48 |
| twisted bar | diag/40 | 19k | 184 | 4.76 | 10.0 | 184 | 179 | 181 | 3 | 0.15 |
| sphere | diag/60 | 255k | 3729 | 6.78 | 10.0 | 3183 | 3723 | 3702 | 0 | 0.38 |
| torus | diag/60 | 106k | 2182 | 8.29 | 10.0 | 2066 | 2182 | 2178 | 0 | 0.36 |
| box with hole | diag/60 | 306k | 5675 | 6.46 | 10.0 | 4986 | 5669 | 5622 | 0 | 0.37 |
| twisted bar | diag/60 | 61k | 1607 | 6.88 | 10.0 | 1422 | 1603 | 1599 | 0 | 0.32 |

- Every bad tet lies within half an alpha of the boundary. Without size control all have >= 2 boundary vertices; with
  h < alpha some have 0 or 1, but their circumcentre is outside all the same (the circumball crosses the surface).
- The facet bound is never above 2 at the default surface angle (the wrap's surface triangles reach r / l_min <= 1.93, i.e.
  angles >= 15 degrees, as `SurfaceMinAngleDegrees` asks): the surface triangles themselves are good enough. What is missing is
  the split of the encroached boundary triangle, which would let the bad tet's apex move away.
- The 25-30k tets of the demo match the size-control rows (h about alpha): there the count is 1-2 % of the tets.

### 2.2 Levers tried

| change | sphere | torus | box with hole | twisted bar | reading |
|---|---|---|---|---|---|
| defaults | 0 | 24 | 65 | 60 | |
| finer surface: alpha diag/80 | 12 | 28 | 47 | 48 | the count does not go to 0: it follows the curved / sharp regions, not the spacing; worst r-e drops to 2.7-3.7 |
| alpha diag/80, h = diag/40 (h = 2 alpha) | 14 | 27 | 43 | 52 | **h no longer below the surface spacing: 30x fewer than alpha 40, h diag/40** |
| surface angle 25 | 0 | 43 | 22 | 30 | mixed; on box and twist the wrap then has worse triangles (min angle 10.2, facet bound > 2 on 2-4 tets): the wrap's refinement does not reach 25 degrees there |
| size diag/40, Improve off | 419 | 237 | 516 | 184 | same as Improve on: stage 2 does not act on radius-edge |
| size diag/40, BoundarySlideFactor 0 | 419 | 237 | 518 | 184 | same: the boundary moves of stage 2 do not change it |

There is no option that lets stage 1 split boundary triangles (`SplitEdges` is a stage-2 dihedral lever; `CreaseAngleDegrees`
recovers a featured surface and then keeps it fixed as well).

### 2.3 The few unexplained tets

0-3 per mesh have the circumcentre inside, no facet bound and no vertex in a diametral ball of the triangles at their vertices.
They are refused because the circumcentre lies in the circumball of an *outside* cell of the wrap's full Delaunay
triangulation (`try_insert` refuses any cavity that removes an outside cell). That conflict test is stricter than the
diametral-ball rule a constrained Delaunay refinement would apply (where cells beyond a constrained facet do not conflict). It
is a consequence of refining in the unconstrained `Delaunay3`, not a bug; its weight is below 1 % of the bad tets.

## 3. Prior art (papers and manuals only)

- Shewchuk, "Tetrahedral mesh generation by Delaunay refinement" (SoCG 1998) and the thesis (CMU-CS-97-137): the radius-edge
  bound (rho > 2) is proven for an algorithm that splits every *encroached* subfacet and subsegment (diametral ball not empty)
  before it inserts a tet's circumcentre; the key step is that with no encroached subfacet every circumcentre lies in the mesh.
  A circumcentre outside therefore always means an encroached boundary piece, which is what 2.1 measures. Without those splits
  there is no bound.
- Si, TetGen (ACM TOMS 2015) and its manual: `-Y` suppresses Steiner points on the boundary; the manual states that with `-Y`
  together with `-q` good elements are not guaranteed and poor tets may remain ([-Y switch](https://wias-berlin.de/software/tetgen/switches.Y.html)).
  The FEniCS mshr FAQ notes the same for a preserved surface ([mshr FAQ](https://bitbucket.org/fenics-project/mshr/wiki/FAQ)).
- Cheng, Dey and Shewchuk, "Delaunay Mesh Generation" (2012), and protecting-ball methods (Cheng, Dey, Levine; CGAL Mesh_3 per its
  papers): guarantees hold away from protected features; the surface itself is refined by restricted Delaunay refinement, so the
  boundary is never kept fixed.

So: no published method guarantees a radius-edge bound next to a boundary that may not change. The known remedies are boundary
splits (in-plane, at the subfacet's circumcentre) or a boundary sized to the interior.

## 4. Options

| # | option | cost | payoff | risk |
|---|---|---|---|---|
| A | keep as is; document it plainly: the guide says radius-edge <= 2 is not reached at a fixed boundary, and that `TargetEdgeLength` below the wrap spacing makes it worse | docs only, 1 h | sets expectations (the demo's reading) | none |
| B | tie the wrap's alpha to the size field: with `TargetEdgeLength` / `SizeFunction` / grading, wrap with alpha about h at the boundary (local alpha exists: `MeshAlphaWrap.AlphaField`, used by `MaxSurfaceDeviation`) | about 1-2 days, opt-in or new default under size control | measured 30x fewer bad tets on the sphere at h = diag/40 (419 -> 14); more surface triangles | more tets and wrap time; the alpha floor alpha / 4 |
| C | encroachment splits in stage 1: split an encroached boundary triangle in its plane (circumcentre, with the 3D facet check and undo), the plan of `TET_MESHING.md` 14.2 / 14.8 step 4 | about 1-2 weeks; 14.8 recorded cascades of midpoint splits on curved wraps; needs a 2D Delaunay refinement per surface patch | the only route to the theoretical bound near the surface | the boundary changes (in-plane, so within rounding of the wrap); validity argument to redo |
| D | radius-edge objective in stage 2 (opt-in, like `MaxDihedralDegrees`) | about 3-5 days | lowers the worst r-e by flips and boundary slides, no guarantee | hill climbing; radius-edge is the refinement's control, not a quality measure that FEM needs (Shewchuk 2002, "What is a good linear element?"): these tets already have dihedrals >= 10 degrees |
| E | refine in a constrained setting (outside cells do not conflict) | large: a constrained `Delaunay3` insertion | removes only the 0-3 tets of 2.3 | not worth it alone |

Recommendation: A now; B when size control is used in earnest (it is where the demo's count comes from); C only if a user
needs the bound itself, and D only if a downstream consumer is shown to depend on radius-edge rather than on dihedral angles.

## 5. Reproduce

```
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- surfprobe 40 15 1 0.5 all 0    # defaults
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- surfprobe 40 15 1 0.5 all 40   # size control h = diag/40
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- surfprobe 80 15 1 0.5 all 40   # wrap at alpha diag/80
```

No issue was opened: no defect was found.
