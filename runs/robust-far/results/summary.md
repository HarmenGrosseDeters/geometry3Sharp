# MeshRobustRepair far parts (#425) against its base (#420 head), 400 set

400 meshes; in base 400, in pr 400

| build | ran | timeout or crash | exact | solidify | wrap | repair kept |
|---|---|---|---|---|---|---|
| base | 400 | 0 | 174 | 36 | 17 | 173 |
| pr | 400 | 0 | 174 | 36 | 17 | 173 |

Retry relative to the largest component (robust_scale_retry): 6 meshes. Meshes with a share > 0 (a triangle below the degenerate area in a component of a multi-component input whose first resolves failed): 6, share min 3.14e-17, max 1.69e-06

## Meshes whose result or verdict differs

| mesh | base | pr | same result (after_hash) | retry | share |
|---|---|---|---|---|---|

0 meshes differ.
