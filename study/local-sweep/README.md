# Local procedural sweep (scale-free study, 2026-11-17)

Scenes: a unit icosphere (subdivision 3, coordinates rounded to 2^-12) plus a small part of half size 2^-r at distance 2^f on x:
v0 a clean box, v1 two overlapping boxes, v2 an open small sphere (6 triangles removed). `gen.py` + `gen_sweep.py` write them to
`benchmarks/MeshRepairBenchmark/data/ids/meshes` (not committed). Run on 26df9b7 (master 86e94029 + probe), net10.0 Release, linux x64:

    python3 scale_free_study.py run sweep_ids.txt sweep_out --configs RPQST --timeout 120
    python3 sweep_table.py <dir containing sweep_out>

Cell code in table.txt: Apply() T/F; R strict criterion, D strict with the degenerate area per own component (option D), m miss;
x a component (>= 4 triangles) of the input lost (> 25 % of its own diagonal from the result), . none.
