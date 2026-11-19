| class | method | pairs | operations | success | clean | empty (volume-consistent) | not clean / not ok | max identity error | median ms | p90 ms | max ms | ops > 5 s | ops > 20 s |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | cgal_epeck | 100 | 300 | 300 | 225 | 24 | degenerate triangles 51 | 3.70e-14 | 53 | 151 | 485 | 0 | 0 |
| thingi-pairs | manifold | 900 | 2700 | 2700 | 1604 | 204 | degenerate triangles 892 | 3.55e-10 | 34 | 77 | 1612 | 0 | 0 |
| thingi-pairs | ours | 900 | 2700 | 2700 | 2496 | 204 | – | 4.58e-08 | 121 | 318 | 6097 | 1 | 0 |
| thingi-pairs-size | manifold | 95 | 285 | 285 | 41 | 17 | degenerate triangles 227 | 3.39e-08 | 418 | 1067 | 25702 | 6 | 3 |
| thingi-pairs-size | ours | 95 | 285 | 285 | 268 | 17 | – | 2.36e-10 | 1494 | 3019 | 23415 | 3 | 3 |
