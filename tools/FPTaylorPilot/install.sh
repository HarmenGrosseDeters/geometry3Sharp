#!/usr/bin/env bash
# Install a pinned FPTaylor toolchain entirely under the requested directory.
set -euo pipefail
if [ "$#" -ne 1 ]; then echo "usage: $0 <installation-directory>" >&2; exit 2; fi
mkdir -p "$1"
root=$(cd "$1" && pwd)
fetch() {
    local url=$1 name=$2 hash=$3
    if [ ! -f "$root/$name" ]; then curl --fail --location --retry 2 "$url" -o "$root/$name"; fi
    echo "$hash  $root/$name" | sha256sum -c -
}
fetch https://github.com/ocaml/ocaml/archive/refs/tags/4.14.2.tar.gz ocaml-4.14.2.tar.gz c2d706432f93ba85bd3383fa451d74543c32a4e84a1afaf3e8ace18f7f097b43
fetch https://github.com/ocaml/num/archive/refs/tags/v1.5.tar.gz num-1.5.tar.gz 7ae07c8f5601e2dfc5008a62dcaf2719912ae596a19365c5d7bdf2230515959a
fetch https://github.com/soarlab/FPTaylor/archive/b5a77cae348400f21f83512210d9f43c4bffb381.tar.gz fptaylor.tar.gz 262cb1fc723ce17f0414a85a31e29749c0041d62e0e3919d0a67cc715f8005ed
for src in ocaml ocaml-4.14.2 num-1.5 FPTaylor-b5a77cae348400f21f83512210d9f43c4bffb381; do
    if [ -e "$root/$src" ]; then echo "Source directory already exists: $root/$src; use a fresh directory" >&2; exit 2; fi
done
tar -xzf "$root/ocaml-4.14.2.tar.gz" -C "$root"
tar -xzf "$root/num-1.5.tar.gz" -C "$root"
tar -xzf "$root/fptaylor.tar.gz" -C "$root"
(cd "$root/ocaml-4.14.2" && ./configure --prefix="$root/ocaml" && make -j"${JOBS:-2}" world.opt && make install)
export PATH="$root/ocaml/bin:$PATH"
# num's upstream install target requires ocamlfind, which FPTaylor itself does not.
(cd "$root/num-1.5" && make -j"${JOBS:-2}")
numlib="$root/ocaml/lib/ocaml"
cp "$root/num-1.5/src/"*.cmi "$root/num-1.5/src/nums.cma" "$root/num-1.5/src/nums.cmxa" "$root/num-1.5/src/nums.a" "$root/num-1.5/src/libnums.a" "$numlib/"
cp "$root/num-1.5/src/dllnums.so" "$numlib/stublibs/"
(cd "$root/FPTaylor-b5a77cae348400f21f83512210d9f43c4bffb381" && make fptaylor-simple-interval)
printf 'FPTaylor installed: %s\n' "$root/FPTaylor-b5a77cae348400f21f83512210d9f43c4bffb381/fptaylor"
