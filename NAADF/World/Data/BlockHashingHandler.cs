using Microsoft.Xna.Framework.Graphics;
using SharpDX;
using SharpDX.MediaFoundation;
using System;
using System.Diagnostics.Metrics;
using System.Numerics;
using Voxels;

namespace NAADF.World.Data
{
    public struct BlockValue
    {
        public uint voxelsPointer = 0x0; // EMPTY
        public uint blockUseCount = 0;
        public uint hash = 0;

        public BlockValue(uint voxelsPointer, uint blockUseCount, uint hash)
        {
            this.voxelsPointer = voxelsPointer;
            this.blockUseCount = blockUseCount;
            this.hash = hash;
        }
    }

    public class BlockHashingHandler : IDisposable
    {
        public uint[] coefficients;
        public Effect mapCopyShader;
        public BlockValue[] map;
        public StructuredBuffer mapGpu;
        public int mapSize = 0, minReservedCount;
        public float wantedEmptyRatio;

        private WorldData worldData;

        public BlockHashingHandler(WorldData worldData, int startSizeMap = 0, float wantedEmptyRatio = 0.5f, int minReservedCount = 64)
        {
            this.worldData = worldData;
            mapSize = Math.Max(1, startSizeMap);
            this.wantedEmptyRatio = wantedEmptyRatio;
            this.minReservedCount = minReservedCount;

            while (mapSize * wantedEmptyRatio < minReservedCount)
            {
                mapSize *= 2;
            }

            mapCopyShader = App.contentManager.Load<Effect>("shaders/world/data/mapCopy");

            coefficients = new uint[65];
            coefficients[64] = 1;
            for (int i = 64 - 1; i >= 0; --i)
            {
                coefficients[i] = 31 * coefficients[i + 1];
            }

            map = new BlockValue[mapSize];
            mapGpu = new StructuredBuffer(App.graphicsDevice, typeof(BlockValue), mapSize, BufferUsage.None, ShaderAccess.ReadWrite);
            mapGpu.SetData(map);

        }

        public uint getHashOfBlock(uint[] voxels, uint offset, out bool isAllTheSame)
        {
            uint hash = coefficients[0];
            uint firstVoxelTypeComp = voxels[offset];
            isAllTheSame = (firstVoxelTypeComp & 0xFFFF) == (firstVoxelTypeComp >> 16);
            for (int v = 0; v < 32; ++v)
            {
                uint voxelComp = voxels[offset + v];
                isAllTheSame &= voxelComp == firstVoxelTypeComp;
                hash += worldData.blockHashingHandler.coefficients[v * 2 + 1] * (voxelComp & 0x7FFF);
                hash += worldData.blockHashingHandler.coefficients[v * 2 + 2] * ((voxelComp >> 16) & 0x7FFF);
            }
            return hash;
        }

        public void SetNewUsedCount(uint count)
        {
            if (count + minReservedCount > wantedEmptyRatio * mapSize)
            {
                IncreaseSizeToNewCount(count);
            }
        }

        public uint AddBlock(uint hash, uint[] voxelData, int offset, out bool isNew)
        {
            uint hashBounds = hash & ((uint)mapSize - 1);
            int count = 0;
            isNew = false;
            while (count < 250)
            {
                uint voxelPointer = 0;

                BlockValue value = map[hashBounds];

                if (value.voxelsPointer == 0) // Found empty hash slot
                {
                    map[hashBounds].blockUseCount = 1;
                    uint newVoxelPointer = worldData.AddVoxels(new Span<uint>(voxelData, offset, 32));
                    map[hashBounds].hash = hash;
                    map[hashBounds].voxelsPointer = newVoxelPointer;
                    voxelPointer = newVoxelPointer;
                    isNew = true;

                    SetNewUsedCount(worldData.voxelCount / 64);
                }
                else // Fully written hash slot
                {
                    if (map[hashBounds].hash == hash)
                    {
                        bool isAllEqual = true;
                        for (int i = 0; i < 32; ++i)
                        {
                            uint voxelComp = worldData.dataVoxel[value.voxelsPointer + i];
                            uint other = voxelData[offset + i];
                            isAllEqual = isAllEqual && voxelData[offset + i] == voxelComp;
                        }
                        if (isAllEqual)
                        {
                            map[hashBounds].blockUseCount++;
                            voxelPointer = value.voxelsPointer;
                        }
                    }
                }
                if (voxelPointer != 0)
                    return voxelPointer;
                hashBounds = (hashBounds + 1) & ((uint)mapSize - 1);
                count++;

            }
            Console.WriteLine("ERROR");
            return 1;
        }

        public bool DeleteBlock(uint hash, uint pointer)
        {
            uint hashBounds = hash & ((uint)mapSize - 1);
            int count = 0;
            while (count < 250)
            {
                BlockValue value = map[hashBounds];

                if (value.voxelsPointer == 0) // Empty hash slot
                    return false;
                else // Fully written hash slot
                {
                    if (map[hashBounds].voxelsPointer == pointer)
                    {
                        uint newUseCount = --map[hashBounds].blockUseCount;
                        if (newUseCount == 0)
                            map[hashBounds].voxelsPointer = 0;
                        return newUseCount == 0;
                    }
                }
                hashBounds = (hashBounds + 1) & ((uint)mapSize - 1);
                count++;
            }
            return false;
        }

        public double GetCompressionFactor()
        {
            uint uniqueCount = 0, allCount = 0;
            for(int i = 0; i < mapSize; ++i)
            {
                BlockValue curBlock = map[i];
                if (curBlock.blockUseCount > 0)
                {
                    uniqueCount++;
                    allCount += curBlock.blockUseCount;
                }
            }
            return allCount / (double)uniqueCount;
        }

        private void IncreaseSizeToNewCount(uint count)
        {
            int newSize = mapSize * 2;
            while(newSize * wantedEmptyRatio < count + minReservedCount)
            {
                newSize *= 2;
            }

            StructuredBuffer newMapGpu = new StructuredBuffer(App.graphicsDevice, typeof(BlockValue), newSize, BufferUsage.None, ShaderAccess.ReadWrite);
            BlockValue[] newMap = new BlockValue[newSize];

            mapCopyShader.Parameters["oldSize"].SetValue(mapSize);
            mapCopyShader.Parameters["newSize"].SetValue(newSize);
            mapCopyShader.Parameters["oldMap"].SetValue(mapGpu);
            mapCopyShader.Parameters["newMap"].SetValue(newMapGpu);
            mapCopyShader.Techniques[0].Passes["CopyMap"].ApplyCompute();
            App.graphicsDevice.DispatchCompute((mapSize / 64) + 1, 1, 1);
            newMapGpu.GetData(newMap);

            mapGpu.Dispose();
            mapGpu = newMapGpu;
            mapSize = newSize;
            map = newMap;

        }

        public void SyncGpuToCpu()
        {
            mapGpu.GetData(map);
        }

        public void Dispose()
        {
            mapGpu?.Dispose();
        }
    }
}
