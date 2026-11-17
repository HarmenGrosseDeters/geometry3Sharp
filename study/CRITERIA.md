# Go / no-go criteria, fixed before the corpus run (2026-11-17, before run 2 of the study workflow reported)

Written after the smoke run and the local procedural sweep, before any row of the 800-mesh run was seen. "Gain" = a mesh that
misses in R and passes in the configuration, paired; "loss" the reverse. 800 = first + holdout selection.

1. Larger effort on part-wise repair (option B stage 1 / 2 with frames): GO if P or Q gains at least 8 meshes on the strict
   criterion, or at least 8 on the part criterion (no closed component off by > 1 % of its own diagonal), with at most a
   quarter as many losses; otherwise NO-GO for the corpus (B stays a correctness feature for assemblies / scenes, sized as
   such, driven by the procedural cases).
2. Local tolerances in the repair stage (option C): GO only if S or L gains at least 8 net on the strict criterion; otherwise
   NO-GO (the global tolerances are not what fails on real inputs).
3. Translation (T, the scene at (5e5, 5e6, 0)): a follow-up only if the strict outcome differs on at least 4 meshes.
4. G (resolution 256) is informational: if it gains more than all scale oracles together, the main lever on the misses is the
   resampling resolution, not scale-freedom, and the note says so.
5. Independent of 1-4: every case where Apply() returns true and a closed component is lost (procedural or corpus) is a bugfix
   against the owner's rule of 2026-11-10 (no silent loss), not part of the go / no-go.
