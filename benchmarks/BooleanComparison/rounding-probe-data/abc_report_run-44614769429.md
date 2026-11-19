| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|

### Rounding probes (measurement only)


## abc: 6 cases

| result | clean | empty-clean | unclean |
|---|---|---|---|
| chained | 2 | 2 | 2 |
| one pass, first rounding only | 2 | 0 | 4 |
| one pass, SafeRounding | 4 | 2 | 0 |

### Q1: components of the exact result

| case | exact tris | comps (real/artefact/unclear) | lost | broken | kept ok | exact volume | rounded volume | thinnest t_avg (ulps) | thinnest t_min (ulps) | largest comp t_avg (ulps) | in X-pair results (>=0.99 of volume) | chained-exact comps (thin/total) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ucd_p0001_00007811_1474acd529dc4d0382eb1670_trimesh_003+00000698_6f0fc559e8f3422c908b6060_trimesh_001+00007811_1474acd529dc4d0382eb1670_trimesh_003 | 32448 | 147/0/0 | 3 | 0 | 144 | 3.91e-06 | 3.91e-06 | 2.12e+03 | -1 | 1.61e+12 | X0-X1, X0-X2 | 0/144 |
| ucd_p0022_00006166_12180861bc614336af906df3_trimesh_003+00009367_3cb074fee0754e5aa10d3b7c_trimesh_004+00006166_12180861bc614336af906df3_trimesh_003 | 19610 | 356/0/0 | 1 | 0 | 355 | 1.27e-06 | 1.27e-06 | 3.74e+07 | -1 | 3.76e+11 | X0-X1, X0-X2 | 0/355 |
| iud_p0008_00008865_6d7ddc2443ed4cff85df4efd_trimesh_002+00003979_30214440d29b401c8b860c42_trimesh_014+00008865_6d7ddc2443ed4cff85df4efd_trimesh_002 | 6612 | 0/97/0 | 97 | 0 | 0 | 9.83e-19 | 0 | 7.24e-08 | 7.22e-08 | 0.704 | X2-X0 | 113/113 |
| iud_p0020_00009726_bdb26e9a610c417cbccd6c20_trimesh_003+00008019_05162de0483a482586bbeb5b_trimesh_010+00008675_ae815d47c2fe41d6af40d85f_trimesh_008 | 23836 | 1/0/0 | 0 | 0 | 1 | 0.00892 | 0.00892 | 6.97e+14 | -1 | 6.97e+14 | X2-X0 | 3/4 |
| iud_p0022_00001666_2d7a230277ef4271b7ffd737_trimesh_001+00008938_82d6de9f4b4044ba9cd87a2b_trimesh_009+00001666_2d7a230277ef4271b7ffd737_trimesh_001 | 14742 | 30/0/0 | 0 | 0 | 30 | 0.000363 | 0.000363 | 9.79e+09 | -1 | 1.26e+13 | X2-X0 | 0/19 |
| iud_p0029_00008305_424b9c270c244a5f9a19b106_trimesh_000+00000707_c08c1c78b86f4ee3bcb7d9de_trimesh_003+00008305_424b9c270c244a5f9a19b106_trimesh_000 | 43306 | 0/121/0 | 121 | 0 | 0 | 4.52e-18 | 0 | 1.78e-08 | 5.68e-09 | 0.161 | X2-X0 | 112/112 |

### Q2: defects left after SafeRounding and quad flips

| case | reason after SafeRounding | defects: degenerate / intersecting pairs / triangles | quad candidates | quad-fixable | flips applied | reason after flips |
|---|---|---|---|---|---|---|

Sanity of the quad detector on the first rounding (before any repair; a sample of at most 150 candidate flips per case):

| case | defect triangles | candidates | quad-fixable in the sample | flips applied | reason after |
|---|---|---|---|---|---|
| ucd_p0022_00006166_12180861bc614336af906df3_trimesh_003+00009367_3cb074fee0754e5aa10d3b7c_trimesh_004+00006166_12180861bc614336af906df3_trimesh_003 | 2 | 2 | 2 | 2 | clean |

