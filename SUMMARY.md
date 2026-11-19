floor 10, max 150; 500 meshes with every config (of 500 with a master row)

| config | min>=F | max<=G | both | valid | median p01 / p99 | worst min / max | tets vs master (median) | improver us/tet (median) | improver s (sum / max) | violating tets (sum) | selfint | input verts outside |
|---|---:|---:|---:|---:|---|---|---:|---:|---|---:|---:|---:|
| master | 498 | 0 | 0 | 500 | 11.4 / 157.8 | 9.43 / 179.0 | 1.000 | 18 | 89 / 1 | 164860 | 0 | 0 |
| p4 | 454 | 466 | 449 | 500 | 14.0 / 148.2 | 2.58 / 158.8 | 0.981 | 26 | 166 / 10 | 443 | 0 | 0 |
| p4g | 462 | 467 | 447 | 500 | 14.0 / 148.2 | 3.87 / 168.5 | 0.982 | 19 | 122 / 5 | 322 | 0 | 0 |
| p4s | 499 | 443 | 443 | 500 | 14.0 / 148.2 | 9.53 / 170.4 | 0.983 | 22 | 161 / 8 | 748 | 0 | 0 |

p4: 44 meshes reach min >= F under master but not here; 46 end below min(F, master's worst min)

p4g: 36 meshes reach min >= F under master but not here; 38 end below min(F, master's worst min)

p4s: 0 meshes reach min >= F under master but not here; 0 end below min(F, master's worst min)
