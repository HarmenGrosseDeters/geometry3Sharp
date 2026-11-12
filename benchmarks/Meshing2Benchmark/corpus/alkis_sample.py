#!/usr/bin/env python3
"""A stratified, seeded sample of ALKIS tiles across NRW for the 2D mesher (a fixed rule, not hand-picked tiles).

    python3 benchmarks/Meshing2Benchmark/corpus/alkis_sample.py --plan                      # strata, districts, download sizes
    python3 benchmarks/Meshing2Benchmark/corpus/alkis_sample.py --draw [--only STRATUM ...]  # fetch, draw, write inputs + manifest

The rule (fixed here, before any mesher run; change it only with a new SEED or a new version of STRATA, never after looking at results):

  Cells:    the 200 m cells of the UTM 32N grid (south-west corner a multiple of 200 m, like the Herne tiles) of a district.
  Measures: nb = buildings, np = parcels whose bounding box centre lies in the cell; use = the land use (Nutzung layer, attribute
            nutzart) at the cell centre; big = the largest bounding box area (m2) of a parcel that intersects the cell.
  Strata:   STRATA below, each a predicate on (nb, np, use, big), the number K of tiles, and the districts it draws from.
  Strata:   mutually exclusive: a cell belongs to the first stratum of the table whose predicate holds (classify). So the draw of a
            stratum depends on nothing but its own earlier tiles, and `--only` and `--first` give subsets of the full sample.
  Draw:     the candidate cells of a (stratum, district) pair are ordered by sha256(SEED | district | cell); the first cell of the
            stratum (classify) that was not taken by an earlier tile of the same stratum is taken (a uniform draw without replacement). Tile i of a stratum
            comes from district DISTRICTS[i mod len(DISTRICTS)], so every stratum spreads over its districts.
  Output:   <out>/<stratum>/ with the inputs of alkis_tiles.py (m2_, base_, cq_ files) and <out>/sample.json (the manifest: stratum,
            district, tile, nb, np, use, big, counts, input checksums). Nothing of it is committed.
"""
import argparse
import hashlib
import json
import os
import sys

import alkis_tiles as at

SEED = "g3-alkis-2026-11-12"
CELL = 200

# district keys: Kreis / kreisfreie Stadt, see `alkis_tiles.py --list-districts`
KOELN, DUESSELDORF, DORTMUND, BIELEFELD, DUISBURG = "05315000", "05111000", "05913000", "05711000", "05112000"
BORKEN, EUSKIRCHEN, SIEGEN, ERFT, DUEREN, HOEXTER, HERNE = "05554000", "05366000", "05970000", "05362000", "05358000", "05762000", "05916000"

BUILDING_USE = ("Wohnbaufläche", "Fläche gemischter Nutzung")
MINING_USE = ("Bergbaubetrieb", "Tagebau, Grube, Steinbruch")

# name: (K, districts, predicate(nb, np, use, big)). The order is the priority: a cell belongs to the first stratum whose predicate
# holds (classify), so the strata are disjoint. (The first version of this table let a cell count for two strata, "centre" and "tiny",
# and handled it with a global used-set; that made `--only` and `--first` change the draw.)
STRATA = {
    "tiny":       (4, [KOELN, DUESSELDORF, DUISBURG, DORTMUND], lambda nb, np_, use, big: np_ >= 120),
    "huge":       (4, [EUSKIRCHEN, SIEGEN, HOEXTER, BORKEN], lambda nb, np_, use, big: big >= 1_000_000),
    "mining":     (4, [ERFT, DUEREN], lambda nb, np_, use, big: use in MINING_USE),
    "river":      (4, [DUISBURG, KOELN, DUESSELDORF, DORTMUND], lambda nb, np_, use, big: use == "Fließgewässer"),
    "industrial": (4, [DUISBURG, DORTMUND, ERFT], lambda nb, np_, use, big: use == "Industrie- und Gewerbefläche" and nb >= 3),
    "forest":     (4, [EUSKIRCHEN, SIEGEN, HOEXTER, DUEREN], lambda nb, np_, use, big: use == "Wald"),
    "farmland":   (4, [BORKEN, HOEXTER, DUEREN, EUSKIRCHEN], lambda nb, np_, use, big: nb <= 2 and use == "Landwirtschaft"),
    "village":    (4, [EUSKIRCHEN, HOEXTER, BORKEN, SIEGEN], lambda nb, np_, use, big: 3 <= nb < 25 and use in BUILDING_USE),
    "suburb":     (4, [BIELEFELD, DORTMUND, KOELN, DUESSELDORF], lambda nb, np_, use, big: 25 <= nb < 80 and use == "Wohnbaufläche"),
    "centre":     (5, [KOELN, DUESSELDORF, DORTMUND], lambda nb, np_, use, big: nb >= 80),
}


