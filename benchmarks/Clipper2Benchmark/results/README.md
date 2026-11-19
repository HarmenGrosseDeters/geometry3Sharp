Raw results of the Clipper2 GC / allocation study (spike, runs/clipper-gc; 4-core Xeon 2.1 GHz, 260 MB L3, .NET 10.0.15, workstation non-concurrent GC).
prof_default / prof_10m: time, bytes, GC counts and pause per call (default gen0 budget, and GCgen0MaxBudget=10 MB); ab_*: each lever against base,
interleaved rounds, median of 5, "identical" = result digest equal bit for bit; sites: allocated bytes by type; alloc: cost of allocating on this host.
Timing noise between identical configs is about +-5 % (more below 1 ms).
