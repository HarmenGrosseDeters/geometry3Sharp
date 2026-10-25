# Exakte geometrische Prädikate – Bestandsaufnahme (Oktober 2026)

Überblick über alle Implementierungen von orient2d, orient3d und incircle in den drei Code-Basen
geometry3Sharp (g3), Triangle.NET-Fork (`HarmenGrosseDeters/triangle.net`) und dem Wrapper „MeshFunctions“
(`HarmenGrosseDeters/Triangle.Net-Old-Custom`): was es gibt, was garantiert ist, wo die Grenzen liegen, wer
was benutzt, wie es getestet ist und welche Lizenz gilt. Am Ende die offenen Punkte als Vorschlag.

Stand: g3 Branch `predicates-consolidation` (= `volumetric-booleans` `f5c7f38` + diese Doku und der Harness),
Triangle.NET-Fork `master` `01af2aa` und offener PR #5 (`claude/awesome-pascal-eex2py`, `0b1b934`). Old-Custom konnte in dieser Session nicht gelesen werden (privates
Repository, kein Zugriff freigegeben); die Angaben dazu stammen aus der vorigen Session und sind als solche
markiert.

## Kurzfassung

| | g3 `ExactPredicates` | Triangle.NET `RobustPredicates` | Old-Custom `Primitives` (`main`) | Old-Custom `claude/fallback-adapter` |
|---|---|---|---|---|
| Prädikate | orient2d, orient3d, incircle | orient2d (`CounterClockwise`), incircle (`InCircle`/`NonRegular`) | orient2d, incircle | wie links |
| Rückgabe | Vorzeichen `int` | Determinante `double` (Vorzeichen exakt, Betrag genähert) | `double` | `double` |
| Exakt im Bereich ~2^±100 | ja | ja (Shewchuk-Stufen A–D) | **nein** (`decimal`) | ja (delegiert an Triangle.NET) |
| Exakt bei Unter-/Überlauf | ja (BigInteger) | **nein** auf `master`; ja mit PR #5 | nein | **nein** (ja, sobald PR #5 im Fork ist) |
| Herkunft | eigene Implementierung nach dem Paper | Port von `predicates.c`-Teilen aus `triangle.c` | Triangle.NET alt | – |
| Lizenz | Boost | Triangle-Lizenz (Shewchuk) / MIT (Triangle.NET) | dto. | dto. |

**Fazit:** g3 ist die einzige Implementierung, die für alle endlichen Doubles das richtige Vorzeichen liefert.
Triangle.NET ist im normalen Koordinatenbereich exakt, scheitert aber bei sehr kleinen Koordinaten (gemessen
1e-160 und 1e-300: Produkte subnormal oder 0) und sehr großen (1e200: Produkte laufen über; incircle
schon bei 1e150, weil dort Terme vom Grad 4 entstehen). Old-Custom `main` ist schon bei normalen Koordinaten falsch.

**PR #5 im Triangle.NET-Fork** („Prädikate: exakt auch bei Unter- und Überlauf“) schließt die Lücke von
Triangle.NET nach derselben Idee wie g3 (Bereichstest, sonst BigInteger). Im Harness nachgemessen: in allen
Szenarien 0 Fehler; die neuen Tests des PR schlagen auf `master` fehl (9 von 11) und laufen mit dem PR grün
(20/20 Prädikat-Tests). Siehe Abschnitt 2.

## 1. geometry3Sharp

### `math/ExactPredicates.cs` – öffentliche API

```
int ExactPredicates.Orient2d(Vector2d a, Vector2d b, Vector2d c)
int ExactPredicates.Orient3d(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
int ExactPredicates.InCircle(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
```

**Vorzeichen:**
- Orient2d: +1, wenn a, b, c gegen den Uhrzeigersinn liegen (= Shewchuk `orient2d`).
- Orient3d: +1, wenn d auf der Seite liegt, in die die Normale des ccw-Dreiecks a, b, c zeigt, also
  sign(((b−a)×(c−a))·(d−a)). **Das ist das Gegenteil von Shewchuks `orient3d`** (dort > 0, wenn d *unter*
  der Ebene liegt). Intern wird beim Aufruf von `AdaptivePredicates.Orient3d` das Vorzeichen umgedreht.
- InCircle: +1, wenn d im Kreis durch ccw a, b, c liegt (= Shewchuk `incircle`); bei cw-Eingabe umgekehrt.

