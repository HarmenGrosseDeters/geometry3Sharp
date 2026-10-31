# 2D meshing: the golden format

This document is enough on its own to produce rows that compare with the golden rows of geometry3Sharp's 2D mesher: no code of
this repository is needed, in any language. It defines the inputs, the fingerprint, the file formats and what may be compared. Its
worked examples are test vectors, and a test of the repository (`Meshing2FormatDocumentTests`) keeps them true.

Why it exists: results of other triangulators are compared with ours ([acceptance strategy](ACCEPTANCE_STRATEGY.md), section 3). **A
reference is not truth.** A difference needs a reason, and either side may be at fault.

Conventions: text files are UTF-8 with `\n` line ends. All hashes are SHA-256 in lowercase hex. "Sorted" always means by bytes
(ordinal), never by locale.

## 1. The steps of a producer

1. Build the input of a case (section 2), and check its `input_sha256` against section 9.
2. Run your triangulator with the options of the case, as the input format states them.
3. Write the result as one row (section 5), with the fingerprints of section 3. Optionally write the plain text output (section 4)
   and run the oracle over it (section 7).

Only rows and input descriptions leave a reference session. No code, no mesh files, no notes on how the other triangulator works.

## 2. Inputs

No mesh files. An input is a generator with parameters, or pinned public data. Coordinates are integer or dyadic, and generators use
no trigonometry, because `sin` and `cos` differ between runtimes.

### 2.1 Coordinates

A coordinate is an IEEE 754 binary64 value. In text it is written as `0x` plus 16 lowercase hex digits of its bits, most significant
first: `1.0` is `0x3ff0000000000000`, `-0.5` is `0xbfe0000000000000`. A reader also accepts a decimal number (`0.5`, `1e-3`), which
foreign files may use if they round-trip; writers of canonical text use bits only. In fingerprints -0 is written as +0 (section 3);
in the input text it keeps its sign.

### 2.2 The random source

All randomness is SplitMix64, 64-bit unsigned with wrap-around:

```
state = state + 0x9E3779B97F4A7C15
z = state
z = (z xor (z >> 30)) * 0xBF58476D1CE4E5B9
z = (z xor (z >> 27)) * 0x94D049BB133111EB
return z xor (z >> 31)
```

Test vector: starting from `state = 1`, the first two values are 910a2dec89025cc1 beeb8da1658eec67. `Bits(k)` is `next() >> (64 - k)`.

### 2.3 Point-set generators (milestone M1)

Points keep the order in which the generator makes them. An input index is that position, and duplicates stay in the list.

| Generator | Points |
|---|---|
| `literal(x y, x y, ...)` | as written; `-0` is the negative zero; `x5` repeats the point five times; `i i, i=0..7` is the list for each i |
| `uniform-int(n bits seed)` | for each of n: `x = Bits(bits)`, then `y = Bits(bits)`, from one SplitMix64 started at `seed` |
| `transform(g, exp dx dy)` | each point of g: `x * 2^exp + dx`, `y * 2^exp + dy` in double arithmetic; dx and dy default to 0 |
| `grid(nx ny)` | `for x in 0..nx-1: for y in 0..ny-1: (x, y)` |
| `lattice-circle(r centre)` | `for x in -r..r` ascending, for the integers y with `x*x + y*y == r*r`, y >= 0: first `(x, -y)` if y != 0, then `(x, y)`. Then `(0, 0)` if `centre=true` |
| `nudge-odd(g)` | each point at an odd index: the bits of x, read as an integer, plus 1; if x is 0, the bits of y instead |
| `near-collinear(n)` | for i in 0..n-1: `(i, i * 2^-20 + 2^-40)` for even i, `(i, i * 2^-20 - 2^-40)` for odd i |
| `parabola(n)` | for i in 0..n-1: `(i, i*i)` |
| `square-border(k)` | `(i, 0)`, then `(k, i)`, then `(k-i, k)`, then `(0, k-i)`, each for i in 0..k-1; last `(k/2, k/2)` |
| `mixed(n bits tiny large seed)` | for i in 0..n-1: `s = 2^large` for even i, `2^tiny` for odd i; `x = Bits(bits) * s`, then `y = Bits(bits) * s` |

### 2.4 Constraint notation (milestones M2 and M3)

