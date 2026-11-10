# Open-class re-measure after #528 (100 sampled open models)

| | ops | clean | contacts | empty | unclean | failed |
|---|---|---|---|---|---|---|
| pre (79a2c1ab) | 300 | 50 | 32 | 5 | 213 | 0 |
| fix (293d0e3b) | 300 | 48 | 31 | 5 | 216 | 0 |

pre unclean by reason: not closed 205, degenerate triangles 5, self-intersections 2, inside-out components 1

fix unclean by reason: not closed 208, degenerate triangles 5, self-intersections 2, inside-out components 1

Verdict changed on 3 of 300 ops (pre -> fix):
- 106620 difference: clean -> unclean: not closed
- 133989 difference: clean -> unclean: not closed
- 1717686 intersection: contacts -> unclean: not closed

Transitions: clean -> unclean: not closed: 2, contacts -> unclean: not closed: 1

Timing sample: 60 ops of 20 models, both builds alone, 2 rounds interleaved (ms per op)

| | median | p95 | max | total s |
|---|---|---|---|---|
| pre | 688 | 11565 | 18600 | 140.4 |
| fix | 1480 | 29763 | 86633 | 472.6 |

fix / pre per op: median 2.02x, p95 4.53x, max 7.03x; total 3.37x
slowest fix ops: 1087144 union 18.6 -> 86.6 s, 1087144 difference 12.4 -> 81.6 s, 1087144 intersection 11.6 -> 81.3 s, 317215 union 11.6 -> 29.8 s, 317215 difference 8.1 -> 25.8 s

Run by the implementing session in its own 4-core container (2026-11-10): a seeded random sample (seed 20261110) of 100
of the 1474 open models of class.tsv, `job2.sh correct` on both builds (4 at a time, 1 core each), then `job2.sh
timing` of 20 of them (`sample_timing.py --per-stratum 4` over the pre rows), alone, both builds interleaved, 2
rounds. "pre" here is job2 on 79a2c1ab, not the rest phase. The 3 changed verdicts, checked with the per-triangle
winding oracle of ExactBooleanOpenPerPieceTests (0 triangles on the wrong side for both builds) and the area the
operation keeps by the other input's winding number at the input triangles: 106620 difference, pre keeps 129 of the
expected 41240 (the defect #528 fixes, clean by accident), fix 40610; 133989 difference and 1717686 intersection: the
same area, fix adds 20 and 1 triangles with 36 and 3 boundary edges where the open input's hole meets {w = 1/2}.
