# Downloads a fixed selection of open geodata of the Hambach open-pit lignite mine (NRW, Germany) and
# converts it to the simple binary formats the benchmark reads.
#
# Source: Geobasis NRW, https://www.opengeodata.nrw.de/produkte/geobasis/hm/
# License: Datenlizenz Deutschland - Zero - Version 2.0 (dl-de/zero-2-0, https://www.govdata.de/dl-de/zero-2-0)
#   - dgm1_tiff:  digital terrain model, 1 m grid, GeoTIFF, airborne laser scan of 2022
#   - bdom50_las: image-based digital surface model, 0.5 m grid, LAZ, aerial images of 2025
#                 (bare mine floor and faces: surface = terrain; vegetation and buildings outside the pit)
#   - 3dm_l_las:  classified airborne laser scan point clouds, LAZ, 2022 (class 2 = ground)
# All data in ETRS89 / UTM zone 32N (EPSG:25832), heights DHHN2016.
#
# Needs: numpy, tifffile, imagecodecs, laspy, lazrs   (pip install numpy tifffile imagecodecs laspy lazrs)
#
# Output in data/:
#   <name>.f32 + <name>.json   height grid: float32 little endian, row-major, row 0 = south, NaN = no data;
#                              json: x0, y0 (node 0,0), dx, nx, ny, source
#   <name>.xyz.f64 + .cls.u8   point cloud: float64 x,y,z per point; uint8 LAS class per point
#   files.sha256               checksums of the downloads
import hashlib, json, os, sys, urllib.request
import numpy as np

BASE = "https://www.opengeodata.nrw.de/produkte/geobasis/hm"
# 3 x 4 km of the Hambach mine: cut faces of the advancing pit (south) and the internal dump (north)
EAST = range(326, 329)      # km, UTM easting of the tile's west edge
NORTH = range(5640, 5644)   # km, UTM northing of the tile's south edge
# laser scan tiles for the ground filter: pit edge with the remains of the Hambach forest, and the pit
# interior (benches, conveyors, excavators)
POINT_TILES = [(322, 5642), (327, 5641)]

os.chdir(os.path.dirname(os.path.abspath(__file__)))
os.makedirs("data/raw", exist_ok=True)


used = []


def fetch(url):
    path = "data/raw/" + url.rsplit("/", 1)[1]
    used.append(path)
    if not os.path.exists(path):
        print("download", url, flush=True)
        urllib.request.urlretrieve(url, path + ".part")
        os.replace(path + ".part", path)
    return path


def dgm_tile(e, n):
    return fetch(f"{BASE}/dgm1_tiff/dgm1_tiff/dgm1_32_{e}_{n}_1_nw_2022.tif")


def bdom_tile(e, n):
    return fetch(f"{BASE}/bdom50_las/bdom50_las/bdom50_32{e}_{n}_1_nw_2025.laz")


def las_tile(e, n):
    return fetch(f"{BASE}/3dm_l_las/3dm_l_las/3dm_32_{e}_{n}_1_nw.laz")


def write_grid(name, z, x0, y0, dx, source):
    z.astype("<f4").tofile(f"data/{name}.f32")
    ny, nx = z.shape
    json.dump({"x0": x0, "y0": y0, "dx": dx, "nx": nx, "ny": ny, "source": source},
              open(f"data/{name}.json", "w"), indent=1)
    print(f"data/{name}: {nx} x {ny}, {np.isnan(z).sum()} no-data nodes", flush=True)


def main():
    import tifffile, laspy
    ne, nn = len(EAST), len(NORTH)
    # DGM1: PixelIsArea, tie point = north-west corner of the tile, nodes at cell centres
    dgm = np.full((nn * 1000, ne * 1000), np.nan, dtype=np.float32)
    for a, e in enumerate(EAST):
        for b, n in enumerate(NORTH):
            t = tifffile.TiffFile(dgm_tile(e, n))
            tie = t.pages[0].tags["ModelTiepointTag"].value
            assert tie[3] == e * 1000 and tie[4] == n * 1000 + 1000, tie
            z = t.pages[0].asarray().astype(np.float32)
            z[z <= -9999] = np.nan
            dgm[b * 1000:(b + 1) * 1000, a * 1000:(a + 1) * 1000] = z[::-1]
    write_grid("dgm2022", dgm, EAST[0] * 1000 + 0.5, NORTH[0] * 1000 + 0.5, 1.0, "Geobasis NRW DGM1 2022, dl-de/zero-2-0")

    # bDOM50: a regular 0.5 m grid stored as LAS points at x = tile + 0.25 + 0.5 i
    bdom = np.full((nn * 2000, ne * 2000), np.nan, dtype=np.float32)
    for a, e in enumerate(EAST):
        for b, n in enumerate(NORTH):
            las = laspy.read(bdom_tile(e, n))
            i = np.rint((np.asarray(las.x) - e * 1000 - 0.25) / 0.5).astype(np.int64)
            j = np.rint((np.asarray(las.y) - n * 1000 - 0.25) / 0.5).astype(np.int64)
            ok = (i >= 0) & (i < 2000) & (j >= 0) & (j < 2000)
            bdom[b * 2000 + j[ok], a * 2000 + i[ok]] = np.asarray(las.z)[ok]
    write_grid("bdom2025", bdom, EAST[0] * 1000 + 0.25, NORTH[0] * 1000 + 0.25, 0.5, "Geobasis NRW bDOM50 2025, dl-de/zero-2-0")
    # resampled to the DGM nodes: mean of the 4 surrounding 0.5 m nodes (= bilinear interpolation there)
    b4 = bdom.reshape(nn * 1000, 2, ne * 1000, 2).astype(np.float64)
    bdom1 = b4.mean(axis=(1, 3)).astype(np.float32)
    write_grid("bdom2025_1m", bdom1, EAST[0] * 1000 + 0.5, NORTH[0] * 1000 + 0.5, 1.0,
               "Geobasis NRW bDOM50 2025 resampled to the DGM1 nodes, dl-de/zero-2-0")

    for e, n in POINT_TILES:
        las = laspy.read(las_tile(e, n))
        xyz = np.stack([np.asarray(las.x), np.asarray(las.y), np.asarray(las.z)], axis=1).astype("<f8")
        xyz.tofile(f"data/3dm_{e}_{n}.xyz.f64")
        np.asarray(las.classification).astype(np.uint8).tofile(f"data/3dm_{e}_{n}.cls.u8")
        # the DGM1 of the same tile, as reference terrain for the ground filter
        t = tifffile.TiffFile(dgm_tile(e, n))
        z = t.pages[0].asarray().astype(np.float32)
        z[z <= -9999] = np.nan
        write_grid(f"dgm2022_{e}_{n}", z[::-1].copy(), e * 1000 + 0.5, n * 1000 + 0.5, 1.0, "Geobasis NRW DGM1 2022, dl-de/zero-2-0")
        print(f"data/3dm_{e}_{n}: {len(xyz)} points", flush=True)

    with open("data/files.sha256", "w") as f:
        for path in sorted(set(used)):
            h = hashlib.sha256(open(path, "rb").read()).hexdigest()
            f.write(f"{h}  {os.path.basename(path)}\n")


if __name__ == "__main__":
    main()