A description is a list of steps, read from the left. A point is numbered at its first appearance, and an exact repeat of a point
(after -0 is made +0) gets its earlier number, so these inputs have no duplicate points unless a step says so.

| Step | Meaning |
|---|---|
| `point(x y)` | a point without a constraint |
| `outer(x y x y ...)` | an outer ring through these points; the closing edge is implied. Marker 0 |
| `hole(x y x y ...)` | a hole ring; it removes everything inside it, also segments. Marker 0 |
| `hole_seed(x y)` | a hole seed point |
| `seg(x0 y0 x1 y1 [marker] [locked])` | a segment; default marker 0; `locked`: it may be split only at input points and crossings |
| `random_segments(range count seed [locked])` | count times, from one SplitMix64 started at `seed`: `a b c d`, each `next() % (range + 1)`; then `seg(a b c d)` |
| `random_points(range count seed)` | count times: `a b`, each `next() % (range + 1)`; then `point(a b)` |
| `region_seed(x y label [max_area])` | a region by seed. If two regions cover a place, the last one wins |
| `region_ring(label max_area x y ...)` | a region by ring; `-` for no area limit |
| `min_angle(d)`, `max_area(a)`, `steiner_limit(n)`, `snap(t)` | options: minimum angle in degrees, largest triangle area, cap on inserted points, snap tolerance in coordinate units (default 0, automatic) |
| `no_noding`, `conforming` | options: crossing segments must fail with a status; empty circumcircles also across segments |

Without an outer ring the domain is the convex hull of the points. A hole does not fill back what another hole removed, and overlapping
outer rings unite. Decisions on these semantics are open ([MESHING2D_DECISIONS.md](MESHING2D_DECISIONS.md)); the
area in a row shows which one a producer followed.

### 2.5 External public data

For data that cannot be generated, a case is described as
`external(url=<URL> sha256=<hash of the file> parts=<which part of the file>)`, with a converter stated in plain words next to the row,
and the licence of the file checked first. No such case exists yet: the polygon fixtures of F-66 (mapbox/earcut, mapbox/delaunator,
jhasse/poly2tri) and the input `issue-211` of artem-ogre/CDT are the candidates. The files themselves stay out of the repository.

### 2.6 The plain text input format

Blank lines and text from `#` to the end of a line are ignored by readers. The canonical text has neither, uses bit tokens for all
coordinates, and its SHA-256 is the case's `input_sha256`. Fields are separated by one space, sections come in this order, and all counts
must match:

```
mesh2-input 1
points <n>
<x> <y>                                           # n lines; input index i is line i
segments <m>
<a> <b> <marker> <locked 0|1>                     # m lines; a and b are input indices
rings <k>
<outer|hole> <marker> <count> <i0> <i1> ...       # k lines; the closing edge is implied
holeseeds <h>
<x> <y>                                           # h lines
regions <q>
ring <label> <max_area|-> <count> <i0> <i1> ...   # or
seed <label> <max_area|-> <x> <y>                 # q lines in input order
min_angle_deg <value|->
max_area <value|->
steiner_limit <integer|->
snap_tolerance <value>
size_constant <value|->                           # the largest edge; a size that varies has no text form yet
resolve_crossings <0|1>
conforming <0|1>
end
```

Worked example: the input `literal(0 0, 1 0, 0 1)` has this canonical text, with a final line end,

```
mesh2-input 1
points 3
0x0000000000000000 0x0000000000000000
0x3ff0000000000000 0x0000000000000000
0x0000000000000000 0x3ff0000000000000
segments 0
rings 0
holeseeds 0
regions 0
min_angle_deg -
max_area -
steiner_limit -
snap_tolerance 0x0000000000000000
size_constant -
resolve_crossings 1
conforming 0
end
```

and `input_sha256` `404ba5d0db189586624422cb3610e0f32df60c39288536b41235e964b8d7e3db`.

## 3. The fingerprint

A fingerprint describes a result by its coordinates, never by vertex numbers, so it does not depend on the numbering or the order of
output. Before hashing, every triangle must be counter-clockwise, decided exactly.

1. **Coordinate token**: 16 lowercase hex digits of the IEEE 754 bits, most significant first, without `0x`. A zero of either sign is
   `0000000000000000`.
