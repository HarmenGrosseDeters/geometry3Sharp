| variant | method | pairs | face contact | operations | ok | failed / crash / timeout | clean | empty (volume-consistent) | unclean | max volume error | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| mirror | cgal_epeck | 30 | 0 | 90 | 81 | 9 | 21 | 0 | bowties 15, degenerate triangles 45 | 2.14e-13 | 295 | 4416 |
| mirror | manifold | 30 | 0 | 90 | 90 | 0 | 30 | 0 | bowties 16, degenerate triangles 34, not closed 9, self-intersections 1 | 1.51e-14 | 53 | 245 |
| mirror | ours | 30 | 0 | 90 | 90 | 0 | 66 | 0 | bowties 15, not closed 9 | 9.38e-06 | 432 | 3216 |
