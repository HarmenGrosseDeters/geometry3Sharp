#!/usr/bin/env python3
"""Which Stryker configs a pull request needs (.github/workflows/mutation.yml).

Reads the changed paths from stdin and writes GITHUB_OUTPUT lines to stdout:
  arithmetic=none|full|source|tests
  mesh=[{"shard": "rounding", "mode": "full"}, ...]

Modes (tests/mutation/README.md):
  full   the config or the tools changed, or only some of the mutated files of a shard with several: the whole shard,
         with its break threshold
  source a mutated file changed (all of them, in a shard with several): only the changed mutants run, the others keep
         their results from the baseline of the last full run (--with-baseline), with the break threshold; without a
         baseline the workflow runs the shard in full
  tests  only tests that run against the shard changed: mutants those tests cover, report only

The files of a shard come from its config (mutate, test-case-filter). The paths of the workflow are a second list, but
only a coarse one: test_select_shards.py checks that it covers every config.
With --all every config runs in full (schedule, workflow_dispatch); --all --only arithmetic,rounding runs only those
(workflow_dispatch input "shards"; "arithmetic" is stryker-config.json, the others are the mesh shards).
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
    if changed & (ALWAYS | {config_path}):
        return "full"
    if changed & sources:
        # --since ignores the mutants of an unchanged file of the shard, so its score would cover only a part of the
        # shard: a shard with several files runs incrementally only when all of them changed
        return "source" if sources <= changed else "full"
    return "tests" if changed & tests else "none"


def shard_name(config_name):
    """config name -> the name used in --only and in the cache keys: config -> arithmetic, mesh-x -> x"""
    return "arithmetic" if config_name == "config" else config_name[len("mesh-"):]


def select(changed, configs, run_all=False, only=None):
    """(mode of the arithmetic config, list of the mesh shards to run)"""
    changed = set(changed)
    if only is not None:
        unknown = set(only) - {shard_name(n) for n in configs}
        if unknown:
            raise ValueError("unknown shards: " + ", ".join(sorted(unknown)))
    modes = {name: ("full" if only is None or shard_name(name) in only else "none") if run_all
             else mode_of(config, changed) for name, config in configs.items()}
    mesh = [{"shard": name[len("mesh-"):], "mode": mode} for name, mode in modes.items()
            if name.startswith("mesh-") and mode != "none"]
    return modes["config"], mesh


if __name__ == "__main__":
    args = sys.argv[1:]
    run_all = "--all" in args
    only = None
    if "--only" in args:
        value = args[args.index("--only") + 1].strip()
        only = None if value in ("", "all") else {x.strip() for x in value.split(",") if x.strip()}
    paths = [] if run_all else [line.strip() for line in sys.stdin if line.strip()]
    arithmetic, mesh = select(paths, load_configs(), run_all, only)
    print("arithmetic=" + arithmetic)
    print("mesh=" + json.dumps(mesh))
