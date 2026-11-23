# Willmore energy and remeshing a moving ring torus

## Decision

Park a general Willmore fairing operator pending a caller and an energy-stable discretization. The existing operators serve different objectives. `MeshCurvature` provides the ingredients for a vertex quadrature of bending energy. `LaplacianMeshSmoother` minimizes a constrained graph-Laplacian residual and `BiharmonicMeshDeformer` minimizes a displacement energy with handles. Neither exposes descent of the current surface's bending energy. Crane, Pinkall and Schröder describe conformal curvature flow with curvature-space updates and fixed connectivity. The experiment below uses a one-parameter surface family to test the library's curvature and remeshing operations. [Source](../sources/meshing-and-repair.md)

The guarded remesher preserved a clean torus through all four tested moves. Its first pass increased the measured bending energy more than fourfold. An intersection guard and analytic projection are therefore insufficient to guarantee energy descent on the evolving triangulation. A production stepping rule would need to check energy on the actual output mesh, including after connectivity changes. The unguarded static control also intersects, so the observed intersections do not establish a defect specific to curvature-driven motion.

## Reproduce

Run `LC_ALL=C dotnet run -c Release --project benchmarks/WillmoreFlowProbe`. The probe generates periodic triangulated ring tori with unit tube radius and major radius R. For the smooth torus, the principal curvatures up to orientation are 1 and cos(v)/(R + cos(v)), and the area element is (R + cos(v)) du dv. Integrating the squared half-sum gives W(R) = π² R² / √(R² − 1). Its minimum in this family occurs at R = √2 with W = 2π². The probe sums the squared signed mean curvature times mixed vertex area, validating every curvature sample. On n × 2n grids the absolute energy errors are:

| Tube samples n | R = √2 | R = 3 |
|---:|---:|---:|
| 12 | 0.860867 | 0.551988 |
| 24 | 0.228949 | 0.141799 |
| 48 | 0.058206 | 0.035766 |

On the structured 48 × 24 torus a centered difference with radius increment 1e-4 chooses the descent direction. Starting at R = 3, try a displacement of 0.75 (three target edge lengths) and halve it until R > 1 and the measured structured energy decreases. This is restricted descent in R without a physical time parameter or area/volume constraint. Apply each accepted radius change to a second mesh by moving vertices radially in the xy plane. Run one serial `Remesher.BasicRemeshPass` per move, with target edge length 0.25 and nearest-point projection onto the new smooth torus. The control keeps R fixed for one pass. The only setting changed between guarded and unguarded runs is `PreventSelfIntersections`.

| Motion | Guard | R | Structured W | Moving mesh W before pass | W after pass | Intersecting triangle pairs |
|---|---|---:|---:|---:|---:|---:|
| Static | Off | 3 | 31.2631 | 31.2631 | 77.9338 | 7 |
| Static | On | 3 | 31.2631 | 31.2631 | 76.9624 | 0 |
| Moving | Off | 2.25 | 24.6537 | 24.6537 | 110.9539 | 15 |
| Moving | On | 2.25 | 24.6537 | 24.6537 | 111.6296 | 0 |
| Moving | On | 1.5 | 19.6642 | 116.0239 | 90.5166 | 0 |
| Moving | On | 1.40625 | 19.5078 | 96.0104 | 62.7280 | 0 |
| Moving | On | 1.40039 | 19.5076 | 62.7913 | 64.3281 | 0 |

Each input to a remeshing pass passed the clean-mesh check. Unguarded runs stop at their first positive pair count. Guarded outputs also passed the checks for a single closed, consistently oriented genus-one component, valid curvature samples and absence of self-intersections. The moving guard rejected 125 operations or moves cumulatively. On the first guarded step the vertex count changed from 1,152 to 2,110. The before/after energy difference includes changes in vertices, triangles and curvature quadrature.

The energy measurement costs one curvature evaluation and one vertex sum per mesh, linear in mesh size. Each radius trial repeats this on the structured grid. Remeshing and the exact intersection checks add separate geometry-dependent costs. The energy oracle and the observed near-fourfold error reduction apply to this refinement family. The experiment does not establish stability of unconstrained fourth-order flow, model membrane equilibria under physiological constraints, or give a general remeshing guarantee.
