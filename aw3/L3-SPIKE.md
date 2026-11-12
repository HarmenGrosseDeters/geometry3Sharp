# L3 spike: parallel gate-test batches, bit-identical (2026-11-12), stopped

Patch: `l3_spike.patch` on top of PR 0 (#651, branch claude/aw3-bench-phases). Design: the serial carving loop is unchanged; the
gate tests (rule-1 crossing, touch) of the queue's top gates are precomputed in parallel into a pool keyed by gate, each
result checked against the stamps of the cell and its neighbour when the carving pops the gate. Output hashes identical to
the serial run on every probe (100040, 75654, 48367), as designed.

Probe (100040, `BENCH_WRAP_GATEP=window,misses,min`, wrap ms, serial 2092):

| window, misses per batch, min new tests | wrap ms | batch ms | tests evaluated | used |
|---|---:|---:|---:|---:|
| 256, 8, 32 | 3278 | 1143 | 322008 | 142729 |
| 1024, 32, 128 | 3694 | 1634 | 156750 | 74383 |
| 4096, 64, 512 | 6300 | 3987 | 139234 | 59139 |
| 16384, 128, 2048 | 9817 | 7525 | 156738 | 49193 |

Serial tests that reach the oracle: about 359k (0.42 of 854756 pops). Coverage (tests taken from a batch): 40 % (100040),
37 % (75654), 36 % (48367): the carving front pops mostly gates pushed since the last batch, and a deeper window goes stale.
Bound with this coverage: 0.38 x 0.75 x 27.6 % (oracle + touch share) = 8 % of the pipeline at zero overhead (<= 1.085x),
against the 1.31x of phase 1, which assumed every test could run ahead. Stop rule applied.
