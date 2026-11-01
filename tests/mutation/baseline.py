#!/usr/bin/env python3
"""The baseline of the incremental mutation runs (.github/workflows/mutation.yml, README.md).

  store <StrykerOutput dir> <baseline dir>
      after a full run: copy the JSON report of the run into <baseline dir>/stryker-report.json, without the files that
      have no tested mutant (Stryker reports every file of the library, nearly all of them only with ignored
      mutants; the baseline filter looks up the mutated files only). Writes stored=true|false to GITHUB_OUTPUT.
  count <StrykerOutput dir>
      after a run: the mutants by how they got their result (tested in this run, taken from the baseline, ignored).
"""
import glob
import json
import os
import sys
from collections import Counter

FROM_BASELINE = "Result based on previous run"


def latest_report(output_dir):
    reports = sorted(glob.glob(os.path.join(output_dir, "*", "reports", "mutation-report.json")))
    return reports[-1] if reports else None


def strip(report):
    """the report with only the files that have a mutant that is not ignored"""
    files = {path: f for path, f in report.get("files", {}).items()
             if any(m.get("status") != "Ignored" for m in f.get("mutants", []))}
    return dict(report, files=files)


def counts(report):
    """Counter of (status, origin): origin 'baseline' for results taken from the baseline, else 'run'"""
    c = Counter()
    for f in report.get("files", {}).values():
        for m in f.get("mutants", []):
            status = m.get("status", "")
            if status == "Ignored":
                c["ignored"] += 1
            elif m.get("statusReason") == FROM_BASELINE:
                c["from baseline"] += 1
            else:
                c["tested in this run"] += 1
    return c


def output(key, value):
    path = os.environ.get("GITHUB_OUTPUT")
    if path:
        with open(path, "a", encoding="utf-8") as f:
            f.write("%s=%s\n" % (key, value))
    print("%s=%s" % (key, value))


def main(argv):
    if len(argv) == 3 and argv[0] == "store":
        report_path = latest_report(argv[1])
        if report_path is None:
            print("no report: nothing stored")
            output("stored", "false")
            return 0
        with open(report_path, encoding="utf-8-sig") as f:
            report = strip(json.load(f))
        os.makedirs(argv[2], exist_ok=True)
        with open(os.path.join(argv[2], "stryker-report.json"), "w", encoding="utf-8") as f:
            json.dump(report, f)
        with open(os.path.join(argv[2], "source.txt"), "w", encoding="utf-8") as f:
            f.write("%s %s run %s\n" % (os.environ.get("GITHUB_REF", "local"), os.environ.get("GITHUB_SHA", ""),
                                        os.environ.get("GITHUB_RUN_ID", "")))
        print("stored %d files of %s" % (len(report["files"]), report_path))
        output("stored", "true")
        return 0
    if len(argv) == 2 and argv[0] == "count":
        report_path = latest_report(argv[1])
        if report_path is None:
            print("no report")
            return 0
        with open(report_path, encoding="utf-8-sig") as f:
            c = counts(json.load(f))
        for k in ("tested in this run", "from baseline", "ignored"):
            print("%s: %d" % (k, c[k]))
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
