### 1 thread(s)

| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 4.83 | 10.67 | 37.00 | 72/81 | 9 | 0 | 0 | 0 / 0 | 66/81 | 9 | degenerate triangles 6 | 0 | 0 / 0 |
| 10k-100k | 71 | 213 | 5.28 | 9.47 | 18.55 | 198/213 | 15 | 0 | 0 | 0 / 0 | 155/213 | 15 | degenerate triangles 43 | 0 | 0 / 0 |

All buckets: median ratio 5.25, p90 10.09, max 37.00 over 294 operations.
All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **yes**.

Five worst ratios (ours phases in ms):

| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |
|---|---|---|---|---|---|---|---|
| p0521_271864+77930 | difference | 1k-10k | 74 | 2 | 37.00 | 2172 | intersections 48, intersections.pairs 41, rounding 9, classification 4, setup 3, intersections.planes 3, assembly 3, setup.triangles 2, intersections.trees 2, input 1, setup.points 1, merge.points 1, merge 1, intersections.intersect 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | intersection | 1k-10k | 60 | 2 | 30.00 | 22 | intersections 48, intersections.pairs 41, setup 3, intersections.planes 3, classification 3, setup.triangles 2, intersections.trees 2, input 1, setup.points 1, merge 1, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0521_271864+77930 | union | 1k-10k | 92 | 4 | 23.00 | 5886 | intersections 47, intersections.pairs 41, rounding 21, assembly 10, setup 3, intersections.planes 3, classification 3, setup.triangles 2, intersections.trees 2, input 1, setup.points 1, merge 1, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, triangulation 0, simplify 0 |
| p0633_37275+70830 | intersection | 10k-100k | 371 | 20 | 18.55 | 958 | intersections 266, intersections.pairs 220, intersections.trees 38, setup 25, triangulation 19, classification 19, setup.triangles 17, merge 13, merge.points 11, rounding 11, input 8, setup.points 7, assembly 6, intersections.planes 4, intersections.intersect 2, merge.crossings 2, simplify 2, merge.points2 0 |
| p0300_57357+289691 | union | 1k-10k | 47 | 3 | 15.67 | 2592 | intersections 19, intersections.pairs 17, rounding 12, merge.crossings 4, merge 4, assembly 3, classification 2, setup.triangles 1, setup 1, intersections.trees 1, input 0, setup.points 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, triangulation 0, simplify 0 |

