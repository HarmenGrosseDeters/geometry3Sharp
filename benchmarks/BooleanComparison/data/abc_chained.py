# The abc-chained data class: chained booleans on models of the ABC dataset (Koch et al., CVPR 2019) placed with the exact
# contact transforms of thingi-contact (README.md, "Chained booleans on ABC models"). No model, mesh or derived mesh is part
# of the repository: the workflow `.github/workflows/abc-chained.yml` downloads one chunk of the dataset on a GitHub-hosted
# runner, extracts a seeded sample of its files, filters them, runs the chains and keeps only result rows (and model ids).
#
#   python3 data/abc_chained.py select <listing.txt> <out.txt> [--count N] [--seed S]   candidates from `7z l -slt` output
#   python3 data/abc_chained.py prepare <dir> <out.csv>     weld the vertices of <dir>/meshes/*.obj by position (exact first, then a tolerance if too few
#                                                           models close), write them as OFF and filter them: id,triangles,reason (<out.csv>.clean: the clean ones)
#   python3 data/abc_chained.py filter <meshes-dir> <out.csv>   NotCleanReason of every mesh of the directory -> id,triangles,reason
#   python3 data/abc_chained.py generate <clean.csv> <out.csv> [--chains N]   the chain list from the clean models (no meshes)
#   python3 data/abc_chained.py manifest <dir> <chains.csv> <out.jsonl> [--limit N] [--variants v1,v2]
#   python3 data/abc_chained.py run <manifest.jsonl> <out.jsonl> [--methods m1,m2] [--timeout S] [--budget S]   the chains
#                                                           (--budget: no new chain is started after S seconds; finished chains are kept)
#   python3 data/abc_chained.py summary results.jsonl...    per variant and method
#
# Everything but `run` and `filter` is a pure function of its input files and the seeds (SplitMix64 of thingi_pairs.py).
import csv, json, math, os, re, shutil, struct, subprocess, sys, tempfile, time
import statistics as st

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import thingi_pairs as tp   # noqa: E402
import thingi_contact as tc   # noqa: E402

CLASS = "abc-chained"
SEED_SELECT, SEED_CHAINS = 20261116, 20261117
OPS = ["union", "intersection", "difference"]
# a chain: (operation, left, right); "A", "B", "C" are the placed inputs, "R" the result of the previous step of the same method
PLANS = {
    "ucd": [("union", "A", "B"), ("difference", "R", "C"), ("intersection", "R", "A")],
    "dui": [("difference", "A", "B"), ("union", "R", "C"), ("intersection", "R", "A")],
    "iud": [("intersection", "A", "B"), ("union", "R", "C"), ("difference", "R", "A")],
}
VARIANTS = list(PLANS)
# the placements allowed per variant: iud starts with an intersection, so B must overlap A (an empty first result ends the chain)
KINDS_B = {"ucd": ["face-contact", "partial-overlap"], "dui": ["face-contact", "partial-overlap"], "iud": ["partial-overlap"]}
KINDS_C = ["face-contact", "partial-overlap", "ulp-shift", "mirror"]        # the last two use a copy of A: c == a
CHAINS_PER_VARIANT = 100
SELECT_BYTES = (60_000, 3_000_000)     # file size of a candidate obj: about 500-30000 triangles with normals
CLEAN_TRIS = (500, 30000)
HEADER = ["pair", "variant", "a", "b", "c", "kb", "axb", "sgb", "pb", "kc", "axc", "sgc", "pc"]

# --- select: candidates from the listing of the archive

def parse_listing(text):
    """(path, size) of every file of the output of `7z l -slt <archive>`"""
    out, cur = [], {}
    for line in text.splitlines() + [""]:
        if line.strip() == "":
            if "Path" in cur and "Size" in cur and cur.get("Folder", "-") != "+" and cur["Size"].isdigit():
                out.append((cur["Path"], int(cur["Size"])))
            cur = {}
        elif " = " in line:
            k, _, v = line.partition(" = ")
            cur[k.strip()] = v.strip()
    return out

def model_id(path):
    return os.path.basename(path).split(".")[0]

