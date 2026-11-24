# Thingi10K selection

Local copy of the dataset (read-only, not copied, no mesh in the repository); ids are the file names `<id>.stl` of `raw_meshes`. Models have individual licenses: results name ids and numbers only.

Rule (`WrapAccuracyProbe classify <raw_meshes> --seed 2611 --per-class 8 --max-tris 100000 --max-scan 800`): the 20 meshes of the T1 sweep of the earlier wrap studies (ids from `benchmarks/ContouringCertificate/README.md`, all present locally), plus a seeded random sample of the other files (the files sorted by numeric id, shuffled by a Fisher-Yates pass of `System.Random(2611)`, scanned in that order, files above 100,000 triangles skipped by size, then by triangle count after the STL welding of `StandardMeshReader`), stratified by a class decided by the library, until each class has 8 meshes (scan stopped after 227 files, 36 skipped as too large or unreadable).

Classes: **C** closed and clean (`MeshRobustRepair.NotCleanReason` is empty); **S** closed (`IsClosed` or `ExactMeshBoolean.IsWindingClosed`) but not clean (the reason is listed: self-intersections, degenerate triangles, inside-out components); **H** open with boundary edges at most 2 % of all edges (small holes); **O** open with more (sheets, soups).

| set | ids (triangles; boundary edge share; reason) |
|---|---|
| T1 C (Spike A) | 96773 (2544), 64983 (396), 43551 (956), 1582399 (188), 104640 (1884), 94664 (12940), 73088 (328), 1582404 (232), 57714 (708), 109459 (2502), 472011 (13194), 113418 (7154), 79961 (508) |
| T1 S (Spike A) | 57084 (19202; degenerate triangles), 116871 (4302; degenerate triangles) |
| T1 H (Spike B) | 96675 (11236; 0.0003559), 37111 (88034; 0.006386), 66112 (16860; 0.002764) |
| T1 O (Spike B) | 37731 (11042; 0.0281), 1038438 (3448; 0.2809) |
| sample C, closed and clean (Spike A) | 37384 (17596), 270456 (77242), 124042 (17210), 189404 (11964), 124038 (13792), 40602 (52), 1120775 (220), 159695 (23528) |
| sample S, closed, not clean (Spike A) | 49900 (472; inside-out components), 225936 (482; self-intersections), 48123 (2098; self-intersections), 1722415 (1870; self-intersections), 77476 (444; degenerate triangles), 441722 (50024; degenerate triangles), 135803 (16250; degenerate triangles), 1514495 (2056; self-intersections) |
| sample H, open with small holes (Spike B) | 285604 (12764; 0.0008353), 57070 (5786; 0.001382), 109091 (15197; 0.000307), 86648 (8604; 0.007565), 50108 (2276; 0.001756), 42331 (4712; 0.0008485), 45341 (4348; 0.009461), 1146197 (14580; 0.001005) |
| sample O, open sheets or soups (Spike B) | 47979 (7241; 0.05454), 66640 (9800; 0.04209), 80177 (588; 0.03563), 38631 (16705; 0.07952), 68656 (639; 0.3272), 73429 (438; 0.05045), 100040 (736; 0.02151), 42293 (310; 0.07054) |

Classes of all meshes read for the selection (`thingi_scan.csv`, T1 and sample): C 127, H 18, O 8, S 74, T1:C 13, T1:H 3, T1:O 2, T1:S 2
