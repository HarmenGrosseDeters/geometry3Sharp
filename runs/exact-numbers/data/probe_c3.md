
#### normal, 20 pairs: 60 operations, 10.86 s, GC pause 0.37 s, allocated 2.31 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 27736 | 258 | 2.37 % | 54 | 2.3 % | 9.3 |
| arr.pt.input | 202 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.edge | 27534 | 146 | 1.34 % | 41 | 1.7 % | 5.3 |
| arr.pt.segments | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.planes | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.reduce | 27534 | 93 | 0.86 % | 8 | 0.3 % | 3.4 |
| arr.pt.publish | 27736 | 14 | 0.13 % | 6 | 0.2 % | 0.5 |
| bool.centroid.big | 642 | 7 | 0.06 % | 1 | 0.1 % | 10.7 |
| bool.side.big | 1189 | 3 | 0.02 % | 0 | 0.0 % | 2.1 |
| bool.orient3d.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.incircle.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.orient.big | 518 | 1 | 0.01 % | 1 | 0.0 % | 2.3 |
| arr.compare.big | 45 | 0 | 0.00 % | 0 | 0.0 % | 8.9 |
| arr.along.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.same_plane.big | 20025 | 12 | 0.11 % | 3 | 0.1 % | 0.6 |
| arr.local_get.big | 18 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.edge_planes.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.planes | 34669 | 109 | 1.00 % | 31 | 1.3 % | 3.1 |
| arr.to_exact.outside | 28750 | 6 | 0.05 % | 4 | 0.2 % | 0.2 |
| arr.to_exact.inside | 58010 | 9 | 0.08 % | 6 | 0.3 % | 0.2 |
| pt.fixed | 60265 | 0 | 0.00 % | 9 | 0.4 % | 0.0 |
| pt.box | 640 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |

#### normal, 13 models with a rotated copy: 39 operations, 6.70 s, GC pause 0.27 s, allocated 1.46 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 45004 | 328 | 4.89 % | 89 | 5.9 % | 7.3 |
| arr.pt.input | 256 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.edge | 44748 | 171 | 2.55 % | 68 | 4.5 % | 3.8 |
| arr.pt.segments | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.planes | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.reduce | 44748 | 127 | 1.90 % | 13 | 0.9 % | 2.8 |
| arr.pt.publish | 45004 | 21 | 0.31 % | 8 | 0.5 % | 0.5 |
| bool.centroid.big | 449 | 5 | 0.08 % | 1 | 0.0 % | 11.6 |
| bool.side.big | 828 | 3 | 0.04 % | 0 | 0.0 % | 3.1 |
| bool.orient3d.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.incircle.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.orient.big | 1126 | 6 | 0.09 % | 1 | 0.1 % | 5.2 |
| arr.compare.big | 114 | 1 | 0.02 % | 0 | 0.0 % | 9.6 |
| arr.along.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.same_plane.big | 30669 | 23 | 0.34 % | 4 | 0.3 % | 0.8 |
| arr.local_get.big | 51 | 0 | 0.00 % | 0 | 0.0 % | 2.0 |
| arr.edge_planes.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.planes | 40924 | 114 | 1.70 % | 33 | 2.2 % | 2.8 |
| arr.to_exact.outside | 31440 | 7 | 0.10 % | 5 | 0.3 % | 0.2 |
| arr.to_exact.inside | 93140 | 12 | 0.19 % | 9 | 0.6 % | 0.1 |
| pt.fixed | 79744 | 0 | 0.00 % | 12 | 0.8 % | 0.0 |
| pt.box | 1140 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |

