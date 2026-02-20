using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxels {
    public class VoxelDataColors : VoxelData<Color> {
        public VoxelDataColors(XYZ size) : base(size) { }

        public sealed override Voxel this[XYZ p] {
            get => new Voxel(Get(p));
            set => Set(p, value.Color);
        }

        protected sealed override Color ColorOf(Voxel voxel) {
            return voxel.Color;
        }
    }
}
