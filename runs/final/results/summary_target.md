# Exact Boolean 2.0 final run

Builds: master 1fd946aa8a3c7c0cf15f4c9cddbe06449254668d; base385 cd7fb8abba0af09d5725b60c700dfec95234e7ad

## Phase `target`

| class | models | ops | clean | empty | unclean | failures | median s | p95 s | max s | over target | of which documented exceptions |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| target | 5413 | 16239 | 15355 | 412 | 472 | 0 | 0.083 | 0.778 | 15.622 | 0 | 0 |

target: unclean:degenerate triangles 432, unclean:self-intersections 40

#385 baseline (cd7fb8ab, another machine): 16239 ops, 81 unclean; timing over all classes p95 4.502 s, max 281.956 s. This phase: 16239 ops, 472 unclean, p95 0.778 s, max 15.622 s.

