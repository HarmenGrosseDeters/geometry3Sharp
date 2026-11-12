#!/bin/bash
# Hand mutants of the monomial pass and of its call sites: each is applied, the kernels are regenerated, and
# GeneratedDoublePredicatesTests must fail (a surviving mutant prints SURVIVED). Run in the repository root on a clean
# tree of the PR branch; restores the files afterwards.
set -u
G=tools/PredicateCompiler/Monomials.cs
E=math/ExactPredicates.cs
K=math/Generated/DoublePredicates.cs
cp $G /tmp/Monomials.cs.orig; cp $E /tmp/ExactPredicates.cs.orig; cp $K /tmp/DoublePredicates.cs.orig
run() {   # name, file, python replace expression (old, new)
    name="$1"; file="$2"
    python3 - "$file" "$3" "$4" <<'PY'
import sys
p, old, new = sys.argv[1:4]
s = open(p).read()
assert s.count(old) >= 1, "mutant does not apply: " + old
open(p, "w").write(s.replace(old, new, 1))
PY
    if [ $? -ne 0 ]; then echo "NOT APPLIED $name"; return; fi
    dotnet run --project tools/PredicateCompiler -c Release -- --dcode tools/PredicateCompiler/doubles.spec $K > /dev/null 2>&1
    out=$(dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~GeneratedDoublePredicatesTests&FullyQualifiedName!~IsUpToDate&FullyQualifiedName!~DesignDocTable" 2>&1 | grep -E "Passed!|Failed!| error " | head -3 | cut -c1-160)
    case "$out" in *"Failed!"*|*" error "*) echo "killed   $name: $(echo "$out" | head -1)";; *) echo "SURVIVED $name: $out";; esac
    cp /tmp/Monomials.cs.orig $G; cp /tmp/ExactPredicates.cs.orig $E; cp /tmp/DoublePredicates.cs.orig $K
}
run "a monomial dropped" $G 'foreach (var mono in p.Monomials) {
                string text' 'foreach (var mono in p.Monomials.Skip(1)) {
                string text'
run "a monomial's sign flipped" $G '{(mono.sign < 0 ? "true" : "false")});     //' '{(mono.sign < 0 && mono != p.Monomials[0] ? "true" : "false")});     //'
run "one limb less" $G 'mp.ExtraLimbs = (MantissaBits * mp.Degree + mp.GuardBits + 63 + 63) / 64;' 'mp.ExtraLimbs = (MantissaBits * mp.Degree + mp.GuardBits + 63 + 63) / 64 - 1;
                if (mp.Degree > 0) { result.Add(mp); continue; }'
run "the shift's carried bits off by one" $G '| (r{inv(k - 1)} >> (64 - s));\n");' '| (r{inv(k - 1)} >> (63 - s));\n");'
run "the top limb of the shift dropped" $G 'sb.Append($"                t{inv(d)} = r{inv(d - 1)} >> (64 - s);\n            }}\n");' 'sb.Append($"                t{inv(d)} = 0;\n            }}\n");'
run "the exponent base not subtracted" $G '.Select(k => $"e[i{inv(k)}]")) + " - b;\n");' '.Select(k => $"e[i{inv(k)}]")) + " - b + 64;\n");'
run "the carry of an addition dropped" $G 'sb.Append("            hi += (x < carry) ? 1UL : 0UL;\n");' 'sb.Append("            hi += 0UL;\n");'
run "the carry beyond the monomial dropped" $G 'sb.Append("                a[k] += 1;\n");' 'sb.Append("                a[k] += 0;\n");'
run "the mantissa signs ignored" $G '.Select(k => $"(neg >> i{inv(k)})")) + ") & 1) != 0) != minus;\n");' '.Select(k => $"(neg >> i{inv(k)})")) + ") & 0) != 0) != minus;\n");'
run "the hidden bit not set" $G 'sb.Append("            if (ex == 0)\n                ex = 1;\n            else\n                f |= 1UL << 52;\n");' 'sb.Append("            if (ex == 0)\n                ex = 1;\n");'
run "subnormal exponent" $G 'sb.Append("            if (ex == 0)\n                ex = 1;\n            else' 'sb.Append("            if (ex == 0)\n                ex = 0;\n            else'
run "a product limb's carry dropped" $G 'sb.Append("            hi += (lo < carry) ? 1UL : 0UL;\n");' 'sb.Append("            hi += 0UL;\n");'
run "the comparison from the low limb" $G 'for (int i = limbs - 1; i >= 0; --i)' 'for (int i = 0; i < limbs; ++i)'
run "the accumulators not cleared" $G 'sb.Append("            for (int i = 0; i < 2 * limbs; ++i)\n                p[i] = 0;\n");' 'sb.Append("            for (int i = 0; i < limbs; ++i)\n                p[i] = 0;\n");'
run "Orient3d call site: arguments swapped" $E 'return DoublePredicates.Orient3d(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z, d.x, d.y, d.z);' 'return DoublePredicates.Orient3d(b.x, b.y, b.z, a.x, a.y, a.z, c.x, c.y, c.z, d.x, d.y, d.z);'
run "Orient2d call site: sign" $E 'return DoublePredicates.Orient2d(a.x, a.y, b.x, b.y, c.x, c.y);' 'return -DoublePredicates.Orient2d(a.x, a.y, b.x, b.y, c.x, c.y);'
run "InCircle call site: back to zero" $E 'return DoublePredicates.InCircle(a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y);' 'return 0;'