def select_candidates(files, count, seed, size_range=SELECT_BYTES):
    """count obj files of the listing in a size range, chosen by a partial Fisher-Yates shuffle of the sorted paths (seeded)"""
    pool = sorted(p for p, n in files if p.lower().endswith(".obj") and size_range[0] <= n <= size_range[1])
    rng = tp.SplitMix64(seed)
    for i in range(min(count, len(pool))):
        j = i + rng.below(len(pool) - i)
        pool[i], pool[j] = pool[j], pool[i]
    return sorted(pool[:count])

# --- filter

def cmd_filter(meshes_dir, out):
    """thingi-filter of the tool on all meshes of a directory: id,triangles,reason (reason: NotCleanReason, '' = clean; positive volume)"""
    meshes = sorted(os.path.join(meshes_dir, f) for f in os.listdir(meshes_dir))
    if os.path.exists(out):
        os.remove(out)
    for i in range(0, len(meshes), 100):
        subprocess.run(tp.tool() + ["thingi-filter", out] + meshes[i:i + 100], check=True)
    rows = [l.rstrip("\n").split(",", 2) for l in open(out)]
    clean = sorted(((k, int(n)) for k, n, r in rows if r == ""))
    with open(out + ".clean", "w") as f:
        f.write("id,tris\n" + "".join(f"{k},{n}\n" for k, n in clean))
    reasons = {}
    for _, _, r in rows:
        reasons[r or "clean"] = reasons.get(r or "clean", 0) + 1
    print("NotCleanReason of the candidates:", reasons, flush=True)
    return reasons, clean

# --- prepare: the obj files of the ABC dataset are tessellated per face of the CAD model, so adjacent faces have their own copies of the
# vertices on a shared edge: read as they are, no mesh is closed. The vertices are welded by position (exactly equal doubles first; a tolerance
# only if that leaves too few clean models) before the filter; a tolerance weld moves a vertex by at most tol x the bounding box diagonal.
# The coordinates are first snapped to single precision (`snap`): the exact placements need the headroom of single precision values.

WELD_TOLERANCES = [0.0, 1e-9, 1e-7, 1e-5]       # relative to the bounding box diagonal; the first one with enough clean models is used
MIN_CLEAN = 40

def read_obj(path):
    """(vertices, triangles) of an obj file: 'v' and 'f' lines only (normals, texture coordinates, groups, colours after the coordinates and
    trailing comments are ignored); polygons are fans. ValueError on a line that cannot be read or an index outside the vertices that exist so far."""
    verts, tris = [], []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            tok = line.split("#")[0].split()
            if not tok:
                continue
            if tok[0] == "v":
                if len(tok) < 4:
                    raise ValueError(f"vertex line with {len(tok) - 1} coordinates")
                verts.append((float(tok[1]), float(tok[2]), float(tok[3])))
            elif tok[0] == "f":
                idx = []
                for t in tok[1:]:
                    i = int(t.split("/")[0])
                    if i == 0 or not -len(verts) <= i <= len(verts):
                        raise ValueError(f"face index {i} with {len(verts)} vertices")
                    idx.append(i - 1 if i > 0 else len(verts) + i)
                for k in range(1, len(idx) - 1):
                    tris.append((idx[0], idx[k], idx[k + 1]))
    return verts, tris

