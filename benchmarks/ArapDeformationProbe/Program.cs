using System;
using System.Collections.Generic;
using g3;
class Probe {
 static double Error(Matrix3d a, Matrix3d b) { double s=0; for(int r=0;r<3;r++)for(int c=0;c<3;c++)s=Math.Max(s,Math.Abs(a[r,c]-b[r,c])); return s; }
 static void Svd(string name, Matrix3d m, Matrix3d expected, int steps) {
  var svd=new FastQuaternionSVD(m,jacobiIters:steps);
  var rotation=svd.U.ToRotationMatrix() * svd.V.Conjugate().ToRotationMatrix();
  Console.WriteLine($"{name} steps={steps} reconstruction={Error(m,svd.ReconstructMatrix()):G17} rotation_error={Error(rotation,expected):G17} determinant={rotation.Determinant:G17} orthogonal_error={Error(rotation.Transpose()*rotation,Matrix3d.Identity):G17}");
 }
 static void Main() {
  var rot=Matrix3d.AxisAngleD(Vector3d.AxisZ,37);
  foreach(int steps in new[]{4,20}) {
   Svd("identity", Matrix3d.Identity, Matrix3d.Identity, steps);
   Svd("rotated-full",rot*new Matrix3d(3,2,1),rot,steps);
   Svd("rotated-planar",rot*new Matrix3d(3,2,0),rot,steps);
   var q=Matrix3d.AxisAngleD(new Vector3d(1,2,3).Normalized,29);
   var shape=q * new Matrix3d(4,2,0.5) * q.Transpose();
   Svd("general",rot*shape,rot,steps);
   Svd("general-small",rot*shape*(1.0/1048576),rot,steps);
   Svd("general-large",rot*shape*1048576.0,rot,steps);
   Svd("rank-one",rot*new Matrix3d(3,0,0),rot,steps);
   Svd("zero",new Matrix3d(0,0,0),Matrix3d.Identity,steps);
   Svd("reflection",new Matrix3d(-3,2,1),Matrix3d.Identity,steps);
  }
  var c=new SparseCholesky(2); c.Add(0,0,2);c.Add(1,0,-1);c.Add(1,1,2); bool ok=c.Factorize();var x=new double[2];if(ok)c.Solve(new double[]{1,0},x);Console.WriteLine($"cholesky valid={ok} x={x[0]:G17},{x[1]:G17}");
  var mesh=new DMesh3(); mesh.AppendVertex(new Vector3d(0,0,0));mesh.AppendVertex(new Vector3d(1,0,0));mesh.AppendVertex(new Vector3d(1,1,0));mesh.AppendVertex(new Vector3d(0,1,0)); mesh.AppendTriangle(0,1,2);mesh.AppendTriangle(0,2,3);
  foreach (var move in new[]{new Vector3d(0,0,0.2),new Vector3d(0.2,0,0)}) {
   var handles=new Dictionary<int,Vector3d>{[0]=mesh.GetVertex(0),[1]=mesh.GetVertex(1),[3]=mesh.GetVertex(3)+move};
   foreach (int maxIterations in new[]{50,500,5000}) {
    var solver=new ArapMeshDeformer(mesh);solver.Options.MaxIterations=maxIterations;var result=solver.Compute(handles);
    Console.WriteLine($"move={move} budget={maxIterations} status={result.Status} iterations={result.Iterations} residual={result.Residual:G17}");
   }
  }
 }
}
