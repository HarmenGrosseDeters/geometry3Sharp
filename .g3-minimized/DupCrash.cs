// "dup crash" minimized by MeshMinimizer (Minimized: 17 vertices, 10 triangles after 114 evaluations in 0.0 s):
// Xunit.Sdk.FalseException: 301 triangles with a coincident pair
static DMesh3 MinimizedDupCrash()
{
    // 17 vertices, 10 triangles
    double[] v = {
        0, 0, -0.0,
        0, 0, -0.0,
        0, 0, -0.0,
        0, 0, -0.0,
        0, -0.0, -0.0,
        0, -0.0, -0.0,
        -0.0, -0.0, -0.0,
        -0.0, -0.0, -0.0,
        0, -0.0, -0.0,
        0, -0.0, -0.0,
        -0.0, -0.0, -0.0,
        -0.0, -0.0, -0.0,
        0, 0, -0.0,
        0, 0, -0.0,
        -0.0, -0.0, -0.0,
        -0.0, -0.0, -0.0,
        -0.0, -0.0, -0.0,
    };
    int[] t = {
        4, 9, 8,
        5, 6, 10,
        5, 10, 9,
        6, 7, 11,
        6, 11, 10,
        12, 13, 1,
        12, 1, 0,
        13, 3, 2,
        13, 2, 1,
        14, 15, 16,
    };
    var m = new DMesh3(false, false, false, false);
    for (int i = 0; i < v.Length; i += 3) m.AppendVertex(new Vector3d(v[i], v[i + 1], v[i + 2]));
    for (int i = 0; i < t.Length; i += 3)
        if (m.AppendTriangle(t[i], t[i + 1], t[i + 2]) < 0)
            throw new InvalidOperationException("repro: triangle " + i / 3);
    return m;
}
