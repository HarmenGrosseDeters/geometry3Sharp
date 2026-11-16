| variant | method | pairs | face contact | operations | ok | failed / crash / timeout | clean | empty (volume-consistent) | unclean | max volume error | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ulp-shift | cgal_epeck | 30 | 0 | 90 | 84 | 6 | 0 | 0 | degenerate triangles 10, not closed 74 | 1.12e-14 | 858 | 3054 |
| ulp-shift | manifold | 30 | 0 | 90 | 90 | 0 | 67 | 4 | bowties 1, degenerate triangles 2, inside-out components 3, not closed 13 | 2.38e-13 | 45 | 179 |
| ulp-shift | ours | 30 | 0 | 90 | 90 | 0 | 59 | 30 | degenerate triangles 1 | 2.59e-09 | 798 | 3435 |
