using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace g3
{


    /// <summary>
    /// Generate a mesh of a sphere by first generating a mesh of a cube, 
    /// and then normalizing the vertices and moving them to sphere of desired radius.
    /// The sphere is centered at Box.Center (the origin by default); EdgeVertices (inherited, default 8) sets the
    /// resolution, giving 12*(EdgeVertices-1)^2 triangles. All vertices lie exactly on the sphere, so the mesh is
    /// slightly smaller than the true sphere (volume and area converge to 4/3 pi r^3 and 4 pi r^2 from below).
    /// With the default NoSharedVertices = false the mesh is closed, with outward normals and genus 0.
    /// Example: <c>DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();</c>
    /// </summary>
    public class Sphere3Generator_NormalizedCube : GridBox3Generator
    {
        /// <summary> sphere radius </summary>
        public double Radius = 1.0;

        /// <summary> how cube points are mapped to the sphere (currently always CubeMapping, the field is not public) </summary>
        public enum NormalizationTypes
        {
            NormalizedVector,
            CubeMapping             // produces more even distribution of quads
                                    // see http://catlikecoding.com/unity/tutorials/cube-sphere/
                                    // or http://mathproofs.blogspot.ca/2005/07/mapping-cube-to-sphere.html
        }
        NormalizationTypes NormalizeType = NormalizationTypes.CubeMapping;

        public override MeshGenerator Generate()
        {
            base.Generate();
            for ( int i = 0; i < vertices.Count; ++i ) {
                Vector3d v = vertices[i] - Box.Center;
                if (NormalizeType == NormalizationTypes.CubeMapping) {
                    double x = v.Dot(Box.AxisX) / Box.Extent.x;
                    double y = v.Dot(Box.AxisY) / Box.Extent.y;
                    double z = v.Dot(Box.AxisZ) / Box.Extent.z;
                    double x2 = x * x, y2 = y * y, z2 = z * z;
                    double sx = x * Math.Sqrt(1.0 - y2*0.5 - z2*0.5 + y2*z2/3.0);
                    double sy = y * Math.Sqrt(1.0 - x2*0.5 - z2*0.5 + x2*z2/3.0);
                    double sz = z * Math.Sqrt(1.0 - x2*0.5 - y2*0.5 + x2*y2/3.0);
                    v = sx*Box.AxisX + sy*Box.AxisY + sz*Box.AxisZ;
                }
                v.Normalize();
                vertices[i] = Box.Center + Radius*v;
                normals[i] = (Vector3f)v;
            }

            return this;
        }

    }






}
