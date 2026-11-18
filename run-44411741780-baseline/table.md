| class | method | pairs | operations | success | clean | empty (volume-consistent) | not clean / not ok | max identity error | median ms | p90 ms | max ms | ops > 5 s | ops > 20 s |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | cgal_epeck | 100 | 300 | 300 | 225 | 24 | degenerate triangles 51 | 3.70e-14 | 60 | 204 | 495 | 0 | 0 |
| thingi-pairs | manifold | 1000 | 3000 | 3000 | 1757 | 228 | degenerate triangles 1015 | 3.55e-10 | 34 | 79 | 1186 | 0 | 0 |
| thingi-pairs | ours | 1000 | 3000 | 3000 | 2765 | 228 | degenerate triangles 7 | 4.58e-08 | 134 | 402 | 3961 | 0 | 0 |
| thingi-pairs-size | manifold | 100 | 300 | 300 | 41 | 17 | degenerate triangles 242 | 3.39e-08 | 449 | 1087 | 25510 | 6 | 3 |
| thingi-pairs-size | ours | 100 | 300 | 300 | 281 | 17 | degenerate triangles 2 | 1.95e-10 | 1795 | 3443 | 35187 | 8 | 3 |
