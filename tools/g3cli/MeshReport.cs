// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Linq;
using System.Text.Json.Nodes;
using g3;
using gs;

namespace g3cli
{
    /// <summary> Metrics of a mesh as JSON (the "mesh" objects of the command results). </summary>
    public static class MeshReport
    {
        /// <summary> null for NaN and infinity, which JSON cannot represent </summary>
        public static JsonNode Num(double d)
        {
            return double.IsFinite(d) ? JsonValue.Create(d) : null;
        }

        public static JsonArray Vec(Vector3d v)
        {
            return new JsonArray(Num(v.x), Num(v.y), Num(v.z));
        }

        /// <summary>
        /// Counts, closedness, volume, area, bounds and the clean-solid checks of MeshRobustRepair. The
        /// self-intersection count (exact, MeshRepairMetrics) is the expensive part; it is skipped (null)
        /// if selfIntersections is false.
        /// </summary>
        public static JsonObject Metrics(DMesh3 mesh, bool selfIntersections = true)
        {
            var o = new JsonObject();
            o["vertices"] = mesh.VertexCount;
            o["triangles"] = mesh.TriangleCount;
            o["edges"] = mesh.EdgeCount;

            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            o["components"] = comps.Count;

            int boundaryEdges = MeshIterators.BoundaryEdges(mesh).Count();
            int bowties = MeshIterators.BowtieVertices(mesh).Count();
            bool closed = mesh.TriangleCount > 0 && boundaryEdges == 0;
            o["boundary_edges"] = boundaryEdges;
            o["bowtie_vertices"] = bowties;
            o["closed"] = closed;
            o["winding_closed"] = mesh.TriangleCount > 0 && ExactMeshBoolean.IsWindingClosed(mesh);

            AxisAlignedBox3d bounds = mesh.TriangleCount > 0 ? mesh.GetBounds() : AxisAlignedBox3d.Zero;
            double diag = bounds.DiagonalLength;
            double areaEps = 1e-12 * diag * diag;
            int degenerate = mesh.TriangleIndices().Count(t => mesh.GetTriArea(t) < areaEps);
            o["degenerate_triangles"] = degenerate;

            Vector2d va = MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex);
            // the signed volume is only meaningful for a closed mesh
            o["volume"] = closed ? Num(va.x) : null;
            o["area"] = Num(va.y);
            o["bounds"] = new JsonObject() {
                ["min"] = Vec(bounds.Min), ["max"] = Vec(bounds.Max),
                ["size"] = Vec(bounds.Diagonal), ["diagonal"] = Num(diag)
            };

            MeshMeasurements.GenusResult genus = MeshMeasurements.Genus(mesh);
            o["genus"] = genus.Valid ? genus.Genus : (JsonNode)null;

            int inverted = closed ? MeshPostRepairCleanup.FindInvertedComponents(mesh).Count : 0;
            o["inverted_components"] = inverted;

            int? selfInt = null;
            if (selfIntersections && mesh.TriangleCount > 0)
                selfInt = MeshRepairMetrics.SelfIntersections(mesh);
            o["self_intersections"] = selfInt;

            // the same checks, in the same order, as MeshRobustRepair.NotCleanReason
            string reason = mesh.TriangleCount == 0 ? "empty"
                : closed == false ? "not closed"
                : bowties > 0 ? "bowties"
                : degenerate > 0 ? "degenerate triangles"
                : inverted > 0 ? "inside-out components"
                : (selfInt ?? 0) > 0 ? "self-intersections" : "";
            o["clean"] = reason == "" && selfInt.HasValue;
            o["not_clean_reason"] = reason == "" ? (selfInt.HasValue ? null : "self-intersections not checked") : reason;
            return o;
        }
    }
}
