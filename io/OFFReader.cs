using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace g3
{
	//
	// Parse ascii OFF mesh format, into one mesh. Polygons are fan-triangulated, vertex colors are ignored.
	// https://en.wikipedia.org/wiki/OFF_(file_format)
	// Internal; use StandardMeshReader with a .off file.
	// 
    class OFFReader : IMeshReader
    {
        // connect to this to get warning messages
		public event ParsingMessagesHandler warningEvent;

        //int nWarningLevel = 0;      // 0 == no diagnostics, 1 == basic, 2 == crazy
        Dictionary<string, int> warningCount = new Dictionary<string, int>();


        public IOReadResult Read(BinaryReader reader, ReadOptions options, IMeshBuilder builder)
        {
            return new IOReadResult(IOCode.FormatNotSupportedError, "binary read not supported for OFF format");
        }

        public IOReadResult Read(TextReader reader, ReadOptions options, IMeshBuilder builder)
        {
			// format is:
			//
			// OFF
			// VCOUNT TCOUNT     (2 ints)
			// x y z
			// ...
			// 3 va vb vc
			// ...
			//

            // header: [ST][C][N]OFF (texture coordinates, colors, normals follow the vertex coordinates and are
            // ignored), optionally after comment lines and with the counts on the same line. 4OFF / nOFF (other
            // dimensions) and binary OFF are not supported.
            int nLines = 0;
            string[] header = null;
            while (reader.Peek() >= 0) {
                string line = reader.ReadLine();
                nLines++;
                string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0 || tokens[0].StartsWith("#"))
                    continue;
                header = tokens;
                break;
            }
            if (header == null || header[0].EndsWith("OFF") == false
                || header[0].Substring(0, header[0].Length - 3).Any(ch => "STCN".IndexOf(ch) < 0))
                return new IOReadResult(IOCode.FileParsingError, "ascii OFF file must start with an OFF header ([ST][C][N]OFF)");
            if (header.Length > 1 && header[1] == "BINARY")
                return new IOReadResult(IOCode.FormatNotSupportedError, "binary OFF is not supported");

            int nVertexCount = 0;
            int nTriangleCount = 0;

            string[] counts = header.Length >= 3 ? header.Skip(1).ToArray() : null;
            while (counts == null && reader.Peek() >= 0) {
                string line = reader.ReadLine();
                nLines++;
                string[] tokens = line.Split( (char[])null , StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;

                if (tokens[0].StartsWith("#"))
                    continue;

                if ( tokens.Length < 2 || tokens.Length > 3 )
                    return new IOReadResult(IOCode.FileParsingError, "first non-comment line of OFF must be vertex/tri/edge counts, found: " + line);
                counts = tokens;
            }
            if (counts == null)
                return new IOReadResult(IOCode.FileParsingError, "OFF file has no vertex/tri/edge counts");
            nVertexCount = int.Parse(counts[0], CultureInfo.InvariantCulture);
            nTriangleCount = int.Parse(counts[1], CultureInfo.InvariantCulture);
            //int nEdgeCount = int.Parse(counts[2], CultureInfo.InvariantCulture);


            builder.AppendNewMesh(false, false, false, false);

            int vi = 0;
            while ( vi < nVertexCount && reader.Peek() > 0 ) {
                string line = reader.ReadLine();
                nLines++;
                string[] tokens = line.Split( (char[])null , StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;

                if (tokens[0].StartsWith("#"))
                    continue;

                // further values on the line (normal, color, texture coordinates) are ignored
                if ( tokens.Length < 3 )
                    return new IOReadResult(IOCode.FileParsingError, "found invalid OFF vertex line: " + line);

                double x = Double.Parse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture);
                double y = Double.Parse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture);
                double z = Double.Parse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture);
                builder.AppendVertex(x, y, z);
                vi++;
            }
            if (vi < nVertexCount)
                return new IOReadResult(IOCode.FileParsingError,
                    string.Format("File specified {0} vertices but only found {1}", nVertexCount, vi));


            int ti = 0;
            while ( ti < nTriangleCount && reader.Peek() > 0 ) {
                string line = reader.ReadLine();
                nLines++;
                string[] tokens = line.Split( (char[])null , StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;

                if (tokens[0].StartsWith("#"))
                    continue;

                if ( tokens.Length < 4 )
                    emit_warning("found invalid OFF triangle line: " + line);

                int nV = int.Parse(tokens[0], CultureInfo.InvariantCulture);
                if ( nV < 3 || tokens.Length < nV + 1 ) {
                    emit_warning("found invalid OFF polygon line: " + line);
                    ti++;
                    continue;
                }

                // polygons with more than 3 vertices are triangulated as a fan
                int a = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                for (int j = 2; j < nV; ++j) {
                    int b = int.Parse(tokens[j], CultureInfo.InvariantCulture);
                    int c = int.Parse(tokens[j + 1], CultureInfo.InvariantCulture);
                    builder.AppendTriangle(a, b, c);
                }
                ti++;
            }
            if (ti < nTriangleCount)
                emit_warning(string.Format("File specified {0} triangles but only found {1}", nTriangleCount, ti));
            
            return new IOReadResult(IOCode.Ok, "");
        }

       

        private void emit_warning(string sMessage)
        {
            string sPrefix = sMessage.Substring(0, 15);
            int nCount = warningCount.ContainsKey(sPrefix) ? warningCount[sPrefix] : 0;
            nCount++; warningCount[sPrefix] = nCount;
            if (nCount > 10)
                return;
            else if (nCount == 10)
                sMessage += " (additional message surpressed)";

            var e = warningEvent;
            if (e != null)
                e(sMessage, null);
        }

    }
}
