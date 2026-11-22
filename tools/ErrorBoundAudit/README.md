# ErrorBoundAudit (the checker of the hand-written error bounds)

Re-derives the hand-written floating-point error bounds of the exact code from their expressions, independently of
the comments that justify them, and runs the library's filters against exact arithmetic on adversarial inputs.
The audit, its table and its findings: [docs/research/ERROR_BOUND_AUDIT_2026-11.md](../../docs/research/ERROR_BOUND_AUDIT_2026-11.md).

| File | Purpose |
|---|---|
| `fpbound.py` | The analysis: the standard model of binary64 rounding in exact rationals (`fractions`), per-operation rules for the error, the magnitude and a lower bound of a computed value, the computed-magnitude invariant (`Mirrored`), and the proof obligation `dominates` (monomial by monomial) |
| `bounds.py` | One model per bound: the guarded expression and the code's bound in the order of the source, with `anchors` (source lines that must still be there) |
| `audit.py` | Runs every model: the derivation, and the same model on concrete doubles against exact fractions; prints the table |
| `test_error_bound_audit.py` | The table as a test (anchors, verdicts, recorded findings) and mutants of the checker |
| `Adversarial/` | C#: the library's own filters (reflection, internal and private ones included) against the library's `ExactDyadic` on near-degenerate inputs over each filter's range, and the reproducers of the findings |

```
python3 tools/ErrorBoundAudit/audit.py [--samples N] [--markdown]
python3 -m unittest discover -s tools/ErrorBoundAudit -p 'test_*.py'
dotnet run --project tools/ErrorBoundAudit/Adversarial -c Release -- [samples] [seed]
```

Python 3 standard library only. No code of Gappa, FPTaylor, CGAL or another checker is used or read; the rules are
from Higham, Shewchuk and Jeannerod and Rump ([sources](../../docs/sources/tools-and-verification.md)).

## Adding a bound

A new hand-written bound in the exact code gets a model in `bounds.py`:

1. Write the leaves (`ctx.diff` a rounded difference of exact inputs, `ctx.exact`, `ctx.leaf(name, err)` an input
   with a stated error, `ctx.param` an error bound or size given as a double).
2. Write the value and the code's bound with `+ - *` in the order of the source (`Mirrored` where the bound is a
   permanent of the same expression: that is tight to second order).
3. Ask for `dominates(value.err, bound.lo, bound.defect)` (an enclosure) or `sign_dominates` (a sign filter), with
   `abs_floor` where a guard replaces an underflow term.
4. List the source lines as `anchors`.

`margin` is the smallest ratio of the bound's coefficient to the error's: 1.25 means the constant could be 20 %
smaller. A margin below 1 is not yet a counterexample (the analysis is an upper bound); look for an input with the
adversarial run.

## What it does not do

Division and square roots only as chains of rounding factors (`norm3_lower` and the `RC-*` entries), not as general
operations. Bounds whose core is an analytic argument (truncation of a series, a geometric inequality) are checked
only in their rounding part; the note lists them as not re-derived.
