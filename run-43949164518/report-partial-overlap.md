| variant | method | pairs | face contact | operations | ok | failed / crash / timeout | clean | empty (volume-consistent) | unclean | max volume error | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| partial-overlap | cgal_epeck | 24 | 5 | 72 | 72 | 0 | 61 | 4 | bowties 1, degenerate triangles 6 | 1.45e-14 | 24 | 385 |
| partial-overlap | manifold | 24 | 5 | 72 | 72 | 0 | 41 | 4 | degenerate triangles 27 | 2.31e-14 | 29 | 48 |
| partial-overlap | ours | 24 | 5 | 72 | 72 | 0 | 67 | 4 | bowties 1 | 9.61e-13 | 109 | 394 |

Placements refused as inexact or failed to load: 6 of 30 cases:
- partial-overlap_p0001_91452+73413: RuntimeError('inexact transform: translation not exact')
- partial-overlap_p0002_121868+200963: RuntimeError('inexact transform: translation not exact')
- partial-overlap_p0009_107439+514852: RuntimeError('inexact transform: translation not exact')
- partial-overlap_p0011_92836+49546: RuntimeError('inexact transform: embedding depth not exact')
- partial-overlap_p0023_44397+54467: RuntimeError('inexact transform: translation not exact')
- partial-overlap_p0028_472179+76057: RuntimeError('inexact transform: translation not exact')
