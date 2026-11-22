| detector | tessellation | cases | precision (mean) | recall (mean) | F1 mean | F1 worst | radius error (mean of medians) | rod area marked (worst) | s per 10k triangles |
|---|---|---|---|---|---|---|---|---|---|
| DeFillet (paper defaults) | CAD strips | 24 | 0.73 | 0.28 | 0.28 | 0.00 | 0.569 | 0.00 | 1.8 |
| DeFillet (radius bound set) | CAD strips | 24 | 0.68 | 0.62 | 0.43 | 0.10 | 0.552 | 1.00 | 2.7 |
| curvature | CAD strips | 24 | 0.76 | 0.80 | 0.72 | 0.34 | 0.426 | 1.00 | 0.0 |
| curvature (oracle band) | CAD strips | 24 | 0.89 | 0.87 | 0.87 | 0.58 | 0.459 | 0.00 | 0.0 |
| medial ball | CAD strips | 24 | 1.00 | 1.00 | 1.00 | 0.99 | 0.080 | 0.00 | 13.1 |
| DeFillet (paper defaults) | subdivided | 6 | 0.42 | 0.76 | 0.49 | 0.18 | 0.417 | 0.93 | 0.7 |
| DeFillet (radius bound set) | subdivided | 6 | 0.36 | 0.93 | 0.50 | 0.19 | 0.007 | 0.74 | 1.8 |
| curvature | subdivided | 6 | 0.59 | 0.48 | 0.45 | 0.35 | 0.459 | 0.87 | 0.0 |
| curvature (oracle band) | subdivided | 6 | 0.52 | 0.65 | 0.55 | 0.39 | 0.781 | 0.03 | 0.0 |
| medial ball | subdivided | 6 | 1.00 | 1.00 | 1.00 | 1.00 | 0.025 | 0.00 | 5.9 |
