using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.World.Generator
{
    public class WorldGenerator
    {
        public virtual bool IsValid()
        {
            return false;
        }

        public virtual void CopyToChunkData(Point3 chunkPos, Point3 chunkSize, Point3 sizeInVoxels, StructuredBuffer chunkDataGpu, int worldSizeY, int voxelGroupSize = 1)
        {

        }
        public virtual void CopyToChunkDataTexture3D(Point3 chunkPos, Point3 chunkSize, Point3 sizeInVoxels, Texture3D chunkDataGpu, int worldSizeY, int voxelGroupSize = 1)
        {

        }
    }
}
