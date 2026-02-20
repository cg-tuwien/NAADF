using NAADF.Common;
using NAADF.World.Model;
using System;

namespace NAADF.World.Data
{
    public class EntityData
    {
        public string name;
        public Point3 size;
        public uint[] voxels;
        public int voxelStartIndexGpu;
        public ModelData model;

        public EntityData(ModelData model)
        {
            this.model = model;
            name = model.name;
            size = model.size;

            int voxelCount = size.X * size.Y * size.Z;
            voxels = new uint[voxelCount];
            for (int z = 0; z < size.Z; ++z)
            {
                for (int y = 0; y < size.Y; ++y)
                {
                    for (int x = 0; x < size.X; ++x)
                    {
                        uint type = 0;
                        Point3 modelChunkPos = new Point3(x / 16, y / 16, z / 16);
                        int modelChunkIndex = modelChunkPos.X + modelChunkPos.Y * model.sizeInChunks.X + modelChunkPos.Z * model.sizeInChunks.X * model.sizeInChunks.Y;
                        uint modelChunk = model.dataChunk[modelChunkIndex];
                        if ((modelChunk >> 30) == 2)
                        {
                            Point3 modelBlockPosInChunk = new Point3(x % 16, y % 16, z % 16) / 4;
                            int modelBlockIndex = modelBlockPosInChunk.X + modelBlockPosInChunk.Y * 4 + modelBlockPosInChunk.Z * 16;
                            uint modelBlock = model.dataBlock[(modelChunk & 0x3FFFFFFF) + modelBlockIndex];
                            if ((modelBlock >> 30) == 2)
                            {
                                Point3 modelVoxelPosInChunk = new Point3(x % 4, y % 4, z % 4);
                                int modelVoxelIndex = modelVoxelPosInChunk.X + modelVoxelPosInChunk.Y * 4 + modelVoxelPosInChunk.Z * 16;
                                uint modelVoxelComp = model.dataVoxel[(modelBlock & 0x3FFFFFFF) + modelVoxelIndex / 2];
                                type = (((modelVoxelIndex % 2) == 0) ? modelVoxelComp : (modelVoxelComp >> 16)) & 0x7FFF;
                            }
                            else if ((modelBlock >> 30) == 1)
                                type = modelBlock & 0x3FFFFFFF;
                        }
                        else if ((modelChunk >> 30) == 1)
                            type = modelChunk & 0x3FFFFFFF;

                        if (type > 0)
                            type |= 0x80000000;
                        voxels[x + y * size.X + z * size.X * size.Y] = type;
                    }
                }
            }

            const int MASK_MX = 0x3D; //0b111101
            const int MASK_PX = 0x3E; //0b111110
            const int MASK_MY = 0x37; //0b110111
            const int MASK_PY = 0x3B; //0b111011
            const int MASK_MZ = 0x1F; //0b011111
            const int MASK_PZ = 0x2F; //0b101111
            for (int i = 0; i < 31; i++)
            {
                // X
                for (int v = 0; v < voxelCount; ++v)
                {
                    int x = v % size.X;
                    uint curVoxel = voxels[v];
                    if ((curVoxel & 0x80000000) != 0)
                        continue;
                    if (x > 0)
                        addBounds(v, MASK_MX, -1, 0, ref curVoxel);
                    if (x + 1 < size.X)
                        addBounds(v, MASK_PX, 1, 5, ref curVoxel);
                    voxels[v] = curVoxel;
                }
                // Y
                for (int v = 0; v < voxelCount; ++v)
                {
                    int y = (v / size.X) % size.Y;
                    uint curVoxel = voxels[v];
                    if ((curVoxel & 0x80000000) != 0)
                        continue;
                    if (y > 0)
                        addBounds(v, MASK_MY, -size.X, 10, ref curVoxel);
                    if (y + 1 < size.Y)
                        addBounds(v, MASK_PY, size.X, 15, ref curVoxel);
                    voxels[v] = curVoxel;
                }
                // Z
                for (int v = 0; v < voxelCount; ++v)
                {
                    int z = (v / (size.X * size.Y)) % size.Z;
                    uint curVoxel = voxels[v];
                    if ((curVoxel & 0x80000000) != 0)
                        continue;
                    if (z > 0)
                        addBounds(v, MASK_MZ, -(size.X * size.Y), 20, ref curVoxel);
                    if (z + 1 < size.Z)
                        addBounds(v, MASK_PZ, (size.X * size.Y), 25, ref curVoxel);
                    voxels[v] = curVoxel;
                }

            }
        }

        private void addBounds(int curIndex, uint mask, int directionOffset, uint boundsLocation, ref uint curVoxel)
        {
            uint neighbor = voxels[curIndex + directionOffset];
            if ((neighbor & 0x80000000) == 0)
            {
                if ((checkMatchingBoundCell(neighbor, curVoxel) & mask) == mask)
                    curVoxel += 1u << (int)boundsLocation;
            }
        }

        private uint checkMatchingBoundCell(uint neighbor, uint curVoxel)
        {
            uint mask = 0;
            mask |= Convert.ToUInt32(((neighbor >> 0) & 0x1F) >= ((curVoxel >> 0) & 0x1F)) << 0; //-x  
            mask |= Convert.ToUInt32(((neighbor >> 5) & 0x1F) >= ((curVoxel >> 5) & 0x1F)) << 1; //+x
            mask |= Convert.ToUInt32(((neighbor >> 10) & 0x1F) >= ((curVoxel >> 10) & 0x1F)) << 2; //-y
            mask |= Convert.ToUInt32(((neighbor >> 15) & 0x1F) >= ((curVoxel >> 15) & 0x1F)) << 3; //+y
            mask |= Convert.ToUInt32(((neighbor >> 20) & 0x1F) >= ((curVoxel >> 20) & 0x1F)) << 4; //-z
            mask |= Convert.ToUInt32(((neighbor >> 25) & 0x1F) >= ((curVoxel >> 25) & 0x1F)) << 5; //+z

            return mask;
        }
    }
}
