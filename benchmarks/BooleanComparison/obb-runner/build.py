#!/usr/bin/env -S uv run --script
# /// script
# requires-python = ">=3.11"
# dependencies = [
#   "pyyaml>=6.0",
# ]
# ///
# Builds the ExactMeshBoolean runner: clones geometry3Sharp at the ref of runner.yaml, publishes
# benchmarks/BooleanComparison (whose "obb" mode implements the runner protocol) and writes bin/build-info.json.
import argparse, datetime, json, os, shutil, stat, subprocess
import yaml

here = os.path.dirname(os.path.abspath(__file__))

def run(cmd, **kw):
    print("+", " ".join(cmd), flush=True)
    return subprocess.run(cmd, check=True, **kw)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="release", choices=["release", "debug"])
    ap.add_argument("--clean", action="store_true")
    ap.add_argument("--force-download", action="store_true")
    args = ap.parse_args()
    bin_dir, src = os.path.join(here, "bin"), os.path.join(here, "download", "geometry3sharp")
    if args.clean:
        shutil.rmtree(bin_dir, ignore_errors=True)
        return
    manifest = yaml.safe_load(open(os.path.join(here, "runner.yaml")))
    up = manifest["upstream"]
    if args.force_download:
        shutil.rmtree(src, ignore_errors=True)
    if not os.path.isdir(src):
        run(["git", "clone", "-q", up["url"], src])
    run(["git", "-C", src, "fetch", "-q", "origin", up["ref"]])
    run(["git", "-C", src, "checkout", "-q", "FETCH_HEAD"])
    commit = subprocess.run(["git", "-C", src, "rev-parse", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    config = "Release" if args.mode == "release" else "Debug"
    app = os.path.join(bin_dir, "app")
    shutil.rmtree(app, ignore_errors=True)
    run(["dotnet", "publish", os.path.join(src, "benchmarks", "BooleanComparison"), "-c", config, "-o", app])
    entry = os.path.join(bin_dir, "exactmeshboolean_runner")
    with open(entry, "w") as f:
        f.write('#!/bin/sh\nexec dotnet "$(dirname "$0")/app/BooleanComparison.dll" obb "$@"\n')
    os.chmod(entry, os.stat(entry).st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    sdk = subprocess.run(["dotnet", "--version"], capture_output=True, text=True).stdout.strip()
    info = {
        "runner_id": manifest["runner"]["runner_id"], "build_mode": args.mode,
        "timestamp_utc": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "upstream": {"source": "git", "url": up["url"], "requested_ref": up["ref"], "resolved_commit": commit},
        "patches": [],
        "toolchain": {"dotnet_sdk": sdk, "build_type": config},
        "artifacts": ["bin/exactmeshboolean_runner", "bin/app/BooleanComparison.dll"],
    }
    with open(os.path.join(bin_dir, "build-info.json"), "w") as f:
        json.dump(info, f, indent=2)

if __name__ == "__main__":
    main()
