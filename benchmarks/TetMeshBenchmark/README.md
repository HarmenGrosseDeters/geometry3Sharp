# TetMeshBenchmark

Stage 0 of [docs/design/TET_MESHING.md](../../docs/design/TET_MESHING.md): the insertion rate of `Delaunay3` and the tetrahedral
mesh that the inside cells of `MeshAlphaWrap` give (`MeshAlphaWrap.ExtractTetMesh`), with validity and quality.
Measured 2026-11-09 in the cloud container (single thread, Release, net10.0; the machine is shared, so read timings as
orders of magnitude).

```
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- insert [n ...]
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- wrap [relativeAlpha] [relativeOffset]
dotnet run -c Release --project benchmarks/TetMeshBenchmark -- file <mesh.obj|stl|off> [relativeAlpha] [relativeOffset]
```

Thingi10K meshes were not measured: the hosts that serve them are blocked in the cloud environment (`file` mode is
ready for a runner session with network access; meshes must not be committed).

## Delaunay3 insertion rate (`insert 20000 100000 400000`, uniform random points in the unit box, `minSeparation` 0)

```
insert,random order,20000,20000,133874,517.1 ms,25.86 us/point,3.863 us/cell
insert,grid-sorted,20000,20000,133874,251.3 ms,12.56 us/point,1.877 us/cell
insert,random order,100000,100000,672106,3513.8 ms,35.14 us/point,5.228 us/cell
insert,grid-sorted,100000,100000,672106,1303.0 ms,13.03 us/point,1.939 us/cell
insert,random order,400000,400000,2696480,27083.6 ms,67.71 us/point,10.044 us/cell
insert,grid-sorted,400000,400000,2696480,5243.9 ms,13.11 us/point,1.945 us/cell
```

Grid-sorted (coarse 32^3 cell order) is the realistic case: about **13 us per point, 1.9 us per final cell**, constant in n.
Random order degrades with n (the point location walk gets long). The design note had assumed 1-2 us per point.

## Inside cells of the wrap, relative alpha 20, relative offset 600 (`wrap 20 600`)

All eight outputs are valid (`TetMeshQuality.NotValidReason` null; the test oracle agrees on the test-sized inputs).

```
input,status,input_tris,steiner_points,wrap_ms,extract_ms,validity_ms,measure_ms,tets,boundary_tris,wrap_us_per_tet,extract_us_per_tet,re_worst,re_p1,re_p5,re_median,re_gt2,mindih_worst,mindih_p1,mindih_p5,mindih_median,maxdih_worst,dih_lt10,dih_lt5,dih_lt1,maxdih_gt170,valid
sphere n=6,Success,300,477,141.349,1.927,14.3526,6.6732,1358,950,104.086,1.419,18.2482,12.1469,8.2474,3.64743,1190,0.0230192,0.0241812,1.96638,9.23615,179.967,743,277,30,215,yes
sphere n=16,Success,2700,794,180.713,18.4928,21.8974,5.9161,2271,1584,79.574,8.14302,11.9152,11.1009,10.5501,7.34638,2241,0.460172,0.78276,1.62062,5.14077,179.185,2081,1075,49,1120,yes
sphere n=40,Success,18252,886,239.256,1.2829,4.3724,5.7738,2465,1768,97.0615,0.520446,10.066,9.81011,9.46833,8.7624,2420,0.103156,0.418218,1.18733,4.23716,179.835,2177,1431,90,1432,yes
box 1x0.5x0.25,Success,12,602,63.643,0.6343,11.5943,3.1641,1606,1200,39.6283,0.394956,12.1362,4.66126,2.40487,1.63444,447,4.33431E-11,3.85119,9.63317,31.1349,180,85,30,9,22,yes
cylinder,Success,128,745,90.823,1.0184,2.7476,5.319,2510,1486,36.1845,0.405737,10.0703,8.73772,8.3691,3.3343,1720,2.84217E-14,0.0156986,1.05713,15.8454,180,876,342,122,289,yes
torus,Success,2304,1011,172.128,1.9721,8.1841,13.0497,4544,2022,37.8803,0.434001,8.43397,6.62385,5.57492,3.15173,3574,0.0698338,0.789263,2.28672,9.91474,179.807,2292,729,67,457,yes
torus thin,Success,2048,1082,140.846,1.5303,4.4269,4.7067,4935,2164,28.5402,0.310091,4.8001,3.77016,2.90644,2.04881,2562,0.00620433,0.565809,2.31314,12.4423,179.989,1721,641,88,455,yes
two spheres (soup),Success,2904,1033,41.6189,1.1888,3.8056,3.0426,3083,2060,13.4995,0.385598,11.1735,9.59476,8.64383,5.94252,2926,0.00720726,0.557309,1.64358,5.93188,179.99,2450,1143,66,1264,yes
```

Surface (the wrap's own boundary, same run): interior dihedral angle at the surface edges and triangle min angle.

```
input,surf_edges,surf_dih_min,surf_edges_lt90,surf_edges_lt45,surf_edges_lt20,surf_edges_gt270,surf_tri_minangle_worst,surf_tri_minangle_p1
sphere n=6,1425,157.727,0,0,0,0,16.0071,17.0886
sphere n=16,2376,168.858,0,0,0,0,26.7716,26.7716
sphere n=40,2652,170.719,0,0,0,0,29.9893,32.2233
box 1x0.5x0.25,1800,91.3465,0,0,0,0,9.28384,9.70033
cylinder,2229,88.013,2,0,0,0,8.57185,15.561
torus,3033,149.957,0,0,0,0,15.0519,15.4645
torus thin,3246,133.436,0,0,0,0,15.0748,16.5908
two spheres (soup),3090,86.4282,1,0,0,2,16.6673,21.8411
```

## Relative alpha 50, relative offset 1500 (`wrap 50 1500`, selected columns)

```
input,status,wrap_ms,tets,boundary_tris,re_worst,re_p5,re_median,re_gt2,mindih_worst,mindih_p5,maxdih_worst,dih_lt10,dih_lt5,dih_lt1,maxdih_gt170,valid
sphere n=6,Success,416.553,7328,5164,17.1102,10.9076,5.77486,7181,2.55795E-13,2.06276,180,5498,2002,106,1696,yes
sphere n=16,Success,282.181,6605,4394,19.4558,14.4552,9.11289,6404,0.00100327,1.63241,179.999,6178,4892,162,4690,yes
sphere n=40,Success,154.764,5323,3608,16.0579,14.573,11.092,5306,0.148011,0.69112,179.747,5036,4186,374,4186,yes
box 1x0.5x0.25,Success,126.359,9051,6354,19.0464,5.25493,2.51753,5680,0.209826,4.91415,179.291,1664,487,24,84,yes
cylinder,Success,77.7188,10448,6506,19.952,10.2005,4.91831,8399,0.000340196,1.34439,180,4681,2387,453,1612,yes
torus,Success,125.203,10321,5458,18.5589,9.1361,4.5373,9357,0.0245915,2.06019,179.957,5585,2066,158,1219,yes
torus thin,Success,81.1958,7779,3714,12.41,4.71516,2.36347,5197,0.0312119,2.76652,179.901,2530,856,100,511,yes
two spheres (soup),Success,85.3176,7271,4756,21.5346,13.5657,7.47241,6992,0.00601521,1.2399,179.984,6483,4096,239,4025,yes
```
