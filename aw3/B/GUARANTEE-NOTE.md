# (B) round-based carving: guarantee note (draft, 2026-11-12)

Claim: a round of (B) that applies a set G of gates whose *touched cells are pairwise disjoint* and re-checks each gate
against the current state before applying it (the cell stamps the serial loop already uses) computes exactly what the
serial carving loop computes when its queue happens to pop the gates of G consecutively in the round's fixed order. So
the round algorithm is a serialization of the serial algorithm with a different queue order, and every guarantee whose
proof does not use the order of the queue holds for it.

Touched cells of a gate (c inside, n outside, facet i): carve: {c}; rule 1 or rule 2: the cavity (conflict region) of the
Steiner point p, which contains c, plus its boundary neighbours whose adjacency changes. Disjointness argument:
- A gate's test (oracle, touch, projection) reads only c, n, their vertices and circumcenters and the static input: unchanged
  if no earlier gate of the round touched c or n (stamp check).
- Insertion: the cavity of p is the set of cells whose circumsphere contains p. If the earlier insertions of the round
  touched none of p's cavity cells, those cells and their circumspheres are unchanged, and an earlier point p' is not inside
  any of them (else that cell would be in p''s cavity too, contradiction). So p's cavity, its insertion and its Lemma 2 radius
  (the empty ball through the facet, radius > alpha for rule 1; the ball around c's center for rule 2) are the same as when
  inserted first.
- Carving c changes only c's label (inside -> outside) and queues c's facets; another gate of the round needs c inside only if
  its inner cell is c (excluded by disjointness).

Which proofs use the queue order (paper Section 4.3, Appendix B; our notes docs/design/ALPHA_WRAP_PAPER_NOTES.md section 2):
- Termination (Theorem 1): packing argument on the insertion radii (Lemmas 1, 2): per insertion, order-free.
- Watertight, orientable, enclosure (a cell meeting the input is never carved: rule 2 first): per step, order-free.
- Offset band [0, alpha + offset] and the size criterion (every final facet has Phi < alpha): from the termination state
  (empty queue), order-free.
- The derived minimum angle bound (sin theta > min(alpha, delta) / (2 alpha)): termination state + Lemma 2, order-free.
- 2-manifold: our final manifold step (and the pinch refinement before it) work on the final labels: order-free.
- Not order-free: the *measured* quality (p01, max angles, the 2.0 angle floors of the quality refinement, triangle count):
  the largest-Phi-first order is a heuristic for it (as in Delaunay refinement); must be re-measured.
- Determinism: G is chosen by a fixed rule on the current state (priority, then cell ids), never by threads, so serial =
  parallel = any thread count; 2^k scaling holds (no new constants). Output differs from master's serial order.

Open points: the selection rule (greedy by priority over a window, or Luby-style by hashed ids), cavity computation without
insertion (Delaunay3 has the conflict search inside Insert; a read-only variant is needed), the quality refinement and
pinch passes (keep serial, or rounds too).
