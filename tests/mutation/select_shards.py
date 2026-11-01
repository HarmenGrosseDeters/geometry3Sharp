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

# an edit of the workflow that only adds or removes trigger paths (`      - 'mesh_ops/X.cs'` between `paths:` and
# `schedule:`), or comments and blank lines of the header before `on:`, changes which PRs start the workflow, not how a
# shard runs
_PATH_ITEM = re.compile(r"^\s*-\s*'[^']*'\s*(#.*)?$")
_COMMENT_OR_BLANK = re.compile(r"^\s*(#.*)?$")
_HUNK = re.compile(r"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")


def _regions(text):
    """(last line of the header before `on:`, (first, last) line of the `paths` list), 1-based"""
    lines = text.splitlines()
    on = next(i for i, l in enumerate(lines, 1) if l.startswith("on:"))
    paths = next(i for i, l in enumerate(lines, 1) if l.strip() == "paths:")
    schedule = next(i for i, l in enumerate(lines, 1) if l.strip() == "schedule:")
    return on - 1, (paths + 1, schedule - 1)


def _allowed(line, number, regions):
    header, (first, last) = regions
    if number <= header:
        return bool(_COMMENT_OR_BLANK.match(line))
    if first <= number <= last:
        return bool(_PATH_ITEM.match(line) or _COMMENT_OR_BLANK.match(line))
    return False


def workflow_edit_is_trigger_only(diff, old_text, new_text):
    """True if a unified diff of the workflow (old_text -> new_text) only adds or removes quoted items of the trigger
    `paths` list, comments or blank lines there, or comments and blank lines of the header before `on:`. Then the
    workflow edit alone runs no shard in full: a full run of all 8 shards takes hours of runners, and adding a test file
    to the trigger paths (AGENTS.md) changes no shard's mutants or tests. A `#` line elsewhere (in a `run:` block it is
    shell text) or any other change keeps the full run."""
    try:
        old_regions, new_regions = _regions(old_text), _regions(new_text)
    except StopIteration:
        return False
    old_no = new_no = None
    changed = False
    for line in diff.splitlines():
        m = _HUNK.match(line)
        if m:
            old_no, new_no = int(m.group(1)), int(m.group(2))
            continue
        if old_no is None or line.startswith("\\"):
            continue                                    # file headers before the first hunk, "\ No newline"
        tag, body = line[:1], line[1:]
        if tag == "-":
            if not _allowed(body, old_no, old_regions):
                return False
            old_no += 1
            changed = True
        elif tag == "+":
            if not _allowed(body, new_no, new_regions):
                return False
            new_no += 1
            changed = True
        else:
            old_no += 1
            new_no += 1
    return changed


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
        def git(*a):
            return subprocess.run(["git"] + list(a), check=True, capture_output=True, text=True).stdout
        merge_base = git("merge-base", base, head).strip()
        diff = git("diff", f"{base}...{head}", "--", WORKFLOW)
        if workflow_edit_is_trigger_only(diff, git("show", f"{merge_base}:{WORKFLOW}"), git("show", f"{head}:{WORKFLOW}")):
            paths.remove(WORKFLOW)
            print("workflow: only trigger paths or comments changed, not a reason for full runs", file=sys.stderr)
    arithmetic, mesh = select(paths, load_configs(), run_all, only)
    print("arithmetic=" + arithmetic)
    print("mesh=" + json.dumps(mesh))
