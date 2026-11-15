### all thread(s)

| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 3.06 | 6.50 | 23.00 | 72/81 | 9 | 0 | 0 | 0 / 0 | 66/81 | 9 | degenerate triangles 6 | 0 | 0 / 0 |
| 10k-100k | 71 | 213 | 3.06 | 5.75 | 8.67 | 198/213 | 15 | 0 | 0 | 0 / 0 | 155/213 | 15 | degenerate triangles 43 | 0 | 0 / 0 |

All buckets: median ratio 3.06, p90 5.95, max 23.00 over 294 operations.
All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **yes**.

Five worst ratios (ours phases in ms):

| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |
|---|---|---|---|---|---|---|---|
| p0521_271864+77930 | intersection | 1k-10k | 23 | 1 | 23.00 | 22 | intersections 13, intersections.pairs 11, merge.crossings 3, merge 3, classification 2, setup.points 1, setup 1, input 0, setup.triangles 0, intersections.trees 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0521_271864+77930 | difference | 1k-10k | 32 | 2 | 16.00 | 2172 | intersections 14, intersections.pairs 13, assembly 4, merge.crossings 3, merge 3, classification 2, rounding 2, setup.points 1, setup 1, intersections.trees 1, input 0, setup.triangles 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | union | 1k-10k | 42 | 3 | 14.00 | 5886 | rounding 13, intersections 12, intersections.pairs 11, assembly 6, merge 3, merge.crossings 2, classification 2, setup.points 1, setup 1, intersections.trees 1, input 0, setup.triangles 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, triangulation 0, simplify 0 |
| p0300_57357+289691 | difference | 1k-10k | 26 | 2 | 13.00 | 1442 | intersections 10, intersections.pairs 9, rounding 4, assembly 3, classification 2, setup 1, merge.crossings 1, merge 1, triangulation 1, input 0, setup.points 0, setup.triangles 0, intersections.trees 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, simplify 0 |
| p0300_57357+289691 | intersection | 1k-10k | 11 | 1 | 11.00 | 48 | intersections.pairs 6, intersections 6, classification 1, input 0, setup.points 0, setup.triangles 0, setup 0, intersections.trees 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, merge 0, triangulation 0, simplify 0, assembly 0, rounding 0 |

