"""The "repaired" criterion of the analysis scripts (analyze_fidelity, analyze_paths, analyze_wrap), in one place
so they count the same meshes. A mesh is repaired if it is closed, valid, topologically clean (no bowties,
consistent orientation, no duplicate/degenerate triangles, no inside-out components), has no exact
self-intersections (checked: results above 300k triangles are not checked and do not count) and stays close to
the input: certified Hausdorff upper bound < HD (relative to the input diagonal) in both directions and volume
IoU > IOU (inputs without a solid pass the IoU; a missing IoU fails)."""

HD, IOU = 0.01, 0.99


def get(r, k, d=None):
    v = r.get(k, d)
    return d if v is None else v


def clean(r):
    return (r.get('status') == 'closed' and get(r, 'after_valid', False) and get(r, 'after_bowties', 1) == 0
            and get(r, 'after_inconsistent_orient_edges', 1) == 0 and get(r, 'after_duplicate_tris', 1) == 0
            and get(r, 'after_degenerate_tris', 1) == 0 and get(r, 'after_inverted_components', 1) == 0)


def selfint(r):
    """exact self-intersecting pairs of the result; -1 if not computed (more than 300k triangles)"""
    return get(r, 'after_selfint_exact', 1)


def no_solid(r):
    """the input encloses no volume (IoU not defined): "NaN", or IoU 0 with nothing missing (older runs)"""
    v = r.get('iou')
    return v == 'NaN' or (v == 0 and get(r, 'iou_missing', 1) == 0)


def iou(r):
    """1 for inputs without a solid, 0 if the IoU is missing (not computed: fails the criterion)"""
    if no_solid(r):
        return 1.0
    v = r.get('iou')
    try:
        v = float(v)
    except (TypeError, ValueError):
        return 0.0
    return 0.0 if v != v else v


def hd(r):
    """the larger certified Hausdorff upper bound of both directions, relative; 1 if missing"""
    return max(float(get(r, 'hd_fwd_hi_rel', 1)), float(get(r, 'hd_back_hi_rel', 1)))


def faithful(r, hd_max=HD, iou_min=IOU):
    return hd(r) < hd_max and iou(r) > iou_min


def repaired(r, hd_max=HD, iou_min=IOU):
    return clean(r) and selfint(r) == 0 and faithful(r, hd_max, iou_min)
