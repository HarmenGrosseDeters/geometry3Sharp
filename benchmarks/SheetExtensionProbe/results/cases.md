| case | frame | expected | actual | ok | g max / diag | U | U / smaller | kept | analytic | vol err / W | kept - intent | curves | construct ms | verify ms | imprint ms | cut ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| planar 0.1 % | identity | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 42.5 | 16.3 | 90.6 | 75.9 |
| planar 0.1 % | shift 1e2 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 1.1 | 0.4 | 1.3 | 4.3 |
| planar 0.1 % | shift 6e5 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 0.3 | 0.9 | 4.4 |
| planar 0.1 % | shift 5.7e6 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 0.3 | 1.0 | 4.6 |
| planar 0.1 % | rotated | extended | extended | yes | 0.0007 | 0.0001 | 0.00000 | 500.0000 | 500.0000 | -2.8e-16 |  | 1 | 0.9 | 0.3 | 3.7 | 63.4 |
| bent 0.1 % | identity | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | -5.7e-17 |  | 1 | 1.2 | 0.4 | 1.2 | 3.3 |
| bent 0.1 % | shift 1e2 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | -1.7e-16 |  | 1 | 1.0 | 0.3 | 0.9 | 3.0 |
| bent 0.1 % | shift 6e5 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | -5.8e-13 |  | 1 | 1.0 | 0.3 | 1.1 | 3.1 |
| bent 0.1 % | shift 5.7e6 | extended | extended | yes | 0.0010 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | -4.7e-12 |  | 1 | 0.6 | 0.5 | 1.6 | 2.9 |
| bent 0.1 % | rotated | extended | extended | yes | 0.0008 | 0.0001 | 0.00000 | 462.5000 | 462.5000 | -1.7e-16 |  | 1 | 0.6 | 0.3 | 13.5 | 6.7 |
| planar 0.5 % | identity | extended | extended | yes | 0.0050 | 0.0022 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.8 | 0.3 | 3.5 | 3.2 |
| planar 0.5 % | shift 1e2 | extended | extended | yes | 0.0050 | 0.0022 | 0.00000 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 0.8 | 0.3 | 1.1 | 3.0 |
| planar 0.5 % | shift 6e5 | extended | extended | yes | 0.0050 | 0.0022 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.7 | 0.5 | 1.7 | 3.1 |
| planar 0.5 % | shift 5.7e6 | extended | extended | yes | 0.0050 | 0.0022 | 0.00000 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.8 | 0.4 | 1.1 | 2.8 |
| planar 0.5 % | rotated | extended | extended | yes | 0.0037 | 0.0022 | 0.00000 | 500.0000 | 500.0000 | -2.8e-16 |  | 1 | 0.7 | 0.3 | 1.2 | 7.0 |
| bent 0.5 % | identity | extended | extended | yes | 0.0052 | 0.0025 | 0.00001 | 462.5000 | 462.5000 | -1.7e-16 |  | 1 | 0.7 | 0.4 | 1.3 | 4.3 |
| bent 0.5 % | shift 1e2 | extended | extended | yes | 0.0052 | 0.0025 | 0.00001 | 462.5000 | 462.5000 | 1.7e-16 |  | 1 | 0.7 | 0.3 | 1.0 | 3.6 |
| bent 0.5 % | shift 6e5 | extended | extended | yes | 0.0052 | 0.0025 | 0.00001 | 462.5000 | 462.5000 | -1.2e-12 |  | 1 | 0.7 | 0.4 | 1.0 | 3.9 |
| bent 0.5 % | shift 5.7e6 | extended | extended | yes | 0.0052 | 0.0025 | 0.00001 | 462.5000 | 462.5000 | 6.8e-12 |  | 1 | 0.7 | 0.3 | 1.0 | 3.5 |
| bent 0.5 % | rotated | extended | extended | yes | 0.0038 | 0.0025 | 0.00001 | 462.5000 | 462.5000 | -5.7e-17 |  | 1 | 0.6 | 0.2 | 14.2 | 4.7 |
| planar 2 % | identity | extended | extended | yes | 0.0200 | 0.0350 | 0.00007 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 1.1 | 0.4 | 1.4 | 20.1 |
| planar 2 % | shift 1e2 | extended | extended | yes | 0.0200 | 0.0350 | 0.00007 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.8 | 0.4 | 1.4 | 3.8 |
| planar 2 % | shift 6e5 | extended | extended | yes | 0.0200 | 0.0350 | 0.00007 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.8 | 0.4 | 1.3 | 3.3 |
| planar 2 % | shift 5.7e6 | extended | extended | yes | 0.0200 | 0.0350 | 0.00007 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.8 | 0.4 | 1.2 | 3.7 |
| planar 2 % | rotated | extended | extended | yes | 0.0146 | 0.0350 | 0.00007 | 500.0000 | 500.0000 | -1.7e-16 |  | 1 | 0.7 | 0.3 | 1.5 | 5.3 |
| bent 2 % | identity | extended | extended | yes | 0.0209 | 0.0389 | 0.00008 | 462.5000 | 462.5000 | 5.7e-17 |  | 1 | 1.1 | 0.6 | 1.9 | 4.7 |
| bent 2 % | shift 1e2 | extended | extended | yes | 0.0209 | 0.0389 | 0.00008 | 462.5000 | 462.5000 | 1.7e-16 |  | 1 | 1.3 | 0.4 | 1.5 | 8.1 |
| bent 2 % | shift 6e5 | extended | extended | yes | 0.0209 | 0.0389 | 0.00008 | 462.5000 | 462.5000 | 5.7e-17 |  | 1 | 0.8 | 0.5 | 1.6 | 8.7 |
| bent 2 % | shift 5.7e6 | extended | extended | yes | 0.0209 | 0.0389 | 0.00008 | 462.5000 | 462.5000 | 6.1e-13 |  | 1 | 0.9 | 0.4 | 1.5 | 5.6 |
| bent 2 % | rotated | extended | extended | yes | 0.0154 | 0.0389 | 0.00008 | 462.5000 | 462.5000 | -4.5e-16 |  | 1 | 0.7 | 0.3 | 2.3 | 6.6 |
| planar 8 % | identity | refused: DepthAboveLimit | refused: DepthAboveLimit | yes | 0.0800 | 0.0000 |  |  | 500.0000 |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| bent 8 % | identity | refused: DepthAboveLimit | refused: DepthAboveLimit | yes | 0.0835 | 0.0000 |  |  | 462.5000 |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| slit ends in the middle | identity | refused: DepthAboveLimit | refused: DepthAboveLimit | yes | 0.3097 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| slot and fin | identity | extended | extended | yes | 0.0271 | 0.0513 | 0.00011 | 506.0000 | 506.0000 | -1.7e-16 |  | 1 | 2.5 | 0.6 | 2.5 | 28.9 |
| slot and fin | shift 1e2 | extended | extended | yes | 0.0271 | 0.0513 | 0.00011 | 506.0000 | 506.0000 | -2.9e-16 |  | 1 | 3.6 | 0.6 | 3.3 | 23.9 |
| slot and fin | shift 6e5 | extended | extended | yes | 0.0271 | 0.0513 | 0.00011 | 506.0000 | 506.0000 | -6.2e-12 |  | 1 | 1.6 | 0.5 | 2.1 | 4.6 |
| slot and fin | shift 5.7e6 | extended | extended | yes | 0.0271 | 0.0513 | 0.00011 | 506.0000 | 506.0000 | -2.3e-11 |  | 1 | 2.0 | 0.5 | 2.4 | 3.2 |
| slot and fin | rotated | extended | extended | yes | 0.0199 | 0.0513 | 0.00011 | 506.0000 | 506.0000 | 5.8e-17 |  | 1 | 1.6 | 0.7 | 2.8 | 5.3 |
| free pin in the slot | identity | refused: CurveRule | refused: CurveRule | yes | 0.0271 | 0.0513 |  |  |  |  |  | 0 | 2.3 | 0.7 | 2.8 | 0.0 |
| wide pocket (concave, re-entry) | identity | extended | extended | yes | 0.0293 | 0.0425 | 0.00011 | 400.0000 | 400.0000 | 0.0e+0 |  | 1 | 1.9 | 0.7 | 2.6 | 4.5 |
| wide pocket (concave, re-entry) | shift 1e2 | extended | extended | yes | 0.0293 | 0.0425 | 0.00011 | 400.0000 | 400.0000 | 0.0e+0 |  | 1 | 1.4 | 0.5 | 2.4 | 3.2 |
| wide pocket (concave, re-entry) | shift 6e5 | extended | extended | yes | 0.0293 | 0.0425 | 0.00011 | 400.0000 | 400.0000 | 0.0e+0 |  | 1 | 1.5 | 0.5 | 2.5 | 3.2 |
| wide pocket (concave, re-entry) | shift 5.7e6 | extended | extended | yes | 0.0293 | 0.0425 | 0.00011 | 400.0000 | 400.0000 | 0.0e+0 |  | 1 | 1.4 | 0.5 | 2.5 | 2.7 |
| wide pocket (concave, re-entry) | rotated | extended | extended | yes | 0.0214 | 0.0425 | 0.00011 | 400.0000 | 400.0000 | -1.9e-16 |  | 1 | 2.1 | 0.7 | 6.3 | 5.8 |
| notch narrower than the depth | identity | refused: InvertedQuad | refused: InvertedQuad | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.5 | 0.0 | 0.0 | 0.0 |
| rim outside, next to another part (U) | identity | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | 0.0e+0 |  | 0 | 0.2 | 0.0 | 0.0 | 2.3 |
| rim outside, next to another part (U) | shift 1e2 | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | 1.7e-16 |  | 0 | 0.3 | 0.0 | 0.0 | 2.6 |
| rim outside, next to another part (U) | shift 6e5 | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | 0.0e+0 |  | 0 | 0.2 | 0.0 | 0.0 | 3.5 |
| rim outside, next to another part (U) | shift 5.7e6 | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | 0.0e+0 |  | 0 | 0.2 | 0.0 | 0.0 | 2.1 |
| rim outside, next to another part (U) | rotated | unchanged | unchanged | yes | 0.0000 | 0.0000 | 0.00000 | 530.0000 | 530.0000 | -1.7e-16 |  | 0 | 0.3 | 0.0 | 0.0 | 6.9 |
| bowtie vertex on the rim | identity | refused: BowtieVertex | refused: BowtieVertex | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| chord across a sphere | identity | extended | extended | yes | 0.0227 | 0.0125 | 0.00006 | 221.1559 | 221.1559 | 2.2e-16 |  | 1 | 4.1 | 0.2 | 17.9 | 25.1 |
| chord across a sphere | shift 1e2 | extended | extended | yes | 0.0227 | 0.0125 | 0.00006 | 221.1559 | 221.1559 | 1.6e-16 |  | 1 | 14.0 | 0.3 | 4.8 | 44.2 |
| chord across a sphere | shift 6e5 | extended | extended | yes | 0.0227 | 0.0125 | 0.00006 | 221.1559 | 221.1559 | 5.1e-13 |  | 1 | 8.6 | 0.2 | 6.9 | 32.3 |
| chord across a sphere | shift 5.7e6 | extended | extended | yes | 0.0227 | 0.0125 | 0.00006 | 221.1559 | 221.1559 | 1.3e-11 |  | 1 | 13.2 | 0.2 | 3.2 | 22.9 |
| chord across a sphere | rotated | extended | extended | yes | 0.0167 | 0.0125 | 0.00006 | 221.1559 | 221.1559 | 2.2e-16 |  | 1 | 13.4 | 0.3 | 3.6 | 39.3 |
| rim vertex exactly on the surface | identity | extended | extended | yes | 0.0118 | 0.0080 | 0.00002 | 500.0000 | 500.0000 | 0.0e+0 |  | 2 | 3.9 | 0.2 | 1.1 | 1.4 |
| exit at 3 degrees | identity | refused: GrazingExit | refused: GrazingExit | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| exit at 20 degrees | identity | extended | extended | yes | 0.0253 | 0.0405 | 0.01180 | 3.4343 | 3.4343 | 0.0e+0 |  | 1 | 0.8 | 0.4 | 2.1 | 2.5 |
| exit at 20 degrees | shift 1e2 | extended | extended | yes | 0.0253 | 0.0405 | 0.01180 | 3.4343 | 3.4343 | -1.0e-15 |  | 1 | 2.0 | 0.3 | 1.6 | 2.3 |
| exit at 20 degrees | shift 6e5 | extended | extended | yes | 0.0253 | 0.0405 | 0.01180 | 3.4343 | 3.4343 | -1.2e-12 |  | 1 | 0.8 | 0.3 | 1.5 | 3.6 |
| exit at 20 degrees | shift 5.7e6 | extended | extended | yes | 0.0253 | 0.0405 | 0.01180 | 3.4343 | 3.4343 | 2.0e-11 |  | 1 | 0.8 | 0.3 | 1.4 | 2.2 |
| exit at 20 degrees | rotated | extended | extended | yes | 0.0215 | 0.0405 | 0.01180 | 3.4343 | 3.4343 | -7.8e-16 |  | 1 | 1.1 | 0.3 | 1.8 | 3.9 |
| short on four sides, rays through box edges | identity | extended | extended | yes | 0.0319 | 0.1025 | 0.00020 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 1.4 | 0.5 | 2.1 | 3.0 |
| short on four sides, rays through box edges | shift 1e2 | extended | extended | yes | 0.0319 | 0.1025 | 0.00020 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 1.4 | 0.5 | 1.8 | 3.0 |
| short on four sides, rays through box edges | shift 6e5 | extended | extended | yes | 0.0319 | 0.1025 | 0.00020 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 1.4 | 0.5 | 2.3 | 2.3 |
| short on four sides, rays through box edges | shift 5.7e6 | extended | extended | yes | 0.0319 | 0.1025 | 0.00020 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 1.4 | 0.5 | 1.8 | 2.5 |
| short on four sides, rays through box edges | rotated | extended | extended | yes | 0.0233 | 0.1025 | 0.00020 | 500.0000 | 500.0000 | -2.8e-16 |  | 1 | 1.2 | 0.4 | 2.6 | 4.0 |
| octahedron, rays through its vertices and edges | identity | any | extended | yes | 0.0186 | 0.2353 | 0.00035 | 666.6667 | 666.6667 | 0.0e+0 |  | 1 | 0.4 | 0.1 | 0.9 | 1.7 |
| cylinder segment 0.5 % | identity | extended | extended | yes | 0.0068 | 0.0039 | 0.00001 | 444.1905 | 444.1905 | -1.7e-16 | 0.0021 | 1 | 1.2 | 0.6 | 0.7 | 14.7 |
| cylinder segment 0.5 % | shift 1e2 | extended | extended | yes | 0.0068 | 0.0039 | 0.00001 | 444.1905 | 444.1905 | -1.1e-16 | 0.0021 | 1 | 0.9 | 1.0 | 0.7 | 8.6 |
| cylinder segment 0.5 % | shift 6e5 | extended | extended | yes | 0.0068 | 0.0039 | 0.00001 | 444.1905 | 444.1905 | 5.5e-13 | 0.0021 | 1 | 0.9 | 0.7 | 0.8 | 9.7 |
| cylinder segment 0.5 % | shift 5.7e6 | extended | extended | yes | 0.0068 | 0.0039 | 0.00001 | 444.1905 | 444.1905 | 3.2e-12 | 0.0021 | 1 | 0.9 | 0.6 | 0.6 | 10.6 |
| cylinder segment 0.5 % | rotated | extended | extended | yes | 0.0050 | 0.0039 | 0.00001 | 444.1905 | 444.1905 | -3.4e-16 | 0.0021 | 1 | 1.1 | 0.9 | 0.8 | 23.5 |
| spherical dome (stated sheet) | identity | extended | extended | yes | 0.0337 | 0.0836 | 0.00017 | 518.4339 | 518.4339 | 8.0e-16 | -0.0252 | 1 | 8.3 | 2.0 | 6.1 | 19.3 |
| spherical dome (stated sheet) | shift 1e2 | extended | extended | yes | 0.0337 | 0.0836 | 0.00017 | 518.4339 | 518.4339 | 8.0e-16 | -0.0252 | 1 | 5.6 | 1.9 | 6.3 | 14.1 |
| spherical dome (stated sheet) | shift 6e5 | extended | extended | yes | 0.0337 | 0.0836 | 0.00017 | 518.4339 | 518.4339 | -9.6e-13 | -0.0252 | 1 | 5.4 | 1.9 | 24.0 | 10.1 |
| spherical dome (stated sheet) | shift 5.7e6 | extended | extended | yes | 0.0337 | 0.0836 | 0.00017 | 518.4339 | 518.4339 | -9.1e-12 | -0.0252 | 1 | 5.3 | 1.9 | 5.9 | 8.2 |
| spherical dome (stated sheet) | rotated | extended | extended | yes | 0.0249 | 0.0836 | 0.00017 | 518.4339 | 518.4339 | 3.7e-8 | -0.0252 | 1 | 21.1 | 1.6 | 6.4 | 13.1 |
| shallow tray (stated sheet) | identity | extended | extended | yes | 0.0254 | 0.1342 | 0.00049 | 724.4160 | 724.4160 | -1.1e-16 | 0.0746 | 1 | 1.3 | 0.5 | 3.2 | 4.6 |
| shallow tray (stated sheet) | shift 1e2 | extended | extended | yes | 0.0254 | 0.1342 | 0.00049 | 724.4160 | 724.4160 | -3.4e-16 | 0.0746 | 1 | 1.3 | 0.5 | 2.0 | 2.6 |
| shallow tray (stated sheet) | shift 6e5 | extended | extended | yes | 0.0254 | 0.1342 | 0.00049 | 724.4160 | 724.4160 | -2.0e-13 | 0.0746 | 1 | 2.9 | 0.5 | 2.0 | 2.7 |
| shallow tray (stated sheet) | shift 5.7e6 | extended | extended | yes | 0.0254 | 0.1342 | 0.00049 | 724.4160 | 724.4160 | -2.5e-11 | 0.0746 | 1 | 1.3 | 0.5 | 1.9 | 3.6 |
| shallow tray (stated sheet) | rotated | extended | extended | yes | 0.0186 | 0.1342 | 0.00049 | 724.4161 | 724.4160 | 1.3e-7 | 0.0747 | 1 | 1.0 | 0.4 | 2.6 | 4.2 |
| ray grazes a reflex edge | identity | refused: NewPointNotOutside | refused: NewPointNotOutside | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| thick pin touching the outer edge (routed round) | identity | extended | extended | yes | 0.0271 | 0.0513 | 0.00011 | 506.1800 | 506.1800 | -1.7e-16 |  | 1 | 3.3 | 0.7 | 2.5 | 3.6 |
| thick pin touching the outer edge (routed round) | shift 1e2 | extended | extended | yes | 0.0271 | 0.0513 | 0.00011 | 506.1800 | 506.1800 | -3.5e-16 |  | 1 | 2.4 | 0.7 | 2.3 | 3.5 |
| thick pin touching the outer edge (routed round) | shift 6e5 | extended | extended | yes | 0.0271 | 0.0513 | 0.00011 | 506.1800 | 506.1800 | -6.0e-12 |  | 1 | 2.5 | 0.7 | 2.5 | 4.1 |
| thick pin touching the outer edge (routed round) | shift 5.7e6 | extended | extended | yes | 0.0271 | 0.0513 | 0.00011 | 506.1800 | 506.1800 | -2.1e-11 |  | 1 | 2.4 | 0.7 | 2.4 | 3.5 |
| thick pin touching the outer edge (routed round) | rotated | extended | extended | yes | 0.0199 | 0.0513 | 0.00011 | 506.1800 | 506.1800 | -1.7e-16 |  | 1 | 2.3 | 0.8 | 2.9 | 5.8 |
| free pin whose top lies in the strip's plane | identity | refused: CoplanarContact|CurveRule | refused: CoplanarContact | yes | 0.0271 | 0.0513 |  |  |  |  |  | 0 | 2.3 | 0.9 | 0.0 | 0.0 |
| rib on the slot wall (no way round) | identity | refused: OuterEdgeInside|InvertedQuad|CurveRule | refused: InvertedQuad | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 1.6 | 0.0 | 0.0 | 0.0 |
| sheet in the plane of a step face | identity | refused: NewPointNotOutside | refused: NewPointNotOutside | yes | 0.0000 | 0.0000 |  |  |  |  |  | 0 | 0.0 | 0.0 | 0.0 | 0.0 |
| strip crosses another sheet | identity | refused: MeetsOtherTool | refused: MeetsOtherTool | yes | 0.0053 | 0.0025 |  |  |  |  |  | 1 | 0.9 | 0.7 | 1.1 | 0.0 |
| folded sheet, strips converge | identity | refused: MeetsItself|MeetsSheet|SheetRoleRefused | refused: MeetsItself | yes | 0.0063 | 0.0059 |  |  |  |  |  | 2 | 0.9 | 1.6 | 1.9 | 0.0 |
| several sheets, one short | identity | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.9 | 0.4 | 1.1 | 4.5 |
| several sheets, one short | shift 1e2 | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 2.1 | 0.5 | 1.2 | 4.7 |
| several sheets, one short | shift 6e5 | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.9 | 0.4 | 1.1 | 4.4 |
| several sheets, one short | shift 5.7e6 | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.9 | 0.4 | 1.1 | 4.3 |
| several sheets, one short | rotated | extended | extended | yes | 0.0039 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 1.7e-16 |  | 1 | 0.8 | 0.4 | 1.6 | 9.4 |
| UncutSide Outside set (no uncut part) | identity | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.9 | 0.5 | 1.5 | 3.2 |
| UncutSide Outside set (no uncut part) | shift 1e2 | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 0.9 | 0.5 | 1.6 | 3.6 |
| UncutSide Outside set (no uncut part) | shift 6e5 | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 1.6 | 0.4 | 2.1 | 5.1 |
| UncutSide Outside set (no uncut part) | shift 5.7e6 | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 1.0 | 0.8 | 1.7 | 5.6 |
| UncutSide Outside set (no uncut part) | rotated | extended | extended | yes | 0.0039 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | -1.7e-16 |  | 1 | 0.8 | 0.3 | 1.2 | 6.0 |
| UncutSide Inside set (no uncut part) | identity | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 1.5 | 0.4 | 1.3 | 3.5 |
| UncutSide Inside set (no uncut part) | shift 1e2 | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | -5.7e-17 |  | 1 | 1.7 | 0.5 | 1.3 | 3.3 |
| UncutSide Inside set (no uncut part) | shift 6e5 | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.9 | 0.4 | 1.1 | 3.7 |
| UncutSide Inside set (no uncut part) | shift 5.7e6 | extended | extended | yes | 0.0053 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | 0.0e+0 |  | 1 | 0.9 | 0.4 | 1.2 | 2.9 |
| UncutSide Inside set (no uncut part) | rotated | extended | extended | yes | 0.0039 | 0.0025 | 0.00001 | 500.0000 | 500.0000 | -1.7e-16 |  | 1 | 0.8 | 0.3 | 1.4 | 5.4 |
| UncutSide Outside, two shells | identity | refused: SheetRoleRefused | refused: SheetRoleRefused | yes | 0.0053 | 0.0025 |  |  |  |  |  | 1 | 1.4 | 0.4 | 1.2 | 3.8 |
