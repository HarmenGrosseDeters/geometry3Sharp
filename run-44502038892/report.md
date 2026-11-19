| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|
| ucd | ours | 30 | 30/30/30 | 28/23/26 | 23 | 0 / 0 | pinches 13 | 2.4e-14/4.0e-07/5.1e-05 | 165 | 304/428/1874 |
| dui | ours | 30 | 30/30/30 | 30/30/30 | 30 | 0 / 0 | – | 6.6e-14/1.5e-08/6.0e-06 | 0 | 226/368/2330 |
| iud | ours | 30 | 30/28/28 | 30/28/23 | 23 | 2 / 0 | degenerate triangles 1, inside-out components 3, pinches 1 | 8.5e-10/1.2e-10/4.5e-10 | 1166 | 131/234/2461 |