def weld(verts, tris, tol):
    """vertices at the same position become one. tol 0: bit-equal doubles. tol > 0: a vertex joins the first earlier representative within
    distance h = tol x bounding box diagonal (looked up in the 27 cells of a grid of h around it, so that vertices on both sides of a cell
    boundary meet); otherwise it is a representative itself. A vertex moves by at most h (to the first vertex seen, order dependent; a chain of
    close vertices is not merged transitively). Triangles are kept, also the ones that collapse: the filter reports them."""
    out, remap = [], []
    if tol > 0 and verts:
        lo = [min(v[k] for v in verts) for k in range(3)]
        hi = [max(v[k] for v in verts) for k in range(3)]
        h = tol * max(sum((hi[k] - lo[k]) ** 2 for k in range(3)) ** 0.5, 1e-300)
        cells = {}
        for v in verts:
            c = (math.floor(v[0] / h), math.floor(v[1] / h), math.floor(v[2] / h))
            found = None
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    for dz in (-1, 0, 1):
                        for r in cells.get((c[0] + dx, c[1] + dy, c[2] + dz), ()):
                            w = out[r]
                            if (v[0] - w[0]) ** 2 + (v[1] - w[1]) ** 2 + (v[2] - w[2]) ** 2 <= h * h and (found is None or r < found):
                                found = r
            if found is None:
                found = len(out)
                out.append(v)
                cells.setdefault(c, []).append(found)
            remap.append(found)
    else:
        ids = {}
        for v in verts:
            if v not in ids:
                ids[v] = len(out)
                out.append(v)
            remap.append(ids[v])
    return out, [(remap[a], remap[b], remap[c]) for a, b, c in tris]

SNAP_TINY_EXPONENT = 26

def snap(verts, tiny_exponent=SNAP_TINY_EXPONENT):
    """The model as the benchmark uses it: coordinates rounded to single precision (the precision of STL files: a single precision value has 29 spare bits
    in a double, which is what makes a translation by a shift of the size of the model exact; ABC's text coordinates are full 53-bit doubles, and the first run
    refused 89 of 90 placements as inexact), and nonzero coordinates with |c| below 2^-26 of the largest extent of the bounding box set to 0: after the
    power-of-two scale to a longest side in [0.5, 1) such a coordinate is at least 2^-27 and has its lowest bit at 2^-50 or above, so a sum with a result
    below 2 stays exact (two bits of slack; at 2^-28 there would be none). The rounding changes a coordinate by at most 6e-8 of its own size, the flush by
    less than 1.5e-8 of the extent. Non-finite coordinates are an error (ValueError), a value outside the single precision range too (OverflowError)."""
    f32 = lambda x: struct.unpack("f", struct.pack("f", x))[0]
    if not all(math.isfinite(c) for v in verts for c in v):
        raise ValueError("non-finite coordinate")
    out = [(f32(x), f32(y), f32(z)) for x, y, z in verts]
    if out:
        extent = max(max(v[k] for v in out) - min(v[k] for v in out) for k in range(3))
        tiny = extent * 2.0 ** -tiny_exponent
        out = [tuple(0.0 if c != 0 and abs(c) < tiny else c for c in v) for v in out]
    return out

def snap_distortion(verts, snapped):
    """the largest change of a coordinate by snap, relative to the largest extent of the bounding box of the model (0 for an empty or flat-point model)"""
    if not verts:
        return 0.0
    extent = max(max(v[k] for v in verts) - min(v[k] for v in verts) for k in range(3))
    return max((abs(a - b) for v, w in zip(verts, snapped) for a, b in zip(v, w)), default=0.0) / extent if extent > 0 else 0.0

def write_off(path, verts, tris):
    with open(path, "w") as f:
        f.write(f"OFF\n{len(verts)} {len(tris)} 0\n")
        f.writelines(f"{x!r} {y!r} {z!r}\n" for x, y, z in verts)
        f.writelines(f"3 {a} {b} {c}\n" for a, b, c in tris)

