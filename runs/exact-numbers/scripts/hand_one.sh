#!/bin/bash
# Hand mutants of the monomial pass and of its call sites: each is applied, the kernels are regenerated, and
# GeneratedDoublePredicatesTests must fail (a surviving mutant prints SURVIVED). Run in the repository root on a clean
# tree of the PR branch; restores the files afterwards.
set -u
G=tools/PredicateCompiler/Monomials.cs
E=math/ExactPredicates.cs
K=math/Generated/DoublePredicates.cs
cp $G /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/data/Monomials.cs.orig; cp $E /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/data/ExactPredicates.cs.orig; cp $K /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/data/DoublePredicates.cs.orig
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
    out=$(dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~GeneratedDoublePredicatesTests&FullyQualifiedName!~IsUpToDate&FullyQualifiedName!~DesignDocTable" 2>&1 | grep -E "Passed!|Failed!| error |\[FAIL\]|Test Run Aborted" | head -3 | cut -c1-160)
    case "$out" in *"Failed!"*|*" error "*|*"[FAIL]"*|*"Test Run Aborted"*) echo "killed   $name: $(echo "$out" | head -1)";; *) echo "SURVIVED $name: $out";; esac
    cp /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/data/Monomials.cs.orig $G; cp /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/data/ExactPredicates.cs.orig $E; cp /tmp/claude-0/-home-user-geometry3Sharp/49db7f38-a9ab-58f1-b2eb-8536a50f6cdc/scratchpad/exactnum/data/DoublePredicates.cs.orig $K
}
run "the carry beyond the monomial dropped" $G 'sb.Append("                *c += 1;\n");' 'sb.Append("                *c += 0;\n");'
