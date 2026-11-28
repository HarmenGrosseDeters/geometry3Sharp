"""Prepare and verify the file-backed operands recorded by the endpoint benchmarks."""

import argparse
import hashlib
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SUITE_REV = "0577940f6916f759e7f0dda7a7e690cb89842d4f"
TAIL_REV = "1e52df6"
TAIL = {
    "117959": "718a6323cbff7ec85fed5672409adf264b05ae6f33765a7eb2d1b652207b9351",
    "1368057": "c896eda306d2fe1efe62baa14d6990be1600ac610463526bac0c6daba240a170",
    "519511": "1f8935ae45d14733189623d5a0b73a470a9f53f8f7529d489d659be4da4bbfbc",
    "55928": "16106af6ac08311ac60dd92ca100a7ce2aa3c4f13ebdca16a65ca31bc949a726",
}
ABC = (("00000177", "coarse", 0.02, 0.45), ("00000382", "fine", 0.001, 0.12))
OBJ_UID = "02543290444e41a3babfbb894e7d0909"


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify(root):
    relative_hashes = {}
    records = 0
    for log in sorted((HERE / "results").glob("endpoint*.jsonl")):
        for line in log.read_text().splitlines():
            row = json.loads(line)
            label, files, hashes = (row[key] for key in ("label", "sourceFiles", "hashes"))
            if not hashes and len(files) == 1 and files[0].isdecimal():
                records += 1
                continue
            if len(files) != len(hashes) or not hashes:
                raise ValueError(f"{log.name}: missing operand hashes for {label}")
            if label.startswith("Thingi10K-"):
                parent = Path("operands") / label.removeprefix("Thingi10K-")
            else:
                parent = Path("fixtures" if files[0].startswith("B") else "derived")
            for name, expected in zip(files, hashes, strict=True):
                relative = parent / name
                if relative_hashes.setdefault(relative, expected) != expected:
                    raise ValueError(f"conflicting recorded hashes for {relative}")
            records += 1
    if not relative_hashes:
        raise ValueError("No file-backed endpoint benchmark results found")
    for relative, expected in relative_hashes.items():
        path = root / relative
        if not path.is_file():
            raise ValueError(f"Missing operand {path}")
        actual = sha256(path)
        if actual != expected:
            raise ValueError(f"{path}: expected {expected}, got {actual}")
    print(f"Verified {len(relative_hashes)} file-backed operands against {records} benchmark records.")


def prepare(args):
    suite = args.suite.resolve()
    revision = subprocess.check_output(["git", "-C", str(suite), "rev-parse", "HEAD"], text=True).strip()
    if revision != SUITE_REV:
        raise ValueError(f"Expected targeted benchmark revision {SUITE_REV}, got {revision}")
    sys.path.insert(0, str(suite / "benchmarks/TargetedMeshRegression"))
    from common import load, save
    from corpus_extension import weld
    from fetch_extended import MANIFEST, fetch
    from generate import box, generate
    from real_booleans import read_stl, transform
    import cadquery as cq

    if cq.__version__ != "2.7.0":
        raise ValueError(f"Expected CadQuery 2.7.0, got {cq.__version__}")
    root = args.root.resolve()
    sources = args.source_cache.resolve()
    assets = {entry["path"]: entry for entry in json.loads(MANIFEST.read_text())["assets"]}
    derived = root / "derived"
    derived.mkdir(parents=True, exist_ok=True)
    fixtures = root / "fixtures"
    fixtures.mkdir(exist_ok=True)

    def source(name, entry=None):
        entry = assets[name] if entry is None else entry
        path = sources / name
        if not path.is_file():
            if args.offline or name.startswith("abc/"):
                raise ValueError(f"Missing source {path}. See the endpoint reproduction guide.")
            fetch(sources, entry)
        if sha256(path) != entry["sha256"]:
            raise ValueError(f"Source hash mismatch for {path}")
        return path

    def run(assembly, *command):
        subprocess.run([args.dotnet, str(assembly), *(str(arg) for arg in command)], check=True)

    generate(fixtures)
    for model, resolution, relative, angle in ABC:
        step = source(f"abc/{model}.step")
        solid = cq.importers.importStep(str(step)).solids().val()
        bounds = solid.BoundingBox()
        dx, dy, dz = bounds.xlen, bounds.ylen, bounds.zlen
        lo = (bounds.xmin + 0.431 * dx, bounds.ymin - dy, bounds.zmin - dz)
        hi = (bounds.xmax + dx, bounds.ymax + dy, bounds.zmax + dz)
        diagonal = bounds.DiagonalLength
        points, triangles = solid.tessellate(diagonal * relative, angle)
        save(derived / f"ABC-{model}-{resolution}.obj",
             weld(([tuple(point.toTuple()) for point in points], triangles), diagonal * 1e-10))
        save(derived / f"ABC-{model}-cutter.obj", box(lo, hi))

    cases = json.loads(source("ember/cases.json").read_text())
    for case in (16, 84):
        for side in ("a", "b"):
            row = cases[case]
            mesh = read_stl(source(f"ember/{row[f'id_{side}']}.stl"))
            save(derived / f"EMBER-{case:04d}-{side}.obj", transform(mesh, row[f"transform_{side}"]))

    imported = derived / "objaverse-imported.obj"
    welded = derived / "objaverse-welded.obj"
    run(args.cli, "convert", source(f"objaverse/{OBJ_UID}.glb"), imported, "--compact")
    save(welded, weld(load(imported)))
    run(args.cli, "repair", welded, derived / f"OBJ-{OBJ_UID[:8]}-welded-repair.obj", "--compact")

    for model, expected in TAIL.items():
        name = f"thingi10k/{model}.stl"
        path = source(name, {
            "path": name,
            "url": f"https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/{TAIL_REV}/raw_meshes/{model}.stl",
            "sha256": expected,
        })
        destination = root / "operands" / model
        destination.mkdir(parents=True, exist_ok=True)
        run(args.exporter, "export", path, destination, "--operands-only")

    verify(root)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    setup = commands.add_parser("prepare")
    setup.add_argument("root", type=Path)
    setup.add_argument("--source-cache", type=Path, required=True)
    setup.add_argument("--suite", type=Path, required=True)
    setup.add_argument("--dotnet", default="dotnet")
    setup.add_argument("--cli", type=Path, required=True)
    setup.add_argument("--exporter", type=Path, required=True)
    setup.add_argument("--offline", action="store_true")
    check = commands.add_parser("verify")
    check.add_argument("root", type=Path)
    args = parser.parse_args()
    if args.command == "prepare":
        prepare(args)
    else:
        verify(args.root)
