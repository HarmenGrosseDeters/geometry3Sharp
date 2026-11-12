# L3b and L2 probes (2026-11-12, master cab88798), instrumentation patches

- l3b_probe.patch: timers in certified_within / MeshHausdorffDistance, UBSERIAL toggle, allocation-free patch_bound.
  certify is already parallel; patch_bound ~12 us per call is ~all of it (compute, not GC); allocation-free changes
  nothing measurable. L3b <= ~1.02x.
- l2_probe.patch: timers in Delaunay3.Insert, the edge-link Dictionary replaced by per-vertex chains (same pairing).
  Split: cavity 45 % (filtered InSphere, 0 exact fallbacks), link 16 %, create 13 %, star 11 %, checks 10 %, locate 4 %.
  Link -15 % only. L2 <= ~1.02x.
