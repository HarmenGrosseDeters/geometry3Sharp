#!/usr/bin/env python3
"""Results of mutation shards that the owner ran locally (run_local.sh / run_local.ps1), and their check in CI.

Exact Boolean 3.0 process (README, "Local runs"): the completion gate is a full run of every touched shard above its
break threshold on master, on GitHub or run locally. A local run writes a small JSON file; it is committed under
tests/mutation/local-results/<sha>/<shard>.json (or attached to the PR) and the mutation workflow checks it.

  record   --root R --shard S --report mutation-report.json --out FILE
      writes the result of a full run of shard S at the HEAD of the repository R (a clean working tree is required)
  check    --root R --head SHA [FILE...] [--summary FILE]
      validates the given result files, or every tests/mutation/local-results/*/*.json of the working tree; exit 1 if one
      is invalid. A result is valid when
        - it is a full run (mode "full") of a known shard, recorded from a clean working tree,
        - its sha is the commit being gated: the PR's head, or an ancestor of it from which the head differs only by
          files under tests/mutation/local-results/ (the result files themselves are added after the run),
        - the sha of its directory is the same,
        - its config hash is that of the shard's config file at the gated commit (LF line breaks) and the break it
          records is the config's thresholds.break,
        - its Stryker version is the one of .config/dotnet-tools.json at the gated commit,
        - its counts give its score (Stryker's formula: killed and timeouts over killed, timeouts, survived and no
          coverage), and the score is at least the break.
"""
import argparse
import glob
import hashlib
import json
import os
import re
import subprocess
import sys

import incremental

FORMAT = 1
RESULTS_DIR = "tests/mutation/local-results"
TOOLS = ".config/dotnet-tools.json"
SHA = re.compile(r"^[0-9a-f]{40}$")
KEYS = {"format", "sha", "shard", "config", "config_sha256", "stryker_version", "mode", "clean_tree", "score", "break",
        "counts", "survivors", "finished_utc", "platform"}


def config_path(shard):
    """repository-relative path of the config of a shard ('arithmetic' is stryker-config.json)"""
    if not re.match(r"^[a-z0-9-]+$", shard):
        raise ValueError("bad shard name %r" % shard)
    return "tests/mutation/stryker-config.json" if shard == "arithmetic" else "tests/mutation/stryker-mesh-%s.json" % shard


def normalized_hash(data):
    """sha256 of a file's bytes with CRLF turned into LF (a Windows checkout may have converted them)"""
    return hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest()


def git(root, *args, check=True):
    r = subprocess.run(["git", "-C", root] + list(args), capture_output=True, text=True)
    if check and r.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), r.stderr.strip()))
    return r


def git_show(root, rev, path):
    r = subprocess.run(["git", "-C", root, "show", "%s:%s" % (rev, path)], capture_output=True)
    if r.returncode != 0:
        raise RuntimeError("%s does not exist at %s" % (path, rev))
    return r.stdout


def tools_version(data):
    return json.loads(data)["tools"]["dotnet-stryker"]["version"]


def counts_of(report):
    c = {"killed": 0, "timeout": 0, "survived": 0, "nocoverage": 0}
    names = {"Killed": "killed", "Timeout": "timeout", "Survived": "survived", "NoCoverage": "nocoverage"}
    for f in report.get("files", {}).values():
        for m in f.get("mutants", []):
            if m.get("status") in names:
                c[names[m["status"]]] += 1
    return c


def score_of(c):
    return incremental.score(c["killed"] + c["timeout"], c["survived"] + c["nocoverage"])


def survivors_of(report):
    out = []
    for path, f in report.get("files", {}).items():
        for m in f.get("mutants", []):
            if m.get("status") in ("Survived", "NoCoverage"):
                out.append("%s %s:%d %s" % (m["status"], path.replace("\\", "/").split("/")[-1], m["location"]["start"]["line"],
                                            m.get("mutatorName", "")))
    return sorted(out)


# ---- record ----

def record(root, shard, report_path, now, platform):
    status = git(root, "status", "--porcelain", "--untracked-files=no").stdout.strip()
    if status:
        raise RuntimeError("the working tree is not clean (a result is only valid for a commit):\n" + status)
    sha = git(root, "rev-parse", "HEAD").stdout.strip()
    cfg_rel = config_path(shard)
    cfg_bytes = git_show(root, "HEAD", cfg_rel)
    cfg = json.loads(cfg_bytes.decode("utf-8-sig"))["stryker-config"]
    with open(report_path, encoding="utf-8-sig") as f:
        report = json.load(f)
    c = counts_of(report)
    s = score_of(c)
    if s is None:
        raise RuntimeError("the report has no mutant that counts")
    return {"format": FORMAT, "sha": sha, "shard": shard, "config": cfg_rel, "config_sha256": normalized_hash(cfg_bytes),
            "stryker_version": tools_version(git_show(root, "HEAD", TOOLS)), "mode": "full", "clean_tree": True,
            "score": round(s, 4), "break": cfg["thresholds"]["break"], "counts": c, "survivors": survivors_of(report),
            "finished_utc": now, "platform": platform}


# ---- check ----

