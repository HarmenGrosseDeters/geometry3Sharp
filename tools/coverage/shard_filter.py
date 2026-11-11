#!/usr/bin/env python3
"""Print a vstest filter that selects shard I of N of the test classes in a `dotnet test --list-tests` listing.

The coverage job splits the core suite over several runners (coverlet makes it about 3x slower). Every class lands in
exactly one shard (round robin over the sorted class names), the base filter is kept. A class name that is a prefix of
another one also selects the longer class (`~` is a contains match); that only repeats work, hits are merged by max.
"""
import sys


def classes(listing):
    names = set()
    for line in listing.splitlines():
        if not line.startswith("    "):  # test names are indented, the header lines are not
            continue
        name = line.strip().split("(")[0]
        if "." in name:
            names.add(name.rsplit(".", 1)[0])
    return sorted(names)


def shard_filter(listing, index, count, base):
    mine = classes(listing)[index::count]
    if not mine:
        raise ValueError("empty shard")
    return f"({base})&(" + "|".join(f"FullyQualifiedName~{c}." for c in mine) + ")"


if __name__ == "__main__":
    index, count, base = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3]
    print(shard_filter(sys.stdin.read(), index, count, base))
