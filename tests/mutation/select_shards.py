#!/usr/bin/env python3
"""Which Stryker configs a pull request needs (.github/workflows/mutation.yml).

Reads the changed paths from stdin and writes GITHUB_OUTPUT lines to stdout:
  arithmetic=none|full|tests
  mesh=[{"shard": "rounding", "mode": "full"}, ...]

Modes (tests/mutation/README.md):
  full   a mutated file, the config or the tools changed: the whole shard, with its break threshold
  tests  only tests that run against the shard changed: mutants those tests cover, report only

The files of a shard come from its config (mutate, test-case-filter). The paths of the workflow are a second list, but
only a coarse one: test_select_shards.py checks that it covers every config.
With --all every config runs in full (schedule, workflow_dispatch).
"""
import glob
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TESTS = "tests/geometry3Sharp.Tests/"
ALWAYS = {".config/dotnet-tools.json", ".github/workflows/mutation.yml"}


def load_configs(directory=HERE):
    """name of the config -> (config path, mutated paths, test paths)"""
    configs = {}
    for path in sorted(glob.glob(os.path.join(directory, "stryker-*.json"))):
        name = os.path.basename(path)[len("stryker-"):-len(".json")]
        with open(path, encoding="utf-8") as f:
            config = json.load(f)["stryker-config"]
        # "**/mesh_ops/ExactMeshBoolean.cs{0..47051}" -> mesh_ops/ExactMeshBoolean.cs
        sources = {re.sub(r"\{.*\}$", "", m).replace("**/", "") for m in config["mutate"]}
        # "FullyQualifiedName~MeshRoundingRepairTests.ExactVertices" -> the file of class MeshRoundingRepairTests
        classes = {t.split("~")[1].split(".")[0] for t in config["test-case-filter"].split("|")}
        configs[name] = ("tests/mutation/" + os.path.basename(path), sources, {TESTS + c + ".cs" for c in classes})
    return configs


def mode_of(config, changed):
    config_path, sources, tests = config
    if changed & (ALWAYS | sources | {config_path}):
        return "full"
    return "tests" if changed & tests else "none"


def select(changed, configs, run_all=False):
    """(mode of the arithmetic config, list of the mesh shards to run)"""
    changed = set(changed)
    modes = {name: "full" if run_all else mode_of(config, changed) for name, config in configs.items()}
    mesh = [{"shard": name[len("mesh-"):], "mode": mode} for name, mode in modes.items()
            if name.startswith("mesh-") and mode != "none"]
    return modes["config"], mesh


if __name__ == "__main__":
    run_all = "--all" in sys.argv[1:]
    paths = [] if run_all else [line.strip() for line in sys.stdin if line.strip()]
    arithmetic, mesh = select(paths, load_configs(), run_all)
    print("arithmetic=" + arithmetic)
    print("mesh=" + json.dumps(mesh))