def cell_of(x, y):
    return (int(x // CELL) * CELL, int(y // CELL) * CELL)


def order_key(district, cell):
    return hashlib.sha256(f"{SEED}|{district}|{cell[0]}_{cell[1]}".encode()).hexdigest()


def point_in_polygon(x, y, rings):
    """Even-odd over all rings of one polygon (holes included)."""
    inside = False
    for ring in rings:
        j = len(ring) - 1
        for i in range(len(ring)):
            xi, yi = ring[i]
            xj, yj = ring[j]
            if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                inside = not inside
            j = i
    return inside


class Stats:
    """Per-district cell statistics, read once."""

    def __init__(self, gpkg):
        import sqlite3
        db = sqlite3.connect("file:" + gpkg + "?mode=ro", uri=True)
        self.nb, self.np = {}, {}
        self.parcels = []      # (envelope, area of the box)
        for layer, counts in (("GebauedeBauwerk", self.nb), ("Flurstueck", self.np)):
            for (blob,) in db.execute(f'select geometrie from "{layer}"'):
                env = at.gpkg_envelope(blob) if blob is not None else None
                if env is None:
                    continue
                c = cell_of((env[0] + env[1]) / 2, (env[2] + env[3]) / 2)
                counts[c] = counts.get(c, 0) + 1
                if layer == "Flurstueck":
                    self.parcels.append((env, (env[1] - env[0]) * (env[3] - env[2])))
        # parcels with a big box, for `big` (a cell's value is the largest box that reaches it)
        self.large = sorted((p for p in self.parcels if p[1] >= 1_000_000), key=lambda p: -p[1])
        self.landuse = [(at.gpkg_envelope(b), b, u) for b, u in db.execute('select geometrie, nutzart from "Nutzung"') if b is not None]
        self.landuse = [(e, b, u) for e, b, u in self.landuse if e is not None]
        db.close()
        self.cells = sorted(set(self.nb) | set(self.np))

    def use(self, cell):
        x, y = cell[0] + CELL / 2, cell[1] + CELL / 2
        for (ax, bx, ay, by), blob, nutzart in self.landuse:
            if ax <= x <= bx and ay <= y <= by and point_in_polygon(x, y, at.gpkg_rings(blob)):
                return nutzart
        return None

    def big(self, cell):
        x0, y0 = cell
        best = 0.0
        for (ax, bx, ay, by), area in self.large:
            if ax <= x0 + CELL and bx >= x0 and ay <= y0 + CELL and by >= y0:
                best = area
                break
        return best


def plan(args):
    idx = at.index(args.cache)
    need = {}
    for name, (k, districts, _) in STRATA.items():
        for i in range(k):
            need.setdefault(districts[i % len(districts)], []).append(name)
    total = 0
    print(f"seed {SEED}, cell {CELL} m")
    for name, (k, districts, _) in STRATA.items():
        print(f"  {name}: {k} tiles from " + ", ".join(idx[d][2] for d in districts))
    for d, names in sorted(need.items()):
        size = idx[d][1]
        total += size
        print(f"  download {idx[d][2]} ({d}): {size / 1e6:.0f} MB, for {len(names)} tiles")
    print(f"  {len(need)} districts, {total / 1e6:.0f} MB of archives, {sum(len(v) for v in need.values())} tiles")


def classify(nb, np_, use, big):
    """The stratum of a cell: the first of STRATA (in table order) whose predicate holds, or None."""
    for name, (_, _, predicate) in STRATA.items():
        if predicate(nb, np_, use, big):
            return name
    return None


def select_tiles(stats_of, only=None, first=0):
    """The tiles of the sample: [(stratum, district, cell, nb, np, use, big)]. stats_of(district) gives the Stats of a district
    (loaded on demand). `only` (strata) and `first` (tiles per stratum) filter the draw, they never change it."""
    picks = []
    for name, (k, districts, _) in STRATA.items():
        if only and name not in only:
            continue
        used = set()
        for i in range(min(k, first) if first else k):
            d = districts[i % len(districts)]
            st = stats_of(d)
            for cell in sorted(st.cells, key=lambda c: order_key(d, c)):
                if (d, cell) in used:
                    continue
                nb, np_ = st.nb.get(cell, 0), st.np.get(cell, 0)
                use = st.use(cell)
                big = st.big(cell)
                if classify(nb, np_, use, big) == name:
                    used.add((d, cell))
                    picks.append((name, d, cell, nb, np_, use, big))
                    break
            else:
                print(f"  {name}: no qualifying cell left in district {d}", flush=True)
    return picks


def draw(args):
    stats = {}
    archives = {}

    def stats_of(d):
        if d not in stats:
            gpkg, digest, city, nbytes = at.open_district(args.cache, d)
            print(f"district {d} {city}: {nbytes} bytes, archive sha256 {digest}", flush=True)
            archives[d] = {"city": city, "file": at.index(args.cache)[d][0], "bytes": nbytes, "sha256": digest}
            stats[d] = (Stats(gpkg), gpkg)
        return stats[d][0]

    manifest = []
    for name, d, cell, nb, np_, use, big in select_tiles(stats_of, args.only, args.first):
        manifest.append({"stratum": name, "district": d, "city": archives[d]["city"], "tile": f"{cell[0]}_{cell[1]}",
                         "nb": nb, "np": np_, "use": use, "big_m2": big, "archive_sha256": archives[d]["sha256"]})
        print(f"  {name}: {archives[d]['city']} {cell[0]}_{cell[1]}  nb {nb} np {np_} use {use} big {big:.0f}", flush=True)
    by_district = {}
    for m in manifest:
        by_district.setdefault(m["district"], []).append(m)
    for d, rows in by_district.items():
        district = at.District(stats[d][1])
        for m in rows:
            e, n = (int(v) for v in m["tile"].split("_"))
            pts, segs, nfeat = district.tile(e, n, CELL, "intersects")
            out = os.path.join(args.out, m["stratum"])
            os.makedirs(out, exist_ok=True)
            name = f"m2_{m['tile']}s"
            m["points"], m["segments"], m["features"] = len(pts), len(segs), nfeat
            m["files"] = {name: at.write_input(os.path.join(out, name + ".txt"), pts, segs, None, False, args.steiner_limit, "0.01")}
            for prefix, constrained in (("base", False), ("cq", True)):
                for angle in (20, 30):
                    name = f"{prefix}_{m['tile']}_q{angle}s"
                    m["files"][name] = at.write_input(os.path.join(out, name + ".txt"), pts, segs, angle, constrained, args.steiner_limit, "0.01")
    path = os.path.join(args.out, "sample.json")
    os.makedirs(args.out, exist_ok=True)
    # the akt source changes over time: the sample is reproducible per archive hash, so the hashes are part of the output
    json.dump({"seed": SEED, "cell": CELL, "archives": archives, "tiles": manifest}, open(path, "w"), indent=1, ensure_ascii=False)
    print(f"{len(manifest)} tiles from {len(archives)} archives, manifest {path}")
    for d, a in sorted(archives.items()):
        print(f"  archive {d} {a['file']} {a['bytes']} bytes sha256 {a['sha256']}")


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--cache", default=os.path.join(here, "data", "alkis"))
    ap.add_argument("--out", default=None, help="default: <cache>/sample")
    ap.add_argument("--plan", action="store_true")
    ap.add_argument("--draw", action="store_true")
    ap.add_argument("--only", nargs="*", help="strata to draw")
    ap.add_argument("--first", type=int, default=0, help="only the first N tiles of every stratum (a measurement subset of the same draw)")
    ap.add_argument("--steiner-limit", type=int, default=200000)
    args = ap.parse_args()
    os.makedirs(args.cache, exist_ok=True)
    args.out = args.out or os.path.join(args.cache, "sample")
    if args.plan:
        plan(args)
    elif args.draw:
        draw(args)
    else:
        ap.error("give --plan or --draw")


if __name__ == "__main__":
    sys.exit(main())
