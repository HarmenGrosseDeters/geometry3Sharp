| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|

### ExactMesh.Round() against today's chain, order-randomized timing

| family | step | chains | median ms chain steps 1..k | median ms chain step k | median ms ExactMesh.Round() | ExactMesh / chain 1..k: median | p90 | max | summed ms chain 1..k / ExactMesh |
|---|---|---|---|---|---|---|---|---|---|
| abc-dui | 1 | 30 | 67.4 | 67.4 | 70.5 | 1.08 | 1.30 | 1.34 | 2344 / 2633 |
| abc-dui | 2 | 30 | 258.6 | 143.0 | 300.6 | 1.02 | 1.15 | 1.46 | 40410 / 40698 |
| abc-dui | 3 | 30 | 1710.9 | 1302.6 | 226.3 | 0.20 | 0.63 | 0.69 | 88225 / 36644 |
| abc-iud | 1 | 30 | 40.5 | 40.5 | 44.9 | 1.08 | 1.27 | 1.37 | 2411 / 2624 |
| abc-iud | 2 | 28 | 124.8 | 75.7 | 511.9 | 1.95 | 21.77 | 68.24 | 7858 / 33779 |
| abc-iud | 3 | 28 | 1395.4 | 1056.9 | 661.4 | 0.50 | 0.99 | 1.01 | 64809 / 50027 |
| abc-ucd | 1 | 30 | 102.4 | 102.4 | 140.9 | 1.12 | 1.45 | 1.88 | 4119 / 4804 |
| abc-ucd | 2 | 30 | 287.6 | 169.3 | 274.6 | 0.95 | 1.08 | 1.26 | 22871 / 23221 |
| abc-ucd | 3 | 30 | 1629.5 | 906.3 | 197.4 | 0.17 | 0.87 | 1.05 | 52098 / 21306 |
