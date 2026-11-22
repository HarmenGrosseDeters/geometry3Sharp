# Cross-field transfer, problem first: which fields already solve the structures behind our hard problems

**Research note, 22 November 2026.** Docs only: no library code, no benchmark run, no measurement of its own. Process
tier **S**. Every item below that leads to work needs its own brief, upper-bound measurement, licence check and review
under [AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md).

## 1. Relation to the earlier notes

- [CROSS_FIELD_GEOMETRY.md](CROSS_FIELD_GEOMETRY.md) (#368) started from four problems (3D snap rounding, the Q17 grid
  refinement, envelopes, open-input inside / outside) and looked for mechanisms.
- [UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md) (#1299) went **field first**: it scanned
  mathematics, physics, biology and chemistry for unused ideas and ranked them by ROI.
- This note goes **problem first, structure second, field third**. It takes the open problems of the library as they
  stand on 2026-11-21 ([OPEN_TOPICS.md](../OPEN_TOPICS.md) sections 1, 5 and 6, the hard problems HP 1 to 11 of
  [roadmap/research.md](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model), the open issues), names the
  mathematical structure under each one, and asks which field solves *that structure* as routine work. The fields
  searched are the ones #1299 did not cover: satisfiability and constraint solving, operations research, radar
  interferometry and geodesy, spatial databases, formal methods and computer-assisted proof, coding theory and
  cryptography, N-body physics, adaptive mesh refinement, performance engineering.

Nothing of #1299 is repeated. Where a finding touches one of its items (the sheaf H1 diagnostics, the operator-adapted
inverse Cholesky, the sum-of-squares verdict) the text says so and adds only what is new.

The main result is uncomfortable for a "transfer" note and is stated first: **most cross-field candidates die on
numbers the repository already has.** Section 6 lists twelve of them with the measurement that kills each; one of them was on this note's own shortlist
until step 6 of the method found the measurement that had already closed it. What
survives is a short list in which the transfer is small, the upper bound is a measurement of hours, and the problem is
one the library has today.

## 2. Evidence discipline

Labels for a claim, as in #1299:

- **Known**: established in the cited source, in its own field.
- **Transfer**: known there; untested on our problem.
- **Speculative**: the gain here is a guess until the upper-bound measurement of the brief is done.

Labels for a source:

- **[V]**: the bibliographic record (authors, title, venue, year) and the abstract-level claim were checked on a
  publisher, arXiv, DBLP or author page on 2026-11-22.
- **[M]**: cited from memory; not re-checked in this session. A spike that builds on an [M] source checks it first.

No paper was read in full for this note, and no code of any external project was opened. Every statement about a paper
is therefore at abstract level; a spike reads the primary paper before it builds on it
([CLAUDE.md](../../CLAUDE.md), licences). Where a reference implementation is named, its licence is the one I
believe it carries and is marked "re-check"; none is proposed for porting. Numbers about this library are quoted from
the linked notes, not measured again.

## 3. Method: the problem-first transfer search

The search has seven steps. It is written so that a later session can repeat it on the then-open problems.

1. **Collect the problems from the repository, not from memory.** Sources: OPEN_TOPICS sections 1, 5, 6; the hard
   problems list; open issues; the "stopped at its stop rule" entries of the design notes. A problem qualifies when a
   lever was measured and fell short, not when something is merely unbuilt.
2. **Find where it actually stops.** Read the design note until it names the step that fails or dominates (a budget
   that runs out, a search that enumerates, a proof that is missing). The headline of a problem is often not its
   bottleneck: the pure-quad fallback rate reads like a flow problem and is a search-budget problem (section 4, row Q).
3. **Name the structure in one line, without geometry words.** "A backtracking search that runs out of budget", "a
   spatial join whose boxes overlap in bulk", "a finite-parameter inequality", "integer corrections with zero curl".
   If the line still needs the word "mesh", it is not abstract enough to search another field with.
4. **Ask who solves that line daily.** Search by the structure, in the vocabulary of the other field. A field counts
   only if it has (a) a standard formulation, (b) solvers or tools in production use, and (c) published numbers.
5. **Check the transfer conditions.** Each field's method rests on assumptions (planar grid, total unimodularity,
   success / failure outcomes, smooth functions). List them and check each against our case. Most failures are here.
