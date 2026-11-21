#!/usr/bin/env python3
"""Fast documentation hygiene checks for geometry3Sharp (runs in < 1 s).

Checks:
1. Unresolved git merge conflict markers in all *.md files.
2. Broken relative file links in all *.md files.
3. Broken #anchor links in user-facing and navigation Markdown files.
4. Index completeness across docs/, benchmarks/, and tools/ README indexes.
5. Table row length limit (<= 500 chars) in user-facing docs (README, OPERATIONS, UPSTREAM_CHANGES, docs/guides/, tools/g3cli/).
6. Root docs/*.md allowlist to keep new docs in their domain subdirectories.

Usage:
    python3 tools/check_docs.py [--root <path>]
"""

import argparse
import glob
import os
import re
import sys

DOCS_ROOT_ALLOWLIST = frozenset({
    "AGENT_WORKFLOW.md",
    "Analyse-2026-10.md",
    "ISSUE_CASE_FINDINGS.md",
    "OPEN_TOPICS.md",
    "OPERATIONS.md",
    "PREDICATES.md",
    "QA.md",
    "README.md",
    "REVIEW_CHECKLIST.md",
    "ROADMAP.md",
    "ROBUSTNESS_SWEEP.md",
    "SOURCES.md",
    "UPSTREAM_CHANGES.md",
})

SKIP_DIRS = frozenset({".git", "bin", "obj", "artifacts", "node_modules", "TestResults"})

TABLE_LINE_LIMIT = 500

CONFLICT_RE = re.compile(r"^(<{7}|>{7}) ")
LINK_RE = re.compile(r"\]\(([^)]+)\)")
FENCED_CODE_RE = re.compile(r"```.*?```", re.S)


def slugify_gfm(heading):
    """GitHub Flavored Markdown heading slugifier."""
    h = heading.strip().lower()
    h = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", h)
    h = h.replace("`", "")
    h = re.sub(r"[^a-z0-9 _-]", "", h)
    h = h.replace(" ", "-")
    return h


def collect_markdown_files(root):
    result = []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
        for fn in sorted(filenames):
            if fn.endswith(".md"):
                result.append(os.path.relpath(os.path.join(dirpath, fn), root).replace("\\", "/"))
    return result


def get_anchors(abs_path, cache):
    if abs_path not in cache:
        anchors = set()
        seen = {}
        in_code = False
        with open(abs_path, encoding="utf-8") as fh:
            for line in fh:
                if line.startswith("```"):
                    in_code = not in_code
                    continue
                if in_code:
                    continue
                m = re.match(r"^#{1,6}\s+(.*)$", line)
                if m:
                    s = slugify_gfm(m.group(1))
                    cnt = seen.get(s, 0)
                    seen[s] = cnt + 1
                    anchors.add(s if cnt == 0 else f"{s}-{cnt}")
        cache[abs_path] = anchors
    return cache[abs_path]


def check_conflict_markers(root, md_files):
    errors = []
    for rel in md_files:
        path = os.path.join(root, rel)
        with open(path, encoding="utf-8") as fh:
            for idx, line in enumerate(fh, 1):
                if CONFLICT_RE.match(line):
                    errors.append(f"{rel}:{idx}: unresolved git conflict marker")
    return errors


def anchor_checked_files(root):
    fixed = [
        "README.md",
        "docs/README.md",
        "docs/OPERATIONS.md",
        "docs/QA.md",
        "docs/SOURCES.md",
        "docs/UPSTREAM_CHANGES.md",
        "docs/design/README.md",
        "docs/archive/README.md",
        "tools/README.md",
        "benchmarks/README.md",
        "tools/g3cli/README.md",
    ]
    globs = [
        "docs/guides/*.md",
        "docs/qa/*.md",
        "docs/sources/*.md",
        "tools/g3cli/docs/*.md",
    ]
    files = set()
    for rel in fixed:
        if os.path.isfile(os.path.join(root, rel)):
            files.add(rel)
    for pattern in globs:
        for p in glob.glob(os.path.join(root, pattern)):
            files.add(os.path.relpath(p, root).replace("\\", "/"))
    return files


def check_links(root, md_files):
    errors = []
    anchor_files = anchor_checked_files(root)
    anchor_cache = {}
    for rel in md_files:
        path = os.path.join(root, rel)
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
        no_code = FENCED_CODE_RE.sub("", text)
        base = os.path.dirname(path)
        check_anchor = rel in anchor_files
        for m in LINK_RE.finditer(no_code):
            link = m.group(1).strip()
            if not link or link.startswith(("http://", "https://", "mailto:")):
                continue
            if "#" in link:
                rel_target, anchor = link.split("#", 1)
            else:
                rel_target, anchor = link, None
            if rel_target and " " in rel_target:
                continue
            target = path if not rel_target else os.path.normpath(os.path.join(base, rel_target))
            if not os.path.exists(target):
                rel_disp = os.path.relpath(target, root).replace("\\", "/")
                errors.append(f"{rel}: broken link '{link}' (missing '{rel_disp}')")
            elif check_anchor and anchor and os.path.isfile(target) and target.endswith(".md"):
                if anchor not in get_anchors(target, anchor_cache):
                    rel_disp = os.path.relpath(target, root).replace("\\", "/")
                    errors.append(f"{rel}: broken anchor '{link}' (missing #{anchor} in '{rel_disp}')")
    return errors


