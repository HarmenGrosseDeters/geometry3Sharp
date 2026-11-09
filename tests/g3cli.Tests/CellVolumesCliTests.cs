using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using g3;
using gs;
using static g3cli.Tests.TestMeshes;

namespace g3cli.Tests
{
    /// <summary>The cell_volumes command: two TIN files in, a CSV per cell and the totals out; checked against the library result.</summary>
    public class CellVolumesCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public CellVolumesCliTests(TestMeshes meshes) { m = meshes; }

        static DMesh3 Surface(int n, Func<double, double, double> h, bool flip)
        {
            var mesh = new DMesh3();
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) { double x = 10.0 * i / (n - 1), y = 10.0 * j / (n - 1); mesh.AppendVertex(new Vector3d(x, y, h(x, y))); }
            for (int j = 0; j + 1 < n; ++j)
                for (int i = 0; i + 1 < n; ++i) {
                    int v = j * n + i;
                    if (flip) { mesh.AppendTriangle(v, v + 1, v + n); mesh.AppendTriangle(v + 1, v + n + 1, v + n); }
                    else { mesh.AppendTriangle(v, v + 1, v + n + 1); mesh.AppendTriangle(v, v + n + 1, v + n); }
                }
            return mesh;
        }

        string Write(string name, DMesh3 mesh)
        {
            string path = m.Path_(name);
            Assert.Equal(IOCode.Ok, StandardMeshWriter.WriteMesh(path, mesh, WriteOptions.Defaults).code);
            return path;
        }

        [Fact]
        public void CellVolumes_AreTheLibrarysResult_AndTheCsvHasOneRowPerCell()
        {
            DMesh3 b = Surface(9, (x, y) => Math.Sin(x * 0.5) + 0.1 * y, false), a = Surface(6, (x, y) => 0.3 * x - 1 + Math.Cos(y * 0.4), true);
            string pb = Write("cv_b.obj", b), pa = Write("cv_a.obj", a), csv = m.Path_("cv.csv");
            var (exit, r) = Run("cell_volumes", pb, pa, csv, "--cell-size", "2.5", "--cell-size-y", "3");
            Assert.True(exit == 0, r.ToJsonString());
            DMesh3 rb = StandardMeshReader.ReadMesh(pb), ra = StandardMeshReader.ReadMesh(pa);
            var lib = new TerrainCellVolumes(rb, ra, 0, 0, 2.5, 3, 4, 4);
            Assert.True(lib.Compute());
            string[] rows = File.ReadAllLines(csv);
            Assert.Equal(1 + 16, rows.Length);
            Assert.StartsWith("ix,iy,x_min,y_min,cut_volume", rows[0]);
            for (int k = 0; k < 16; ++k) {
                string[] f = rows[1 + k].Split(',');
                Assert.Equal(k % 4, int.Parse(f[0])); Assert.Equal(k / 4, int.Parse(f[1]));
                Assert.Equal(lib.CutVolume[k], double.Parse(f[4], CultureInfo.InvariantCulture));
                Assert.Equal(lib.FillVolume[k], double.Parse(f[5], CultureInfo.InvariantCulture));
            }
            Assert.Equal(16, I(r["cells"])); Assert.Equal(4, I(r["lattice"]["nx"]));
            Assert.Equal(lib.TotalCutVolume, D(r["total_cut_volume"]));
            Assert.InRange(Math.Abs(D(r["cut_volume"]) - D(r["total_cut_volume"])), 0, 1e-9);
            // swapped
            var (_, s) = Run("cell_volumes", pa, pb, "--cell-size", "2.5", "--cell-size-y", "3");
            Assert.Equal(D(r["cut_volume"]), D(s["fill_volume"]), 9);
            // a smaller lattice leaves a part outside
            var (_, p) = Run("cell_volumes", pb, pa, "--cell-size", "2.5", "--x0", "0", "--y0", "0", "--nx", "2", "--ny", "2");
            Assert.True(Math.Abs(D(p["outside_cut_volume"])) + Math.Abs(D(p["outside_fill_volume"])) > 0.1);
        }

        [Fact]
        public void CellVolumes_Errors_HaveResultCodes()
        {
            DMesh3 s = Surface(4, (x, y) => 0, false);
            string p = Write("cv_s.obj", s);
            string Code(params string[] a) => S(Run(a).result["code"]);
            Assert.Equal(ResultCodes.InvalidArguments, Code("cell_volumes", p, p, "--cell-size", "0"));
            Assert.Equal(ResultCodes.InvalidArguments, Code("cell_volumes", p, p, "--cell-size", "1", "--nx", "0"));
            Assert.Equal(ResultCodes.InvalidArguments, Code("cell_volumes", p, p, "--cell-size", "1e-9"));
            Assert.Equal(ResultCodes.ReadError, Code("cell_volumes", p, m.Path_("none.obj"), "--cell-size", "1"));
            Assert.Equal(ResultCodes.WriteError, Code("cell_volumes", p, p, Path.Combine(m.Dir, "nodir", "o.csv"), "--cell-size", "1"));
            // a surface that is no height field
            DMesh3 twice = new DMesh3(s);
            new MeshEditor(twice).AppendMesh(Surface(4, (x, y) => 1, false));
            var (exit, r) = Run("cell_volumes", Write("cv_twice.obj", twice), p, "--cell-size", "5");
            Assert.Equal(1, exit); Assert.True(I(r["before_overlaps"]) > 0);
            // equal surfaces: nothing to cut or fill
            var (e2, z) = Run("cell_volumes", p, p, "--cell-size", "5");
            Assert.Equal(0, e2); Assert.Equal(0, D(z["cut_volume"])); Assert.Equal(0, D(z["fill_volume"]));
        }
    }
}
