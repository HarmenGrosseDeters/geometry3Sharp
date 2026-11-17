| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|

### ExactMesh.Round() against today's chain, order-randomized timing

| family | step | chains | median ms chain steps 1..k | median ms chain step k | median ms ExactMesh.Round() | ExactMesh / chain 1..k: median | p90 | max | summed ms chain 1..k / ExactMesh |
|---|---|---|---|---|---|---|---|---|---|
| abc-dui | 1 | 30 | 60 | 60 | 70 | 1.11 | 1.34 | 2.17 | 2121 / 2490 |
| abc-dui | 2 | 30 | 270 | 154 | 280 | 1.00 | 1.14 | 1.52 | 38532 / 38895 |
| abc-dui | 3 | 30 | 1754 | 1256 | 190 | 0.20 | 0.62 | 0.67 | 86620 / 35104 |
| abc-iud | 1 | 30 | 39 | 39 | 48 | 1.08 | 1.35 | 1.76 | 2415 / 2598 |
| abc-iud | 2 | 28 | 128 | 74 | 514 | 2.04 | 20.10 | 74.13 | 8053 / 33891 |
| abc-iud | 3 | 28 | 1458 | 1124 | 664 | 0.49 | 0.99 | 1.02 | 63785 / 48559 |
| abc-ucd | 1 | 30 | 88 | 88 | 110 | 1.16 | 1.65 | 2.22 | 3688 / 4424 |
| abc-ucd | 2 | 30 | 274 | 172 | 238 | 0.94 | 1.08 | 1.22 | 22454 / 22157 |
| abc-ucd | 3 | 30 | 1614 | 902 | 176 | 0.14 | 0.85 | 1.00 | 52341 / 20533 |
