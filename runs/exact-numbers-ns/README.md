# netstandard2.0 probe (candidate C, carried limbs), 5 rounds, one thread

Throwaway probe for docs/design/EXACT_LIMB_SIGNS.md section 7a. Not for a PR.

- Library: geometry3Sharp_netstandard.csproj, netstandard2.0 target, Release, from master cfe86fc with
  `c/probe_ns_port_and_carried.diff` (the section 7 carried-limbs patch plus the netstandard2.0 port:
  `ToByteArray` load, byte-array BigInteger from limbs, `limbs_of` via `ToByteArray().Length`, no `#if NET`).
- Host: .NET 10.0.115 SDK (runtime 10.0.15), Ubuntu 24.04, 4 cores. The harness `Probe` references the
  netstandard2.0 DLL (`SetTargetFramework`); its md5 matches the netstandard2.0 build.
- Settings: `base` (EXACT_PROTO unset: the section 2 limb kernels on netstandard2.0, no carried limbs) and
  `carcen` (EXACT_PROTO=carried,centroid).
- Strata: `t107554` and `t42327` (rotated copy, union/intersection/difference), `npairs` (first 20 pairs of
  thingi-pairs, three operations). One thread: taskset core 0, EXACT_SERIAL=1, gcConcurrent off,
  DOTNET_TieredPGO=0, EXACT_TIMERS=0.
- Rounds: 5, the order of the two settings rotated. The first run of round 1 died with the agent's turn (about
  10:40 UTC); its incomplete files were removed, and round 1 was rerun from the missing strata (base 42327 was
  complete and was rerun too). Rows of all rounds are in `results/ns`.
- Meshes are fetched at run time (`fetch_cases.py`), none committed. `data/` holds the two tail case lists;
  the mesh paths in them point to the scratch directory of the run.
- Analysis: `analysis.txt` (analyze_c7.py, one thread).
- Not measured: the 4-core strata of c7_probe.sh and the `ncopy` stratum (not part of this question).