def cmd_prepare(d, out, tolerances=None, min_clean=MIN_CLEAN):
    """welds and filters <d>/meshes/*.obj with the first tolerance that gives at least min_clean clean models in the triangle range (else the
    one with the most, the earlier on a tie); leaves the welded meshes as <d>/meshes/<id>.off (the obj files are removed), <out>(.clean) and
    <d>/weld_tol.txt. An obj file that cannot be read is skipped and listed in <d>/unreadable.txt (the rest goes on). The files are read
    again for every tolerance (a mesh of 3 MB takes about 10 MB as Python tuples, 400 of them would be 4 GB)."""
    tolerances = WELD_TOLERANCES if tolerances is None else tolerances
    mdir = os.path.join(d, "meshes")
    objs = sorted(f for f in os.listdir(mdir) if f.endswith(".obj"))
    if not objs:
        raise SystemExit(f"no obj files in {mdir}")
    unreadable, distortion = {}, {}
    best = None
    for tol in tolerances:
        tdir = os.path.join(d, f"welded_{tol:g}")
        os.makedirs(tdir, exist_ok=True)
        for f in objs:
            k = f[:-4]
            if k in unreadable:
                continue
            try:
                v, t = read_obj(os.path.join(mdir, f))
                sv = snap(v)
                distortion[k] = snap_distortion(v, sv)
                write_off(os.path.join(tdir, k + ".off"), *weld(sv, t, tol))
            except (ValueError, IndexError, OverflowError) as ex:
                unreadable[k] = str(ex)
        print(f"weld tolerance {tol:g} (x bounding box diagonal), {len(unreadable)} unreadable files, largest snap change {max(distortion.values(), default=0):.2e} of a model's extent:", flush=True)
        reasons, clean = cmd_filter(tdir, f"{out}.{tol:g}")
        n = sum(1 for _, t in clean if CLEAN_TRIS[0] <= t <= CLEAN_TRIS[1])
        print(f"  {n} clean models with {CLEAN_TRIS[0]}-{CLEAN_TRIS[1]} triangles", flush=True)
        if best is None or n > best[1]:
            best = (tol, n)
        if n >= min_clean:
            best = (tol, n)
            break
        for t in tolerances:                                  # the disk: keep only the welded meshes of the best tolerance so far
            if t != best[0]:
                shutil.rmtree(os.path.join(d, f"welded_{t:g}"), ignore_errors=True)
    tol, n = best
    for f in os.listdir(mdir):
        os.remove(os.path.join(mdir, f))
    for f in os.listdir(os.path.join(d, f"welded_{tol:g}")):
        shutil.move(os.path.join(d, f"welded_{tol:g}", f), os.path.join(mdir, f))
    for t in tolerances:
        shutil.rmtree(os.path.join(d, f"welded_{t:g}"), ignore_errors=True)
        if t != tol:
            for suffix in ("", ".clean"):
                if os.path.exists(f"{out}.{t:g}{suffix}"):
                    os.remove(f"{out}.{t:g}{suffix}")
    shutil.move(f"{out}.{tol:g}", out)
    shutil.move(f"{out}.{tol:g}.clean", out + ".clean")
    with open(os.path.join(d, "weld_tol.txt"), "w") as f:
        f.write(f"{tol:g}\n")
    with open(os.path.join(d, "unreadable.txt"), "w") as f:
        f.write("".join(f"{k}: {v}\n" for k, v in sorted(unreadable.items())))
    print(f"weld tolerance used: {tol:g}, {n} clean models in range, {len(unreadable)} unreadable files", flush=True)
    return tol, n

def read_clean(path):
    with open(path) as f:
        return [(r["id"], int(r["tris"])) for r in csv.DictReader(l for l in f if not l.startswith("#"))]

# --- generate

def chain_rows(models, variant, count, seed=SEED_CHAINS):
    """count rows of a variant: three different models (a, b, c), the placement of B and of C against A. Independent of count (prefix)."""
    rng = tp.SplitMix64(seed + 7919 * VARIANTS.index(variant))
    rows = []
    for i in range(count):
        a = models[rng.below(len(models))]
        b = a
        while b == a:
            b = models[rng.below(len(models))]
        c = a
        while c in (a, b):
            c = models[rng.below(len(models))]
        kb = KINDS_B[variant][rng.below(len(KINDS_B[variant]))]
        axb, sgb, pb = rng.below(3), 1 if rng.below(2) == 0 else -1, rng.below(tc.PARAMS[kb])
        kc = KINDS_C[rng.below(len(KINDS_C))]
        axc, sgc, pc = rng.below(3), 1 if rng.below(2) == 0 else -1, rng.below(tc.PARAMS[kc])
        if kc in tc.SAME_MODEL:
            c = a
        rows.append([f"{i:04d}", variant, a, b, c, kb, axb, sgb, pb, kc, axc, sgc, pc])
    return rows

