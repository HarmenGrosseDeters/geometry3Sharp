Added share = area share of the result farther than 4 offsets from the input; lost share = area share of the input farther than 4 offsets from the result; patches = connected far parts of the result, matched = within 2 alpha + 4 offsets of an input boundary vertex; patch/hole = far area over the removed area (the 4 variants use the same tau).

| input | alpha | offset | variant | verdict | vol / solid | added | lost | patches (matched) | patch / hole area | deep volume | s |
|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| hole_box_e_0.1 | d/100 | 0.001 | wrap | clean | 0.026 | 1.41% | 0% | 153 (8) | 0.437 | 2.08% | 1.11 |
| hole_box_e_0.1 | d/100 | 0.001 | snap | clean | 0.00372 | 1.08% | 0% | 155 (3) | 0.137 | - | - |
| hole_box_e_0.1 | d/100 | 0.001 | auto | clean | 0.966 | 6.59% | 0% | 1 (1) | 0.671 | - | 0.954 |
| hole_box_e_0.1 | d/100 | 0.001 | robust (Solidify) | clean | 0.945 | 5.59% | 0.01% | 1 (1) | 0.543 | - | 1.97 |
| hole_box_e_0.1 | d/100 | 0.0001 | wrap | clean | 0.00439 | 2.74% | 0% | 8 (8) | 0.51 | 45.45% | 12 |
| hole_box_e_0.1 | d/100 | 0.0001 | snap | clean | 0.00215 | 2.65% | 0% | 8 (8) | 0.515 | - | - |
| hole_box_e_0.1 | d/100 | 0.0001 | auto | clean | 0.966 | 6.92% | 0% | 1 (1) | 0.671 | - | 0.705 |
| hole_box_e_0.1 | d/100 | 0.0001 | robust (Solidify) | clean | 0.945 | 5.99% | 0.12% | 1 (1) | 0.573 | - | 2.37 |
| hole_box_e_0.1 | d/20 | 0.001 | wrap | clean | 0.0715 | 11.85% | 0% | 7 (7) | 2.33 | 52.28% | 0.312 |
| hole_box_e_0.1 | d/20 | 0.001 | snap | clean | 0.0509 | 11.5% | 0% | 8 (8) | 2.38 | - | - |
| hole_box_e_0.1 | d/20 | 0.001 | auto | clean | 0.966 | 6.59% | 0% | 1 (1) | 0.671 | - | 0.18 |
| hole_box_e_0.1 | d/20 | 0.001 | robust (Solidify) | clean | 0.945 | 5.59% | 0.01% | 1 (1) | 0.543 | - | 3.12 |
| hole_box_e_0.1 | d/20 | 0.0001 | wrap | clean | 0.0524 | 13.63% | 0% | 6 (6) | 2.39 | 93.19% | 5.14 |
| hole_box_e_0.1 | d/20 | 0.0001 | snap | clean | 0.0504 | 13.62% | 0% | 6 (6) | 2.39 | - | - |
| hole_box_e_0.1 | d/20 | 0.0001 | auto | clean | 0.966 | 6.92% | 0% | 1 (1) | 0.671 | - | 0.176 |
| hole_box_e_0.1 | d/20 | 0.0001 | robust (Solidify) | clean | 0.945 | 5.99% | 0.12% | 1 (1) | 0.573 | - | 1.82 |
| hole_box_e_0.1 | d/4 | 0.001 | wrap | clean | 0.973 | 6.66% | 0% | 1 (1) | 0.667 | 94.23% | 0.0841 |
| hole_box_e_0.1 | d/4 | 0.001 | snap | clean | 0.961 | 6.67% | 0% | 1 (1) | 0.665 | - | - |
| hole_box_e_0.1 | d/4 | 0.001 | auto | clean | 0.966 | 6.59% | 0% | 1 (1) | 0.671 | - | 0.0642 |
| hole_box_e_0.1 | d/4 | 0.001 | robust (Solidify) | clean | 0.945 | 5.59% | 0.01% | 1 (1) | 0.543 | - | 1.5 |
| hole_box_e_0.1 | d/4 | 0.0001 | wrap | clean | 0.963 | 7.01% | 0% | 1 (1) | 0.68 | 99.4% | 0.0673 |
| hole_box_e_0.1 | d/4 | 0.0001 | snap | clean | 0.961 | 7% | 0% | 1 (1) | 0.68 | - | - |
| hole_box_e_0.1 | d/4 | 0.0001 | auto | clean | 0.966 | 6.92% | 0% | 1 (1) | 0.671 | - | 0.0482 |
| hole_box_e_0.1 | d/4 | 0.0001 | robust (Solidify) | clean | 0.945 | 5.99% | 0.12% | 1 (1) | 0.573 | - | 1.69 |
| hole_box_face | d/100 | 0.001 | wrap | clean | 0.0213 | 1.2% | 0% | 93 (4) | 0.124 | 2.53% | 0.859 |
| hole_box_face | d/100 | 0.001 | snap | clean | 0.00268 | 0.89% | 0% | 107 (2) | 0.0376 | - | - |
| hole_box_face | d/100 | 0.001 | auto | clean | 1 | 24.64% | 0% | 1 (1) | 1 | - | 0.153 |
| hole_box_face | d/100 | 0.001 | robust (Repair) | clean | 1 | 24.64% | 0% | 1 (1) | 1 | - | 0.311 |
| hole_box_face | d/100 | 0.0001 | wrap | clean | 0.00338 | 2.19% | 0% | 1 (1) | 0.141 | 43.28% | 8.6 |
| hole_box_face | d/100 | 0.0001 | snap | clean | 0.00152 | 2.29% | 0% | 1 (1) | 0.143 | - | - |
| hole_box_face | d/100 | 0.0001 | auto | clean | 1 | 25.26% | 0% | 1 (1) | 1 | - | 0.255 |
| hole_box_face | d/100 | 0.0001 | robust (Repair) | clean | 1 | 25.26% | 0% | 1 (1) | 1 | - | 0.539 |
| hole_box_face | d/20 | 0.001 | wrap | clean | 0.0534 | 9.84% | 0% | 1 (1) | 0.64 | 50.23% | 0.255 |
| hole_box_face | d/20 | 0.001 | snap | clean | 0.036 | 9.72% | 0% | 1 (1) | 0.652 | - | - |
| hole_box_face | d/20 | 0.001 | auto | clean | 1 | 24.64% | 0% | 1 (1) | 1 | - | 0.061 |
| hole_box_face | d/20 | 0.001 | robust (Repair) | clean | 1 | 24.64% | 0% | 1 (1) | 1 | - | 0.315 |
| hole_box_face | d/20 | 0.0001 | wrap | clean | 0.0372 | 11.34% | 0% | 1 (1) | 0.654 | 92.99% | 1.32 |
| hole_box_face | d/20 | 0.0001 | snap | clean | 0.0355 | 11.37% | 0% | 1 (1) | 0.655 | - | - |
| hole_box_face | d/20 | 0.0001 | auto | clean | 1 | 25.26% | 0% | 1 (1) | 1 | - | 0.371 |
| hole_box_face | d/20 | 0.0001 | robust (Repair) | clean | 1 | 25.26% | 0% | 1 (1) | 1 | - | 0.368 |
| hole_box_face | d/4 | 0.001 | wrap | clean | 0.646 | 33.91% | 0% | 1 (1) | 1.63 | 92.94% | 0.0924 |
| hole_box_face | d/4 | 0.001 | snap | clean | 0.634 | 34.01% | 0% | 1 (1) | 1.63 | - | - |
| hole_box_face | d/4 | 0.001 | auto | clean | 1 | 24.64% | 0% | 1 (1) | 1 | - | 0.107 |
| hole_box_face | d/4 | 0.001 | robust (Repair) | clean | 1 | 24.64% | 0% | 1 (1) | 1 | - | 0.304 |
| hole_box_face | d/4 | 0.0001 | wrap | clean | 0.637 | 34.75% | 0% | 1 (1) | 1.62 | 99.3% | 0.18 |
| hole_box_face | d/4 | 0.0001 | snap | clean | 0.636 | 34.75% | 0% | 1 (1) | 1.63 | - | - |
| hole_box_face | d/4 | 0.0001 | auto | clean | 1 | 25.26% | 0% | 1 (1) | 1 | - | 0.0755 |
| hole_box_face | d/4 | 0.0001 | robust (Repair) | clean | 1 | 25.26% | 0% | 1 (1) | 1 | - | 0.339 |
| hole_box_p_0.01 | d/100 | 0.001 | wrap | clean | 0.0288 | 1.42% | 0% | 165 (5) | 5.13 | 2.23% | 1.55 |
| hole_box_p_0.01 | d/100 | 0.001 | snap | clean | 0.00436 | 1.22% | 0% | 179 (5) | 1.65 | - | - |
| hole_box_p_0.01 | d/100 | 0.001 | auto | clean | 1 | 0.86% | 0% | 1 (1) | 1 | - | 0.207 |
| hole_box_p_0.01 | d/100 | 0.001 | robust (Repair) | clean | 1 | 0.86% | 0% | 1 (1) | 1 | - | 0.234 |
| hole_box_p_0.01 | d/100 | 0.0001 | wrap | clean | 0.00499 | 2.94% | 0% | 7 (6) | 5.92 | 46.94% | 13.5 |
| hole_box_p_0.01 | d/100 | 0.0001 | snap | clean | 0.00253 | 3.01% | 0% | 7 (6) | 5.98 | - | - |
| hole_box_p_0.01 | d/100 | 0.0001 | auto | clean | 1 | 1.01% | 0% | 1 (1) | 1 | - | 0.52 |
| hole_box_p_0.01 | d/100 | 0.0001 | robust (Repair) | clean | 1 | 1.01% | 0% | 1 (1) | 1 | - | 0.379 |
| hole_box_p_0.01 | d/20 | 0.001 | wrap | clean | 0.0818 | 12.82% | 0% | 2 (2) | 26.9 | 52.45% | 0.264 |
| hole_box_p_0.01 | d/20 | 0.001 | snap | clean | 0.0593 | 12.62% | 0% | 2 (2) | 27.5 | - | - |
| hole_box_p_0.01 | d/20 | 0.001 | auto | clean | 1 | 0.86% | 0% | 1 (1) | 1 | - | 0.211 |
| hole_box_p_0.01 | d/20 | 0.001 | robust (Repair) | clean | 1 | 0.86% | 0% | 1 (1) | 1 | - | 0.469 |
| hole_box_p_0.01 | d/20 | 0.0001 | wrap | clean | 0.0612 | 14.46% | 0% | 4 (3) | 27.1 | 93.77% | 1.77 |
| hole_box_p_0.01 | d/20 | 0.0001 | snap | clean | 0.0589 | 14.42% | 0% | 4 (3) | 27.1 | - | - |
| hole_box_p_0.01 | d/20 | 0.0001 | auto | clean | 1 | 1.01% | 0% | 1 (1) | 1 | - | 0.272 |
| hole_box_p_0.01 | d/20 | 0.0001 | robust (Repair) | clean | 1 | 1.01% | 0% | 1 (1) | 1 | - | 0.0817 |
| hole_box_p_0.01 | d/4 | 0.001 | wrap | clean | 1.01 | 0.89% | 0% | 0 (0) | 0 | 93.91% | 0.0446 |
| hole_box_p_0.01 | d/4 | 0.001 | snap | clean | 1 | 0.88% | 0% | 0 (0) | 0 | - | - |
| hole_box_p_0.01 | d/4 | 0.001 | auto | clean | 1 | 0.86% | 0% | 1 (1) | 1 | - | 0.137 |
| hole_box_p_0.01 | d/4 | 0.001 | robust (Repair) | clean | 1 | 0.86% | 0% | 1 (1) | 1 | - | 0.126 |
| hole_box_p_0.01 | d/4 | 0.0001 | wrap | clean | 1 | 0.97% | 0% | 0 (0) | 0 | 99.38% | 0.0329 |
| hole_box_p_0.01 | d/4 | 0.0001 | snap | clean | 1 | 0.97% | 0% | 0 (0) | 0 | - | - |
| hole_box_p_0.01 | d/4 | 0.0001 | auto | clean | 1 | 1.01% | 0% | 1 (1) | 1 | - | 0.0505 |
| hole_box_p_0.01 | d/4 | 0.0001 | robust (Repair) | clean | 1 | 1.01% | 0% | 1 (1) | 1 | - | 0.106 |
| hole_box_p_0.1 | d/100 | 0.001 | wrap | clean | 0.0265 | 1.69% | 0% | 180 (21) | 0.534 | 2.77% | 1.53 |
| hole_box_p_0.1 | d/100 | 0.001 | snap | clean | 0.00429 | 1.35% | 0% | 179 (19) | 0.164 | - | - |
| hole_box_p_0.1 | d/100 | 0.001 | auto | clean | 1 | 9.53% | 0% | 1 (1) | 1 | - | 0.694 |
| hole_box_p_0.1 | d/100 | 0.001 | robust (Repair) | clean | 1 | 9.53% | 0% | 1 (1) | 1 | - | 0.611 |
| hole_box_p_0.1 | d/100 | 0.0001 | wrap | clean | 0.00476 | 3.27% | 0% | 14 (11) | 0.622 | 47.83% | 13.3 |
| hole_box_p_0.1 | d/100 | 0.0001 | snap | clean | 0.00253 | 3.28% | 0% | 14 (11) | 0.628 | - | - |
| hole_box_p_0.1 | d/100 | 0.0001 | auto | clean | 1 | 10% | 0% | 1 (1) | 1 | - | 0.111 |
| hole_box_p_0.1 | d/100 | 0.0001 | robust (Repair) | clean | 1 | 10% | 0% | 1 (1) | 1 | - | 0.173 |
| hole_box_p_0.1 | d/20 | 0.001 | wrap | clean | 0.0791 | 13.91% | 0% | 5 (5) | 2.75 | 54.14% | 0.339 |
| hole_box_p_0.1 | d/20 | 0.001 | snap | clean | 0.0587 | 13.64% | 0% | 5 (5) | 2.79 | - | - |
| hole_box_p_0.1 | d/20 | 0.001 | auto | clean | 1 | 9.53% | 0% | 1 (1) | 1 | - | 0.237 |
| hole_box_p_0.1 | d/20 | 0.001 | robust (Repair) | clean | 1 | 9.53% | 0% | 1 (1) | 1 | - | 0.202 |
| hole_box_p_0.1 | d/20 | 0.0001 | wrap | clean | 0.0606 | 16.21% | 0% | 8 (8) | 2.83 | 94.04% | 1.63 |
| hole_box_p_0.1 | d/20 | 0.0001 | snap | clean | 0.0586 | 16.14% | 0% | 8 (8) | 2.83 | - | - |
| hole_box_p_0.1 | d/20 | 0.0001 | auto | clean | 1 | 10% | 0% | 1 (1) | 1 | - | 0.654 |
| hole_box_p_0.1 | d/20 | 0.0001 | robust (Repair) | clean | 1 | 10% | 0% | 1 (1) | 1 | - | 0.429 |
| hole_box_p_0.1 | d/4 | 0.001 | wrap | clean | 1.01 | 9.5% | 0% | 2 (2) | 1.03 | 94.32% | 0.198 |
| hole_box_p_0.1 | d/4 | 0.001 | snap | clean | 1 | 9.53% | 0% | 2 (2) | 1.03 | - | - |
| hole_box_p_0.1 | d/4 | 0.001 | auto | clean | 1 | 9.53% | 0% | 1 (1) | 1 | - | 0.0709 |
| hole_box_p_0.1 | d/4 | 0.001 | robust (Repair) | clean | 1 | 9.53% | 0% | 1 (1) | 1 | - | 0.34 |
| hole_box_p_0.1 | d/4 | 0.0001 | wrap | clean | 1 | 9.95% | 0% | 2 (2) | 0.966 | 99.43% | 0.0556 |
| hole_box_p_0.1 | d/4 | 0.0001 | snap | clean | 1 | 9.95% | 0% | 2 (2) | 0.966 | - | - |
| hole_box_p_0.1 | d/4 | 0.0001 | auto | clean | 1 | 10% | 0% | 1 (1) | 1 | - | 0.0671 |
| hole_box_p_0.1 | d/4 | 0.0001 | robust (Repair) | clean | 1 | 10% | 0% | 1 (1) | 1 | - | 0.151 |
| hole_box_p_0.3 | d/100 | 0.001 | wrap | clean | 0.0208 | 1.87% | 0% | 168 (23) | 0.151 | 2.96% | 0.869 |
| hole_box_p_0.3 | d/100 | 0.001 | snap | clean | 0.00361 | 1.52% | 0% | 161 (18) | 0.0474 | - | - |
| hole_box_p_0.3 | d/100 | 0.001 | auto | clean | 1 | 28.9% | 0% | 2 (2) | 1 | - | 0.0678 |
| hole_box_p_0.3 | d/100 | 0.001 | robust (Solidify) | clean | 0.76 | 28.32% | 0.02% | 3 (3) | 0.941 | - | 2.12 |
| hole_box_p_0.3 | d/100 | 0.0001 | wrap | clean | 0.0039 | 3.82% | 0% | 39 (37) | 0.186 | 50% | 10.4 |
| hole_box_p_0.3 | d/100 | 0.0001 | snap | clean | 0.00217 | 3.78% | 0% | 39 (37) | 0.187 | - | - |
| hole_box_p_0.3 | d/100 | 0.0001 | auto | clean | 1 | 29.95% | 0% | 2 (2) | 1 | - | 0.117 |
| hole_box_p_0.3 | d/100 | 0.0001 | robust (Solidify) | clean | 0.76 | 29.48% | 0.29% | 1 (1) | 0.973 | - | 2.09 |
| hole_box_p_0.3 | d/20 | 0.001 | wrap | clean | 0.0663 | 16.48% | 0% | 22 (22) | 0.871 | 58.3% | 0.302 |
| hole_box_p_0.3 | d/20 | 0.001 | snap | clean | 0.0504 | 16.18% | 0% | 22 (22) | 0.878 | - | - |
| hole_box_p_0.3 | d/20 | 0.001 | auto | clean | 1 | 28.9% | 0% | 2 (2) | 1 | - | 0.865 |
| hole_box_p_0.3 | d/20 | 0.001 | robust (Solidify) | clean | 0.76 | 28.32% | 0.02% | 3 (3) | 0.941 | - | 4.54 |
| hole_box_p_0.3 | d/20 | 0.0001 | wrap | clean | 0.0516 | 19.41% | 0% | 25 (25) | 0.897 | 94.57% | 1.7 |
| hole_box_p_0.3 | d/20 | 0.0001 | snap | clean | 0.05 | 19.38% | 0% | 25 (25) | 0.899 | - | - |
| hole_box_p_0.3 | d/20 | 0.0001 | auto | clean | 1 | 29.95% | 0% | 2 (2) | 1 | - | 0.124 |
| hole_box_p_0.3 | d/20 | 0.0001 | robust (Solidify) | clean | 0.76 | 29.48% | 0.29% | 1 (1) | 0.973 | - | 1.61 |
| hole_box_p_0.3 | d/4 | 0.001 | wrap | clean | 0.64 | 39.3% | 0% | 4 (4) | 1.56 | 93.24% | 0.0722 |
| hole_box_p_0.3 | d/4 | 0.001 | snap | clean | 0.628 | 39.39% | 0% | 4 (4) | 1.56 | - | - |
| hole_box_p_0.3 | d/4 | 0.001 | auto | clean | 1 | 28.9% | 0% | 2 (2) | 1 | - | 0.125 |
| hole_box_p_0.3 | d/4 | 0.001 | robust (Solidify) | clean | 0.76 | 28.32% | 0.02% | 3 (3) | 0.941 | - | 2.01 |
| hole_box_p_0.3 | d/4 | 0.0001 | wrap | clean | 0.626 | 40.67% | 0% | 5 (5) | 1.6 | 99.37% | 0.192 |
| hole_box_p_0.3 | d/4 | 0.0001 | snap | clean | 0.624 | 40.67% | 0% | 5 (5) | 1.6 | - | - |
| hole_box_p_0.3 | d/4 | 0.0001 | auto | clean | 1 | 29.95% | 0% | 2 (2) | 1 | - | 0.278 |
| hole_box_p_0.3 | d/4 | 0.0001 | robust (Solidify) | clean | 0.76 | 29.48% | 0.29% | 1 (1) | 0.973 | - | 2.09 |
| hole_box_slit | d/100 | 0.001 | wrap | clean | 0.0288 | 1.52% | 0% | 163 (1) | 5.61 | 3.03% | 1.59 |
| hole_box_slit | d/100 | 0.001 | snap | clean | 0.00436 | 1.23% | 0% | 183 (0) | 1.76 | - | - |
| hole_box_slit | d/100 | 0.001 | auto | clean | 1 | 0.81% | 0% | 1 (1) | 1 | - | 0.595 |
| hole_box_slit | d/100 | 0.001 | robust (Repair) | clean | 1 | 0.81% | 0% | 1 (1) | 1 | - | 0.592 |
| hole_box_slit | d/100 | 0.0001 | wrap | clean | 0.005 | 2.82% | 0% | 5 (4) | 6.32 | 46.94% | 14.5 |
| hole_box_slit | d/100 | 0.0001 | snap | clean | 0.00254 | 2.79% | 0% | 5 (4) | 6.38 | - | - |
| hole_box_slit | d/100 | 0.0001 | auto | clean | 1 | 0.94% | 0% | 1 (1) | 1 | - | 0.131 |
| hole_box_slit | d/100 | 0.0001 | robust (Repair) | clean | 1 | 0.94% | 0% | 1 (1) | 1 | - | 0.109 |
| hole_box_slit | d/20 | 0.001 | wrap | clean | 1.01 | 0.8% | 0% | 1 (1) | 1.25 | 93.81% | 0.141 |
| hole_box_slit | d/20 | 0.001 | snap | clean | 1 | 0.8% | 0% | 1 (1) | 1.11 | - | - |
| hole_box_slit | d/20 | 0.001 | auto | clean | 1 | 0.81% | 0% | 1 (1) | 1 | - | 0.283 |
| hole_box_slit | d/20 | 0.001 | robust (Repair) | clean | 1 | 0.81% | 0% | 1 (1) | 1 | - | 0.203 |
| hole_box_slit | d/20 | 0.0001 | wrap | clean | 1 | 0.93% | 0% | 1 (1) | 1.09 | 99.37% | 0.164 |
| hole_box_slit | d/20 | 0.0001 | snap | clean | 1 | 0.92% | 0% | 1 (1) | 1.09 | - | - |
| hole_box_slit | d/20 | 0.0001 | auto | clean | 1 | 0.94% | 0% | 1 (1) | 1 | - | 0.399 |
| hole_box_slit | d/20 | 0.0001 | robust (Repair) | clean | 1 | 0.94% | 0% | 1 (1) | 1 | - | 0.183 |
| hole_box_slit | d/4 | 0.001 | wrap | clean | 1.01 | 0.77% | 0% | 1 (1) | 0.481 | 93.91% | 0.0958 |
| hole_box_slit | d/4 | 0.001 | snap | clean | 1 | 0.77% | 0% | 1 (1) | 0.481 | - | - |
| hole_box_slit | d/4 | 0.001 | auto | clean | 1 | 0.81% | 0% | 1 (1) | 1 | - | 0.381 |
| hole_box_slit | d/4 | 0.001 | robust (Repair) | clean | 1 | 0.81% | 0% | 1 (1) | 1 | - | 0.395 |
| hole_box_slit | d/4 | 0.0001 | wrap | clean | 1 | 0.97% | 0% | 2 (2) | 1.46 | 99.38% | 0.131 |
| hole_box_slit | d/4 | 0.0001 | snap | clean | 1 | 0.97% | 0% | 2 (2) | 1.46 | - | - |
| hole_box_slit | d/4 | 0.0001 | auto | clean | 1 | 0.94% | 0% | 1 (1) | 1 | - | 0.0926 |
| hole_box_slit | d/4 | 0.0001 | robust (Repair) | clean | 1 | 0.94% | 0% | 1 (1) | 1 | - | 0.267 |
| hole_cylinder_p_0.1 | d/100 | 0.001 | wrap | clean | 0.0204 | 0.66% | 0% | 64 (4) | 0.215 | 1.24% | 1 |
| hole_cylinder_p_0.1 | d/100 | 0.001 | snap | clean | 0.00195 | 0.55% | 0% | 86 (5) | 0.0728 | - | - |
| hole_cylinder_p_0.1 | d/100 | 0.001 | auto | clean | 1 | 9.85% | 0% | 1 (1) | 0.998 | - | 0.194 |
| hole_cylinder_p_0.1 | d/100 | 0.001 | robust (Repair) | clean | 1 | 9.85% | 0% | 1 (1) | 0.998 | - | 0.266 |
| hole_cylinder_p_0.1 | d/100 | 0.0001 | wrap | clean | 0.00279 | 1.27% | 0% | 2 (1) | 0.245 | 37.78% | 13.7 |
| hole_cylinder_p_0.1 | d/100 | 0.0001 | snap | clean | 0.000922 | 1.35% | 0% | 2 (1) | 0.247 | - | - |
| hole_cylinder_p_0.1 | d/100 | 0.0001 | auto | clean | 1 | 10.17% | 0% | 1 (1) | 1 | - | 0.0799 |
| hole_cylinder_p_0.1 | d/100 | 0.0001 | robust (Repair) | clean | 1 | 10.17% | 0% | 1 (1) | 1 | - | 0.116 |
| hole_cylinder_p_0.1 | d/20 | 0.001 | wrap | clean | 0.0395 | 5.75% | 0% | 2 (1) | 1.13 | 34.38% | 0.384 |
| hole_cylinder_p_0.1 | d/20 | 0.001 | snap | clean | 0.022 | 5.55% | 0% | 2 (1) | 1.15 | - | - |
| hole_cylinder_p_0.1 | d/20 | 0.001 | auto | clean | 1 | 9.85% | 0% | 1 (1) | 0.998 | - | 0.0607 |
| hole_cylinder_p_0.1 | d/20 | 0.001 | robust (Repair) | clean | 1 | 9.85% | 0% | 1 (1) | 0.998 | - | 0.159 |
| hole_cylinder_p_0.1 | d/20 | 0.0001 | wrap | clean | 0.0221 | 9.06% | 0% | 148 (15) | 1.22 | 88.96% | 2.26 |
| hole_cylinder_p_0.1 | d/20 | 0.0001 | snap | clean | 0.0203 | 6.95% | 0% | 2 (1) | 1.15 | - | - |
| hole_cylinder_p_0.1 | d/20 | 0.0001 | auto | clean | 1 | 10.17% | 0% | 1 (1) | 1 | - | 0.0494 |
| hole_cylinder_p_0.1 | d/20 | 0.0001 | robust (Repair) | clean | 1 | 10.17% | 0% | 1 (1) | 1 | - | 0.067 |
| hole_cylinder_p_0.1 | d/4 | 0.001 | wrap | clean | 1.01 | 9.74% | 0% | 1 (1) | 0.992 | 95.13% | 0.11 |
| hole_cylinder_p_0.1 | d/4 | 0.001 | snap | clean | 1 | 9.78% | 0% | 1 (1) | 0.996 | - | - |
| hole_cylinder_p_0.1 | d/4 | 0.001 | auto | clean | 1 | 9.85% | 0% | 1 (1) | 0.998 | - | 0.267 |
| hole_cylinder_p_0.1 | d/4 | 0.001 | robust (Repair) | clean | 1 | 9.85% | 0% | 1 (1) | 0.998 | - | 0.124 |
| hole_cylinder_p_0.1 | d/4 | 0.0001 | wrap | clean | 1 | 10.17% | 0% | 1 (1) | 0.999 | 99.56% | 0.139 |
| hole_cylinder_p_0.1 | d/4 | 0.0001 | snap | clean | 1 | 10.19% | 0% | 1 (1) | 0.999 | - | - |
| hole_cylinder_p_0.1 | d/4 | 0.0001 | auto | clean | 1 | 10.17% | 0% | 1 (1) | 1 | - | 0.0881 |
| hole_cylinder_p_0.1 | d/4 | 0.0001 | robust (Repair) | clean | 1 | 10.17% | 0% | 1 (1) | 1 | - | 0.134 |
| hole_sphere_p_0.01 | d/100 | 0.001 | wrap | clean | 0.0209 | 0% | 0% | 0 (0) | 0 | 0% | 0.693 |
| hole_sphere_p_0.01 | d/100 | 0.001 | snap | clean | 0.00111 | 0% | 0% | 0 (0) | 0 | - | - |
| hole_sphere_p_0.01 | d/100 | 0.001 | auto | clean | 1 | 0.91% | 0% | 1 (1) | 0.992 | - | 0.101 |
| hole_sphere_p_0.01 | d/100 | 0.001 | robust (Repair) | clean | 1 | 0.91% | 0% | 1 (1) | 0.992 | - | 0.0991 |
| hole_sphere_p_0.01 | d/100 | 0.0001 | wrap | clean | 0.00217 | 0.01% | 0% | 6 (6) | 0.0229 | 0% | 6.62 |
| hole_sphere_p_0.01 | d/100 | 0.0001 | snap | clean | 0.000165 | 0.01% | 0% | 6 (6) | 0.022 | - | - |
| hole_sphere_p_0.01 | d/100 | 0.0001 | auto | clean | 1 | 1.03% | 0% | 1 (1) | 0.992 | - | 0.0376 |
| hole_sphere_p_0.01 | d/100 | 0.0001 | robust (Repair) | clean | 1 | 1.03% | 0% | 1 (1) | 0.992 | - | 0.0608 |
| hole_sphere_p_0.01 | d/20 | 0.001 | wrap | clean | 0.0243 | 0.21% | 0% | 7 (7) | 0.49 | 0% | 0.251 |
| hole_sphere_p_0.01 | d/20 | 0.001 | snap | clean | 0.00472 | 0.18% | 0% | 7 (7) | 0.445 | - | - |
| hole_sphere_p_0.01 | d/20 | 0.001 | auto | clean | 1 | 0.91% | 0% | 1 (1) | 0.992 | - | 0.0625 |
| hole_sphere_p_0.01 | d/20 | 0.001 | robust (Repair) | clean | 1 | 0.91% | 0% | 1 (1) | 0.992 | - | 0.121 |
| hole_sphere_p_0.01 | d/20 | 0.0001 | wrap | clean | 0.00337 | 8.56% | 0% | 938 (68) | 17.2 | 16.67% | 1.59 |
| hole_sphere_p_0.01 | d/20 | 0.0001 | snap | clean | 0.00136 | 4.21% | 0% | 630 (47) | 8.74 | - | - |
| hole_sphere_p_0.01 | d/20 | 0.0001 | auto | clean | 1 | 1.03% | 0% | 1 (1) | 0.992 | - | 0.0427 |
| hole_sphere_p_0.01 | d/20 | 0.0001 | robust (Repair) | clean | 1 | 1.03% | 0% | 1 (1) | 0.992 | - | 0.102 |
| hole_sphere_p_0.01 | d/4 | 0.001 | wrap | clean | 1.01 | 0.92% | 0% | 1 (1) | 0.917 | 95.06% | 0.109 |
| hole_sphere_p_0.01 | d/4 | 0.001 | snap | clean | 0.999 | 0.91% | 0% | 1 (1) | 0.915 | - | - |
| hole_sphere_p_0.01 | d/4 | 0.001 | auto | clean | 1 | 0.91% | 0% | 1 (1) | 0.992 | - | 0.0778 |
| hole_sphere_p_0.01 | d/4 | 0.001 | robust (Repair) | clean | 1 | 0.91% | 0% | 1 (1) | 0.992 | - | 0.171 |
| hole_sphere_p_0.01 | d/4 | 0.0001 | wrap | clean | 1 | 1.84% | 0.87% | 12 (12) | 1.77 | 99.5% | 0.332 |
| hole_sphere_p_0.01 | d/4 | 0.0001 | snap | clean | 1 | 1.43% | 0.42% | 4 (4) | 1.09 | - | - |
| hole_sphere_p_0.01 | d/4 | 0.0001 | auto | clean | 1 | 1.03% | 0% | 1 (1) | 0.992 | - | 0.0492 |
| hole_sphere_p_0.01 | d/4 | 0.0001 | robust (Repair) | clean | 1 | 1.03% | 0% | 1 (1) | 0.992 | - | 0.0772 |
| hole_sphere_p_0.1 | d/100 | 0.001 | wrap | clean | 0.0186 | 0.02% | 0% | 3 (3) | 0.00565 | 0% | 0.875 |
| hole_sphere_p_0.1 | d/100 | 0.001 | snap | clean | 0.000969 | 0.02% | 0% | 3 (3) | 0.00492 | - | - |
| hole_sphere_p_0.1 | d/100 | 0.001 | auto | clean | 0.979 | 8.81% | 0% | 1 (1) | 0.921 | - | 0.0464 |
| hole_sphere_p_0.1 | d/100 | 0.001 | robust (Repair) | clean | 0.979 | 8.81% | 0% | 1 (1) | 0.921 | - | 0.319 |
| hole_sphere_p_0.1 | d/100 | 0.0001 | wrap | clean | 0.00193 | 0.05% | 0% | 12 (12) | 0.0143 | 0% | 9.44 |
| hole_sphere_p_0.1 | d/100 | 0.0001 | snap | clean | 0.000144 | 0.06% | 0% | 12 (12) | 0.0143 | - | - |
| hole_sphere_p_0.1 | d/100 | 0.0001 | auto | clean | 0.979 | 9.26% | 0% | 1 (1) | 0.921 | - | 0.194 |
| hole_sphere_p_0.1 | d/100 | 0.0001 | robust (Repair) | clean | 0.979 | 9.26% | 0% | 1 (1) | 0.921 | - | 0.373 |
| hole_sphere_p_0.1 | d/20 | 0.001 | wrap | clean | 0.0217 | 0.51% | 0% | 8 (8) | 0.112 | 0.39% | 0.2 |
| hole_sphere_p_0.1 | d/20 | 0.001 | snap | clean | 0.00416 | 0.44% | 0% | 6 (6) | 0.105 | - | - |
| hole_sphere_p_0.1 | d/20 | 0.001 | auto | clean | 0.979 | 8.81% | 0% | 1 (1) | 0.921 | - | 0.156 |
| hole_sphere_p_0.1 | d/20 | 0.001 | robust (Repair) | clean | 0.979 | 8.81% | 0% | 1 (1) | 0.921 | - | 0.388 |
| hole_sphere_p_0.1 | d/20 | 0.0001 | wrap | clean | 0.00298 | 8.7% | 0% | 861 (138) | 1.69 | 11.11% | 1.7 |
| hole_sphere_p_0.1 | d/20 | 0.0001 | snap | clean | 0.00118 | 4.67% | 0% | 570 (110) | 0.89 | - | - |
| hole_sphere_p_0.1 | d/20 | 0.0001 | auto | clean | 0.979 | 9.26% | 0% | 1 (1) | 0.921 | - | 0.0583 |
| hole_sphere_p_0.1 | d/20 | 0.0001 | robust (Repair) | clean | 0.979 | 9.26% | 0% | 1 (1) | 0.921 | - | 0.251 |
| hole_sphere_p_0.1 | d/4 | 0.001 | wrap | clean | 0.99 | 8.76% | 0% | 1 (1) | 0.924 | 95.27% | 0.0633 |
| hole_sphere_p_0.1 | d/4 | 0.001 | snap | clean | 0.981 | 8.8% | 0% | 1 (1) | 0.92 | - | - |
| hole_sphere_p_0.1 | d/4 | 0.001 | auto | clean | 0.979 | 8.81% | 0% | 1 (1) | 0.921 | - | 0.0415 |
| hole_sphere_p_0.1 | d/4 | 0.001 | robust (Repair) | clean | 0.979 | 8.81% | 0% | 1 (1) | 0.921 | - | 0.239 |
| hole_sphere_p_0.1 | d/4 | 0.0001 | wrap | clean | 0.982 | 10.15% | 0.98% | 9 (9) | 0.958 | 99.54% | 0.292 |
| hole_sphere_p_0.1 | d/4 | 0.0001 | snap | clean | 0.981 | 9.6% | 0.4% | 3 (3) | 0.925 | - | - |
| hole_sphere_p_0.1 | d/4 | 0.0001 | auto | clean | 0.979 | 9.26% | 0% | 1 (1) | 0.921 | - | 0.0368 |
| hole_sphere_p_0.1 | d/4 | 0.0001 | robust (Repair) | clean | 0.979 | 9.26% | 0% | 1 (1) | 0.921 | - | 0.26 |
| hole_sphere_p_0.3 | d/100 | 0.001 | wrap | clean | 0.0136 | 0.15% | 0% | 17 (17) | 0.0104 | 0% | 1.15 |
| hole_sphere_p_0.3 | d/100 | 0.001 | snap | clean | 0.00068 | 0.14% | 0% | 17 (17) | 0.00926 | - | - |
| hole_sphere_p_0.3 | d/100 | 0.001 | auto | clean | 0.804 | 22.92% | 0% | 1 (1) | 0.724 | - | 0.0607 |
| hole_sphere_p_0.3 | d/100 | 0.001 | robust (Solidify) | clean | 0.793 | 23.15% | 0.02% | 16 (16) | 0.715 | - | 1.17 |
| hole_sphere_p_0.3 | d/100 | 0.0001 | wrap | clean | 0.00141 | 0.41% | 0% | 26 (26) | 0.0208 | 0% | 14.4 |
| hole_sphere_p_0.3 | d/100 | 0.0001 | snap | clean | 9.85E-05 | 0.38% | 0% | 25 (25) | 0.0209 | - | - |
| hole_sphere_p_0.3 | d/100 | 0.0001 | auto | clean | 0.804 | 23.76% | 0% | 1 (1) | 0.73 | - | 0.144 |
| hole_sphere_p_0.3 | d/100 | 0.0001 | robust (Solidify) | clean | 0.793 | 24.27% | 0.33% | 6 (6) | 0.749 | - | 1.84 |
| hole_sphere_p_0.3 | d/20 | 0.001 | wrap | clean | 0.0158 | 0.99% | 0% | 5 (5) | 0.0644 | 0.44% | 0.723 |
| hole_sphere_p_0.3 | d/20 | 0.001 | snap | clean | 0.00289 | 0.9% | 0% | 5 (5) | 0.0604 | - | - |
| hole_sphere_p_0.3 | d/20 | 0.001 | auto | clean | 0.804 | 22.92% | 0% | 1 (1) | 0.724 | - | 0.62 |
| hole_sphere_p_0.3 | d/20 | 0.001 | robust (Solidify) | clean | 0.793 | 23.15% | 0.02% | 16 (16) | 0.715 | - | 3.79 |
| hole_sphere_p_0.3 | d/20 | 0.0001 | wrap | clean | 0.00211 | 8.33% | 0% | 705 (175) | 0.41 | 11.76% | 1.38 |
| hole_sphere_p_0.3 | d/20 | 0.0001 | snap | clean | 0.000781 | 4.61% | 0% | 412 (105) | 0.227 | - | - |
| hole_sphere_p_0.3 | d/20 | 0.0001 | auto | clean | 0.804 | 23.76% | 0% | 1 (1) | 0.73 | - | 0.0871 |
| hole_sphere_p_0.3 | d/20 | 0.0001 | robust (Solidify) | clean | 0.793 | 24.27% | 0.33% | 6 (6) | 0.749 | - | 1.36 |
| hole_sphere_p_0.3 | d/4 | 0.001 | wrap | clean | 0.0495 | 42.1% | 0% | 6 (6) | 2.25 | 34.06% | 0.177 |
| hole_sphere_p_0.3 | d/4 | 0.001 | snap | clean | 0.0363 | 36.9% | 0% | 6 (6) | 2.1 | - | - |
| hole_sphere_p_0.3 | d/4 | 0.001 | auto | clean | 0.804 | 22.92% | 0% | 1 (1) | 0.724 | - | 0.0902 |
| hole_sphere_p_0.3 | d/4 | 0.001 | robust (Solidify) | clean | 0.793 | 23.15% | 0.02% | 16 (16) | 0.715 | - | 2.98 |
| hole_sphere_p_0.3 | d/4 | 0.0001 | wrap | clean | 0.0115 | 50.01% | 0.61% | 12 (12) | 2.48 | 68% | 0.75 |
| hole_sphere_p_0.3 | d/4 | 0.0001 | snap | clean | 0.0102 | 48.97% | 0.27% | 6 (6) | 2.47 | - | - |
| hole_sphere_p_0.3 | d/4 | 0.0001 | auto | clean | 0.804 | 23.76% | 0% | 1 (1) | 0.73 | - | 0.0978 |
| hole_sphere_p_0.3 | d/4 | 0.0001 | robust (Solidify) | clean | 0.793 | 24.27% | 0.33% | 6 (6) | 0.749 | - | 2.09 |
| sheet | d/100 | 0.001 | wrap | clean | - | 0% | 0% | 0 (0) | - | 0% | 0.39 |
| sheet | d/100 | 0.001 | snap | clean | - | 0% | 0% | 0 (0) | - | - | - |
| sheet | d/100 | 0.001 | auto | self-intersections | - | 22.32% | 0% | 1 (1) | - | - | 0.117 |
| sheet | d/100 | 0.001 | robust (Solidify) | clean | - | 48.53% | 4.27% | 27 (27) | - | - | 1.74 |
| sheet | d/100 | 0.0001 | wrap | clean | - | 0% | 0% | 0 (0) | - | 0% | 4.19 |
| sheet | d/100 | 0.0001 | snap | clean | - | 0% | 0% | 0 (0) | - | - | - |
| sheet | d/100 | 0.0001 | auto | self-intersections | - | 33.01% | 0% | 1 (1) | - | - | 0.18 |
| sheet | d/100 | 0.0001 | robust (Solidify) | clean | - | 50.73% | 6.57% | 11 (11) | - | - | 1.64 |
| sheet | d/20 | 0.001 | wrap | clean | - | 0% | 0% | 0 (0) | - | 0% | 0.238 |
| sheet | d/20 | 0.001 | snap | clean | - | 0% | 0% | 0 (0) | - | - | - |
| sheet | d/20 | 0.001 | auto | self-intersections | - | 22.32% | 0% | 1 (1) | - | - | 0.36 |
| sheet | d/20 | 0.001 | robust (Solidify) | clean | - | 48.53% | 4.27% | 27 (27) | - | - | 2.48 |
| sheet | d/20 | 0.0001 | wrap | clean | - | 0% | 0% | 0 (0) | - | 0% | 1.1 |
| sheet | d/20 | 0.0001 | snap | clean | - | 0% | 0% | 0 (0) | - | - | - |
| sheet | d/20 | 0.0001 | auto | self-intersections | - | 33.01% | 0% | 1 (1) | - | - | 1.54 |
| sheet | d/20 | 0.0001 | robust (Solidify) | clean | - | 50.73% | 6.57% | 11 (11) | - | - | 2.15 |
| sheet | d/4 | 0.001 | wrap | clean | - | 0.12% | 0% | 0 (0) | - | 0% | 0.329 |
| sheet | d/4 | 0.001 | snap | clean | - | 0.01% | 0% | 0 (0) | - | - | - |
| sheet | d/4 | 0.001 | auto | self-intersections | - | 22.32% | 0% | 1 (1) | - | - | 0.907 |
| sheet | d/4 | 0.001 | robust (Solidify) | clean | - | 48.53% | 4.27% | 27 (27) | - | - | 5.27 |
| sheet | d/4 | 0.0001 | wrap | clean | - | 17.14% | 0% | 26 (26) | - | 6.45% | 0.567 |
| sheet | d/4 | 0.0001 | snap | clean | - | 7.67% | 0% | 0 (0) | - | - | - |
| sheet | d/4 | 0.0001 | auto | self-intersections | - | 33.01% | 0% | 1 (1) | - | - | 0.208 |
| sheet | d/4 | 0.0001 | robust (Solidify) | clean | - | 50.73% | 6.57% | 11 (11) | - | - | 2.42 |
