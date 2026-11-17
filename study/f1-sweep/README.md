# F1 procedural A/B (2026-11-17)

The 54 scenes of the study's sweep (generator: runs/scale-free-study, study/local-sweep/gen.py + gen_sweep.py; not committed as meshes),
run locally on this branch's library (F1 before the MinVolumeIoU gate and the extra reason constants; the decisions are the same for these
default-settings runs): `python3 scale_free_study.py run sweep_ids.txt <out> --configs RN --timeout 120`, then `merge` and `f1`.
R: with the final lost-part check, N: without (BENCH_NO_F1=1). summary.md is the `f1` output.
