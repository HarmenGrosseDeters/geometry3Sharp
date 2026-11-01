#!/usr/bin/env python3
"""Which Stryker configs a pull request needs (.github/workflows/mutation.yml).

Reads the changed paths from stdin and writes GITHUB_OUTPUT lines to stdout:
  arithmetic=none|full|source|tests
  mesh=[{"shard": "rounding", "mode": "full"}, ...]

Modes (tests/mutation/README.md):
  full   the config or the tools changed: the whole shard, with its break threshold
  source a mutated file changed: only the mutants of the changed lines run, the others keep their results from the
         baseline of the last full run (incremental.py), with the break threshold on the combined score; without a
         baseline the workflow runs the shard in full
  tests  only tests that run against the shard changed: mutants those tests cover, report only

The files of a shard come from its config (mutate, test-case-filter). The paths of the workflow are a second list, but
only a coarse one: test_select_shards.py checks that it covers every config.
An edit of the workflow runs every config in full, unless it only adds or removes trigger paths or comments (checked
on the diff between $BASE and $HEAD, which the workflow sets): adding a test file to the paths changes no shard.
With --all every config runs in full (schedule, workflow_dispatch); --all --only arithmetic,rounding runs only those
(workflow_dispatch input "shards"; "arithmetic" is stryker-config.json, the others are the mesh shards).
"""
import glob
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TESTS = "tests/geometry3Sharp.Tests/"
WORKFLOW = ".github/workflows/mutation.yml"
ALWAYS = {".config/dotnet-tools.json", WORKFLOW}

# a changed line of the workflow that only adds or removes a trigger path (`      - 'mesh_ops/X.cs'`), a comment or a
# blank line: such an edit changes which PRs start the workflow, not how a shard runs
_TRIGGER_ONLY_LINE = re.compile(r"^\s*(-\s*'[^']*'\s*(#.*)?|#.*)?$")


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


def workflow_edit_is_trigger_only(diff):
    """True if a unified diff of the workflow only adds or removes quoted list items (the `paths` of the trigger),
    comments or blank lines. Then the workflow edit alone runs no shard in full: a full run of all 8 shards takes hours
    of runners, and adding a test file to the trigger paths (AGENTS.md) changes no shard's mutants or tests."""
    changed = [line[1:] for line in diff.splitlines()
               if line[:1] in "+-" and not line.startswith("+++") and not line.startswith("---")]
    return bool(changed) and all(_TRIGGER_ONLY_LINE.match(line) for line in changed)


def mode_of(config, changed):
    config_path, sources, tests = config
    if changed & (ALWAYS | {config_path}):
        return "full"
    if changed & sources:
        # (an unchanged file of a shard with several keeps all its baseline results: incremental.py)
        return "source"
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
    base, head = os.environ.get("BASE"), os.environ.get("HEAD")
    if WORKFLOW in paths and base and head:
        # (a failed git diff raises: an empty diff must not count as trigger-only)
        diff = subprocess.run(["git", "diff", f"{base}...{head}", "--", WORKFLOW],
                              check=True, capture_output=True, text=True).stdout
        if workflow_edit_is_trigger_only(diff):
            paths.remove(WORKFLOW)
            print("workflow: only trigger paths or comments changed, not a reason for full runs", file=sys.stderr)
    arithmetic, mesh = select(paths, load_configs(), run_all, only)
    print("arithmetic=" + arithmetic)
    print("mesh=" + json.dumps(mesh))
