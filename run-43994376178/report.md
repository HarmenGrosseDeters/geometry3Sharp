| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|
| ucd | cgal_epeck | 30 | 30/29/25 | 25/13/2 | 2 | 0 / 5 | bowties 4, degenerate triangles 24, failed 4, not closed 4, rejected 8 | 2.4e-14/2.9e-14/3.9e-12 | – | 50/97/1305 |
| ucd | manifold | 30 | 30/30/30 | 20/11/16 | 10 | 0 / 0 | bowties 7, degenerate triangles 25, not closed 8, self-intersections 3 | 5.7e-14/5.5e-12/3.0e-06 | – | 34/37/65 |
| ucd | ours | 30 | 30/30/30 | 28/23/26 | 23 | 0 / 0 | bowties 8, not closed 5 | 2.4e-14/4.0e-07/5.1e-05 | 165 | 234/334/1907 |
| dui | cgal_epeck | 30 | 30/30/30 | 29/14/1 | 1 | 0 / 0 | degenerate triangles 19, not closed 14, rejected 13 | 6.5e-14/6.0e-14/6.3e-12 | – | 32/96/1019 |
| dui | manifold | 30 | 30/30/30 | 24/18/19 | 14 | 0 / 0 | degenerate triangles 28, not closed 1 | 1.0e-13/3.1e-14/5.3e-11 | – | 32/38/74 |
| dui | ours | 30 | 30/30/30 | 30/30/30 | 30 | 0 / 0 | – | 6.6e-14/1.5e-08/6.0e-06 | 0 | 179/289/2350 |
| iud | cgal_epeck | 30 | 30/28/28 | 26/11/2 | 1 | 2 / 0 | degenerate triangles 17, failed 8, not closed 12, rejected 10 | 1.9e-14/5.9e-10/3.8e-12 | – | 20/63/896 |
| iud | manifold | 30 | 30/28/28 | 26/22/13 | 10 | 2 / 0 | bowties 2, degenerate triangles 12, inside-out components 2, not closed 8, self-intersections 1 | 1.4e-14/8.6e-10/6.3e-11 | – | 31/28/45 |
| iud | ours | 30 | 30/28/28 | 30/28/22 | 22 | 2 / 0 | bowties 1, inside-out components 2, not closed 3 | 8.5e-10/1.2e-10/4.5e-10 | 1166 | 101/180/2296 |
