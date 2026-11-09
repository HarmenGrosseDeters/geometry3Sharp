using System; using g3; using gs;
static class P { static DMesh3 Box(Vector3d c, Vector3d h) => new TrivialBox3Generator(){Box=new Box3d(c,h),NoSharedVertices=false}.Generate().MakeDMesh();
 static void Main(){
  DMesh3 a = Box(Vector3d.Zero, new Vector3d(1,0.7,0.4)), b = Box(new Vector3d(0.9,0.3,0), new Vector3d(0.6,0.7,0.4));
  var R = Matrix3d.AxisAngleD(Vector3d.AxisZ, 31) * Matrix3d.AxisAngleD(Vector3d.AxisX, 17);
  foreach (var m in new[]{a,b}) foreach (int v in m.VertexIndices()) m.SetVertex(v, R * m.GetVertex(v));
  var bo = new ExactMeshBoolean(a,b,ExactMeshBoolean.Operations.Difference); g3.gParallel.SerialOnThisThread = true; bo.Compute(); 
  var r = bo.Rounding; Console.WriteLine($"clean {r.IsClean} initDeg {r.InitialDegenerate} initInt {r.InitialIntersections} remDeg {r.RemainingDegenerate} remInt {r.RemainingIntersections} rej {r.SmallestRejectedDrift:R} max {r.MaxLength:R} edits {r.Collapses},{r.Flips},{r.Moves},{r.Zips} at {r.RemainingDefectAt}");
  var M = bo.Result; double diag = gs.MeshRepairMetrics.TriangleBounds(M).DiagonalLength; double eps = 1e-12*diag*diag;
  foreach (int t in M.TriangleIndices()) { if (M.GetTriArea(t) >= eps) continue; var f = M.GetTriangle(t);
    Console.WriteLine($"deg tri {t} {f} area {M.GetTriArea(t):E3} eps {eps:E3}");
    foreach (int v in new[]{f.a,f.b,f.c}) Console.WriteLine($"  v{v} {M.GetVertex(v).x:R} {M.GetVertex(v).y:R} {M.GetVertex(v).z:R} valence {M.GetVtxEdgeCount(v)} boundary {M.IsBoundaryVertex(v)}");
    for (int k=0;k<3;k++){ int u=f[k], w=f[(k+1)%3]; double L=(M.GetVertex(u)-M.GetVertex(w)).Length; int e=M.FindEdge(u,w); var et=M.GetEdgeT(e); int o = et.a==t?et.b:et.a; Console.WriteLine($"  edge {u}-{w} len {L:E3} ({L/r.MaxLength:F2} x bound) other tri {o} area {M.GetTriArea(o):E3} {M.GetTriangle(o)}"); }
  }
 }}
