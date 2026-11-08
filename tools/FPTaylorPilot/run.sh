#!/usr/bin/env bash
# Run a single bounded example; accept only an explicit finite exact-error result.
set -euo pipefail
if [ "$#" -ne 2 ]; then echo "usage: $0 <FPTaylor-source-directory> <input-file>" >&2; exit 2; fi
base=$(cd "$1" && pwd)
input=$(realpath "$2")
if [ ! -x "$base/fptaylor" ]; then echo "FPTaylor executable missing: $base/fptaylor" >&2; exit 2; fi
export FPTAYLOR_BASE="$base"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
result="$work/result.log"
cd "$work"
"$base/fptaylor" --opt bb-eval --abs-error true --opt-exact true "$input" > "$result" 2>&1 || { cat "$result" >&2; exit 1; }
cat "$result"
python3 - "$result" <<'PY'
from decimal import Decimal, InvalidOperation
import re, sys
text = open(sys.argv[1], encoding='utf-8').read()
values = re.findall(r'^Absolute error \(exact\):\s+(\S+)', text, re.M)
if len(values) != 1:
    sys.exit('expected exactly one absolute error result')
try:
    value = Decimal(values[0])
except InvalidOperation:
    sys.exit('invalid error result')
if not value.is_finite() or value < 0:
    sys.exit('error result is not finite and non-negative')
PY
