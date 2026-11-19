
## procedural: 12 cases

| result | clean | empty-clean | unclean |
|---|---|---|---|
| chained | 12 | 0 | 0 |
| one pass, first rounding only | 0 | 0 | 12 |
| one pass, SafeRounding | 0 | 0 | 12 |

### Q1: components of the exact result

| case | exact tris | comps (real/artefact/unclear) | lost | broken | kept ok | exact volume | rounded volume | thinnest t_avg (ulps) | thinnest t_min (ulps) | largest comp t_avg (ulps) | in X-pair results (>=0.99 of volume) | chained-exact comps (thin/total) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| mixed s3 k6 | 3244 | 1/30/0 | 30 | 1 | 0 | 0.0164 | 0.0164 | 0.00859 | 0.00859 | 2.35e+14 | - | 6/52 |
| mixed s3 k7 | 3416 | 1/30/0 | 30 | 1 | 0 | 0.0796 | 0.0796 | 0.00859 | 0.00859 | 3.64e+14 | - | 0/4 |
| mixed s6 k6 | 4052 | 4/3/0 | 3 | 1 | 3 | 0.0197 | 0.0197 | 0.00389 | 0.00389 | 2.04e+14 | X5-X0, X5-X6 | 20/138 |
| mixed s6 k7 | 4148 | 5/3/0 | 3 | 1 | 4 | 0.0905 | 0.0905 | 0.00389 | 0.00389 | 2.04e+14 | - | 0/9 |
| mixed s6 k8 | 4224 | 10/3/0 | 3 | 2 | 8 | 7.92e-05 | 7.92e-05 | 0.00389 | 0.00389 | 3.82e+11 | X8-X0, X8-X7 | 0/14 |
| mixed s9 k5 | 3380 | 12/17/0 | 17 | 1 | 11 | 0.0302 | 0.0302 | 0.0554 | 0.0554 | 2.64e+14 | - | 0/12 |
| mixed s12 k8 | 6214 | 36/69/0 | 69 | 1 | 35 | 8.28e-05 | 8.28e-05 | 0.0261 | 0.0261 | 1.99e+12 | X8-X0, X8-X7 | 0/36 |
| mixed s24 k8 | 5076 | 38/28/0 | 28 | 1 | 37 | 0.000102 | 0.000102 | 0.00475 | 0.00356 | 2.91e+12 | X8-X0, X8-X7 | 0/38 |
| mixed s30 k5 | 4398 | 1/2/0 | 2 | 1 | 0 | 0.0312 | 0.0312 | 0.00622 | 0.00622 | 1.62e+14 | - | 0/1 |
| mixed s30 k6 | 2902 | 2/0/0 | 0 | 1 | 1 | 0.0171 | 0.0171 | 1.32e+13 | 0.00198 | 1.32e+13 | - | 4/41 |
| mixed s30 k7 | 3010 | 2/0/0 | 0 | 1 | 1 | 0.0879 | 0.0879 | 2.44e+14 | 0.000992 | 2.44e+14 | - | 0/8 |
| mixed s30 k8 | 2956 | 7/0/0 | 0 | 1 | 6 | 0.00128 | 0.00128 | 1.3e+12 | 1.33e+12 | 1.32e+13 | X8-X0, X8-X7 | 0/11 |

### Q2: defects left after SafeRounding and quad flips

