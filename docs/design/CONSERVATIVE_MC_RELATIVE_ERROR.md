# Relative error bridge for conservative Marching Cubes

Tier M, verification tools and documentation only; owner-authorized follow-up to
#423. No library arithmetic, table, API or extraction behavior changes.

## Goal and acceptance

Remove the old `|sample| <= 1` and `sample gap >= 2^-20` preconditions. Retain the
existing normalized coordinate/exact-grid requirements, the position envelope
`E = 2^-28`, and the documented `2^-26` level pad. Acceptance: SAT assumptions
and UNSAT counterexample queries, a required SAT bound mutant with exact replay,
actual binary64 regressions outside the old sample domain, and an independent
bounds review. The SAT bound mutant concerns the independent-error envelope;
it is not claimed to be attainable by actual C# crossing inputs. UNKNOWN, contradictory assumptions or an unreplayed control fail.
The continuous enclosure theorem and the table adapter remain those of #423.

## Arithmetic bridge

Assume standard binary64 round-to-nearest/ties-to-even, gradual underflow (no
FTZ/DAZ), no fused contraction or extended intermediates, finite samples and a
finite level. Consider a sorted crossing `fa < sigma <= fb`. Put

```
u = 2^-53
n = sigma - fa (exact real difference)
d = fb - fa    (exact real difference)
T = n/d, 0 <= T <= 1, d > 0
```

Every finite binary64 operand is an integer multiple of `2^-1074`. If a
subtraction's exact result is subnormal, it is therefore exactly representable.
For normal results, the standard relative model applies. Hence, whenever the
rounded subtraction is finite, including cancellation and subnormal results,

```
RN(n) = n (1 + dn), RN(d) = d (1 + dd), |dn|, |dd| <= u.
```

There is no sample-magnitude ceiling or positive lower bound on d. The actual
`LinearEdges` guards reject an infinite gap or nonfinite quotient; with finite
inputs a successful crossing therefore also has a finite rounded numerator.
An overflowing operation is a failed extraction, not a certified mesh.

For division use a relative model with an absolute underflow allowance:

```
RN(z) = z (1 + dq) + eta, |dq| <= u, |eta| <= u^2.
```

For a normal exact result choose eta=0. For a subnormal exact result choose
dq=0; its absolute rounding error is at most `2^-1075 < u^2 = 2^-106`.
The deliberately loose allowance keeps this lemma small and dimensionless;
it is not a lower bound on any sample difference. A purely relative division
model would be false: `RN(2^-1074 / 0.75) = 2^-1074` has relative error 1/4.

Substituting the two differences and canceling the strictly positive d gives

```
(t_raw - eta) (1 + dd) = T (1 + dn) (1 + dq).
```

Z3 checks this normalized equation for all errors in the intervals above and
all T in [0,1], proving `|t_raw - T| <= 4u`. This cancellation is the algebraic
bridge from source operations to the query; it imposes no restriction on d.
Projection to [0,1] cannot increase the parameter error.

## Composition with the existing position and lift bounds

The coordinate proof keeps the previous conservative absolute operation
allowances on coordinates in [-1,1]: subtraction error <= 2u, product and sum
each <= 4u, including underflow. Replacing its old parameter error `2^-30` with
`4u` proves the same position envelope `E = 2^-28`. Projection to the original
grid edge cannot increase distance from its exact linear crossing. On an
axis-aligned cell only one coordinate varies, so this is a Euclidean bound.

For exact 1-Lipschitz samples, the lift deficit is at most E; for approximate
samples with a proved absolute error e and sampled-edge slope bound S it is at
most `e + S E`. All other #423 prerequisites remain: exact Cartesian grid nodes,
the complete zero set inside the actual extraction boundary, high boundary
samples, full uncanceled extraction retaining every triangle, and a closed valid
result. No embeddedness or arbitrary-world-coordinate certificate is added.

## Existing building blocks and sources

Reuse `conservative_mc.py`'s source contracts, SAT/UNSAT gate, table certificate
and exact-rational controls. The existing `filter_rules.py` and pilot README
already use the standard relative rounding model; the new subtraction lattice
argument and division underflow case are stated above for review. Mathematical
geometry source remains only Jacobson, arXiv:2609.13430v1; no external geometry
code or additional geometry paper is used.
