| detector | tessellation | cases | precision (mean) | recall (mean) | F1 mean | F1 worst | radius error (mean of medians) | rod area marked (worst) | s per 10k triangles |
|---|---|---|---|---|---|---|---|---|---|
| DeFillet (paper defaults) | CAD strips | 24 | 0.50 | 0.02 | 0.05 | 0.00 | 0.374 | 0.00 | 1.3 |
| DeFillet (radius bound set) | CAD strips | 24 | 0.65 | 0.63 | 0.43 | 0.10 | 0.537 | 1.00 | 2.2 |
| curvature | CAD strips | 24 | 0.76 | 0.80 | 0.72 | 0.34 | 0.426 | 1.00 | 0.0 |
| curvature (oracle band) | CAD strips | 24 | 0.89 | 0.87 | 0.87 | 0.58 | 0.459 | 0.00 | 0.0 |
| medial ball | CAD strips | 24 | 1.00 | 1.00 | 1.00 | 0.99 | 0.080 | 0.00 | 13.0 |
| DeFillet (paper defaults) | subdivided | 6 | 0.42 | 0.77 | 0.50 | 0.17 | 0.490 | 0.85 | 0.6 |
| DeFillet (radius bound set) | subdivided | 6 | 0.36 | 0.88 | 0.48 | 0.19 | 0.049 | 0.75 | 1.2 |
| curvature | subdivided | 6 | 0.59 | 0.48 | 0.45 | 0.35 | 0.459 | 0.87 | 0.0 |
| curvature (oracle band) | subdivided | 6 | 0.52 | 0.65 | 0.55 | 0.39 | 0.781 | 0.00 | 0.0 |
| medial ball | subdivided | 6 | 1.00 | 1.00 | 1.00 | 1.00 | 0.025 | 0.00 | 5.8 |
