#!/bin/bash
# Hand mutants of ExactLimbSigns and of its call sites in ExactMeshBoolean (candidate B): each is applied in the
# worktree given as $1 (a clean, committed tree of the PR branch), ExactLimbSignsTests and ExactBooleanLimbSignsTests
# (and, for the call sites the two do not reach, the tests named in the PR) must fail; a surviving mutant prints
# SURVIVED; the files are restored afterwards. $2 (optional): only the mutants whose name contains it.
set -u
cd "$1"
ONLY="${2:-}"
K=math/ExactLimbSigns.cs
F=math/FixedPoint3.cs
B=mesh_ops/ExactMeshBoolean.cs
FILTER="FullyQualifiedName~ExactLimbSignsTests|FullyQualifiedName~ExactBooleanLimbSignsTests"
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
    out=$(dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "$FILTER" 2>&1 | grep -E "Passed!|Failed!| error |\[FAIL\]|Test Run Aborted" | grep -oE "Passed!.*|Failed!.*| error .*|[A-Za-z0-9_.]+\([^\[]{0,40}|\[FAIL\]|Test Run Aborted" | head -6 | cut -c1-120 | tr '\n' ' ')
    case "$out" in *"Failed!"*|*" error "*|*"[FAIL]"*|*"Test Run Aborted"*) echo "killed   $name: $out";; *) echo "SURVIVED $name: $out";; esac
    git checkout -q -- $K $F $B
}
# the arithmetic
run "plane side: the D W term added" $K 'int sign = FixedArith.Sub(s, FixedArith.Mul(fd, fw, ref ar), ref ar).Sign;' 'int sign = FixedArith.Add(s, FixedArith.Mul(fd, fw, ref ar), ref ar).Sign;'
run "plane side: the C Z term dropped" $K 's = FixedArith.Add(s, FixedArith.Mul(fc, fz, ref ar), ref ar);' 's = FixedArith.Add(s, FixedArith.Mul(fc, fc, ref ar), ref ar);'
run "integer-point side: the D term dropped" $K 'int sign = FixedArith.Sub(s, fd, ref ar).Sign;' 'int sign = s.Sign;'
run "dot: the third product dropped" $K 'int sign = FixedArith.Add(s, FixedArith.Mul(fc, fz, ref ar), ref ar).Sign;' 'int sign = s.Sign;'
run "load: a negative operand not negated" $K 'ulong carry = 1;' 'ulong carry = 0;'
run "load: the sign lost" $K 'return new FixedInt(dst, len, sign < 0);' 'return new FixedInt(dst, len, false);'
run "load: one limb too few" $K 'static int limbs_of(BigInteger v) { return (v.GetByteCount(false) + 7) >> 3; }' 'static int limbs_of(BigInteger v) { return (v.GetByteCount(false)) >> 3; }'
run "load: no sign extension" $K 'bytes.Fill(sign < 0 ? (byte)0xFF : (byte)0);' 'bytes.Fill((byte)0);'
run "det3: a minor's sign" $K 'FixedInt t2 = FixedArith.Mul(b, FixedArith.Sub(FixedArith.Mul(d, i, ref ar), FixedArith.Mul(f, g, ref ar), ref ar), ref ar);' 'FixedInt t2 = FixedArith.Mul(b, FixedArith.Add(FixedArith.Mul(d, i, ref ar), FixedArith.Mul(f, g, ref ar), ref ar), ref ar);'
run "Det4: no alternating sign" $K 'det = (col % 2 == 0) ? FixedArith.Add(det, term, ref ar) : FixedArith.Sub(det, term, ref ar);' 'det = FixedArith.Add(det, term, ref ar);'
run "Det4: a wrong column of a minor" $K 'int c0 = (col == 0) ? 1 : 0, c1 = (col <= 1) ? 2 : 1, c2 = (col <= 2) ? 3 : 2;' 'int c0 = (col == 0) ? 1 : 0, c1 = (col <= 1) ? 2 : 1, c2 = 3;'
# the scratch space
run "scratch: sides, 3 total" $K 'return 4 * total + 16;' 'return 3 * total + 16;'
run "scratch: sides, total only" $K 'return 4 * total + 16;' 'return total;'
run "scratch: Det4, the first row" $K 'return 8L * a + 28L' 'return 4L * a + 28L'
run "scratch: Det4, the second row" $K '28L * b' '20L * b'
run "scratch: Det4, the other rows" $K '64L * ((long)c + d)' '56L * ((long)c + d)'
run "scratch: Det4, the fourth row not counted" $K '64L * ((long)c + d)' '64L * ((long)c + c)'
run "scratch: Det4, no constant" $K '((long)c + d) + 96;' '((long)c + d);'
run "scratch: Det4, a row's widest entry (first row)" $K 'int wa = Math.Max(Math.Max(n[0], n[1]), Math.Max(n[2], n[3]))' 'int wa = Math.Max(n[0], n[1])'
run "scratch: Det4, a row's widest entry (fourth row)" $K 'wd = Math.Max(Math.Max(n[12], n[13]), Math.Max(n[14], n[15]));' 'wd = Math.Max(n[12], n[13]);'
# the stack limits (S1, S2)
run "limit: plane side always BigInteger" $K 'if (total <= MaxOperandLimbs) {' 'if (total <= 0) {' 0
run "limit: plane side, the last sum excluded" $K 'if (total <= MaxOperandLimbs) {' 'if (total < MaxOperandLimbs) {' 0
run "limit: plane side, one limb more" $K 'if (total <= MaxOperandLimbs) {' 'if (total <= MaxOperandLimbs + 1) {' 0
run "limit: integer-point side, the last sum excluded" $K 'if (total <= MaxOperandLimbs) {' 'if (total < MaxOperandLimbs) {' 1
run "limit: integer-point side, one limb more" $K 'if (total <= MaxOperandLimbs) {' 'if (total <= MaxOperandLimbs + 1) {' 1
run "limit: dot, the last sum excluded" $K 'if (total <= MaxOperandLimbs) {' 'if (total < MaxOperandLimbs) {' 2
run "limit: dot, one limb more" $K 'if (total <= MaxOperandLimbs) {' 'if (total <= MaxOperandLimbs + 1) {' 2
run "limit: plane side on the widest operand, not the sum" $K 'if (total <= MaxOperandLimbs) {' 'if (Math.Max(Math.Max(Math.Max(na, nb), Math.Max(nc, nd)), Math.Max(Math.Max(nx, ny), Math.Max(nz, nw))) <= MaxOperandLimbs / 8) {' 0
run "limit: Det4, the last sum excluded" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (total + need < MaxDeterminantLimbs) {'
run "limit: Det4, three limbs more" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (total + need <= MaxDeterminantLimbs + 3) {'
run "limit: Det4, the operands not counted" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (need <= MaxDeterminantLimbs) {'
run "limit: Det4 always BigInteger" $K 'if (total + need <= MaxDeterminantLimbs) {' 'if (total + need <= 0) {'
# the high-water mark (S4)
run "hook: plane side reports nothing used" $K 'used = scratch - ar.Remaining;' 'used = 0;' 0
run "hook: integer-point side reports nothing used" $K 'used = scratch - ar.Remaining;' 'used = 0;' 1
run "hook: dot reports nothing used" $K 'used = scratch - ar.Remaining;' 'used = 0;' 2
run "hook: Det4 reports nothing used" $K 'used = scratch - ar.Remaining;' 'used = 0;' 3
run "hook: the arena's remaining limbs" $F 'public int Remaining { get { return (int)(end - cur); } }' 'public int Remaining { get { return 0; } }'
# the call sites
run "call site local_side / side: the sign reversed" $B 'ExactLimbSigns.PlaneSide(U.P.A, U.P.B, U.P.C, U.P.D, P.X, P.Y, P.Z, P.W)' '-ExactLimbSigns.PlaneSide(U.P.A, U.P.B, U.P.C, U.P.D, P.X, P.Y, P.Z, P.W)'
FILTER="FullyQualifiedName~ExactBooleanOpenInputTests"
run "call site interior_side: the sign reversed" $B 'ExactLimbSigns.PlaneSide(t1.P.A, t1.P.B, t1.P.C, t1.P.D, X.X, X.Y, X.Z, X.W)' '-ExactLimbSigns.PlaneSide(t1.P.A, t1.P.B, t1.P.C, t1.P.D, X.X, X.Y, X.Z, X.W)'
FILTER="FullyQualifiedName~ExactMeshBooleanTestsTraced"
run "call site carried_crossings: a dot's operands" $B 'ExactLimbSigns.Dot(sp.NX, sp.NY, sp.NZ, U.NX, U.NY, U.NZ)' 'ExactLimbSigns.Dot(sp.NX, sp.NY, sp.NZ, U.NX, U.NZ, U.NY)'
FILTER="FullyQualifiedName~ExactLimbSignsTests|FullyQualifiedName~ExactBooleanLimbSignsTests"
run "call site orient3d: two points exchanged" $B 'return ExactLimbSigns.Det4(a, b, c, d);' 'return ExactLimbSigns.Det4(b, a, c, d);'
echo "hand mutants B finished"
