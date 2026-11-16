| variant | method | pairs | face contact | operations | ok | failed / crash / timeout | clean | empty (volume-consistent) | unclean | max volume error | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ulp-shift-few | cgal_epeck | 30 | 0 | 90 | 87 | 3 | 0 | 0 | degenerate triangles 14, not closed 73 | 1.46e-14 | 625 | 93228 |
| ulp-shift-few | manifold | 30 | 0 | 90 | 90 | 0 | 63 | 5 | bowties 1, degenerate triangles 4, inside-out components 1, not closed 15, self-intersections 1 | 5.27e-14 | 74 | 221 |
| ulp-shift-few | ours | 30 | 0 | 90 | 90 | 0 | 60 | 30 | – | 4.97e-08 | 792 | 10702 |