**Ablauf (alle drei Prädikate):**
1. **Stufe A, Filter:** Determinante in double, Fehlerschranke nach Shewchuk (`(3+16ε)ε`, `(7+56ε)ε`,
   `(10+96ε)ε` mal Permanente). Orient3d/InCircle entscheiden nur, wenn die Permanente > 1e-200 ist
   (`UnderflowLimit`, sonst gilt die relative Schranke wegen subnormaler Produkte nicht). Überlauf ergibt
   ∞/NaN, der Vergleich `det > bound` ist dann falsch → weiter zu 2. Orient2d entscheidet ebenso nur, wenn
   |l| + |r| > 1e-200 ist. *Korrigiert:* Ohne diese Grenze gab Orient2d bei exakt kollinearen Punkten um
   1e-160 ±1 statt 0 – die Schranke rundet bei subnormalen Produkten auf 0, die Rundungsfehler der
   Differenzen lassen aber ein `det` von 5e-324 übrig (Test `Orient2d_SubnormalProducts`).
2. **Exakter Pfad** (`*_exact`): gleiche Punkte → 0. Liegen alle Koordinaten bei 0 oder in [2^-100, 2^100]
   (`Expansion.InRange`), dann
   - `AdaptivePredicates` (Stufen B und C nach Shewchuk), bei `NotDecided`
   - volle Expansion über `Expansion.Terms` (Liste von Doubles, Produkte paarweise per Dekker-TwoProduct,
     Vorzeichen per Grow-Expansion; Puffer pro Thread, keine Allokation).
   Sonst `*_bigint`: alle Koordinaten als `m·2^e` auf gemeinsamen Exponenten, Determinante in `BigInteger`.
3. NaN → `ArgumentException` (im exakten Pfad); ±∞ ebenso (Test `InfiniteCoordinates_Throw`).

`Expansion.TwoProduct` nutzt bewusst kein FMA (`Math.FusedMultiplyAdd` gibt es unter netstandard2.0/net48
nicht); Dekker-Splitting mit `2^27+1` ist exakt, solange nichts über-/unterläuft – dafür sorgt `InRange`.

### `math/AdaptivePredicates.cs` – Stufen B und C (intern)

- Stufe B: Determinante exakt aus den *gerundeten* Differenzen (Expansion aus wenigen Dutzend Doubles),
  Schranke `ccwerrboundB` usw. Sind alle Differenzen exakt (Tails = 0), entscheidet B direkt.
- Stufe C: Terme erster Ordnung der Rundungsfehler der Differenzen, Schranke `…errboundC`.
- Keine Stufe D; was C nicht entscheidet, rechnet die volle Expansion in `ExactPredicates`.
- Vorzeichen nach Shewchuk (Orient3d also umgekehrt zu g3, siehe oben).
- Workspace pro Thread (`[ThreadStatic]`), Arrays fester Größe.

### Weitere Prädikate in g3

| Ort | Was | Beziehung zu `ExactPredicates` |
|---|---|---|
| `mesh_ops/ExactMeshArrangement.cs` `Orient`, `InCircle` | orient2d/incircle auf **rationalen** (homogenen) Punkten `ExactPoint3` in der Projektion eines Dreiecks, mit eigenem Filter auf genäherten Koordinaten (`orient2d_approx`, Fehlerschranken in den Kommentaren hergeleitet), exakt per `BigInteger`-Det3/Det4 | eigenständig, weil die Punkte implizite Schnittpunkte sind (keine Doubles); keine Dublette |
| `ExactMeshArrangement` (Eingabedreiecke, Degeneriertheit) | `ExactPredicates.Orient3d`/`Orient2d` auf Eingabekoordinaten | nutzt die zentralen Prädikate |
| `mesh_ops/ExactMeshBoolean.cs` `orient3d(ExactPoint3…)` | Vorzeichen der 4×4-Determinante homogener Punkte (Strahltest) | eigenständig (rationale Punkte), über `ExactMeshArrangement.Det4` |
| `mesh_ops/ExactTriangulation2.cs` | CDT, bekommt `orient`/`incircle` als Delegates | nutzt, was der Aufrufer gibt (aus `ExactMeshArrangement`) |
| `intersection/ExactTriangleIntersection.cs` | Dreieck-Dreieck-Tests | nutzt `ExactPredicates` |
| `mesh_ops/TerrainSafety.cs`, `TerrainVolumeDifference.cs` | Seiten-/Überlappungstests | nutzen `ExactPredicates` |
| `math/Query2.cs` (`Query2d`), `QueryTuple2d.cs`, `PrimalQuery2d.cs` | alte WildMagic/GTEngine-Ports, double **ohne** exakten Fallback | nicht exakt; benutzt von `ConvexHull2`, `IntrTriangle3Triangle3`, `MeshInsertUVPolyCurve` |
| `math/Query2Integer.cs` (`Query2Int64`) | Ganzzahl-Version, castet auf `long` | exakt nur für kleine ganzzahlige Eingaben (Überlauf ab ~2^31) |
| `math/MathUtil.IsLeft` | double ohne Filter | nicht exakt |

