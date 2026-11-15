#!/bin/bash
# tool tests, library build (all targets) and a filtered test run in the worktree $1 with the test filter $2
cd "$1" || exit 1
for t in test_ci_scope test_trx_durations test_changed_line_coverage; do
  echo "$t: $(python3 -m unittest discover -s tools -p "$t.py" 2>&1 | tail -1)"
done
for t in test_select_shards test_baseline test_incremental test_suggest_split; do
  echo "$t: $(cd tests/mutation && python3 $t.py 2>&1 | tail -1)"
done
dotnet build geometry3Sharp_netstandard.csproj -c Release 2>&1 | grep -E "Error\(s\)|Build succeeded" | sort | uniq -c
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "$2" 2>&1 | grep -E " error |\[FAIL\]|Passed!|Failed!" | head -10
dotnet build tools/PredicateCompiler -c Release 2>&1 | grep -E "Error\(s\)"
echo quick_check finished
