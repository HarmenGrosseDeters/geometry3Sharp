# Exact Boolean 2.0 final run

Builds: master 1fd946aa8a3c7c0cf15f4c9cddbe06449254668d; base385 cd7fb8abba0af09d5725b60c700dfec95234e7ad

## Phase `target`

| class | models | ops | clean | empty | unclean | failures | median s | p95 s | max s | over target | of which documented exceptions |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| target | 5413 | 16239 | 15355 | 412 | 472 | 0 | 0.083 | 0.778 | 15.622 | 0 | 0 |

target: unclean:degenerate triangles 432, unclean:self-intersections 40

#385 baseline (cd7fb8ab, another machine): 16239 ops, 81 unclean; timing over all classes p95 4.502 s, max 281.956 s. This phase: 16239 ops, 472 unclean, p95 0.778 s, max 15.622 s.

## Phase `control`

| class | models | ops | clean | empty | unclean | failures | median s | p95 s | max s | over target | of which documented exceptions |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| target | 5413 | 16239 | 15746 | 412 | 81 | 0 | 0.100 | 0.969 | 35.653 | 11 | 0 |

target: unclean:self-intersections 50, unclean:degenerate triangles 31

target, over max(5 s, 20 us x pieces), not a documented exception (11):

| id | op | s | pieces | target s | rounding s | verdict |
|---|---|---:|---:|---:|---:|---|
| 519511 | union | 35.65 | 1147764 | 22.96 | 13.17 | clean |
| 119829 | union | 26.30 | 260444 | 5.21 | 25.10 | unclean:degenerate triangles |
| 119830 | union | 26.18 | 270220 | 5.40 | 24.92 | unclean:degenerate triangles |
| 519511 | difference | 25.08 | 1147764 | 22.96 | 3.56 | clean |
| 519511 | intersection | 23.79 | 1147764 | 22.96 | 1.52 | clean |
| 1517923 | union | 10.09 | 280656 | 5.61 | 2.99 | clean |
| 1517923 | difference | 8.09 | 280656 | 5.61 | 2.08 | clean |
| 1517923 | intersection | 7.33 | 280656 | 5.61 | 0.98 | clean |
| 110044 | union | 5.88 | 88556 | 5.00 | 1.13 | clean |
| 119829 | difference | 5.83 | 260444 | 5.21 | 5.14 | unclean:degenerate triangles |
| 119830 | difference | 5.81 | 270220 | 5.40 | 5.13 | unclean:degenerate triangles |

#385 baseline (cd7fb8ab, another machine): 16239 ops, 81 unclean; timing over all classes p95 4.502 s, max 281.956 s. This phase: 16239 ops, 81 unclean, p95 0.969 s, max 35.653 s.

## Final against the #385 baseline build on the same machine (target class)

16239 operations timed in both: total 4324 s -> 3360 s (-22.3 %); per operation ratio median 0.85, p95 1.19.

| | baseline cd7fb8ab | final |
|---|---:|---:|
| median s | 0.100 | 0.083 |
| p95 s | 0.969 | 0.778 |
| max s | 35.653 | 15.622 |
| over max(5 s, 20 us x pieces) | 11.000 | 0.000 |
| unclean | 81.000 | 472.000 |

unclean in the baseline, not in the final: 15; unclean in the final, not in the baseline: 406  (100344 intersection, 100606 intersection, 100831 difference, 100831 intersection, 101954 intersection, 101955 intersection, 1021166 difference, 102249 difference, 102250 difference, 102250 intersection, 102250 union, 102296 difference, 102296 intersection, 103194 intersection, 1036402 difference, 1036402 intersection, 1036402 union, 1043471 intersection, 104563 difference, 104563 intersection, 1053875 intersection, 105799 intersection, 105862 intersection, 106100 intersection, 106557 intersection, 1080516 intersection, 108147 intersection, 110213 intersection, 110699 intersection, 111240 intersection)

