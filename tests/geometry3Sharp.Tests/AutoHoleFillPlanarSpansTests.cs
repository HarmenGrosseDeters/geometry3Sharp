using System.Collections.Generic;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class AutoHoleFillPlanarSpansTests
    {
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void PlanarSpans_KeepDisjointBoundsInTheLocalFrame(string name)
        {
            Similarity s = Similarity.ByName(name);
            var mesh = new DMesh3();
            var spans = new List<EdgeSpan> { MakeSpan(0.01), MakeSpan(0.08) };
            var fill = new AutoHoleFill(mesh, null);
            // The experimental span strategy is disabled by the public classifier.
            var sort = typeof(AutoHoleFill).GetMethod("sort_planar_spans", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(sort);
            var groups = Assert.IsType<List<List<EdgeSpan>>>(
                sort.Invoke(fill, new object[] { spans, s.ApplyDirection(Vector3d.AxisZ) }));
            Assert.Collection(groups,
                first => Assert.Same(spans[0], Assert.Single(first)),
                second => Assert.Same(spans[1], Assert.Single(second)));

            EdgeSpan MakeSpan(double y)
            {
                int a = mesh.AppendVertex(s.Apply(new Vector3d(0.01, y, 0)));
                int b = mesh.AppendVertex(s.Apply(new Vector3d(0.02, y, 0)));
                int c = mesh.AppendVertex(s.Apply(new Vector3d(0.01, y + 0.01, 0)));
                mesh.AppendTriangle(a, b, c);
                return new EdgeSpan(mesh, new[] { a, b, c },
                    new[] { mesh.FindEdge(a, b), mesh.FindEdge(b, c) }, false);
            }
        }
    }
}