def generate(clean, out, chains=CHAINS_PER_VARIANT):
    lo, hi = CLEAN_TRIS
    pool = sorted(k for k, n in clean if lo <= n <= hi)
    if len(pool) < 3:
        raise SystemExit(f"only {len(pool)} clean models with {lo}-{hi} triangles")
    rows = [r for v in VARIANTS for r in chain_rows(pool, v, chains)]
    with open(out, "w", newline="") as f:
        f.write(f"# abc-chained: {chains} chains per variant, SplitMix64 seed {SEED_CHAINS} (+7919 x variant index), from {len(pool)} clean models with {lo}-{hi} triangles\n")
        w = csv.writer(f, lineterminator="\n")
        w.writerow(HEADER)
        w.writerows(rows)

def read_chains(path):
    with open(path) as f:
        return list(csv.DictReader(l for l in f if not l.startswith("#")))

def mesh_file(d, k):
    for e in (".obj", ".off", ".stl", ".ply"):
        p = os.path.join(d, "meshes", k + e)
        if os.path.exists(p):
            return p
    raise FileNotFoundError(k)

def cmd_manifest(d, chains, out, limit=None, variants=None):
    rows = tc.select(read_chains(chains), limit, variants)
    with open(out, "w") as f:
        for r in rows:
            f.write(json.dumps({"class": CLASS, "name": f"{r['variant']}_p{r['pair']}_{r['a']}+{r['b']}+{r['c']}", "variant": r["variant"],
                                "chain": [mesh_file(d, r["a"]), mesh_file(d, r["b"]), mesh_file(d, r["c"]),
                                          r["kb"], r["axb"], r["sgb"], r["pb"], r["kc"], r["axc"], r["sgc"], r["pc"]]}) + "\n")
    print(len(rows), "chains ->", out)

# --- run

def off_triangles(path):
    """triangle count of an OFF file (-1 if there is none / unreadable)"""
    try:
        with open(path) as f:
            first = f.readline().strip()
            counts = f.readline().split() if first == "OFF" else first[3:].split()
            return int(counts[1])
    except (OSError, ValueError, IndexError):
        return -1

def last_json(text):
    for line in reversed(text.strip().splitlines()):
        if line.startswith("{"):
            return json.loads(line)
    return {"error": "no output"}

def run_method(m, case, d0, plan, files, timeout, rows, info):
    """the steps of one method on its own results; rows are appended as they are produced (an exception keeps the earlier ones)"""
    import compare
    prev = None
    for k, (op, left, right) in enumerate(plan):
        dk = os.path.join(d0, f"{m}_{k}")
        lf, rf = (prev if left == "R" else files[left]), (prev if right == "R" else files[right])
        s = subprocess.run(compare.tool + ["chain-step", lf, rf, dk, f"{case['name']}_s{k}"], capture_output=True, text=True, timeout=3600)
        sinfo = last_json(s.stdout)
        if "error" in sinfo:
            rows.append({"class": CLASS, "file": case["name"], "method": m, "step": k, "op": op, "error": sinfo["error"]})
            return
        # the chosen operation first: if it fails, the other two (only needed for the identities of the step) are not run
        runs = {m: {op: compare.run_one(m, op, dk, timeout)}}
        ops = [op]
        if runs[m][op].get("status") == "ok":
            for o in OPS:
                if o != op:
                    runs[m][o] = compare.run_one(m, o, dk, timeout)
            ops = OPS
        row = compare.evaluate(dk, [m], {"class": CLASS, "name": case["name"]}, sinfo, runs, ops)[0]
        row.update({"step": k, "op": op, "variant": case["variant"], "chain_inputs": info})
        out = os.path.join(dk, f"{m}_{op}.off")
        row["result_tris"] = off_triangles(out)
        rows.append(row)
        if row["result_tris"] <= 0:       # no result (failed, rejected) or an empty one: nothing to chain on
            return
        prev = out

