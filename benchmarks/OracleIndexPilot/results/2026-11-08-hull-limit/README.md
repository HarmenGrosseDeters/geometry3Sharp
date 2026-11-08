# Former-timeout meshes after the hull limit

All 18 cached meshes from the point/edge campaign were checked with `OracleIndexPilot edge-hulls <mesh>` at unbounded commit `0d08145b06afab024e99837a8762acae547c76c0` and bounded commit `b58b007800015bf716136c591a8be1ce1f58d4dd`. Three serial paired rounds per case, with alternating before/after order. Each mesh is re-hashed before a pair. The records bind the original selection, complete runtime files and controller source; no meshes or foreign fixture contents are committed.

All 54 pairs have identical query counts, hits, fingerprints **and leaf candidate counts**. There are zero proper crossings in these meshes. This checks the complete edge component, including tree construction; parsing and initial mesh/key preparation are excluded from the component timer and included in the separately recorded process time. This is not a new full-oracle verdict campaign.

The blanket claim that the current PR head does not slow any of these cases is **not established**: median case ratios range from 0.942 to 1.107, with a median case ratio of 1.020. The three 1044 cases are 8–11% slower. Other cases improve or have smaller changes. These are three local repetitions per case, without host activity instrumentation or confidence intervals, not a guarantee of isolated hardware.

| Case | Edge queries | Before median, s | Bounded head median, s | Change |
|---|---:|---:|---:|---:|
| misc/TestBufferExternal-2/92/q30 | 504,702 | 6.207 | 5.854 | -5.7% |
| misc/TestBufferExternal-2/92/q25 | 504,702 | 6.003 | 5.732 | -4.5% |
| misc/stmlf-20061020/10/q20 | 502,674 | 6.927 | 7.275 | +5.0% |
| misc/stmlf-20061020/10/q25 | 502,674 | 7.078 | 7.208 | +1.8% |
| robust/overlay/TestOverlay-stmlf/1044/q20 | 501,497 | 7.675 | 8.298 | +8.1% |
| misc/stmlf-20061020/10/q30 | 502,674 | 7.024 | 7.157 | +1.9% |
| robust/overlay/TestOverlay-stmlf/1044/q25 | 501,497 | 7.807 | 8.640 | +10.7% |
| robust/overlay/TestOverlay-stmlf/1044/q30 | 501,497 | 7.908 | 8.603 | +8.8% |
| robust/overlay/TestOverlay-stmlf/1771/q20 | 597,733 | 9.886 | 10.656 | +7.8% |
| robust/overlay/TestOverlay-stmlf/1056/q30 | 600,018 | 17.212 | 17.251 | +0.2% |
| robust/overlay/TestOverlay-stmlf/1771/q25 | 597,733 | 10.102 | 10.520 | +4.1% |
| robust/overlay/TestOverlay-stmlf/1771/q30 | 597,733 | 10.534 | 10.640 | +1.0% |
| robust/overlay/TestOverlay-stmlf/473/q20 | 502,674 | 7.419 | 7.394 | -0.3% |
| robust/overlay/TestOverlay-stmlf/473/q25 | 502,674 | 6.979 | 7.259 | +4.0% |
| robust/overlay/TestOverlay-stmlf/548/q20 | 513,004 | 8.423 | 8.705 | +3.4% |
| robust/overlay/TestOverlay-stmlf/473/q30 | 502,674 | 7.736 | 7.283 | -5.8% |
| robust/overlay/TestOverlay-stmlf/548/q25 | 513,004 | 8.654 | 8.834 | +2.1% |
| robust/overlay/TestOverlay-stmlf/548/q30 | 513,004 | 8.578 | 8.635 | +0.7% |

## Attribution control on 1044/q30

Three rotated serial rounds on mesh SHA-256 `56de8a1b8f6969f2fba35e9950b7bee0a6b746da18df0a00a694f464bacc6521`, with identical queries/hits/fingerprints throughout:

| Variant | Median component seconds |
|---|---:|
| Original unbounded 0d08145b | 8.104 |
| Current bounded b58b0078 | 8.443 |
| b58b0078, only HullLimit changed to int.MaxValue | 8.902 |
| b58b0078, only the first-loop pruneTests++ omitted | 8.541 |

The cap is beneficial against the **same current code without the cap** on this diagnostic mesh. It does not account for the entire old-to-current difference. The last control omits only `pruneTests++`; it **retains** the second-loop `pruneTests += 2`, so it does not remove all counting overhead. Its raw label is `uncounted` for historical command fidelity. Do not infer that all instrumentation overhead was excluded. Runtime/source hashes and these exact source substitutions are in `overhead-metadata.json`. No diagnostic variant is proposed as production code.

## Reproduction and verification

Use `../../compare_hull_limit.py --help` and `../../diagnose_hull_overhead.py --help`. The command file uses source/cache paths supplied through environment variables. The original local selection JSONL has SHA-256 `182f4327daa49fa2d33a75316cc940a9ba859432611e7317abf9f4531ee4be8f` and contains the producer mesh paths; it is the same selection bound to the prior edge campaign. Keep the frozen producer and variant binaries unchanged; build each comparison revision into a separate artifacts directory. Run no builds, other test suites or other measurements beside the timing pair. Resume reruns an interrupted pair or four-variant round; completed records are retained only when metadata matches.

`metadata.json`, `pairs-01.jsonl` through `pairs-03.jsonl`, `summary.json`, and the two `overhead-*` files contain the numeric evidence. The old unbounded and new bounded component self-tests, Python controller tests, all-target/net48 builds and API-validating packs are checked before publishing this follow-up.
