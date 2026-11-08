#!/usr/bin/env python3
"""Which Stryker configs a pull request needs (.github/workflows/mutation.yml).

Reads the changed paths from stdin and writes GITHUB_OUTPUT lines to stdout:
  arithmetic=none|full|source|tests
  mesh=[{"shard": "rounding-1", "mode": "full"}, ...]

Modes (tests/mutation/README.md):
  full   the config or the tools changed: the whole shard, with its break threshold
  source a mutated file changed: only the mutants of the changed lines run, the others keep their results from the
         baseline of the last full run (incremental.py), with the break threshold on the combined score; without a
         baseline the workflow runs the shard in full
  tests  only tests that run against the shard changed: mutants those tests cover, report only

The files of a shard come from its config (mutate, test-case-filter). The paths of the workflow are a second list, but
only a coarse one: test_select_shards.py checks that it covers every config.
An edit of the workflow runs every config in full, unless every changed line lies in a region that cannot change how
a shard runs (workflow_edit_forces_full below; checked on the diff between $BASE and $HEAD, which the workflow sets):
adding a test file to the trigger paths, raising max-parallel or editing a reporting step changes no shard.
With --all every config runs in full (schedule, workflow_dispatch); --all --only arithmetic,rounding-1 runs only those
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

# Which edits of the workflow force every shard to run in full. A shard's measurement depends on how Stryker is set up
# and run (the Stryker step, its env, the runner, the tools, the checkout), not on the trigger, the selection job, the
# reporting steps or the scheduling of the matrix. So every line of an edit is classified by its region (below), by
# position in the old text (removed lines) or the new text (added lines); any line in an unknown or Stryker-relevant
# region keeps the full run. Unknown jobs, keys and steps force a full run (conservative).
#   not forcing: the header (name, on: with the trigger paths, the cron, the dispatch input), concurrency, the whole
#     `changes` job, in the two Stryker jobs `needs`, `if`, `timeout-minutes`, `outputs`, `strategy.fail-fast`,
#     `strategy.max-parallel` (scheduling only), and the steps Summary, Surviving mutants and upload-artifact
#     (reporting); comment and blank lines whose neighbours are not forcing.
#   not forcing, on purpose: the cache key lines of "Restore the baseline" / "Store the baseline". A changed key can only
#     lose baselines, and a shard in `source` mode without a baseline runs in full (run_stryker.sh), so no result is ever
#     taken from a wrong baseline by it. That a key still names its shard and the format is checked by
#     test_select_shards.test_cache_keys_name_the_shard_and_the_format, as widening restore-keys would be unsafe.
#   forcing: permissions, env, runs-on, strategy.matrix, the steps checkout, setup-dotnet, Restore tools, Restore the
#     baseline (its `if`, path), Stryker (name, env, run), Prepare the baseline and Store the baseline (their `if` and
#     path), every other top key, job, job key and step.
# The shards themselves come from the configs: the matrix is `fromJSON` of the selection, so a new or changed shard is
# selected by its own config path (mode full for that shard alone).
_HUNK = re.compile(r"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")
_KEY = re.compile(r"^(\s*)(?:- )?([A-Za-z0-9_-]+):")
TOP_OK = {"name", "on", "concurrency"}
KNOWN_JOBS = {"exact-arithmetic", "exact-mesh"}
JOB_KEYS_OK = {"needs", "if", "timeout-minutes", "outputs", "steps"}
STRATEGY_OK = {"fail-fast", "max-parallel"}
STEPS_OK = {"name:Summary", "name:Surviving mutants"}
KEY_LINES_OK_STEPS = {"name:Restore the baseline", "name:Store the baseline"}


def _indent(line):
    return len(line) - len(line.lstrip(" "))


def _step_identities(lines):
    """first line number (0-based) of a step of the Stryker jobs -> its identity: `name:<name>` if it has one, else
    `uses:<action without version>`, else the text of its first line"""
    identities = {}
    i = 0
    while i < len(lines):
        line = lines[i]
        if _indent(line) == 6 and line.lstrip().startswith("- "):
            j = i + 1
            while j < len(lines) and (not lines[j].strip() or lines[j].lstrip().startswith("#") or _indent(lines[j]) > 6):
                j += 1
            ident = None
            for k in range(i, j):
                m = re.match(r"^\s*(?:- )?name:\s*(.*?)\s*$", lines[k])
                if m and _indent(lines[k]) in (6, 8):
                    ident = "name:" + m.group(1)
                    break
            if ident is None:
                m = re.match(r"^\s*- uses:\s*([^@\s]+)", line)
                ident = "uses:" + m.group(1) if m else "step:" + line.strip()
            identities[i] = ident
            i = j
        else:
            i += 1
    return identities


def _forcing_lines(text):
    """for every line of the workflow text: True if an edit of it forces the full run, False if not"""
    lines = text.splitlines()
    steps = _step_identities(lines)
    flags = [None] * len(lines)
    top = job = jobkey = substrategy = step = None
    for i, line in enumerate(lines):
        if not line.strip() or line.lstrip().startswith("#"):
            continue                                    # comment and blank lines: resolved below
        ind = _indent(line)
        m = _KEY.match(line)
        key = m.group(2) if m else None
        if ind == 0:
            top, job, jobkey, substrategy, step = key, None, None, None, None
            flags[i] = False if top in TOP_OK or top == "jobs" else True
            continue
        if top in TOP_OK:
            flags[i] = False
            continue
        if top != "jobs":
            flags[i] = True
            continue
        if ind == 2:
            job, jobkey, substrategy, step = key, None, None, None
            flags[i] = job not in KNOWN_JOBS and job != "changes"
            continue
        if job == "changes":
            flags[i] = False
            continue
        if job not in KNOWN_JOBS:
            flags[i] = True
            continue
        if ind == 4:
            jobkey, substrategy, step = key, None, None
            flags[i] = jobkey not in JOB_KEYS_OK and jobkey != "strategy"
            continue
        if jobkey == "strategy":
            if ind == 6:
                substrategy = key
            flags[i] = substrategy not in STRATEGY_OK
            continue
        if jobkey != "steps":
            flags[i] = jobkey not in JOB_KEYS_OK
            continue
        if i in steps:
            step = steps[i]
        if step is None:
            flags[i] = True
            continue
        ok = step in STEPS_OK or step.startswith("uses:actions/upload-artifact")
        if step in KEY_LINES_OK_STEPS and re.match(r"^\s*(key|restore-keys):", line):
            ok = True                                   # (a continuation of a key is on its own line: see the test)
        flags[i] = not ok
    # a comment or blank line forces if a neighbouring line of code does (a `#` in a run block is shell text)
    for i, f in enumerate(flags):
        if f is None:
            before = next((flags[k] for k in range(i - 1, -1, -1) if flags[k] is not None), None)
            after = next((flags[k] for k in range(i + 1, len(flags)) if flags[k] is not None), None)
            known = [x for x in (before, after) if x is not None]
            flags[i] = any(known) if known else True
    return flags


def workflow_edit_forces_full(diff, old_text, new_text):
    """True if a unified diff of the workflow (old_text -> new_text) changes a line that can change how a shard runs
    (see the table above), or is empty. False: the edit alone runs no shard in full (a full run of all shards takes
    hours of runners; adding a trigger path, raising max-parallel or editing a reporting step changes no shard)."""
    try:
        old_flags, new_flags = _forcing_lines(old_text), _forcing_lines(new_text)
    except Exception:
        return True
    old_no = new_no = None
    changed = False
    for line in diff.splitlines():
        m = _HUNK.match(line)
        if m:
            old_no, new_no = int(m.group(1)), int(m.group(2))
            continue
        if old_no is None or line.startswith("\\"):
            continue                                    # file headers before the first hunk, "\ No newline"
        tag = line[:1]
        if tag == "-":
            if old_no > len(old_flags) or old_flags[old_no - 1]:
                return True
            old_no += 1
            changed = True
        elif tag == "+":
            if new_no > len(new_flags) or new_flags[new_no - 1]:
                return True
            new_no += 1
            changed = True
        else:
            old_no += 1
            new_no += 1
    return not changed


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
        # (a failed git diff raises: an empty diff must not count as harmless)
        def git(*a):
            return subprocess.run(["git"] + list(a), check=True, capture_output=True, text=True).stdout
        merge_base = git("merge-base", base, head).strip()
        diff = git("diff", f"{base}...{head}", "--", WORKFLOW)
        if not workflow_edit_forces_full(diff, git("show", f"{merge_base}:{WORKFLOW}"), git("show", f"{head}:{WORKFLOW}")):
            paths.remove(WORKFLOW)
            print("workflow: only regions that do not change how a shard runs were edited, not a reason for full runs",
                  file=sys.stderr)
    arithmetic, mesh = select(paths, load_configs(), run_all, only)
    print("arithmetic=" + arithmetic)
    print("mesh=" + json.dumps(mesh))
