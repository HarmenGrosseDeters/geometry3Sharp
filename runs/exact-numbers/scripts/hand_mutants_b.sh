#!/bin/bash
# Hand mutants of ExactLimbSigns and of its call sites in ExactMeshBoolean (candidate B): each is applied in the
# worktree given as $1 (a clean tree of the PR branch), ExactLimbSignsTests and ExactBooleanLimbSignsTests must fail
# (a surviving mutant prints SURVIVED); the files are restored afterwards.
set -u
cd "$1"
K=math/ExactLimbSigns.cs
B=mesh_ops/ExactMeshBoolean.cs
run() {   # name, file, old, new
    name="$1"; file="$2"
    python3 - "$file" "$3" "$4" <<'PY'
import sys
p, old, new = sys.argv[1:4]
s = open(p).read()
assert s.count(old) >= 1, "mutant does not apply: " + old
open(p, "w").write(s.replace(old, new, 1))
PY
    if [ $? -ne 0 ]; then echo "NOT APPLIED $name"; return; fi
    out=$(dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~ExactLimbSignsTests|FullyQualifiedName~ExactBooleanLimbSignsTests" 2>&1 | grep -E "Passed!|Failed!| error |\[FAIL\]|Test Run Aborted" | head -3 | cut -c1-160 | tr '\n' ' ')
    case "$out" in *"Failed!"*|*" error "*|*"[FAIL]"*|*"Test Run Aborted"*) echo "killed   $name: $out";; *) echo "SURVIVED $name: $out";; esac
    git checkout -q -- $K $B
}
run "plane side: the D W term added" $K 'return FixedArith.Sub(s, FixedArith.Mul(fd, fw, ref ar), ref ar).Sign;' 'return FixedArith.Add(s, FixedArith.Mul(fd, fw, ref ar), ref ar).Sign;'
run "plane side: the C Z term dropped" $K 's = FixedArith.Add(s, FixedArith.Mul(fc, fz, ref ar), ref ar);' 's = FixedArith.Add(s, FixedArith.Mul(fc, fc, ref ar), ref ar);'
run "dot: the third product dropped" $K 'return FixedArith.Add(s, FixedArith.Mul(fc, fz, ref ar), ref ar).Sign;' 'return s.Sign;'
run "load: a negative operand not negated" $K 'ulong carry = 1;' 'ulong carry = 0;'
run "load: the sign lost" $K 'return new FixedInt(dst, len, sign < 0);' 'return new FixedInt(dst, len, false);'
run "load: one limb too few" $K 'static int limbs_of(BigInteger v) { return (v.GetByteCount(false) + 7) >> 3; }' 'static int limbs_of(BigInteger v) { return (v.GetByteCount(false)) >> 3; }'
run "load: no sign extension" $K 'bytes.Fill(sign < 0 ? (byte)0xFF : (byte)0);' 'bytes.Fill((byte)0);'
run "det3: a minor's sign" $K 'FixedInt t2 = FixedArith.Mul(b, FixedArith.Sub(FixedArith.Mul(d, i, ref ar), FixedArith.Mul(f, g, ref ar), ref ar), ref ar);' 'FixedInt t2 = FixedArith.Mul(b, FixedArith.Add(FixedArith.Mul(d, i, ref ar), FixedArith.Mul(f, g, ref ar), ref ar), ref ar);'
run "Det4: no alternating sign" $K 'det = (col % 2 == 0) ? FixedArith.Add(det, term, ref ar) : FixedArith.Sub(det, term, ref ar);' 'det = FixedArith.Add(det, term, ref ar);'
run "Det4: a wrong column of a minor" $K 'int c0 = (col == 0) ? 1 : 0, c1 = (col <= 1) ? 2 : 1, c2 = (col <= 2) ? 3 : 2;' 'int c0 = (col == 0) ? 1 : 0, c1 = (col <= 1) ? 2 : 1, c2 = 3;'
run "Det4: the scratch space too small" $K 'int scratch = 164 * widest + 96;' 'int scratch = 100 * widest + 96;'
run "plane side: the scratch space too small" $K 'int scratch = 4 * total + 16;
                ulong* sc = stackalloc ulong[scratch];
                var ar = new FixedArena(sc, scratch);
                ulong* p = buf;
                FixedInt fa = load(a, p, na); p += na;
                FixedInt fb = load(b, p, nb); p += nb;
                FixedInt fc = load(c, p, nc); p += nc;
                FixedInt fd = load(d, p, nd); p += nd;' 'int scratch = total;
                ulong* sc = stackalloc ulong[scratch];
                var ar = new FixedArena(sc, scratch);
                ulong* p = buf;
                FixedInt fa = load(a, p, na); p += na;
                FixedInt fb = load(b, p, nb); p += nb;
                FixedInt fc = load(c, p, nc); p += nc;
                FixedInt fd = load(d, p, nd); p += nd;'
run "the stack limit ignored (always BigInteger)" $K 'if (total <= 8 * MaxLimbs) {' 'if (total <= 0) {'
run "call site local_side / side: the sign reversed" $B 'ExactLimbSigns.PlaneSide(U.P.A, U.P.B, U.P.C, U.P.D, P.X, P.Y, P.Z, P.W)' '-ExactLimbSigns.PlaneSide(U.P.A, U.P.B, U.P.C, U.P.D, P.X, P.Y, P.Z, P.W)'
run "call site interior_side: the sign reversed" $B 'ExactLimbSigns.PlaneSide(t1.P.A, t1.P.B, t1.P.C, t1.P.D, X.X, X.Y, X.Z, X.W)' '-ExactLimbSigns.PlaneSide(t1.P.A, t1.P.B, t1.P.C, t1.P.D, X.X, X.Y, X.Z, X.W)'
run "integer-point side: the D term dropped" $K 'return FixedArith.Sub(s, fd, ref ar).Sign;' 'return s.Sign;'
run "call site carried_crossings: a dot's operands" $B 'ExactLimbSigns.Dot(sp.NX, sp.NY, sp.NZ, U.NX, U.NY, U.NZ)' 'ExactLimbSigns.Dot(sp.NX, sp.NY, sp.NZ, U.NX, U.NZ, U.NY)'
run "call site orient3d: two points exchanged" $B 'return ExactLimbSigns.Det4(a, b, c, d);' 'return ExactLimbSigns.Det4(b, a, c, d);'
echo "hand mutants B finished"
