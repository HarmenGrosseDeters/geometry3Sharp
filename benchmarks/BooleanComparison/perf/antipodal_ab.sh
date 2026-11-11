#!/bin/bash
# A/B of the open-operand classification (BooleanComparison antipodal-ab): the Antipodal Method with the switch at
# 1000 boundary segments against fast winding numbers everywhere, on the 21 open models of the Lévy samples (#385
# pairing, cases from `BooleanComparison export`), alone on the machine. usage: bash antipodal_ab.sh <tool dir> <cases dir> <out>
set -uo pipefail
T=$(cd "$1" && pwd); C=$(cd "$2" && pwd); O=$3; touch "$O"
for id in 100030 102815 1017015 107554 109088 112535 112536 116062 118289 118923 124526 122796 113906 1087141 1087144 1088051 1088054 1088138 112798 117959 1005586; do
  grep -q "\"case\":\"$id\"" "$O" && continue
  timeout 3600 dotnet "$T/BooleanComparison.dll" antipodal-ab "$C/$id" >> "$O" 2> /dev/null || echo "{\"case\":\"$id\",\"error\":\"exit\"}" >> "$O"
done
echo done