2. **Vertex token**: `<x token>,<y token>`.
3. **Triangle line**: `T <v0> <v1> <v2>`. Keep the counter-clockwise order, and rotate so that the smallest vertex token, compared by
   bytes, comes first.
4. **Subsegment line**: `S <v0> <v1>`, with the smaller vertex token first. A repeated subsegment counts once. Markers are not part of it.
5. **Vertex line**: `V <v>` for each vertex used by a triangle, once.
6. **Fingerprint**: sort the lines of one kind by bytes, join them each followed by `\n`, and take the SHA-256 of the bytes.

The three fingerprints are `triangles_fp`, `subsegments_fp` and `vertices_fp`. No lines: `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`,
the hash of nothing.

**Worked examples.** The unit square with vertices (0,0), (1,0), (1,1), (0,1).

Split along (0,0)-(1,1), triangle lines:

```
T 0000000000000000,0000000000000000 3ff0000000000000,0000000000000000 3ff0000000000000,3ff0000000000000
T 0000000000000000,0000000000000000 3ff0000000000000,3ff0000000000000 0000000000000000,3ff0000000000000
```

`triangles_fp` = `317e467c370a6c83e88c6f76f1b66dea72631b430b808f846777d84fcfa4c522`

Split along (1,0)-(0,1), triangle lines:

```
T 0000000000000000,0000000000000000 3ff0000000000000,0000000000000000 0000000000000000,3ff0000000000000
T 0000000000000000,3ff0000000000000 3ff0000000000000,0000000000000000 3ff0000000000000,3ff0000000000000
```

`triangles_fp` = `6e47897f690b6ccabd68c1d3cd96ee17fb761b5e0a7dacf7df3ef782bad5fddc`

With subsegments (0,0)-(1,0) and (1,0)-(1,1):

```
S 0000000000000000,0000000000000000 3ff0000000000000,0000000000000000
S 3ff0000000000000,0000000000000000 3ff0000000000000,3ff0000000000000
```

`subsegments_fp` = `aedb9edb7c99b9480df16cc0b8311bdf0b86614e12f2b747d578da118d06b3d6`

Vertex lines:

```
V 0000000000000000,0000000000000000
V 0000000000000000,3ff0000000000000
V 3ff0000000000000,0000000000000000
V 3ff0000000000000,3ff0000000000000
```

`vertices_fp` = `3076ab557b104d6572bf220d2b90ab771b02b0ccb2c78df83e713740d4332b16`

## 4. The plain text output format

The same reader rules as in 2.6. Coordinates may be bit tokens or decimals; if you want to be compared bit for bit, write bits.

```
mesh2-output 1
producer <name without spaces>
status <Success|Empty|Failed>
vertices <n>
<x> <y>                                  # n lines; the output's own numbering, from 0
triangles <t>
<a> <b> <c>                              # t lines, counter-clockwise
subsegments <s>                          # or: subsegments -   if the producer does not report them
<a> <b> <marker>                         # s lines; the marker may be left out
map <n> <v0> <v1> ...                    # or: map -           input index i -> output vertex
labels <t> <l0> <l1> ...                 # or: labels -        region label per triangle
bad <k> <t0> <t1> ...                    # or: bad -           triangles below the minimum angle
steiner_limit_reached <0|1>
end
```

`map -` marks a foreign producer. The oracle then does not judge our numbering rule (input point i is vertex i, new vertices at or
above the input count), and it finds each input point by its exact coordinates.

## 5. The row

A row is one JSON object. A file is `{"format": 1, "rows": [ ... ], "removed": [ {"case": ..., "reason": ...} ]}`. No times, as they vary
between machines; no mesh data.

