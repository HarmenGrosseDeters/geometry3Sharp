#!/usr/bin/env python3
"""Fetches ALKIS cadastral data from Geobasis NRW at run time and prepares 2D mesher inputs (the plain text input format).

Source (original, only host used): https://www.opengeodata.nrw.de/produkte/geobasis/lk/akt/gru_vereinfacht_gpkg/
"Grundrissdaten vereinfacht" of the Liegenschaftskataster NRW, one GeoPackage per district (Kreis / kreisfreie Stadt), ETRS89 / UTM 32N
(EPSG:25832). Licence: Datenlizenz Deutschland - Zero - Version 2.0 (dl-de/zero-2-0). Nothing fetched or written here is committed:
the cache and the outputs go to directories you give (default corpus/data/alkis, git-ignored).

Usage (Herne, the three tiles of herne_ratchet.py):

    python3 benchmarks/Meshing2Benchmark/corpus/alkis_tiles.py --herne [--cache <dir>] [--out <dir>]
    python3 benchmarks/Meshing2Benchmark/herne_ratchet.py --inputs <out dir>

Any tile: --district 05916000 --tile 371800_5710600 [--tile ...]   (district = 8-digit key from the index, see --list-districts).

Preparation (what the tile files contain):
  - A tile is a square of --tile-size metres (default 200) whose south-west corner is the tile name (easting_northing, UTM 32N).
  - Features: the boundaries of all polygons of the layers Flurstueck (parcels) and GebauedeBauwerk (buildings) of the district whose
    bounding box intersects the tile; the whole boundary rings, not clipped to the tile (--select clip clips them to the tile instead).
  - Every ring is a polyline of segments between consecutive points. A point (identical coordinates) is stored once and a segment (same
    two points, any direction) once. No rings, no holes, no regions: the domain is the convex hull of the input (D13).
  - Coordinates are written as they are in the source; the mesher rounds them (snap_tolerance 0.01 m).
  - Files: m2_<tile>s.txt (milestone M2: constrained Delaunay of the snapped segments, no refinement), base_<tile>_q<angle>s.txt (default mode, conforming 1) and cq_<tile>_q<angle>s.txt (constrained quality mode),
    for the angles 20 and 30; steiner_limit 200000; resolve_crossings 1.
The printed SHA-256 sums identify the downloaded archive and every written file.
"""
import argparse
import hashlib
import json
import os
import re
import sqlite3
import struct
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

BASE = "https://www.opengeodata.nrw.de/produkte/geobasis/lk/akt/gru_vereinfacht_gpkg"
LAYERS = ("Flurstueck", "GebauedeBauwerk")
HERNE = ("05916000", ["371800_5710600", "373600_5709000", "381000_5710400"])


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def get(url, path, attempts=5, pause=10, sleep=time.sleep):
    """Downloads url to path through a .part file; an existing file is kept (the cache). A transient network error (a reset
    connection, a timeout, a 5xx answer) is retried from the start after a growing pause; the last error is raised."""
    if os.path.exists(path):
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for attempt in range(1, attempts + 1):
        print("download", url, file=sys.stderr, flush=True)
        try:
            urllib.request.urlretrieve(url, path + ".part")
            os.replace(path + ".part", path)
            return
        except (urllib.error.URLError, OSError) as e:
            if os.path.exists(path + ".part"):
                os.remove(path + ".part")
            if isinstance(e, urllib.error.HTTPError) and e.code < 500:
                raise                           # a 4xx answer is not transient
            if attempt == attempts:
                raise
            print(f"  attempt {attempt} of {attempts} failed ({e}); retrying in {pause * attempt} s", file=sys.stderr, flush=True)
            sleep(pause * attempt)


def index(cache):
    """{district key: (file name, size)} from the host's own index.json."""
    path = os.path.join(cache, "index.json")
    get(BASE + "/index.json", path)
    files = json.load(open(path, encoding="utf-8"))["datasets"][0]["files"]
    out = {}
    for f in files:
        m = re.match(r"gru_vereinf_(\d{8})_(.+)_EPSG25832_GeoPackage\.zip$", f["name"])
        if m:
            out[m.group(1)] = (f["name"], int(f["size"]), m.group(2))
    return out


