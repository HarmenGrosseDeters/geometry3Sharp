#!/usr/bin/env python3
"""Run bounded hand mutants of the gate in isolated copies; never edit the checkout."""

import argparse
import json
from pathlib import Path
import subprocess
import sys
import tempfile


MUTANTS = {
    "invert_uncovered": ("if coverage[path][n] == 0", "if coverage[path][n] > 0"),
    "unlisted_file_fails": ('code = 0 if result["ok"] else 1', 'code = 0 if result["ok"] and not result["files_without_executable_lines"] else 1'),
    "intersect_suite_hits": ("max(hits.get(number, 0), count)", "min(hits.get(number, 0), count)"),
    "compare_head_to_itself": ('git(root, "merge-base", base, head)', 'git(root, "rev-parse", head)'),
    "ignore_shard_layout": (' or sorted((directory / suite).glob("*/*/coverage.cobertura.xml"))', ""),
    "merge_counts_as_mainline": (" and not _is_ancestor(root, commit, first)", ""),
    "ignore_method_only_records": ('raise ValueError("incomplete class-level line evidence")', "pass"),
    "slow_hit_excuses_everything": ("slow_hits.get(n, 0) > 0", "slow_hits.get(n, 0) >= 0"),
    "always_exit_zero": ('code = 0 if result["ok"] else 1', "code = 0"),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    tools = Path(__file__).resolve().parents[1]
    source = (tools / "changed_line_coverage.py").read_text()
    tests = (tools / "test_changed_line_coverage.py").read_text()
    records = []
    with tempfile.TemporaryDirectory(prefix="coverage-mutants-") as directory:
        root = Path(directory)
        (root / "test_changed_line_coverage.py").write_text(tests)
        for name, replacement in [("baseline", None), *MUTANTS.items()]:
            mutated = source
            if replacement:
                old, new = replacement
                if source.count(old) != 1:
                    raise ValueError(f"mutation target drift: {name}")
                mutated = source.replace(old, new)
            (root / "changed_line_coverage.py").write_text(mutated)
            # Disable bytecode caching: variants may have identical length and timestamp.
            run = subprocess.run([sys.executable, "-B", "-m", "unittest", "discover", "-s", str(root), "-v"],
                                 text=True, capture_output=True, timeout=60)
            failed_assertion = "FAIL:" in run.stderr or "ERROR:" in run.stderr
            ok = run.returncode == 0 if name == "baseline" else run.returncode == 1 and failed_assertion
            records.append({"name": name, "exit_code": run.returncode, "expected_result": ok,
                            "test_output": run.stdout + run.stderr})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(records, indent=2) + "\n")
    print(json.dumps([{k: v for k, v in r.items() if k != "test_output"} for r in records], indent=2))
    return 0 if all(r["expected_result"] for r in records) else 1


if __name__ == "__main__":
    sys.exit(main())
