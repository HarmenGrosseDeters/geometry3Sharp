| case | frame | expected | actual | ok | g max / diag | U | U / smaller | kept | analytic | vol err / W | kept - intent | curves | construct ms | verify ms | imprint ms | cut ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| planar 0.1 % | identity | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 98.1 | 15.6 | 121.0 | 150.8 |
| planar 0.1 % | shift 1e2 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 0.4 | 0.4 | 2.2 | 24.0 |
| planar 0.1 % | shift 6e5 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 3.8 | 4.6 | 1.5 | 9.4 |
| planar 0.1 % | shift 5.7e6 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.4 | 0.7 | 1.6 | 9.7 |
| planar 0.1 % | rotated | extended | extended | yes | 0.0007 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | -1.7e-16 |  | 1 | 0.7 | 0.6 | 3.2 | 154.5 |
| bent 0.1 % | identity | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | -5.7e-17 |  | 1 | 0.4 | 0.4 | 4.7 | 11.6 |
| bent 0.1 % | shift 1e2 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | 0.0e+0 |  | 1 | 3.8 | 4.7 | 1.8 | 7.0 |
| bent 0.1 % | shift 6e5 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | -5.8e-13 |  | 1 | 0.3 | 0.5 | 5.3 | 6.8 |
| bent 0.1 % | shift 5.7e6 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | -4.7e-12 |  | 1 | 0.4 | 0.4 | 5.5 | 13.3 |
| bent 0.1 % | rotated | extended | extended | yes | 0.0008 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | -1.7e-16 |  | 1 | 0.9 | 0.4 | 24.3 | 19.1 |
| planar 0.5 % | identity | extended | extended | yes | 0.0050 | 0.0022 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.6 | 0.5 | 6.8 | 10.6 |
| planar 0.5 % | shift 1e2 | extended | extended | yes | 0.0050 | 0.0022 | 0.00000 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 0.5 | 0.4 | 4.5 | 21.1 |
| planar 0.5 % | shift 6e5 | extended | extended | yes | 0.0050 | 0.0022 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.6 | 0.9 | 5.1 | 19.8 |
| planar 0.5 % | shift 5.7e6 | extended | extended | yes | 0.0050 | 0.0022 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.5 | 0.6 | 4.7 | 11.1 |
| planar 0.5 % | rotated | extended | extended | yes | 0.0037 | 0.0021 | 0.00000 | 500.0000 | 500.0000 | -2.3e-16 |  | 1 | 0.4 | 0.4 | 5.2 | 28.8 |
| bent 0.5 % | identity | extended | extended | yes | 0.0052 | 0.0025 | 0.00001 | 462.5000 | 462.5000 | -1.7e-16 |  | 1 | 0.5 | 0.6 | 4.2 | 10.7 |
| bent 0.5 % | shift 1e2 | extended | extended | yes | 0.0052 | 0.0025 | 0.00001 | 462.5000 | 462.5000 | 1.7e-16 |  | 1 | 0.5 | 0.4 | 5.6 | 19.5 |
| bent 0.5 % | shift 6e5 | extended | extended | yes | 0.0052 | 0.0025 | 0.00001 | 462.5000 | 462.5000 | -1.2e-12 |  | 1 | 0.5 | 0.5 | 1.8 | 11.8 |
| bent 0.5 % | shift 5.7e6 | extended | extended | yes | 0.0052 | 0.0025 | 0.00001 | 462.5000 | 462.5000 | 6.8e-12 |  | 1 | 0.3 | 0.4 | 1.1 | 12.2 |
| bent 0.5 % | rotated | extended | extended | yes | 0.0038 | 0.0023 | 0.00001 | 462.5000 | 462.5000 | -2.8e-16 |  | 1 | 0.5 | 0.4 | 42.5 | 14.3 |
| planar 2 % | identity | extended | extended | yes | 0.0200 | 0.0359 | 0.00007 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 0.6 | 6.0 | 28.4 |
| planar 2 % | shift 1e2 | extended | extended | yes | 0.0200 | 0.0359 | 0.00007 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.6 | 0.6 | 6.9 | 45.6 |
| planar 2 % | shift 6e5 | extended | extended | yes | 0.0200 | 0.0359 | 0.00007 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 1.1 | 11.5 | 13.9 |
| planar 2 % | shift 5.7e6 | extended | extended | yes | 0.0200 | 0.0359 | 0.00007 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 4.1 | 0.8 | 2.4 | 16.7 |
| planar 2 % | rotated | extended | extended | yes | 0.0146 | 0.0355 | 0.00007 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 1.0 | 3.2 | 4.3 | 9.9 |
| bent 2 % | identity | extended | extended | yes | 0.0209 | 0.0400 | 0.00009 | 462.5000 | 462.5000 | 5.7e-17 |  | 1 | 0.4 | 0.6 | 5.9 | 12.3 |
| bent 2 % | shift 1e2 | extended | extended | yes | 0.0209 | 0.0400 | 0.00009 | 462.5000 | 462.5000 | 1.7e-16 |  | 1 | 4.1 | 3.7 | 1.6 | 8.9 |
| bent 2 % | shift 6e5 | extended | extended | yes | 0.0209 | 0.0400 | 0.00009 | 462.5000 | 462.5000 | 2.2e-13 |  | 1 | 0.6 | 4.9 | 4.5 | 7.7 |
| bent 2 % | shift 5.7e6 | extended | extended | yes | 0.0209 | 0.0400 | 0.00009 | 462.5000 | 462.5000 | 2.4e-12 |  | 1 | 0.6 | 4.6 | 12.1 | 6.9 |
| bent 2 % | rotated | extended | extended | yes | 0.0154 | 0.0395 | 0.00009 | 462.5000 | 462.5000 | -4.5e-16 |  | 1 | 0.5 | 0.4 | 4.6 | 24.0 |
| planar 8 % | identity | refused: DepthAboveLimit | refused: DepthAboveLimit | yes | 0.0000 | 0.0000 |  |  | 500.0000 |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| bent 8 % | identity | refused: DepthAboveLimit | refused: DepthAboveLimit | yes | 0.0000 | 0.0000 |  |  | 462.5000 |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| slit ends in the middle | identity | refused: DepthAboveLimit | refused: DepthAboveLimit | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| slot and fin | identity | extended | extended | yes | 0.0271 | 0.0533 | 0.00011 | 506.0000 | 506.0000 | -1.7e-16 |  | 1 | 2.0 | 4.8 | 7.3 | 37.3 |
| slot and fin | shift 1e2 | extended | extended | yes | 0.0271 | 0.0533 | 0.00011 | 506.0000 | 506.0000 | -2.9e-16 |  | 1 | 1.3 | 1.2 | 11.3 | 39.1 |
| slot and fin | shift 6e5 | extended | extended | yes | 0.0271 | 0.0533 | 0.00011 | 506.0000 | 506.0000 | -6.2e-12 |  | 1 | 1.0 | 1.2 | 11.2 | 8.8 |
| slot and fin | shift 5.7e6 | extended | extended | yes | 0.0271 | 0.0533 | 0.00011 | 506.0000 | 506.0000 | -2.3e-11 |  | 1 | 1.0 | 1.0 | 6.1 | 20.7 |
| slot and fin | rotated | extended | extended | yes | 0.0199 | 0.0533 | 0.00011 | 506.0000 | 506.0000 | 0.0e+0 |  | 1 | 0.9 | 1.3 | 11.8 | 21.1 |
| free pin in the slot | identity | refused: CurveRule | refused: CurveRule | yes | 0.0271 | 0.0533 |  |  |  |  |  | 0 | 0.9 | 1.2 | 14.0 | 0.0 |
| wide pocket (concave, re-entry) | identity | extended | extended | yes | 0.0293 | 0.0461 | 0.00012 | 400.0000 | 400.0000 | 0.0e+0 |  | 1 | 1.5 | 0.9 | 15.5 | 22.9 |
| wide pocket (concave, re-entry) | shift 1e2 | extended | extended | yes | 0.0293 | 0.0461 | 0.00012 | 400.0000 | 400.0000 | 0.0e+0 |  | 1 | 1.2 | 1.0 | 17.0 | 6.0 |
| wide pocket (concave, re-entry) | shift 6e5 | extended | extended | yes | 0.0293 | 0.0461 | 0.00012 | 400.0000 | 400.0000 | 0.0e+0 |  | 1 | 1.1 | 1.0 | 3.2 | 7.9 |
| wide pocket (concave, re-entry) | shift 5.7e6 | extended | extended | yes | 0.0293 | 0.0461 | 0.00012 | 400.0000 | 400.0000 | 0.0e+0 |  | 1 | 4.6 | 4.5 | 13.0 | 5.5 |
| wide pocket (concave, re-entry) | rotated | extended | extended | yes | 0.0205 | 0.0439 | 0.00011 | 400.0000 | 400.0000 | -1.9e-16 |  | 1 | 0.8 | 2.7 | 12.1 | 7.4 |
| notch narrower than the depth | identity | refused: InvertedQuad | refused: InvertedQuad | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.6 | 0.0 | 0.0 | 0.0 |
| rim outside, next to another part (U) | identity | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | 0.0e+0 |  | 0 | 0.4 | 0.0 | 0.0 | 12.1 |
| rim outside, next to another part (U) | shift 1e2 | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | 1.7e-16 |  | 0 | 0.4 | 0.0 | 0.0 | 7.5 |
| rim outside, next to another part (U) | shift 6e5 | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | 0.0e+0 |  | 0 | 0.3 | 0.0 | 0.0 | 7.1 |
| rim outside, next to another part (U) | shift 5.7e6 | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | 0.0e+0 |  | 0 | 0.3 | 0.0 | 0.0 | 6.5 |
| rim outside, next to another part (U) | rotated | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | -1.7e-16 |  | 0 | 0.5 | 0.0 | 0.0 | 24.7 |
| bowtie vertex on the rim | identity | refused: BowtieVertex | refused: BowtieVertex | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| chord across a sphere | identity | extended | extended | yes | 0.0227 | 0.0213 | 0.00010 | 221.1559 | 221.1559 | 3.8e-16 |  | 1 | 9.0 | 0.3 | 28.8 | 48.2 |
| chord across a sphere | shift 1e2 | extended | extended | yes | 0.0227 | 0.0213 | 0.00010 | 221.1559 | 221.1559 | 5.5e-17 |  | 1 | 23.7 | 0.3 | 18.3 | 83.7 |
| chord across a sphere | shift 6e5 | extended | extended | yes | 0.0227 | 0.0213 | 0.00010 | 221.1559 | 221.1559 | 6.7e-13 |  | 1 | 28.7 | 0.5 | 18.3 | 71.7 |
| chord across a sphere | shift 5.7e6 | extended | extended | yes | 0.0227 | 0.0213 | 0.00010 | 221.1559 | 221.1559 | 1.4e-11 |  | 1 | 29.5 | 0.4 | 21.1 | 51.7 |
| chord across a sphere | rotated | extended | extended | yes | 0.0167 | 0.0213 | 0.00010 | 221.1559 | 221.1559 | 5.5e-17 |  | 1 | 34.0 | 4.2 | 13.1 | 103.9 |
| rim vertex exactly on the surface | identity | extended | extended | yes | 0.0109 | 0.0092 | 0.00002 | 500.0000 | 500.0000 | 0.0e+0 |  | 2 | 0.3 | 0.3 | 5.2 | 2.0 |
| exit at 3 degrees | identity | refused: GrazingExit | refused: GrazingExit | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| exit at 20 degrees | identity | extended | extended | yes | 0.0253 | 0.0416 | 0.01212 | 3.4343 | 3.4343 | 0.0e+0 |  | 1 | 0.4 | 0.4 | 5.1 | 6.5 |
| exit at 20 degrees | shift 1e2 | extended | extended | yes | 0.0253 | 0.0416 | 0.01212 | 3.4343 | 3.4343 | -1.0e-15 |  | 1 | 0.6 | 0.6 | 16.6 | 9.5 |
| exit at 20 degrees | shift 6e5 | extended | extended | yes | 0.0253 | 0.0416 | 0.01212 | 3.4343 | 3.4343 | -1.2e-12 |  | 1 | 0.5 | 0.6 | 4.7 | 6.0 |
| exit at 20 degrees | shift 5.7e6 | extended | extended | yes | 0.0253 | 0.0416 | 0.01212 | 3.4343 | 3.4343 | 2.0e-11 |  | 1 | 0.5 | 0.8 | 5.2 | 11.0 |
| exit at 20 degrees | rotated | extended | extended | yes | 0.0215 | 0.0416 | 0.01212 | 3.4343 | 3.4343 | -4.5e-16 |  | 1 | 0.6 | 3.8 | 5.7 | 9.9 |
| short on four sides, rays through box edges | identity | extended | extended | yes | 0.0319 | 0.1165 | 0.00023 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 4.3 | 7.4 | 6.5 |
| short on four sides, rays through box edges | shift 1e2 | extended | extended | yes | 0.0319 | 0.1165 | 0.00023 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 0.5 | 4.3 | 12.8 | 6.6 |
| short on four sides, rays through box edges | shift 6e5 | extended | extended | yes | 0.0319 | 0.1165 | 0.00023 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 4.0 | 0.7 | 7.2 | 6.6 |
| short on four sides, rays through box edges | shift 5.7e6 | extended | extended | yes | 0.0319 | 0.1165 | 0.00023 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 1.1 | 8.9 | 10.8 |
| short on four sides, rays through box edges | rotated | extended | extended | yes | 0.0233 | 0.1165 | 0.00023 | 500.0000 | 500.0000 | -2.8e-16 |  | 1 | 0.7 | 4.2 | 8.3 | 18.4 |
| octahedron, rays through its vertices and edges | identity | any | extended | yes | 0.0186 | 0.3618 | 0.00054 | 666.6667 | 666.6667 | 0.0e+0 |  | 1 | 0.8 | 0.2 | 1.3 | 5.8 |
| cylinder segment 0.5 % | identity | extended | extended | yes | 0.0062 | 0.0037 | 0.00001 | 444.1905 | 444.1905 | -1.7e-16 | 0.0021 | 1 | 0.6 | 4.1 | 0.9 | 42.0 |
| cylinder segment 0.5 % | shift 1e2 | extended | extended | yes | 0.0062 | 0.0037 | 0.00001 | 444.1905 | 444.1905 | -5.7e-17 | 0.0021 | 1 | 0.9 | 0.8 | 1.1 | 41.7 |
| cylinder segment 0.5 % | shift 6e5 | extended | extended | yes | 0.0062 | 0.0037 | 0.00001 | 444.1905 | 444.1905 | 6.4e-13 | 0.0021 | 1 | 1.4 | 0.9 | 1.4 | 19.3 |
| cylinder segment 0.5 % | shift 5.7e6 | extended | extended | yes | 0.0062 | 0.0037 | 0.00001 | 444.1905 | 444.1905 | 3.9e-12 | 0.0021 | 1 | 4.4 | 1.1 | 0.9 | 27.2 |
| cylinder segment 0.5 % | rotated | extended | extended | yes | 0.0046 | 0.0037 | 0.00001 | 444.1905 | 444.1905 | -3.4e-16 | 0.0021 | 1 | 1.0 | 0.9 | 2.2 | 53.6 |
| spherical dome (stated sheet) | identity | extended | extended | yes | 0.0337 | 0.1042 | 0.00022 | 518.4339 | 518.4339 | 5.7e-16 | -0.0252 | 1 | 1.4 | 9.6 | 9.0 | 48.6 |
| spherical dome (stated sheet) | shift 1e2 | extended | extended | yes | 0.0337 | 0.1042 | 0.00022 | 518.4339 | 518.4339 | 5.7e-16 | -0.0252 | 1 | 2.2 | 6.0 | 25.1 | 31.8 |
| spherical dome (stated sheet) | shift 6e5 | extended | extended | yes | 0.0337 | 0.1042 | 0.00022 | 518.4339 | 518.4339 | -7.0e-13 | -0.0252 | 1 | 1.7 | 7.5 | 50.0 | 17.8 |
| spherical dome (stated sheet) | shift 5.7e6 | extended | extended | yes | 0.0337 | 0.1042 | 0.00022 | 518.4339 | 518.4339 | -7.6e-12 | -0.0252 | 1 | 2.2 | 6.9 | 18.8 | 19.8 |
| spherical dome (stated sheet) | rotated | extended | extended | yes | 0.0249 | 0.1042 | 0.00022 | 518.4339 | 518.4339 | 3.7e-8 | -0.0252 | 1 | 1.8 | 5.6 | 32.8 | 30.0 |
| shallow tray (stated sheet) | identity | extended | extended | yes | 0.0254 | 0.1734 | 0.00063 | 724.4160 | 724.4160 | -1.1e-16 | 0.0746 | 1 | 0.7 | 0.6 | 5.3 | 15.5 |
| shallow tray (stated sheet) | shift 1e2 | extended | extended | yes | 0.0254 | 0.1734 | 0.00063 | 724.4160 | 724.4160 | -3.4e-16 | 0.0746 | 1 | 1.3 | 1.0 | 6.5 | 10.5 |
| shallow tray (stated sheet) | shift 6e5 | extended | extended | yes | 0.0254 | 0.1734 | 0.00063 | 724.4160 | 724.4160 | -2.0e-13 | 0.0746 | 1 | 0.5 | 0.6 | 5.5 | 6.7 |
| shallow tray (stated sheet) | shift 5.7e6 | extended | extended | yes | 0.0254 | 0.1734 | 0.00063 | 724.4160 | 724.4160 | -2.3e-11 | 0.0746 | 1 | 3.9 | 0.5 | 5.7 | 6.7 |
| shallow tray (stated sheet) | rotated | extended | extended | yes | 0.0186 | 0.1734 | 0.00063 | 724.4161 | 724.4160 | 1.3e-7 | 0.0747 | 1 | 0.4 | 0.9 | 5.3 | 12.2 |
| ray grazes a reflex edge | identity | refused: NewPointNotOutside | refused: NewPointNotOutside | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| thick pin touching the outer edge (routed round) | identity | extended | extended | yes | 0.0271 | 0.0533 | 0.00011 | 506.1800 | 506.1800 | -1.7e-16 |  | 1 | 4.5 | 4.9 | 12.0 | 8.7 |
| thick pin touching the outer edge (routed round) | shift 1e2 | extended | extended | yes | 0.0271 | 0.0533 | 0.00011 | 506.1800 | 506.1800 | -3.5e-16 |  | 1 | 3.1 | 1.6 | 6.3 | 21.8 |
| thick pin touching the outer edge (routed round) | shift 6e5 | extended | extended | yes | 0.0271 | 0.0533 | 0.00011 | 506.1800 | 506.1800 | -6.0e-12 |  | 1 | 1.5 | 4.5 | 6.0 | 8.5 |
| thick pin touching the outer edge (routed round) | shift 5.7e6 | extended | extended | yes | 0.0271 | 0.0533 | 0.00011 | 506.1800 | 506.1800 | -2.1e-11 |  | 1 | 5.2 | 1.6 | 7.2 | 18.2 |
| thick pin touching the outer edge (routed round) | rotated | extended | extended | yes | 0.0199 | 0.0533 | 0.00011 | 506.1800 | 506.1800 | -2.3e-16 |  | 1 | 2.0 | 6.7 | 26.4 | 12.5 |
| free pin whose top lies in the strip's plane | identity | refused: CoplanarContact|CurveRule | refused: CoplanarContact | yes | 0.0271 | 0.0533 |  |  |  |  |  | 0 | 9.9 | 1.4 | 0.0 | 0.0 |
| rib on the slot wall (no way round) | identity | refused: OuterEdgeInside|InvertedQuad|CurveRule | refused: InvertedQuad | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 14.0 | 0.0 | 0.0 | 0.0 |
| sheet in the plane of a step face | identity | refused: NewPointNotOutside | refused: NewPointNotOutside | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| strip crosses another sheet | identity | refused: MeetsOtherTool | refused: MeetsOtherTool | yes | 0.0053 | 0.0025 |  |  |  |  |  | 1 | 0.7 | 1.1 | 6.9 | 0.0 |
| folded sheet, strips converge | identity | refused: MeetsItself|MeetsSheet|SheetRoleRefused | refused: MeetsItself | yes | 0.0056 | 0.0054 |  |  |  |  |  | 2 | 0.4 | 2.1 | 7.3 | 0.0 |
| several sheets, one short | identity | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 12.5 | 0.6 | 21.1 | 24.1 |
| several sheets, one short | shift 1e2 | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 0.7 | 6.9 | 1.7 | 16.0 |
| several sheets, one short | shift 6e5 | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.6 | 11.3 | 1.3 | 12.8 |
| several sheets, one short | shift 5.7e6 | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.6 | 2.5 | 6.1 | 6.1 |
| several sheets, one short | rotated | extended | extended | yes | 0.0039 | 0.0024 | 0.00000 | 500.0000 | 500.0000 | -2.3e-16 |  | 1 | 0.5 | 1.1 | 5.2 | 14.3 |
| UncutSide Outside set (no uncut part) | identity | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 0.5 | 6.5 | 11.2 |
| UncutSide Outside set (no uncut part) | shift 1e2 | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 2.9 | 0.7 | 12.1 | 12.6 |
| UncutSide Outside set (no uncut part) | shift 6e5 | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 0.5 | 4.1 | 5.3 |
| UncutSide Outside set (no uncut part) | shift 5.7e6 | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.5 | 0.7 | 3.4 | 16.8 |
| UncutSide Outside set (no uncut part) | rotated | extended | extended | yes | 0.0039 | 0.0024 | 0.00000 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 0.7 | 1.2 | 3.6 | 14.6 |
| UncutSide Inside set (no uncut part) | identity | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 0.5 | 2.7 | 10.2 |
| UncutSide Inside set (no uncut part) | shift 1e2 | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 0.6 | 0.7 | 11.6 | 7.8 |
| UncutSide Inside set (no uncut part) | shift 6e5 | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.6 | 3.6 | 4.8 | 6.9 |
| UncutSide Inside set (no uncut part) | shift 5.7e6 | extended | extended | yes | 0.0053 | 0.0025 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 2.6 | 0.6 | 5.8 | 9.4 |
| UncutSide Inside set (no uncut part) | rotated | extended | extended | yes | 0.0039 | 0.0024 | 0.00000 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 0.5 | 0.4 | 2.1 | 78.3 |
| UncutSide Outside, two shells | identity | refused: SheetRoleRefused | refused: SheetRoleRefused | yes | 0.0053 | 0.0025 |  |  |  |  |  | 1 | 0.6 | 0.4 | 2.8 | 5.9 |
