# Sources: Tools, Analyzers, Benchmarks, Fuzzing & Verification

Back to [Sources Index](../SOURCES.md) · [sources/README.md](README.md)

---

## Test harness (`tests/geometry3Sharp.Tests/Harness/`)

Implemented from the paper, no code taken over.

| Part | Source |
|---|---|
| Test-case minimization (`DeltaDebug.Minimize`, `MeshMinimizer`): ddmin over components, triangles, unused vertices and the bytes or lines of reader inputs | A. Zeller, R. Hildebrandt, *Simplifying and Isolating Failure-Inducing Input*, IEEE Transactions on Software Engineering 28(2), 2002 |
| Boolean oracles (`BooleanOracle`): solid angle of a triangle for the winding number probes | A. Van Oosterom, J. Strackee, *The Solid Angle of a Plane Triangle*, IEEE Transactions on Biomedical Engineering 30(2), 1983 |
| Winding number samples of the metamorphic bug hunt (`benchmarks/MetamorphicBugHunt/Checks.cs`): the same solid angle formula, written from the paper | A. Van Oosterom, J. Strackee, *The Solid Angle of a Plane Triangle*, IEEE Transactions on Biomedical Engineering 30(2), 1983 |
| Metamorphic relations as the test oracle of the bug hunt (docs/research/METAMORPHIC_BUG_HUNT_2026-11.md, `benchmarks/MetamorphicBugHunt`) | the metamorphic testing rows of this file (Chen, Cheung, Yiu 1998 and the surveys), as in docs/research/METAMORPHIC_TESTING.md; no further source |
| Analytic box reference, exact dyadic volumes and areas, representation changes (`BooleanOracle`) | own construction (docs/design/BOOLEAN_PROPERTY_TESTS.md) |
| Certifying checker of the exact boolean (`BooleanCertificate`): output plus witness checked by a simpler program; local consistency plus one anchor per connected set; winding-number jumps across patches and the radial order around an edge; partition check by matching facets | R. M. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 5(2), 2011; K. Mehlhorn et al., *Checking geometric programs or verification of geometric structures*, CGTA 12, 1999; Q. Zhou, E. Grinspun, D. Zorin, A. Jacobson, *Mesh arrangements for solid geometry*, ACM TOG 35(4), 2016; L. Palios, *Verification of partitions of 2d and 3d objects*; the design in docs/research/BOOLEAN_RESEARCH_PRIOR_ART_2026-11.md section 3 (from the papers, no code read) |
| Exact squared distance of a point to a triangle (`BooleanCertificate`, in rationals) | C. Ericson, *Real-Time Collision Detection*, 2005, section 5.1.5 (the Voronoi regions of the triangle) |

