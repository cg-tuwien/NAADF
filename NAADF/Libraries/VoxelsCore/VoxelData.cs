using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Voxels {
    /// <summary>
    /// Represents a set of voxels in a fixed size grid.
    /// NOTE: XY is the horizontal plane and Z is the vertical axis.
    /// </summary>
    public abstract class VoxelData : IEnumerable<XYZ> {

        public BoundsXYZ Bounds => new BoundsXYZ(XYZ.Zero, Size-XYZ.One);
        public XYZ Size { get; }

        public VoxelData(XYZ size) {
            this.Size = size;
        }

        public abstract int Count { get; }

        public bool IsValid(XYZ p) {
            return (p.X >= 0 && p.X < Size.X)
                && (p.Y >= 0 && p.Y < Size.Y)
                && (p.Z >= 0 && p.Z < Size.Z);
        }

        public abstract Voxel this[XYZ p] { get; set; }

        public void Add(VoxelData voxelData) {
            foreach (var p in voxelData) {
                this[p] = voxelData[p];
            }
        }

        protected abstract Color ColorOf(Voxel voxel);

        public Color ColorOf(XYZ p) {
            return ColorOf(this[p]);
        }

        public IEnumerator<XYZ> GetEnumerator() {
            for (var x = 0; x < Size.X; ++x) {
                for (var y = 0; y < Size.Y; ++y) {
                    for (var z = 0; z < Size.Z; ++z) {
                        yield return new XYZ(x, y, z);
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }
    }
}
