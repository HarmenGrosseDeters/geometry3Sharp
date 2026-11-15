### all thread(s)

| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 3.86 | 8.33 | 26.00 | 72/81 | 9 | 0 | 0 | 0 / 0 | 66/81 | 9 | degenerate triangles 6 | 0 | 0 / 0 |
| 10k-100k | 71 | 213 | 3.79 | 6.74 | 12.17 | 198/213 | 15 | 0 | 0 | 0 / 0 | 155/213 | 15 | degenerate triangles 43 | 0 | 0 / 0 |

All buckets: median ratio 3.81, p90 7.14, max 26.00 over 294 operations.
All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **yes**.

Five worst ratios (ours phases in ms):

| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |
|---|---|---|---|---|---|---|---|
| p0521_271864+77930 | intersection | 1k-10k | 26 | 1 | 26.00 | 22 | intersections 15, intersections.pairs 14, merge 3, setup 2, merge.crossings 2, classification 2, setup.points 1, setup.triangles 1, intersections.trees 1, input 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0521_271864+77930 | difference | 1k-10k | 34 | 2 | 17.00 | 2172 | intersections 17, intersections.pairs 15, merge 3, rounding 3, setup 2, merge.crossings 2, classification 2, assembly 2, input 1, setup.points 1, setup.triangles 1, intersections.trees 1, merge.points 1, intersections.planes 0, intersections.intersect 0, merge.points2 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | union | 1k-10k | 48 | 3 | 16.00 | 5886 | rounding 18, intersections 15, intersections.pairs 13, assembly 4, merge 3, merge.crossings 2, classification 2, setup 1, intersections.trees 1, input 0, setup.points 0, setup.triangles 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, triangulation 0, simplify 0 |
| p0300_57357+289691 | intersection | 1k-10k | 14 | 1 | 14.00 | 48 | intersections 8, intersections.pairs 7, classification 1, input 0, setup.points 0, setup.triangles 0, setup 0, intersections.trees 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, merge 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0882_1458688+591229 | intersection | 10k-100k | 73 | 6 | 12.17 | 84 | intersections 46, intersections.pairs 25, intersections.trees 20, setup 9, setup.triangles 6, classification 6, merge.points 4, merge 4, input 3, setup.points 2, triangulation 1, intersections.planes 0, intersections.intersect 0, merge.crossings 0, merge.points2 0, simplify 0, assembly 0, rounding 0 |