Test-only package: **CsCheck 4.9.1** (Apache-2.0, https://github.com/AnthonyLloyd/CsCheck, NuGet metadata checked),
referenced by `tests/geometry3Sharp.Tests` on net10.0 only with `PrivateAssets="all"`, for generation and shrinking
in `ExactBooleanPropertyTests.CsCheck.cs`. No code is copied from it and it is not a dependency of the library
package. 4.9.1 (2026-09-17, targets net8.0) rather than 5.0.0, which was a week old when it was added (2026-10-24).

## Build-time analyzers (`BannedSymbols.txt`, `tools/analyzers/G3ExactCodeAnalyzers`)

Build-time only, not shipped in the package. `Microsoft.CodeAnalysis.BannedApiAnalyzers` (MIT, NuGet) enforces the
banned APIs in the exact code; the analyzers G3E0001-3 (float, parallel loops, FMA) are own code on the Roslyn API (`Microsoft.CodeAnalysis.CSharp`,
MIT).

## Benchmarks

`benchmarks/Meshing2Benchmark` is the acceptance harness of the 2D mesher (`docs/design/MESHING2D_PLAN.md`). NetTopologySuite 2.6.0
(BSD-3-Clause) is its black-box reference for the M1 differential, in that project only. Its oracle (`Oracle/`) is written from
definitions: empty circumcircles and the local Delaunay criterion (B. Delaunay 1934; C. L. Lawson 1977; D. T. Lee, A. K. Lin,
*Generalized Delaunay triangulation for planar graphs*, 1986; L. P. Chew, *Constrained Delaunay triangulations*, 1989), and
monotone chain hulls (A. M. Andrew 1979). Its random source is SplitMix64 (G. L. Steele, D. Lea, C. H. Flood, *Fast splittable
pseudorandom number generators*, OOPSLA 2014). Triangle and Triangle.NET were not looked at; `MESHING2D_GOLDEN_FORMAT.md` defines how a separate session may compare with them as black boxes.

`benchmarks/BooleanComparison` runs CGAL (GPL), Cherchi et al. (MIT) and Manifold (Apache-2.0) as separate
programs for comparison only. The driver `external/cgal_bool.cpp` links against CGAL; compiled, it falls under
the GPL and is not distributed. None of these tools is part of the library.

`benchmarks/MeshRepairBenchmark/external/cgal_wrap.cpp` runs CGAL's alpha wrap (`CGAL::alpha_wrap_3`, GPL) the
same way, for the comparison with `MeshAlphaWrap` (`BENCH_REPAIR=cgalwrap`). It is written against CGAL's
documented API; CGAL's implementation was not looked at. Compiled, it falls under the GPL and is not distributed.

Data classes of the comparison (only downloaded at run time by `benchmarks/BooleanComparison/data/fetch.py`
at fixed commits; no mesh files are in the repository):

| class | source | license |
|---|---|---|
| `cgal` | CGAL test and example meshes, `Data/data/meshes` of https://github.com/CGAL/cgal, tag v6.0.1 (commit 50cfbde) | the data has no license of its own; CGAL's code is GPL/LGPL. Used only by the external comparison, nothing of it is part of the repository or the library |
| `cherchi` | test meshes of https://github.com/gcherchi/InteractiveAndRobustMeshBooleans, commit 7bd6c26 | MIT (repository); the models themselves (Stanford bunny and armadillo, fertility, cow, cactus) originate from public model collections with their own terms, which the repository does not restate |
| `synthetic`, `sequence` | generated by `BooleanComparison synth` (boxes, spheres, cube grids) | part of this repository (Boost) |
| `thingi10k` | Thingi10K selection of `benchmarks/MeshRepairBenchmark` | individual licenses per model |
| `thingi-pairs`, `thingi-pairs-size` | pairs of Thingi10K models (https://huggingface.co/datasets/Thingi10K/Thingi10K, metadata and raw meshes), fetched at run time by `data/thingi_pairs.py`; only the lists of ids, seeds and transforms are committed. Protocol after Trettner, Nehring-Wirxel, Kobbelt, *EMBER*, SIGGRAPH 2022 (from the paper only; no EMBER code) | individual licenses per model (mostly Creative Commons); the lists are part of this repository (Boost) |
| `thingi-contact` | pairs of Thingi10K models (as `thingi-pairs`) or one model against a copy of itself, placed by exact translations and reflections (`ContactTransforms.cs`), fetched at run time by `data/thingi_contact.py`; only the pair list `thingi-pairs/contact_pairs.csv` is committed | individual licenses per model; the list is part of this repository (Boost) |
| `abc-chained` | models of the ABC dataset (M. Koch et al., *ABC: A Big CAD Model Dataset For Geometric Deep Learning*, CVPR 2019, https://deep-geometry.github.io/abc-dataset, [doi:10.1109/CVPR.2019.00983](https://doi.org/10.1109/CVPR.2019.00983)), obj chunk downloaded on a GitHub-hosted runner by `.github/workflows/abc-chained.yml` at run time (owner decision 2026-11-16: run-time fetching only; no model or derived mesh is committed, redistributed or uploaded as an artifact; Fusion 360 Gallery excluded, GrabCAD not pursued) | the dataset is released under the MIT licence; the copyright of the models is owned by their creators (Onshape Terms of Use 1.g.ii), no warranties |

Measurements of the geogram survey ([GEOGRAM_MEASUREMENTS_2026-11.md](../research/GEOGRAM_MEASUREMENTS_2026-11.md); benchmark
code only, written from the papers, no geogram code): the spectral conformal spike (`ParameterizationBenchmark spectral`)
from P. Mullen, Y. Tong, P. Alliez, M. Desbrun, *Spectral Conformal Parameterization*, SGP 2008; the lfs yardstick
(`Meshing2Benchmark lfs`) from J. Ruppert, *A Delaunay Refinement Algorithm for Quality 2-Dimensional Mesh Generation*,
J. Algorithms 1995 (the local feature size of a PSLG and size optimality), and S. A. Mitchell, *Cardinality Bounds for
Triangulations with Bounded Minimum Angle*, CCCG 1994 (the lower bound, cited only); the restricted Voronoi cell check
(`RestrictedDelaunaySpike rvc`) from L. Yan, B. Lévy, Y. Liu, F. Sun, W. Wang, *Isotropic Remeshing with Fast and Exact
Computation of Restricted Voronoi Diagram*, SGP 2009 (facet clipping by bisectors, propagation, the security radius).

The Open Boolean Benchmark (https://github.com/Open-Boolean-Benchmark/boolean-benchmark-runners, MIT) defines
the request/result protocol that `BooleanComparison obb` implements; `obb-runner/` follows its runner layout.
No code of the runners repository was taken over. Its benchmark cases (Nefertiti, dental scans, iterated cube
grid) are not published; `cube_grid_*` of the `sequence` class is a case of our own modelled after the
description of the iterated cube grid.

## Sharp-feature probe (`benchmarks/SharpFeatureProbe`)

| Method | Source |
|---|---|
| QEF vertex from Hermite data (planes of the input around a vertex, singular values cut off) | T. Ju, F. Losasso, S. Schaefer, J. Warren, *Dual Contouring of Hermite Data*, SIGGRAPH 2002; S. Schaefer, T. Ju, J. Warren, *Manifold Dual Contouring*, IEEE TVCG 2007 |
| Feature sensitive extraction (read for the prior-art line only) | L. Kobbelt, M. Botsch, U. Schwanecke, H.-P. Seidel, *Feature Sensitive Surface Extraction from Volume Data*, SIGGRAPH 2001 |

## Changed-line coverage gate (test/tool only)

`coverlet.collector` 6.0.4 (MIT, private test dependency) collects Cobertura data in the three Linux test runs;
no package/runtime dependency is added. Integration follows the [coverlet VSTest documentation](https://github.com/coverlet-coverage/coverlet/blob/v6.0.4/Documentation/VSTestIntegration.md)
and [.NET coverage guide](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-code-coverage).
The original Python gate uses changed/executable-line intersection and union of suite hits, as documented by
[diff-cover](https://github.com/Bachmann1234/diff_cover) (Apache-2.0), with stricter missing-evidence checks.
Documentation only was consulted; no external implementation was ported. [Scope and acceptance](../../tools/coverage/README.md).

## SMT verification pilot (test/tool only)

`tools/Z3Pilot` uses `z3-solver` 4.15.4.0 (Z3 4.15.4, MIT), only as a pinned
Python verification-tool dependency. Its native library is not distributed with
geometry3Sharp and no library/NuGet dependency is added. Modeling uses the
[SMT-LIB FloatingPoint specification](https://smt-lib.org/theories-FloatingPoint.shtml)
and [Z3's public Python API](https://z3prover.github.io/api/html/namespacez3py.html).
The [pilot README](../../tools/Z3Pilot/README.md) states proof scope, mathematical
bridges, native replay and the UNKNOWN limitation of the monolithic attempt.
Pilot 2 (`tools/Z3Pilot/filter_rules.py`, the error-bound rules of `tools/PredicateCompiler`) uses the standard
model of floating-point arithmetic and its variant `fl(s) = s / (1 + d)` from N. J. Higham, *Accuracy and Stability
of Numerical Algorithms*, 2nd ed., SIAM 2002, section 2.2, and the form of the filter bounds (`|e~ - e| <= c m~`) of
J. R. Shewchuk 1997 (see above); results only, no code.

- Alec Jacobson, *A Sharp Conservative Offset for Marching Cubes*,
  [arXiv:2609.13430v1](https://arxiv.org/html/2609.13430v1), 2026, sections 1, 3,
  3.1. Paper-only mathematical source for the `LinearEdges` adapter and
  `tools/Z3Pilot/conservative_mc.py`; no external MC code ported.

## FPTaylor setup (test/tool only)

[tools/FPTaylorPilot](../../tools/FPTaylorPilot/README.md) installs FPTaylor b5a77cae (MIT) from its
[official repository](https://github.com/soarlab/FPTaylor) for local analysis. It uses OCaml 4.14.2 and
Num 1.5, each LGPL-2.1 with the OCaml linking exception, as local build/runtime dependencies; these are not
ported or distributed with geometry3Sharp. Background: Solovyev et al., [*Rigorous Estimation of
Floating-Point Round-off Errors with Symbolic Taylor Expansions*](https://soarlab.org/2015/04/fm2015-sjrg/),
FM 2015. The smoke example does not certify any geometry predicate.

## Coverage-guided fuzzing of the readers and the mesh operations (test/tool only)

[ReaderFuzzPilot](../../tools/ReaderFuzzPilot/README.md) and [MeshFuzzPilot](../../tools/MeshFuzzPilot/README.md) (the
mesh operations) use [SharpFuzz 2.2.0](https://github.com/Metalnem/sharpfuzz)
(MIT) and [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet) (MIT; libFuzzer Apache-2.0 with
LLVM exceptions) as optional test tools. Integration follows their public documentation; no external code is
ported into geometry3Sharp and no dependency is added to the library package.

## Metamorphic testing and fuzzing research note (`docs/research/METAMORPHIC_TESTING.md`, `benchmarks/MetamorphicFalsification`)

Method and prior-art references only; no code is taken from any of them (the harness is written from the relations of the note).
Titles, authors, venues and DOIs verified by a search on 2026-11-16.

| Topic | Source |
|---|---|
| Metamorphic testing, surveys | T. Y. Chen, F.-C. Kuo, H. Liu, P.-L. Poon, D. Towey, T. H. Tse, Z. Q. Zhou, *Metamorphic Testing: A Review of Challenges and Opportunities*, ACM Computing Surveys 51(1), art. 4, 2018, [doi:10.1145/3143561](https://doi.org/10.1145/3143561); S. Segura, G. Fraser, A. B. Sanchez, A. Ruiz-Cortes, *A Survey on Metamorphic Testing*, IEEE TSE 42(9), 805-824, 2016, [doi:10.1109/TSE.2016.2532875](https://doi.org/10.1109/TSE.2016.2532875) |
| Random program generation, differential testing of compilers | X. Yang, Y. Chen, E. Eide, J. Regehr, *Finding and understanding bugs in C compilers*, PLDI 2011, [doi:10.1145/1993316.1993532](https://doi.org/10.1145/1993316.1993532) |
| Equivalence modulo inputs | V. Le, M. Afshari, Z. Su, *Compiler validation via equivalence modulo inputs*, PLDI 2014, [doi:10.1145/2594291.2594334](https://doi.org/10.1145/2594291.2594334) |
| Semantics-preserving transformations of shaders | A. F. Donaldson, H. Evrard, A. Lascu, P. Thomson, *Automated testing of graphics shader compilers*, OOPSLA 2017, [doi:10.1145/3133917](https://doi.org/10.1145/3133917) |
| Evaluation of exact mesh Booleans (context only; the protocol itself was not read) | P. Trettner, J. Nehring-Wirxel, L. Kobbelt, *EMBER: Exact Mesh Booleans via Efficient and Robust Local Arrangements*, ACM TOG 41(4), 2022; G. Cherchi, M. Livesu, R. Scateni, M. Attene, *Fast and Robust Mesh Arrangements using Floating-point Arithmetic*, ACM TOG 39(6), 2020, [doi:10.1145/3414685.3417818](https://doi.org/10.1145/3414685.3417818) |

## Cross-field research

The [research report](../research/CROSS_FIELD_GEOMETRY.md) records opened sources, reading depth and license checks
(papers and documentation only; no external implementation code was read). New sources it adds to the open items:

| Topic | Source | Status |
|---|---|---|
| Separation and rounding with a displacement budget | V. Milenkovic, E. Sacks, *Geometric Rounding and Feature Separation in Meshes*, Computer-Aided Design 108, 2019, [preprint](https://arxiv.org/abs/1805.03252); LAMMPS developers, [Neighbor lists](https://docs.lammps.org/Developer_par_neigh.html) (Verlet skins) | paper / documentation only |
| Rounding cost and the leads of 3D snap rounding (docs/design/SNAP_ROUNDING_3D.md, a research note; prototypes in `benchmarks/SnapRoundingExperiment`, the lemma in `tools/Z3Pilot/separating_axis.py`; no library code) | S. Fortune, *Vertex-rounding a three-dimensional polyhedral subdivision*, DCG 22, 1999; L. Diazzi, D. Panozzo, A. Vaxman, M. Attene, *Constrained Delaunay Tetrahedrization: A Robust and Practical Approach*, TOG 2023, [arXiv 2309.09805](https://arxiv.org/abs/2309.09805); Y. Hu et al., *Fast Tetrahedral Meshing in the Wild*, TOG 2020, [arXiv 1908.03581](https://arxiv.org/abs/1908.03581); L. Diazzi, M. Attene, *Convex polyhedral meshing for robust solid modeling*, TOG 2021; Liu, Ye, Meng, Liu, Chen, *Robust and fast local repair for intersecting triangle meshes*, CAD 2025; X. Zhang, M. Lee, Y. J. Kim, *Interactive continuous collision detection for non-convex polyhedra*, The Visual Computer 22, 2006; S. Oh et al., *PaMO*, CGF 2025, [arXiv 2509.05595](https://arxiv.org/abs/2509.05595); V. Milenkovic, L. Nackman, *Finding compact coordinate representations for polygons and polyhedra*, SoCG 1990; the separating-axis theorem as in S. Gottschalk, M. Lin, D. Manocha, *OBBTree*, SIGGRAPH 1996, and T. Akenine-Möller, *Fast 3D triangle-box overlap testing*, JGT 2001 | papers only (full text or abstract as marked in the note); no code read; the prototypes are own |
| The plateau variant of the position search (docs/design/SNAP_ROUNDING_3D.md section 10, a design that is not built; the lemma in `tools/Z3Pilot/plateau_search.py`, the acceptance rule prototyped in `benchmarks/SnapRoundingExperiment/LatticeProbe.cs`; no library code) | Own design. Background, the scheme only (steps that keep the count, ended by a cap): B. Selman, H. Levesque, D. Mitchell, *A New Method for Solving Hard Satisfiability Problems*, AAAI 1992; J. Frank, P. Cheeseman, J. Stutz, *When Gravity Fails: Local Search Topology*, JAIR 7, 1997, [arXiv cs/9712101](https://arxiv.org/abs/cs/9712101). Read and not used: L. Freitag, P. Plassmann, *Local optimization-based simplicial mesh untangling and improvement*, IJNME 49, 2000; D. Halperin, C. Shelton, *A perturbation scheme for spherical arrangements with application to molecular modeling*, CGTA 10, 1998; S. Funke, C. Klein, K. Mehlhorn, S. Schmitt, *Controlled Perturbation for Delaunay Triangulations*, SODA 2005. The integer sequence a build would use is SplitMix64, as `TerrainSlopeDesign` already has it | abstracts and snippets only; no code read; the prototype is own |
| Incidences and pinches missed by snap rounding in practice | J. Hershberger, *Snap Rounding: A Cautionary Tale*, SoCG 2025, [paper](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.SoCG.2025.57) | paper only; background for the Q17 pinch fix |
| Certified convex free regions | R. Deits, R. Tedrake, *Computing Large Convex Regions of Obstacle-Free Space Through Semidefinite Programming*, WAFR 2014, [paper](https://groups.csail.mit.edu/robotics-center/public_papers/Deits14.pdf) | paper only |

## Forks of geometry3Sharp

| Fork | Taken over |
|---|---|
| [Digital-Production-Aachen/DAPComputationalGeometry](https://github.com/Digital-Production-Aachen/DAPComputationalGeometry) (Boost-1.0), delta `8f185f1`..`158507b` | Nothing: audited 2026-11-01, no bug fix applies to this code ([audit result](../design/DAP_BUGFIX_AUDIT.md#audit-result-2026-11-01)). A later port of one of its features is listed here with the DAP commit and file |