def gated(root, sha, head):
    """None if sha is the commit being gated (head, or an ancestor from which head differs only by result files), else why not"""
    if sha == head:
        return None
    if git(root, "merge-base", "--is-ancestor", sha, head, check=False).returncode != 0:
        return "its sha %s is not the head %s of the PR nor an ancestor of it" % (sha[:10], head[:10])
    changed = [p for p in git(root, "diff", "--name-only", "%s..%s" % (sha, head)).stdout.split("\n") if p]
    other = [p for p in changed if not p.startswith(RESULTS_DIR + "/")]
    if other:
        return "the head differs from its sha %s by %d file(s) besides the results, e.g. %s: run again at the head" % (
            sha[:10], len(other), other[0])
    return None


def check_one(root, head, path):
    """list of problems of one result file (empty: valid) and its short description"""
    try:
        with open(path, encoding="utf-8-sig") as f:
            r = json.load(f)
    except (OSError, ValueError) as e:
        return ["not readable as JSON: %s" % e], path
    if not isinstance(r, dict) or set(r) != KEYS:
        return ["wrong keys (expected %s)" % ", ".join(sorted(KEYS))], path
    desc = "%s at %s" % (r.get("shard"), str(r.get("sha"))[:10])
    p = []
    if r["format"] != FORMAT:
        p.append("format %r, expected %d" % (r["format"], FORMAT))
    if r["mode"] != "full":
        p.append("mode %r: only a full run counts" % r["mode"])
    if r["clean_tree"] is not True:
        p.append("not run from a clean working tree")
    if not isinstance(r["sha"], str) or not SHA.match(r["sha"]):
        return p + ["sha is not a full commit hash"], desc
    parent = os.path.basename(os.path.dirname(os.path.abspath(path)))
    if parent != r["sha"]:
        p.append("stored in the directory %s, not %s" % (parent, r["sha"]))
    why = gated(root, r["sha"], head)
    if why:
        p.append(why)
        return p, desc
    try:
        cfg_rel = config_path(r["shard"])
        cfg_bytes = git_show(root, head, cfg_rel)
    except (ValueError, RuntimeError) as e:
        return p + [str(e)], desc
    if r["config"] != cfg_rel:
        p.append("config %s, expected %s" % (r["config"], cfg_rel))
    if r["config_sha256"] != normalized_hash(cfg_bytes):
        p.append("the config of the shard changed since the run (hash differs)")
    brk = json.loads(cfg_bytes.decode("utf-8-sig"))["stryker-config"]["thresholds"]["break"]
    if r["break"] != brk:
        p.append("break %s recorded, the config has %s" % (r["break"], brk))
    try:
        want = tools_version(git_show(root, head, TOOLS))
    except (RuntimeError, KeyError, ValueError) as e:
        want = None
        p.append("cannot read the Stryker version: %s" % e)
    if want is not None and r["stryker_version"] != want:
        p.append("Stryker %s, the tools file has %s" % (r["stryker_version"], want))
    c = r["counts"]
    if not isinstance(c, dict) or set(c) != {"killed", "timeout", "survived", "nocoverage"} or not all(
            isinstance(v, int) and not isinstance(v, bool) and v >= 0 for v in c.values()):
        p.append("counts are not killed/timeout/survived/nocoverage integers")
    else:
        s = score_of(c)
        if s is None:
            p.append("no mutant that counts")
        elif not isinstance(r["score"], (int, float)) or abs(s - r["score"]) > 0.01:
            p.append("score %s does not match the counts (%.4f)" % (r["score"], s))
        elif s < brk:
            p.append("score %.2f is below the break threshold %s" % (s, brk))
    return p, desc


def check(root, head, files, summary=None):
    if not files:
        files = sorted(glob.glob(os.path.join(root, RESULTS_DIR, "*", "*.json")))
    rows, bad = [], 0
    for f in files:
        problems, desc = check_one(root, head, f)
        bad += bool(problems)
        rows.append((os.path.relpath(f, root), desc, problems))
    lines = []
    for f, desc, problems in rows:
        lines.append("%s %s (%s)%s" % ("INVALID" if problems else "ok", f, desc, "".join("\n    - " + x for x in problems)))
    text = "\n".join(lines) if lines else "no local results"
    md = ["### Local mutation results", ""]
    if not rows:
        md.append("None in this PR.")
    for f, desc, problems in rows:
        md.append("- %s `%s` (%s)%s" % ("**invalid**" if problems else "valid, counts as the shard passing for the 3.0 completion gate:",
                                        f, desc, "".join("\n  - " + x for x in problems)))
    if summary:
        with open(summary, "a", encoding="utf-8") as fh:
            fh.write("\n".join(md) + "\n")
    return (1 if bad else 0), text


def main(argv):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("record")
    r.add_argument("--root", default=".")
    r.add_argument("--shard", required=True)
    r.add_argument("--report", required=True)
    r.add_argument("--out", required=True)
    r.add_argument("--platform", default="")
    c = sub.add_parser("check")
    c.add_argument("--root", default=".")
    c.add_argument("--head", required=True)
    c.add_argument("--summary")
    c.add_argument("files", nargs="*")
    a = ap.parse_args(argv)
    if a.cmd == "record":
        import datetime
        now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        try:
            res = record(a.root, a.shard, a.report, now, a.platform or sys.platform)
        except (RuntimeError, ValueError) as e:
            print("error: %s" % e, file=sys.stderr)
            return 2
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            json.dump(res, f, indent=2)
            f.write("\n")
        print("%s: score %.2f %% (break %s), %d survivors -> %s" % (a.shard, res["score"], res["break"], len(res["survivors"]), a.out))
        return 0 if res["score"] >= res["break"] else 1
    code, text = check(a.root, a.head, a.files, a.summary)
    print(text)
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
