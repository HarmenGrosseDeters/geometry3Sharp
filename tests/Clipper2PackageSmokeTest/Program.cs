using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using g3;
using g3.Clipper2;

// A union and an offset through the packaged library, and a check of the package contents.
static class Program
{
    static int Main(string[] args)
    {
        int failures = 0;
        Action<bool, string> check = (ok, what) => {
            Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
            if (!ok) failures++;
        };

        if (args.Length > 0) {
            using (ZipArchive zip = ZipFile.OpenRead(args[0])) {
                string[] entries = zip.Entries.Select(e => e.FullName).ToArray();
                foreach (string expected in new[] {
                        "lib/net10.0/geometry3Sharp.Clipper2.dll", "lib/net10.0/geometry3Sharp.Clipper2.xml",
                        "lib/netstandard2.0/geometry3Sharp.Clipper2.dll", "lib/netstandard2.0/geometry3Sharp.Clipper2.xml",
                        "README.md", "LICENSE" })
                    check(entries.Contains(expected), "package contains " + expected);
                ZipArchiveEntry nuspec = zip.Entries.First(e => e.FullName.EndsWith(".nuspec"));
                string spec = new StreamReader(nuspec.Open()).ReadToEnd();
                check(spec.Contains("id=\"Clipper2\""), "depends on Clipper2");
                check(spec.Contains("id=\"geometry3Sharp\""), "depends on geometry3Sharp");
            }
        }

        string asm = typeof(Clipper2Polygons).Assembly.Location;
        Console.WriteLine("geometry3Sharp.Clipper2 from " + asm);
        check(File.Exists(Path.ChangeExtension(asm, ".xml")), "XML documentation next to the assembly");

        GeneralPolygon2d Rect(double x0, double y0, double x1, double y1) => new GeneralPolygon2d(new Polygon2d(new[] {
            new Vector2d(x0, y0), new Vector2d(x1, y0), new Vector2d(x1, y1), new Vector2d(x0, y1) }));
        Clipper2Result union = Clipper2Polygons.Union(new[] { Rect(0, 0, 2, 2), Rect(1, 1, 3, 3) });
        check(union.Ok && union.Polygons.Count == 1 && Math.Abs(union.Area - 7) < 1e-12, "union of two squares: " + union);
        Clipper2Result offset = Clipper2Polygons.Offset(union.Polygons, -0.25);
        check(offset.Ok && offset.Area > 3 && offset.Area < 7, "offset: " + offset);
        return failures == 0 ? 0 : 1;
    }
}
