"""Generate deterministic OBJ fixtures and per-case expectations with Python 3."""
import argparse
import json
import math
import struct
from fractions import Fraction
from pathlib import Path

QUADS = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
         (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
EPSILONS = (-2.0**-20, 0, 2.0**-20)


def box(lo=(0, 0, 0), hi=(1, 1, 1), *, alternate=False, omit=()):
    x, y, z = lo
    X, Y, Z = hi
    vertices = [(x, y, z), (X, y, z), (X, Y, z), (x, Y, z),
                (x, y, Z), (X, y, Z), (X, Y, Z), (x, Y, Z)]
    faces = []
    for index, (a, b, c, d) in enumerate(QUADS):
        if index in omit:
            continue
        faces.extend([(a, b, d), (b, c, d)] if alternate else [(a, b, c), (a, c, d)])
    return vertices, faces


def join(*meshes):
    vertices, faces = [], []
    for points, triangles in meshes:
        start = len(vertices)
        faces.extend(tuple(index + start for index in triangle) for triangle in triangles)
        vertices.extend(points)
    return vertices, faces


def tube(radius, segments=32):
    # Four rings: outer bottom/top, inner bottom/top.
    points = [(r * math.cos(2 * math.pi * i / segments),
               r * math.sin(2 * math.pi * i / segments), z)
              for r, z in [(0.5, 0), (0.5, 1), (radius, 0), (radius, 1)]
              for i in range(segments)]
    faces = []
    for i in range(segments):
        j = (i + 1) % segments
        n = segments
        for a, b, c, d in [(i, j, n+j, n+i), (2*n+j, 2*n+i, 3*n+i, 3*n+j),
                           (j, i, 2*n+i, 2*n+j), (n+i, n+j, 3*n+j, 3*n+i)]:
            faces.extend([(a, b, c), (a, c, d)])
    return points, faces


def write_obj(path, mesh):
    points, faces = mesh
    lines = ["v " + " ".join(format(value, ".17g") for value in point) for point in points]
    lines.extend("f " + " ".join(str(i + 1) for i in face) for face in faces)
    path.write_text("\n".join(lines) + "\n", encoding="ascii")


def write_glb(path):
    # One indexed cube, instantiated below a translated parent, once mirrored.
    points, faces = box()
    positions = b"".join(struct.pack("<3f", *point) for point in points)
    indices = b"".join(struct.pack("<3H", *face) for face in faces)
    binary = positions + indices
    scene = {
        "asset": {"version": "2.0"}, "scene": 0, "scenes": [{"nodes": [0]}],
        "nodes": [{"translation": [10, 0, 0], "children": [1, 2]},
                  {"mesh": 0}, {"mesh": 0, "translation": [3, 0, 0], "scale": [-1, 1, 1]}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1}]}],
        "buffers": [{"byteLength": len(binary)}],
        "bufferViews": [{"buffer": 0, "byteOffset": 0, "byteLength": len(positions), "target": 34962},
                        {"buffer": 0, "byteOffset": len(positions), "byteLength": len(indices), "target": 34963}],
        "accessors": [{"bufferView": 0, "componentType": 5126, "count": len(points), "type": "VEC3",
                       "min": [0, 0, 0], "max": [1, 1, 1]},
                      {"bufferView": 1, "componentType": 5123, "count": 3 * len(faces), "type": "SCALAR"}],
    }
    metadata = json.dumps(scene, separators=(",", ":")).encode("ascii")
    metadata += b" " * (-len(metadata) % 4)
    path.write_bytes(struct.pack("<4sII", b"glTF", 2, 28 + len(metadata) + len(binary)) +
                     struct.pack("<I4s", len(metadata), b"JSON") + metadata +
                     struct.pack("<I4s", len(binary), b"BIN\x00") + binary)


