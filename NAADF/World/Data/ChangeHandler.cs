using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.World.Render;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.World.Data
{
    public class ChangeHandler : IDisposable
    {
        private WorldData worldData;

        // CPU
        private uint[] distanceFloodFill;
        private Queue<int> floodFillQueue;
        private uint[] changedGroups;
        public Uint2[] changedGroupsWithDist, changedChunks;
        public int changedGroupCount = 0, changedChunkCount = 0, changedBlockCount = 0, changedVoxelCount = 0;
        public uint[] changedBlocks, changedVoxels;

        // CPU -> GPU
        StructuredBuffer changedGroupsDynamic;
        DynamicStructuredBuffer changedChunksDynamic;
        DynamicStructuredBuffer changedBlocksDynamic;
        DynamicStructuredBuffer changedVoxelsDynamic;

        // GPU
        Effect worldChangeEffect;

        public float changeProcessingTime = 0;
        private readonly object _changeDataLock = new object();

        public ChangeHandler(WorldData worldData)
        {
            this.worldData = worldData;

            worldChangeEffect = App.contentManager.Load<Effect>("shaders/world/data/worldChange");

            distanceFloodFill = new uint[worldData.queueGroupCount];
            for (int i = 0; i <  worldData.queueGroupCount; i++)
            {
                distanceFloodFill[i] = 0x3FFFFFFF;
            }
            floodFillQueue = new();
            changedGroups = new uint[worldData.queueGroupCount];
            changedGroupsWithDist = new Uint2[changedGroups.Length];
            changedChunks = new Uint2[2000000];
            changedBlocks = new uint[2000000];
            changedVoxels = new uint[5000000];
            changedGroupsDynamic = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), changedGroups.Length, BufferUsage.None, ShaderAccess.Read, StructuredBufferType.Basic, -1, true);
            changedChunksDynamic = new DynamicStructuredBuffer(App.graphicsDevice, typeof(Uint2), changedChunks.Length, BufferUsage.None, ShaderAccess.Read, StructuredBufferType.Basic, -1, true);
            changedBlocksDynamic = new DynamicStructuredBuffer(App.graphicsDevice, typeof(uint), changedBlocks.Length, BufferUsage.None, ShaderAccess.Read, StructuredBufferType.Basic, -1, true);
            changedVoxelsDynamic = new DynamicStructuredBuffer(App.graphicsDevice, typeof(uint), changedVoxels.Length, BufferUsage.None, ShaderAccess.Read, StructuredBufferType.Basic, -1, true);


        }

        public void Update()
        {
            UpdateWorld();
        }

        public void UpdateWorld()
        {
            Stopwatch sw = Stopwatch.StartNew();
            int originalChangedGroupCount = changedGroupCount;
            while (floodFillQueue.Count > 0)
            {
                int curGroup = floodFillQueue.Dequeue();
                int curGroupX = curGroup & 0x7FF;
                int curGroupY = (curGroup >> 11) & 0x3FF;
                int curGroupZ = curGroup >> 21;
                int curGroupIndex = curGroupX + curGroupY * worldData.sizeInQueueGroups.X + curGroupZ * worldData.sizeInQueueGroups.X * worldData.sizeInQueueGroups.Y;
                uint curDistance = distanceFloodFill[curGroupIndex] & 0x7FFFFFFF;
                for (int z = -1; z <= 1; ++z)
                {
                    int nextGroupZ = curGroupZ + z;
                    if (nextGroupZ < 0 || nextGroupZ >= worldData.sizeInQueueGroups.Z)
                        continue;
                    for (int y = -1; y <= 1; ++y)
                    {
                        int nextGroupY = curGroupY + y;
                        if (nextGroupY < 0 || nextGroupY >= worldData.sizeInQueueGroups.Y)
                            continue;
                        for (int x = -1; x <= 1; ++x)
                        {
                            int nextGroupX = curGroupX + x;
                            if (nextGroupX < 0 || nextGroupX >= worldData.sizeInQueueGroups.X || (x == 0 && y == 0 && z == 0))
                                continue;

                            int nextGroupIndex = nextGroupX + nextGroupY * worldData.sizeInQueueGroups.X + nextGroupZ * worldData.sizeInQueueGroups.X * worldData.sizeInQueueGroups.Y;
                            if (distanceFloodFill[nextGroupIndex] == 0x3FFFFFFF)
                            {
                                uint newDistance = curDistance + 4;
                                distanceFloodFill[nextGroupIndex] = newDistance;
                                int nextGroupComp = nextGroupX | (nextGroupY << 11) | (nextGroupZ << 21);
                                if (curDistance < 28)
                                    floodFillQueue.Enqueue(nextGroupComp);
                                changedGroups[changedGroupCount++] = (uint)nextGroupIndex;
                            }
                        }
                    }
                }
            }

            for (int i = originalChangedGroupCount; i < changedGroupCount; i++)
            {
                int groupIndex = (int)changedGroups[i];
                distanceFloodFill[groupIndex] = 0;
            }

            const int MASK_MX = 0x3D; //0b111101
            const int MASK_PX = 0x3E; //0b111110
            const int MASK_MY = 0x37; //0b110111
            const int MASK_PY = 0x3B; //0b111011
            const int MASK_MZ = 0x1F; //0b011111
            const int MASK_PZ = 0x2F; //0b101111
            for (int i = 0; i < 7; i++)
            {
                // X
                for (int v = originalChangedGroupCount; v < changedGroupCount; ++v)
                {
                    int groupIndex = (int)changedGroups[v];
                    int x = groupIndex % worldData.sizeInQueueGroups.X;
                    uint curGroup = distanceFloodFill[groupIndex];
                    if (x > 0)
                        addBounds(groupIndex, MASK_MX, -1, 0, ref curGroup);
                    else
                        curGroup += 4u << 0;
                    if (x + 1 < worldData.sizeInQueueGroups.X)
                        addBounds(groupIndex, MASK_PX, 1, 5, ref curGroup);
                    else
                        curGroup += 4u << 5;
                    distanceFloodFill[groupIndex] = curGroup;
                }
                // Y
                for (int v = originalChangedGroupCount; v < changedGroupCount; ++v)
                {
                    int groupIndex = (int)changedGroups[v];
                    int y = (groupIndex / worldData.sizeInQueueGroups.X) % worldData.sizeInQueueGroups.Y;
                    uint curGroup = distanceFloodFill[groupIndex];
                    if (y > 0)
                        addBounds(groupIndex, MASK_MY, -worldData.sizeInQueueGroups.X, 10, ref curGroup);
                    else
                        curGroup += 4u << 10;
                    if (y + 1 < worldData.sizeInQueueGroups.Y)
                        addBounds(groupIndex, MASK_PY, worldData.sizeInQueueGroups.X, 15, ref curGroup);
                    else
                        curGroup += 4u << 15;
                    distanceFloodFill[groupIndex] = curGroup;
                }
                // Z
                for (int v = originalChangedGroupCount; v < changedGroupCount; ++v)
                {
                    int groupIndex = (int)changedGroups[v];
                    int z = groupIndex / (worldData.sizeInQueueGroups.X * worldData.sizeInQueueGroups.Y);
                    uint curGroup = distanceFloodFill[groupIndex];
                    if (z > 0)
                        addBounds(groupIndex, MASK_MZ, -worldData.sizeInQueueGroups.X * worldData.sizeInQueueGroups.Y, 20, ref curGroup);
                    else
                        curGroup += 4u << 20;
                    if (z + 1 < worldData.sizeInQueueGroups.Z)
                        addBounds(groupIndex, MASK_PZ, worldData.sizeInQueueGroups.X * worldData.sizeInQueueGroups.Y, 25, ref curGroup);
                    else
                        curGroup += 4u << 25;
                    distanceFloodFill[groupIndex] = curGroup;
                }
            }
            for (int i = 0; i < changedGroupCount; i++)
            {
                int groupIndex = (int)changedGroups[i];
                Point3 groupPos = new Point3(groupIndex % worldData.sizeInQueueGroups.X, (groupIndex / worldData.sizeInQueueGroups.X) % worldData.sizeInQueueGroups.Y, groupIndex / (worldData.sizeInQueueGroups.X * worldData.sizeInQueueGroups.Y));
                uint distance = i < originalChangedGroupCount ? 0xC0000000 : distanceFloodFill[groupIndex];

                changedGroupsWithDist[i] = new Uint2((uint)groupPos.X | ((uint)groupPos.Y << 11) | ((uint)groupPos.Z << 21), distance);
                distanceFloodFill[groupIndex] = 0x3FFFFFFF;
            }


            sw.Stop();

            worldChangeEffect.Parameters["groupSizeX"].SetValue(worldData.sizeInQueueGroups.X);
            worldChangeEffect.Parameters["groupSizeY"].SetValue(worldData.sizeInQueueGroups.Y);
            worldChangeEffect.Parameters["groupSizeZ"].SetValue(worldData.sizeInQueueGroups.Z);
            worldChangeEffect.Parameters["chunkSizeX"].SetValue(worldData.sizeInChunks.X);
            worldChangeEffect.Parameters["chunkSizeY"].SetValue(worldData.sizeInChunks.Y);
            worldChangeEffect.Parameters["chunkSizeZ"].SetValue(worldData.sizeInChunks.Z);
            worldChangeEffect.Parameters["chunks"].SetValue(worldData.dataChunkGpu);
            worldChangeEffect.Parameters["blocks"].SetValue(worldData.dataBlockGpu.GetBuffer());
            worldChangeEffect.Parameters["voxels"].SetValue(worldData.dataVoxelGpu.GetBuffer());
            worldChangeEffect.Parameters["boundQueueInfo"].SetValue(worldData.boundHandler.boundQueueInfoGpu);
            worldChangeEffect.Parameters["boundGroupQueues"].SetValue(worldData.boundHandler.boundGroupQueuesGpu);
            worldChangeEffect.Parameters["boundGroupMasks"].SetValue(worldData.boundHandler.boundGroupMasksGpu);
            worldChangeEffect.Parameters["boundGroupQueueMaxSize"].SetValue(worldData.queueGroupCount);


            if (changedChunkCount > 0)
            {
                if (changedChunkCount > changedChunksDynamic.GetBuffer().ElementCount)
                    changedChunksDynamic.Resize((int)(changedChunkCount * 1.5));
                changedChunksDynamic.SetData(changedChunks, 0, changedChunkCount);

                worldChangeEffect.Parameters["changedChunkCount"].SetValue(changedChunkCount);
                worldChangeEffect.Parameters["changedChunksDynamic"].SetValue(changedChunksDynamic.GetBuffer());

                worldChangeEffect.Techniques[0].Passes["ApplyChunkChange"].ApplyCompute();
                App.graphicsDevice.DispatchCompute((changedChunkCount + 63) / 64, 1, 1);
            }

            if (changedBlockCount > 0)
            {
                if (changedBlockCount * 65 > changedBlocksDynamic.GetBuffer().ElementCount)
                    changedBlocksDynamic.Resize((int)(changedBlockCount * 65 * 1.5));
                changedBlocksDynamic.SetData(changedBlocks, 0, changedBlockCount * 65);

                worldChangeEffect.Parameters["changedBlocksDynamic"].SetValue(changedBlocksDynamic.GetBuffer());

                worldChangeEffect.Techniques[0].Passes["ApplyBlockChange"].ApplyCompute();
                App.graphicsDevice.DispatchCompute(changedBlockCount, 1, 1);
            }

            if (changedVoxelCount > 0)
            {
                if (changedVoxelCount * 33 > changedVoxelsDynamic.GetBuffer().ElementCount)
                    changedVoxelsDynamic.Resize((int)(changedVoxelCount * 33 * 1.5));
                changedVoxelsDynamic.SetData(changedVoxels, 0, changedVoxelCount * 33);

                worldChangeEffect.Parameters["changedVoxelsDynamic"].SetValue(changedVoxelsDynamic.GetBuffer());

                worldChangeEffect.Techniques[0].Passes["ApplyVoxelChange"].ApplyCompute();
                App.graphicsDevice.DispatchCompute(changedVoxelCount, 1, 1);
            }

            if (changedGroupCount > 0)
            {
                changeProcessingTime = changeProcessingTime * 0.99f + (float)sw.Elapsed.TotalMilliseconds * 0.01f;
                changedGroupsDynamic.SetData(changedGroupsWithDist, 0, changedGroupCount);

                worldChangeEffect.Parameters["changedGroupsDynamic"].SetValue(changedGroupsDynamic);

                worldChangeEffect.Techniques[0].Passes["ApplyGroupChange"].ApplyCompute();
                App.graphicsDevice.DispatchCompute(changedGroupCount, 1, 1);
            }

            changedGroupCount = 0;
            changedChunkCount = 0;
            changedBlockCount = 0;
            changedVoxelCount = 0;
        }

        public void AddChangedChunk(int chunkIndex)
        {
            Point3 chunkPos = new Point3(chunkIndex % worldData.sizeInChunks.X, (chunkIndex / worldData.sizeInChunks.X) % worldData.sizeInChunks.Y, chunkIndex / (worldData.sizeInChunks.X * worldData.sizeInChunks.Y));
            AddChangedChunk(chunkPos);
        }

        public void AddChangedChunk(Point3 chunkPos)
        {
            Point3 groupPos = chunkPos / 4;
            int groupIndex = groupPos.X + groupPos.Y * worldData.sizeInQueueGroups.X + groupPos.Z * worldData.sizeInQueueGroups.X * worldData.sizeInQueueGroups.Y;
            lock(_changeDataLock)
            {
                if (distanceFloodFill[groupIndex] == 0x3FFFFFFF)
                {
                    distanceFloodFill[groupIndex] = 0x80000000;
                    int groupPosComp = groupPos.X | (groupPos.Y << 11) | (groupPos.Z << 21);
                    changedGroups[changedGroupCount++] = (uint)groupIndex;
                    floodFillQueue.Enqueue(groupPosComp);
                }
            }

        }

        private void addBounds(int curIndex, uint mask, int directionOffset, int boundsLocation, ref uint curVoxel)
        {
            uint neighbor = distanceFloodFill[curIndex + directionOffset];
            if ((neighbor & 0x80000000) == 0)
            {
                if ((checkMatchingBoundCell(neighbor, curVoxel) & mask) == mask)
                    curVoxel += 4u << boundsLocation;
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

        public void Dispose()
        {
            worldData = null;
            changedGroupsDynamic?.Dispose();
            changedChunksDynamic.GetBuffer()?.Dispose();
            changedBlocksDynamic.GetBuffer()?.Dispose();
            changedVoxelsDynamic.GetBuffer()?.Dispose();
        }
    }
}