| case | reason after SafeRounding | defects: degenerate / intersecting pairs / triangles | quad candidates | quad-fixable | flips applied | reason after flips |
|---|---|---|---|---|---|---|
| mixed s3 k6 | degenerate triangles | 2 / 20 / 32 | 21 | 0 | 0 | degenerate triangles |
| mixed s3 k7 | degenerate triangles | 4 / 24 / 40 | 22 | 0 | 0 | degenerate triangles |
| mixed s6 k6 | degenerate triangles | 64 / 46 / 133 | 61 | 0 | 0 | degenerate triangles |
| mixed s6 k7 | degenerate triangles | 64 / 49 / 137 | 62 | 0 | 0 | degenerate triangles |
| mixed s6 k8 | degenerate triangles | 65 / 44 / 131 | 60 | 0 | 0 | degenerate triangles |
| mixed s9 k5 | degenerate triangles | 6 / 8 / 18 | 6 | 0 | 0 | degenerate triangles |
| mixed s12 k8 | degenerate triangles | 4 / 16 / 28 | 16 | 0 | 0 | degenerate triangles |
| mixed s24 k8 | degenerate triangles | 2 / 34 / 53 | 38 | 0 | 0 | degenerate triangles |
| mixed s30 k5 | degenerate triangles | 54 / 45 / 121 | 74 | 0 | 0 | degenerate triangles |
| mixed s30 k6 | degenerate triangles | 1 / 35 / 53 | 32 | 0 | 0 | degenerate triangles |
| mixed s30 k7 | degenerate triangles | 1 / 35 / 53 | 32 | 0 | 0 | degenerate triangles |
| mixed s30 k8 | degenerate triangles | 1 / 35 / 53 | 32 | 0 | 0 | degenerate triangles |

Sanity of the quad detector on the first rounding (before any repair; a sample of at most 150 candidate flips per case):

| case | defect triangles | candidates | quad-fixable in the sample | flips applied | reason after |
|---|---|---|---|---|---|
| mixed s3 k6 | 1432 | 1475 | 150 | 34 | degenerate triangles |
| mixed s3 k7 | 1432 | 1475 | 150 | 34 | degenerate triangles |
| mixed s6 k6 | 2532 | 2716 | 150 | 26 | degenerate triangles |
| mixed s6 k7 | 2532 | 2716 | 150 | 26 | degenerate triangles |
| mixed s6 k8 | 2566 | 2759 | 150 | 26 | degenerate triangles |
| mixed s9 k5 | 1134 | 1086 | 150 | 17 | degenerate triangles |
| mixed s12 k8 | 1470 | 1588 | 150 | 37 | degenerate triangles |
| mixed s24 k8 | 1848 | 2022 | 150 | 32 | degenerate triangles |
| mixed s30 k5 | 2516 | 2894 | 150 | 32 | degenerate triangles |
| mixed s30 k6 | 1718 | 1787 | 106 | 20 | degenerate triangles |
| mixed s30 k7 | 1718 | 1787 | 104 | 20 | degenerate triangles |
| mixed s30 k8 | 1732 | 1798 | 106 | 21 | degenerate triangles |

Cases with defects after SafeRounding: 12; defect triangles: 852; quad-fixable: 0; cases clean after the flips: 0.

### Q3: the exact result rounded about an origin (then the same repair)

| origin | runs | clean | empty-clean | unclean |
|---|---|---|---|---|
| world | 12 | 0 | 0 | 12 |
| bbox_centre | 12 | 5 | 0 | 7 |
| thin_vertex | 12 | 7 | 0 | 5 |
| control | 36 | 14 | 0 | 22 |

