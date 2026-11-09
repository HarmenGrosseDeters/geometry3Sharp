using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace g3
{
    /// <summary> Outcome of reading a height grid (<see cref="HeightGridReader"/>). </summary>
    public enum HeightGridStatus
    {
        Ok = 0,
        /// <summary> the file could not be opened or read </summary>
        IoError,
        /// <summary> no points / no values </summary>
        EmptyInput,
        /// <summary> XYZ: a line that is not "x y z" with finite x, y and a number or NaN as z </summary>
        BadLine,
        /// <summary> ESRI ASCII: a missing, repeated, unknown or invalid header entry </summary>
        BadHeader,
        /// <summary> ESRI ASCII: a data value that is not a number </summary>
        BadValue,
        /// <summary> XYZ: the points are not on one regular grid (spacing varies, or a point is off the nodes) </summary>
        NotOnGrid,
        /// <summary> XYZ: the node spacing in x and y differs, or a cell size is given twice and differs (cells must be square) </summary>
        NonSquareCells,
        /// <summary> XYZ: two points on the same node </summary>
        DuplicateNode,
        /// <summary> XYZ: the grid would have far more nodes than points (the points do not form a raster) </summary>
        TooSparse,
        /// <summary> the grid would have more nodes than <see cref="HeightGridReadOptions.MaxNodes"/> </summary>
        TooLarge,
        /// <summary> XYZ: fewer than two different x or y values, so that no spacing exists </summary>
        TooSmall,
        /// <summary> ESRI ASCII: fewer values than ncols * nrows </summary>
        Truncated,
        /// <summary> ESRI ASCII: more values than ncols * nrows </summary>
        ExtraData
    }

    public struct HeightGridReadResult
    {
        public HeightGridStatus Status;
        /// <summary> what is wrong, with the line number where there is one; "" if Ok </summary>
        public string Message;
        /// <summary> the grid if Status is Ok, else null </summary>
        public HeightGrid Grid;
        public bool Ok { get { return Status == HeightGridStatus.Ok; } }

        public static HeightGridReadResult Fail(HeightGridStatus status, string message)
        {
            return new HeightGridReadResult() { Status = status, Message = message };
        }
    }

    public class HeightGridReadOptions
    {
        /// <summary> a height equal to this is no data (NaN in the grid). XYZ files have no header for it; ESRI ASCII
        /// takes NODATA_value from its header, and this value is used if the header has none. null: none. </summary>
        public double? NoDataValue = null;
        /// <summary> the largest number of grid nodes read (guards memory) </summary>
        public long MaxNodes = 1L << 26;
        /// <summary> XYZ: a grid with more than this many nodes per point read (and more than 1024) is rejected as TooSparse </summary>
        public int MaxNodesPerPoint = 64;
        /// <summary> XYZ: node positions may differ from the regular lattice by this fraction of the cell size </summary>
        public double GridTolerance = 1e-3;
    }

    /// <summary>
    /// Readers for height grids (DGM / DTM rasters). Heights are node values: node (i, j) of the <see cref="HeightGrid"/>
    /// is a point of the file (XYZ) or the center of a raster cell (ESRI ASCII). All readers return a status instead of
    /// throwing on bad input.
    /// <list type="bullet">
    /// <item>XYZ ASCII: one point per line, "x y z" separated by blanks, tabs, commas or semicolons (further columns are
    /// ignored; empty lines and lines starting with # are skipped). The points need not be sorted. They must lie on
    /// one regular square lattice; nodes without a point are NaN (gaps). Otherwise the status says why not.</item>
    /// <item>ESRI ASCII Grid (.asc): the header ncols, nrows, xllcorner | xllcenter, yllcorner | yllcenter, cellsize
    /// (or dx = dy), optional NODATA_value, then nrows rows of ncols values, north row first.</item>
    /// </list>
    /// </summary>
    public static class HeightGridReader
    {
        static readonly char[] Separators = { ' ', '\t', ',', ';' };

        public static HeightGridReadResult ReadXyzFile(string path, HeightGridReadOptions options = null)
        {
            try {
                using (var reader = new StreamReader(path))
                    return ReadXyz(reader, options);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) {
                return HeightGridReadResult.Fail(HeightGridStatus.IoError, e.Message);
            }
        }

        public static HeightGridReadResult ReadEsriAsciiFile(string path, HeightGridReadOptions options = null)
        {
            try {
                using (var reader = new StreamReader(path))
                    return ReadEsriAscii(reader, options);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) {
                return HeightGridReadResult.Fail(HeightGridStatus.IoError, e.Message);
            }
        }

        static bool TryNumber(string s, out double v)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // ---------------------------------------------------------------- XYZ

        public static HeightGridReadResult ReadXyz(TextReader reader, HeightGridReadOptions options = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (options == null) options = new HeightGridReadOptions();
            var px = new List<double>(); var py = new List<double>(); var pz = new List<double>();
            string line; int lineNo = 0;
            try {
                while ((line = reader.ReadLine()) != null) {
                    ++lineNo;
                    string[] t = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length == 0 || t[0][0] == '#') continue;
                    double x, y, z;
                    if (t.Length < 3 || !TryNumber(t[0], out x) || !TryNumber(t[1], out y) || !TryNumber(t[2], out z)
                        || double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y) || double.IsInfinity(z))
                        return HeightGridReadResult.Fail(HeightGridStatus.BadLine, "line " + lineNo + " is not 'x y z' with finite x and y");
                    px.Add(x); py.Add(y);
                    pz.Add(options.NoDataValue.HasValue && z == options.NoDataValue.Value ? double.NaN : z);
                }
            } catch (IOException e) {
                return HeightGridReadResult.Fail(HeightGridStatus.IoError, e.Message);
            }
            int n = px.Count;
            if (n == 0)
                return HeightGridReadResult.Fail(HeightGridStatus.EmptyInput, "no points");

            double x0, dx, y0, dy;
            int nx, ny;
            if (!Lattice(px, options.GridTolerance, out x0, out dx, out nx, out string errX))
                return HeightGridReadResult.Fail(errX == null ? HeightGridStatus.TooSmall : HeightGridStatus.NotOnGrid, errX ?? "fewer than two different x values: no grid spacing");
            if (!Lattice(py, options.GridTolerance, out y0, out dy, out ny, out string errY))
                return HeightGridReadResult.Fail(errY == null ? HeightGridStatus.TooSmall : HeightGridStatus.NotOnGrid, errY ?? "fewer than two different y values: no grid spacing");
            if (Math.Abs(dx - dy) > 1e-4 * Math.Max(dx, dy))
                return HeightGridReadResult.Fail(HeightGridStatus.NonSquareCells, "spacing " + dx.ToString("R", CultureInfo.InvariantCulture) + " in x, " + dy.ToString("R", CultureInfo.InvariantCulture) + " in y");

            long nodes = (long)nx * ny;
            if (nodes > Math.Max(1024L, (long)options.MaxNodesPerPoint * n))
                return HeightGridReadResult.Fail(HeightGridStatus.TooSparse, n + " points on " + nx + " x " + ny + " nodes");
            if (nodes > Math.Min(options.MaxNodes, 1L << 30))
                return HeightGridReadResult.Fail(HeightGridStatus.TooLarge, nx + " x " + ny + " nodes exceed MaxNodes " + options.MaxNodes);

            double[] zs = new double[nodes];
            for (long k = 0; k < nodes; ++k) zs[k] = double.NaN;
            var seen = new BitArray((int)nodes);
            for (int k = 0; k < n; ++k) {
                int i = (int)Math.Round((px[k] - x0) / dx), j = (int)Math.Round((py[k] - y0) / dx);
                int idx = i + nx * j;
                if (seen[idx])
                    return HeightGridReadResult.Fail(HeightGridStatus.DuplicateNode, "two points at node (" + i + ", " + j + ")");
                seen[idx] = true;
                zs[idx] = pz[k];
            }
            return new HeightGridReadResult() { Status = HeightGridStatus.Ok, Message = "", Grid = new HeightGrid(zs, nx, ny, x0, y0, dx) };
        }

        /// <summary>
        /// The regular lattice of one coordinate: origin = smallest value, spacing = smallest gap (refined over the full
        /// extent), count. False with error == null: fewer than two different values; with an error: not on a lattice.
        /// </summary>
        static bool Lattice(List<double> v, double tolerance, out double origin, out double step, out int count, out string error)
        {
            origin = 0; step = 0; count = 0; error = null;
            var set = new HashSet<double>(v);
            var u = new double[set.Count];
            set.CopyTo(u);
            Array.Sort(u);
            if (u.Length < 2) return false;
            double min = double.MaxValue;
            for (int k = 1; k < u.Length; ++k) min = Math.Min(min, u[k] - u[k - 1]);
            double extent = u[u.Length - 1] - u[0];
            double cells = Math.Round(extent / min);
            if (!(cells < int.MaxValue - 1)) {
                error = "spacing " + min.ToString("R", CultureInfo.InvariantCulture) + " over extent " + extent.ToString("R", CultureInfo.InvariantCulture) + " is not a regular grid";
                return false;
            }
            origin = u[0];
            step = extent / cells;
            double eps = 4 * Math.Max(Math.Abs(u[0]), Math.Abs(u[u.Length - 1])) * 2.220446049250313e-16;
            double tol = tolerance * step + eps;
            for (int k = 0; k < u.Length; ++k) {
                double f = (u[k] - origin) / step;
                if (Math.Abs(f - Math.Round(f)) * step > tol) {
                    error = "value " + u[k].ToString("R", CultureInfo.InvariantCulture) + " is not on the lattice of spacing " + step.ToString("R", CultureInfo.InvariantCulture) + " from " + origin.ToString("R", CultureInfo.InvariantCulture);
                    return false;
                }
            }
            count = (int)cells + 1;
            return true;
        }

        // ---------------------------------------------------------------- ESRI ASCII

        sealed class Tokens
        {
            readonly TextReader r;
            readonly StringBuilder sb = new StringBuilder();
            public Tokens(TextReader reader) { r = reader; }
            public string Next()
            {
                sb.Clear();
                int c;
                while ((c = r.Read()) >= 0 && char.IsWhiteSpace((char)c)) { }
                if (c < 0) return null;
                do {
                    sb.Append((char)c);
                    if (sb.Length > 1024) return sb.ToString();     // no number is this long: let the caller reject it
                } while ((c = r.Read()) >= 0 && !char.IsWhiteSpace((char)c));
                return sb.ToString();
            }
        }

        static bool IsKey(string s)
        {
            switch (s.ToLowerInvariant()) {
                case "ncols": case "nrows": case "xllcorner": case "xllcenter": case "yllcorner": case "yllcenter":
                case "cellsize": case "dx": case "dy": case "nodata_value":
                    return true;
            }
            return false;
        }

        public static HeightGridReadResult ReadEsriAscii(TextReader reader, HeightGridReadOptions options = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (options == null) options = new HeightGridReadOptions();
            var tokens = new Tokens(reader);
            var h = new Dictionary<string, double>();
            string tok;
            try {
                tok = tokens.Next();
                if (tok == null) return HeightGridReadResult.Fail(HeightGridStatus.EmptyInput, "empty file");
                while (tok != null && IsKey(tok)) {
                    string key = tok.ToLowerInvariant();
                    string val = tokens.Next();
                    double v;
                    if (val == null || !TryNumber(val, out v) || double.IsNaN(v) || double.IsInfinity(v))
                        return HeightGridReadResult.Fail(HeightGridStatus.BadHeader, "header entry " + key + " has no valid number");
                    if (h.ContainsKey(key))
                        return HeightGridReadResult.Fail(HeightGridStatus.BadHeader, "header entry " + key + " is repeated");
                    h[key] = v;
                    tok = tokens.Next();
                }
            } catch (IOException e) {
                return HeightGridReadResult.Fail(HeightGridStatus.IoError, e.Message);
            }

            double ncolsD, nrowsD;
            if (!h.TryGetValue("ncols", out ncolsD) || !h.TryGetValue("nrows", out nrowsD))
                return HeightGridReadResult.Fail(HeightGridStatus.BadHeader, "ncols / nrows missing");
            if (ncolsD < 1 || nrowsD < 1 || ncolsD != Math.Floor(ncolsD) || nrowsD != Math.Floor(nrowsD) || ncolsD > int.MaxValue || nrowsD > int.MaxValue)
                return HeightGridReadResult.Fail(HeightGridStatus.BadHeader, "ncols / nrows must be positive integers");
            int nx = (int)ncolsD, ny = (int)nrowsD;
            long nodes = (long)nx * ny;
            if (nodes > Math.Min(options.MaxNodes, 1L << 30))
                return HeightGridReadResult.Fail(HeightGridStatus.TooLarge, nx + " x " + ny + " values exceed MaxNodes " + options.MaxNodes);

            double cell;
            if (h.TryGetValue("cellsize", out cell)) {
                double ddx, ddy;
                if ((h.TryGetValue("dx", out ddx) && ddx != cell) || (h.TryGetValue("dy", out ddy) && ddy != cell))
                    return HeightGridReadResult.Fail(HeightGridStatus.NonSquareCells, "cellsize and dx / dy differ");
            } else {
                double ddx, ddy;
                if (!h.TryGetValue("dx", out ddx) || !h.TryGetValue("dy", out ddy))
                    return HeightGridReadResult.Fail(HeightGridStatus.BadHeader, "cellsize missing");
                if (ddx != ddy)
                    return HeightGridReadResult.Fail(HeightGridStatus.NonSquareCells, "dx " + ddx.ToString("R", CultureInfo.InvariantCulture) + " differs from dy " + ddy.ToString("R", CultureInfo.InvariantCulture));
                cell = ddx;
            }
            if (!(cell > 0))
                return HeightGridReadResult.Fail(HeightGridStatus.BadHeader, "cellsize must be positive");

            double x0, y0, t;
            bool hasCornerX = h.TryGetValue("xllcorner", out t), hasCenterX = h.TryGetValue("xllcenter", out double cx);
            bool hasCornerY = h.TryGetValue("yllcorner", out double ty), hasCenterY = h.TryGetValue("yllcenter", out double cy);
            if (hasCornerX == hasCenterX || hasCornerY == hasCenterY)
                return HeightGridReadResult.Fail(HeightGridStatus.BadHeader, "exactly one of xllcorner / xllcenter and one of yllcorner / yllcenter is needed");
            x0 = hasCenterX ? cx : t + cell / 2;
            y0 = hasCenterY ? cy : ty + cell / 2;

            double? noData = options.NoDataValue;
            if (h.TryGetValue("nodata_value", out double nd)) noData = nd;

            // the values grow with the input: a header that claims more than the file holds allocates nothing
            var vals = new List<double>((int)Math.Min(nodes, 1 << 16));
            try {
                while (vals.Count < nodes) {
                    if (tok == null)
                        return HeightGridReadResult.Fail(HeightGridStatus.Truncated, vals.Count + " of " + nodes + " values");
                    double v;
                    if (!TryNumber(tok, out v) || double.IsInfinity(v))
                        return HeightGridReadResult.Fail(HeightGridStatus.BadValue, "value " + (vals.Count + 1) + " is not a number");
                    vals.Add(noData.HasValue && v == noData.Value ? double.NaN : v);
                    tok = tokens.Next();
                }
                if (tok != null)
                    return HeightGridReadResult.Fail(HeightGridStatus.ExtraData, "more than " + nodes + " values");
            } catch (IOException e) {
                return HeightGridReadResult.Fail(HeightGridStatus.IoError, e.Message);
            }

            double[] z = new double[nodes];
            for (int r = 0; r < ny; ++r)                  // the file starts with the north row
                vals.CopyTo(r * nx, z, nx * (ny - 1 - r), nx);
            return new HeightGridReadResult() { Status = HeightGridStatus.Ok, Message = "", Grid = new HeightGrid(z, nx, ny, x0, y0, cell) };
        }
    }
}
