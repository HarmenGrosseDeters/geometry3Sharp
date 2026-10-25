# geometry3Sharp – Fehleranalyse (Oktober 2026)

Stand des Codes: Upstream-`master` `ece3364` (Fork per Fast-Forward aktualisiert).
Geprüft wurden alle 183 Upstream-Issues (Bug-verdächtige im Detail) und alle Verzeichnisse Klasse für Klasse.

**Methode:** Code lesen und Verdachtsfälle mit kleinen Repro-Programmen unter .NET 8 ausführen. Dafür wird die
komplette Bibliothek in ein Konsolenprojekt eingebunden.

**Status-Spalte:**
- **B** = per Repro bestätigt (falsche Ausgabe oder Exception beobachtet)
- **S** = sicher per Code-Lesen
- **P** = plausibel, nicht ausgeführt

**Schwere:** K = kritisch, H = hoch, M = mittel, N = niedrig

Die Befunde beziehen sich auf `ece3364`. In `master` sind einige davon inzwischen behoben, etwa `Matrix3d/f`-Konstruktor,
`MeshMeshCut:208`, `IndexPriorityQueue.Contains`, `DistPoint2Circle2` und `Dijkstra.Compute_Dense`.

### Status der Fixes

Erste Runde, nur Einzeiler. Regressionstests stehen in `tests/geometry3Sharp.Tests/ReviewFixesTests.cs`.

Behoben:
- `MathUtil.InvSqrt`
- Kopierkonstruktoren `Index3i`/`Index4i`
- `MatrixUtil.Transpose3x3`
- `DijkstraGraphDistance.update_neighbours_dense`
- `BiArcFit2` (arc2)
- `ContOrientedBox3.ResultValid`
- `BaseCurve2.GetMinTime`
- `DistTriangle3Triangle3` (nächste Punkte)
- `LinearFalloff.Duplicate`
- `ImplicitPoint2d.InBounds`
- `EdgeSpan.CheckValidity`
- `MarchingCubesPro.edge_hash`
- `MeshPlaneCut.FillHoles`
- `DMeshAABBTree3.find_any_intersection`

Zweite Runde, I/O. Regressionstests stehen in `tests/geometry3Sharp.Tests/IOFixesTests.cs`.

Behoben:
- `OBJReader` (inkl. `.mtl`), `STLReader` und `OFFReader` parsen mit `CultureInfo.InvariantCulture`.
- `OBJWriter` (inkl. `.mtl`), `OFFWriter` und `STLWriter` formatieren Zahlen mit `CultureInfo.InvariantCulture`. Das gilt
  auch dann, wenn der übergebene `TextWriter` eine eigene Kultur trägt, etwa ein unter de-DE erzeugter `StringWriter`.
- Die `.mtl`-Datei wird geflusht.
- `OBJReader.ReadMaterials` schließt die Datei wieder.
- `BufferUtil.DecompressZLib` liest, bis alle Daten da sind.
- `ParallelStream`: Exceptions aus Producer- oder Consumer-Thread werden in `Run` weitergeworfen. Vorher beendeten sie den
  Prozess oder ließen den Consumer endlos laufen. Davon profitiert der binäre `STLWriter`.

Bewusst zurückgestellt: `OBJReader.is_complex`. Der Fix leitet Dateien auf `BuildMeshes_ByMaterial` um. Dieser Pfad
verwirft UVs bei Meshes ohne Material, übernimmt keine Gruppen und stürzt bei Faces ohne Normal-Index ab. Er muss deshalb
zusammen mit diesem Pfad repariert werden.

---

## 1. Upstream-Issues

### 1.1 Echte Bugs, im aktuellen Code noch vorhanden

