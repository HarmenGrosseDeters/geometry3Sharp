#!/usr/bin/env python3
"""Find the merge that turned a batched master CI run red (docs/qa/ci-and-gates.md "Batched master CI").

Master CI runs every 2 hours over all merges since the last tested head (tools/master_batch.py), so a red run can hold several
merges. This script bisects over the merges only (`git bisect --first-parent`), locally, with the failing tests as the check:
no runner minutes, about log2(merges) builds.

    # the merges between the last green master head and the red one
    python3 tools/master_bisect.py --good <green sha> --bad <red sha> --list
    # bisect them with the failing tests (from the red run's log), net10.0
    python3 tools/master_bisect.py --good <green sha> --bad <red sha> --filter "FullyQualifiedName~.FooTests."
    # any other check: a command whose exit code is 0 on a good commit (e.g. a net48 build)
    python3 tools/master_bisect.py --good <green sha> --bad <red sha> --cmd "dotnet build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true"

Prints the first bad merge (its subject names the PR). The working tree must be clean; the script resets the bisect at the end.
A failure that only shows on the Windows net48 shards and cannot be reproduced locally: dispatch ci.yml on a branch made at the
middle merge (the procedure in the doc), halving by hand.
"""

import shlex
import subprocess
import sys


def git(*args, check=True):
    return subprocess.run(["git"] + list(args), check=check, capture_output=True, text=True).stdout


def merges(good, bad):
    """the first-parent commits after good up to bad, oldest first, as (sha, subject)"""
    out = git("log", "--first-parent", "--reverse", "--format=%H %s", f"{good}..{bad}")
    return [tuple(line.split(" ", 1)) if " " in line else (line, "") for line in out.splitlines() if line.strip()]


def check_command(filt, cmd):
    if cmd:
        return cmd
    if not filt:
        raise SystemExit("give --filter (the failing tests) or --cmd")
    return ("dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter " + shlex.quote(filt))


def bisect(good, bad, command):
    """runs git bisect over the first-parent history; returns (sha, subject) of the first bad commit"""
    if git("status", "--porcelain").strip():
        raise SystemExit("the working tree is not clean")
    try:
        git("bisect", "start", "--first-parent", bad, good)
        # (exit 125 = skip is not used: a commit that does not build is bad, like a red test)
        p = subprocess.run(["git", "bisect", "run", "sh", "-c", command + " >/dev/null 2>&1 || exit 1"], capture_output=True, text=True)
        if p.returncode != 0:
            raise SystemExit("git bisect run failed:\n" + p.stdout + p.stderr)
        sha = git("rev-parse", "refs/bisect/bad").strip()
        return sha, git("log", "-1", "--format=%s", sha).strip()
    finally:
        git("bisect", "reset", check=False)


def main(argv):
    def arg(name):
        return argv[argv.index(name) + 1] if name in argv else None

    good, bad = arg("--good"), arg("--bad")
    if not good or not bad:
        print(__doc__)
        return 2
    found = merges(good, bad)
    if "--list" in argv:
        for sha, subject in found:
            print(sha[:12], subject)
        print(f"{len(found)} commits; a bisect needs about {max(1, len(found)).bit_length()} checks")
        return 0
    if len(found) == 1:
        print("first bad:", found[0][0], found[0][1])
        return 0
    sha, subject = bisect(good, bad, check_command(arg("--filter"), arg("--cmd")))
    print("first bad:", sha, subject)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
