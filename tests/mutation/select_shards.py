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
only a coarse one: test_select_shards.py checks that it covers every mutated file of every config.
An edit of the workflow runs every config in full, unless every changed line lies in a region that cannot change how
a shard runs (workflow_edit_forces_full below; checked on the diff between $BASE and $HEAD, which the workflow sets):
adding a test file to the trigger paths, raising max-parallel or editing a reporting step changes no shard.
A config whose edit only moves the spans of its `mutate` entries (a source edit shifts their character offsets; a
resplit moves a boundary) runs in `source` mode instead of in full: incremental.py carries the baseline's spans over
the line diff and runs what the moved boundary adds (config_edit_moves_only_spans; checked on $BASE / $HEAD as well).
With --pr (pull requests, owner 2026-11-23) only the `source` runs of the shards that mutate a changed exact-code file stay
(or of every shard with a changed file under the label `mutation`, --labels); the others are listed in `skipped` and run on
master (pr_selection below).
With --all every config runs in full (schedule, workflow_dispatch); --all --only arithmetic,rounding-1 runs only those
(workflow_dispatch input "shards"; "arithmetic" is stryker-config.json, the others are the mesh shards). --only also takes
prefixes (`boolean-*`) and the groups `nightly` (boolean-*, arrangement-*, rounding-*) and `weekly` (everything else).
"""
import fnmatch
import glob
import json
import os
import re
import subprocess
import sys

import incremental

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
#     (reporting) if they come after the Stryker step, are the only one of their name, have only the keys name, if, run,
#     uses, with, continue-on-error, and a run command that only reads (cat or jq); comment and blank lines whose
#     neighbours are not forcing. A workflow with YAML anchors, aliases or merge keys is not classified (any edit forces).
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


def _steps(lines):
    """the steps of the workflow (items at indent 6 of a `steps:` list): [(first line, end line (exclusive), identity)];
    the identity is `name:<name>` if the step has one, else `uses:<action without version>`, else its first line"""
    out = []
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
            out.append((i, j, ident))
            i = j
        else:
            i += 1
    return out


_STEP_KEYS_OK = {"name", "if", "run", "uses", "with", "continue-on-error"}
_QUOTED = re.compile(r"'[^']*'|\"[^\"]*\"")


def _read_only(command):
    """True if a `run` value only reads: cat or jq, with no redirect other than the append to the step summary, no
    command separator, pipe (other than `|| true`), substitution or background. Quoted text (the jq program) is
    skipped. A reporting step runs before "Prepare the baseline", so a command that wrote into StrykerOutput would
    change what is stored as the baseline."""
    text = " ".join(command.split())
    text = re.sub(r"^[>|][+-]?\s*", "", text)
    text = text.replace('>> "$GITHUB_STEP_SUMMARY"', "")
    text = _QUOTED.sub("''", text).replace("|| true", "")
    if not re.match(r"^(cat|jq)\s", text):
        return False
    return re.search(r"[;&<>`|]|\$\(", text) is None


def _reporting_step_ok(lines, step, stryker_start, idents):
    """a step that only reports: a name or action from the harmless list, after the Stryker step, unique in its job,
    with only plain keys, and a read-only command. A new step of the same name BEFORE the Stryker step (a `sed` on the
    thresholds, a write to $GITHUB_ENV, run_stryker.sh itself) is not harmless."""
    start, end, ident = step
    uploads = any(re.match(r"^\s*(?:- )?uses:\s*actions/upload-artifact@", lines[k]) and _indent(lines[k]) in (6, 8)
                  for k in range(start, end))
    if not (ident in STEPS_OK or ident.startswith("uses:actions/upload-artifact") or uploads):
        return False                                    # (an upload step may have a name of its own: the logs step)
    if stryker_start is None or start < stryker_start or idents.count(ident) != 1:
        return False
    run = []
    for k in range(start, end):
        line = lines[k]
        m = re.match(r"^\s*(?:- )?([A-Za-z0-9_-]+):\s*(.*)$", line)
        if m and _indent(line) in (6, 8):
            if m.group(1) not in _STEP_KEYS_OK:
                return False
            if m.group(1) == "run":
                run = [m.group(2)]
                for r in range(k + 1, end):
                    if lines[r].strip() and not lines[r].lstrip().startswith("#") and _indent(lines[r]) > 8:
                        run.append(lines[r].strip())
                    elif lines[r].strip() and not lines[r].lstrip().startswith("#"):
                        break
    if any(re.match(r"^\s*(?:- )?run:", lines[k]) for k in range(start, end)) and not _read_only(" ".join(run)):
        return False
    return True


# YAML anchors, aliases and merge keys can move a value into a region that the classification calls harmless, so a
# workflow that uses them is not classified: any edit forces the full run
_ANCHOR = re.compile(r"(^|[\s:\[{,])[&*][A-Za-z0-9_-]+|^\s*<<:")


def _uses_anchors(text):
    return any(_ANCHOR.search(l) for l in text.splitlines() if not l.lstrip().startswith("#"))


def _forcing_lines(text):
    """for every line of the workflow text: True if an edit of it forces the full run, False if not"""
    lines = text.splitlines()
    step_list = _steps(lines)
    steps = {st[0]: st for st in step_list}
    flags = [None] * len(lines)
    # per job (by the line of its first step): the first Stryker step and the identities of all steps
    job_starts = [i for i, l in enumerate(lines) if _indent(l) == 2 and re.match(r"^\s+[A-Za-z0-9_-]+:\s*$", l)] + [len(lines)]
    def job_of(i):
        return max((j for j in job_starts[:-1] if j <= i), default=0), min((j for j in job_starts if j > i), default=len(lines))
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
        first, last = job_of(i)
        in_job = [st for st in step_list if first <= st[0] < last]
        stryker = next((st[0] for st in in_job if st[2] == "name:Stryker"), None)
        ok = _reporting_step_ok(lines, step, stryker, [st[2] for st in in_job])
        if step[2] in KEY_LINES_OK_STEPS and re.match(r"^\s*(key|restore-keys):", line):
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
        if _uses_anchors(old_text) or _uses_anchors(new_text):
            return True
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


def config_edit_moves_only_spans(old_text, new_text):
    """True if two texts of a config differ at most in the spans of `mutate` (the same files in the same order, every
    other key equal); False for anything else, including a text that is not a config"""
    try:
        old, new = json.loads(old_text)["stryker-config"], json.loads(new_text)["stryker-config"]
        return incremental.without_spans(old) == incremental.without_spans(new)
    except (ValueError, KeyError, TypeError, AttributeError):
        return False


def test_class(term):
    """the test class a positive FullyQualifiedName term names: `~C.M` or `~C`, or `=Namespace.C.M` (an exact method)"""
    if term.startswith("FullyQualifiedName="):
        parts = term[len("FullyQualifiedName="):].split(".")
        return parts[-2] if len(parts) >= 2 else parts[0]
    return term.split("~")[1].split(".")[0]


def test_files(cls):
    """the files of a test class: `C.cs` and/or `C.CsCheck.cs` (the property tests, which the csproj compiles on net10.0 only); `C.cs`
    when neither exists (the existence test of the configs then reports it)"""
    found = [TESTS + cls + suffix for suffix in (".cs", ".CsCheck.cs") if os.path.isfile(os.path.join(HERE, "..", "..", TESTS + cls + suffix))]
    return found or [TESTS + cls + ".cs"]


def load_configs(directory=HERE):
    """name of the config -> (config path, mutated paths, test paths)"""
    configs = {}
    for path in sorted(glob.glob(os.path.join(directory, "stryker-*.json"))):
        name = os.path.basename(path)[len("stryker-"):-len(".json")]
        with open(path, encoding="utf-8") as f:
            config = json.load(f)["stryker-config"]
        # "**/mesh_ops/ExactMeshBoolean.cs{0..47051}" -> mesh_ops/ExactMeshBoolean.cs
        sources = {re.sub(r"\{.*\}$", "", m).replace("**/", "") for m in config["mutate"]}
        # "FullyQualifiedName~MeshRoundingRepairTests.ExactVertices" -> the file of class MeshRoundingRepairTests;
        # "A&FullyQualifiedName!~B" narrows A: only the positive terms name test classes
        # "(FullyQualifiedName=geometry3Sharp.Tests.C.M&DisplayName~row)" (coverage_select.py) -> class C
        terms = [t.strip("()") for alt in config["test-case-filter"].split("|") for t in alt.split("&")]
        classes = {test_class(t) for t in terms if t.startswith("FullyQualifiedName") and "!~" not in t}
        configs[name] = ("tests/mutation/" + os.path.basename(path), sources, {path for c in classes for path in test_files(c)})
    return configs


def mode_of(config, changed):
    config_path, sources, tests = config
    if changed & (ALWAYS | {config_path}):
        return "full"
    if changed & sources:
        # (an unchanged file of a shard with several keeps all its baseline results: incremental.py)
        return "source"
    return "tests" if changed & tests else "none"


# The scheduled runs of master (3.0 process, README): the big shards of the exact boolean pipeline Monday to Saturday, the rest
# on Sunday. `--only nightly` / `--only weekly` name these groups (the other names of --only are shards, or prefixes ending in `*`).
NIGHTLY = ("boolean-*", "arrangement-*", "rounding-*")


def expand_only(value, names):
    """the set of shard names that a --only value selects: shard names, prefixes ending in `*`, and the groups `nightly` and
    `weekly` (every shard that is not nightly); a name that matches nothing is an error"""
    out = set()
    for item in (x.strip() for x in value.split(",") if x.strip()):
        if item == "nightly":
            out |= expand_only(",".join(NIGHTLY), names)
        elif item == "weekly":
            out |= set(names) - expand_only("nightly", names)
        elif item.endswith("*"):
            hits = {n for n in names if n.startswith(item[:-1])}
            if not hits:
                raise ValueError("no shard matches " + item)
            out |= hits
        else:
            out.add(item)
    return out


def shard_name(config_name):
    """config name -> the name used in --only and in the cache keys: config -> arithmetic, mesh-x -> x"""
    return "arithmetic" if config_name == "config" else config_name[len("mesh-"):]


# These scripts run in every shard's real Stryker run (run_stryker.sh, the baseline store and the incremental plan/merge);
# the tests with a fake dotnet cannot see Stryker itself. A change to one of them selects the cheapest mesh shard in full
# (about 30 minutes), as a representative; on a PR that full run is not made (pr_selection), so it is the next dispatched or
# scheduled run on master. select_shards.py and the test files run in the `changes` job of every workflow run.
# Exact code (CLAUDE.md "Review before every merge"): the files whose change gives a PR the incremental runs of the shards that
# mutate them (pr_selection). The list is the section of .editorconfig that holds the exact code (the source of truth:
# predicates, generated predicates, exact and fixed-width points, mesh_ops/ExactMesh*, ...), found by the glob of the exact mesh
# pipeline; it is not a second list kept by hand.
EDITORCONFIG = os.path.join(os.path.dirname(os.path.dirname(HERE)), ".editorconfig")
EXACT_MARKER = "mesh_ops/ExactMesh*.cs"


def exact_globs(text=None):
    """the globs of the exact-code section of .editorconfig; ValueError when there is none (the gate must not go silent)"""
    if text is None:
        with open(EDITORCONFIG, encoding="utf-8") as f:
            text = f.read()
    for line in text.splitlines():
        m = re.match(r"^\[\{(.*)\}\]\s*$", line.strip())
        if m and EXACT_MARKER in m.group(1).split(","):
            return m.group(1).split(",")
    raise ValueError("no exact-code section with %s in .editorconfig" % EXACT_MARKER)


def exact_gated(path):
    return any(fnmatch.fnmatchcase(path, g) for g in exact_globs())


SCRIPTS = {"tests/mutation/run_stryker.sh", "tests/mutation/incremental.py", "tests/mutation/baseline.py", "tests/mutation/exclude_other_sources.py"}
SCRIPT_SHARD = "mesh-fixed"


def select(changed, configs, run_all=False, only=None, moved=()):
    """(mode of the arithmetic config, list of the mesh shards to run); `moved`: the names of the configs whose edit
    only moved their spans (not in `changed`), which run at least incrementally"""
    changed = set(changed)
    if only is not None:
        unknown = set(only) - {shard_name(n) for n in configs}
        if unknown:
            raise ValueError("unknown shards: " + ", ".join(sorted(unknown)))
    modes = {name: ("full" if only is None or shard_name(name) in only else "none") if run_all
             else mode_of(config, changed) for name, config in configs.items()}
    for name in moved:
        if not run_all and modes.get(name) in ("none", "tests"):
            modes[name] = "source"
    if not run_all and changed & SCRIPTS and SCRIPT_SHARD in modes:
        modes[SCRIPT_SHARD] = "full"
    mesh = [{"shard": name[len("mesh-"):], "mode": mode} for name, mode in modes.items()
            if name.startswith("mesh-") and mode != "none"]
    return modes["config"], mesh


# Pull requests (owner 2026-11-23, runner load: mutation took 44 % of the Actions minutes, almost all on PRs that touch no exact
# code): a PR runs a shard only incrementally (`source`: the changed lines against the master baseline), and only when the PR
# changes an exact-code file that the shard mutates (exact_gated, the exact section of .editorconfig), or when the PR has the
# label `mutation` (set by the coordinator for a PR that adds or changes an error bound outside the exact files; then every
# shard that mutates a changed file runs incrementally). Everything else makes no PR run: full runs (a config, the workflow or
# the tools changed; a shard without a cached baseline is skipped by run_stryker.sh NO_FULL), runs of changed tests only, and
# source changes of non-exact code. They run on master: the scheduled nightly / weekly runs, or a dispatch of the shards.
LABEL = "mutation"


def eligible(config, changed, labels=()):
    """True if a PR with these changed paths and labels gets the incremental run of this shard"""
    touched = set(changed) & config[1]
    if LABEL in labels:
        return bool(touched)
    return any(exact_gated(p) for p in touched)


WHY = {
    "full": "its config, the workflow or the mutation tools changed: full run on master",
    "tests": "only tests of this shard changed: scheduled run on master",
    "source": "the PR changes no exact-code file of this shard (and has no label `mutation`): scheduled run on master",
    "full-gated": "its config changed, and the PR changes exact code of this shard: no full run on a PR, "
                  "GATE ON MASTER: dispatch the full run of this shard on master after the merge (it is the gate)",
}


def pr_selection(arithmetic, mesh, configs, changed, labels=()):
    """(arithmetic, mesh, skipped) for a pull request: only `source` runs of eligible shards stay; every other selected shard is
    not started and is listed in `skipped` as {"shard": name as in --only, "why": reason}"""
    changed = set(changed)
    skipped = []

    def keep(name, mode):
        if mode == "source" and eligible(configs[name], changed, labels):
            return True
        if mode == "full" and eligible(configs[name], changed, labels) and configs[name][0] not in changed:
            # (full only because of the workflow, the tools or a script, not its own config: its baseline still fits the
            # config, so the incremental run of the changed exact lines keeps the PR gated; without a fitting baseline
            # run_stryker.sh skips it with a warning)
            return True
        why = WHY["full-gated"] if mode == "full" and eligible(configs[name], changed, labels) else WHY[mode]
        skipped.append({"shard": shard_name(name), "why": why})
        return False

    if arithmetic != "none":
        arithmetic = "source" if keep("config", arithmetic) else "none"
    mesh = [dict(e, mode="source") for e in mesh if keep("mesh-" + e["shard"], e["mode"])]
    return arithmetic, mesh, skipped


# The error bound audit (tools/ErrorBoundAudit): a PR that touches it or a file it anchors may change an error bound; the
# coordinator decides whether the label `mutation` is needed (a hint in the job summary, no automatic gating)
AUDIT_DIR = "tools/ErrorBoundAudit/"
AUDIT_BOUNDS = os.path.join(os.path.dirname(os.path.dirname(HERE)), "tools", "ErrorBoundAudit", "bounds.py")


def audit_files(text=None):
    if text is None:
        with open(AUDIT_BOUNDS, encoding="utf-8") as f:
            text = f.read()
    return set(re.findall(r"'([A-Za-z0-9_]+/[A-Za-z0-9_/]+\.cs)'", text))


def error_bound_hint(changed, files=None):
    files = audit_files() if files is None else files
    return sorted(p for p in set(changed) if p.startswith(AUDIT_DIR) or p in files)


def skip_notice(entry):
    # (a shard whose gate moves to master is a warning: the coordinator must dispatch its full run after the merge)
    kind = "warning" if "GATE ON MASTER" in entry["why"] else "notice"
    return f"::{kind}::{entry['shard']}: NOT RUN on this PR ({entry['why']})"


if __name__ == "__main__":
    args = sys.argv[1:]
    if args[:1] == ["report"]:
        # report <json list of skipped shards> [--summary]: the notices (or the job summary) for the shards skipped on a PR
        shards = json.loads(args[1])
        if "--summary" in args:
            if shards:
                print("### Shards not run on this PR (NOT RUN)\n")
                for e in shards:
                    print(f"- `{e['shard']}`: {e['why']} (tests/mutation/README.md).")
        else:
            for e in shards:
                print(skip_notice(e))
        sys.exit(0)
    if args[:1] == ["hint"]:
        # hint [--summary]: changed paths on stdin; the paths that may change an error bound (the audit or a file it anchors)
        paths = error_bound_hint([l.strip() for l in sys.stdin if l.strip()])
        if paths:
            if "--summary" in args:
                print("### Error bound touched?\n")
                print("The PR changes the error bound audit or a file it anchors: " + ", ".join(f"`{p}`" for p in paths)
                      + ". If it adds or changes an error bound outside the exact files, the coordinator sets the label `mutation` "
                      "and re-runs this workflow (the label is read live).")
            else:
                print("::warning::error bound touched? " + ", ".join(paths) + ": label `mutation` if a bound outside the exact files changed")
        sys.exit(0)
    run_all = "--all" in args
    only = None
    if "--only" in args:
        value = args[args.index("--only") + 1].strip()
        only = None if value in ("", "all") else expand_only(value, {shard_name(n) for n in load_configs()})
    paths = [] if run_all else [line.strip() for line in sys.stdin if line.strip()]
    all_paths = list(paths)
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
    moved = set()
    if base and head and not run_all:
        def git_show(rev, path):
            p = subprocess.run(["git", "show", f"{rev}:{path}"], capture_output=True, text=True)
            return p.stdout if p.returncode == 0 else None
        merge_base = subprocess.run(["git", "merge-base", base, head], check=True, capture_output=True,
                                    text=True).stdout.strip()
        for path in [p for p in paths if re.fullmatch(r"tests/mutation/stryker-[^/]*\.json", p)]:
            old, new = git_show(merge_base, path), git_show(head, path)
            if old is not None and new is not None and config_edit_moves_only_spans(old, new):
                paths.remove(path)
                moved.add(os.path.basename(path)[len("stryker-"):-len(".json")])
                print(f"{path}: only the spans of mutate moved, the shard runs incrementally", file=sys.stderr)
    configs = load_configs()
    arithmetic, mesh = select(paths, configs, run_all, only, moved)
    skipped = []
    if "--pr" in args and not run_all:
        labels = []
        if "--labels" in args:
            labels = [x.strip() for x in args[args.index("--labels") + 1].split(",") if x.strip()]
        arithmetic, mesh, skipped = pr_selection(arithmetic, mesh, configs, all_paths, labels)
    print("arithmetic=" + arithmetic)
    print("mesh=" + json.dumps(mesh))
    if "--pr" in args:
        print("skipped=" + json.dumps(skipped))