#### slow tail, copy_107554: 3 operations, 133.35 s, GC pause 11.23 s, allocated 27.52 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 1334763 | 7604 | 5.70 % | 2419 | 8.6 % | 5.7 |
| arr.pt.input | 35295 | 24 | 0.02 % | 8 | 0.0 % | 0.7 |
| arr.pt.edge | 1038825 | 2833 | 2.12 % | 1397 | 5.0 % | 2.7 |
| arr.pt.segments | 106275 | 477 | 0.36 % | 198 | 0.7 % | 4.5 |
| arr.pt.planes | 154368 | 776 | 0.58 % | 316 | 1.1 % | 5.0 |
| arr.pt.reduce | 1299468 | 2814 | 2.11 % | 293 | 1.0 % | 2.2 |
| arr.pt.publish | 1334763 | 457 | 0.34 % | 207 | 0.7 % | 0.3 |
| bool.centroid.big | 2386593 | 8000 | 6.00 % | 3085 | 10.9 % | 3.4 |
| bool.side.big | 7215738 | 3767 | 2.82 % | 1 | 0.0 % | 0.5 |
| bool.orient3d.big | 645702 | 1534 | 1.15 % | 0 | 0.0 % | 2.4 |
| arr.incircle.big | 224319 | 3220 | 2.41 % | 1363 | 4.8 % | 14.4 |
| arr.orient.big | 126821 | 236 | 0.18 % | 165 | 0.6 % | 1.9 |
| arr.compare.big | 89270 | 293 | 0.22 % | 80 | 0.3 % | 3.3 |
| arr.along.big | 114444 | 169 | 0.13 % | 121 | 0.4 % | 1.5 |
| arr.same_plane.big | 357417 | 233 | 0.17 % | 34 | 0.1 % | 0.7 |
| arr.local_get.big | 184179 | 222 | 0.17 % | 85 | 0.3 % | 1.2 |
| arr.edge_planes.big | 55392 | 202 | 0.15 % | 109 | 0.4 % | 3.6 |
| arr.planes | 405735 | 1175 | 0.88 % | 234 | 0.8 % | 2.9 |
| arr.to_exact.outside | 6631103 | 1435 | 1.08 % | 727 | 2.6 % | 0.2 |
| arr.to_exact.inside | 10766445 | 2361 | 1.77 % | 1286 | 4.6 % | 0.2 |
| pt.fixed | 1462437 | 0 | 0.00 % | 223 | 0.8 % | 0.0 |
| pt.box | 235389 | 0 | 0.00 % | 50 | 0.2 % | 0.0 |

#### slow tail, copy_42327: 3 operations, 218.83 s, GC pause 7.38 s, allocated 25.82 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 729870 | 3799 | 1.74 % | 1480 | 5.6 % | 5.2 |
| arr.pt.input | 4395 | 2 | 0.00 % | 1 | 0.0 % | 0.6 |
| arr.pt.edge | 294111 | 768 | 0.35 % | 399 | 1.5 % | 2.6 |
| arr.pt.segments | 210294 | 447 | 0.20 % | 393 | 1.5 % | 2.1 |
| arr.pt.planes | 221070 | 763 | 0.35 % | 386 | 1.5 % | 3.5 |
| arr.pt.reduce | 725475 | 1459 | 0.67 % | 190 | 0.7 % | 2.0 |
| arr.pt.publish | 729870 | 236 | 0.11 % | 112 | 0.4 % | 0.3 |
| bool.centroid.big | 3779985 | 16501 | 7.54 % | 6131 | 23.2 % | 4.4 |
| bool.side.big | 47816814 | 27458 | 12.55 % | 0 | 0.0 % | 0.6 |
| bool.orient3d.big | 2321109 | 6312 | 2.88 % | 0 | 0.0 % | 2.7 |
| arr.incircle.big | 86088 | 1142 | 0.52 % | 582 | 2.2 % | 13.3 |
| arr.orient.big | 149739 | 256 | 0.12 % | 220 | 0.8 % | 1.7 |
| arr.compare.big | 379728 | 342 | 0.16 % | 347 | 1.3 % | 0.9 |
| arr.along.big | 290307 | 372 | 0.17 % | 313 | 1.2 % | 1.3 |
| arr.same_plane.big | 48537 | 8 | 0.00 % | 5 | 0.0 % | 0.2 |
| arr.local_get.big | 267399 | 316 | 0.14 % | 137 | 0.5 % | 1.2 |
| arr.edge_planes.big | 4833 | 180 | 0.08 % | 10 | 0.0 % | 37.2 |
| arr.planes | 27477 | 54 | 0.02 % | 14 | 0.1 % | 2.0 |
| arr.to_exact.outside | 5626263 | 1508 | 0.69 % | 683 | 2.6 % | 0.3 |
| arr.to_exact.inside | 13738706 | 3415 | 1.56 % | 2031 | 7.7 % | 0.2 |
| pt.fixed | 756294 | 0 | 0.00 % | 115 | 0.4 % | 0.0 |
| pt.box | 160617 | 0 | 0.00 % | 36 | 0.1 % | 0.0 |
