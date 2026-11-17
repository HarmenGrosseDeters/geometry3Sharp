using System;

namespace g3
{
    /// <summary>
    /// Double-precision coordinate frame (origin and orientation), the counterpart of <see cref="Frame3f"/> for results
    /// whose origin must keep double precision (eg a point on a mesh at UTM coordinates: a float origin is off by
    /// up to 0.25 m there). Minimal on purpose: construction, axes, mapping of points and vectors, conversion.
    /// </summary>
    public struct Frame3d
    {
        Quaterniond rotation;
        Vector3d origin;

        public static readonly Frame3d Identity = new Frame3d(Vector3d.Zero, Quaterniond.Identity);

        public Frame3d(Vector3d origin) { rotation = Quaterniond.Identity; this.origin = origin; }

        /// <summary> Frame at origin with the Z axis along setZ (the minimal rotation from the world Z axis). </summary>
        public Frame3d(Vector3d origin, Vector3d setZ)
        {
            rotation = Quaterniond.FromTo(Vector3d.AxisZ, setZ);
            this.origin = origin;
        }

        public Frame3d(Vector3d origin, Quaterniond orientation) { rotation = orientation; this.origin = origin; }

        /// <summary> Frame with the given orthonormal axes. </summary>
        public Frame3d(Vector3d origin, Vector3d x, Vector3d y, Vector3d z)
        {
            this.origin = origin;
            rotation = new Matrix3d(x, y, z, false).ToQuaternion();
        }

        public Quaterniond Rotation { get { return rotation; } set { rotation = value; } }
        public Vector3d Origin { get { return origin; } set { origin = value; } }
        public Vector3d X { get { return rotation * Vector3d.AxisX; } }
        public Vector3d Y { get { return rotation * Vector3d.AxisY; } }
        public Vector3d Z { get { return rotation * Vector3d.AxisZ; } }

        /// <summary> Map a point from world coordinates into the local coordinates of the frame. </summary>
        public Vector3d ToFrameP(Vector3d v) { return rotation.Inverse() * (v - origin); }
        /// <summary> Map a point from local frame coordinates into world coordinates. </summary>
        public Vector3d FromFrameP(Vector3d v) { return rotation * v + origin; }
        /// <summary> Map a vector from world coordinates into local frame coordinates. </summary>
        public Vector3d ToFrameV(Vector3d v) { return rotation.Inverse() * v; }
        /// <summary> Map a vector from local frame coordinates into world coordinates. </summary>
        public Vector3d FromFrameV(Vector3d v) { return rotation * v; }

        /// <summary> Widening conversion: exact. </summary>
        public static implicit operator Frame3d(Frame3f f)
        {
            return new Frame3d((Vector3d)f.Origin, new Quaterniond(f.Rotation.x, f.Rotation.y, f.Rotation.z, f.Rotation.w));
        }

        /// <summary> Narrowing conversion: rounds the origin and the rotation to float. </summary>
        public static explicit operator Frame3f(Frame3d f)
        {
            return new Frame3f((Vector3f)f.origin, new Quaternionf((float)f.rotation.x, (float)f.rotation.y, (float)f.rotation.z, (float)f.rotation.w));
        }

        public override string ToString() { return string.Format("[Frame3d: Origin={0}, X={1}, Y={2}, Z={3}]", origin, X, Y, Z); }
    }
}