Cases with defects after SafeRounding: 0; defect triangles: 0; quad-fixable: 0; cases clean after the flips: 0.

### Q3: the exact result rounded about an origin (then the same repair)

| origin | runs | clean | empty-clean | unclean |
|---|---|---|---|---|
| world | 6 | 4 | 2 | 0 |
| bbox_centre | 6 | 4 | 2 | 0 |
| thin_vertex | 6 | 4 | 2 | 0 |
| control | 18 | 12 | 6 | 0 |

| case | origin | max abs coordinate | result | tris | components | volume / exact | removed components |
|---|---|---|---|---|---|---|---|
| ucd_p0001_00007811_1474acd529dc4d0382eb1670_trimesh_003+00000698_6f0fc559e8f3422c908b6060_trimesh_001+00007811_1474acd529dc4d0382eb1670_trimesh_003 | world | 0.391 | clean (clean) | 32394 | 144 | 1 | 3 |
| ucd_p0001_00007811_1474acd529dc4d0382eb1670_trimesh_003+00000698_6f0fc559e8f3422c908b6060_trimesh_001+00007811_1474acd529dc4d0382eb1670_trimesh_003 | bbox_centre | 0.391 | clean (clean) | 32394 | 144 | 1 | 3 |
| ucd_p0001_00007811_1474acd529dc4d0382eb1670_trimesh_003+00000698_6f0fc559e8f3422c908b6060_trimesh_001+00007811_1474acd529dc4d0382eb1670_trimesh_003 | thin_vertex | 0.608 | clean (clean) | 32394 | 144 | 1 | 3 |
| ucd_p0001_00007811_1474acd529dc4d0382eb1670_trimesh_003+00000698_6f0fc559e8f3422c908b6060_trimesh_001+00007811_1474acd529dc4d0382eb1670_trimesh_003 | control0 | 0.453 | clean (clean) | 32394 | 144 | 1 | 3 |
| ucd_p0001_00007811_1474acd529dc4d0382eb1670_trimesh_003+00000698_6f0fc559e8f3422c908b6060_trimesh_001+00007811_1474acd529dc4d0382eb1670_trimesh_003 | control1 | 0.516 | clean (clean) | 32394 | 144 | 1 | 3 |
| ucd_p0001_00007811_1474acd529dc4d0382eb1670_trimesh_003+00000698_6f0fc559e8f3422c908b6060_trimesh_001+00007811_1474acd529dc4d0382eb1670_trimesh_003 | control2 | 0.641 | clean (clean) | 32394 | 144 | 1 | 3 |
| ucd_p0022_00006166_12180861bc614336af906df3_trimesh_003+00009367_3cb074fee0754e5aa10d3b7c_trimesh_004+00006166_12180861bc614336af906df3_trimesh_003 | world | 0.298 | clean (clean) | 19596 | 355 | 1 | 1 |
| ucd_p0022_00006166_12180861bc614336af906df3_trimesh_003+00009367_3cb074fee0754e5aa10d3b7c_trimesh_004+00006166_12180861bc614336af906df3_trimesh_003 | bbox_centre | 0.298 | clean (clean) | 19596 | 355 | 1 | 1 |
| ucd_p0022_00006166_12180861bc614336af906df3_trimesh_003+00009367_3cb074fee0754e5aa10d3b7c_trimesh_004+00006166_12180861bc614336af906df3_trimesh_003 | thin_vertex | 0.54 | clean (clean) | 19596 | 355 | 1 | 1 |
| ucd_p0022_00006166_12180861bc614336af906df3_trimesh_003+00009367_3cb074fee0754e5aa10d3b7c_trimesh_004+00006166_12180861bc614336af906df3_trimesh_003 | control0 | 0.423 | clean (clean) | 19596 | 355 | 1 | 1 |
| ucd_p0022_00006166_12180861bc614336af906df3_trimesh_003+00009367_3cb074fee0754e5aa10d3b7c_trimesh_004+00006166_12180861bc614336af906df3_trimesh_003 | control1 | 0.548 | clean (clean) | 19596 | 355 | 1 | 1 |
| ucd_p0022_00006166_12180861bc614336af906df3_trimesh_003+00009367_3cb074fee0754e5aa10d3b7c_trimesh_004+00006166_12180861bc614336af906df3_trimesh_003 | control2 | 0.422 | clean (clean) | 19596 | 355 | 1 | 1 |
| iud_p0008_00008865_6d7ddc2443ed4cff85df4efd_trimesh_002+00003979_30214440d29b401c8b860c42_trimesh_014+00008865_6d7ddc2443ed4cff85df4efd_trimesh_002 | world | 0.485 | empty-clean (empty) | 0 | 0 | 0 | 151 |
| iud_p0008_00008865_6d7ddc2443ed4cff85df4efd_trimesh_002+00003979_30214440d29b401c8b860c42_trimesh_014+00008865_6d7ddc2443ed4cff85df4efd_trimesh_002 | bbox_centre | 0.482 | empty-clean (empty) | 0 | 0 | 0 | 151 |
| iud_p0008_00008865_6d7ddc2443ed4cff85df4efd_trimesh_002+00003979_30214440d29b401c8b860c42_trimesh_014+00008865_6d7ddc2443ed4cff85df4efd_trimesh_002 | thin_vertex | 0.789 | empty-clean (empty) | 0 | 0 | 0 | 151 |
| iud_p0008_00008865_6d7ddc2443ed4cff85df4efd_trimesh_002+00003979_30214440d29b401c8b860c42_trimesh_014+00008865_6d7ddc2443ed4cff85df4efd_trimesh_002 | control0 | 0.607 | empty-clean (empty) | 0 | 0 | 0 | 151 |
| iud_p0008_00008865_6d7ddc2443ed4cff85df4efd_trimesh_002+00003979_30214440d29b401c8b860c42_trimesh_014+00008865_6d7ddc2443ed4cff85df4efd_trimesh_002 | control1 | 0.732 | empty-clean (empty) | 0 | 0 | 0 | 151 |
| iud_p0008_00008865_6d7ddc2443ed4cff85df4efd_trimesh_002+00003979_30214440d29b401c8b860c42_trimesh_014+00008865_6d7ddc2443ed4cff85df4efd_trimesh_002 | control2 | 0.545 | empty-clean (empty) | 0 | 0 | 0 | 151 |
| iud_p0020_00009726_bdb26e9a610c417cbccd6c20_trimesh_003+00008019_05162de0483a482586bbeb5b_trimesh_010+00008675_ae815d47c2fe41d6af40d85f_trimesh_008 | world | 0.354 | clean (clean) | 23836 | 1 | 1 | 0 |
| iud_p0020_00009726_bdb26e9a610c417cbccd6c20_trimesh_003+00008019_05162de0483a482586bbeb5b_trimesh_010+00008675_ae815d47c2fe41d6af40d85f_trimesh_008 | bbox_centre | 0.354 | clean (clean) | 23836 | 1 | 1 | 0 |
| iud_p0020_00009726_bdb26e9a610c417cbccd6c20_trimesh_003+00008019_05162de0483a482586bbeb5b_trimesh_010+00008675_ae815d47c2fe41d6af40d85f_trimesh_008 | thin_vertex | 0.479 | clean (clean) | 23836 | 1 | 1 | 0 |
| iud_p0020_00009726_bdb26e9a610c417cbccd6c20_trimesh_003+00008019_05162de0483a482586bbeb5b_trimesh_010+00008675_ae815d47c2fe41d6af40d85f_trimesh_008 | control0 | 0.479 | clean (clean) | 23836 | 1 | 1 | 0 |
| iud_p0020_00009726_bdb26e9a610c417cbccd6c20_trimesh_003+00008019_05162de0483a482586bbeb5b_trimesh_010+00008675_ae815d47c2fe41d6af40d85f_trimesh_008 | control1 | 0.604 | clean (clean) | 23836 | 1 | 1 | 0 |
| iud_p0020_00009726_bdb26e9a610c417cbccd6c20_trimesh_003+00008019_05162de0483a482586bbeb5b_trimesh_010+00008675_ae815d47c2fe41d6af40d85f_trimesh_008 | control2 | 0.416 | clean (clean) | 23836 | 1 | 1 | 0 |
| iud_p0022_00001666_2d7a230277ef4271b7ffd737_trimesh_001+00008938_82d6de9f4b4044ba9cd87a2b_trimesh_009+00001666_2d7a230277ef4271b7ffd737_trimesh_001 | world | 0.47 | clean (clean) | 14742 | 30 | 1 | 0 |
| iud_p0022_00001666_2d7a230277ef4271b7ffd737_trimesh_001+00008938_82d6de9f4b4044ba9cd87a2b_trimesh_009+00001666_2d7a230277ef4271b7ffd737_trimesh_001 | bbox_centre | 0.469 | clean (clean) | 14742 | 30 | 1 | 0 |
| iud_p0022_00001666_2d7a230277ef4271b7ffd737_trimesh_001+00008938_82d6de9f4b4044ba9cd87a2b_trimesh_009+00001666_2d7a230277ef4271b7ffd737_trimesh_001 | thin_vertex | 0.937 | clean (clean) | 14742 | 30 | 1 | 0 |
| iud_p0022_00001666_2d7a230277ef4271b7ffd737_trimesh_001+00008938_82d6de9f4b4044ba9cd87a2b_trimesh_009+00001666_2d7a230277ef4271b7ffd737_trimesh_001 | control0 | 0.5 | clean (clean) | 14742 | 30 | 1 | 0 |
| iud_p0022_00001666_2d7a230277ef4271b7ffd737_trimesh_001+00008938_82d6de9f4b4044ba9cd87a2b_trimesh_009+00001666_2d7a230277ef4271b7ffd737_trimesh_001 | control1 | 0.541 | clean (clean) | 14742 | 30 | 1 | 0 |
| iud_p0022_00001666_2d7a230277ef4271b7ffd737_trimesh_001+00008938_82d6de9f4b4044ba9cd87a2b_trimesh_009+00001666_2d7a230277ef4271b7ffd737_trimesh_001 | control2 | 0.666 | clean (clean) | 14742 | 30 | 1 | 0 |
| iud_p0029_00008305_424b9c270c244a5f9a19b106_trimesh_000+00000707_c08c1c78b86f4ee3bcb7d9de_trimesh_003+00008305_424b9c270c244a5f9a19b106_trimesh_000 | world | 0.469 | empty-clean (empty) | 0 | 0 | 0 | 130 |
| iud_p0029_00008305_424b9c270c244a5f9a19b106_trimesh_000+00000707_c08c1c78b86f4ee3bcb7d9de_trimesh_003+00008305_424b9c270c244a5f9a19b106_trimesh_000 | bbox_centre | 0.469 | empty-clean (empty) | 0 | 0 | 0 | 130 |
| iud_p0029_00008305_424b9c270c244a5f9a19b106_trimesh_000+00000707_c08c1c78b86f4ee3bcb7d9de_trimesh_003+00008305_424b9c270c244a5f9a19b106_trimesh_000 | thin_vertex | 0.761 | empty-clean (empty) | 0 | 0 | 0 | 130 |
| iud_p0029_00008305_424b9c270c244a5f9a19b106_trimesh_000+00000707_c08c1c78b86f4ee3bcb7d9de_trimesh_003+00008305_424b9c270c244a5f9a19b106_trimesh_000 | control0 | 0.594 | empty-clean (empty) | 0 | 0 | 0 | 130 |
| iud_p0029_00008305_424b9c270c244a5f9a19b106_trimesh_000+00000707_c08c1c78b86f4ee3bcb7d9de_trimesh_003+00008305_424b9c270c244a5f9a19b106_trimesh_000 | control1 | 0.719 | empty-clean (empty) | 0 | 0 | 0 | 130 |
| iud_p0029_00008305_424b9c270c244a5f9a19b106_trimesh_000+00000707_c08c1c78b86f4ee3bcb7d9de_trimesh_003+00008305_424b9c270c244a5f9a19b106_trimesh_000 | control2 | 0.719 | empty-clean (empty) | 0 | 0 | 0 | 130 |
