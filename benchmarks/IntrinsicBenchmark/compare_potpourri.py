"""Compare identical procedural fixtures exported by --affine-export; no mesh assets are committed."""
import argparse
import csv
import importlib.metadata
import json
from pathlib import Path

import numpy as np
import potpourri3d as pp3d


def unit(v):
    return v / np.linalg.norm(v)


def oracle(data):
    vertices = np.asarray(data["vertices"], dtype=float)
    source = data["source"]
    p = vertices[source]
    name = data["name"]
    normal = np.array([0., 0., 1.]) if name == "plane" else unit(p if name == "sphere" else p * [1, 1, 0])
    gauge = np.asarray(data["gauge"])
    e1 = unit(gauge - normal * (gauge @ normal))
    e2 = np.cross(normal, e1)
    valid = np.ones(len(vertices), dtype=bool)
    if name == "sphere":
        positions = vertices / np.linalg.norm(vertices, axis=1)[:, None]
        cosine = np.clip(positions @ normal, -1, 1)
        tangent = positions - cosine[:, None] * normal
        lengths = np.linalg.norm(tangent, axis=1)
        valid = ~((cosine < 0) & (lengths < 1e-14))
        exact = tangent * (np.arccos(cosine) / np.maximum(lengths, 1e-300))[:, None]
        max_radius = np.pi
    elif name == "cylinder":
        angle = (np.arctan2(vertices[:, 1], vertices[:, 0]) - np.arctan2(p[1], p[0]) + np.pi) % (2 * np.pi) - np.pi
        valid = np.abs(np.abs(angle) - np.pi) >= 1e-14
        exact = angle[:, None] * np.array([-p[1], p[0], 0.])
        exact[:, 2] = vertices[:, 2] - p[2]
        max_radius = np.sqrt(np.pi**2 + 4)
    else:
        exact = vertices - p
        max_radius = np.sqrt(8)
    exact[source] = 0
    valid[source] = False  # same aggregate mask as the C# acceptance gate
    radius = np.linalg.norm(exact, axis=1)
    bands = np.minimum(3, (4 * radius / max_radius).astype(int))
    return exact @ np.stack([e1, e2], axis=1), valid, radius, bands, normal, e1, e2


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fixtures", type=Path)
    parser.add_argument("points", type=Path)
    args = parser.parse_args()
    print("potpourri3d=" + importlib.metadata.version("potpourri3d"))
    print("numpy=" + np.__version__ + "; t_coef=1; both input and intrinsic triangulations; no fitted alignment")
    print("mesh,triangulation,strategy,band,count,cs_mean,cs_max,pp_mean,pp_max,delta_mean,delta_max,cs_regressions,pp_regressions")
    sanity_failures = []
    with args.points.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(["mesh", "intrinsic", "strategy", "vertex", "radius", "band", "legacy_error", "cs_error", "pp_error", "map_delta"])
        for path in sorted(args.fixtures.glob("*.json")):
            data = json.loads(path.read_text(encoding="utf-8"))
            vertices = np.asarray(data["vertices"], dtype=float)
            faces = np.asarray(data["faces"], dtype=np.int64)
            solver = pp3d.MeshVectorHeatSolver(vertices, faces, t_coef=1., use_intrinsic_delaunay=data["intrinsic"])
            exact, valid, radius, bands, normal, e1, e2 = oracle(data)
            bx, by, _ = solver.get_tangent_frames()
            px = unit(bx[data["source"]] - normal * (bx[data["source"]] @ normal))
            py = np.cross(normal, px)
            if py @ by[data["source"]] < 0:
                py = -py
            rotation = np.array([[px @ e1, px @ e2], [py @ e1, py @ e2]])
            legacy = np.linalg.norm(np.asarray(data["maps"]["Legacy"], dtype=float) - exact, axis=1)
            for strategy in ["VectorHeat", "AffineLocal", "AffineAdaptive"]:
                cs = np.asarray(data["maps"]["Legacy" if strategy == "VectorHeat" else strategy], dtype=float)
                reference = solver.compute_log_map(data["source"], strategy=strategy) @ rotation
                cs_error = np.linalg.norm(cs - exact, axis=1)
                pp_error = np.linalg.norm(reference - exact, axis=1)
                delta = np.linalg.norm(cs - reference, axis=1)
                if not (np.isfinite(cs_error[valid]).all() and np.isfinite(pp_error[valid]).all()):
                    raise RuntimeError(f"Nonfinite defined samples: {path.name}, {strategy}")
                for i in np.flatnonzero(valid):
                    writer.writerow([data["name"], data["intrinsic"], strategy, i, radius[i], bands[i], legacy[i], cs_error[i], pp_error[i], delta[i]])
                for band in range(4):
                    mask = valid & (bands == band)
                    a, b, d = cs_error[mask], pp_error[mask], delta[mask]
                    row = [data["name"], "intrinsic" if data["intrinsic"] else "input", strategy, band, int(mask.sum()),
                           a.mean(), a.max(), b.mean(), b.max(), d.mean(), d.max(),
                           int((a > legacy[mask] + 1e-10).sum()), int((b > legacy[mask] + 1e-10).sum())]
                    print(",".join(map(str, row)))
                if data["name"] == "plane" and strategy != "VectorHeat":
                    if cs_error[valid].max() >= 1e-9 or pp_error[valid].max() >= 1e-9:
                        sanity_failures.append(f"{path.name}:{strategy}")
    if sanity_failures:
        raise RuntimeError("Planar sanity failed (all comparisons retained): " + ", ".join(sanity_failures))


if __name__ == "__main__":
    main()
