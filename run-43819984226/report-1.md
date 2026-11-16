### 1 thread(s)

| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 5.33 | 12.67 | 26.00 | 72/81 | 9 | 0 | 0 | 0 / 0 | 66/81 | 9 | degenerate triangles 6 | 0 | 0 / 0 |
| 10k-100k | 71 | 213 | 5.19 | 9.05 | 13.56 | 198/213 | 15 | 0 | 0 | 0 / 0 | 155/213 | 15 | degenerate triangles 43 | 0 | 0 / 0 |

All buckets: median ratio 5.24, p90 9.50, max 26.00 over 294 operations.
All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **yes**.

Five worst ratios (ours phases in ms):

| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |
|---|---|---|---|---|---|---|---|
| p0521_271864+77930 | intersection | 1k-10k | 26 | 1 | 26.00 | 22 | intersections 13, intersections.cull 7, classification 5, merge 3, intersections.trees 2, intersections.pairs 2, merge.crossings 2, input 1, setup.triangles 1, setup 1, setup.points 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0521_271864+77930 | union | 1k-10k | 72 | 3 | 24.00 | 5886 | intersections 31, intersections.pairs 26, rounding 21, classification 6, assembly 4, intersections.planes 3, merge 3, setup 2, intersections.trees 2, merge.crossings 2, input 1, setup.points 1, setup.triangles 1, intersections.cull 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | difference | 1k-10k | 40 | 2 | 20.00 | 2172 | intersections 20, intersections.pairs 14, classification 6, intersections.planes 3, merge 3, rounding 3, setup 2, intersections.trees 2, merge.crossings 2, assembly 2, input 1, setup.points 1, setup.triangles 0, intersections.cull 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0300_57357+289691 | union | 1k-10k | 39 | 2 | 19.50 | 2592 | intersections 15, intersections.pairs 13, rounding 7, classification 6, merge 4, merge.predict 3, assembly 2, setup 1, intersections.trees 1, input 0, setup.points 0, setup.triangles 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0 |
| p0300_57357+289691 | intersection | 1k-10k | 19 | 1 | 19.00 | 48 | intersections 5, classification 5, merge 4, merge.crossings 3, intersections.pairs 2, setup 1, intersections.trees 1, input 0, setup.points 0, setup.triangles 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0, assembly 0, rounding 0 |