def open_district(cache, district):
    """Downloads and unpacks the district's GeoPackage (once); returns (path of the .gpkg, sha256 of the archive)."""
    name, size, city = index(cache)[district]
    folder = os.path.join(cache, district)
    archive = os.path.join(folder, name)
    get(BASE + "/" + urllib.parse.quote(name), archive)
    digest = sha256_file(archive)
    if os.path.getsize(archive) != size:
        sys.exit(f"{name}: {os.path.getsize(archive)} bytes, the index says {size}")
    gpkg = os.path.join(folder, "unpacked")
    if not os.path.isdir(gpkg):
        with zipfile.ZipFile(archive) as z:
            for info in z.infolist():
                # no path traversal out of the cache
                if os.path.basename(info.filename) != info.filename or not info.filename.endswith(".gpkg"):
                    sys.exit(f"unexpected entry in {name}: {info.filename}")
            z.extractall(gpkg + ".part")
        os.replace(gpkg + ".part", gpkg)
    paths = [os.path.join(gpkg, f) for f in os.listdir(gpkg) if f.endswith(".gpkg")]
    if len(paths) != 1:
        sys.exit(f"{name}: expected one .gpkg, found {paths}")
    return paths[0], digest, city, size


# ---- GeoPackage geometry (header + WKB), polygons only ----

def gpkg_envelope(blob):
    """(minx, maxx, miny, maxy) from the GeoPackage header; None for an empty geometry without envelope."""
    flags = blob[3]
    kind = (flags >> 1) & 7
    if kind == 0:
        return None
    order = "<" if flags & 1 else ">"
    return struct.unpack(order + "4d", blob[8:40])


def gpkg_rings(blob):
    """The rings (lists of (x, y)) of a Polygon / MultiPolygon in a GeoPackage blob. Anything else is an error, not skipped."""
    flags = blob[3]
    if flags & 0x20:
        raise ValueError("extended GeoPackage geometry")
    if flags & 0x10:
        return []
    kind = (flags >> 1) & 7
    offset = 8 + {0: 0, 1: 32, 2: 48, 3: 48, 4: 64}[kind]
    rings = []

    def read(pos):
        order = "<" if blob[pos] == 1 else ">"
        (t,) = struct.unpack_from(order + "I", blob, pos + 1)
        pos += 5
        dim = 2
        if t >= 1000:
            iso, t = divmod(t, 1000)
            dim = {1: 3, 2: 3, 3: 4}[iso]
        elif t & 0x80000000 or t & 0x40000000:
            dim = 3 + (1 if t & 0x40000000 and t & 0x80000000 else 0)
            t &= 0x0FFFFFFF
        if t == 3:
            (nr,) = struct.unpack_from(order + "I", blob, pos)
            pos += 4
            for _ in range(nr):
                (n,) = struct.unpack_from(order + "I", blob, pos)
                pos += 4
                vals = struct.unpack_from(order + f"{n * dim}d", blob, pos)
                pos += 8 * n * dim
                rings.append([(vals[i * dim], vals[i * dim + 1]) for i in range(n)])
            return pos
        if t == 6:
            (np_,) = struct.unpack_from(order + "I", blob, pos)
            pos += 4
            for _ in range(np_):
                pos = read(pos)
            return pos
        raise ValueError(f"geometry type {t} is not a polygon")

    read(offset)
    return rings


class District:
    """All polygons of the two layers: (envelope, rings), read once."""

    def __init__(self, gpkg):
        db = sqlite3.connect("file:" + gpkg + "?mode=ro", uri=True)
        self.features = []
        self.counts = {}
        for layer in LAYERS:
            n = 0
            for (blob,) in db.execute(f'select geometrie from "{layer}"'):
                if blob is None:
                    continue
                env = gpkg_envelope(blob)
                if env is None:
                    continue
                self.features.append((env, blob))
                n += 1
            self.counts[layer] = n
        db.close()
        xs = [f[0][0] for f in self.features] + [f[0][1] for f in self.features]
        ys = [f[0][2] for f in self.features] + [f[0][3] for f in self.features]
        self.bounds = (min(xs), max(xs), min(ys), max(ys)) if xs else None

    def tile(self, e, n, size, select):
        """Points and segments of the tile (see the module text)."""
        x0, x1, y0, y1 = e, e + size, n, n + size
        ids, pts, segs = {}, [], set()
        nfeat = 0

        def point(p):
            i = ids.get(p)
            if i is None:
                i = ids[p] = len(pts)
                pts.append(p)
            return i

        for (ax, bx, ay, by), blob in self.features:
            if bx < x0 or ax > x1 or by < y0 or ay > y1:
                continue
            nfeat += 1
            for ring in gpkg_rings(blob):
                for p, q in zip(ring, ring[1:]):
                    if select == "clip":
                        c = clip(p, q, x0, y0, x1, y1)
                        if c is None:
                            continue
                        p, q = c
                    if p == q:
                        continue
                    a, b = point(p), point(q)
                    segs.add((a, b) if a < b else (b, a))
        return pts, sorted(segs), nfeat


