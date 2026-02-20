using System;
using System.Runtime.InteropServices;

namespace Voxels {
    /// <summary>
    /// The color or palette index of the voxel.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct Voxel : IEquatable<Voxel> {
        [FieldOffset(0)] public Color Color;
        [FieldOffset(0)] public uint Index;

        public Voxel(uint index) {
            this.Index = index;
            this.Color = new Color(index);
        }
        public Voxel(Color color) {
            this.Color = color;
            this.Index = color.RGBA;
        }

        public bool IsVisible { get { return Index != 0; } }

        public static readonly Voxel Empty;

        public static bool operator ==(Voxel a, Voxel b) {
            return a.Index == b.Index;
        }

        public static bool operator !=(Voxel a, Voxel b) {
            return a.Index != b.Index;
        }

        public bool Equals(Voxel other) {
            return this == other;
        }

        public override bool Equals(object obj) {
            return Equals((Voxel)obj);
        }

        public override int GetHashCode() {
            return Index.GetHashCode();
        }

        public override string ToString() {
            return Color.ToString();
        }
    }
}