def generate(root):
    root.mkdir(parents=True, exist_ok=True)
    cases = []

    def record(name, suite, meshes, **expectations):
        inputs = {}
        for label, mesh in meshes.items():
            path = root / f"{name}-{label}.obj"
            write_obj(path, mesh)
            inputs[label] = path.name
        entry = {"id": name, "suite": suite, "inputs": inputs, **expectations}
        cases.append(entry)
        return entry

    def wrap(name, mesh, probe, *, swept=True):
        record(name, "wrap", {"input": mesh}, mandatory=["closed", "oriented_2_manifold",
               "self_intersection_free", "strictly_encloses_input"], measure=probe, sweep=swept)

    def repair(name, mesh, target, **expectations):
        record(name, "repair", {"input": mesh, **({"reference": target} if target else {})},
               **expectations)

    cube = box()
    points, faces = cube
    wrap("W01-sheet", (points[:4], faces[:2]), "positive_volume_and_two_sided_thickness")
    wrap("W02-open-box", box(omit=(1,)), "inside_outside_of_cavity_probe")
    for i, radius in enumerate((1/128, 1/32, 1/8), 1):
        wrap(f"W03-bore-{i}", tube(radius), {"bore_radius": radius, "probe": [0, 0, 0.5]})
    for i, gap in enumerate((1/256, 1/32, 1/4), 1):
        wrap(f"W04-gap-{i}", join(cube, box((1+gap, 0, 0), (2+gap, 1, 1))),
             {"gap": gap, "probe": [1+gap/2, 0.5, 0.5], "measure": "components"})
    wrap("W05-thin-wall", box((0, 0, 0), (1, 1, 1/1024)), "thickness_and_surface_error")
    wrap("W06-satellite", join(cube, box((2, 0, 0), (2+1/128, 1/128, 1/128))),
         "satellite_enclosure_and_main_part_error")
    wrap("W07-nested", join(cube, box((0.25,)*3, (0.75,)*3)),
         {"cavity_probe": [0.5, 0.5, 0.5]})
    wrap("W08-sliver-retessellation", (points + [(1/65536, 1/65536, 1)],
         faces[:2] + [(4, 5, 8), (5, 6, 8), (6, 7, 8), (7, 4, 8)] + faces[4:]),
         "compare_surface_error_against_W09_clean")
    wrap("W09-clean", cube, "baseline_surface_error", swept=False)
    wrap("W10-edge-contact", join(cube, box((1, 1, 0), (2, 2, 1))),
         "local_manifoldness_near_contact", swept=False)

    repair("R01-clean", cube, cube, check="preserve_geometry_and_topology")
    repair("R02-hole", box(omit=(1,)), cube, check="close_one_planar_boundary")
    for i, gap in enumerate((0, 2.0**-20, 2.0**-10), 1):
        seam = [(x, y, z+gap) for x, y, z in points[4:8]]
        repair(f"R03-seam-{i}", (points + seam, faces[:2] + [(8, 9, 10), (8, 10, 11)] + faces[4:]),
               cube, check="seam_closure_vs_weld_tolerance", gap=gap)
    repair("R04-winding", (points, faces[:2] + [faces[2][::-1]] + faces[3:]), cube,
           check="consistent_outward_orientation")
    repair("R05-duplicate", (points, faces + [faces[0], faces[1][::-1]]), cube, check="remove_duplicates")
    repair("R06-degenerate", (points + [(0.5, 0.5, 0)], faces + [(0, 0, 1), (0, 8, 2)]),
           cube, check="remove_zero_area_faces")
    repair("R07-fin", (points + [(0.5, -0.5, 0)], faces + [(0, 1, 8)]), cube,
           check="remove_single_face_fin_if_solid_mode")
    other, other_faces = box((1, 1, 1), (2, 2, 2))
    repair("R08-bow-tie-vertex", (points + other[1:], faces +
           [tuple(6 if i == 0 else 7+i for i in face) for face in other_faces]), None,
           check="vertex_link_and_declared_point_contact_policy")
    repair("R09-crossing-shells", join(cube, box((0.5, 0.25, 0.25), (1.5, 1.25, 1.25))), None,
           check="resolve_proper_intersections_if_supported", union_volume="55/32")
    repair("R10-coplanar-shells", join(cube, box((0.5, 0, 0), (1.5, 1, 1), alternate=True)),
           box((0, 0, 0), (1.5, 1, 1)), check="regularized_union_if_solid_union_mode")
    inside, inside_faces = box((0.25,)*3, (0.75,)*3)
    cavity = join(cube, (inside, [face[::-1] for face in inside_faces]))
    repair("R11-cavity", cavity, cavity, check="preserve_inner_shell", volume="7/8")
    repair("R12-two-solids", join(cube, box((2, 0, 0), (3, 1, 1))),
           join(cube, box((2, 0, 0), (3, 1, 1))), check="preserve_components")
    repair("R13-defect-interaction", (points, faces[:2] + [faces[4][::-1]] + faces[5:] +
           [faces[0], (0, 0, 1)]), cube, check="close_hole_fix_orientation_and_remove_degeneracies")
    t_point = (0.5, 0, 0)
    repair("R14-t-junction", (points + [t_point], [(0, 3, 2), (0, 2, 8), (8, 2, 1)] + faces[2:]),
           cube, check="split_unsplit_edge_or_equivalent_conforming_repair")
    repair("R15-sheet", (points[:4], faces[:2]), None, check="declared_zero_volume_input_policy")
    repair("R16-global-inversion", (points, [face[::-1] for face in faces]), cube,
           check="orient_closed_outer_shell_outwards")

    def boolean(name, lo, hi, *, a_lo=(0, 0, 0), a_hi=(1, 1, 1), alternate=False,
                contact="none", **metadata):
        bounds = [tuple(Fraction(x) for x in xyz) for xyz in (a_lo, a_hi, lo, hi)]
        L, H, l, h = bounds
        a = math.prod(H[i]-L[i] for i in range(3))
        b = math.prod(h[i]-l[i] for i in range(3))
        intersection = math.prod(max(0, min(H[i], h[i])-max(L[i], l[i])) for i in range(3))
        volumes = {"union": a+b-intersection, "intersection": intersection,
                   "difference_ab": a-intersection, "difference_ba": b-intersection,
                   "xor": a+b-2*intersection}
        record(name, "boolean", {"a": box(a_lo, a_hi), "b": box(lo, hi, alternate=alternate)},
               regularized_volumes={op: str(value) for op, value in volumes.items()},
               empty_operations=[op for op, value in volumes.items() if value == 0],
               reference_boxes=[[list(a_lo), list(a_hi)], [list(lo), list(hi)]],
               contact=contact, **metadata)

    boolean("B01-disjoint", (2, 0, 0), (3, 1, 1), contact="disjoint")
    boolean("B02-contained", (0.25,)*3, (0.75,)*3, contact="containment",
            check="difference_ab_has_inward_cavity_shell")
    boolean("B03-identical", (0, 0, 0), (1, 1, 1), contact="identical")
    boolean("B04-identical-other-diagonal", (0, 0, 0), (1, 1, 1), alternate=True,
            contact="identical")
    boolean("B05-face-contact", (1, 0, 0), (2, 1, 1), alternate=True, contact="face")
    boolean("B06-edge-contact", (1, 1, 0), (2, 2, 1), contact="edge")
    boolean("B07-point-contact", (1, 1, 1), (2, 2, 2), contact="point")
    boolean("B08-coplanar-overlap", (0.5, 0.25, 0), (1.5, 1.25, 1), alternate=True,
            contact="coplanar_and_volume_overlap")
    for i, delta in enumerate(EPSILONS, 1):
        boolean(f"B09-contact-transition-{i}", (1+delta, 0.25, 0.25),
                (2+delta, 0.75, 0.75), contact=("overlap", "partial_face", "gap")[i-1],
                signed_gap=delta)
    boolean("B10-almost-identical", (2.0**-20, 0, 0), (1+2.0**-20, 1, 1), alternate=True,
            contact="near_coincident")
    boolean("B11-tiny-tool", (0.5, 0.5, -0.5), (0.5+2.0**-16, 0.5+2.0**-16, 1.5),
            contact="through_cut", check="difference_ab_has_through_hole")
    for exponent in (-20, 20):
        scale = 2.0**exponent
        boolean(f"B12-scale-{exponent:+d}", (scale/2, scale/4, 0),
                (1.5*scale, 1.25*scale, scale), a_hi=(scale,)*3, alternate=True,
                contact="coplanar_and_volume_overlap", equivalent_to="B08-coplanar-overlap",
                scale=scale)
    shift = 2**24
    boolean("B13-far-origin", (shift+0.5, 0.25, 0), (shift+1.5, 1.25, 1),
            a_lo=(shift, 0, 0), a_hi=(shift+1, 1, 1), alternate=True,
            contact="coplanar_and_volume_overlap", equivalent_to="B08-coplanar-overlap",
            translation=[shift, 0, 0], check="float32_round_trip_can_lose_x_extent")
    tetrahedron = ([(0, 0, 0), (3, 0, 0), (0, 1, 0), (0, 0, 1)],
                   [(0, 2, 1), (0, 1, 3), (0, 3, 2), (1, 2, 3)])
    slab = box((1, -1, -1), (4, 2, 2))
    record("B14-rational-cut", "boolean", {"a": tetrahedron, "b": slab},
           regularized_volumes={"intersection": "4/27", "difference_ab": "19/54",
                                "union": "1477/54", "difference_ba": "725/27", "xor": "1469/54"},
           empty_operations=[], check="fractional_vertices_and_volume_before_and_after_export")
    record("C01-repaired-cutter", "pipeline", {"a": cube, "b": box((0.25, 0.25, -1),
           (0.75, 0.75, 2), omit=(1,)), "repair_reference_b": box((0.25, 0.25, -1), (0.75, 0.75, 2))},
           repair_then_difference_volume="3/4", check="through_hole_after_exact_reconstruction")
    record("C02-boolean-chain", "pipeline", {"a": cube, "b": box((0.25, 0.25, -1),
           (0.75, 0.75, 2)), "c": box((0.375, 0.375, 0), (0.625, 0.625, 1))},
           expressions=["(A-B)|C", "(A|C)-(B-C)"], regularized_volume="13/16",
           solid_components=2, check="compare_output_geometry_of_equivalent_trees")
    path = root / "I01-instanced-mirror.glb"
    write_glb(path)
    entry = record("I01-instanced-mirror", "import",
                   {"reference": join(box((10, 0, 0), (11, 1, 1)), box((12, 0, 0), (13, 1, 1)))},
                   expected_components=2, expected_faces=24, expected_bbox=[[10, 0, 0], [13, 1, 1]],
                   expected_outward_volume="2")
    entry["inputs"]["glb"] = path.name

    (root / "manifest.json").write_text(json.dumps({"version": 1, "cases": cases}, indent=2) + "\n",
                                        encoding="utf-8")
    return len(cases)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="directory for the fixtures and manifest")
    args = parser.parse_args()
    print(f"{generate(args.output)} cases in {args.output}")