| Field | Type | Meaning |
|---|---|---|
| `case` | string | the case id of section 9, such as `m1/grid-8x8` |
| `milestone` | string | `M1`, `M2` or `M3` |
| `producer` | string | **who made the row**: name and version of the triangulator |
| `generator` | string | the input description of section 2 |
| `input_sha256` | string | the hash of the canonical input text |
| `status` | string | `Success`, `Empty` or `Failed` |
| `vertices` | integer | vertices used by triangles |
| `triangles` | integer | number of triangles |
| `subsegments` | integer | number of subsegments reported, 0 if none |
| `constructed_vertices` | integer | vertices that are not exactly an input point |
| `unique` | boolean | no free interior edge has its four corners exactly on one circle (exact test): the (constrained) Delaunay triangulation is unique |
| `triangles_fp`, `subsegments_fp`, `vertices_fp` | string | section 3 |
| `identity_fp` | string, optional | our producer only: numbering, mapping, markers and labels. Foreign rows leave it out |
| `area2` | string | twice the total area of the triangles, exact: `n` or `n/d`, reduced, d positive |
| `min_angle_sin2` | string or null | sin² of the smallest angle of all triangles, exact, then cut to 12 digits after the point without rounding. Null without triangles. Compute it as `cross² / (|u|² |v|²)` at each corner of each triangle, take the minimum, and use no floating point |
| `bad_triangles` | integer | triangles below the requested minimum angle |
| `steiner_limit_reached` | boolean | the Steiner limit stopped the refinement |
| `reason` | string | why this row was recorded or changed; for a foreign row, who ran it and how, in a few words |

## 6. What may be compared

The rows of two producers for one case, with the same `input_sha256`.

| Milestone and situation | Comparable | Not comparable |
|---|---|---|
| any | `status` | |
| M1, or M2 without constructed vertices, both rows `unique` | **`triangles_fp` the same**, `subsegments_fp` the same if both report subsegments, `area2` | |
| M1 or M2, cocircular ties | `triangles`, `area2`; each row checked by the oracle on its own | `triangles_fp`: the choice among ties is free |
| M2 with constructed vertices (crossings) | `triangles`, if both rows are unique | fingerprints and `area2`: a crossing is rational, and how it rounds is the producer's choice |
| M3, refinement | `triangles` within a factor of 2; minimum angle, area limits and protected segments, each row checked by the oracle on its own | fingerprints: the result is not unique |

The factor is a stated expectation from a table, not a measurement, and it may be revised with a reason once rows exist.

## 7. The oracle as a standalone command

The oracle judges any triangulation by definition, with exact integers. It shares no code with our mesher, and reference outputs go through it
as well. It needs the .NET SDK and this public repository:

```
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- oracle --milestone M1 --input <input.txt> --output <output.txt> [--tube 0|auto|<value>] [--case <id>] [--row <row.json>]
```

- `--input` and `--output` are the plain text formats above.
- `--milestone`: M1 forbids constructed vertices and checks the plain Delaunay property, M2 the constrained one, M3 also the bounds of the options.
- `--tube`: how far a chain of edges may run from its input segment. `0` means exactly on it; the default is 0 for M1 and otherwise
  automatic (2^-40 of the largest coordinate, or the snap tolerance if larger).
- It prints one `VIOLATION <kind>: <detail>` per finding and then `VALID` or `INVALID`. Exit code 0 means valid, 1 invalid, 2 unreadable input, 3 the
  oracle's own self-check failed. `--row` writes the row of section 5, which is the easiest way to get a correct one.

`dotnet run ... -- case --case m1/grid-8x8 --output grid.txt` writes one input in the plain text format; `cases` lists all of them with their hashes.

## 8. Rows of another producer

They stay outside this repository until the owner decides on publishing them ([strategy](ACCEPTANCE_STRATEGY.md), section 3).

```
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- compare --rows <file> [--reasons <file>]
```

The path can also come from the environment variable `G3_MESH2_REFERENCE_ROWS`. Without one the command prints `SKIPPED: ...` and exits 0.
It is not part of required CI. It compares by section 6. Each difference is printed; `--reasons` takes lines `<case><TAB><reason>` for those
already explained, and a difference without a reason exits 1. To explain a difference, run the oracle on both outputs: the side that fails is at fault,
and if both pass where the result must be unique, the oracle has a gap that needs closing.

## 9. The cases

The M1 rows are the CI subset of the golden master. The M2 and M3 rows are the small reference set of the failure catalogue: enough
to compare, cheap to run. For each: milestone, generator, and the hash your input must reproduce.

