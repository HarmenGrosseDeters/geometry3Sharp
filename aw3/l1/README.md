# L1 (oracle + touch), PR #681: raw results

- ab_l1b_24.jsonl: final build vs master cab88798 alternating on l4/list_ab.txt (ab2.py): 24/24 identical, pipeline 0.949,
  wrap 0.921, intersection 0.631, touch 1.040 (sums).
- ab_l1a_24.jsonl: the build before dropping the redundant slack term (same picture).
- l1_269.jsonl: final build on l4/list_269.txt, against l4/l4_269.jsonl (cmp269.py): 269/269 identical; times not alternating
  (pipeline 0.731 vs the older run, not a speedup figure).
- ab_fm3_24.jsonl, fm.jsonl: face-pruning variants (instrumented build, FACEMODE 0 none, 2 leaves only, 3 hint + leaves).
