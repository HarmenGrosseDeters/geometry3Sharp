### 1 thread(s)

| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 4.29 | 9.25 | 20.80 | 72/81 | 9 | 0 | 0 | 0 / 0 | 66/81 | 9 | degenerate triangles 6 | 0 | 0 / 0 |
| 10k-100k | 71 | 213 | 4.21 | 8.13 | 13.00 | 198/213 | 15 | 0 | 0 | 0 / 0 | 155/213 | 15 | degenerate triangles 43 | 0 | 0 / 0 |

All buckets: median ratio 4.23, p90 8.45, max 20.80 over 294 operations.
All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **yes**.

Five worst ratios (ours phases in ms):

| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |
|---|---|---|---|---|---|---|---|
| p0521_271864+77930 | union | 1k-10k | 104 | 5 | 20.80 | 5886 | intersections 49, intersections.pairs 40, rounding 27, classification 9, assembly 7, intersections.trees 4, intersections.planes 4, merge 4, setup 3, merge.crossings 2, input 1, setup.points 1, setup.triangles 1, intersections.cull 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | difference | 1k-10k | 61 | 3 | 20.33 | 2172 | intersections 31, intersections.pairs 22, classification 8, rounding 7, intersections.trees 4, merge 4, setup 3, intersections.planes 3, assembly 3, merge.crossings 2, input 1, setup.points 1, setup.triangles 1, intersections.cull 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | intersection | 1k-10k | 36 | 2 | 18.00 | 22 | intersections 18, intersections.cull 10, classification 7, intersections.trees 4, merge 4, setup 3, intersections.pairs 3, merge.crossings 2, input 1, setup.points 1, setup.triangles 1, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0300_57357+289691 | union | 1k-10k | 52 | 3 | 17.33 | 2592 | intersections 20, intersections.pairs 17, rounding 12, classification 8, merge 4, merge.predict 3, assembly 3, setup 1, intersections.trees 1, input 0, setup.points 0, setup.triangles 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0 |
| p0882_1458688+591229 | difference | 10k-100k | 169 | 13 | 13.00 | 7804 | intersections 85, intersections.pairs 50, rounding 33, intersections.trees 26, classification 12, setup 11, merge 9, assembly 9, setup.triangles 7, intersections.cull 7, input 4, merge.points 4, setup.points 3, merge.predict 3, triangulation 1, intersections.planes 0, intersections.intersect 0, merge.crossings 0, merge.points2 0, simplify 0 |

