| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|
| ucd | cgal_epeck | 12 | 12/11/9 | 9/5/1 | 1 | 0 / 3 | degenerate triangles 14, failed 3 | 2.4e-14/2.0e-14/3.9e-12 | – | 66/98/1532 |
| ucd | geogram | 12 | 12/12/12 | 10/6/1 | 1 | 0 / 0 | crash 6, degenerate triangles 9, not closed 3, pinches 1 | 3.6e-14/3.4e-11/7.0e-13 | – | 691/686/2759 |
| ucd | ours | 12 | 12/12/12 | 11/9/11 | 9 | 0 / 0 | pinches 5 | 3.4e-11/5.3e-11/3.6e-10 | 0 | 297/364/1454 |
| dui | cgal_epeck | 12 | 12/12/12 | 12/6/0 | 0 | 0 / 0 | degenerate triangles 6, not closed 6, rejected 6 | 5.7e-14/1.2e-14/1.0e-14 | – | 32/98/1182 |
| dui | geogram | 12 | 12/12/7 | 12/6/0 | 0 | 0 / 5 | crash 9, degenerate triangles 3, not closed 1 | 1.4e-14/4.0e-15/3.4e-15 | – | 721/317/430 |
| dui | ours | 12 | 12/12/12 | 12/12/12 | 12 | 0 / 0 | – | 2.1e-14/2.9e-13/5.6e-10 | 0 | 218/309/1506 |
| iud | cgal_epeck | 12 | 12/11/11 | 11/4/1 | 0 | 1 / 0 | degenerate triangles 7, failed 3, not closed 3, rejected 5 | 1.3e-14/5.3e-12/3.8e-12 | – | 17/61/1165 |
| iud | geogram | 12 | 12/11/8 | 12/5/2 | 2 | 1 / 3 | crash 6, degenerate triangles 3, not closed 3 | 9.7e-15/1.5e-14/5.0e-15 | – | 455/217/306 |
| iud | ours | 12 | 12/11/11 | 12/11/11 | 11 | 1 / 0 | – | 1.1e-14/3.4e-14/1.4e-11 | 816 | 112/210/1319 |

### ours against Geogram (threads all)

Ratio: time of ours / time of geogram for the chosen operation of the step (below 1: ours is faster); volumes agree: |difference| <= 1e-06 (vA + vB of the step's inputs).

| step | both ok | median ratio | p90 | max | volumes agree | max volume diff |
|---|---|---|---|---|---|---|
| 1 | 36 | 0.36 | 0.57 | 1.04 | 36/36 | 3.4e-11 |
| 2 | 27 | 0.66 | 1.76 | 2.85 | 27/27 | 2.4e-11 |
| 3 | 14 | 1.58 | 3.63 | 5.16 | 14/14 | 5.0e-12 |
| all | 77 | 0.49 | 1.96 | 5.16 | 77/77 | 3.4e-11 |
