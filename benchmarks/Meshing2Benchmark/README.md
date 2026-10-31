# Meshing2Benchmark: the acceptance gates of the 2D mesher

This is the working form of [docs/design/ACCEPTANCE_STRATEGY.md](../../docs/design/ACCEPTANCE_STRATEGY.md) for the 2D mesher: an oracle by definition, a differential against
NetTopologySuite, and a golden master. Where to start: [docs/design/MESHING2D_PLAN.md](../../docs/design/MESHING2D_PLAN.md). It holds no mesher: what it judges is registered in
`Candidates/CandidateRegistry.cs`, for now the kernel of the abandoned PR #200, copied in for that purpose.

```
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- <command>
```

Run it without a command for the list. Every command first runs the oracle's self-check and stops with exit code 3 if that fails.

## Layout

| Path | What |
|---|---|
| `Oracle/` | Its own project, without a reference to the library, so the compiler proves it shares no code with what it judges. Built for netstandard2.0, used by this benchmark and by the test suite |
| `Oracle/Exact.cs` | `BigInteger` predicates by definition: orientation, in-circle, areas, distances, hull, exact sin² of the smallest angle |
| `Oracle/Domain.cs` | The region to mesh, from the rings alone, with its exact area by slab decomposition |
| `Oracle/Oracle.cs` | The verdict: validity, identity, coverage, chains of segments, constrained or plain Delaunay, bounds |
| `Oracle/SelfCheck.cs` | Hand values, correct results accepted, every kind of broken result rejected as that kind, and regressions of what the oracle's independent review found |
| `Oracle/Cases.cs` | The M1 cases: procedural, integer or dyadic, without trigonometry |
| `Candidates/` | What is judged. Also compiled into the test project |
| `NtsReference.cs` | NetTopologySuite 2.6.0 (BSD-3-Clause) as a black box. This project only, never a dependency of the library |

## The pilot: M1 against NetTopologySuite (2026-10-31)

```
$ dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- differential --larger
oracle self-check: 153 checks passed
m1/tiny-64: ReferenceInvalid: NotDelaunayLocal: edge 2-3 is not locally Delaunay against 62; ...
m1/subnormal-32: ReferenceThrew: LocateFailureException: Locate failed to converge ...
m1/mixed-48: ReferenceThrew: LocateFailureException: Locate failed to converge ...
m1/near-cocircular: ReferenceInvalid: NotDelaunayLocal: edge 2-0 is not locally Delaunay against 3; ...
differential pr200-prototype against nts-2.0.0.0: SameTriangles 24, BothValid 7, ReferenceInvalid 2, ReferenceThrew 2
```

- **24 of 24 comparable cases in general position have the same triangle set**, and 7 of 7 tied cases are both valid with equal counts.
- **In four cases the reference is at fault, not the candidate.** At 2^-560 and on near-cocircular input NetTopologySuite's in-circle test gives a wrong sign, which the oracle shows; on subnormal and mixed magnitudes it
  throws. That is outside what it promises, and it is why the oracle comes first. It only excludes those inputs from the comparison. It does not count against the candidate.
- **The stop rule of the strategy did not trigger.** The reference is 50 lines and adds about a second to a run, far less than the benchmark work it replaces, and it is usable: it agrees wherever its arithmetic suffices. What it does not catch is anything
  the kernel and NetTopologySuite share; the kernel's speed is measured elsewhere (plan, section 2).
- What it replaced: the oracle of #200's benchmark, which truncated coordinates to a 2^-40 grid (`new BigInteger(p.x * 2^40)`), so it could not judge anything finer, and which had a self-test of five broken results.