| Issue | Titel | Ursache (Datei:Zeile) | Schwere | Status | Fix |
|---|---|---|---|---|---|
| #215 | DistPoint2Circle2 incorrect distance | `distance/DistPoint2Circle2.cs:63`: `Center + Radius` addiert den Skalar auf x **und** y | M | B | `Center + new Vector2d(Radius,0)` |
| #211 | ContOrientedBox3.ResultValid immer false | `containment/ContBox3.cs:15-35`: `ResultValid` wird nie gesetzt | M | B | nach `Box.Contain` `ResultValid = true` |
| #168 | SVD-Testfall schlägt fehl | `solvers/SingularValueDecomposition.cs:392/431`: Householder mit Länge 0 wird trotzdem in U/V akkumuliert. Zweiter Fehler `:506`: Vorzeichen von `sn` in der Givens-Rotation (Rang-defizite Matrizen) | **K** | B | `twoinvudu = length>0 ? … : 0`; `GivensRotation(i,j,cs,-sn)`. Siehe Abschnitt 2.6 |
| #154 | OBJWriter flusht .mtl nicht | `io/OBJWriter.cs:252`: `StreamWriter` wird nie geflusht, die .mtl-Datei bleibt leer | H | B | `w.Flush()` bzw. `using(... leaveOpen:true)` |
| #138 | MeshMeshCut: falsche Koordinate | `mesh_ops/MeshMeshCut.cs:208`: `find_existing_vertex(isect.point0)` statt `(v)` | **K** | B | Vorschlag aus dem Issue ist korrekt |
| #137 | Segment2d ∩ 0-Längen-Segment | `math/Segment2.cs:213-245`: Nullrichtung landet im Parallel-Zweig, das Ergebnis ist asymmetrisch | M | B | Null-Segmente vorab als Punkt behandeln |
| #133 | OBJReader nicht kulturinvariant | `io/OBJReader.cs:366-395, 642-653, 705-707`: unter de-DE wird `1.5` still zu `15` | H | B | überall `CultureInfo.InvariantCulture` |
| #53 | OBJWriter nicht kulturinvariant | `io/OBJWriter.cs:86-110, 261-277`: bei direktem Aufruf `v 1,5 …` | N–M | B | `WriteLine(CultureInfo.InvariantCulture, …)` |
| #176 | TubeGenerator bei ungleichen Segmentlängen | `curve/CurveUtils.cs:24` (längengewichtete Tangente) und keine Miter-Skalierung | M | B | Tangente aus normierten Kantenrichtungen; Ring um `1/cos(θ/2)` skalieren |
| #110 | Crash LaplacianMeshSmoother | `mesh_ops/LaplacianMeshSmoother.cs:397-405`: `constrained` ist `null`, wenn die Region keinen Rand hat | M | B | `constrained` immer allokieren |
| #72 | FastWindingNumber-Bug (parallel) | `spatial/PointAABBTree3.cs:229-231, 307-313` und `spatial/DMeshAABBTree.cs:1314-1317`: Cache-Aufbau ohne Synchronisierung | H | B | Cache lokal bauen, unter `lock` veröffentlichen |
| #85 | IntrTriangle3Triangle3 bei degenerierten Dreiecken | `intersection/IntrTriangle3Triangle3.cs:162/179`: Normale = 0, der Trennungstest greift nicht | N | B | `N.LengthSquared < eps` prüfen und auf Segment/Punkt zurückfallen |
| (#104) | *Zusatz:* DMesh3Builder.Build mit TriGroups | `mesh/DMesh3Builder.cs:183-191`: Gruppe wird per Index statt per tid gesetzt, Rückgabe von `AppendTriangle` wird ignoriert, Crash | M | B | `AppendTriangle(t[i], groups[i])` und Rückgabe prüfen |

### 1.2 Echte Bugs, inzwischen behoben
#44 (MeshIsoCurves.compute_full), #42 (CurveUtils.ArcLength), #28 (StandardMeshWriter-Kultur), #19 (Vector2.Cross).

### 1.3 Kein Bug (Fehlbedienung, Missverständnis oder gewolltes Verhalten)

| Issue | Begründung |
|---|---|
| #155 | `Segment2d.Direction` wird im Konstruktor in-place normiert, `PointAt` stimmt |
| #136 | `Normalize()` gibt absichtlich die alte Länge zurück |
| #11 | Mit float normierte Werte sind in double nicht exakt 1; `Normalize()` danach ist korrekt |
| #179 | `PositionF` hat nicht verschoben (`v*1f`) |
| #157 | `PreserveBoundaryLoops` fixiert absichtlich keine Ecken (vom Maintainer bestätigt) |
| #131 | Eingabe mit falscher Wicklung, Abhilfe: `ReverseOrientation()` |
| #117 | `GetEdgeT` liefert an Randkanten absichtlich -1 |
| #111, #37, #69, #175 | Unity- bzw. Build-Umgebung (fremde DLL, .NET-Version, Symbol `G3_USING_UNITY`, Unity-Typen nicht unterstützt) |
| #62 | `CompactCopy` kopiert Normalen korrekt. Der Reducer aktualisiert Normalen nicht, danach `QuickCompute` aufrufen |
| #56 | `ContOrientedBox3` ist eine Kovarianz-Box, keine minimale OBB (Feature-Wunsch) |
| #43, #23 | Unity-Würfel ist nicht verschweißt, jede Kante ist eine Randkante |
| #32 | Kappenzentrum außerhalb des Profils, inzwischen gibt es `OverrideCapCenter` |
| #145 | Zusätzliche Vertices sind eine Eigenschaft von `MeshInsertPolygon` |
| #135 | Lokale und Welt-Koordinaten in Unity verwechselt |
| #104 | Nicht-manifolde Kante, DMesh3 lässt max. 2 Dreiecke pro Kante zu (der Crash mit Gruppen ist ein eigener Bug, siehe oben) |
| #7 | Folgeverhalten erklärbar (Rand und UV-Nähte fixiert); ursprünglicher Crash laut Maintainer behoben |

### 1.4 Feature-Wunsch oder Frage
#153 (Tri-Tri ohne Berührung), #134, #70, #60 (`CheckValidity` gibt keine Details), #50, #45, #35, #30 (veralteter Kommentar `DMesh3Builder.cs:86-87`).
Die übrigen rund 130 Issues sind laut Titel und Inhalt Fragen oder Feature-Wünsche (Point-Cloud-Meshing, Booleans, UV-Unwrap …).

### 1.5 Nicht entscheidbar (Eingabedaten fehlen)
#199 (SDF-Hülle, nur Bilder, vom Autor geschlossen), #181 (FindNearestTriangle; Algorithmus gegen Brute-Force geprüft und korrekt, vermutlich Koordinatensystem-Problem), #173 (Reducer, STL fehlt).

---

## 2. Review Klasse für Klasse

Pro Abschnitt erst die Defekte nach Schwere, danach die Mängel kurz.

### 2.1 `math/`

| Datei:Zeile | Klasse.Methode | Schwere | Status | Fehler → Fix |
|---|---|---|---|---|
| `MathUtil.cs:222` | `MathUtil.InvSqrt` | H | B | `f/Math.Sqrt(f)` ergibt √f statt 1/√f. Dadurch liefert `Vector3d.GenerateComplementBasis` nicht-normierte Vektoren (betrifft DistLine3Triangle3, IntrTriangle3Triangle3) → `1.0/Math.Sqrt(f)` |
| `Matrix3d.cs:53`, `Matrix3f.cs:53` | Konstruktor `(Func<int,int,…>)` | H | B | `matF(1,2)` statt `matF(2,1)` in Row2 |
| `IndexTypes.cs:15, 322` | `Index3i(Index3i)`, `Index4i(Index4i)` | H | B | `c = copy.b` → `copy.c` |
| `MatrixUtil.cs:47-57` | `Transpose3x3` | H | B | `MTranspose[8]` wird nie gesetzt |
| `MathUtil.cs:617-636` | `SolveQuadratic` | H | B | b=0 → `Sign(0)=0` → Division durch 0; a=0 → ±Inf |
| `Matrix2d.cs:6,11,99` (2f analog) | `Matrix2d` ist veränderliche `class` | H | B | `Inverse()` gibt die geteilte Instanz `Matrix2d.Zero` zurück, `Ellipse2.FromCoefficients` schreibt hinein → struct oder neue Instanzen |
| `Box3.cs:158, 559` | `Box3d/f.MinExtent` | M | B | `Min(x, Max(y,z))` → `Min(x, Min(y,z))` |
| `VectorTuple.cs:116` | `Vector2dTuple4`-Indexer (Setter) | M | B | Index 0/1 vertauscht, dadurch falsche `Box2d.ComputeVertices` |
| `Ray3.cs:115-120` | `Ray3f.DistanceSquared` | M | B | Behandelt den Strahl als Gerade (`t<0` wird nicht geklemmt) |
| `Segment2.cs:110-121` | `Segment2d.WhichSide` (Instanz) | M | B | P0/P1 vertauscht, Vorzeichen umgekehrt gegenüber der statischen Variante. Genutzt in `MeshInsertUVPolyCurve.cs:374` |
| `PrimalQuery2d.cs:100-109` | `ToLine(…, out order)` | M | B | `sqrLength` aus falschem Vektor, „innen“ und „jenseits“ vertauscht |
| `TransformSequence2.cs:59, 215` + `Segment2.cs:156` | `ScaleIsUniform`, `TransformN` | M | B | Vergleicht Skalierung mit Pivot; Richtung wird skaliert statt normiert, Segment wird quadratisch skaliert |
| `AxisAlignedBox3d.cs:369-376` | 3f↔3d-Konvertierung | M | B | Leere Box wird zur unendlichen Box |
| `AxisAlignedBox2d.cs:236`, `2f.cs:212` | `Contains`/`Intersects` | M | B | 2D strikt (`<`), 3D inklusiv. Uneinheitlich, Box enthält sich selbst nicht |
| `Interval1d.cs:128`, `Interval1i.cs:105` | unäres `-`, `*` negativ | M | B | Liefert verdrehtes Intervall (a>b) |
| `Quaterniond.cs:242`, `Quaternionf.cs:283`, `Frame3f.cs:436` | `Slerp`, `Interpolate` | M | B | Keine Behandlung von `dot<0`: Null-Quaternion oder Drehung über den langen Weg |
| `MathUtil.cs:317-324` | `PlaneAngleSignedD` (2D) | M | B | Antiparallel ergibt 0° statt 180° |
| `Box3.cs:42` | `Box3d(AxisAlignedBox3d)` | N | B | Extent über `Vector3f`, Präzisionsverlust |
| `Quaterniond.cs:195` | `AxisAngleR` | N | B | `Rad2Degf` (float) im double-Typ |
| `Vector4f.cs:68, 77` | `Normalized`, `IsNormalized` | N | B | double-Toleranzen im float-Typ |
| `AxisAlignedBox3f.cs:172` | `Contract` | N | B | Keine Klemmung (3d klemmt) |
| `AxisAlignedBox2i/3i` | Width/Volume der Empty-Box | N | B | int-Überlauf |
| alle Vektor/Index/Box-Typen, z. B. `Vector3d.cs:279` | `Equals(object)` | N | B | Wirft bei `null` oder fremdem Typ → `obj is T o && …` |
| `IndexUtil.cs:245` | `sort_indices` | N | B | Sortiert bei Duplikaten falsch |
| `MathUtil.cs:650` | `ModuloIteration` | N | B | Vielfache von 31337 → nur ein Element |
| `Box3.cs:196` | `Box3d.Contain(IEnumerable)` | N | B | Wirft bei leerer Eingabe |
| alle `ToString()` | | N | B | Kulturabhängig (`1,5`) |
| `MathUtil.cs:111-166` | `ClampAngleDeg/Rad` | N | B | Eine einzige ±360-Korrektur reicht nicht |

Mängel: `Interval1i.RangeInclusive` (Doku [0,N-1], Code [0,N]); unbenutzte Parameter (`AxisAlignedBox2i` fHalfDepth/sz, `MatrixUtil`); `(int)`-Cast statt Floor in `AxisAlignedBox*i.Contain`; Schwelle `double.Epsilon` (4.9e-324) in `Matrix3*.Inverse` und `Segment2d.Intersects`; `Box2d(Segment2d)` linkshändig; `Frame3f.ToFrame(ref Box)` verändert die Eingabe; `Vector3d.ComputeOrthogonalComplement` ohne `ref`; inkonsistente `==`/Equals-Verfügbarkeit; `Interval1i`-Enumerator endlos bei `int.MaxValue`.

### 2.2 `mesh/`

Die Kernoperatoren `SplitEdge`, `FlipEdge`, `CollapseEdge`, `PokeTriangle` und `MergeEdges` sind korrekt. Geprüft wurde jeweils mit und ohne Attribute; `CheckValidity` war danach immer true.

| Datei:Zeile | Klasse.Methode | Schwere | Status | Fehler → Fix |
|---|---|---|---|---|
| `DMesh3.cs:2282-2446` | `CompactInPlace` | H | B | `MapV` liefert `int.MinValue` für nicht verschobene Vertices; Timestamp wird nicht erhöht; Absturz bei leerem Mesh |
| `NTMesh3.cs:479` | `GetOrientedBoundaryEdgeV` | H | B | `triangles[ti]` statt `3*ti` |
| `MeshDecomposition.cs:102, 188` | `BuildLinear`/`extract_submesh` | H | B | `if … else if` in der Min/Max-Bestimmung, dadurch falsche Dreiecke in Submeshes |
| `SimpleMesh.cs:161-176` | `AppendVertices` | H | B | Verliert alle Normalen/Farben/UVs (Prüfreihenfolge); Z.170 schreibt den Farbwert in `Normals` |
| `MeshCaches.cs:20` | `MeshTriInfoCache` | H | B | Puffergröße `TriangleCount` statt `MaxTriangleID`, Absturz bei nicht kompakten Meshes |
| `DMesh3_edge_operators.cs:164-251` | `SetTriangle` | M | B | Kanten werden vor dem Entfernen gesucht und danach wiederverwendet → Exception |
| `DMesh3.cs:253-274` | `Copy(DMesh3)` | M | B | Alte Gruppen bleiben stehen; kein Timestamp-Update, `CachedBounds` veraltet (NTMesh3.Copy analog) |
| `DMesh3.cs:1298, 641` | `EnableTriangleGroups`, `AllocateTriangleGroup` | M | B | Vergibt belegte Gruppen-IDs bzw. überspringt welche |
| `MeshEditor.cs:125, 157, 287, 330` | `AddTriangleFan…`, `StitchLoop`, `StitchSpan`, `StitchUnorderedEdges` | M | B | Rücknahme bei Fehler ist unvollständig, verwaiste Dreiecke bleiben |
| `MeshEditor.cs:604-636` | `DisconnectBowtie` | M | B | Zerlegt zusammenhängende Fächer, reißt geschlossene Meshes auf |
| `NTMesh3.cs:1398` | `PokeTriangle(tid, bary)` | M | B | Ignoriert `bary` |
| `NTMesh3.cs:116` | `NTMesh3(DMesh3)` | M | B | Normalen/Farben/Gruppen werden nicht kopiert |
| `Remesher.cs:566`, `RemesherPro.cs:454…` | Smooth/Projection-Passes | M | B | `BitArray` wird parallel beschrieben, Vertices gehen verloren |
| `MeshConstraintUtil.cs:162, 197` | `ConstrainVtxLoopTo/SpanTo(Remesher…)` | M | B | `setID` wird nicht weitergereicht |
| `DSubmesh3.cs:35` | Konstruktor | M | B | `compute()` läuft vor den Object-Initializern, `ComputeTriMaps` u. a. wirken nicht |
| `EdgeLoop.cs:293` | `IsSameLoop` | M | B | `GetVertex(start_i)` statt `GetVertex(Vertices[start_i])` |
| `EdgeSpan.cs:229` | `CheckValidity` | M | B | Gibt immer `true` zurück |
| `MeshMeasurements.cs:147` | `MassProperties(…, bodyCoords)` | M | B | Parameter ignoriert |
| `MeshMeasurements.cs:392-409` | `ExtremeVertices` | M | B | `else if` |
| `SimpleMesh.cs:191-213` | `AppendTriangles` | M | B | Gruppen gehen verloren |
| `MeshWeights.cs:83, 44-56` | `VoronoiArea`, `CotanCentroid` | M | B | Fläche aus normierten Vektoren; rechter Winkel (cot=0) gilt als degeneriert |
| `MeshNormals.cs:76` | `Compute` | M | B | `VertexF` wird ignoriert |
| `FaceGroupUtil.cs:78, 110` | `CountAllGroups`, `FindTriangleSetsByGroup` | M | B | Absturz bei Gruppe -1 oder ≥ MaxGroupID |
| `DMesh3Builder.cs:183` | `Build` | M | B | siehe #104 |
| `MeshTransforms.cs:46` | `Rotate(…, Quaternionf)` | M | B | Normalen werden nicht rotiert; Positionen laufen über float |
| `Remesher.cs:244` (Reducer, EdgeLoopRemesher analog) | `next_edge` | N | B | Schritt 31337 deckt nicht alle Kanten ab, wenn ggT ≠ 1 |
| `Remesher.cs:458` | `update_after_split` | N | S | `FixedSetID > 0` statt `>= 0` |

Mängel: `AppendTriangle` gibt bei ungültiger Eingabe nie `InvalidID` zurück, weil `gDevAssert` vorher wirft; `RemesherPro.TrackedProjectionPass` iteriert `smooth_vertices()`; Timestamp-Rennen in parallelen `SetVertex`-Pässen (P); `GetBounds()` auf leerem Mesh liefert keine Empty-Box; `AddTriangleStrip` ignoriert `group_id`; `MeshTransforms.Scale` korrigiert bei Spiegelung weder Normalen noch Orientierung; `NotImplemented` in `EdgeSpan.IsSameSpan`, `FlipLeftRightCoordSystems(Frame3f)`.

### 2.3 `curve/`, `shapes3/`

| Datei:Zeile | Klasse.Methode | Schwere | Status | Fehler → Fix |
|---|---|---|---|---|
| `BSplineBasis.cs:82` → `NURBSCurve2.cs:426` | `Clone` | H | B | `mBD0` fehlt, jede geklonte NURBS wirft NRE (auch `PlanarComplex.FindSolidRegions`) |
| `BezierCurve2.cs:221` | `Clone` | H | B | NRE bei Grad 2 |
| `Arc2.cs:159-169` | `Arc2d.Bounds` | H | B | Negative Winkel ergeben negativen Array-Index |
| `Polygon2d.cs:819`, `PolyLine2d.cs:410` | `HasArcLength=true`, aber `SampleArcLength` wirft | H | B | `PlanarComplex.JoinElements`/`Transform` stürzen ab |
| `PolyLine2f.cs:135-191` | `DPolyLine2f.OrderVertices` | H | B | Indexer auf leere Liste, wirft immer |
| `CurveUtils.cs:180-191` | `IterativeSmooth` (offen) | H | B | Endpunkte kollabieren in den Ursprung |
| `CurveResampler.cs:19` | `SplitResample` (geschlossen) | H | B | `GetVertex(N)` |
| `DGraph2Util.cs:389` | `FindRayIntersection` | H | B | Iteriert `VertexIndices()` statt `EdgeIndices()` |
| `BaseCurve2.cs:22` | `GetMinTime` | M | B | `return mTMax` |
| `Arc2.cs:144` | `Arc2d.Transform` | M | B | Spiegelung ergibt Komplementärbogen (Circle/Ellipse analog, N) |
| `Polygon2d.cs:806` | `Polygon2DCurve.SampleT` | M | B | Schließende Kante wird nie abgetastet |
| `PolyLine2d.cs:394` | `PolyLine2DCurve.ParamLength` | M | B | Off-by-one |
| `PlanarComplex.cs:138, 918` | `Add(Polygon2d)`/`Transform` | M | B | Verändert das Polygon des Aufrufers |
| `CurveSampler2.cs:50-89` | `SampleT(spacing)`/`SampleArcLen` | M | B | Geschlossene Kurven haben doppelten Start/End-Vertex |
| `ParametricCurveSequence2.cs:19` | Konstruktor | M | B | `closed = true` ignoriert den Parameter |
| `GeneralPolygon2d.cs:181` | `Contains(Polygon2d)` | M | B | Umschlossene bzw. überlappende Löcher werden nicht erkannt |
| `GeneralPolygon2d.cs:208` | `Intersects(Polygon2d)` | N–M | B | Nur Kante gegen Kante, ein innenliegendes Polygon ergibt `false` |
| `CurveResampler.cs:84`, `CurveUtils2.cs:267`, `PolySimplification2.cs:146`, `DGraph2Util.cs:241` | diverse | M | B | Doppelter Start/End-Vertex in geschlossenen Ergebnissen |
| `DGraph2Resampler.cs:83, 177` | `Collapse*` | M | B | `% 0` bei leerem Graph |
| `PolySimplification2.cs:109` | `find_constrained_segments` | M | B | Quadrierte Distanz gegen unquadrierte Schwelle |
| `Polygon2d.cs:722` | `Chamfer` | M | B | Konvex und konkav vertauscht |
| `NURBSCurve2.cs:449-487` | periodische NURBS | M | B | Nicht-monotone Intervalle, `Last()` auf leerer Liste |
| `Circle3.cs:38` | `Circle3d(center,r)` | N | B | Linkshändiger Frame, widerspricht dem Kommentar |
| `DGraph.cs:344` | `SplitEdge(vA,vB)` | N | B | Ergebnis entspricht nicht der Doku |

Mängel: Orientierungs-Doku (`Arc2d` „clockwise“, `IsReversed`-Kommentare) ist invertiert; inkonsistenter Comparator in `PlanarComplex.FindSolidRegions` (P); `NotImplemented` bei `TangentT`, `Ellipse2d.FromCoefficients`, `BezierCurve2.Reverse`; fehlendes `Timestamp++` in `Polygon2d.SetVertices` und mehreren PolyLine2d-Methoden; `BSplineBasis` nicht threadsicher; `GetBounds` auf leerem Graph liefert keine Empty-Box.

### 2.4 `spatial/`, `distance/`, `queries/`, `containment/`

Gegen Brute-Force geprüft und korrekt: `DMeshAABBTree3` (`FindNearestTriangle/Vertex/HitTriangle`, alle Build-Strategien), `PointAABBTree3`, `WindingNumber`, SDF-Grid (seriell und parallel), mehrere `Dist*`-Klassen.

| Datei:Zeile | Klasse.Methode | Schwere | Status | Fehler → Fix |
|---|---|---|---|---|
| `DMeshAABBTree.cs:604` | `find_any_intersection(tree,tree)` | H | B | Absteigen mit `oBox` statt `oChild1` → Endlosrekursion, nicht abfangbarer StackOverflow |
| `MeshWindingNumberGrid.cs:155`, `MeshScalarSamplingGrid.cs:151` | `make_grid` | H | B | `BufferCells≠1` → IndexOutOfRange |
| `DistTriangle3Triangle3.cs:90` | `GetSquared` | H | B | `Triangle0Closest`/`Triangle1Closest` vertauscht (94/300 Zufallspaare) |
| `GridIndexing.cs:215, 230`, `GridIndexing2.cs:149, 163` | `MultigridIndexer*.ToBlock*` | H | B | Floor bei negativen Vielfachen falsch, `BiGrid3.Update` schreibt in falsche Zelle oder wirft |
| `DistPoint3Triangle3.cs:132`, `MeshQueries.cs:269` | degeneriertes Dreieck | M–H | B | NaN; das AABB-Pruning überspringt solche Dreiecke still |
| `MeshSignedDistanceGrid.cs:993, 1020, 971` | `point_triangle_distance` | M | B | Falsche Distanz bei degenerierten Dreiecken, `/0` |
| `GridIndexing*.cs:224, 157` | `ToBlockLocal` | M | B | Ignoriert `OuterShift`/`BlockShift` |
| `DenseGrid2.cs:67` | `assign_border` | M | B | Off-by-one |
| `Bitmap3.cs:126` | `Filter` | M | B | Löscht in-place während der Zählung |
| `DCurveBoxTree.cs:132` | `SquaredDistance(Ray3d)` | M | B | Liefert unquadrierte Distanz |
| `DCurveBoxTree.cs:301` | `build_sequential` | M | B | Endlosschleife bei 0 oder 1 Vertex |
| `BasicIntersectionTargets.cs:11, 24, 82` | `TransformedIntersectionTarget`, `PlaneIntersectionTarget` | M | B | `BaseTarget` ist immer null; Normale über `MapFromBasePosF`; Ebenen-Normale fest `AxisY` |
| `DistPoint2Circle2.cs:63` | | M | B | siehe #215 |
| `DistPoint3Cylinder3.cs:91, 155` | Punkt auf der Achse | M | B | NaN |
| `TilingUtil.cs:22, 77` | `BoundedRegularTiling2`/`CircleTiling2` | M | B | 1 statt 25 Kacheln |
| `IntersectionUtil.cs:210` | `RaySphere` (Rückgabe-Overload) | M | B | Ruft `LineSphere` auf |
| `RayIntersection.cs:19, 40, 70` | `Sphere`/Zylinder | M | B | Ursprung innen: `Math.Abs` ergibt falsches t |
| `ContMinBox2.cs:54` | Konstruktor | M | B | Kollineare Punkte → NotImplementedException |
| `ContBox3.cs:15` | `ContOrientedBox3.ResultValid` | M | B | siehe #211 |
| `PointAABBTree3.cs:229`, `DMeshAABBTree.cs:1314` | FastWinding-Cache | H | B | siehe #72 |
| `DSparseGrid3.cs:109` | `Density` | N | B | Infinity |
| `DCurveBoxTree.cs:57`, `Polygon2dBoxTree.cs:162, 220` | `max_dist` / `build_sequential` | N | B | Quadrierte Distanz gegen unquadrierte; IndexOutOfRange bei 2 Vertices |
| `MeshQueries.cs:462` | `FindNearestTriangles_LinearSearch` | N | B | `fNearestSqr` ist nicht quadriert |
| `DMeshAABBTree.cs:1587`, `PointAABBTree3.cs:519` | `build_top_down` | N–M | S | Mit NaN-Dreiecken: falsches Count, tid 0 doppelt |
| `GridIndexing*.cs:80ff` | `ToGrid` | N | B | `(int)` statt Floor |
| `SegmentHashGrid.cs:54` | `MaxExtent` | N | P | Außerhalb des Locks |

Mängel: `MeshQueries.EdgeLengthStats` `% 0`; `MeshScalarSamplingGrid` setzt „außen = 0“; `TriangleBinsGrid2d` ohne Klemmung (P); `EditMeshSpatial.FindNearestHitTriangle` ignoriert `fMaxDist`; Copy-Konstruktoren `DenseGrid2f/3f` mit leerer Quelle werfen NRE; toter Code `Distance.cs`.

### 2.5 `core/`, `io/`, `color/`, `approximation/`

| Datei:Zeile | Klasse.Methode | Schwere | Status | Fehler → Fix |
|---|---|---|---|---|
| `DijkstraGraphDistance.cs:233` | `Compute_Dense` | **K** | B | `g.distance = max_value` vor dem Update, alle Distanzen falsch → `= idx_priority` |
| `DijkstraGraphDistance.cs:545` | `update_neighbours_dense` | H | B | `nbr.distance` wird nicht aktualisiert, dadurch falscher kürzester Pfad |
| `IndexPriorityQueue.cs:90, 197` | `Contains` | H | B | Nach `Remove`/`Dequeue` meldet `Contains` weiter true; im Reducer wird dadurch die Priorität eines fremden Knotens geändert |
| `OBJReader.cs:65, 67` | `Triangle.is_complex` | H | B | Selbstvergleich `vNormals[j] != vNormals[j]`, dadurch falsche Normalen/UV-Zuordnung |
| `OBJReader`, `STLReader.cs:209`, `OFFReader.cs:61, 84`, `CommandArgumentSet.cs:94`, `Util.cs:140` | Parse ohne InvariantCulture | H | B | siehe #133 |
| `OBJWriter.cs:252` | `write_materials` | H | B | siehe #154 |
| `STLWriter.cs:60` + `gParallel.cs:199` | `ParallelStream` | H | B | Exception im Consumer-Thread beendet den Prozess; Producer-Fehler → Endlos-Spin |
| `BufferUtil.cs:548` | `DecompressZLib` | H | B | Nur ein einziges `Read`, ab .NET 6 werden Daten still abgeschnitten |
| `HBitArray.cs:130, 156` | `Set(i,false)`, Enumerator | H | B | Parent-Bit wird zu früh gelöscht (Kommentar „ACK NO THIS IS WRONG“); letzter Block wird bei Vielfachen von 32 ausgelassen |
| `BiArcFit2.cs:240` | `Fit` (Halbkreis) | H | B | `arc1 =` statt `arc2 =` |
| `OBJWriter.cs:42-110` | mehrere Meshes | M | B | `vn`/`vt`-Indizes falsch, wenn nicht alle Meshes Attribute haben |
| `OBJReader.cs:276` | `BuildMeshes_ByMaterial` | M | B | Face ohne `//n` → `vNormals[-6]` |
| `OBJReader.cs:203` | `append_triangle` | M | B | Ungültiger Index wirft statt zu warnen |
| `OBJReader.cs:597` | `ReadMaterials` | M | B | StreamReader wird nie geschlossen (Handle-Leck) |
| `OBJWriter`, `OFFWriter.cs:57`, `STLWriter.cs:107` | Format ohne InvariantCulture | M | B | siehe #53 |
| `Util.cs:88` | `IsBinaryStream/File` | M | B | Default `-1` liefert immer „nicht binär“ |
| `GaussPointsFit3.cs:138, 148` | gewichtete Variante | M | B | w² und (Σw)², Ergebnis um den Faktor 1/N falsch |
| `GaussPointsFit3.cs:~118` | Achsen | M | B | Zeilen statt Spalten von Q |
| `GaussPointsFit3.cs:171`, `OrthogonalPlaneFit3.cs:56` | `ResultValid` | M | B | Bereits diagonale Kovarianz gilt als Fehlschlag |
| `gSerialization.cs:393` | `Store/Restore(DMesh3)` | M | B | Isolierte Vertices und Punktwolken gehen verloren |
| `DVector.cs:282, 321, 121, 271, 358` | `Initialize`, `Apply`, `pop_back`, `GetBytes`/`FastGetBuffer` | M | B/P | Leeres Array → `Blocks[-1]`; lokaler statt globaler Index; `pop_back` nach `resize` ohne Wirkung; Überlauf |
| `Colorf.cs:44, 111` | `SqrDistance`, `operator -` | M | B | Shadowing von `b`; `a = f` statt `a - f` |
| `Colorb.cs:16` | Konstruktoren | M | B | Default-Alpha 1 statt 255 |
| `Colorb.cs:18` | float→byte | N | B | Abschneiden statt Runden |
| `IndexPriorityQueue.cs:353`, `DynamicPriorityQueue.cs:290` | `IsValidQueue` | N | S | `<` statt `<=` |
| `DynamicPriorityQueue.cs:66` | `Clear(false)` | N | S | Alte Referenzen bleiben stehen |
| `BufferUtil.cs:151`, `Util.cs:131`, `OBJReader.cs:396, 426`, `OFFReader.cs:109`, `SVGWriter.cs:418`, `STLReader.cs:137`, `SmallListSet.cs:144`, `Snapping.cs:274`, `Units.cs:66` | diverse | N | B/S | `Filter` ignoriert `max_i`; `ParseInt` gibt float zurück; `UVDimension`+1; `g` ohne Namen; OFF-Polygone n>3 → nur 1 Dreieck; SVG-Arc ohne Trenner; Native-Leck bei Exception; `Remove` auf leerer Liste; Snapping asymmetrisch; Meile 1609.34 |

Mängel: `new Colorf(1,0,0)` trifft den int-Konstruktor (ergibt 0.0039); `DVector.Clear` setzt die Länge nicht zurück; StandardMeshReader meldet Parse-Fehler als „Could not open file“; `ReadOptions.Defaults` ist eine geteilte, veränderliche Instanz; Format-Argumente in `SmallListSet.MemoryUsage` vertauscht; `ParallelStream` mit Busy-Spin auf nicht-volatilem Flag; viele `NotImplementedException` in `MappedList`/`IntSequence`.

### 2.6 `intersection/`, `implicit/`, `comp_geom/`, `solvers/`

| Datei:Zeile | Klasse.Methode | Schwere | Status | Fehler → Fix |
|---|---|---|---|---|
| `SingularValueDecomposition.cs:392, 431` | `Bidiagonalize` | **K** | B | Schon die Einheitsmatrix liefert ein falsches U·S·Vᵀ (siehe #168) |
| `SingularValueDecomposition.cs:506` | `DiagonalEntriesNonzero` | **K** | B | Vorzeichen von `sn`, Rang-defizite Matrizen falsch. Empfehlung: GTE-SVD neu portieren |
| `SingularValueDecomposition.cs:101` | `Solve` | M | S | `double.Epsilon` statt Maschinengenauigkeit |
| `SymmetricEigenSolver.cs:403` | `Tridiagonalize` | H | B | Derselbe Householder-Fehler (betrifft PlaneFit/GaussFit bei achsenparallelen Daten) |
| `SymmetricEigenSolver.cs:304` | `GetEigenvector(c)` | H | B | `mPermutation[c]` statt `[0]` |
| `SymmetricEigenSolver.cs:228, 277` | `IsRotation` | M | B | Paritätsberechnung falsch |
| `DenseMatrix.cs:101-279` | `Transpose`, `Multiply` | H | B | Indizes nur für quadratische Matrizen korrekt |
| `SparseSymmetricCG.cs:60, 67, 298, 312` | `Solve`, `MultipleRHS.Solve` | H | B | RHS=0 → NaN; konvergierte RHS werden weiter iteriert → NaN |
| `SparseSymmetricCG.cs:60` | Abbruchkriterium | N | B | Quadratische gegen lineare Norm |
| `SparseMatrix.cs:151` | `SymmetricSparseMatrix.Multiply` | M | B | Unsymmetrisches Produkt wird symmetrisch gespeichert |
| `PackedSparseMatrix.cs:327, 484, 549, 587` | `DotRowColumn`, `DotColumnVector`, `Square`, `MaxNorm` | M/N | B | Leere Zeile → Crash; `end_row` läuft über; A·Aᵀ statt A·A; fehlendes `Abs` |
| `CholeskyDecomposition.cs:99` | `ComputeParallel` | N | B | N=1 → Crash |
| `IntrLine2Triangle2.cs:156, 168` | `GetInterval` | M | B | Degeneriertes Dreieck auf der Linie → Exception |
| `IntrRay3Triangle3.cs:68, 133` | `Find`/`Intersects` | M | B | Absolute Toleranz, Dreiecke mit Kanten < 1e-3 gelten als parallel |
| `IntrRay3Triangle3.cs:76`, `IntrTriangle3Triangle3.cs:67` | `Find` | N | B | Type/Quantity werden nach einem Fehlschlag nicht zurückgesetzt |
| `IntrTriangle2Triangle2.cs:103` | `Find`/`Test` | M | B | `Find` setzt CCW voraus; `Test` und `Find` widersprechen sich |
| `IntrTriangle3Triangle3.cs:162` | degeneriert | N | B | siehe #85 |
| `ImplicitOperators.cs:126` | `ImplicitIntersection2d.Gradient` | M | B | Fehlende Klammern, falsches Kind |
| `ImplicitOperators.cs:38` | `ImplicitNAryOp2d.Bounds` | N | B | Box beginnt bei (0,0) statt Empty |
| `Implicit2d.cs:76` | `ImplicitPoint2d.InBounds` | M | B | `x` statt `y` |
| `Implicit2d.cs:59` | `ImplicitPoint2d.Gradient` | M | B | Radius ignoriert, NaN im Zentrum |
| `FalloffFunctions.cs:45` | `LinearFalloff.Duplicate` | M | B | Liefert `WyvillFalloff` |
| `MarchingQuads.cs:411, 388` | `SetBounds`, `ResetCells` | H/M | B | Gitter falsch verschoben bei positivem Min (keine Ausgabe); alte Werte bei zweitem Lauf |
| `GridImplicits3d.cs:56, 175`, `CachingGridImplicit3d.cs:58, 203`, `CachingMeshSDF.cs:453, 580` | `Value`/`Gradient` | M | B | Gradient nicht durch CellSize geteilt; `(int)` statt Floor |
| `CachingMeshSDF.cs:165` | `GetValue` | N | B | float-Marker geht ab ≥2048 verloren, Cache wirkungslos |
| `ConvexHull2.cs:196` | Konstruktor | H | B | Schritt 31337 → bei N = k·31337 fehlen Hull-Punkte |
| `Arrangement2d.cs:270` | `find_nearest_vertex` | M | B | Quadrierte Distanz als Radius, Snap-Toleranz 3e-3 statt 1e-5 |

Mängel: `DenseMatrix.IsPositiveDefinite` prüft nur Diagonaldominanz; `Cholesky.Compute` gibt immer true zurück; `ConvexHull2`-Modi mit `NotImplemented`; `ImplicitIntersection3d.Bounds` liefert die Vereinigung; Doku der Smooth-Intersection/Difference vertauscht; SVD-Kopfdoku falsch.

### 2.7 `mesh_ops/`, `mesh_generators/`, `mesh_selection/`

Ohne Befund geprüft (gültig, positives Volumen): Hole-Filler (Simple/Minimal/Smoothed/Auto), MeshAutoRepair, Extrude*, MarchingCubes(+Pro, Kugel), MeshPlaneCut mit Füllung, Box/Sphere/Revolve/Tube-Generatoren.

| Datei:Zeile | Klasse.Methode | Schwere | Status | Fehler → Fix |
|---|---|---|---|---|
| `MeshMeshCut.cs:208` | `find_segments` | **K** | B | siehe #138 (zerstört die Mesh-Gültigkeit) |
| `CylinderGenerators.cs` (mehrere) | Open/Capped-Cylinder, Cone, VerticalGeneralizedCylinder | H | B | Wicklung zeigt nach innen (Volumen negativ), widerspricht den gesetzten Normalen und allen anderen Generatoren |
| `MarchingCubesPro.cs:198-200` | `edge_hash` | H | B | `const int 1<<60` ist in Wahrheit `1<<28`, Hash-Kollisionen ab 4096 Zellen in y → `long`/`1L<<60` |
| `RemoveOccludedTriangles.cs:132` | `Apply` (PerVertex) | H | B | Paralleles Schreiben in `BitArray`, nichtdeterministisch (auch MeshAutoRepair) |
| `CylinderGenerators.cs:29, 95, 232`, `DiscGenerators.cs:21, 72` | Keil/Halbscheibe | M | B | Überzählige (0,0,0)-Dreiecke |
| `CylinderGenerators.cs:247` | Cone-Normale | M | B | `BaseRadius/Height` statt `BaseRadius` |
| `BoxGenerators.cs:25` | `TrivialBox3Generator` | M | B | `Box.Center[i]` (Skalar) statt `Box.Center` |
| `PlaneGenerators.cs:319, 355` | `RoundRectGenerator` | M | B | UV.y immer 0; `GetBorderLoop` mehrfach falsch |
| `GenCylGenerators.cs:110, 144` | `TubeGenerator` (ClosedLoop) | M | B | Frame verdreht an der Naht |
| `MeshPlaneCut.cs:239` | `FillHoles` | M | B | Erfolgsabfrage invertiert |
| `MergeCoincidentEdges.cs:409` | `OnlyUniquePairs` | M | B | `continue` in der falschen Schleife |
| `LaplacianMeshSmoother.cs:397` | `RegionSmooth` | M | B | siehe #110 |
| `MeshVertexSelection.cs:291` | `FloodFill` | M | B | NRE beim Default-Filter |
| `MeshFaceSelection.cs:34, 342` | `ExpandToOneRingNeighbours` | M | B | `BitArray` wird nicht nachvergrößert |
| `MeshIsoCurves.cs:64, 318` | `Compute`/`SplitAtIsoCrossings` | M | B | `EdgeLocations` wird nie zurückgesetzt |
| `MeshBoolean.cs:55` | `Compute` | M | B | Ergebnis ist offen und unverschweißt, gibt trotzdem immer true zurück |
| `MeshVertexSelection.cs:155` | `SelectConnectedBoundaryV` | M/N | B | Springt in andere Randschleifen |
| `MeshInsertUVPolyCurve.cs:202` | | N | B | `vid > 0` statt `>= 0` |
| `MeshIsoCurves.cs:342` | | N | B | Mindestabstand gegen `mid` statt `pos` geprüft |
| `RevolveGenerator.cs:108, 277` | Kappen-Normalen | N | B | ±Z statt ±Y |
| `CylinderGenerators.cs:368, 266` | VGenCyl-UV, Cone NoShared | N | B | UV > 1; Dreiecke mit Fläche 0 |
| `PointsMeshGenerators.cs:168` | | N | B | Normalen nie gefüllt |
| `LaplacianMeshSmoother.cs:115`, `LaplacianMeshDeformer.cs:115` | | N | S | `M.Set(vid,vid)` statt `(i,i)` |
| `MeshBoundaryLoops.cs:207, 217` | | N | S | `CountValid`-Ergebnis verworfen; `e1 = 1` statt -1 |
| `MeshTopology.cs:130`, `MeshStitchLoops`, `PlanarHoleFiller`, `MeshExtrude*`, `SmoothedHoleFill`, `VoxelSurfaceGenerator`, `MeshICP.cs:169` | diverse | N | S/P | `Abs(dot)` übersieht sehr scharfe Kanten; Rückgaben ungeprüft; `FailedMerges` immer 0; Meshes sammeln sich an; Array-Index statt Vertex-ID |

Mängel: Abbruch im Halbzustand (MeshPlaneCut, MeshInsertUVPolyCurve); `MeshMeshCut.connect_edges` verschluckt Exceptions; Debug-Reste (`Console.WriteLine`, `throw new Exception("shit")`); toter Code (AutoHoleFill-PlanarSpans, `remove_duplicate_faces`, leere Klasse `MeshOps`); falsche Gruppen-ID-Doku bei CappedCylinder; kopierte Doku bei `MeshTrimLoop`.

---

## 3. Querschnittsthemen

1. **Kulturabhängigkeit:** Alle Reader/Writer sind nur über `StandardMeshReader/Writer` sicher. Direkt aufgerufen lesen sie unter de-DE still falsche Werte. Für einen deutschsprachigen Nutzer ist das besonders relevant.
2. **Threading:** `BitArray` wird mehrfach aus `gParallel.ForEach` beschrieben (Remesher, RemesherPro, RemoveOccludedTriangles). Dazu kommen der FastWinding-Cache und die Exceptions in `ParallelStream`.
3. **Schrittweite 31337:** `(i + 31337) % N` deckt bei N = k·31337 nicht alle Elemente ab (ConvexHull2, Remesher, Reducer, DGraph2Resampler, MeshQueries, MathUtil.ModuloIteration). Selten, aber dann still falsch.
4. **`double.Epsilon` als Toleranz:** an mehreren Stellen (SVD, Matrix3.Inverse, Segment2d.Intersects). Gemeint war die Maschinengenauigkeit.
5. **Degenerierte Eingaben:** NaN bzw. Exceptions in Distanz- und Intersection-Routinen bei kollinearen Dreiecken, Null-Segmenten und Punkten auf der Achse.
6. **Solver-Basis:** SVD und SymmetricEigenSolver sind bei strukturierten Matrizen (diagonal, achsenparallel, Rang-defizit) falsch. Das schlägt auf die Fits (Plane/Gauss/OBB) und auf MeshICP durch.

## 4. Empfohlene Reihenfolge für Fixes

1. **Einzeiler mit großer Wirkung:** `InvSqrt`, `Matrix3d/f`-Konstruktor, `Index3i/4i`-Copy, `Transpose3x3`, `MeshMeshCut:208`, Dijkstra (dense), `IndexPriorityQueue.Contains`, `OBJReader.is_complex`, `BiArcFit2`, `MarchingCubesPro` (long), `DistTriangle3Triangle3`, `find_any_intersection`, `ContOrientedBox3.ResultValid`, `DistPoint2Circle2`, `BaseCurve2.GetMinTime`, `MeshPlaneCut.FillHoles`, `EdgeSpan.CheckValidity`, `LinearFalloff.Duplicate`, `ImplicitPoint2d.InBounds`.
2. **I/O:** InvariantCulture in allen Readern und Writern, `.mtl`-Flush, `ReadMaterials`-Leck, `DecompressZLib`-Schleife, `ParallelStream`-Exceptions.
3. **Solver:** SVD (zwei Fixes, besser neu portieren) und SymmetricEigenSolver.
4. **Threading:** `BitArray` durch `bool[]` ersetzen; FastWinding-Cache mit Lock.
5. **Generatoren:** Zylinder- und Kegel-Wicklung. Achtung: Das ändert das sichtbare Verhalten für bestehende Nutzer.
6. Danach die Mittel- und Niedrig-Befunde abschnittsweise, jeweils mit Regressionstest.

Ein Test-Projekt gibt es im Repo bisher nicht. Sinnvoll ist, die Repro-Programme als Unit-Tests mit den Fixes einzuchecken.