def check_table_row_lengths(root):
    errors = []
    targets = [
        "README.md",
        "docs/OPERATIONS.md",
        "docs/UPSTREAM_CHANGES.md",
        "tools/g3cli/README.md",
    ]
    for pattern in ("docs/guides/*.md", "tools/g3cli/docs/*.md"):
        for p in sorted(glob.glob(os.path.join(root, pattern))):
            targets.append(os.path.relpath(p, root).replace("\\", "/"))
    for rel in targets:
        path = os.path.join(root, rel)
        if not os.path.isfile(path):
            continue
        in_code = False
        with open(path, encoding="utf-8") as fh:
            for idx, line in enumerate(fh, 1):
                if line.startswith("```"):
                    in_code = not in_code
                    continue
                if in_code:
                    continue
                stripped = line.rstrip("\r\n")
                if stripped.startswith("|") and len(stripped) > TABLE_LINE_LIMIT:
                    errors.append(
                        f"{rel}:{idx}: table row is {len(stripped)} chars (limit {TABLE_LINE_LIMIT}); move detail to a subsection"
                    )
    return errors


def check_index_completeness(root):
    errors = []

    def require_indexed(index_rel, expected_items):
        idx_path = os.path.join(root, index_rel)
        if not os.path.isfile(idx_path):
            return
        with open(idx_path, encoding="utf-8") as fh:
            txt = fh.read()
        for item in sorted(expected_items):
            if item not in txt:
                errors.append(f"{index_rel}: missing index entry for '{item}'")

    # Subdirectory README indexes listing sibling *.md files
    for subdir in ("docs/qa", "docs/sources", "docs/perf", "docs/research"):
        d = os.path.join(root, subdir)
        if os.path.isdir(d):
            items = [f for f in os.listdir(d) if f.endswith(".md") and f != "README.md"]
            require_indexed(f"{subdir}/README.md", items)

    # docs/design/README.md lists every file in docs/design/
    design_dir = os.path.join(root, "docs/design")
    if os.path.isdir(design_dir):
        items = [f for f in os.listdir(design_dir) if f != "README.md" and os.path.isfile(os.path.join(design_dir, f))]
        require_indexed("docs/design/README.md", items)

    # docs/archive/README.md lists every archived *.md under docs/archive/*/
    archive_dir = os.path.join(root, "docs/archive")
    if os.path.isdir(archive_dir):
        items = [
            os.path.relpath(p, archive_dir).replace("\\", "/")
            for p in glob.glob(os.path.join(archive_dir, "*", "*.md"))
        ]
        require_indexed("docs/archive/README.md", items)

    # docs/guides/*.md listed in docs/OPERATIONS.md (except upstream-history-and-tutorials.md, indexed in docs/README.md)
    guides_dir = os.path.join(root, "docs/guides")
    if os.path.isdir(guides_dir):
        guide_files = [f for f in os.listdir(guides_dir) if f.endswith(".md")]
        ops_guides = [f"guides/{f}" for f in guide_files if f != "upstream-history-and-tutorials.md"]
        require_indexed("docs/OPERATIONS.md", ops_guides)
        if "upstream-history-and-tutorials.md" in guide_files:
            require_indexed("docs/README.md", ["guides/upstream-history-and-tutorials.md"])

    # tools/g3cli/docs/*.md listed in tools/g3cli/README.md
    g3cli_docs = os.path.join(root, "tools/g3cli/docs")
    if os.path.isdir(g3cli_docs):
        items = [f"docs/{f}" for f in os.listdir(g3cli_docs) if f.endswith(".md")]
        require_indexed("tools/g3cli/README.md", items)

    # benchmarks/*/README.md listed in benchmarks/README.md
    bench_readmes = [
        os.path.basename(os.path.dirname(p)) + "/README.md"
        for p in glob.glob(os.path.join(root, "benchmarks", "*", "README.md"))
    ]
    require_indexed("benchmarks/README.md", bench_readmes)

    # tools/*/README.md listed in tools/README.md
    tool_readmes = [
        os.path.basename(os.path.dirname(p)) + "/README.md"
        for p in glob.glob(os.path.join(root, "tools", "*", "README.md"))
    ]
    require_indexed("tools/README.md", tool_readmes)

    return errors


def check_docs_root_allowlist(root):
    errors = []
    docs_dir = os.path.join(root, "docs")
    if not os.path.isdir(docs_dir):
        return errors
    for fn in sorted(os.listdir(docs_dir)):
        if fn.endswith(".md") and fn not in DOCS_ROOT_ALLOWLIST:
            errors.append(
                f"docs/{fn}: unexpected file in docs/ root; place new docs in docs/guides/, docs/design/, docs/qa/, docs/sources/, docs/perf/, docs/research/, or docs/archive/"
            )
    return errors


def run_checks(root):
    md_files = collect_markdown_files(root)
    errors = []
    errors.extend(check_conflict_markers(root, md_files))
    errors.extend(check_links(root, md_files))
    errors.extend(check_table_row_lengths(root))
    errors.extend(check_index_completeness(root))
    errors.extend(check_docs_root_allowlist(root))
    return md_files, errors


def main(argv=None):
    parser = argparse.ArgumentParser(description="Check Markdown documentation hygiene.")
    parser.add_argument("--root", default=".", help="Repository root directory (default: .)")
    args = parser.parse_args(argv)
    root = os.path.abspath(args.root)
    md_files, errors = run_checks(root)
    if errors:
        for err in errors:
            print(f"ERROR: {err}", file=sys.stderr)
        print(f"check_docs: {len(errors)} error(s) across {len(md_files)} markdown files.", file=sys.stderr)
        return 1
    print(f"check_docs: OK ({len(md_files)} markdown files checked).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
