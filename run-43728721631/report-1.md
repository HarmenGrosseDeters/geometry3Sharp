### 1 thread(s)

| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 5.12 | 10.75 | 31.00 | 72/81 | 9 | 0 | 0 | 0 / 0 | 66/81 | 9 | degenerate triangles 6 | 0 | 0 / 0 |
| 10k-100k | 71 | 213 | 5.22 | 9.60 | 15.56 | 198/213 | 15 | 0 | 0 | 0 / 0 | 155/213 | 15 | degenerate triangles 43 | 0 | 0 / 0 |

All buckets: median ratio 5.20, p90 10.49, max 31.00 over 294 operations.
All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **yes**.

Five worst ratios (ours phases in ms):

| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |
|---|---|---|---|---|---|---|---|
| p0521_271864+77930 | intersection | 1k-10k | 62 | 2 | 31.00 | 22 | intersections 49, intersections.pairs 42, intersections.planes 4, setup 3, classification 3, setup.triangles 2, intersections.trees 2, input 1, setup.points 1, merge 1, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0521_271864+77930 | difference | 1k-10k | 73 | 3 | 24.33 | 2172 | intersections 49, intersections.pairs 42, rounding 9, intersections.planes 4, setup 3, classification 3, assembly 3, setup.triangles 2, intersections.trees 2, input 1, setup.points 1, merge.points 1, merge 1, intersections.intersect 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | union | 1k-10k | 95 | 4 | 23.75 | 5886 | intersections 50, intersections.pairs 44, rounding 21, assembly 11, intersections.planes 4, setup 3, classification 3, setup.triangles 2, intersections.trees 2, input 1, setup.points 1, merge 1, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0 |
| p0882_1458688+591229 | intersection | 10k-100k | 140 | 9 | 15.56 | 84 | intersections 100, intersections.pairs 74, intersections.trees 25, setup 14, setup.triangles 10, classification 9, merge.points 5, merge 5, input 4, setup.points 4, triangulation 1, assembly 1, intersections.planes 0, intersections.intersect 0, merge.crossings 0, merge.points2 0, simplify 0, rounding 0 |
| p0300_57357+289691 | intersection | 1k-10k | 31 | 2 | 15.50 | 48 | intersections 19, intersections.pairs 17, merge.crossings 4, merge 4, classification 2, setup.triangles 1, setup 1, intersections.trees 1, input 0, setup.points 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, triangulation 0, simplify 0, assembly 0, rounding 0 |