6. **Kill by an existing number first.** Before proposing a measurement, look for one the repository already has:
   the share of the targeted work, the size of the operands, the count of cases. An idea whose best case is below
   that number goes to the dead-end table with the number. (This step removed one item from a draft of this note: the
   rounding's box-neighbour enumeration, row 1 of section 6.)
7. **Write the survivor as a brief**: question, upper-bound measurement, constraints, stop criterion, and a paragraph
   "why it probably fails here" written by the proposer against their own idea.

Two rules kept the list short. An idea that only restates what a running spike does is recorded as a pointer to that
spike, not as an item. An idea whose only gain is elegance (a unifying language, a nicer formulation) is dropped.

## 4. Problems, structures, fields

One row per open problem. "Where it stops" quotes the linked note. The last column points to section 5 (shortlist),
6 (dead ends) or 7 (pointers for running spikes).

| | Problem | Where it stops today | Structure | Fields that solve the structure routinely | Result |
|---|---|---|---|---|---|
| Q | Pure quads, stage 3B: fallback in 54 % ([QUAD_REMESHING.md](../design/QUAD_REMESHING.md) 4.4) | 26 of the 29 fallbacks end as `BudgetExceeded`; the local repair is a DPLL search with chronological backtracking and no learning under a budget of 300k decisions | a backtracking search over "exactly one" groups that exhausts its budget | SAT solving (conflict-driven clause learning, restarts) | 5.1 |
| Q' | Position-field integer offsets (3B flow; "flow on the size field" follow-up) | the flow removes the face violations but relabels thin strings of vertices; follow-ups on hold | integer corrections on edges with zero curl per face, L1 cost, sources at the singular faces | radar interferometry (phase unwrapping by min-cost flow), railway timetabling (periodic event scheduling) | 5.4, 6 |
| T | Tet floor and the improver's work cap (HP 5, #1154; [TET_MESHING.md](../design/TET_MESHING.md) 17.11, 17.16) | hill climbing stops in local optima; slow meshes run all 200 passes with little progress; the count of meshes reaching the floor moves with the order of equal candidates | a budgeted local search whose outcome depends on tie order | SAT / constraint solving (heavy-tailed run times, restart schedules, portfolios) | 5.2 |
| R | Rounding cost, part b (HP 3; [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md) 4.3) | 20 to 355 box hits per query triangle on results with long thin triangles on tilted flat faces; 0 of 60M pairs intersect on 519511 union | a spatial join in which axis boxes overlap in bulk and almost no pair is a true neighbour | spatial databases (dead-space pruning), collision detection (oriented volumes) | 6 |
| M6 | 2D meshing, small-angle cascade (HP 6) | exemption up to 60 degrees; a provably tight excuse region is open | a geometric inequality over a compact configuration space of few parameters | computer-assisted proof (interval branch and bound; the Kepler proof), nonlinear SMT | 5.3 |
| M7 | 2D meshing, oracle speed (HP 7; [MESHING2D_FAST_ORACLE.md](../design/MESHING2D_FAST_ORACLE.md)) | 3.6 times the mesher on Herne, target 2 | a checker that repeats the producer's search | SAT proof checking (DRAT against LRAT), certifying algorithms | 5.5 |
| B | Biharmonic cold latency (#980, #976) | sparse factorization | a sparse SPD solve on an elliptic operator | theoretical computer science (approximate elimination for Laplacians) | 5.6 |
| S | 3D snap rounding with a guarantee (HP 1) | no guarantee known; the plateau search is clean in 2 of 3 seeds on its one real case | round many coupled variables so that every local constraint keeps its sign | combinatorics (local lemma, constructive form), geodesy and communications (integer least squares), lattice reduction | 6 (all three) |
| E | Exact predicate cost on the slow tail | fixed after the allocation-free kernel; multiplications under 1 % | the sign of an integer polynomial | cryptography and computer algebra (residue number systems) | 6 |
| O | Open inputs: inside / outside across patches (plan #1247) | 62 of 111 fallbacks at the structural crack limit; leaning no-go | a minimum chain with a given boundary; a labelling consistent across cycles | algebraic topology with linear programming, quantum error-correction decoders | 6 |
| C | Boolean classification on open operands (HP 8, #1047) | 66.9 % of the time of the 66 extreme operations | a potential evaluated at many clustered targets | N-body physics (dual-tree traversal) | 7 |
| K | Contouring certificate, silhouette edges (plan #1289) | double crossings at silhouette edges come back at every level | range bounds of a function over a box | accelerator physics (Taylor models) | 6 |
| D | Determinism across runtimes (HP 11) | stages 0 and 1 merged | the same bits from two execution environments | consensus systems, WebAssembly's deterministic subset | 6 |
| N | Integer (normal) coordinates (plan #1284) | study only | a curve stored as crossing counts | low-dimensional topology (compressed curves) | 7 |
| V | Envelope-guaranteed simplification (HP 10) | a cheap certified bound per collapse is missing | a one-sided distance bound that survives a local edit | nothing new found beyond [CROSS_FIELD_GEOMETRY.md](CROSS_FIELD_GEOMETRY.md) idea 4 and the permission grids already in [RESEARCH_REDUCER_REMESHER.md](../design/RESEARCH_REDUCER_REMESHER.md) | none |
| W | Runtime watch, tests above 20 s in some runs (#698) | judged by a fixed rule per run | a step change in a noisy time series | performance engineering (change-point detection) | 7 |

Fields searched with no usable result for any row: numerical relativity and adaptive mesh refinement (their grid
rules, proper nesting and 2:1 balance, are the octree balance and grading the library has; the Berger-Rigoutsos box
clustering [M] has no problem here that asks for it), robust statistics (no open problem is an estimation problem with
outliers; the thresholds in question, such as #1204, need an exact predicate, not a better estimator), control and
certified reachability (the sum-of-squares verdict of #1299 stands for run-time use; the offline use is in 5.3),
seismic imaging (its fast solvers are eikonal and kriging methods the library has or #1299 covers), crystallography
beyond #1299 (Coxeter triangulations give in 3D the body-centred lattice that isosurface stuffing already uses).

## 5. Shortlist

Ranked by (chance that the upper bound comes out positive) x (size of the gain on a problem the library has today) /
(cost of the first measurement). Six items earn a brief; a seventh and an eighth were not found, and the list is not
padded. Item 4 depends on the hold on the 3B follow-ups, item 5 on HP 7, which is "not now".

| Rank | Item | Problem | Class | First measurement | Feeds a running spike |
|---|---|---|---|---|---|
| 1 | Clause learning in the 3B local repair | Q | Transfer | hours: classify the 26 budget fallbacks | sheaf H1 for quads, quad layout |
| 2 | Tie-order distribution and restart schedules for budgeted local searches | T (also S, Q) | Transfer | hours: 8 tie orders on the slow meshes | tet HP 5, 3D snap rounding |
| 3 | Interval branch-and-bound proofs for quality floors | M6 (also T) | Transfer | days: state the lemma, count its parameters | 2D meshing HP 6 / 7, tet HP 5 |
| 4 | Phase-unwrapping practice for the integer offsets | Q' | Transfer / speculative | a day: reliability-weighted costs on the gate set | quad layout |
| 5 | Hints instead of search in the checkers | M7 | Known pattern, partly in use | hours: split the oracle time into search and predicates | 2D meshing HP 7, certifying Boolean checker |
| 6 | Approximate elimination as a second arm | B | Transfer | inside the running spike | operator-adapted inverse Cholesky |

### 5.1 Clause learning in the 3B local repair

**Problem and structure.** `QuadRemesher.PureQuads` falls back on 29 of the 54 gate runs, and 26 of those 29 are
`BudgetExceeded`: "the SAT budget or the split budget is used up and the map still has folds". The solver is
`GroupSatSolver`: unit propagation with two watched literals, chronological backtracking, no learning, a total budget
of 300k decisions. The headline problem looks like a flow or a topology problem; the step that fails is a propositional
search that runs out of budget.

**Who solves it.** This is the daily work of SAT solving since the late 1990s. Conflict analysis with learned clauses
and non-chronological backtracking was introduced as GRASP (Marques-Silva and Sakallah, IEEE Trans. Computers 48(5),
1999 [V]); the modern architecture is that of MiniSat (Eén and Sörensson, *An Extensible SAT-solver*, SAT 2003 [M];
reference code MIT, re-check). The known effect: a chronological search re-derives the same conflict under every
assignment of the unrelated variables, a learned clause removes that whole family at once. Maturity: the base of
every industrial solver; the algorithm fits in a few hundred lines and is deterministic when its restarts are
counted in conflicts and its ties are broken by index.

**What the transfer needs.** Conflict analysis (first unique implication point), clause learning, backjumping, an
activity order with deterministic ties, restarts counted in conflicts. The "exactly one" groups stay as they are (as
clauses or as a native constraint). The preferred-value order that makes the first model "the nearest" must be kept
as the phase choice, or the result changes more than needed.

**Upper bound.** At most 26 of 54 runs (the budget fallbacks); the fallback rate could fall from 54 % to 6 % at best.
The real bound is the number of those 26 whose instances are satisfiable at all.

**Why it probably fails here.** The budget may be exhausted on instances that have no solution: if the folds cannot
be removed inside the move range of the rings, every local instance is unsatisfiable, and a better solver only reaches
the same fallback sooner. The note itself says the dislocations "sit in the integer structure of the lattice and a
local update cannot move them". Second, the instances are small (rings 1 to 6); on small instances learning helps less
than on industrial ones, and the budget may go to the number of instances, not to one hard one. Third, the label
covers the split budget too, and that share is not separated in the note. Fourth, a learning solver finds a different
model, so the clean rows change bits; the option is opt-in, which permits it, but the gate (Hausdorff, corner
deviation) has to be re-measured.

**Spike brief.** *Question:* how many of the 26 `BudgetExceeded` fallbacks of `benchmarks/QuadFlow3BGate` are caused
by the search and not by the problem? *Upper-bound measurement:* log, per fallback, which budget ended it (SAT
decisions or splits) and dump the local instances of the SAT cases as DIMACS; decide each with a throwaway
clause-learning solver written from the papers (or, as a black box and for the measurement only, an MIT-licensed
solver) with no budget; report satisfiable / unsatisfiable / undecided per run, and how many runs would have no fold
left if every satisfiable instance were solved. *Constraints:* no library change; effort counted in conflicts, never
in time; no code of QuadriFlow or Instant Meshes read; results only for the opt-in path. *Stop:* if fewer than 8 of
the 26 runs become fold-free in the best case, stop and hand the unsatisfiable instances to the sheaf H1 spike as its
test set (they are certified local obstructions, which is what that spike wants to localize). Otherwise the next step
is a tier M brief for the solver, with the determinism and reuse tests.

### 5.2 Tie-order distribution and restart schedules for budgeted local searches

**Problem and structure.** Three open items share one shape. The tet improver runs "all 200 passes with little
progress in the last ones" on its slow meshes (#1154), and the number of meshes that reach the floor is "sensitive
to choices between equal candidates" (36, 37 or 38 of 40 for versions that differ only in a list order). The plateau
search of the rounding is clean "in 2 of 3 seeds" on its one real case. The 3B repair has a decision budget. Each is
a search whose effort and outcome depend on arbitrary choices, cut by a deterministic budget.

**Who solves it.** The constraint-solving community measured exactly this in the late 1990s: the run time of a
backtracking search over the choice of ties is heavy-tailed, so a few orders are very fast and the mean is dominated by
the slow ones (Gomes, Selman, Crato and Kautz, J. Automated Reasoning 24, 2000 [V]: restarts "eliminate" the heavy
tail). The universal restart schedule of Luby, Sinclair and Zuckerman (Information Processing Letters 47(4), 1993 [V])
needs no knowledge of the distribution and is within a logarithmic factor of the best schedule for it. Running
several orders side by side is the algorithm portfolio (Huberman, Lukose and Hogg, Science 275, 1997 [M]; Gomes and
Selman, Artificial Intelligence 126, 2001 [M]). Maturity: every SAT solver restarts.

**What the transfer needs.** Nothing but a family of deterministic tie orders (a seed that permutes equal
candidates, as the position search already has with "the vertex id and the pass") and a schedule of budgets in
visits. A restart here means: return to the mesh as it was after the main passes and run the rescue with the next
order and the next budget of the schedule; the undo log of the improver makes that cheap.

**Upper bound.** For the work cap: the visits of the fastest of K orders against the visits of today's order, on the
meshes above 20 s. For the floor: the meshes of the 500-id sample that reach 10 degrees under at least one of K orders
(today 498; 38781 and 282598 are at 9.4 and 9.5 degrees).

**Why it probably fails here.** The theory is for searches that succeed or fail. The improver is a hill climb with a
quality level as its result; a restart throws away real progress, and a portfolio only pays if some order is much
better, not slightly better. The spread seen so far (36 to 38 of 40) is small. The floor is "a resolution effect" by
the note's own diagnosis, and 839690 "stays below 8 degrees in every variant tried": an order cannot fix a mesh that
needs a boundary change. And under the bit-for-bit rule the schedule becomes part of the result, so it can never be
tuned again without changing outputs.

**Spike brief.** *Question:* is the effort of the rescue phase heavy-tailed over the order of equal candidates?
*Upper-bound measurement:* on the meshes of TET_MESHING 17.16 that take more than 20 s at floors 20 and 30, and on
38781 and 282598, run the improver under 8 deterministic permutations of equal candidates (a throwaway switch); record
the visits to reach the final quality, the worst dihedral and the count below the floor; report minimum, median and
maximum per mesh. *Constraints:* effort in visits, never in time; no change of the default; one process at a time.
*Stop:* if the minimum over the 8 orders is above 0.5 of the median on every mesh, and no order lifts 38781 or 282598
to 10 degrees, stop: the distribution is not heavy-tailed and the cap of #1154 is the answer. If it is heavy-tailed, the
next step is an opt-in Luby schedule over the rescue, with the result to the owner before a PR, as #1154 requires.

### 5.3 Interval branch-and-bound proofs for quality floors

**Problem and structure.** HP 6 asks for "a region-based oracle excuse that is provably tight (the floor
arcsin(sin(phi/2)/sqrt 2) near clusters)". HP 5 has a floor that holds on 498 of 500 meshes and no bound. Both
claims have the form: for every configuration in a described set, an angle is at least a constant. When the set is
compact and has few parameters this is a finite computation.

**Who solves it.** Computer-assisted proof. Labelle and Shewchuk proved the dihedral bounds of isosurface stuffing
(10.7 to 164.8 degrees; ACM TOG 26(3), 2007 [V]) this way: Labelle's thesis describes "recursive bisection of the
space of possible tetrahedron configurations together with interval arithmetic". The largest example is the formal
proof of the Kepler conjecture, which rests on about a thousand nonlinear inequalities over small boxes, checked by
interval arithmetic with Taylor approximations (Solovyev and Hales, NASA Formal Methods 2013, arXiv:1301.1702 [V]).
For inequalities with trigonometric functions there are decision tools: MetiTarski (Akbarpour and Paulson, J.
Automated Reasoning 44, 2010 [M]) and the delta-complete procedure of dReal (Gao, Kong and Clarke, CADE 2013 [V];
licence not confirmed, one source reports GPL-3 for one version, so tool use at most, never a port). The library has
the ingredients for its own checker: `ExactInterval`, and the Z3 pilot (`tools/Z3Pilot`), which already states
lemmas over the reals. The new part is the method: a proof by bisection over the whole parameter box, as opposed to
the pilot's searches on tiny configurations.

**What the transfer needs.** The hard step is not the computation. It is to state the configuration space: which
parameters describe a triangle near a cluster of small input angles after the concentric-shell splits (the angle phi,
the positions of the split points within a shell ratio, the insertion radius), with ranges, such that every triangle
the rule can produce is in the set. Then a branch and bound of perhaps a hundred lines evaluates the angle over
boxes and bisects until the bound holds or a counterexample box appears. A counterexample box is a test case.

**Upper bound.** No speed. The gain is a statement: either the floor becomes a lemma with a machine-checked proof
(and the 60 degree exemption of #496 can be narrowed to the proven region), or the bisection returns a configuration
below the floor, which is a mesher input worth having. The measurable part: how many triangles the oracle excuses
today that the tight region would no longer excuse.

**Why it probably fails here.** The configuration space may not be finite-dimensional: a cluster can hold any number
of segments, and the proof needs a reduction to a bounded stencil that may not exist (it is the reason Shewchuk's
Terminator argument is what it is). A proof about the rule is not a proof about the implementation in doubles; the
oracle's exact predicates close part of that gap, not all. For HP 5 the method fits lattice meshers and not Delaunay
refinement on a wrap surface; OPEN_TOPICS already records "lattice method only if a proof is required", and this
item does not change that verdict, it only says how the proof would be done. Interval bounds on arcsin and sin need
correctly rounded or outward-rounded functions (the library has `CoreMath` for some).

**Spike brief.** *Question:* can the HP 6 floor near a cluster be written as an inequality over a compact box of at
most six parameters? *Upper-bound measurement:* none on a benchmark; the deliverable is the lemma. Write the
configuration space and the claim in the design note; run a throwaway interval bisection (Python with outward
rounding, or `ExactInterval`) to a fixed depth; report "proved to depth d", "counterexample box", or "space not
finite". *Constraints:* papers only; no Triangle or Triangle.NET; no library change; the result is a section of
[MESHING2D_SMALL_ANGLES.md](../design/MESHING2D_SMALL_ANGLES.md) or a new design note, reviewed as a proof. *Stop:*
after two days without a finite parametrization, stop and record why (that negative result closes the "provably
tight region" branch of HP 6 and leaves "a new rule").

### 5.4 Phase-unwrapping practice for the integer offsets

**Problem and structure.** Stage 3B looks for an integer offset per edge, close to a real target, whose (frame
rotated) sum around every face is zero; the cost is an L1 distance, the solver a min-cost flow. The 3B upper bound
(4.3) saw the typical artefact: the repair "relabels thin strings of vertices", and "a unit travels 20-70 edges".

**Who solves it.** This is two-dimensional phase unwrapping as radar interferometry has run it for 25 years: integer
corrections on the edges of a grid with zero curl per cell, L1 cost, solved as a min-cost flow whose sources are the
residues (Costantini, IEEE Trans. Geoscience and Remote Sensing 36(3), 1998 [V]: "weights derived from coherence").
The production successor uses statistical, non-uniform costs (Chen and Zebker, J. Opt. Soc. Am. A 18(2), 2001 [V];
their companion paper of 2000 shows the general problem intractable and motivates the network heuristics). The thin
strings are that field's branch cuts, and its standard remedy is the cost: make a unit of flow cheap where the data
is unreliable, so that the cuts run where they do no harm. In the same structural family, railway timetabling states
integer period offsets on a cycle basis (the periodic event scheduling problem, Serafini and Ukovich, SIAM J.
Discrete Math. 2(4), 1989 [M]; integral cycle bases, Liebchen and Peeters, Discrete Optimization 2009 [M, title and
year to be checked]); it has cutting planes for the integers but no method that fits here better than the flow.
As far as this search saw, the quad-meshing papers and the interferometry papers do not cite each other; that is an
observation from a short search, not a novelty claim.

**What the transfer needs.** Only a cost model: a reliability per edge (field smoothness, distance to a feature or
to a field singularity, the size field) in place of the two-tier uniform cost. The flow solver
(`solvers/MinCostFlow.cs`) stays.

**Upper bound.** The irregular-vertex count and the string length after the flow with an oracle weight (cheap along
the shortest paths between the sources, expensive elsewhere) against cost mode 2, on the 54 gate runs.

**Why it probably fails here.** Interferometry works on a planar regular grid, where the constraint matrix is a
network matrix; on a surface with frame rotations it is not (the 3B note balances the cycles with a holonomy
greedily), so the guarantees do not carry over. The flow is not what limits 3B today: it is 50 to 54 % of the time
but the fallbacks are folds and budgets (5.1). The floor on the irregular count is set by the uniform spacing (the
note's coframe argument), which no cost changes. And the follow-ups are on hold for paper access.

**Spike brief.** *Question:* does a reliability-weighted cost shorten the relabelled strings and lower the irregular
count of 3B? *Upper-bound measurement:* on `benchmarks/QuadFlow3BGate`, replace the cost by an oracle weight computed
from the known sources (throwaway); report irregular vertices, relabelled vertices and the fallback split against
cost mode 2. *Constraints:* opt-in path only; integer costs; no code of interferometry packages read (their licences
were not checked). *Stop:* if the oracle weight does not cut the irregular count on the clean rows by at least 20 %,
stop; a real weight can only do worse. Start only after 5.1 has shown where the fallbacks come from.

### 5.5 Hints instead of search in the checkers

**Problem and structure.** The fast oracle is at 3.6 times the mesher on Herne against a target of 2. A checker is
slow when it has to find again what the producer already knew: which triangle holds a point, which segments are near
a vertex, which vertices compete for a needle.

**Who solves it.** SAT solving met the same wall. Checking a DRAT proof needs the checker to search for the clauses
that justify each step; the LRAT format adds those hints to the proof, and a formally verified checker then runs
"almost as fast as, and in some cases faster than" the unverified search-based one (Cruz-Filipe, Heule, Hunt,
Kaufmann and Schneider-Kamp, CADE 2017 [V]). The lesson is quantitative and general: move every search to the
untrusted side, leave the trusted side with a replay. The oracle already follows it in one place (the mesher's hull
triangulation as a certified witness, after McConnell et al. 2011), and the mesher already carries provenance
([MESHING2D_PROVENANCE.md](../design/MESHING2D_PROVENANCE.md)).

**What the transfer needs.** For each remaining part of the oracle, the question "what would the mesher have to
write down so that this check needs no search": a containing triangle per vertex, the nearest segment per excused
triangle, the chain a pixel belongs to. Each hint is untrusted and is verified by the predicate it shortcuts.

**Upper bound.** The share of the oracle's time that is search and not predicate evaluation. The profile exists in
part: on the corpus coverage is 35 %, segments 19 %, marks 11 %; on Herne quality is 37 % and Delaunay 24 %.

**Why it probably fails here.** The oracle has been through this once and stopped at its rule; what is left may be
the per-element exact predicates, which no hint removes. The acceptance strategy wants an oracle that shares no code
with the mesher; a hint format is an interface between them, and a mesher bug that writes a consistent wrong hint
must still be caught, which costs a completeness check per hint class. And HP 7 is "not now".

**Spike brief.** *Question:* how much of the remaining oracle time is search? *Upper-bound measurement:* in
`benchmarks/` the oracle profile of MESHING2D_FAST_ORACLE, split each part's time into locating (walks, candidate
enumeration, grid lookups) and deciding (predicates); report the bound "search removed" per part for Herne and the
corpus. *Constraints:* measurement only; the oracle's independence rules stay. *Stop:* if removing all search leaves
Herne above 2.5 times the mesher, stop: the stop rule of the oracle note holds and hints do not change it. The same
question, asked of stage 3 of the certifying Boolean checker (plan #1256), is a pointer for that spike, not a second
brief.

### 5.6 Approximate elimination as a second arm of the solver spike

**Problem and structure.** The cold latency of the biharmonic solve (#980) is a sparse SPD factorization on an
elliptic operator. #1299 proposed the operator-adapted inverse Cholesky, and a spike is running.

**Who solves it.** Theoretical computer science has a second, independent line for the same structure: approximate
Gaussian elimination for graph Laplacians, where each eliminated vertex's clique is replaced by a sampled sparse
one (Kyng and Sachdeva, FOCS 2016 [M]), with a practical version benchmarked against multigrid and incomplete
Cholesky (Gao, Kyng and Spielman, arXiv:2303.00709, 2023 [V]; reference code in Julia, MIT, re-check). It is a
preconditioner of a few hundred lines with no geometric ordering to compute.

**What the transfer needs.** A seeded generator (the sampling is random; with a fixed seed and a serial elimination
the factor is deterministic), the conjugate gradient iteration with a fixed-order reduction, and a matrix that is
symmetric diagonally dominant with non-positive off-diagonals.

**Why it probably fails here.** The last condition: a cotangent Laplacian has positive off-diagonals at obtuse
angles unless the triangulation is intrinsic Delaunay, and the biharmonic operator is not a Laplacian at all (it
would be two nested solves, and with constraints not even that). The sampling order is serial, so the parallel gain
of the inverse-Cholesky route is absent. It is a preconditioner, so the result depends on the iteration count and
the tolerance, which the certified biharmonic mode has to bound.

**Spike brief (an addition to the running spike, not a new one).** *Question:* on the Laplace-type systems of the
library (heat method, cross field), does seeded approximate elimination with conjugate gradients beat the candidate
of the inverse-Cholesky spike at equal residual? *Upper-bound measurement:* one more arm in that spike's benchmark
table, on intrinsic Delaunay Laplacians only. *Constraints:* papers only; seeded and serial; fixed-order sums.
*Stop:* if it is not within a factor 2 of the spike's own candidate on the first mesh, drop the arm.

## 6. Dead ends, with the number that kills each

The anti-hype filter of #1299 asked "is the constant too large, is the fit poor". Here most candidates could be
closed by a number the repository already has. Each row names what would reopen it.

| Candidate (field) | Structure it solves | Why not here | Reopen if |
|---|---|---|---|
| Dead-space pruning in the rounding's first search (spatial databases: clipped bounding boxes, Šidlauskas, Chester, Tzirita Zacharatou and Ailamaki, ICDE 2018 [V], whose abstract reports 19 % fewer I/Os on the revised R*-tree; collision detection: k-DOPs, Klosowski et al. 1998 [M]) | a spatial join whose axis boxes overlap in bulk (316 box hits per query on 519511 union, 0 of 60.0M pairs intersect) | Measured and closed in [SNAP_ROUNDING_3D.md](../design/SNAP_ROUNDING_3D.md) 5.3: eight separating axes per pair remove 68 to 97 % of the exact pair tests but 4 to 52 % of the search time, -1.6 % in the real A/B; culling tree nodes by the query's axes "removes a quarter to a third of the hits and costs more". Clip points on the nodes are the same node-level cull. This row was shortlist item 3 of a draft of this note. | a join built in the frame of each flat face (not a filter on the axis-box tree) gets an upper bound first; it is under the speed stop |
| Residue number systems for exact predicates (cryptography, computer algebra; sign determination in residues, Brönnimann, Emiris, Pan and Pion, Theoretical Computer Science 210, 1999 [V]) | integer polynomial arithmetic with multiplication linear in the operand size | The operands are small: 4 limbs per component cover 100 % of the grid points and 98.9 % of the sphere case, and "the multiplications themselves are under 1 % of the time" ([FIXED_WIDTH_EXACT_POINTS.md](../design/FIXED_WIDTH_EXACT_POINTS.md)). Residues win from tens of limbs on; the sign needs a conversion back at the end. | a predicate class with operands above about 16 limbs dominates a profile |
| Residue checks as an independent test of the limb kernel (coding theory: arithmetic codes) | a cheap independent check of big-integer results | The tests already compare against a `BigInteger` reference; a second reference adds no kill signal the mutation runs lack. | a limb bug escapes the reference tests |
| Constructive local lemma for rounding (combinatorics; Moser and Tardos, J. ACM 57(2), 2010 [M]) | set many variables so that all local bad events are avoided, by resampling | Its condition is that each bad event is improbable under random choices. Where that holds, rounding to nearest already works; the leftovers are clusters at ulp scale, where the events are likely. It would be a conditional guarantee for the easy cases only. | a use case accepts a guarantee under a stated clearance |
| Integer least squares, LAMBDA and sphere decoding (geodesy; Teunissen, J. Geodesy 70, 1995 [V]; Agrell et al., IEEE Trans. Information Theory 48(8), 2002 [M]) | the nearest integer vector under a quadratic form | In rounding the objective is not quadratic and the constraints are orientation signs. In the quad position field, "the rounding of the origins only changes the representative": the result is bit for bit the same. | a mixed-integer parameterization solver enters the library |
| Lattice basis reduction | short lattice vectors | Closed in CROSS_FIELD_GEOMETRY section 3; nothing new found. | as stated there |
| Optimal homologous chains by linear programming; decoders of topological codes (Dey, Hirani and Krishnamoorthy, SIAM J. Computing 40(4), 2011 [M]; Delfosse and Nickerson, Quantum 5, 2021 [M]) | a minimum chain with a given boundary; a consistent labelling from local evidence | The open-input spike (#331) found 62 of 111 fallbacks at the structural crack limit, "no labelling closes them", and the owner question (plan #1247) is a use case, not a method. A minimal closing chain is a completion policy, which changes the semantics. | the owner names a use case for an opt-in closure of large holes |
| Taylor models (accelerator physics; Makino and Berz, 2003 [M]) | tighter range bounds for smooth functions | The contouring certificate stops at silhouette edges of piecewise linear surfaces; the cause is the geometry of the double crossing, not loose bounds. For class S (analytic implicits) intervals already certify "from 2 cells on". | stage 3 shows refinement chains caused by interval overestimation |
| Sum-of-squares at run time (control, robotics) | certified polynomial non-negativity | Verdict of #1299 (5b) unchanged. The offline use, as a proof tool, is in 5.3. | curved primitives become a core representation |
| Determinism checklists of consensus systems and WebAssembly | the same bits on every platform | Every item such a list holds (NaN bits, signed zero in `Min` / `Max`, out-of-range casts, unstable sorts, libm) is a numbered rule in [DETERMINISM_PROOF.md](../design/DETERMINISM_PROOF.md) (R4, R5, R11, R18), most with a probe. | a rule class is found that the ledger lacks |
| Multiset fingerprints, memory checking (verified computing; Blum et al., Algorithmica 12, 1994 [M]) | check a pairing in linear time without a table | In use: the oracle's certificate hashes the directed edges with a mixing hash (MESHING2D_FAST_ORACLE). | - |
| Periodic event scheduling as a solver for quads (railway timetabling) | integer period offsets on a cycle basis | No mixed-integer parameterization in the library to apply the cutting planes to; the dislocation floor of the uniform spacing is not a solver problem. Kept as a formulation pointer in 5.4. | as for integer least squares |

## 7. Pointers for spikes that are already running

These are not items. Each is one fact or one measurement that a named spike can use; none starts work of its own.
Spikes with no pointer from this search: the SolidBooleanPipeline audit (#1203 and #1204 ask for exact predicates,
which is not a transfer question), alpha-wrap features (HP 4: #1299 item 1 stands; nothing found beyond it),
cross-runtime determinism (section 6), contouring upgrades (section 6), envelope simplification (row V of section 4).

| Running spike | Pointer |
|---|---|
| Sheaf H1 for quads | The unsatisfiable local instances of 5.1 are machine-certified local obstructions: a ready test set for "does the H1 support localize the failure". The real-valued sheaf obstruction and the integer one can differ; the flow's infeasibility has its own classical certificate, a cut that violates the circulation condition (Hoffman; in Ahuja, Magnanti and Orlin, which the repository cites), and it costs nothing extra to report when `FlowInfeasible` occurs. |
| Quad layout | 5.1 before any layout change (where do the fallbacks come from); 5.4 for the cost model. |
| Tet HP 5 | 5.2 (tie-order distribution) and 5.3 (how a floor would be proved, and that the method fits lattice meshers only). |
| 3D snap rounding | 5.2: the plateau variant's "2 of 3 seeds" is the pattern restart schedules address, but the variant is a no-go on its gain (one operation), which a schedule does not change. The three guarantee candidates of section 6 are closed. |
| Boolean speed | The rounding's box-neighbour enumeration is closed again in section 6. For the classification on open operands (66.9 % of the 66 extreme operations): N-body codes evaluate a potential at many clustered targets by a dual-tree traversal, cell against cell, in place of one descent per target (Dehnen, J. Computational Physics 179, 2002 [M]). Whether it applies depends on how much of the classification time is repeated descent for neighbouring query points after the per-patch evaluation and the hierarchical boundary sum; that share is not in the notes and is the upper bound to measure first. |
| 2D meshing HP 6 / 7 | 5.3 and 5.5. |
| Certifying Boolean checker | 5.5: design stage 3 so that the Boolean writes the hints (containing cell, generating pair) and the checker only replays. |
| Operator-adapted inverse Cholesky | 5.6. |
| Intrinsic normal coordinates | Low-dimensional topology stores curves as crossing counts and works on them without unpacking: tracing takes time polynomial in the size of the triangulation and the bit length of the counts, not in the number of crossings (Erickson and Nayyeri, Discrete and Computational Geometry 49(4), 2013 [V]). Relevant if the study needs the common subdivision of a curve with very many crossings; for the counts the current operations produce it changes nothing. |
| Runtime watch (#698, the 20 s rule; no spike) | Performance engineering replaced fixed thresholds on noisy timings by change-point detection on the series per test (Daly et al., ICPE 2020, arXiv:2003.00584 [V]). Worth a look only if the fixed rule keeps producing false alarms; it is tooling, not geometry. |

## 8. What this search says about the approach

- **The bottleneck is rarely where the headline says.** Row Q is the clearest case: a "min-cost flow with
  integrality" problem by its name, a budgeted propositional search by its failure log. A field-first scan would
  have matched it to flow algorithms (#1299 did, and rightly put the almost-linear flow on the watchlist); the
  problem-first reading of the fallback statuses points at a different field.
- **The transfers that survive are small and old.** Clause learning (1999), restart schedules (1993), proofs by
  bisection (2007), flow costs from interferometry (1998 to 2001): none is a 2025 breakthrough. The recent, impressive candidates (residue
  arithmetic from homomorphic encryption, decoders from quantum error correction) died on the repository's own
  numbers.
- **A negative table is a result.** Twelve closed candidates with a number each are worth more to the next search
  than a longer shortlist: they say which structures of this library are *not* its bottleneck (integer
  multiplication, the determinism rule set, range bounds on smooth implicits).
- **Limits of this note.** It is one session's search at abstract level. The [M] sources are unverified, no paper
  was read in full, and "nobody has connected these" was never established for any item. The ranking is ordinal.
  The upper bounds are proposals for measurements, not measurements.

## 9. Sources

Checked on 2026-11-22 at record and abstract level ([V]) or cited from memory ([M]); see section 2. No code of any
of the projects named was opened.

| Source | Level | Used in |
|---|---|---|
| J. P. Marques-Silva, K. A. Sakallah, *GRASP: A Search Algorithm for Propositional Satisfiability*, IEEE Trans. Computers 48(5), 506-521, 1999 | V | 5.1 |
| N. Eén, N. Sörensson, *An Extensible SAT-solver*, SAT 2003, LNCS 2919 (MiniSat; MIT, re-check) | M | 5.1 |
| M. Luby, A. Sinclair, D. Zuckerman, *Optimal Speedup of Las Vegas Algorithms*, Information Processing Letters 47(4), 173-180, 1993 | V | 5.2 |
| C. P. Gomes, B. Selman, N. Crato, H. Kautz, *Heavy-Tailed Phenomena in Satisfiability and Constraint Satisfaction Problems*, J. Automated Reasoning 24, 67-100, 2000 | V | 5.2 |
| B. A. Huberman, R. M. Lukose, T. Hogg, *An Economics Approach to Hard Computational Problems*, Science 275, 51-54, 1997 | M | 5.2 |
| C. P. Gomes, B. Selman, *Algorithm Portfolios*, Artificial Intelligence 126, 43-62, 2001 | M | 5.2 |
| D. Šidlauskas, S. Chester, E. Tzirita Zacharatou, A. Ailamaki, *Improving Spatial Data Processing by Clipping Minimum Bounding Boxes*, ICDE 2018, 425-436 | V | 6 |
| J. T. Klosowski, M. Held, J. S. B. Mitchell, H. Sowizral, K. Zikan, *Efficient Collision Detection Using Bounding Volume Hierarchies of k-DOPs*, IEEE TVCG 4(1), 1998 | M | 6 |
| F. Labelle, J. R. Shewchuk, *Isosurface Stuffing: Fast Tetrahedral Meshes with Good Dihedral Angles*, ACM TOG 26(3), 2007; F. Labelle, *Tetrahedral Mesh Generation with Good Dihedral Angles Using Point Lattices*, UC Berkeley report EECS-2007-104 | V | 5.3 |
| A. Solovyev, T. C. Hales, *Formal Verification of Nonlinear Inequalities with Taylor Interval Approximations*, NASA Formal Methods 2013, 383-397, arXiv:1301.1702 | V | 5.3 |
| B. Akbarpour, L. C. Paulson, *MetiTarski: An Automatic Theorem Prover for Real-Valued Special Functions*, J. Automated Reasoning 44, 175-205, 2010 | M | 5.3 |
| S. Gao, S. Kong, E. M. Clarke, *dReal: An SMT Solver for Nonlinear Theories over the Reals*, CADE-24, 2013 (licence not confirmed) | V | 5.3 |
| M. Costantini, *A Novel Phase Unwrapping Method Based on Network Programming*, IEEE Trans. Geoscience and Remote Sensing 36(3), 813-821, 1998 | V | 5.4 |
| C. W. Chen, H. A. Zebker, *Two-dimensional phase unwrapping with use of statistical models for cost functions in nonlinear optimization*, J. Opt. Soc. Am. A 18(2), 338-351, 2001; *Network approaches to two-dimensional phase unwrapping: intractability and two new algorithms*, J. Opt. Soc. Am. A 17, 401-414, 2000 | V | 5.4 |
| P. Serafini, W. Ukovich, *A Mathematical Model for Periodic Scheduling Problems*, SIAM J. Discrete Mathematics 2(4), 1989 | M | 5.4, 6 |
| C. Liebchen, L. Peeters, *Integral cycle bases for cyclic timetabling*, Discrete Optimization, 2009 (title and year to be checked) | M | 5.4 |
| L. Cruz-Filipe, M. J. H. Heule, W. A. Hunt Jr., M. Kaufmann, P. Schneider-Kamp, *Efficient Certified RAT Verification*, CADE-26, LNCS 10395, 220-236, 2017 | V | 5.5 |
| R. Kyng, S. Sachdeva, *Approximate Gaussian Elimination for Laplacians: Fast, Sparse, and Simple*, FOCS 2016 | M | 5.6 |
| Y. Gao, R. Kyng, D. A. Spielman, *Robust and Practical Solution of Laplacian Equations by Approximate Elimination*, arXiv:2303.00709, 2023 (Laplacians.jl; MIT, re-check) | V | 5.6 |
| H. Brönnimann, I. Z. Emiris, V. Y. Pan, S. Pion, *Sign Determination in Residue Number Systems*, Theoretical Computer Science 210(1), 173-197, 1999 | V | 6 |
| R. A. Moser, G. Tardos, *A Constructive Proof of the General Lovász Local Lemma*, J. ACM 57(2), 2010 | M | 6 |
| P. J. G. Teunissen, *The least-squares ambiguity decorrelation adjustment: a method for fast GPS integer ambiguity estimation*, J. Geodesy 70, 65-82, 1995 | V | 6 |
| E. Agrell, T. Eriksson, A. Vardy, K. Zeger, *Closest Point Search in Lattices*, IEEE Trans. Information Theory 48(8), 2002 | M | 6 |
| T. K. Dey, A. N. Hirani, B. Krishnamoorthy, *Optimal Homologous Cycles, Total Unimodularity, and Linear Programming*, SIAM J. Computing 40(4), 2011 | M | 6 |
| N. Delfosse, N. H. Nickerson, *Almost-linear time decoding algorithm for topological codes*, Quantum 5, 595, 2021 | M | 6 |
| K. Makino, M. Berz, *Taylor Models and Other Validated Functional Inclusion Methods*, Int. J. Pure and Applied Mathematics, 2003 | M | 6 |
| M. Blum, W. Evans, P. Gemmell, S. Kannan, M. Naor, *Checking the Correctness of Memories*, Algorithmica 12, 1994 | M | 6 |
| M. J. Berger, I. Rigoutsos, *An Algorithm for Point Clustering and Grid Generation*, IEEE Trans. Systems, Man, and Cybernetics 21(5), 1991 | M | 4 |
| W. Dehnen, *A Hierarchical O(N) Force Calculation Algorithm*, J. Computational Physics 179, 27-42, 2002 | M | 7 |
| J. Erickson, A. Nayyeri, *Tracing Compressed Curves in Triangulated Surfaces*, Discrete and Computational Geometry 49(4), 823-863, 2013 | V | 7 |
| D. Daly, W. Brown, H. Ingo, J. O'Leary, D. Bradford, *The Use of Change Point Detection to Identify Software Performance Regressions in a Continuous Integration System*, ICPE 2020, 67-75, arXiv:2003.00584 | V | 7 |
