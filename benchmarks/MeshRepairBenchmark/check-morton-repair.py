"""Check a paired packed-Morton campaign and report median Apply timings."""

import argparse
from collections import defaultdict
import json
import math
from pathlib import Path
import re
from statistics import median


def checked_checks(row):
    checks = row["robust_volume_checks"]
    sorted_counts = [check["morton_sorted_samples"] for check in checks]
    expected = [check["samples"] if row["packed_morton"] else 0 for check in checks]
    if sorted_counts != expected or (row["packed_morton"] and not any(sorted_counts)):
        raise ValueError(f"{row['file']} has invalid sorted-path coverage")
    result_fields = ("cells", "samples", "union", "iou_lo", "iou_hi", "sampled_cells", "child_tests",
                     "depth", "unclassified", "windings", "sampled_volume", "known_union", "known_both")
    return [tuple(check[key] for key in result_fields) for check in checks]


def apply_time(row):
    if (row["status"] != "closed" or not row["apply_returned"] or not row["after_valid"] or
            row["after_bowties"] != 0 or not math.isfinite(row["after_bbox_diag"])):
        raise ValueError(f"{row['file']} did not produce a valid closed repair")
    fingerprint = row["after_hash"]
    if not isinstance(fingerprint, str) or not re.fullmatch(r"[0-9A-Fa-f]{16}", fingerprint):
        raise ValueError(f"{row['file']} has an invalid repair fingerprint")
    ms = row["robust_apply_ms"]
    if not math.isfinite(ms) or ms <= 0:
        raise ValueError(f"{row['file']} has an invalid Apply duration")
    return ms


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("jsonl", type=Path)
    parser.add_argument("--smoke", action="store_true", help="one pair each for 356074 and B01")
    args = parser.parse_args()
    expected = {"356074.stl": 1, "B01-disjoint-a.obj": 1} if args.smoke else {
        "356074.stl": 6, "B01-disjoint-a.obj": 6, "996816.stl": 2}
    groups = defaultdict(list)
    with args.jsonl.open() as stream:
        for line in stream:
            row = json.loads(line)
            groups[row["file"]].append(row)
    if groups.keys() != expected.keys():
        raise ValueError("Unexpected repair workloads")

    for name, rows in groups.items():
        pairs = expected[name]
        if len(rows) != 2 * pairs:
            raise ValueError(f"{name} has missing or extra rows")
        baseline_times, packed_times = [], []
        fingerprints = set()
        for pair in range(pairs):
            a, b = rows[2 * pair:2 * pair + 2]
            packed_first = pair % 2 == 1
            if [a["packed_morton"], b["packed_morton"]] != [packed_first, not packed_first]:
                raise ValueError(f"{name} has unordered arms in pair {pair}")
            baseline, packed = (b, a) if packed_first else (a, b)
            baseline_times.append(apply_time(baseline))
            packed_times.append(apply_time(packed))
            fields = ("after_hash", "robust_used_exact", "robust_used_wrap", "robust_used_solidify",
                      "robust_result_reason", "robust_repair_iou", "hybrid_fallback", "hybrid_reason",
                      "robust_exact_reason", "robust_solidify_reason", "robust_wrap_reason",
                      "robust_exact_rounds", "robust_topology_changed")
            if any(baseline[key] != packed[key] for key in fields):
                raise ValueError(f"{name} changed its output or repair path in pair {pair}")
            if checked_checks(baseline) != checked_checks(packed):
                raise ValueError(f"{name} changed its volume-check results in pair {pair}")
            fingerprints.add(packed["after_hash"])
        savings = [1 - b / a for a, b in zip(baseline_times, packed_times)]
        print(f"{name}: {median(baseline_times):.2f} / {median(packed_times):.2f} ms, "
              f"{100 * median(savings):+.2f}% paired saving, {len(fingerprints)} output fingerprint(s)")


if __name__ == "__main__":
    main()
