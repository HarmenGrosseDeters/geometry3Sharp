#!/usr/bin/env bash
# NativeAOT spike (docs/design/NATIVE_AOT_FEASIBILITY.md): publish the C ABI as a shared library, run the C caller's
# self test, and compare the result hashes of the native library with the managed (JIT) driver, bit for bit.
# usage: run_spike.sh [rid] [outdir]    (Linux and macOS; needs the .NET 10 SDK, the ILCompiler runtime pack and a C compiler)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
rid="${1:-linux-x64}"; out="${2:-$(mktemp -d)}"
case "$rid" in osx-*) ext=dylib;; win-*) ext=dll;; *) ext=so;; esac
dotnet publish "$here" -c Release -r "$rid" -p:PublishNative=true -o "$out/native"
dotnet build "$here/driver" -c Release -o "$out/driver"
lib="$out/native/g3native.$ext"; ls -l "$lib"
${CC:-cc} -O2 -Wall -o "$out/caller" "$here/c/caller.c" "$lib" -Wl,-rpath,"$out/native"
"$out/caller" selftest | tee "$out/selftest.txt"
grep -q "union        rc=0" "$out/selftest.txt" && grep -q "volume=1.875000" "$out/selftest.txt" && grep -q "volume=0.125000" "$out/selftest.txt" && grep -q "volume=0.875000" "$out/selftest.txt"
grep -q "bad index    rc=2" "$out/selftest.txt" && grep -q "max_pieces=3 rc=5" "$out/selftest.txt"
fail=0
for op in 0 1 2 imprint; do
  for n in 16 32 64; do
    a="$("$out/caller" bench "$op" "$n" 2 | grep -o "tris=[0-9]* hash=[0-9a-f]* reason='[^']*'")"
    b="$(dotnet "$out/driver/NativeAotDriver.dll" "$op" "$n" 2 | grep -o "tris=[0-9]* hash=[0-9a-f]* reason='[^']*'")"
    if [ "$a" = "$b" ]; then echo "op=$op n=$n identical: $a"; else echo "op=$op n=$n DIFFER: native [$a] managed [$b]"; fail=1; fi
  done
done
exit $fail
