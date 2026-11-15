### 1 thread(s)

| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 4.37 | 9.38 | 24.00 | 72/81 | 9 | 0 | 0 | 0 / 0 | 66/81 | 9 | degenerate triangles 6 | 0 | 0 / 0 |
| 10k-100k | 71 | 213 | 4.21 | 8.14 | 13.44 | 198/213 | 15 | 0 | 0 | 0 / 0 | 155/213 | 15 | degenerate triangles 43 | 0 | 0 / 0 |

All buckets: median ratio 4.23, p90 8.43, max 24.00 over 294 operations.
All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **yes**.

Five worst ratios (ours phases in ms):

| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |
|---|---|---|---|---|---|---|---|
| p0521_271864+77930 | difference | 1k-10k | 48 | 2 | 24.00 | 2172 | intersections 25, intersections.pairs 18, classification 6, rounding 5, intersections.trees 3, intersections.planes 3, merge 3, setup 2, merge.crossings 2, assembly 2, input 1, setup.points 1, setup.triangles 1, intersections.cull 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | union | 1k-10k | 82 | 4 | 20.50 | 5886 | intersections 38, intersections.pairs 31, rounding 21, classification 7, assembly 5, intersections.trees 3, intersections.planes 3, merge 3, setup 2, merge.crossings 2, input 1, setup.points 1, setup.triangles 1, intersections.cull 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | intersection | 1k-10k | 29 | 2 | 14.50 | 22 | intersections 14, intersections.cull 8, classification 5, intersections.trees 3, merge 3, setup 2, intersections.pairs 2, merge.crossings 2, input 1, setup.points 1, setup.triangles 1, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0300_57357+289691 | difference | 1k-10k | 28 | 2 | 14.00 | 1442 | intersections 10, intersections.pairs 8, classification 5, merge 3, rounding 3, merge.predict 2, setup 1, intersections.trees 1, assembly 1, input 0, setup.points 0, setup.triangles 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0 |
| p0300_57357+289691 | union | 1k-10k | 41 | 3 | 13.67 | 2592 | intersections 15, intersections.pairs 13, rounding 9, classification 6, merge 3, assembly 3, merge.predict 2, setup 1, intersections.trees 1, input 0, setup.points 0, setup.triangles 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0 |