def clip(p, q, x0, y0, x1, y1):
    """Liang-Barsky: the part of the segment inside the closed rectangle, or None."""
    dx, dy = q[0] - p[0], q[1] - p[1]
    t0, t1 = 0.0, 1.0
    for pp, qq in ((-dx, p[0] - x0), (dx, x1 - p[0]), (-dy, p[1] - y0), (dy, y1 - p[1])):
        if pp == 0:
            if qq < 0:
                return None
        else:
            r = qq / pp
            if pp < 0:
                if r > t1:
                    return None
                t0 = max(t0, r)
            else:
                if r < t0:
                    return None
                t1 = min(t1, r)
    return (p[0] + t0 * dx, p[1] + t0 * dy), (p[0] + t1 * dx, p[1] + t1 * dy)


def write_input(path, pts, segs, angle, constrained, steiner_limit, snap):
    """angle None: the M2 input (constrained Delaunay of the snapped segments, no refinement)."""
    lines = ["mesh2-input 1", f"points {len(pts)}"]
    lines += [f"{x!r} {y!r}" for x, y in pts]
    lines.append(f"segments {len(segs)}")
    lines += [f"{a} {b} 0 0" for a, b in segs]
    m2 = angle is None
    lines += ["rings 0", "holeseeds 0", "regions 0", "min_angle_deg " + ("-" if m2 else str(angle)), "max_area -",
              "steiner_limit " + ("-" if m2 else str(steiner_limit)), f"snap_tolerance {snap}", "size_constant -", "resolve_crossings 1",
              "conforming 1" if not (m2 or constrained) else "conforming 0"]
    if constrained:
        lines.append("mode constrained")
    lines.append("end")
    text = "\n".join(lines) + "\n"
    with open(path, "w", newline="\n") as f:
        f.write(text)
    return hashlib.sha256(text.encode()).hexdigest()


def prepare(cache, out, district, tiles, size, select, angles, steiner_limit, snap):
    gpkg, digest, city, nbytes = open_district(cache, district)
    print(f"district {district} {city}: {nbytes} bytes, archive sha256 {digest}")
    d = District(gpkg)
    print(f"  {d.counts}, bounds {d.bounds}")
    os.makedirs(out, exist_ok=True)
    rows = []
    for tile in tiles:
        e, n = (int(v) for v in tile.split("_"))
        pts, segs, nfeat = d.tile(e, n, size, select)
        name = f"m2_{tile}s"
        sha = write_input(os.path.join(out, name + ".txt"), pts, segs, None, False, steiner_limit, snap)
        print(f"  {name}.txt  {len(pts)} points, {len(segs)} segments, {nfeat} features  sha256 {sha}")
        rows.append((name, len(pts), len(segs), nfeat, sha))
        for prefix, constrained in (("base", False), ("cq", True)):
            for angle in angles:
                name = f"{prefix}_{tile}_q{angle}s"
                sha = write_input(os.path.join(out, name + ".txt"), pts, segs, angle, constrained, steiner_limit, snap)
                print(f"  {name}.txt  {len(pts)} points, {len(segs)} segments, {nfeat} features  sha256 {sha}")
                rows.append((name, len(pts), len(segs), nfeat, sha))
    return rows


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    here = os.path.dirname(os.path.abspath(__file__))
    ap.add_argument("--cache", default=os.path.join(here, "data", "alkis"))
    ap.add_argument("--out", default=None, help="default: <cache>/inputs")
    ap.add_argument("--herne", action="store_true", help="the three Herne tiles of herne_ratchet.py")
    ap.add_argument("--district", help="8-digit district key (see --list-districts)")
    ap.add_argument("--tile", action="append", default=[], help="easting_northing of the south-west corner, UTM 32N")
    ap.add_argument("--tile-size", type=int, default=200)
    ap.add_argument("--select", choices=("intersects", "clip"), default="intersects")
    ap.add_argument("--angles", default="20,30")
    ap.add_argument("--steiner-limit", type=int, default=200000)
    ap.add_argument("--snap", default="0.01")
    ap.add_argument("--list-districts", action="store_true")
    args = ap.parse_args()
    os.makedirs(args.cache, exist_ok=True)
    if args.list_districts:
        for key, (name, size, city) in sorted(index(args.cache).items()):
            print(key, city, size)
        return 0
    district, tiles = (HERNE if args.herne else (args.district, args.tile))
    if not district or not tiles:
        ap.error("give --herne, or --district and at least one --tile")
    prepare(args.cache, args.out or os.path.join(args.cache, "inputs"), district, tiles, args.tile_size, args.select,
            [int(a) for a in args.angles.split(",")], args.steiner_limit, args.snap)
    return 0


if __name__ == "__main__":
    sys.exit(main())
