| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|
| ucd | geogram | 30 | 30/30/25 | 27/15/4 | 3 | 0 / 5 | None 1, crash 15, degenerate triangles 13, not closed 6, pinches 4 | 3.6e-14/8.5e-11/2.9e-06 | – | 594/536/1561 |
| ucd | ours | 30 | 30/30/30 | 28/23/26 | 23 | 0 / 0 | pinches 13 | 2.4e-14/4.0e-07/5.1e-05 | 165 | 228/323/1812 |
| dui | geogram | 30 | 30/30/18 | 30/15/1 | 1 | 0 / 12 | crash 20, degenerate triangles 9, not closed 3 | 8.2e-14/1.9e-14/2.7e-13 | – | 640/563/788 |
| dui | ours | 30 | 30/30/30 | 30/30/30 | 30 | 0 / 0 | – | 6.6e-14/1.5e-08/6.0e-06 | 0 | 181/282/2204 |
| iud | geogram | 30 | 30/28/22 | 28/13/3 | 3 | 2 / 6 | crash 14, degenerate triangles 10, not closed 12 | 1.3e-14/1.0e-09/9.6e-15 | – | 472/299/526 |
| iud | ours | 30 | 30/28/28 | 30/28/23 | 23 | 2 / 0 | degenerate triangles 1, inside-out components 3, pinches 1 | 8.5e-10/1.2e-10/4.5e-10 | 1166 | 111/182/2150 |

### ours against Geogram (threads all)

Ratio: time of ours / time of geogram for the chosen operation of the step (below 1: ours is faster); volumes agree: |difference| <= 1e-06 (vA + vB of the step's inputs).

| step | both ok | median ratio | p90 | max | volumes agree | max volume diff |
|---|---|---|---|---|---|---|
| 1 | 90 | 0.32 | 0.70 | 1.13 | 90/90 | 2.4e-10 |
| 2 | 65 | 0.56 | 1.47 | 2.89 | 65/65 | 2.1e-07 |
| 3 | 39 | 1.56 | 5.42 | 8.22 | 38/38 | 4.0e-07 |
| all | 194 | 0.45 | 1.69 | 8.22 | 193/193 | 4.0e-07 |