| case | origin | max abs coordinate | result | tris | components | volume / exact | removed components |
|---|---|---|---|---|---|---|---|
| mixed s3 k6 | world | 0.771 | unclean (degenerate triangles) | 1808 | 1 | 1 | 30 |
| mixed s3 k6 | bbox_centre | 0.586 | unclean (degenerate triangles) | 1810 | 1 | 1 | 30 |
| mixed s3 k6 | thin_vertex | 0.834 | unclean (degenerate triangles) | 1812 | 1 | 1 | 30 |
| mixed s3 k6 | control0 | 0.648 | unclean (degenerate triangles) | 1808 | 1 | 1 | 30 |
| mixed s3 k6 | control1 | 0.711 | clean (clean) | 1802 | 1 | 1 | 30 |
| mixed s3 k6 | control2 | 0.836 | unclean (degenerate triangles) | 1810 | 1 | 1 | 30 |
| mixed s3 k7 | world | 1.16 | unclean (degenerate triangles) | 1980 | 1 | 1 | 30 |
| mixed s3 k7 | bbox_centre | 0.778 | clean (clean) | 1988 | 1 | 1 | 30 |
| mixed s3 k7 | thin_vertex | 1.22 | unclean (degenerate triangles) | 1984 | 1 | 1 | 30 |
| mixed s3 k7 | control0 | 0.841 | clean (clean) | 1980 | 1 | 1 | 30 |
| mixed s3 k7 | control1 | 0.903 | clean (clean) | 1978 | 1 | 1 | 30 |
| mixed s3 k7 | control2 | 1.03 | clean (clean) | 1986 | 1 | 1 | 30 |
| mixed s6 k6 | world | 0.657 | unclean (degenerate triangles) | 3004 | 4 | 1 | 3 |
| mixed s6 k6 | bbox_centre | 0.578 | unclean (degenerate triangles) | 3004 | 4 | 1 | 3 |
| mixed s6 k6 | thin_vertex | 0.752 | clean (clean) | 3010 | 4 | 1 | 3 |
| mixed s6 k6 | control0 | 0.609 | unclean (degenerate triangles) | 3008 | 4 | 1 | 3 |
| mixed s6 k6 | control1 | 0.64 | unclean (degenerate triangles) | 3008 | 4 | 1 | 3 |
| mixed s6 k6 | control2 | 0.703 | clean (clean) | 3002 | 4 | 1 | 3 |
| mixed s6 k7 | world | 0.997 | unclean (degenerate triangles) | 3100 | 5 | 1 | 3 |
| mixed s6 k7 | bbox_centre | 0.632 | clean (clean) | 3102 | 5 | 1 | 3 |
| mixed s6 k7 | thin_vertex | 0.752 | clean (clean) | 3106 | 5 | 1 | 3 |
| mixed s6 k7 | control0 | 0.757 | unclean (degenerate triangles) | 3096 | 5 | 1 | 3 |
| mixed s6 k7 | control1 | 0.882 | unclean (degenerate triangles) | 3100 | 5 | 1 | 3 |
| mixed s6 k7 | control2 | 0.703 | unclean (degenerate triangles) | 3112 | 7 | 1 | 3 |
| mixed s6 k8 | world | 0.499 | unclean (degenerate triangles) | 3146 | 10 | 1 | 3 |
| mixed s6 k8 | bbox_centre | 0.499 | unclean (degenerate triangles) | 3144 | 10 | 1 | 3 |
| mixed s6 k8 | thin_vertex | 0.734 | clean (clean) | 3154 | 10 | 1 | 3 |
| mixed s6 k8 | control0 | 0.53 | unclean (degenerate triangles) | 3144 | 10 | 1 | 3 |
| mixed s6 k8 | control1 | 0.625 | clean (clean) | 3152 | 10 | 1 | 3 |
| mixed s6 k8 | control2 | 0.624 | clean (clean) | 3150 | 10 | 1 | 3 |
| mixed s9 k5 | world | 0.747 | unclean (degenerate triangles) | 2388 | 12 | 1 | 17 |
| mixed s9 k5 | bbox_centre | 0.499 | clean (clean) | 2380 | 12 | 1 | 17 |
| mixed s9 k5 | thin_vertex | 0.991 | unclean (degenerate triangles) | 2378 | 14 | 1 | 17 |
| mixed s9 k5 | control0 | 0.622 | unclean (degenerate triangles) | 2384 | 12 | 1 | 17 |
| mixed s9 k5 | control1 | 0.747 | clean (clean) | 2386 | 12 | 1 | 17 |
| mixed s9 k5 | control2 | 0.749 | clean (clean) | 2386 | 12 | 1 | 17 |
| mixed s12 k8 | world | 0.499 | unclean (degenerate triangles) | 3002 | 36 | 1 | 69 |
| mixed s12 k8 | bbox_centre | 0.498 | unclean (degenerate triangles) | 3000 | 38 | 1 | 69 |
| mixed s12 k8 | thin_vertex | 0.973 | unclean (degenerate triangles) | 2992 | 36 | 1 | 69 |
| mixed s12 k8 | control0 | 0.561 | unclean (degenerate triangles) | 3002 | 36 | 1 | 69 |
| mixed s12 k8 | control1 | 0.654 | unclean (degenerate triangles) | 3002 | 36 | 1 | 69 |
| mixed s12 k8 | control2 | 0.748 | unclean (degenerate triangles) | 3006 | 38 | 1 | 69 |
| mixed s24 k8 | world | 0.499 | unclean (degenerate triangles) | 3064 | 38 | 1 | 28 |
| mixed s24 k8 | bbox_centre | 0.498 | unclean (degenerate triangles) | 3060 | 38 | 1 | 28 |
| mixed s24 k8 | thin_vertex | 0.762 | clean (clean) | 3074 | 38 | 1 | 28 |
| mixed s24 k8 | control0 | 0.621 | unclean (degenerate triangles) | 3066 | 38 | 1 | 28 |
| mixed s24 k8 | control1 | 0.746 | unclean (degenerate triangles) | 3070 | 38 | 1 | 28 |
| mixed s24 k8 | control2 | 0.627 | unclean (degenerate triangles) | 3060 | 40 | 1 | 28 |
| mixed s30 k5 | world | 0.78 | unclean (degenerate triangles) | 3530 | 1 | 1 | 2 |
| mixed s30 k5 | bbox_centre | 0.64 | unclean (degenerate triangles) | 3530 | 1 | 1 | 2 |
| mixed s30 k5 | thin_vertex | 0.908 | unclean (degenerate triangles) | 3530 | 1 | 1 | 2 |
| mixed s30 k5 | control0 | 0.702 | unclean (degenerate triangles) | 3530 | 1 | 1 | 2 |
| mixed s30 k5 | control1 | 0.765 | unclean (degenerate triangles) | 3534 | 1 | 1 | 2 |
| mixed s30 k5 | control2 | 0.89 | unclean (degenerate triangles) | 3534 | 1 | 1 | 2 |
| mixed s30 k6 | world | 0.78 | unclean (degenerate triangles) | 2162 | 2 | 1 | 0 |
| mixed s30 k6 | bbox_centre | 0.64 | clean (clean) | 2154 | 2 | 1 | 0 |
| mixed s30 k6 | thin_vertex | 0.817 | clean (clean) | 2160 | 2 | 1 | 0 |
| mixed s30 k6 | control0 | 0.702 | unclean (degenerate triangles) | 2166 | 2 | 1 | 0 |
| mixed s30 k6 | control1 | 0.765 | clean (clean) | 2156 | 2 | 1 | 0 |
| mixed s30 k6 | control2 | 0.89 | clean (clean) | 2156 | 2 | 1 | 0 |
| mixed s30 k7 | world | 1.12 | unclean (degenerate triangles) | 2270 | 2 | 1 | 0 |
| mixed s30 k7 | bbox_centre | 0.661 | unclean (degenerate triangles) | 2264 | 6 | 1 | 0 |
| mixed s30 k7 | thin_vertex | 0.817 | clean (clean) | 2268 | 2 | 1 | 0 |
| mixed s30 k7 | control0 | 0.702 | unclean (degenerate triangles) | 2274 | 6 | 1 | 0 |
| mixed s30 k7 | control1 | 0.765 | unclean (degenerate triangles) | 2266 | 4 | 1 | 0 |
| mixed s30 k7 | control2 | 0.89 | unclean (degenerate triangles) | 2266 | 4 | 1 | 0 |
| mixed s30 k8 | world | 0.506 | unclean (degenerate triangles) | 2200 | 7 | 1 | 0 |
| mixed s30 k8 | bbox_centre | 0.5 | clean (clean) | 2194 | 7 | 1 | 0 |
| mixed s30 k8 | thin_vertex | 0.993 | clean (clean) | 2198 | 7 | 1 | 0 |
| mixed s30 k8 | control0 | 0.623 | clean (clean) | 2194 | 7 | 1 | 0 |
| mixed s30 k8 | control1 | 0.748 | clean (clean) | 2202 | 7 | 1 | 0 |
| mixed s30 k8 | control2 | 0.75 | clean (clean) | 2196 | 7 | 1 | 0 |
