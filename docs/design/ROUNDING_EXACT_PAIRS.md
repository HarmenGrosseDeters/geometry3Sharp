# SafeRounding: skip the pairs the exact arrangement already decided (Exact Boolean 2.0, lever L1)

## 1. Goal and evidence

SafeRounding (`MeshRoundingRepair`) starts with one full exact self-intersection search over the rounded result. On the
operations of the Thingi10K closed, self-intersection free cohort that take over 5 s (4 cores) that search is the
largest part of the rounding: 1063860 union 4.0 of 4.5 s rounding, 1063866 union 3.0 of 3.4 s; serially 46-77 % of the
rounding is the box-tree traversal of that search (Exact Boolean 2.0 WP1 report). In large results most triangles are
unchanged input triangles (97-98 % in 1063860, 1063866), and the search tests their pairs again, although the exact
arrangement has already resolved every pair of input triangles.

## 2. The rule

Let `E` be the result triangles that exist when `Apply` starts and whose three vertices are *exact*: their double
coordinates are the exact coordinates of the result vertex (ExactMeshBoolean: the vertex is an input point, which is a
double and so rounds to itself). A triangle leaves `E` when an edit touches it (`mark_dirty`) or removes it. The full
search queries the box tree only from the triangles not in `E`, and records every pair it finds with them (both orders);
pairs of two triangles in `E` are not tested.

### Lemma

Assumptions:
1. (Arrangement) The exact result of ExactMeshArrangement / ExactMeshBoolean, before rounding, is a set of triangles with
   rational vertices of which any two meet at most in common vertices or a common edge (pieces of a conforming
   arrangement; coplanar overlaps are output once, from one stack).
2. (Exact vertices) For a vertex `v` with `exact(v)` true, `pos(v)` equals its exact point.
3. (Shared vertices) Two result vertices of one exact point are at one position, so with SharedPositionsTouch their
   canonical ids are equal; two different exact points are either at different positions or, at one position, a
   contact the rounding made, which the repair counts separately (#398).
4. (No change) A triangle in `E` has not been changed by an edit (`E` only shrinks; an id that DMesh3 reuses for a new
   triangle was removed from `E` when its old triangle was removed).

Claim: for `t, u` in `E`, `ExactTriangleIntersection.IntersectsBeyondShared(canonical(t), T, canonical(u), U)` is false.

Proof: by 4, `t` and `u` are triangles of the exact result; by 2 their double triangles `T`, `U` are their exact triangles.
By 1, `T ∩ U` is empty, a common vertex or a common edge of the exact result. A common vertex of the exact result is a
vertex of `t` and one of `u` with one exact point; by 3 they have one canonical id, so the intersection is within what
the two share and the test is false. A common edge: both ends likewise. (The test's own decision is exact, so no
rounding of the test enters.)

What is *not* claimed: nothing about triangles with a rounded vertex; they are tested as before, against all triangles.
Triangles that are exact but were created by an edit (eg a contraction joining input vertices) are not in `E` and are
tested.

### Evaluation in doubles

The rule adds no arithmetic: `E` is a flag per triangle id, the exact flag per vertex comes from ExactMeshBoolean's
assembly (`vertexPoint >= 0 && inputPoints[...]`, the same as `resultFixed`), and the test itself is unchanged. No
error bound is involved, so there is nothing for the bound review or Z3 beyond the lemma's combinatorial assumptions;
assumption 1 is the arrangement's own guarantee.

## 3. Verification

- `CheckOnThisThread`: every full search with the rule also runs the full search without it and compares the pairs
  (`InvalidOperationException` if they differ), as the incremental search is checked today.
- Bit-identical results: the set of intersecting pairs is the same, and the order (sorted) too.

## 4. Upper bound (probe, 2026-11-08)

Probe build (env-switched, not committed), 4 cores, under load: on the 12 slow models of the cohort (36 operations) the
first full search −37 %, the operations −14 %, operations over 5 s 22 → 16; on 12 random cohort models (36 operations)
the search −62 %, the operations −10 %. 72 of 72 results identical. Little gain where few result triangles are exact
input triangles (519511: 31 %, 668056/7: 34-38 %): there the traversal itself is the lever left (tree quality, a join of
the non-exact triangles against all).

## 5. Prior art

- EMBER (Trettner et al. 2022) skips the self-pairs of meshes flagged free of self-intersections (NSI); our rule is the
  exact counterpart inside one boolean: the arrangement's resolution is the certificate, no flag is trusted.
- Milenkovic and Sacks 2019: after rounding, only features near moved vertices can come closer than the separation; the
  same locality, used here for the exact check.
- Cherchi et al. 2022, Zhou et al. 2016, Lévy 2024: no checked rounding, so no such search to skip.
