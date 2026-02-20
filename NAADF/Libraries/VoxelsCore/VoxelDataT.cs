using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Voxels {
    /// <summary>
    /// Represents a set of voxels in a fixed size grid.
    /// NOTE: XY is the horizontal plane and Z is the vertical axis.
    /// </summary>
    public abstract class VoxelData<T> : VoxelData where T:struct, IEquatable<T> {
        Dictionary<XYZ,Chunk> chunks = new Dictionary<XYZ, Chunk>();

        public class Chunk {
            T[] voxels = new T[Size*Size*Size];

            /// <summary>
            /// Count of visible voxels in chunk.
            /// </summary>
            public int Count { get; private set; }

            public const int Size = 32;

            int IndexOf(XYZ p) {
                return p.X * (Size * Size) + p.Y * Size + p.Z;
            }

            public T this[XYZ p] {
                get {
                    return voxels[IndexOf(p)];
                }
                set {
                    var i = IndexOf(p);
                    var oldValue = voxels[i];
                    if (!oldValue.Equals(value)) {
                        voxels[i] = value;

                        if (oldValue.Equals(default)) {
                            Count++;
                        }
                        else {
                            Count--;
                        }
                    }
                }
            }
        }

        public VoxelData(XYZ size) : base(size) {}

        public sealed override int Count {
            get { 
                var count = 0;
                foreach (var chunk in chunks.Values) {
                    count += chunk.Count;
                }
                return count;
            }
        }

        protected T Get(XYZ p) {
            if (IsValid(p)) {
                var c = p / Chunk.Size;
                if (chunks.TryGetValue(c, out var chunk)) {
                    return chunk[p % Chunk.Size];
                }
            }
            return default;
        }

        protected void Set(XYZ p, T value) {
            if (IsValid(p)) {
                var c = p / Chunk.Size;
                if (!chunks.TryGetValue(c, out var chunk)) {
                    // Chunk doesn't currently exist - add one unless setting to invisible
                    if (!value.Equals(default)) {
                        chunks.Add(c, chunk = new Chunk());
                    }
                }

                if (chunk != null) {
                    chunk[p % Chunk.Size] = value;

                    // Chunk now entirely invisible - remove it
                    if (chunk.Count == 0) {
                        chunks.Remove(c);
                    }
                }
            }
            else {
                throw new ArgumentOutOfRangeException("p", p, "point not in voxel data set.");
            }
        }
    }
}
