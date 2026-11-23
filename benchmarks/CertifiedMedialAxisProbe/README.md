# Certified medial axis probe

Run `dotnet run --project benchmarks/CertifiedMedialAxisProbe -c Release`. On procedural boxes and thin slabs it prints the crease witnesses of the medial axis, the 2V/A thickness proxy and the tree midpoint distances that the study compares with a certified lower bound on local feature size. Its outputs, the derivations and the decision (a component-wide bound is zero even on the unit cube, so it cannot replace SafeRounding's thin-solid criteria) are in [docs/research/CERTIFIED_MEDIAL_AXIS_STUDY.md](../../docs/research/CERTIFIED_MEDIAL_AXIS_STUDY.md).
