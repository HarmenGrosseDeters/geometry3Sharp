# Acceptance of spec-driven work: references and golden masters

Status: proposal, for the owner to decide on. It replaces the plan for hidden acceptance tests by separate authors (#202). That plan needs
isolated authors, and this repository is public, with the quadtree implementation and its tests on `origin/quadtree`: the authors
could read them, and nobody could tell. Independence now comes from references the implementer cannot bend: mathematics and other libraries.

## 1. Three questions, three kinds of check

| Question | Check | Can it be wrong? |
|---|---|---|
| Is the result correct by definition? | Oracle (section 2) | Only if the oracle is wrong: it shares no code with the implementation |
| Does it agree with other solutions? | Differential (section 3) | A difference needs a reason; either side can be at fault |
| Did it change since it was accepted? | Golden master (section 4) | It says nothing about correctness: it pins what was accepted |

The order matters. **A golden master is recorded only after the first two pass**, or it freezes defects as expected results.

## 2. Oracle by definition

Every test and benchmark result goes through it. The M1 oracle in `benchmarks/Meshing2Benchmark` is the model: exact integers and `BigInteger`
determinants, no production predicates, no `IsValid`, and it must reject deliberately broken results at startup.
Extend it per milestone: empty circumcircles apart from constraints, every input segment a union of output edges, no crossing edges,
exact domain area, holes empty, and for refinement the promised angle and size bounds.

## 3. Differential against other solutions

Inputs are procedural, with integer or dyadic coordinates. No mesh files in the repository.

| Milestone | Reference | What must agree |
|---|---|---|
| M1 Delaunay of points | NetTopologySuite (BSD), in the benchmark project | General position: the triangulation is unique, so **the same triangle set**. Cocircular ties: only that both are valid |
| M2 constrained Delaunay | `Triangle.NET`, owner only (below) | The same, in general position with a unique result |
| M3 refinement | `Triangle.NET`, owner only | Not unique, so only metrics: triangle count within a stated factor, minimum angle, protected segments, area |
| Quadtree | Brute force (exists), NetTopologySuite (exists), the owner's old quadtree per `QUADTREE2D_SPEC.md` section 5 | The same result sets by identity; timing and memory on the owner's machine |

**Clean room.** Agents never read `Triangle.NET` or its source. Only the owner runs it, with a small tool outside this repository. The tool reads the
procedural case descriptions and writes one JSON row per case: triangle count, order-independent fingerprint of triangles and subsegments,
minimum angle, area, status. Only those rows enter the repository. Fingerprints reveal no algorithm. Whether its license permits publishing such
results is for the owner to confirm before the first rows are committed.

## 4. Golden master

- Per case, stored in JSON next to the benchmark: fingerprint, triangle and subsegment counts, status, quality values. No times, as they vary between machines
  and are measured separately. No mesh files.
- CI compares them on a small fixed subset that runs in seconds to about a minute, as in `docs/QA.md` 4a item 5. A different value fails.
- **Updating values:** only with the benchmark's diff in the PR and a reason for each changed case, seen by a reviewer. An update without a reason counts as a regression. The implementer
  can see the values, and that is fine: the protection is that changes are visible, not that they are secret.
- **Runtime rounding:** the prism fixtures broke because `Sin`/`Cos` differ between runtimes. Golden cases use no trigonometry in their inputs. Exact topology is compared across
  net10, net48 and both operating systems. If Steiner coordinates differ by runtime, they get a stated tolerance or a value per runtime, never a loosened fingerprint for the topology.

## 5. Failure catalogue

The F cases of `MESHING2D_SPEC.md` become procedural fixtures that run through sections 2 to 4, in a table with one state per case: covered, deferred by the
owner's scope, or blocked with a reason. The D cases of the quadtree are simple enough for ordinary tests. Nobody needs to work from the spec alone.

## 6. Where to spend it

- **Quadtree: nothing new.** It is easy to test, and brute force is a complete oracle. What stays open is the side-by-side on the owner's machine.
- **Triangulation: this is where it pays off.** Order: M1 differential against NetTopologySuite as the pilot, then the golden subset for M1. `Triangle.NET` rows follow with M2.

**Stop rule for the pilot:** it is one small PR. If it costs more than the benchmark work it replaces, or NetTopologySuite turns out to be unusable as a reference, stop and report that,
rather than building the rest. A pilot that finds no difference is still a result: the oracle and the differential then run on every later change.

## 7. What this does not give

- No proof for unusual inputs nobody thought of: the catalogue and fuzzing cover those, as far as they do.
- The same person or agent can still write a weak oracle. An independent review of each oracle, and the check that it rejects broken results, stay required.
- Golden masters pin behaviour: a defect that all references share, or that nobody checks for, stays unseen.