def run_chain(case, methods, timeout):
    """one chain: B and C placed against A (abc-chain-inputs), then the steps of the variant's plan for each method on its own
    results (the result of a step is the input of the next one, rounded as it is). Rows per step and method: the step's operations
    as in compare.py (identities of the step on the method's own inputs), `step`, `op` and `input.inputs_clean`. A runner failure
    of one method (an exception, a hung tool) is a row `runner_error` of that method; the rows of the other methods are kept."""
    import compare
    d0 = tempfile.mkdtemp(prefix="abcchain_")
    try:
        try:
            e = subprocess.run(compare.tool + ["abc-chain-inputs"] + case["chain"][:3] + [d0] + [str(x) for x in case["chain"][3:]],
                               capture_output=True, text=True, timeout=3600)
            info = last_json(e.stdout)
            if "error" in info:
                raise RuntimeError(info["error"])
        except Exception as ex:
            return [{"class": CLASS, "file": case["name"], "method": "export", "error": repr(ex)}]
        plan = PLANS[case["variant"]]
        rows = []
        for m in methods:
            files = {k: os.path.join(d0, k + ".off") for k in "ABC"}
            try:
                run_method(m, case, d0, plan, files, timeout, rows, info)
            except Exception as ex:
                rows.append({"class": CLASS, "file": case["name"], "method": m, "runner_error": repr(ex)})
        return rows
    finally:
        shutil.rmtree(d0, ignore_errors=True)

def cmd_run(manifest, out, methods, timeout, budget_s=None):
    cases = [json.loads(l) for l in open(manifest) if l.strip()]
    done = set()
    if os.path.exists(out):
        with open(out, "rb+") as f:
            data = f.read()
            if data and not data.endswith(b"\n"):
                f.write(b"\n")
        for l in open(out):
            try:
                r = json.loads(l)
                done.add(r["file"])
            except ValueError:
                pass
    todo = [c for c in cases if c["name"] not in done]
    print(f"{len(cases) - len(todo)} chains finished, {len(todo)} to run", flush=True)
    t0 = time.monotonic()
    with open(out, "a") as f:
        for n, c in enumerate(todo):
            if budget_s is not None and time.monotonic() - t0 > budget_s:      # the job's time limit would end the run without a report
                print(f"time budget of {budget_s} s used: {len(todo) - n} chains not run", flush=True)
                break
            for r in run_chain(c, methods, timeout):
                f.write(json.dumps(r) + "\n")
            f.flush()
            print(c["name"], flush=True)

# --- summary

def removed(r, op):
    """components that the rounding stage of ours removed from the result of op (None: not recorded, or another method); the field
    `<op>_removed_components` of compare.py, or the `counts` of `BooleanComparison run`"""
    v = r.get(f"{op}_removed_components")
    if v is None:
        v = (r.get(f"{op}_counts") or {}).get("rounding_removed_components")
    return v

def step_clean(r):
    reason = r.get(f"{r['op']}_clean_reason")
    return reason in ("", "empty")

def slash(vals):
    return "/".join(str(v) for v in vals)