Es gibt in g3 also **eine** Double-Implementierung (ExactPredicates + AdaptivePredicates + Expansion). Die
früheren zwei parallelen Expansions-Implementierungen wurden beim Merge von `claude/youthful-clarke-843cvk`
zusammengeführt (übernommen wurde die des Benutzers); es ist keine zweite Kopie mehr im Code.

### Tests

`tests/geometry3Sharp.Tests/ExactPredicatesTests.cs` (alle grün, net10.0):
- gegen BigInteger: `Orient_MatchesExactIntegerArithmetic_NearDegenerate`, `ExpansionArithmetic_MatchesBigInteger`,
  `ExpansionArithmetic_Cocircular`, `TwoProduct_IsExact`
- Skalen: `Orient_TinyCoordinates`, `Orient3d_SubnormalProducts`, `Predicates_NearDegenerate_ScaledIntoTheSubnormalRange`,
  `Orient_ExpansionPath_CoplanarAndNearCoplanar_AtManyScales`
- adaptive Stufen: `AdaptiveStages_DecideNearlyDegenerateCasesCorrectly` (jedes Vorzeichen von B/C gegen
  BigInteger, > 90 % entschieden; fängt Mutationen der Stufe-C-Terme), `AdaptiveStages_ExactDifferences_DecideDegenerateCases`
- Grundfälle, `InfiniteCoordinates_Throw`, Dreieck-Dreieck-Tests (`TriTri_*`)

Dazu `ExactImplicitPointTests.cs` und `ExactBooleanBuildingBlockTests.cs` für die rationalen Prädikate des Arrangements.

## 2. Triangle.NET-Fork (`HarmenGrosseDeters/triangle.net`)

`src/Triangle/RobustPredicates.cs` (implementiert `IPredicates`):
- `CounterClockwise` + `CounterClockwiseAdapt` (Stufen A–D), `InCircle` + `InCircleAdapt` (A–D,
  vollständige Stufe D), `NonRegular` = `InCircle`, `FindCircumcenter` (Konstruktion, kein Prädikat).
- Im Kern 1:1 aus `triangle.c` (Makros per `CL /P` expandiert). Abweichungen: Grenzprüfungen in
  `FastExpansionSumZeroElim` (das C-Original liest ein Element hinter das Array-Ende), B-Array-Länge 5,
  Workspace-Arrays lokal bzw. pro Aufruf, nicht-atomare `Statistic`-Zähler, Schalter `Behavior.NoExact`.
- Kein orient3d.
- **Keine Behandlung von Unter-/Überlauf:** Bei Koordinaten ~1e-160 werden die Produkte subnormal oder 0,
  der Schnelltest `detleft > 0 && detright <= 0 → return det` und die Expansionen sind dann nicht mehr
  exakt; bei großen Koordinaten entstehen ∞/NaN (`Math.Sign(NaN)` wirft).
- Rückgabe ist die Determinante als `double`. Viele Aufrufer werten nur das Vorzeichen aus, einige aber
  auch den Betrag (z. B. `QualityMesher` `multiplier = CounterClockwise(...)`, `Dwyer`/`ConstraintMesher`
  `area = ...`). Eine Korrektur für Unter-/Überlauf muss deshalb ein Ergebnis mit richtigem Vorzeichen und
  sinnvollem Betrag liefern.
- Tests: `src/Triangle.Tests/RobustPredicatesTest.cs` (Grundfälle), `Robustness/PredicatesTest.cs`
  (fast kollineare Punkte unterschiedlicher Größenordnung, unendliche Koordinaten).

**PR #5 (`0b1b934`, offen):** behebt Unter-/Überlauf.
- `CounterClockwise`: Die Schnellzweige mit Produkten verschiedenen Vorzeichens bleiben (Rundung ist monoton,
  ändert also kein Vorzeichen eines Produkts und keine Reihenfolge; ∞ und −0 sind dort unkritisch). Der Zweig
  `detleft == 0` (oder NaN) gibt `det` nur zurück, wenn ein Faktor exakt 0 ist und `detright` kein auf 0
  gerundetes Produkt; sonst exakt. Filter nur, wenn `detsum >= 1e-200`.
