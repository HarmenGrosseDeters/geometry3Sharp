using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Regression tests for the core/, io/, color/ and approximation/ findings of the
    /// class-by-class review (docs/Analyse-2026-10.md, section 2.5).
    /// </summary>
    public class CoreIOFixesTests
    {
        static string TempFile(string ext)
        {
            string dir = Path.Combine(Path.GetTempPath(), "g3_coreio_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "test." + ext);
        }

        static T WithGermanCulture<T>(Func<T> f)
        {
            var current = Thread.CurrentThread.CurrentCulture;
            try {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                return f();
            } finally {
                Thread.CurrentThread.CurrentCulture = current;
            }
        }

        static object GetPrivateField(object o, string name)
        {
            return o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
        }


        // ---------------------------------------------------------------- HBitArray

        [Fact]
        public void HBitArray_ClearingOneBit_KeepsOtherBitsOfSameBlockEnumerable()
        {
            // few set bits -> enumerator uses the hierarchical (sparse) path
            var bits = new HBitArray(32 * 100);
            bits.Set(0, true);
            bits.Set(1, true);
            bits.Set(1000, true);
            bits.Set(0, false);

            Assert.Equal(new[] { 1, 1000 }, bits.ToArray());
        }

        [Fact]
        public void HBitArray_ClearingLastBitOfBlock_ClearsParentBit()
        {
            var bits = new HBitArray(32 * 100);
            bits.Set(40, true);
            bits.Set(40, false);
            bits.Set(41, true);
            bits.Set(41, false);
            Assert.Empty(bits.ToArray());
            Assert.Equal(0, bits.TrueCount);
        }

        [Fact]
        public void HBitArray_DenseEnumeration_IncludesLastBlockForMultipleOf32()
        {
            // all bits set -> enumerator uses the dense path
            var bits = new HBitArray(64);
            for (int i = 0; i < 64; ++i)
                bits.Set(i, true);
            Assert.Equal(Enumerable.Range(0, 64).ToArray(), bits.ToArray());
        }


        // ---------------------------------------------------------------- OBJWriter

        static DMesh3 TriMesh(Vector3d offset, bool normals, bool uvs)
        {
            var mesh = new DMesh3(normals, false, uvs, false);
            var p = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) };
            int[] v = new int[3];
            for (int k = 0; k < 3; ++k) {
                var info = new NewVertexInfo(p[k] + offset);
                if (normals) { info.bHaveN = true; info.n = (Vector3f)ExpectedNormal(p[k] + offset); }
                if (uvs) { info.bHaveUV = true; info.uv = ExpectedUV(p[k] + offset); }
                v[k] = mesh.AppendVertex(info);
            }
            mesh.AppendTriangle(v[0], v[1], v[2]);
            return mesh;
        }
        static Vector3d ExpectedNormal(Vector3d p) { return new Vector3d(p.x + 1, p.y + 2, p.z + 3).Normalized; }
        static Vector2f ExpectedUV(Vector3d p) { return new Vector2f((float)(p.x * 0.1 + p.z * 0.01), (float)(p.y * 0.1)); }

        static List<double[]> ParseRecords(string[] lines, string key)
        {
            return lines.Where(l => l.StartsWith(key + " "))
                .Select(l => l.Split(' ').Skip(1).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray())
                .ToList();
        }

        // checks that every face corner v/vt/vn refers to an existing record with the data of that vertex
        static void AssertFaceAttributesMatch(string obj, bool normals, bool uvs)
        {
            string[] lines = obj.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var vs = ParseRecords(lines, "v");
            var vns = ParseRecords(lines, "vn");
            var vts = ParseRecords(lines, "vt");
            int nCorners = 0;
            foreach (string f in lines.Where(l => l.StartsWith("f "))) {
                foreach (string corner in f.Split(' ').Skip(1)) {
                    string[] parts = corner.Split('/');
                    var p = vs[int.Parse(parts[0]) - 1];
                    var pos = new Vector3d(p[0], p[1], p[2]);
                    if (uvs) {
                        int ti = int.Parse(parts[1]);
                        Assert.InRange(ti, 1, vts.Count);
                        Vector2f uv = ExpectedUV(pos);
                        Assert.Equal(uv.x, vts[ti - 1][0], 5);
                        Assert.Equal(uv.y, vts[ti - 1][1], 5);
                    }
                    if (normals) {
                        int ni = int.Parse(parts[2]);
                        Assert.InRange(ni, 1, vns.Count);
                        Vector3d n = ExpectedNormal(pos);
                        Assert.Equal(n.x, vns[ni - 1][0], 5);
                        Assert.Equal(n.y, vns[ni - 1][1], 5);
                        Assert.Equal(n.z, vns[ni - 1][2], 5);
                    }
                    nCorners++;
                }
            }
            Assert.Equal(9, nCorners);
        }

        static string WriteOBJ(List<WriteMesh> meshes, WriteOptions options)
        {
            var sw = new StringWriter();
            var result = new OBJWriter().Write(sw, meshes, options);
            Assert.Equal(IOCode.Ok, result.code);
            return sw.ToString();
        }

        [Fact]
        public void OBJWriter_NormalIndices_CorrectWhenFirstMeshHasNoNormals()
        {
            var meshes = new List<WriteMesh>() {
                new WriteMesh(TriMesh(Vector3d.Zero, false, false)),
                new WriteMesh(TriMesh(new Vector3d(5, 0, 0), true, false)),
                new WriteMesh(TriMesh(new Vector3d(9, 0, 0), true, false)),
            };
            var options = WriteOptions.Defaults;
            options.bPerVertexNormals = true;
            string obj = WriteOBJ(meshes, options);
            Assert.Contains("f 1 2 3", obj);
            AssertFaceAttributesMatch(obj, false, false);
            // faces of the meshes with normals reference their own vn records
            string[] faces = obj.Split('\n').Where(l => l.StartsWith("f ")).Select(l => l.Trim()).ToArray();
            Assert.Equal("f 4//1 5//2 6//3", faces[1]);
            Assert.Equal("f 7//4 8//5 9//6", faces[2]);
        }

        [Fact]
        public void OBJWriter_VertexUVIndices_CorrectWhenFirstMeshHasNoUVs()
        {
            var meshes = new List<WriteMesh>() {
                new WriteMesh(TriMesh(Vector3d.Zero, true, false)),
                new WriteMesh(TriMesh(new Vector3d(5, 0, 0), true, true)),
                new WriteMesh(TriMesh(new Vector3d(9, 0, 0), true, true)),
            };
            var options = WriteOptions.Defaults;
            options.bPerVertexNormals = true;
            options.bPerVertexUVs = true;
            string obj = WriteOBJ(meshes, options);
            string[] faces = obj.Split('\n').Where(l => l.StartsWith("f ")).Select(l => l.Trim()).ToArray();
            Assert.Equal("f 1//1 2//2 3//3", faces[0]);
            Assert.Equal("f 4/1/4 5/2/5 6/3/6", faces[1]);
            Assert.Equal("f 7/4/7 8/5/8 9/6/9", faces[2]);
        }

        [Fact]
        public void OBJWriter_VertexUVsFollowedBySeparateUVSet_IndicesDoNotOverlap()
        {
            var m0 = TriMesh(Vector3d.Zero, false, true);
            var m1 = TriMesh(new Vector3d(5, 0, 0), false, false);
            var uvSet = new DenseUVMesh();
            uvSet.UVs = new DVector<Vector2f>(new[] { ExpectedUV(new Vector3d(5, 0, 0)), ExpectedUV(new Vector3d(6, 0, 0)), ExpectedUV(new Vector3d(5, 1, 0)) });
            uvSet.TriangleUVs = new DVector<Index3i>(new[] { new Index3i(0, 1, 2) });
            var wm1 = new WriteMesh(m1) { UVs = uvSet };
            var options = WriteOptions.Defaults;
            options.bPerVertexUVs = true;
            string obj = WriteOBJ(new List<WriteMesh>() { new WriteMesh(m0), wm1 }, options);
            string[] faces = obj.Split('\n').Where(l => l.StartsWith("f ")).Select(l => l.Trim()).ToArray();
            Assert.Equal("f 1/1 2/2 3/3", faces[0]);
            Assert.Equal("f 4/4 5/5 6/6", faces[1]);
        }


        // ---------------------------------------------------------------- OBJReader

        static DMesh3Builder ReadOBJ(string text, List<string> warnings, out IOReadResult result)
        {
            var builder = new DMesh3Builder();
            var reader = new OBJReader();
            reader.warningEvent += (msg, o) => warnings.Add(msg);
            result = reader.Read(new StringReader(text), new ReadOptions(), builder);
            return builder;
        }

        [Theory]
        [InlineData("f 1 2 9")]
        [InlineData("f 0 1 2")]
        public void OBJReader_InvalidFaceIndex_WarnsInsteadOfThrowing(string badFace)
        {
            string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n" + badFace + "\n";
            var warnings = new List<string>();
            var builder = ReadOBJ(obj, warnings, out IOReadResult result);
            Assert.Equal(IOCode.Ok, result.code);
            Assert.Single(builder.Meshes);
            Assert.Equal(1, builder.Meshes[0].TriangleCount);
            Assert.Contains(warnings, w => w.Contains("invalid triangle"));
        }

        [Fact]
        public void OBJReader_UVDimension_IsNumberOfComponents()
        {
            string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nf 1/1 2/2 3/3\n";
            var builder = new DMesh3Builder();
            var reader = new OBJReader();
            reader.Read(new StringReader(obj), new ReadOptions(), builder);
            Assert.Equal(2, reader.UVDimension);
        }

        [Fact]
        public void OBJReader_GroupWithoutName_StartsDefaultGroup()
        {
            string obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\ng a\nf 1 2 3\ng\nf 2 4 3\n";
            var warnings = new List<string>();
            var builder = ReadOBJ(obj, warnings, out IOReadResult result);
            Assert.Equal(IOCode.Ok, result.code);
            Assert.Empty(warnings);
            DMesh3 mesh = builder.Meshes[0];
            Assert.NotEqual(mesh.GetTriangleGroup(0), mesh.GetTriangleGroup(1));
        }


        // ---------------------------------------------------------------- Util

        [Fact]
        public void IsBinaryStream_DefaultSearchLength_SearchesWholeStream()
        {
            var binary = new MemoryStream(new byte[] { 65, 66, 1, 2, 3, 0, 0, 7 });
            Assert.True(Util.IsBinaryStream(binary));
        }

        [Fact]
        public void IsBinaryStream_TextShorterThanSearchLength_IsNotBinary()
        {
            var text = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("solid t\nendsolid t\n"));
            Assert.False(Util.IsBinaryStream(text, 500));
            Assert.Equal(0, text.Position);
        }

        [Fact]
        public void IsBinaryFile_DefaultSearchLength_DetectsBinaryFile()
        {
            string path = TempFile("bin");
            File.WriteAllBytes(path, new byte[] { 65, 66, 1, 2, 3, 0, 0, 7 });
            Assert.True(Util.IsBinaryFile(path));
        }

        [Fact]
        public void StandardMeshReader_ShortAsciiSTL_IsReadAsText()
        {
            string stl =
                "solid t\n" +
                "facet normal 0 0 1\n outer loop\n  vertex 0 0 0\n  vertex 1 0 0\n  vertex 0 1 0\n endloop\nendfacet\n" +
                "endsolid t\n";
            Assert.True(stl.Length < 500);
            string path = TempFile("stl");
            File.WriteAllText(path, stl);
            DMesh3 mesh = StandardMeshReader.ReadMesh(path);
            Assert.NotNull(mesh);
            Assert.Equal(1, mesh.TriangleCount);
            Assert.Equal(new Vector3d(1, 0, 0), mesh.GetVertex(mesh.GetTriangle(0).b));
        }

        [Fact]
        public void Util_ParseInt_ReturnsInt()
        {
            object r = Util.ParseInt("16777217", 0);
            Assert.IsType<int>(r);
            Assert.Equal(16777217, (int)r);
            Assert.Equal(-3, (int)(object)Util.ParseInt("x", -3));
        }

        [Fact]
        public void Util_ParseFloatDouble_UseInvariantCulture()
        {
            Assert.Equal(1.5f, WithGermanCulture(() => Util.ParseFloat("1.5", 0)));
            Assert.Equal(1.5, WithGermanCulture(() => Util.ParseDouble("1.5", 0)));
        }


        // ---------------------------------------------------------------- GaussPointsFit3 / OrthogonalPlaneFit3

        static List<Vector3d> SpreadPoints()
        {
            // dominant spread along (1,2,3), smaller along a perpendicular direction
            var dir = new Vector3d(1, 2, 3).Normalized;
            var perp = dir.Cross(Vector3d.AxisX).Normalized;
            var perp2 = dir.Cross(perp).Normalized;
            var pts = new List<Vector3d>();
            for (int i = -5; i <= 5; ++i) {
                for (int j = -2; j <= 2; ++j) {
                    pts.Add(new Vector3d(1, -2, 0.5) + 2.0 * i * dir + 0.7 * j * perp + 0.1 * ((i + j) % 2) * perp2);
                }
            }
            return pts;
        }

        static Matrix3d Covariance(List<Vector3d> pts)
        {
            Vector3d c = Vector3d.Zero;
            foreach (var p in pts) c += p;
            c /= pts.Count;
            double[] m = new double[9];
            foreach (var p in pts) {
                Vector3d d = p - c;
                for (int r = 0; r < 3; ++r)
                    for (int k = 0; k < 3; ++k)
                        m[3 * r + k] += d[r] * d[k] / pts.Count;
            }
            return new Matrix3d(m);
        }

        [Fact]
        public void GaussPointsFit3_Axes_AreEigenvectorsOfCovariance()
        {
            var pts = SpreadPoints();
            var fit = new GaussPointsFit3(pts);
            Assert.True(fit.ResultValid);
            Matrix3d cov = Covariance(pts);
            for (int k = 0; k < 3; ++k) {
                Vector3d axis = fit.Box.Axis(k);
                Vector3d av = cov * axis;
                Assert.True((av - fit.Box.Extent[k] * axis).Length < 1e-8,
                    "axis " + k + " is not an eigenvector: " + av + " vs " + fit.Box.Extent[k] * axis);
            }
            Assert.True(Math.Abs(fit.Box.AxisZ.Dot(new Vector3d(1, 2, 3).Normalized)) > 0.999);
        }

        [Fact]
        public void GaussPointsFit3_Weighted_MatchesUnweightedForConstantWeights()
        {
            var pts = SpreadPoints();
            var fit = new GaussPointsFit3(pts);
            foreach (double w in new[] { 1.0, 2.5 }) {
                var wfit = new GaussPointsFit3(pts, pts.Select(p => w));
                Assert.True(wfit.ResultValid);
                Assert.True((wfit.Box.Center - fit.Box.Center).Length < 1e-10);
                for (int k = 0; k < 3; ++k)
                    Assert.Equal(fit.Box.Extent[k], wfit.Box.Extent[k], 8);
            }
        }

        [Fact]
        public void GaussPointsFit3_Weighted_EqualsDuplicatedPoints()
        {
            // weight 2 on a point is the same as having that point twice
            var pts = SpreadPoints();
            var weights = pts.Select((p, i) => (i % 3 == 0) ? 2.0 : 1.0).ToList();
            var dup = new List<Vector3d>(pts);
            for (int i = 0; i < pts.Count; i += 3)
                dup.Add(pts[i]);
            var wfit = new GaussPointsFit3(pts, weights);
            var dfit = new GaussPointsFit3(dup);
            for (int k = 0; k < 3; ++k)
                Assert.Equal(dfit.Box.Extent[k], wfit.Box.Extent[k], 8);
        }

        [Fact]
        public void GaussPointsFit3_DiagonalCovariance_IsValid()
        {
            var pts = new List<Vector3d>() {
                new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0),
                new Vector3d(0, 2, 0), new Vector3d(0, -2, 0),
                new Vector3d(0, 0, 3), new Vector3d(0, 0, -3),
            };
            var fit = new GaussPointsFit3(pts);
            Assert.True(fit.ResultValid);
            Assert.Equal(2.0 / 6.0, fit.Box.Extent.x, 10);
            Assert.Equal(8.0 / 6.0, fit.Box.Extent.y, 10);
            Assert.Equal(18.0 / 6.0, fit.Box.Extent.z, 10);
            Assert.Equal(1.0, Math.Abs(fit.Box.AxisZ.z), 10);
        }

        [Fact]
        public void OrthogonalPlaneFit3_DiagonalCovariance_IsValid()
        {
            var pts = new List<Vector3d>() {
                new Vector3d(1, 0, 4), new Vector3d(-1, 0, 4),
                new Vector3d(0, 2, 4), new Vector3d(0, -2, 4),
            };
            var fit = new OrthogonalPlaneFit3(pts);
            Assert.True(fit.ResultValid);
            Assert.Equal(1.0, Math.Abs(fit.Normal.z), 10);
            Assert.True((fit.Origin - new Vector3d(0, 0, 4)).Length < 1e-12);
        }


        // ---------------------------------------------------------------- gSerialization

        static DMesh3 StoreRestore(DMesh3 mesh)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            gSerialization.Store(mesh, w);
            w.Flush();
            ms.Position = 0;
            var restored = new DMesh3();
            gSerialization.Restore(restored, new BinaryReader(ms));
            return restored;
        }

        [Fact]
        public void gSerialization_DMesh3_KeepsIsolatedVertices()
        {
            var mesh = new DMesh3(true, false, false, false);
            int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
            int iso = mesh.AppendVertex(new NewVertexInfo(new Vector3d(7, 8, 9), new Vector3f(0, 0, 1)));
            int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
            int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
            mesh.AppendTriangle(a, b, c);

            DMesh3 restored = StoreRestore(mesh);
            Assert.Equal(4, restored.VertexCount);
            Assert.True(restored.IsVertex(iso));
            Assert.Equal(new Vector3d(7, 8, 9), restored.GetVertex(iso));
            Assert.Equal(new Vector3f(0, 0, 1), restored.GetVertexNormal(iso));
            Assert.Equal(1, restored.TriangleCount);
            Assert.True(restored.CheckValidity(true));
            // free list is consistent: next vertex gets a new id
            Assert.Equal(4, restored.AppendVertex(Vector3d.One));
        }

        [Fact]
        public void gSerialization_DMesh3_KeepsPointCloud()
        {
            var mesh = new DMesh3();
            for (int i = 0; i < 5; ++i)
                mesh.AppendVertex(new Vector3d(i, 2 * i, 0));
            mesh.RemoveVertex(2);

            DMesh3 restored = StoreRestore(mesh);
            Assert.Equal(4, restored.VertexCount);
            Assert.False(restored.IsVertex(2));
            Assert.Equal(new Vector3d(4, 8, 0), restored.GetVertex(4));
            Assert.True(restored.CheckValidity(true));
        }

        [Fact]
        public void gSerialization_DMesh3_ReadsVersion1Data()
        {
            // guard: data written in the old format (version 1) still restores
            var mesh = new DMesh3();
            mesh.AppendTriangle(mesh.AppendVertex(Vector3d.Zero), mesh.AppendVertex(Vector3d.AxisX), mesh.AppendVertex(Vector3d.AxisY));
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(1);
            w.Write((int)mesh.Components);
            gSerialization.Store(mesh.VerticesBuffer, w);
            gSerialization.Store(mesh.TrianglesBuffer, w);
            gSerialization.Store(mesh.EdgesBuffer, w);
            gSerialization.Store(mesh.EdgesRefCounts.RawRefCounts, w);
            if (mesh.HasVertexNormals)
                gSerialization.Store(mesh.NormalsBuffer, w);
            Assert.False(mesh.HasVertexColors || mesh.HasVertexUVs || mesh.HasTriangleGroups);
            w.Flush();
            ms.Position = 0;
            var restored = new DMesh3();
            gSerialization.Restore(restored, new BinaryReader(ms));
            Assert.Equal(3, restored.VertexCount);
            Assert.Equal(1, restored.TriangleCount);
        }


        // ---------------------------------------------------------------- DVector

        [Fact]
        public void DVector_InitializeEmptyArray_IsUsable()
        {
            var v = new DVector<int>(new int[0]);
            Assert.Equal(0, v.Length);
            Assert.Empty(v);
            Assert.Empty(v.GetBytes());
            v.Add(5);
            Assert.Equal(new[] { 5 }, v.ToArray());
        }

        [Fact]
        public void DVector_Apply_PassesGlobalIndex()
        {
            var v = new DVector<int>(Enumerable.Range(0, 5000));
            int n = 0;
            v.Apply((value, i) => { Assert.Equal(value, i); n++; });
            Assert.Equal(5000, n);

            v.ApplyReplace((value, i) => i * 2);
            Assert.Equal(Enumerable.Range(0, 5000).Select(i => i * 2).ToArray(), v.ToArray());
        }

        [Fact]
        public void DVector_PopBackAfterResizeToBlockMultiple_RemovesElement()
        {
            var v = new DVector<int>();
            v.resize(2048);
            v[2047] = 7;
            v[2046] = 6;
            v.pop_back();
            Assert.Equal(2047, v.Length);
            Assert.Equal(6, v.back);
            v.Add(9);
            Assert.Equal(2048, v.Length);
            Assert.Equal(9, v[2047]);
        }

        static DVector<int> PoppedAcrossBlock()
        {
            // leaves an allocated but unused block after iCurBlock
            var v = new DVector<int>(Enumerable.Range(0, 2049));
            v.pop_back();
            v.pop_back();
            return v;
        }

        [Fact]
        public void DVector_GetBytes_AfterPopBackAcrossBlock()
        {
            var v = PoppedAcrossBlock();
            Assert.Equal(2047, v.Length);
            byte[] bytes = v.GetBytes();
            Assert.Equal(2047 * sizeof(int), bytes.Length);
            int[] back = new int[2047];
            Buffer.BlockCopy(bytes, 0, back, 0, bytes.Length);
            Assert.Equal(Enumerable.Range(0, 2047).ToArray(), back);
        }

        [Fact]
        public void DVector_FastGetBuffer_DoesNotWritePastLength()
        {
            var v = PoppedAcrossBlock();
            int[] buffer = Enumerable.Repeat(-1, 2047 + 4096).ToArray();
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
            try {
                // FastGetBuffer takes an int*; reflection accepts an IntPtr for a pointer parameter
                MethodInfo fast = typeof(DVector<int>).GetMethod("FastGetBuffer", new[] { typeof(DVector<int>), typeof(int).MakePointerType() });
                fast.Invoke(null, new object[] { v, handle.AddrOfPinnedObject() });
            } finally {
                handle.Free();
            }
            Assert.Equal(Enumerable.Range(0, 2047).ToArray(), buffer.Take(2047).ToArray());
            Assert.All(buffer.Skip(2047), x => Assert.Equal(-1, x));
        }


        // ---------------------------------------------------------------- Colorf / Colorb

        [Fact]
        public void Colorf_SqrDistance_UsesAllChannels()
        {
            var c1 = new Colorf(0.0f, 0.0f, 1.0f, 0.5f);
            var c2 = new Colorf(0.0f, 0.0f, 0.0f, 0.5f);
            Assert.Equal(1.0f, c1.SqrDistance(c2));
            Assert.Equal(1.0f + 0.25f, new Colorf(1.0f, 0.0f, 0.0f, 1.0f).SqrDistance(new Colorf(0.0f, 0.0f, 0.0f, 0.5f)));
        }

        [Fact]
        public void Colorf_MinusScalar_SubtractsFromAlpha()
        {
            Colorf c = new Colorf(1.0f, 1.0f, 1.0f, 1.0f) - 0.25f;
            Assert.Equal(new Colorf(0.75f, 0.75f, 0.75f, 0.75f), c);
        }

        [Fact]
        public void Colorb_ByteConstructors_DefaultAlphaIsOpaque()
        {
            Assert.Equal(255, new Colorb((byte)10, (byte)20, (byte)30).a);
            Assert.Equal(255, new Colorb((byte)10).a);
        }

        [Fact]
        public void Colorb_FloatConstructor_Rounds()
        {
            var c = new Colorb(0.999f, 0.5f, 0.002f, 0.4f);
            Assert.Equal(255, c.r);
            Assert.Equal(128, c.g);
            Assert.Equal(1, c.b);
            Assert.Equal(102, c.a);
            Assert.Equal(0, new Colorb(-0.5f, 0, 0).r);
            Assert.Equal(255, new Colorb(2.0f, 0, 0).r);
        }


        // ---------------------------------------------------------------- priority queues

        class TestNode : DynamicPriorityQueueNode { }

        [Fact]
        public void DynamicPriorityQueue_IsValidQueue_ChecksLastNode()
        {
            var q = new DynamicPriorityQueue<TestNode>();
            var nodes = new[] { new TestNode(), new TestNode(), new TestNode() };
            for (int i = 0; i < 3; ++i)
                q.Enqueue(nodes[i], i + 1);
            Assert.True(q.IsValidQueue());
            Assert.Equal(3, nodes[2].index);
            nodes[2].priority = 0;      // corrupt the last node in the heap
            Assert.False(q.IsValidQueue());
        }

        [Fact]
        public void IndexPriorityQueue_IsValidQueue_ChecksLastNode()
        {
            var q = new IndexPriorityQueue(10);
            for (int i = 0; i < 3; ++i)
                q.Insert(i, i + 1);
            Assert.True(q.IsValidQueue());

            // corrupt the last node in the heap
            object nodes = GetPrivateField(q, "nodes");
            PropertyInfo item = nodes.GetType().GetProperty("Item");
            object node = item.GetValue(nodes, new object[] { 3 });
            node.GetType().GetField("priority").SetValue(node, 0.0f);
            item.SetValue(nodes, node, new object[] { 3 });

            Assert.False(q.IsValidQueue());
        }

        [Fact]
        public void DynamicPriorityQueue_ClearWithoutFreeingMemory_DropsNodeReferences()
        {
            var q = new DynamicPriorityQueue<TestNode>();
            q.Enqueue(new TestNode(), 1);
            q.Enqueue(new TestNode(), 2);
            q.Clear(false);
            Assert.Equal(0, q.Count);
            var nodes = (DVector<TestNode>)GetPrivateField(q, "nodes");
            Assert.All(nodes, n => Assert.Null(n));

            // queue is still usable
            var t = new TestNode();
            q.Enqueue(t, 5);
            Assert.Same(t, q.Dequeue());
        }


        // ---------------------------------------------------------------- assorted

        [Fact]
        public void BufferUtil_Filter_RespectsMaxIndex()
        {
            int[] data = new[] { 1, 2, 3, 4 };
            Assert.Equal(new[] { 1, 2 }, BufferUtil.Filter(data, x => x > 0, 2));
            Assert.Equal(new[] { 2, 4 }, BufferUtil.Filter(data, x => x % 2 == 0));
        }

        [Fact]
        public void OFFReader_Polygon_IsTriangulated()
        {
            string off = "OFF\n6 2 0\n0 0 0\n1 0 0\n1 1 0\n0 1 0\n2 0 0\n2 1 0\n4 0 1 2 3\n4 1 4 5 2\n";
            var builder = new DMesh3Builder();
            var warnings = new List<string>();
            var reader = new OFFReader();
            reader.warningEvent += (m, o) => warnings.Add(m);
            var result = reader.Read(new StringReader(off), new ReadOptions(), builder);
            Assert.Equal(IOCode.Ok, result.code);
            DMesh3 mesh = builder.Meshes[0];
            Assert.Equal(4, mesh.TriangleCount);
            Assert.Equal(2.0, MeshMeasurements.AreaT(mesh, mesh.TriangleIndices()), 10);
            Assert.True(mesh.IsClosed() == false && mesh.CheckValidity(true));
            Assert.Empty(warnings);
        }

        [Fact]
        public void SVGWriter_Arc_SeparatesFlagsFromEndPoint()
        {
            var svg = new SVGWriter();
            svg.AddArc(new Arc2d(new Vector2d(0, 0), 10, 0, 90));
            string path = TempFile("svg");
            Assert.Equal(IOCode.Ok, svg.Write(path).code);
            string text = File.ReadAllText(path);
            Match m = Regex.Match(text, "d=\"([^\"]*)\"");
            Assert.True(m.Success, text);
            Assert.Matches(@"^M-?[\d.]+,-?[\d.]+ A[\d.]+,[\d.]+ 0 [01],[01] -?[\d.]+,-?[\d.]+$", m.Groups[1].Value);
        }

        [Fact]
        public void STLReader_BinaryReadException_ReturnsError()
        {
            // guard (behavior unchanged): an exception while reading binary triangles is reported as an error.
            // The fix frees the native buffer on this path too, which a test cannot observe.
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new byte[80]);
            w.Write(1000);
            w.Write(new byte[60]);
            w.Flush();
            var stream = new ThrowingStream(ms.ToArray(), 80 + 4 + 50);
            var result = new STLReader().Read(new BinaryReader(stream), new ReadOptions(), new DMesh3Builder());
            Assert.Equal(IOCode.GenericReaderError, result.code);
        }

        class ThrowingStream : MemoryStream
        {
            readonly int limit;
            public ThrowingStream(byte[] data, int limit) : base(data) { this.limit = limit; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Position + count > limit)
                    throw new IOException("read failed");
                return base.Read(buffer, offset, count);
            }
        }

        [Fact]
        public void SmallListSet_RemoveFromUnallocatedList_ReturnsFalse()
        {
            var set = new SmallListSet();
            set.Resize(4);
            Assert.False(set.Remove(2, 7));
            set.AllocateAt(3);
            Assert.False(set.Remove(3, 7));
            set.Insert(3, 7);
            Assert.True(set.Remove(3, 7));
            Assert.Equal(0, set.Count(3));
        }

        [Fact]
        public void SmallListSet_MemoryUsage_ReportsFieldsInOrder()
        {
            var set = new SmallListSet();
            set.Resize(4);
            set.AllocateAt(0);
            set.Insert(0, 1);
            set.AllocateAt(1);
            set.Insert(1, 2);
            set.Clear(0);
            // 2 blocks allocated, 1 of them free, 2*(8+2) ints of block store = 0kb
            Assert.Equal("ListSize 4  Blocks Count 2 Free 1 Mem 0kb  Linked Mem 0kb", set.MemoryUsage);
        }

        [Fact]
        public void Snapping_LowHigh_AreSymmetricForExactIncrements()
        {
            Assert.Equal(2.0, Snapping.SnapToIncrementLow(2.0, 1.0));
            Assert.Equal(-2.0, Snapping.SnapToIncrementLow(-2.0, 1.0));
            Assert.Equal(2.0, Snapping.SnapToIncrementHigh(2.0, 1.0));
            Assert.Equal(-2.0, Snapping.SnapToIncrementHigh(-2.0, 1.0));

            Assert.Equal(2.0, Snapping.SnapToIncrementLow(2.5, 1.0));
            Assert.Equal(-3.0, Snapping.SnapToIncrementLow(-2.5, 1.0));
            Assert.Equal(3.0, Snapping.SnapToIncrementHigh(2.5, 1.0));
            Assert.Equal(-2.0, Snapping.SnapToIncrementHigh(-2.5, 1.0));
            Assert.Equal(-2.0 + 0.25, Snapping.SnapToIncrementLow(-1.5, 1.0, 0.25), 12);
        }

        [Fact]
        public void Units_Mile_IsInternationalMile()
        {
            Assert.Equal(1609.344, Units.ToMeters(Units.Linear.Miles), 12);
            Assert.Equal(1.0 / 1609.344, Units.MetersTo(Units.Linear.Miles), 15);
            Assert.Equal(5280.0, Units.Convert(Units.Linear.Miles, Units.Linear.Feet), 9);
        }

        [Fact]
        public void StandardMeshReader_ParseError_IsNotReportedAsFileAccessError()
        {
            string path = TempFile("g3mesh");
            File.WriteAllBytes(path, new byte[] { 1, 0, 0, 0, 99, 0, 0, 0, 5, 5, 5, 5 });
            var reader = new StandardMeshReader() { MeshBuilder = new DMesh3Builder() };
            IOReadResult result = reader.Read(path, new ReadOptions());
            Assert.Equal(IOCode.GenericReaderError, result.code);
            Assert.DoesNotContain("Could not open", result.message);

            // a missing file is still a file access error
            result = reader.Read(path + ".missing.g3mesh", new ReadOptions());
            Assert.Equal(IOCode.FileAccessError, result.code);
        }
    }
}
