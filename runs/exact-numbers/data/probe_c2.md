
#### normal, 20 pairs: 60 operations, 11.64 s, GC pause 0.38 s, allocated 2.31 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 27736 | 248 | 2.13 % | 54 | 2.3 % | 8.9 |
| arr.pt.input | 202 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.edge | 27534 | 132 | 1.13 % | 41 | 1.7 % | 4.8 |
| arr.pt.segments | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.planes | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.reduce | 27534 | 93 | 0.80 % | 8 | 0.3 % | 3.4 |
| arr.pt.publish | 27736 | 17 | 0.15 % | 6 | 0.2 % | 0.6 |
| bool.centroid.big | 642 | 7 | 0.06 % | 1 | 0.1 % | 11.7 |
| bool.side.big | 1189 | 2 | 0.02 % | 0 | 0.0 % | 1.9 |
| bool.orient3d.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.incircle.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.orient.big | 518 | 1 | 0.01 % | 1 | 0.0 % | 1.9 |
| arr.compare.big | 45 | 0 | 0.00 % | 0 | 0.0 % | 8.9 |
| arr.along.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.same_plane.big | 20025 | 11 | 0.10 % | 3 | 0.1 % | 0.6 |
| arr.local_get.big | 18 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.edge_planes.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.planes | 34669 | 126 | 1.08 % | 31 | 1.3 % | 3.6 |
| pt.fixed | 60265 | 0 | 0.00 % | 9 | 0.4 % | 0.0 |
| pt.box | 640 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |

#### normal, 13 models with a rotated copy: 39 operations, 7.14 s, GC pause 0.23 s, allocated 1.46 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 45004 | 323 | 4.52 % | 89 | 5.9 % | 7.2 |
| arr.pt.input | 256 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.edge | 44748 | 152 | 2.13 % | 68 | 4.5 % | 3.4 |
| arr.pt.segments | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.planes | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.reduce | 44748 | 141 | 1.97 % | 13 | 0.9 % | 3.1 |
| arr.pt.publish | 45004 | 22 | 0.31 % | 8 | 0.5 % | 0.5 |
| bool.centroid.big | 449 | 5 | 0.07 % | 1 | 0.0 % | 11.4 |
| bool.side.big | 828 | 2 | 0.03 % | 0 | 0.0 % | 2.4 |
| bool.orient3d.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.incircle.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.orient.big | 1126 | 6 | 0.08 % | 1 | 0.1 % | 5.2 |
| arr.compare.big | 114 | 1 | 0.02 % | 0 | 0.0 % | 10.5 |
| arr.along.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.same_plane.big | 30669 | 24 | 0.33 % | 4 | 0.3 % | 0.8 |
| arr.local_get.big | 51 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.edge_planes.big | 0 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.planes | 40924 | 122 | 1.71 % | 33 | 2.2 % | 3.0 |
| pt.fixed | 79744 | 0 | 0.00 % | 12 | 0.8 % | 0.0 |
| pt.box | 1140 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |

#### slow tail, copy_107554: 3 operations, 125.86 s, GC pause 11.11 s, allocated 27.52 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 1334763 | 7399 | 5.88 % | 2419 | 8.6 % | 5.5 |
| arr.pt.input | 35295 | 19 | 0.01 % | 8 | 0.0 % | 0.5 |
| arr.pt.edge | 1038825 | 2668 | 2.12 % | 1397 | 5.0 % | 2.6 |
| arr.pt.segments | 106275 | 415 | 0.33 % | 198 | 0.7 % | 3.9 |
| arr.pt.planes | 154368 | 759 | 0.60 % | 316 | 1.1 % | 4.9 |
| arr.pt.reduce | 1299468 | 2940 | 2.34 % | 293 | 1.0 % | 2.3 |
| arr.pt.publish | 1334763 | 397 | 0.32 % | 207 | 0.7 % | 0.3 |
| bool.centroid.big | 2386593 | 7602 | 6.04 % | 3085 | 10.9 % | 3.2 |
| bool.side.big | 7215738 | 3509 | 2.79 % | 1 | 0.0 % | 0.5 |
| bool.orient3d.big | 645702 | 1452 | 1.15 % | 0 | 0.0 % | 2.2 |
| arr.incircle.big | 224319 | 2857 | 2.27 % | 1363 | 4.8 % | 12.7 |
| arr.orient.big | 126821 | 208 | 0.16 % | 165 | 0.6 % | 1.6 |
| arr.compare.big | 89270 | 312 | 0.25 % | 80 | 0.3 % | 3.5 |
| arr.along.big | 114444 | 148 | 0.12 % | 121 | 0.4 % | 1.3 |
| arr.same_plane.big | 357417 | 91 | 0.07 % | 34 | 0.1 % | 0.3 |
| arr.local_get.big | 184179 | 198 | 0.16 % | 85 | 0.3 % | 1.1 |
| arr.edge_planes.big | 55392 | 165 | 0.13 % | 109 | 0.4 % | 3.0 |
| arr.planes | 405735 | 963 | 0.77 % | 234 | 0.8 % | 2.4 |
| pt.fixed | 1462437 | 0 | 0.00 % | 223 | 0.8 % | 0.0 |
| pt.box | 235389 | 0 | 0.00 % | 50 | 0.2 % | 0.0 |

#### slow tail, copy_42327: 3 operations, 208.64 s, GC pause 7.05 s, allocated 25.82 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 729870 | 3434 | 1.65 % | 1480 | 5.6 % | 4.7 |
| arr.pt.input | 4395 | 2 | 0.00 % | 1 | 0.0 % | 0.5 |
| arr.pt.edge | 294111 | 854 | 0.41 % | 399 | 1.5 % | 2.9 |
| arr.pt.segments | 210294 | 366 | 0.18 % | 393 | 1.5 % | 1.7 |
| arr.pt.planes | 221070 | 579 | 0.28 % | 386 | 1.5 % | 2.6 |
| arr.pt.reduce | 725475 | 1305 | 0.63 % | 190 | 0.7 % | 1.8 |
| arr.pt.publish | 729870 | 216 | 0.10 % | 112 | 0.4 % | 0.3 |
| bool.centroid.big | 3779985 | 14975 | 7.18 % | 6131 | 23.2 % | 4.0 |
| bool.side.big | 47816814 | 25500 | 12.22 % | 0 | 0.0 % | 0.5 |
| bool.orient3d.big | 2321109 | 5943 | 2.85 % | 0 | 0.0 % | 2.6 |
| arr.incircle.big | 86088 | 1080 | 0.52 % | 582 | 2.2 % | 12.5 |
| arr.orient.big | 149739 | 225 | 0.11 % | 220 | 0.8 % | 1.5 |
| arr.compare.big | 379728 | 290 | 0.14 % | 347 | 1.3 % | 0.8 |
| arr.along.big | 290307 | 361 | 0.17 % | 313 | 1.2 % | 1.2 |
| arr.same_plane.big | 48537 | 9 | 0.00 % | 5 | 0.0 % | 0.2 |
| arr.local_get.big | 267399 | 287 | 0.14 % | 137 | 0.5 % | 1.1 |
| arr.edge_planes.big | 4833 | 117 | 0.06 % | 10 | 0.0 % | 24.1 |
| arr.planes | 27477 | 53 | 0.03 % | 14 | 0.1 % | 1.9 |
| pt.fixed | 756294 | 0 | 0.00 % | 115 | 0.4 % | 0.0 |
| pt.box | 160617 | 0 | 0.00 % | 36 | 0.1 % | 0.0 |
