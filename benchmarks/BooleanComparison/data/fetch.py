# Downloads the test meshes of a data class at a fixed commit and writes a case manifest for compare.py.
# Usage: python3 fetch.py <cgal|cherchi> <target dir>        -> <target dir>/cases.jsonl
# Nothing of this is part of the repository: the meshes are only fetched to run the comparison (licenses in
# docs/SOURCES.md). Needs git with network access to github.com.
import json, os, subprocess, sys

SOURCES = {
    # CGAL test and example data (Data/data/meshes), tag v6.0.1. The data comes without a license of its own;
    # CGAL's code is GPL / LGPL. Only downloaded and used by the external comparison.
    "cgal": ("https://github.com/CGAL/cgal", "50cfbde3b84dbeae8338268db2d78fe4fcb522de", "Data/data/meshes"),
    # Cherchi et al., Interactive and Robust Mesh Booleans (MIT), test data of the repository
    "cherchi": ("https://github.com/gcherchi/InteractiveAndRobustMeshBooleans", "7bd6c2697695f555f9a15d7e154e084d841316f0", "data"),
}

# CGAL meshes with at least ~160 triangles (closed and open ones, some with degeneracies), without the shuffled
# duplicates: each with a rotated, shifted copy, as the Thingi10K inputs
CGAL_MESHES = """
ALSTOM_TEST4 ChineseDragon-10kv am anchor anchor_dense armadillo b9_mesh bear blade blob blob-closed blobby
blobby_3cc boeing bones bull bunny00 cactus camel cheese couplingdown cow cube-meshed cylinder
cylinder_locally_refined degenerated-sd diplodocus dino double-torus-3-holes double-torus-example dragknob eight
elephant elephant-with-holes elk ellipe0.003 ellipsoid fandisk fandisk_large femur geosphere hand handle head helmet
holes homer horizons itemb joint knot knot1 knot2 larger_sphere lion lion-head man mannequin-devil mask_cone
mech-holes-shark mesh_with_border mpi_triang mushroom nefertiti oblong part patch-01 patch-13 patch-20 patch-21
patch-23 patch-30 pig pinion pipe plane poly2x^2+y^2-0.062500 refined_elephant retinal rotor rotor_small sphere
sphere966 spool three_peaks triceratops turbine u u_arch
""".split()
# pairs as given: the inputs of CGAL's corefinement examples
CGAL_PAIRS = [("blobby", "eight")]

# Cherchi et al.: the pairs of the demos (bunny with cow at four resolutions, the two spheres), the stencil of the
# paper (fertility minus 703 spheres, as one mesh), and meshes with a rotated, shifted copy
CHERCHI_PAIRS = [("bunny25k.obj", "cow25k.obj"), ("bunny50k.obj", "cow50k.obj"), ("bunny100k.obj", "cow100K.obj"),
                 ("bunny200k.obj", "cow200K.obj"), ("bunny.obj", "cow.obj"), ("sphere1.obj", "sphere2.obj"),
                 ("cube.obj", "armadillo.obj"), ("fertility.obj", "spheres.obj")]
CHERCHI_MESHES = ["armadillo.obj", "fertility.obj", "cactus.off", "cactus200k.obj", "bunny200k.obj"]

def fetch(cls, target):
    url, commit, path = SOURCES[cls]
    repo = os.path.join(target, "repo")
    if not os.path.isdir(repo):
        subprocess.run(["git", "init", "-q", repo], check=True)
        subprocess.run(["git", "-C", repo, "remote", "add", "origin", url], check=True)
        subprocess.run(["git", "-C", repo, "sparse-checkout", "set", path], check=True)
    subprocess.run(["git", "-C", repo, "fetch", "-q", "--depth", "1", "--filter=blob:none", "origin", commit], check=True)
    subprocess.run(["git", "-C", repo, "checkout", "-q", commit], check=True)
    return os.path.abspath(os.path.join(repo, path))

def main():
    cls, target = sys.argv[1], sys.argv[2]
    os.makedirs(target, exist_ok=True)
    d = fetch(cls, target)
    cases = []
    if cls == "cgal":
        cases += [{"class": cls, "name": m, "mesh": os.path.join(d, m + ".off")} for m in CGAL_MESHES]
        cases += [{"class": cls, "name": f"{a}+{b}", "a": os.path.join(d, a + ".off"), "b": os.path.join(d, b + ".off")} for a, b in CGAL_PAIRS]
    else:
        cases += [{"class": cls, "name": f"{a}+{b}", "a": os.path.join(d, a), "b": os.path.join(d, b)} for a, b in CHERCHI_PAIRS]
        cases += [{"class": cls, "name": m, "mesh": os.path.join(d, m)} for m in CHERCHI_MESHES]
    missing = [c["name"] for c in cases if not all(os.path.exists(c[k]) for k in ("mesh", "a", "b") if k in c)]
    if missing:
        sys.exit("missing meshes: " + ", ".join(missing))
    with open(os.path.join(target, "cases.jsonl"), "w") as f:
        f.writelines(json.dumps(c) + "\n" for c in cases)
    print(f"{len(cases)} cases in {os.path.join(target, 'cases.jsonl')}")

if __name__ == "__main__":
    main()
