### all thread(s)

| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 3.14 | 7.00 | 25.00 | 72/81 | 9 | 0 | 0 | 0 / 0 | 66/81 | 9 | degenerate triangles 6 | 0 | 0 / 0 |
| 10k-100k | 71 | 213 | 2.75 | 5.62 | 10.89 | 198/213 | 15 | 0 | 0 | 0 / 0 | 155/213 | 15 | degenerate triangles 43 | 0 | 0 / 0 |

All buckets: median ratio 2.91, p90 6.00, max 25.00 over 294 operations.
All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **yes**.

Five worst ratios (ours phases in ms):

| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |
|---|---|---|---|---|---|---|---|
| p0521_271864+77930 | intersection | 1k-10k | 25 | 1 | 25.00 | 22 | intersections 9, merge 5, classification 5, intersections.cull 4, merge.points 3, setup 2, intersections.trees 2, merge.crossings 2, input 1, setup.points 1, intersections.pairs 1, setup.triangles 0, intersections.planes 0, intersections.intersect 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0521_271864+77930 | difference | 1k-10k | 35 | 2 | 17.50 | 2172 | intersections 10, intersections.pairs 7, classification 6, merge 5, rounding 5, merge.points 3, setup 2, intersections.trees 2, merge.crossings 2, assembly 2, input 1, setup.points 1, setup.triangles 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | union | 1k-10k | 59 | 4 | 14.75 | 5886 | rounding 19, intersections 15, intersections.pairs 12, assembly 8, classification 7, merge 4, merge.crossings 3, setup 2, intersections.trees 2, input 1, setup.points 1, setup.triangles 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0321_1146187+118299 | union | 10k-100k | 98 | 9 | 10.89 | 10944 | rounding 33, intersections 31, intersections.pairs 16, classification 12, intersections.trees 11, assembly 10, setup 3, intersections.planes 3, merge 3, input 2, setup.points 2, merge.points 2, setup.triangles 1, intersections.cull 0, intersections.intersect 0, merge.crossings 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0300_57357+289691 | difference | 1k-10k | 21 | 2 | 10.50 | 1442 | intersections 6, classification 5, intersections.pairs 4, rounding 3, setup 1, merge 1, assembly 1, input 0, setup.points 0, setup.triangles 0, intersections.trees 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |

