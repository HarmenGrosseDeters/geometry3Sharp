using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace g3
{

    /// <summary>
    /// Writes the internal .g3mesh binary format: a mesh count followed by each DMesh3 stored with gSerialization.
    /// Only DMesh3 meshes are supported (throws NotImplementedException otherwise), and only the BinaryWriter overload
    /// (the TextWriter overload throws NotSupportedException). Not intended for exchange with other software.
    /// </summary>
    public class BinaryG3Writer : IMeshWriter
    {
        public IOWriteResult Write(BinaryWriter writer, List<WriteMesh> vMeshes, WriteOptions options)
        {
            int nMeshes = vMeshes.Count;
            writer.Write(nMeshes);
            for (int k = 0; k < vMeshes.Count; ++k) {
                DMesh3 mesh = vMeshes[k].Mesh as DMesh3;
                if (mesh == null)
                    throw new NotImplementedException("BinaryG3Writer.Write: can only write DMesh3 meshes");
                gSerialization.Store(mesh, writer);
            }

            return new IOWriteResult(IOCode.Ok, "");
        }

        public IOWriteResult Write(TextWriter writer, List<WriteMesh> vMeshes, WriteOptions options)
        {
            throw new NotSupportedException("BinaryG3 Writer does not support ascii mode");
        }

    }



    /// <summary>
    /// Reads the internal .g3mesh binary format written by <see cref="BinaryG3Writer"/>, appending each stored DMesh3
    /// to the builder. Only the BinaryReader overload is supported (the TextReader overload throws NotSupportedException).
    /// Read errors throw; StandardMeshReader catches them and returns GenericReaderError. A vertex with a NaN or infinite
    /// coordinate returns FileParsingError (unless <see cref="ReadOptions.AllowNonFiniteCoordinates"/> is set).
    /// </summary>
    public class BinaryG3Reader : IMeshReader
    {
        public IOReadResult Read(BinaryReader reader, ReadOptions options, IMeshBuilder builder)
        {
            int nMeshes = reader.ReadInt32();
            var meshes = new List<DMesh3>();
            for (int k = 0; k < nMeshes; ++k) {
                DMesh3 m = new DMesh3();
                gSerialization.Restore(m, reader);
                // a NaN or infinite vertex coordinate fails here (FileParsingError), like in the other readers, unless
                // ReadOptions.AllowNonFiniteCoordinates is set
                if (options == null || options.AllowNonFiniteCoordinates == false) {
                    foreach (int vid in m.VertexIndices()) {
                        Vector3d v = m.GetVertex(vid);
                        if (ReadOptions.RejectsPosition(options, v.x, v.y, v.z))
                            return ReadOptions.NonFinitePosition(v.x, v.y, v.z);
                    }
                }
                meshes.Add(m);
            }
            // (appended only when every mesh passed: no partial result on an error)
            foreach (DMesh3 m in meshes)
                builder.AppendNewMesh(m);

            return new IOReadResult(IOCode.Ok, "");
        }

        public IOReadResult Read(TextReader reader, ReadOptions options, IMeshBuilder builder)
        {
            throw new NotSupportedException("BinaryG3Reader Writer does not support ascii mode");
        }

    }



}
