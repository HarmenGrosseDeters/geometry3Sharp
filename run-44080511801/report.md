| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|
| ucd | geogram | 30 | 30/30/25 | 27/15/4 | 3 | 0 / 5 | degenerate triangles 14, failed 15, not closed 6, pinches 4 | 3.5e-14/1.3e-14/2.9e-06 | – | 1244/1047/2836 |
| ucd | ours | 30 | 30/30/30 | 28/23/26 | 23 | 0 / 0 | pinches 13 | 2.4e-14/4.0e-07/5.1e-05 | 165 | 378/542/2446 |
| dui | geogram | 30 | 30/30/18 | 30/15/1 | 1 | 0 / 12 | degenerate triangles 9, failed 18, not closed 5 | 9.2e-14/1.9e-14/2.7e-13 | – | 1338/1146/1784 |
| dui | ours | 30 | 30/30/30 | 30/30/30 | 30 | 0 / 0 | – | 6.6e-14/1.5e-08/6.0e-06 | 0 | 266/472/3354 |
| iud | geogram | 30 | 30/28/23 | 28/15/3 | 3 | 2 / 5 | degenerate triangles 9, failed 13, not closed 13 | 1.2e-14/1.0e-09/1.3e-14 | – | 988/618/1103 |
| iud | ours | 30 | 30/28/28 | 30/28/23 | 23 | 2 / 0 | degenerate triangles 1, inside-out components 3, pinches 1 | 8.5e-10/1.2e-10/4.5e-10 | 1166 | 146/264/3412 |

### ours against Geogram (threads 1)

Ratio: time of ours / time of geogram for the chosen operation of the step (below 1: ours is faster); volumes agree: |difference| <= 1e-06 (vA + vB of the step's inputs).

| step | both ok | median ratio | p90 | max | volumes agree | max volume diff |
|---|---|---|---|---|---|---|
| 1 | 90 | 0.24 | 0.48 | 0.72 | 90/90 | 2.4e-10 |
| 2 | 66 | 0.44 | 1.26 | 3.98 | 66/66 | 2.1e-07 |
| 3 | 42 | 1.27 | 4.44 | 5.51 | 42/42 | 4.0e-07 |
| all | 198 | 0.37 | 1.61 | 5.51 | 198/198 | 4.0e-07 |