- `InCircle`: Filter nur, wenn alle Differenzen 0 oder in [2^-200, 2^200] liegen (Produkte vom Grad 4 dann in
  [2^-800, 2^800]).
- Adaptive Stufen nur für Koordinaten 0 oder in [2^-100, 2^100], sonst `CounterClockwiseExact`/`InCircleExact`
  in BigInteger. Rückgabe über `ExactResult`: das double-`det`, wenn es endlich ist und das exakte Vorzeichen
  hat, sonst ±1 (Aufrufer, die den Betrag nutzen, bekommen also weiterhin den genäherten Wert).
- Neue Tests `ExtremeCoordinatesGiveTheExactSign` (1e-100 … 1e-310, 1e100 … 1e300) und
  `MixedMagnitudesGiveTheExactSign`, Referenz BigInteger.
- Durchgesehen: Argumentation und Schranken passen; im normalen Bereich ist orient2d unverändert, incircle hat
  sechs zusätzliche Vergleiche. Im Harness 0 Fehler in allen Szenarien. Kein Einwand gegen den Merge.
- Lizenz: übernimmt nur die Idee aus g3, kein Problem (Richtung g3 → Triangle.NET wäre auch Code erlaubt).

## 3. Old-Custom „MeshFunctions“ (`HarmenGrosseDeters/Triangle.Net-Old-Custom`)

*Nicht nachgeprüft (kein Zugriff in dieser Session), Stand laut voriger Session:*
- `TriangleLib/Primitives.cs` auf `main`: unsichere Fälle werden in `decimal` nachgerechnet. Das ist **nicht
  exakt**: `(decimal)double` behält ~15 signifikante Stellen, und die Produkte der Prädikate brauchen mehr als
  28 Stellen. Gemessen 9–29 % falsche Vorzeichen bei fast degenerierten Fällen schon bei normalen Koordinaten.
- Branch `claude/fallback-adapter`, Commit `4bfefd0` „Exakte Prädikate statt decimal“: delegiert die
  unsicheren Fälle per `extern alias neu` an `RobustPredicates` des Triangle.NET-Forks (Projekt-Referenz
  `..\..\Triangle.NET\src\Triangle\Triangle.csproj`), Filter gegen Unter-/Überlauf abgesichert. Im normalen
  Bereich 0 Fehler, bei 1e-160/1e-300/1e150/1e200 aber dieselben Fehler wie Triangle.NET.
- Der Code-Kommentar dort behauptet einen Ganzzahl-Pfad für sehr kleine/große Koordinaten, den es im Fork
  nicht gibt → Kommentar falsch (oder Fork ergänzen, siehe unten).
- Targets net48;net8.0-windows, unter Linux nicht direkt baubar.

## 4. Messung

`benchmarks/PredicateComparison` (siehe README dort), je 20 000 Fälle, Referenz: exakte BigInteger-Determinante
der tatsächlichen Doubles. Ausnahmen und NaN zählen als falsch. Spalten: g3 | naives double | Triangle.NET.

| Szenario | orient2d | incircle | orient3d (g3 \| naiv) |
|---|---|---|---|
| fast degeneriert, Skala 1 | 0 \| 1210 \| 0 | 0 \| 2012 \| 0 | 0 \| 2106 |
| fast degeneriert, 1e6 | 0 \| 1218 \| 0 | 0 \| 1988 \| 0 | 0 \| 2084 |
| fast degeneriert, 1e-10 | 0 \| 1164 \| 0 | 0 \| 1959 \| 0 | 0 \| 2099 |
| fast degeneriert, 1e-160 | 0 \| 20000 \| **20000** | 0 \| 20000 \| **20000** | 0 \| 20000 |
| fast degeneriert, 1e-300 | 0 \| 20000 \| **20000** | 0 \| 20000 \| **20000** | 0 \| 20000 |
| fast degeneriert, 1e150 | 0 \| 1194 \| 0 | 0 \| 20000 \| **20000** | 0 \| 20000 |
| fast degeneriert, 1e200 | 0 \| 20000 \| **20000** | 0 \| 20000 \| **20000** | 0 \| 20000 |
| Gitter exakt degeneriert, 1 | 0 \| 0 \| 0 | 0 \| 0 \| 0 | 0 \| 0 |
| Gitter, 1e6 | 0 \| 0 \| 0 | 0 \| 9232 \| 0 | 0 \| 5877 |
| Gitter, 1e-160 | 0 \| 18095 \| **18095** | 0 \| 18944 \| **18944** | 0 \| 19691 |
| Gitter, 1e150 | 0 \| 3017 \| 0 | 0 \| 20000 \| **20000** | 0 \| 19843 |
| Kettner-Gitter 141², 1 | 0 \| 5980 \| 0 | 0 \| 1378 \| 0 | 0 \| 3032 |
| Kettner-Gitter, 1e-160 | 0 \| 19740 \| **19740** | 0 \| 19880 \| **19880** | 0 \| 19740 |
| Kettner-Gitter, 1e200 | 0 \| 19881 \| **19881** | 0 \| 19881 \| **19881** | 0 \| 19881 |

