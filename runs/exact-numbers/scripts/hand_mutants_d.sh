#!/bin/bash
# Hand mutants of ExactLimbSigns.Orient2 / InCircle and of their two call sites in ExactMeshArrangement (candidate D):
# each is applied in the worktree given as $1 (a clean, committed tree), ExactLimbSignsTestsPlanar and
# ExactBooleanWidePointTests must fail; a surviving mutant prints SURVIVED; the files are restored afterwards.
# $2 (optional): only the mutants whose name contains it.
set -u
cd "$1"
ONLY="${2:-}"
K=math/ExactLimbSigns.cs
A=mesh_ops/ExactMeshArrangement.cs
FILTER="FullyQualifiedName~ExactLimbSignsTestsPlanar|FullyQualifiedName~ExactBooleanWidePointTests"
run() {   # name, file, old, new, [occurrence, 0-based]
    name="$1"; file="$2"
    case "$name" in *"$ONLY"*) ;; *) return;; esac
    python3 - "$file" "$3" "$4" "${5:-0}" <<'PY'
import sys
p, old, new, nth = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
s = open(p).read()
parts = s.split(old)
assert len(parts) > nth + 1, "mutant does not apply: " + old
open(p, "w").write(old.join(parts[:nth + 1]) + new + old.join(parts[nth + 1:]))
PY
    if [ $? -ne 0 ]; then echo "NOT APPLIED $name"; return; fi
    out=$(dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "$FILTER" 2>&1 | grep -E "Passed!|Failed!| error |\[FAIL\]|Test Run Aborted" | grep -oE "Passed!.*|Failed!.*| error .*|[A-Za-z0-9_.]+\([^\[]{0,40}|[A-Za-z0-9_.]+ \[FAIL\]|\[FAIL\]|Test Run Aborted" | head -6 | cut -c1-120 | tr '\n' ' ')
    case "$out" in *"Failed!"*|*" error "*|*"[FAIL]"*|*"Test Run Aborted"*) echo "killed   $name: $out";; *) echo "SURVIVED $name: $out";; esac
    git checkout -q -- $K $A
}
# the arithmetic
run "in-circle row: A added instead of subtracted" $K 'FixedInt A = FixedArith.Sub(FixedArith.Mul(vi, dw, ref ar), FixedArith.Mul(di, vw, ref ar), ref ar);' 'FixedInt A = FixedArith.Add(FixedArith.Mul(vi, dw, ref ar), FixedArith.Mul(di, vw, ref ar), ref ar);'
run "in-circle row: B from the axis i" $K 'FixedInt B = FixedArith.Sub(FixedArith.Mul(vj, dw, ref ar), FixedArith.Mul(dj, vw, ref ar), ref ar);' 'FixedInt B = FixedArith.Sub(FixedArith.Mul(vi, dw, ref ar), FixedArith.Mul(dj, vw, ref ar), ref ar);'
run "in-circle row: the factor s dropped from x" $K 'x = FixedArith.Mul(A, s, ref ar);' 'x = A;'
run "in-circle row: the factor s of the fourth point only" $K 'FixedInt s = FixedArith.Mul(vw, dw, ref ar);' 'FixedInt s = dw;'
run "in-circle row: B^2 dropped" $K 'l = FixedArith.Add(FixedArith.Mul(A, A, ref ar), FixedArith.Mul(B, B, ref ar), ref ar);' 'l = FixedArith.Mul(A, A, ref ar);'
run "in-circle row: A^2 - B^2" $K 'l = FixedArith.Add(FixedArith.Mul(A, A, ref ar), FixedArith.Mul(B, B, ref ar), ref ar);' 'l = FixedArith.Sub(FixedArith.Mul(A, A, ref ar), FixedArith.Mul(B, B, ref ar), ref ar);'
run "in-circle: two rows exchanged" $K 'sign = det3(ax, ay, al, bx, by, bl, cx, cy, cl, ref ar).Sign;' 'sign = det3(bx, by, bl, ax, ay, al, cx, cy, cl, ref ar).Sign;'
run "in-circle: the third row against the wrong point" $K 'incircle_row(f6, f7, f8, f9, f10, f11, ref ar, out cx, out cy, out cl);' 'incircle_row(f6, f7, f8, f3, f4, f5, ref ar, out cx, out cy, out cl);'
run "in-circle: an operand loaded from the wrong axis" $K 'FixedInt f10 = load(dj, b, n[10]); b += n[10];' 'FixedInt f10 = load(di, b, n[10]); b += n[10];'
run "orient: two columns exchanged" $K 'sign = det3(f0, f1, f2, f3, f4, f5, f6, f7, f8, ref ar).Sign;' 'sign = det3(f1, f0, f2, f4, f3, f5, f7, f6, f8, ref ar).Sign;'
run "orient: W of the third point taken as its j entry" $K 'FixedInt f8 = load(c.W, p, n[8]);' 'FixedInt f8 = load(cj, p, n[8]);'
# the scratch space
run "scratch: orient, the first row" $K 'return 6L * a + 15L' 'return 3L * a + 15L'
run "scratch: orient, the other rows" $K '15L * ((long)b + c) + 32;' '12L * ((long)b + c) + 32;'
run "scratch: orient, no constant" $K '15L * ((long)b + c) + 32;' '15L * ((long)b + c);'
run "scratch: in-circle, the first point" $K 'return 28L * p + 46L' 'return 20L * p + 46L'
run "scratch: in-circle, the other points" $K '46L * ((long)q + r) + 128;' '40L * ((long)q + r) + 128;'
run "scratch: in-circle, the third point not counted" $K '46L * ((long)q + r) + 128;' '46L * ((long)q + q) + 128;'
run "scratch: in-circle, no constant" $K '46L * ((long)q + r) + 128;' '46L * ((long)q + r);'
run "scratch: in-circle, the fourth point's width not added" $K 'Math.Max(n[0], Math.Max(n[1], n[2])) + wd, Math.Max(n[3]' 'Math.Max(n[0], Math.Max(n[1], n[2])), Math.Max(n[3]'
run "scratch: in-circle, the fourth point's widest entry from two of three" $K 'int wd = Math.Max(n[9], Math.Max(n[10], n[11]));' 'int wd = Math.Max(n[9], n[10]);'
run "scratch: orient, a row's widest entry from two of three" $K 'long need = OrientScratch(Math.Max(n[0], Math.Max(n[1], n[2])),' 'long need = OrientScratch(Math.Max(n[0], n[1]),'
# the limits
run "limit: orient, the last sum excluded" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (total + need < MaxDeterminantLimbs) {' 1
run "limit: orient, one limb more" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (total + need <= MaxDeterminantLimbs + 1) {' 1
run "limit: orient, the operands not counted" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (need <= MaxDeterminantLimbs) {' 1
run "limit: orient never on the stack" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (total + need <= 0) {' 1
run "limit: in-circle, the last sum excluded" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (total + need < MaxDeterminantLimbs) {' 2
run "limit: in-circle, one limb more" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (total + need <= MaxDeterminantLimbs + 1) {' 2
run "limit: in-circle, the operands not counted" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (need <= MaxDeterminantLimbs) {' 2
run "limit: in-circle never on the stack" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (total + need <= 0) {' 2
# the result and the high-water mark
run "orient: reports the stack path without a sign" $K 'sign = det3(f0, f1, f2, f3, f4, f5, f6, f7, f8, ref ar).Sign;' 'sign = 0; det3(f0, f1, f2, f3, f4, f5, f6, f7, f8, ref ar);'
run "hook: orient reports nothing used" $K 'used = scratch - ar.Remaining;' 'used = 0;' 4
run "hook: in-circle reports nothing used" $K 'used = scratch - ar.Remaining;' 'used = 0;' 5
# the call sites
run "call site Orient: two points exchanged" $A 'return orient_exact(Exact(p), Exact(q), Exact(r), i, j) * t.Sign;' 'return orient_exact(Exact(q), Exact(p), Exact(r), i, j) * t.Sign;'
run "call site Orient: the axes exchanged" $A 'return orient_exact(Exact(p), Exact(q), Exact(r), i, j) * t.Sign;' 'return orient_exact(Exact(p), Exact(q), Exact(r), j, i) * t.Sign;'
run "call site Orient: the triangle's sign dropped" $A 'return orient_exact(Exact(p), Exact(q), Exact(r), i, j) * t.Sign;' 'return orient_exact(Exact(p), Exact(q), Exact(r), i, j);'
run "call site InCircle: two points exchanged" $A 'return incircle_exact(Exact(p), Exact(q), Exact(r), Exact(s), i, j) * t.Sign;' 'return incircle_exact(Exact(q), Exact(p), Exact(r), Exact(s), i, j) * t.Sign;'
run "call site InCircle: the fourth point exchanged with the third" $A 'return incircle_exact(Exact(p), Exact(q), Exact(r), Exact(s), i, j) * t.Sign;' 'return incircle_exact(Exact(p), Exact(q), Exact(s), Exact(r), i, j) * t.Sign;'
run "call site InCircle: the triangle's sign dropped" $A 'return incircle_exact(Exact(p), Exact(q), Exact(r), Exact(s), i, j) * t.Sign;' 'return incircle_exact(Exact(p), Exact(q), Exact(r), Exact(s), i, j);'
run "orient_exact: the kernel's zero sent to the fallback" $A 'if (ExactLimbSigns.Orient2(a, b, c, i, j, out sign) == false)' 'if (ExactLimbSigns.Orient2(a, b, c, i, j, out sign) == false && sign != 0)'
run "orient_exact: the fallback's sign reversed" $A 'sign = ExactPoint3.Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W).Sign;' 'sign = -ExactPoint3.Det3(a[i], a[j], a.W, b[i], b[j], b.W, c[i], c[j], c.W).Sign;'
run "orient_exact: the fallback not evaluated" $A 'if (ExactLimbSigns.Orient2(a, b, c, i, j, out sign) == false)
                sign = ExactPoint3.Det3' 'if (ExactLimbSigns.Orient2(a, b, c, i, j, out sign))
                sign = ExactPoint3.Det3'
run "incircle_exact: the fallback's sign reversed" $A 'sign = incircle_big(p, q, r, d, i, j);' 'sign = -incircle_big(p, q, r, d, i, j);'
run "incircle_exact: the fallback not evaluated" $A 'if (ExactLimbSigns.InCircle(p, q, r, d, i, j, out sign) == false)
                sign = incircle_big' 'if (ExactLimbSigns.InCircle(p, q, r, d, i, j, out sign))
                sign = incircle_big'
echo "hand mutants D finished"