| Case | Milestone | Generator | `input_sha256` |
|---|---|---|---|
| `m1/empty` | M1 | `literal()` | `98b3da0bca96aedab99e4b5d33daa70534f98b270fd18b19abbcdf6203fd6ef8` |
| `m1/single` | M1 | `literal(1 1)` | `e96bb6fab98f33697bb033e116aeff2f24c2f0bb0becc88f473239e95c6a4ee2` |
| `m1/two` | M1 | `literal(1 1, 2 2)` | `b8cd19868691fc497ed8dc39284ae3e2b0a030d675b8b3d5683bd65722386ab4` |
| `m1/identical-5` | M1 | `literal(1 1 x5)` | `e17758de96561abaf6d7c8bebf216dcf3a072d9415e8a7876186eca523891a5e` |
| `m1/collinear-8` | M1 | `literal(i i, i=0..7)` | `2da24e7c363d315b0af8048faa3aa5d04ffb6514bb8b42679b01eee37f3d3bce` |
| `m1/collinear-duplicates` | M1 | `literal(-2 -4, 0 0, 2 4, 1 2, -2 -4)` | `f5f80a72c0b44db9be1e37474c25913701e376a99df0be5556928b6e75cc64e8` |
| `m1/triangle` | M1 | `literal(0 0, 8 0, 0 7)` | `08065f60c432de8e42b655c371d1b384eed00983efe8cc43af4b068bcf01153e` |
| `m1/square` | M1 | `literal(0 0, 8 0, 8 8, 0 8)` | `2b2e37b94261e558a5d8c0cae1e1131d1ac1235d08cbb182d2e634233b5ef201` |
| `m1/square-centre` | M1 | `literal(0 0, 8 0, 8 8, 0 8, 4 4)` | `a0ebe9f7f75f8fdc03b5d1f2fe990fef86e9d7045f145d01b09c4ec5a1e84f26` |
| `m1/hull-edge-point` | M1 | `literal(0 0, 8 0, 0 7, 4 0)` | `b94b24810db3c02dc47069457955c73f41921d50618467082c700a802729bb7f` |
| `m1/duplicates-signed-zero` | M1 | `literal(-0 0, 4 0, 0 4, 0 -0, 4 0, 1 1)` | `1633502164a6063581570ece86e01372907215ac45b915750b2d8067006429a2` |
| `m1/collinear-extension` | M1 | `literal(0 0, 2 0, 1 0, 4 0, 1 1, 3 0, 2 5)` | `106794067ca0888063438a0614d55b0224923d6c0a3ac79263569f82c1820e80` |
| `m1/uniform-int-32` | M1 | `uniform-int(n=32 bits=20 seed=1)` | `2207ef5193974901f993bafa8423c0fc8ffdcc0ef330b17d9aa4c2b380f7277a` |
| `m1/uniform-int-128` | M1 | `uniform-int(n=128 bits=20 seed=2)` | `88493e27ee8f10e13a21d513e5b898cbe13a10d6793f423c37ff01dc78696c16` |
| `m1/uniform-small-range-64` | M1 | `uniform-int(n=64 bits=4 seed=3)` | `032691d8577b01988fa743fd679ee2ad1ebbfa3ea7ea37cbfdfb1c36ef52c3b0` |
| `m1/dyadic-64` | M1 | `transform(uniform-int(n=64 bits=30 seed=4), exp=-20)` | `ae0680dedb218a0e528d44904635be9168c23d618c324826e27ed928264118d5` |
| `m1/utm-64` | M1 | `transform(uniform-int(n=64 bits=27 seed=5), exp=-10, dx=632117.25, dy=5712043.5)` | `c9c196086b9b17de520959550742767a43a5dcca701e4970e6e3be23b771ccef` |
| `m1/tiny-64` | M1 | `transform(uniform-int(n=64 bits=31 seed=6), exp=-560)` | `23603e07268c07f467fc35740ba408de1b46a81029281f0b754bc25a7e8e5dbc` |
| `m1/huge-64` | M1 | `transform(uniform-int(n=64 bits=31 seed=7), exp=200)` | `c25dce507128ca853a32e742c5cc8a99dfd92f90a5ee6a6605d10d3859f7de19` |
| `m1/subnormal-32` | M1 | `transform(uniform-int(n=32 bits=20 seed=8), exp=-1074)` | `1bff122e4bb146ee3493626921fefaed572656a21a518310da82b745ec35e46e` |
| `m1/mixed-48` | M1 | `mixed(n=48 bits=20 tiny=-530 large=200 seed=9)` | `845d7c3748470269eef4cd88f9129b6e32b694e78f3ce957d7333833be72b1d5` |
| `m1/grid-8x8` | M1 | `grid(nx=8 ny=8)` | `d0384b478bcd20df916e566c7c4b48787c5c2fb88261fe1c4a2318ab31e5fa7c` |
| `m1/lattice-circle-1105` | M1 | `lattice-circle(r=1105 centre=false)` | `bf7df3308342d8843728e98a366f08948c8e91868f72a7dcec2efa68456270d1` |
| `m1/lattice-circle-centre` | M1 | `lattice-circle(r=1105 centre=true)` | `5857104836d563916d514365fe4933a4be6c7cb99680a0ac9ce7f24b0d772a1c` |
| `m1/near-cocircular` | M1 | `nudge-odd(lattice-circle(r=1105 centre=false))` | `50129ce429eb8242b0c98b64058d1e811a750398cff81205c405811614b11cef` |
| `m1/near-collinear-64` | M1 | `near-collinear(n=64)` | `de08a4de74c9dbf83b805ecabf209f1900e9f10970307b31d5e84b5b06389c7e` |
| `m1/parabola-48` | M1 | `parabola(n=48)` | `c0c7fd5c72e801288aca8e879e86699dfea48cdc3100120d2d22988b0be2e52b` |
| `m1/square-border-16` | M1 | `square-border(k=16)` | `caa163a89ff251b79a8130ec88df39b7da1dc052fb3c839ab208065fbaace580` |
| `f-1/a` | M2 | `outer(0 0 10 0 10 10 0 10) seg(2 4 4 8) seg(1 5 6 6) seg(1 7 5 3)` | `60d68574863c9d993c08347d247e25e55b7e3e2c655f50c658f04d672904420b` |
| `f-1/b` | M2 | `outer(0 0 10 0 10 10 0 10) seg(2 5 9 5) seg(6 3 10 10) seg(8 7 5 0)` | `86a855b27899556fcd9f1806d77a00d547c3975f0da9fbdbbd43210f51cde5b5` |
| `f-1/c` | M2 | `outer(0 0 10 0 10 10 0 10) seg(5 0 1 10) seg(7 7 0 7) seg(3 3 8 8)` | `1024bf3787077972df66dbcc8d0de54c337ad245825bbb1cd1a86976f89f400c` |
| `f-1/random` | M2 | `outer(0 0 10 0 10 10 0 10) random_segments(range=10 count=15 seed=1)` | `4898e91c58fab8eb8f6e05056c5853bb197eaf82fd619b90afeb77f58a4b89d9` |
| `f-2` | M2 | `outer(0 0 10 0 10 10 0 10) seg(9 3 2 0) seg(7 2 1 8) seg(7 3 3 6) seg(10 6 9 1) seg(10 10 8 2)` | `e1269abb52e4da2b037349df3890fb0ef90c938bb297c0cab508a7e6c9d2b7ba` |
| `f-3/a` | M2 | `outer(0 0 10 0 10 10 0 10) seg(9 5 5 7) seg(7 7 4 1) seg(6 5 3 10)` | `0499a2c646729f0943037e2d157cf45341ffc73360d188e9f9398ba046d84247` |
| `f-6/a` | M2 | `outer(0 0 4 0 4 4 0 4) point(1 1) point(2 2) seg(0.5 0.5 3.5 3.5)` | `3b6ddf9288e789ec6700c83f7bf80293b34d42090cdad05e88ca5efa890d02ce` |
| `f-7/a` | M2 | `outer(0 0 10 0 10 10 0 10) seg(1 5 6 5) seg(4 5 9 5)` | `b792ee0423034e567ba08eddea07ca6c6824e8fcbd0361243e04f9b49a82f5dd` |
| `f-7/d` | M2 | `outer(0 0 10 0 10 10 0 10) seg(2 2 8 8) seg(2 2 8 8) seg(8 8 2 2)` | `e38601256a8bf18fb7de2a2f82c438f663c03f83f3d32fa9be9b766ffbc4b6f0` |
| `f-7/e` | M2 | `outer(0 0 1 1 1 0 0 1)` | `c4970615e5a45096cd5ee64b214e1fe46a6d62f51aa8d6d1e26715258901ae7b` |
| `f-43/b` | M2 | `outer(0 0 10 0 10 10 0 10) hole(0 4 3 4 3 6 0 6) hole_seed(1 5)` | `fdf6afd2a2d0109d3ae20c86a0292104d783355fc3b4cae5f9473b7bf04a9c67` |
| `f-43/c` | M2 | `outer(0 0 10 0 10 10 0 10) hole(2 2 4 2 4 4 2 4) hole_seed(2 3)` | `f544dd714933070a41fef9519f70ed03ccf7c5baf6fb4ac1e561332e7f5b353f` |
| `f-44` | M3 | `outer(0 0 10 0 10 10 0 10) hole(2 2 8 2 8 8 2 8) hole_seed(3 3) hole(4 4 6 4 6 6 4 6) hole_seed(5 5) hole_seed(20 20) region_seed(5 5 9) region_seed(1 1 5) min_angle(20)` | `1b2cb078b7e0aea0202594cf52ba2ac9af6467944fbc2781a37201ab60be1938` |
| `f-46/a` | M2 | `outer(0 0 10 0 10 10 0 10) hole(2 2 8 2 8 8 2 8) seg(2 5 8 5) seg(5 3 5 7)` | `030f900ad3ee4fdea069c872dc5898210b8d9df85848bdfd4a6d2c8eea25d35f` |
| `f-46/b` | M2 | `outer(0 0 10 0 10 10 0 10) hole(2 2 8 2 8 8 2 8) seg(0 5 10 5)` | `3a5cb33d96391363dea1e9c11dc7f5dd915bf577b6f792c0e87b68a0d87846c9` |
| `f-48/a` | M3 | `outer(0 0 10 0 10 10 0 10) hole(2 2 5 5 8 8) min_angle(25)` | `8e36334936edb8a92cda98bfc2c0a04a50d6215c3399efc8502c513ddb4313ec` |
| `f-48/b` | M3 | `outer(0 0 10 0 10 10 0 10) hole(2 2 8 8 2 2) min_angle(25)` | `22da602522cdc39ca0c643000bc39176fc54866fd7ccee5b8598ece052918ada` |
| `f-48/c` | M3 | `outer(0 0 10 0 10 10 0 10) hole(2 2 8 8 5 5 2 2) min_angle(25)` | `092e66f40335fe54eba4d1e9d6532fe32e64496c9e7fc72d2f0f4cea6bf7d431` |
| `f-23/angle-0` | M3 | `outer(0 0 10 0 10 10 0 10) max_area(0.5) min_angle(0)` | `e7a5f1962877afbd0e77e5f0c6c749323c87f1b8216f66201c03b7ad4117d2bd` |
| `f-23/angle-20` | M3 | `outer(0 0 10 0 10 10 0 10) max_area(0.5) min_angle(20)` | `0c4905433a3986fb8db422703a2b13a74ff8034a08da16a0695515e23e19d365` |
| `f-23/angle-30` | M3 | `outer(0 0 10 0 10 10 0 10) max_area(0.5) min_angle(30)` | `325eef26bbacad5bdf264673dd1f01a2909df345a3e7951fd8e472ce17249ffd` |
| `f-34` | M3 | `outer(0 0 1 0 1 1 0 1) max_area(0.0009765625)` | `124b5b1d1366685844de299c355e91c95775d91b859ae183e46fd26ecb8336a4` |
| `f-38/a` | M3 | `outer(0 0 1 0 1 0.015625) min_angle(30)` | `a22750bef6cf9a7f6c95ffca00b11e50c890365b2db6cab99f6dad6ce0f4069c` |
| `f-38/b` | M3 | `outer(0 0 1 0 1 0.001953125) min_angle(30)` | `dc38ef835d91f1654614bb11d659d3620065c5df5d459c59782d4a5aa05ebffc` |
| `f-40/angle-33.75` | M3 | `outer(0 0 16 0 16 16 0 16) random_points(range=16 count=30 seed=40) min_angle(33.75) steiner_limit(200000)` | `53b51fc5002aa14bf70df8a118f2f5d0b8f4805e767e69a1396bfd4dc7e4559d` |
| `f-41/a` | M3 | `outer(0 0 128 0 128 0.0078125 0 0.0078125) min_angle(20)` | `d7bcaabf0bc482a1ffedf023d66e6912f7ff814b3cbdf66a8bfa3b9eb92a7a3b` |