Laut voriger Session (nicht nachgemessen) Old-Custom `main`: fast degeneriert Skala 1: 1853 (orient2d) /
5820 (incircle) falsch; Kettner-Gitter 614 / 5693.

Zeit für 200 000 Aufrufe (ms, net10.0, ein Kern):

| | orient2d allg. | fast kollinear | incircle allg. | fast kozirkulär | orient3d allg. | fast koplanar |
|---|---|---|---|---|---|---|
| g3 | 3 | 7 | 2 | 169 | 3 | 54 |
| Triangle.NET | 8 | 12 | 10 | 178 | – | – |
| naiv double | 2 | 1 | 2 | 1 | 1 | 1 |

(Triangle.NET allokiert pro Aufruf drei `Point`-Objekte im Harness; das erklärt einen Teil des Unterschieds
in allgemeiner Lage.)

## 5. Lizenzlage

- g3 steht unter der Boost-Lizenz. `ExactPredicates`, `AdaptivePredicates` und `Expansion` sind nach dem
  Paper (Shewchuk 1997) geschrieben, ohne Code aus `predicates.c`, `triangle.c` oder Triangle.NET; siehe
  `docs/SOURCES.md`.
- Shewchuks `predicates.c` ist gemeinfrei. `triangle.c` (und damit der daraus expandierte Code in
  Triangle.NETs `RobustPredicates.cs`) steht unter Shewchuks Triangle-Lizenz (keine kommerzielle Nutzung
  ohne Erlaubnis). **Aus Triangle.NET darf nichts nach g3 kopiert werden.** Umgekehrt dürfte Boost-Code
  aus g3 nach Triangle.NET übernommen werden (Boost ist permissiv; Lizenzhinweis mitnehmen).

## 6. Offene Punkte / Vorschlag

1. **Triangle.NET-Fork: Unter-/Überlauf** – erledigt durch PR #5 (geprüft, siehe Abschnitt 2); bleibt nur
   der Merge. Optional später: orient3d ergänzen (wird in Triangle.NET derzeit nicht gebraucht).
2. **Old-Custom:** Nach dem Merge von PR #5 stimmt der Kommentar zum Ganzzahl-Pfad (er beschreibt dann den
   Fork); `claude/fallback-adapter` muss gegen den Fork-Stand mit PR #5 gebaut werden. Offene PRs dort konnte
   ich nicht sehen (kein Zugriff).
   Alternativ g3 statt Triangle.NET nutzen – das würde aber eine Abhängigkeit von g3 einführen; einfacher
   ist es, bei Triangle.NET zu bleiben, nachdem Punkt 1 erledigt ist. Benötigt Zugriff auf das Repository.
3. **g3:** keine Dubletten bei den Double-Prädikaten. Kandidaten zum Umstellen auf `ExactPredicates`: die
   alten nicht-exakten Queries (`Query2d`, `QueryTuple2d`, `PrimalQuery2d`) in `ConvexHull2`,
   `IntrTriangle3Triangle3`, `MeshInsertUVPolyCurve` – das ändert aber Verhalten dieser alten Klassen und ist
   ein eigenes Thema.
4. **g3, Geschwindigkeit:** fast kozirkuläre incircle-Fälle (169 ms / 200k) und fast koplanare orient3d-Fälle
   fallen oft in die volle Expansion (O(n²)-Grow-Expansion über ~1500 Terme bei incircle). Eine Stufe D
   (Shewchuks Fortsetzung von C mit den Tail-Produkten, nur mit kleinen Expansionen) würde das beschleunigen.
   Nur lohnend, wenn das in echten Daten ins Gewicht fällt.
5. **Harness** als Test absichern: die Szenarien bei extremen Skalen gibt es in g3 schon als Unit-Tests;
   für Triangle.NET sollen sie mit Punkt 1 als NUnit-Tests in den Fork.