def cmd_summary(files):
    rows = []
    for f in files:
        with open(f) as fh:
            rows += [json.loads(l) for l in fh if l.strip()]
    steps, refused, runner = {}, {}, {}
    for r in rows:
        if r.get("method") == "export":
            refused[r["file"]] = r.get("error", "")
        elif "runner_error" in r:
            runner[(r["file"], r["method"])] = r["runner_error"]
        elif "step" in r:
            steps[(r["file"], r["method"], r["step"])] = r       # the last row of a step wins
    chains = {}
    for (name, m, k), r in steps.items():
        chains.setdefault((r.get("variant") or name.split("_p")[0], m), {}).setdefault(name, {})[k] = r
    for (name, m) in runner:                                    # a chain that only has a runner error still counts
        chains.setdefault((name.split("_p")[0], m), {}).setdefault(name, {})
    n_steps = max((len(p) for p in PLANS.values()), default=3)
    print("| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |")
    print("|" + "---|" * 11)
    for (variant, m), by_chain in sorted(chains.items(), key=lambda kv: (VARIANTS.index(kv[0][0]) if kv[0][0] in VARIANTS else 99, kv[0][1])):
        ran = [0] * n_steps; clean = [0] * n_steps; err = [0.0] * n_steps; ms = [[] for _ in range(n_steps)]
        reasons, removed_total, all_clean, early_empty, early_failed = {}, 0, 0, 0, 0
        for name, ks in by_chain.items():
            for k, r in ks.items():
                if k >= n_steps or "op" not in r:
                    continue
                ran[k] += 1
                if "error" in r:
                    reasons[r["error"][:30]] = reasons.get(r["error"][:30], 0) + 1
                    continue
                op = r["op"]
                ok = r.get(f"{op}_status") == "ok"
                if not ok:
                    key = r.get(f"{op}_status") or "failed"
                    reasons[key] = reasons.get(key, 0) + 1
                elif step_clean(r):
                    clean[k] += 1
                    ms[k].append(r.get(f"{op}_ms") or 0)           # only results of a finished operation: a timeout is not a fast run
                else:
                    reasons[r.get(f"{op}_clean_reason")] = reasons.get(r.get(f"{op}_clean_reason"), 0) + 1
                    ms[k].append(r.get(f"{op}_ms") or 0)
                err[k] = max([err[k]] + [r[e] for e in ("ie_error", "diff_error") if r.get(e) is not None])
                removed_total += removed(r, op) or 0
            last = ks[max(ks)] if ks else None
            done = len(ks) == n_steps and all("op" in r and "error" not in r and r.get(f"{r['op']}_status") == "ok" and step_clean(r) for r in ks.values())
            if done:
                all_clean += 1
            if len(ks) < n_steps or (name, m) in runner:
                # the chain stopped before its last step: an empty result of a finished operation is expected, anything else is a failure
                if last is not None and (name, m) not in runner and last.get(f"{last['op']}_status") == "ok" and last.get("result_tris") == 0:
                    early_empty += 1
                else:
                    early_failed += 1
        print(f"| {variant} | {m} | {len(by_chain)} | {slash(ran)} | {slash(clean)} | {all_clean} | {early_empty} / {early_failed} | "
              f"{', '.join(f'{k} {v}' for k, v in sorted(reasons.items(), key=lambda kv: str(kv[0]))) or '–'} | {slash(f'{e:.1e}' for e in err)} | "
              f"{removed_total if m == 'ours' else '–'} | {slash(f'{st.median(x):.0f}' if x else '–' for x in ms)} |")
    if refused:
        print(f"\nChains refused (inexact placement) or failed to load their inputs: {len(refused)}:")
        for k, v in sorted(refused.items()):
            print(f"- {k}: {v}")
    if runner:
        print(f"\nRunner errors (an exception or a hung tool of the driver, not a result of the method): {len(runner)}:")
        for (k, m), v in sorted(runner.items()):
            print(f"- {k} {m}: {v}")

if __name__ == "__main__":
    a = sys.argv[1:]
    def opt(name, default=None, cast=str):
        return cast(a[a.index(name) + 1]) if name in a else default
    if a[:1] == ["select"]:
        files = parse_listing(open(a[1]).read())
        sel = select_candidates(files, opt("--count", 400, int), opt("--seed", SEED_SELECT, int))
        open(a[2], "w").write("".join(p + "\n" for p in sel))
        print(len(sel), "candidates of", len(files), "files")
    elif a[:1] == ["filter"]: cmd_filter(a[1], a[2])
    elif a[:1] == ["prepare"]: cmd_prepare(a[1], a[2])
    elif a[:1] == ["generate"]: generate(read_clean(a[1]), a[2], opt("--chains", CHAINS_PER_VARIANT, int))
    elif a[:1] == ["manifest"]: cmd_manifest(a[1], a[2], a[3], opt("--limit", None, int), opt("--variants", None, lambda s: s.split(",")))
    elif a[:1] == ["run"]: cmd_run(a[1], a[2], opt("--methods", "ours,manifold,cgal_epeck", lambda s: s.split(",")), opt("--timeout", 300, int), opt("--budget", None, int))
    elif a[:1] == ["summary"]: cmd_summary(a[1:])
    else: sys.exit(__doc__)
