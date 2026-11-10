# Positioning run (topic 6), 2026-11-10

4-core cloud machine, -j 1, master library c70e08b (+ the pre-study's benchmark modes), Manifold (pip manifold3d, current), CGAL 6.0 EPECK (Ubuntu package, `external/cgal_bool.cpp`, external binary, not distributed). Classes: `thingi-pairs` on the fixed 100-pair CGAL sample (`pairs_cgal_sample.csv`), `thingi-pairs-size` on the first 10 pairs of `pairs_size.csv`. Our times are warm (JIT warm-up in the process), the boolean alone, SafeRounding on.

```
python3 compare.py cases_sample.jsonl res_sample.jsonl --methods ours,manifold,cgal_epeck --timeout 300 -j 1
python3 compare.py cases_size10.jsonl res_size.jsonl --methods ours,manifold,cgal_epeck --timeout 900 -j 1
python3 data/thingi_pairs.py summary res_sample.jsonl res_size.jsonl
```

| class | method | pairs | operations | ok | rejected | failed / crash / timeout | clean | empty (volume-consistent) | unclean | max volume error | median ms | geomean ms | p95 ms | max ms | median result triangles |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | cgal_epeck | 100 | 300 | 300 | 0 | 0 | 225 | 24 | degenerate triangles 51 | 3.74e-14 | 81 | 63 | 364 | 529 | 8109 |
| thingi-pairs | manifold | 100 | 300 | 300 | 0 | 0 | 196 | 24 | degenerate triangles 80 | 4.11e-14 | 39 | 34 | 107 | 155 | 7069 |
| thingi-pairs | ours | 100 | 300 | 300 | 0 | 0 | 276 | 24 | – | 3.07e-10 | 221 | 196 | 669 | 1095 | 7609 |
| thingi-pairs-size | cgal_epeck | 10 | 30 | 30 | 0 | 0 | 10 | 1 | degenerate triangles 19 | 2.38e-13 | 1372 | 1488 | 3567 | 3628 | 245205 |
| thingi-pairs-size | manifold | 10 | 30 | 30 | 0 | 0 | 6 | 1 | degenerate triangles 23 | 6.93e-13 | 404 | 418 | 930 | 1065 | 244835 |
| thingi-pairs-size | ours | 10 | 30 | 30 | 0 | 0 | 29 | 1 | – | 2.83e-11 | 2349 | 2412 | 4929 | 5589 | 244876 |

ours, phase shares of the summed time, thingi-pairs (100-pair sample) (sum 77.0 s): input 2 %, setup 8 %, intersections 37 %, merge 5 %, triangulation 8 %, classification 8 %, simplify 3 %, assembly 7 %, rounding 21 %

ours, phase shares of the summed time, thingi-pairs-size (10 pairs) (sum 79.8 s): input 4 %, setup 10 %, intersections 34 %, merge 4 %, triangulation 4 %, classification 13 %, simplify 1 %, assembly 10 %, rounding 20 %
