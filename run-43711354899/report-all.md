### all thread(s)

| bucket | pairs | ops (both ok) | median ratio | p90 | max | ours clean | ours empty | ours unclean | ours refused | ours failed / timeout | CGAL clean | CGAL empty | CGAL unclean | CGAL refused | CGAL failed / timeout |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1k-10k | 27 | 81 | 4.00 | 8.75 | 25.00 | 72/81 | 9 | 0 | 0 | 0 / 0 | 66/81 | 9 | degenerate triangles 6 | 0 | 0 / 0 |
| 10k-100k | 71 | 213 | 3.55 | 6.54 | 10.44 | 198/213 | 15 | 0 | 0 | 0 / 0 | 155/213 | 15 | degenerate triangles 43 | 0 | 0 / 0 |

All buckets: median ratio 3.61, p90 7.09, max 25.00 over 294 operations.
All-clean (every result of ours clean or correctly empty, no refusal, failure or timeout): **yes**.

Five worst ratios (ours phases in ms):

| pair | op | bucket | ours ms | CGAL ms | ratio | result tris | phases |
|---|---|---|---|---|---|---|---|
| p0521_271864+77930 | intersection | 1k-10k | 25 | 1 | 25.00 | 22 | intersections 12, classification 5, intersections.cull 4, intersections.planes 3, merge 3, merge.crossings 2, input 1, setup.points 1, setup 1, intersections.trees 1, intersections.pairs 1, setup.triangles 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0, assembly 0, rounding 0 |
| p0521_271864+77930 | union | 1k-10k | 56 | 3 | 18.67 | 5886 | rounding 20, intersections 14, intersections.pairs 12, classification 7, merge 4, assembly 4, merge.crossings 3, input 1, setup.points 1, setup 1, intersections.trees 1, setup.triangles 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0521_271864+77930 | difference | 1k-10k | 31 | 2 | 15.50 | 2172 | intersections 9, intersections.pairs 7, classification 6, merge 4, rounding 4, merge.crossings 3, assembly 2, setup.points 1, setup 1, intersections.trees 1, input 0, setup.triangles 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0300_57357+289691 | union | 1k-10k | 26 | 2 | 13.00 | 2592 | intersections 7, intersections.pairs 6, classification 6, rounding 6, assembly 2, merge 1, input 0, setup.points 0, setup.triangles 0, setup 0, intersections.trees 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0 |
| p0300_57357+289691 | intersection | 1k-10k | 13 | 1 | 13.00 | 48 | classification 5, intersections 4, intersections.pairs 2, merge 1, input 0, setup.points 0, setup.triangles 0, setup 0, intersections.trees 0, intersections.cull 0, intersections.planes 0, intersections.intersect 0, merge.points 0, merge.crossings 0, merge.points2 0, merge.predict 0, triangulation 0, simplify 0, assembly 0, rounding 0 |

