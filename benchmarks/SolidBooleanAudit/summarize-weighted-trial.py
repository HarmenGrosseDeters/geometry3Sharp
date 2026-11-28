"""Validate paired endpoint trial rows and summarize each workload."""
import json
import sys
from collections import defaultdict
from pathlib import Path
from statistics import median


def read_groups(paths):
    groups = defaultdict(list)
    for path in paths:
        for line in Path(path).read_text().splitlines():
            row = json.loads(line)
            groups[row["label"], row["operation"], row["budget"]].append(row)
    return groups


def report(groups):
    print("| Case | Cap | Pairs | Current ms | Weighted ms | Paired gain ms | Weighted guard ms | Current / weighted allocated MB |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|")
    for (label, operation, cap), rows in groups.items():
        assert len({(row["prediction"], tuple(row["hashes"])) for row in rows}) == 1
        for index, row in enumerate(rows):
            assert row["repetition"] == index and row["earlyFirst"] == bool(index % 2)
            rejected = cap > 0 and row["prediction"] > cap
            assert row["status"] == ("BudgetExceeded" if rejected else "Success")
            if not rejected:
                assert row["sameMesh"]
            for bound, phases in (
                (row["referenceLowerBound"], row["baselinePhases"]),
                (row["lowerBound"], row["filteredPhases"]),
            ):
                if bound is not None:
                    assert cap < bound <= row["prediction"] and "merge.crossings" not in phases
                else:
                    assert "merge.predict" in phases
        before = median(row["baselineMs"] for row in rows)
        after = median(row["filteredMs"] for row in rows)
        gain = median(row["baselineMs"] - row["filteredMs"] for row in rows)
        guard = median(row["filteredPhases"]["merge.endpoint_budget"] for row in rows)
        before_mb = median(row["baselineBytes"] for row in rows) / 1e6
        after_mb = median(row["filteredBytes"] for row in rows) / 1e6
        print(f"| {label} {operation.lower()} | {cap:,} | {len(rows)} | {before:,.2f} | {after:,.2f} | "
              f"{gain:,.2f} | {guard:,.1f} | {before_mb:,.2f} / {after_mb:,.2f} |")


if __name__ == "__main__":
    report(read_groups(sys.argv[1:]))
