// "dup case" minimized by MeshMinimizer (Minimized: 6 vertices, 2 triangles after 62 evaluations in 0.0 s):
// Xunit.Sdk.FalseException: 301 triangles with a coincident pair
static DMesh3 MinimizedDupCase()
{
    // 6 vertices, 2 triangles
    double[] v = {
        -0.0, -0.5, -1,
        -0.0, -0.0, -1,
        -0.5, -0.0, -1,
        -0.0, -0.5, -1,
        -0.5, -0.0, -1,
        -0.0, -0.0, -1,
    };
    int[] t = {
        0, 2, 1,
        3, 4, 5,
    };
    var m = new DMesh3(false, false, false, false);
    for (int i = 0; i < v.Length; i += 3) m.AppendVertex(new Vector3d(v[i], v[i + 1], v[i + 2]));
    for (int i = 0; i < t.Length; i += 3)
        if (m.AppendTriangle(t[i], t[i + 1], t[i + 2]) < 0)
            throw new InvalidOperationException("repro: triangle " + i / 3);
    return m;
}
