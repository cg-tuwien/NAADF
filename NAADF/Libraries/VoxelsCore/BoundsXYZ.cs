using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxels {
    /// <summary>
    /// A Voxel bounding box where Min and Max are both inclusive.
    /// A single vox would have Min and Max set to the same value.
    /// </summary>
    public class BoundsXYZ {
        public XYZ Min;
        public XYZ Max;

        public BoundsXYZ() {}

        public BoundsXYZ(XYZ size) {
            // Origin is 0,0,0
            // Half voxels aren't allowed so the center is slightly off for uneven voxel dimensions
            Min = -size / 2;
            Max = Min+size - XYZ.One;
        }

        public BoundsXYZ(XYZ min, XYZ max) {
            Min = min;
            Max = max;
        }

        public XYZ Size => (Max - Min + XYZ.One);

        public void Add(BoundsXYZ other) {
            if (other.Min.X < Min.X) { Min.X = other.Min.X; }
            if (other.Min.Y < Min.Y) { Min.Y = other.Min.Y; }
            if (other.Min.Z < Min.Z) { Min.Z = other.Min.Z; }

            if (other.Max.X > Max.X) { Max.X = other.Max.X; }
            if (other.Max.Y > Max.Y) { Max.Y = other.Max.Y; }
            if (other.Max.Z > Max.Z) { Max.Z = other.Max.Z; }
        }

        public BoundsXYZ Transform(Matrix4x4 matrix) {
            var min = Min.Transform(matrix);
            var max = Max.Transform(matrix);

            if (max.X < min.X) { Swap(ref min.X, ref max.X); }
            if (max.Y < min.Y) { Swap(ref min.Y, ref max.Y); }
            if (max.Z < min.Z) { Swap(ref min.Z, ref max.Z); }

            return new BoundsXYZ() {
                Min = min,
                Max = max,
            };
        }

        static void Swap(ref int a, ref int b) {
            var t = a;
            a = b;
            b = t;
        }

        public static BoundsXYZ CreateEmpty() { return new BoundsXYZ() { Min = XYZ.Max, Max = XYZ.Min }; }
    }
}
    