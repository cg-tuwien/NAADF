using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.Gui;
using NAADF.World.Generator;
using NAADF.World.Render;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace NAADF.World.Data
{

    public class WorldData : IDisposable
    {
        public Point3 actualSizeInVoxels, sizeInVoxels, sizeInBlocks, sizeInWorldGenSegments;
        public bool isLoaded = false;

        public int chunkCount, queueGroupCount;
        public int worldGenSegmentSizeInVoxels, worldGenSegmentSizeInChunks;
        public int chunkVolumeInVoxels;

        public Point3 sizeInChunks;
        public Point3 sizeInQueueGroups;

        public DynamicStructuredBuffer dataVoxelGpu, dataBlockGpu;
        public StructuredBuffer blockVoxelCountGpu;
        public Texture3D dataChunkGpu;

        public uint[] dataVoxel;
        public uint[] dataBlock;
        public uint[] dataChunk;
        public ConcurrentQueue<uint> freeVoxelSlots, freeBlockSlots;

        public StructuredBuffer segmentVoxelBuffer;

        public BlockHashingHandler blockHashingHandler;
        public WorldBoundHandler boundHandler;
        public EntityHandler entityHandler;
        public ChangeHandler changeHandler;
        public EditingHandler editingHandler;

        private const int GPU_MAX_ELEMENTS_UINT = 1024 * 1024 * 511;
        private static Effect chunkProcessor;

        public uint blockCount, voxelCount;
        private double initialVoxelCompressionFactor;

        private readonly object _resizeLock = new object();

        public WorldData(Point3 wantedSizeInVoxels, int worldGenSegmentSizeInGroups)
        {
            this.actualSizeInVoxels = wantedSizeInVoxels;
            this.worldGenSegmentSizeInChunks = worldGenSegmentSizeInGroups * 4;
            this.worldGenSegmentSizeInVoxels = worldGenSegmentSizeInChunks * 16;
            this.sizeInWorldGenSegments = (actualSizeInVoxels + new Point3(worldGenSegmentSizeInVoxels - 1)) / worldGenSegmentSizeInVoxels;
            this.sizeInVoxels = sizeInWorldGenSegments * worldGenSegmentSizeInVoxels;
            this.sizeInBlocks = sizeInVoxels / 4;
            this.sizeInChunks = sizeInBlocks / 4;
            this.sizeInQueueGroups = sizeInChunks / 4;

            this.chunkVolumeInVoxels = 16 * 16 * 16;

            this.chunkCount = sizeInChunks.X * sizeInChunks.Y * sizeInChunks.Z;
            queueGroupCount = chunkCount / 64;

            segmentVoxelBuffer = new StructuredBuffer(App.graphicsDevice, typeof(uint), ((int)Math.Pow(worldGenSegmentSizeInVoxels, 3)) / 2, BufferUsage.None, ShaderAccess.ReadWrite);
            if (chunkProcessor == null)
                chunkProcessor = App.contentManager.Load<Effect>("shaders/world/data/chunkCalc");

            dataVoxelGpu = new DynamicStructuredBuffer(App.graphicsDevice, typeof(uint), (worldGenSegmentSizeInVoxels * worldGenSegmentSizeInVoxels * worldGenSegmentSizeInVoxels) / 2, BufferUsage.None, ShaderAccess.ReadWrite);

            dataBlockGpu = new DynamicStructuredBuffer(App.graphicsDevice, typeof(uint), (worldGenSegmentSizeInVoxels * worldGenSegmentSizeInVoxels * worldGenSegmentSizeInVoxels) / 64, BufferUsage.None, ShaderAccess.ReadWrite);

            dataChunk = new uint[chunkCount];
            dataChunkGpu = new Texture3D(App.graphicsDevice, sizeInChunks.X, sizeInChunks.Y, sizeInChunks.Z, false, BuildFlags.Entities ? SurfaceFormat.Rg64Uint : SurfaceFormat.R32Uint, ShaderAccess.ReadWrite);

            blockVoxelCountGpu = new StructuredBuffer(App.graphicsDevice, typeof(uint), 2, BufferUsage.None, ShaderAccess.ReadWrite);

            freeVoxelSlots = new();
            freeBlockSlots = new();
            boundHandler = new WorldBoundHandler(this);
            changeHandler = new ChangeHandler(this);
            entityHandler = new EntityHandler(this);
            editingHandler = new EditingHandler(this);
            blockHashingHandler = new BlockHashingHandler(this, 0, 0.5f, (worldGenSegmentSizeInVoxels * worldGenSegmentSizeInVoxels * worldGenSegmentSizeInVoxels) / 64);
        }

        public unsafe void DrawDebugInfo()
        {
            if (!isLoaded)
                return;

            boundHandler.DrawDebugInfo();

            long chunkByteCount = BuildFlags.Entities ? 8L : 4L;
            ImGui.Text("Chunks memory usage: " + ((chunkCount * chunkByteCount) / 1_000_000.0).ToString("N3") + " MB");
            ImGui.Text("Blocks memory usage: " + ((blockCount * 4L) / 1_000_000.0).ToString("N3") + " MB");
            ImGui.Text("Voxels memory usage: " + ((voxelCount * 2L) / 1_000_000.0).ToString("N3") + " MB");
            ImGui.Text("Voxels compression factor: " + initialVoxelCompressionFactor.ToString("N3"));
        }

        public void Update(float gameTime)
        {
            if (!isLoaded)
                return;

            entityHandler.Update(gameTime, WorldRender.render.taaIndex);
            editingHandler.Update(gameTime);
            changeHandler.Update();
            boundHandler.Update();
        }

        public void GenerateWorld(WorldGenerator worldGenerator)
        {
            isLoaded = false;
            if (!worldGenerator.IsValid())
                return;

            Stopwatch sw = Stopwatch.StartNew();
            int maxNewVoxelsPerGenSegment = worldGenSegmentSizeInVoxels * worldGenSegmentSizeInVoxels * worldGenSegmentSizeInVoxels;
            int maxNewBlocksPerGenSegment = maxNewVoxelsPerGenSegment / 64;
            uint[] blockVoxelCount = [64, 64];
            boundHandler.Initialize();
            blockHashingHandler?.Dispose();
            blockHashingHandler = new BlockHashingHandler(this, 0, 0.5f, maxNewVoxelsPerGenSegment / 32);
            blockVoxelCountGpu.SetData(blockVoxelCount);

            int count = 0, maxCount = sizeInWorldGenSegments.X * sizeInWorldGenSegments.Y * sizeInWorldGenSegments.Z;
            for (int z = 0; z < sizeInWorldGenSegments.Z; ++z)
            {
                for (int y = 0; y < sizeInWorldGenSegments.Y; ++y)
                {
                    for (int x = 0; x < sizeInWorldGenSegments.X; ++x)
                    {
                        Point3 segmentPos = new Point3(x, y, z);
                        Point3 segmentPosInChunks = segmentPos * worldGenSegmentSizeInChunks;
                        worldGenerator.CopyToChunkData(segmentPosInChunks, new Point3(worldGenSegmentSizeInChunks), actualSizeInVoxels, segmentVoxelBuffer, 1);
                        CalculateChunkBlocks(segmentPosInChunks);

                        count++;
                        blockVoxelCountGpu.GetData(blockVoxelCount);
                        blockHashingHandler.SetNewUsedCount(blockVoxelCount[0] / 64);
                        dataBlockGpu.SetNewMinCount((int)blockVoxelCount[1] + maxNewBlocksPerGenSegment, 2);
                        dataVoxelGpu.SetNewMinCount((int)blockVoxelCount[0] + maxNewVoxelsPerGenSegment / 2, 2);
                        if (count % 50 == 0)
                            Console.WriteLine("Chunk Generation: " + Math.Round((100 * count) / (float)maxCount) + "%");
                    }
                }
            }

            blockVoxelCountGpu.GetData(blockVoxelCount);
            blockCount = blockVoxelCount[1];
            voxelCount = blockVoxelCount[0];

            if (BuildFlags.Entities)
            {
                uint[] gpuCpuSyncBufferCpu = new uint[1024 * 1024 * 16];
                StructuredBuffer gpuCpuSyncBufferGpu = new StructuredBuffer(App.graphicsDevice, typeof(uint), 1024 * 1024 * 16, BufferUsage.None, ShaderAccess.ReadWrite);
                
                // Copy chunk, block and voxel data to cpu in smaller segments
                chunkProcessor.Parameters["chunks"].SetValue(dataChunkGpu);
                chunkProcessor.Parameters["gpuCpuSyncBuffer"].SetValue(gpuCpuSyncBufferGpu);
                chunkProcessor.Parameters["sizeInChunksX"].SetValue(sizeInChunks.X);
                chunkProcessor.Parameters["sizeInChunksY"].SetValue(sizeInChunks.Y);
                chunkProcessor.Parameters["sizeInChunksZ"].SetValue(sizeInChunks.Z);

                int chunkCopyOffset = 0;
                while (true)
                {
                    int curChunkCopyAmount = Math.Min(1024 * 1024 * 16, chunkCount - chunkCopyOffset);
                    if (curChunkCopyAmount <= 0)
                        break;

                    chunkProcessor.Parameters["copyMaxCount"].SetValue(chunkCount);
                    chunkProcessor.Parameters["copyOffset"].SetValue(chunkCopyOffset);
                    chunkProcessor.Techniques[0].Passes["ChunkCopyToCpu"].ApplyCompute();
                    App.graphicsDevice.DispatchCompute(curChunkCopyAmount / 64, 1, 1);

                    gpuCpuSyncBufferGpu.GetData(0, gpuCpuSyncBufferCpu, 0, curChunkCopyAmount);
                    Array.Copy(gpuCpuSyncBufferCpu, 0, dataChunk, chunkCopyOffset, curChunkCopyAmount);
                    chunkCopyOffset += curChunkCopyAmount;
                }
                gpuCpuSyncBufferGpu.Dispose();
            }
            else
                dataChunkGpu.GetData(dataChunk);

            dataBlock = new uint[dataBlockGpu.GetBuffer().ElementCount];
            dataVoxel = new uint[dataVoxelGpu.GetBuffer().ElementCount];
            App.helper.CopyFromStructuredBufferLarge(dataBlock, dataBlockGpu.GetBuffer(), dataBlockGpu.GetBuffer().ElementCount);
            App.helper.CopyFromStructuredBufferLarge(dataVoxel, dataVoxelGpu.GetBuffer(), dataVoxelGpu.GetBuffer().ElementCount);

            chunkProcessor.Parameters["blocks"].SetValue(dataBlockGpu.GetBuffer());
            chunkProcessor.Parameters["voxels"].SetValue(dataVoxelGpu.GetBuffer());

            chunkProcessor.Techniques[0].Passes["ComputeVoxelBounds"].ApplyCompute();
            App.graphicsDevice.DispatchCompute((int)(voxelCount / 64), 1, 1);

            chunkProcessor.Techniques[0].Passes["ComputeBlockBounds"].ApplyCompute();
            App.graphicsDevice.DispatchCompute((int)(blockCount / 64), 1, 1);

            blockHashingHandler.SyncGpuToCpu();
            initialVoxelCompressionFactor = blockHashingHandler.GetCompressionFactor();
            sw.Stop();
            Console.WriteLine("Construction time: " + sw.Elapsed.TotalMilliseconds + " milliseconds");

            if (worldGenerator is WorldGeneratorModel) // Should be handled somewhere else
                ((WorldGeneratorModel)worldGenerator).modelData?.Dispose();

            isLoaded = true;
        }

        public void FillChunkData(int chunkIndex, uint[] buffer, int offset)
        {
            uint chunk = dataChunk[chunkIndex];
            uint chunkState = chunk >> 30;
            uint chunkContent = chunk & 0x3FFFFFFF;
            if (chunkState != 2)
            {
                uint type = chunkState == 1 ? ((1 << 15) | chunkContent) : 0u;
                for (int v = 0; v < 2048; ++v)
                    buffer[offset + v] = type | (type << 16);
            }
            else
            {
                for (int b = 0; b < 64; ++b)
                {
                    uint block = dataBlock[chunkContent + b];
                    uint blockState = block >> 30;
                    uint blockContent = block & 0x3FFFFFFF;
                    if (blockState != 2)
                    {
                        uint type = blockState == 1 ? ((1 << 15) | blockContent) : 0u;
                        for (int v = 0; v < 32; ++v)
                            buffer[offset + b * 32 + v] = type | (type << 16);
                    }
                    else
                    {
                        for (int v = 0; v < 32; ++v)
                        {
                            uint voxelComp = dataVoxel[blockContent + v];
                            buffer[offset + b * 32 + v] = voxelComp;
                        }
                    }
                }
            }
        }

        public void FillBlockData(Point3 blockPos, uint[] buffer, int offset, uint stateBit = 1)
        {
            Point3 chunkPos = blockPos / 4;
            Point3 blockPosInChunk = blockPos - chunkPos * 4;
            int chunkIndex = chunkPos.X + chunkPos.Y * sizeInChunks.X + chunkPos.Z * sizeInChunks.X * sizeInChunks.Y;
            int blockIndexInChunk = blockPosInChunk.X + blockPosInChunk.Y * 4 + blockPosInChunk.Z * 16;
            uint chunk = dataChunk[chunkIndex];
            uint chunkState = chunk >> 30;
            uint chunkContent = chunk & 0x3FFFFFFF;
            if (chunkState != 2)
            {
                uint type = chunkState == 1 ? ((stateBit << 15) | chunkContent) : 0u;
                for (int v = 0; v < 32; ++v)
                    buffer[offset + v] = type | (type << 16);
            }
            else
            {
                uint block = dataBlock[chunkContent + blockIndexInChunk];
                uint blockState = block >> 30;
                uint blockContent = block & 0x3FFFFFFF;
                if (blockState != 2)
                {
                    uint type = blockState == 1 ? ((stateBit << 15) | blockContent) : 0u;
                    for (int v = 0; v < 32; ++v)
                        buffer[offset + v] = type | (type << 16);
                }
                else
                {
                    for (int v = 0; v < 32; ++v)
                    {
                        uint voxelComp = dataVoxel[blockContent + v];
                        if (stateBit == 0)
                            voxelComp &= 0x7FFF7FFF;
                        buffer[offset + v] = voxelComp;
                    }
                }
            }
        }

        public uint AddVoxels(Span<uint> voxels)
        {
            uint location = 0;
            uint freeLocation = 0;
            if (freeVoxelSlots.TryDequeue(out freeLocation))
                location = freeLocation;
            else
            {
                uint newVoxelCount = Interlocked.Add(ref voxelCount, 64);
                location = (newVoxelCount - 64) / 2;
            }

            if (location + 32 >= dataVoxel.Length)
            {
                lock (_resizeLock)
                {
                    if (location + 32 >= dataVoxel.Length)
                    {
                        long newSizeInBytes = Math.Min((long)dataVoxel.Length * 2 * 4, 0xFFFF0000);
                        long newSize = newSizeInBytes / 4;
                        if (newSize != dataVoxel.Length)
                        {
                            Console.WriteLine("Voxel resize");
                            Array.Resize(ref dataVoxel, (int)newSize);
                            dataVoxelGpu.Resize((int)newSize);
                        }
                    }
                }
            }

            voxels.CopyTo(new Span<uint>(dataVoxel, (int)location, 32));
            return location;
        }




        public uint SetBlocks(int chunkIndex, Span<uint> blocks)
        {
            if ((dataChunk[chunkIndex] >> 30) == 2)
            {
                uint existingLocation = dataChunk[chunkIndex] & 0x3FFFFFFF;
                for (int i = 0; i < 64; ++i)
                {
                    dataBlock[existingLocation + i] = blocks[i];
                }
                return existingLocation;
            }
            else
            {
                uint location = 0;
                uint freeLocation = 0;
                if (freeBlockSlots.TryDequeue(out freeLocation))
                    location = freeLocation;
                else
                {
                    uint newLocation = Interlocked.Add(ref blockCount, 64);
                    location = newLocation - 64;
                }

                if (location + 64 >= dataBlock.Length)
                {
                    lock (_resizeLock)
                    {
                        if (location + 64 >= dataBlock.Length)
                        {
                            long newSizeInBytes = Math.Min((long)dataBlock.Length * 2 * 4, 0xFFFF0000);
                            long newSize = newSizeInBytes / 4;
                            if (newSize != dataBlock.Length)
                            {
                                Console.WriteLine("Block resize");
                                Array.Resize(ref dataBlock, (int)newSize);
                                dataBlockGpu.Resize((int)newSize);
                            }
                        }
                    }
                }

                for (int i = 0; i < 64; ++i)
                {
                    dataBlock[location + i] = blocks[i];
                }
                return location;
            }
        }

        public void SetChunk(int chunkIndex, uint chunkData)
        {
            uint curChunk = dataChunk[chunkIndex];
            bool curHasContent = (curChunk >> 30) != 0;
            bool newHasContent = (chunkData >> 30) != 0;

            // Remove existing blocks
            if ((curChunk >> 30) == 2 && (chunkData >> 30) != 2)
                freeBlockSlots.Enqueue(curChunk & 0x3FFFFFFF);

            dataChunk[chunkIndex] = chunkData;
            if (curHasContent != newHasContent || !newHasContent)
                changeHandler.AddChangedChunk(chunkIndex);
        }

        public uint RayTraversal(Vector3 rayOrigin, Vector3 rayDir, out float resultLength, out Point3 voxelPos, out Point3 normal)
        {
            Vector3 startPos = rayOrigin;
            BoundingBox worldBB = new BoundingBox(new Vector3(0.1f), sizeInVoxels.ToVector3() - new Vector3(0.1f));
            float? worldBBdist = worldBB.Intersects(new Ray(rayOrigin, rayDir));
            if (worldBB.Contains(rayOrigin) == ContainmentType.Disjoint && worldBBdist != null)
            {
                startPos += rayDir * (float)worldBBdist;
            }

            Vector3 invRayDirAbs = new Vector3(Math.Abs(1.0f / (1e-10f + rayDir.X)), Math.Abs(1.0f / (1e-10f + rayDir.Y)), Math.Abs(1.0f / (1e-10f + rayDir.Z)));

            int stepCount = 0;
            Point3 isNegative = new Point3(rayDir.X < 0 ? 1 : 0, rayDir.Y < 0 ? 1 : 0, rayDir.Z < 0 ? 1 : 0);
            Vector3 signRayDir = new Vector3(rayDir.X < 0 ? -1 : 1, rayDir.Y < 0 ? -1 : 1, rayDir.Z < 0 ? -1 : 1);

            Vector3 mask = Vector3.Zero;

            float curDist = 0;
            resultLength = -1;
            uint resultType = 0;
            voxelPos = new Point3(0);
            normal = new Point3(0);
            while (stepCount < 1000)
            {
                Vector3 curPos = startPos + rayDir * curDist;
                Point3 curCell = Point3.FromVector3(mask * signRayDir * 0.5f + curPos);

                if (curCell.X < 0 || curCell.Y < 0 || curCell.Z < 0 || curCell.X >= sizeInVoxels.X || curCell.Y >= sizeInVoxels.Y || curCell.Z >= sizeInVoxels.Z)
                    break;

                // Get data from chunk
                Point3 voxelPosInChunk = curCell % 16;
                Point3 chunkPos = curCell / 16;
                int chunkIndex = chunkPos.X + chunkPos.Y * sizeInChunks.X + chunkPos.Z * sizeInChunks.X * sizeInChunks.Y;
                uint curNode = dataChunk[chunkIndex];

                Point3 boundsInDir = new Point3(rayDir.X < 0 ? voxelPosInChunk.X : 15 - voxelPosInChunk.X, rayDir.Y < 0 ? voxelPosInChunk.Y : 15 - voxelPosInChunk.Y, rayDir.Z < 0 ? voxelPosInChunk.Z : 15 - voxelPosInChunk.Z);

                if ((curNode >> 31) != 0)
                {
                    Point3 blockPosInChunk = voxelPosInChunk / 4;
                    int blockIndex = (int)(curNode & 0x3FFFFFFF) + blockPosInChunk.X + blockPosInChunk.Y * 4 + blockPosInChunk.Z * 16;
                    curNode = dataBlock[blockIndex];
                    Point3 voxelPosInBlock = curCell % 4;

                    boundsInDir = new Point3(rayDir.X < 0 ? voxelPosInBlock.X : 3 - voxelPosInBlock.X, rayDir.Y < 0 ? voxelPosInBlock.Y : 3 - voxelPosInBlock.Y, rayDir.Z < 0 ? voxelPosInBlock.Z : 3 - voxelPosInBlock.Z);
                    if ((curNode >> 31) != 0)
                    {
                        int voxelIndex = (int)(curNode & 0x3FFFFFFF) * 2 + voxelPosInBlock.X + voxelPosInBlock.Y * 4 + voxelPosInBlock.Z * 16;
                        uint curVoxelPair = dataVoxel[voxelIndex / 2];
                        curNode = (curVoxelPair >> (16 * (voxelIndex & 0x1))) & 0xFFFF;

                        if ((curNode & 0x8000) != 0)
                            curNode = (1 << 30) | (curNode & 0x7FFF);
                        else
                            boundsInDir = new Point3(0);
                    }
                }

                if ((curNode & 0x40000000) != 0)
                {
                    resultType = curNode & 0x3FFFFFFF;
                    resultLength = curDist + (worldBBdist ?? 0);
                    voxelPos = curCell;
                    normal = Point3.FromVector3(mask * new Vector3(rayDir.X < 0 ? 1 : -1, rayDir.Y < 0 ? 1 : -1, rayDir.Z < 0 ? 1 : -1));
                    break;
                }

                Vector3 curPosFrac = new Vector3((float)Math.Abs(isNegative.X - (curPos.X - Math.Truncate(curPos.X))), (float)Math.Abs(isNegative.Y - (curPos.Y - Math.Truncate(curPos.Y))), (float)Math.Abs(isNegative.Z - (curPos.Z - Math.Truncate(curPos.Z))));
                Vector3 distForIntersect = ((new Point3(1) + boundsInDir).ToVector3() - (Vector3.One - mask) * curPosFrac) * invRayDirAbs;
                float minDist = Math.Min(distForIntersect.X, Math.Min(distForIntersect.Y, distForIntersect.Z));
                mask = new Vector3(minDist >= distForIntersect.X ? 1 : 0, minDist >= distForIntersect.Y ? 1 : 0, minDist >= distForIntersect.Z ? 1 : 0);
                curDist += Math.Max(minDist, 0.00001f);
                stepCount++;
            }
            return resultType;
        }

        public void setEffect(Effect effect)
        {
            effect.Parameters["boundingBoxMin"].SetValue(new Vector3(+0.1f));
            effect.Parameters["boundingBoxMax"].SetValue(sizeInVoxels.ToVector3() - new Vector3(0.1f));
            effect.Parameters["groupSizeX"].SetValue(sizeInQueueGroups.X);
            effect.Parameters["groupSizeY"].SetValue(sizeInQueueGroups.Y);
            effect.Parameters["groupSizeZ"].SetValue(sizeInQueueGroups.Z);
            effect.Parameters["blocks"]?.SetValue(dataBlockGpu.GetBuffer());
            effect.Parameters["chunks"]?.SetValue(dataChunkGpu);
            effect.Parameters["voxels"]?.SetValue(dataVoxelGpu.GetBuffer());
            effect.Parameters["voxelTypeData"]?.SetValue(App.worldHandler.voxelTypeHandler.typesRenderGpu.GetBuffer());
            effect.Parameters["entityChunkInstances"]?.SetValue(entityHandler.entityChunkInstancesGpu);
            effect.Parameters["entityVoxelData"]?.SetValue(entityHandler.entityVoxelDataGpu);
        }

        private void CalculateChunkBlocks(Point3 chunkOffset)
        {
            chunkProcessor.Parameters["chunkOffsetX"].SetValue(chunkOffset.X);
            chunkProcessor.Parameters["chunkOffsetY"].SetValue(chunkOffset.Y);
            chunkProcessor.Parameters["chunkOffsetZ"].SetValue(chunkOffset.Z);
            chunkProcessor.Parameters["chunks"]?.SetValue(dataChunkGpu);
            chunkProcessor.Parameters["blockVoxelCount"].SetValue(blockVoxelCountGpu);
            chunkProcessor.Parameters["blocks"].SetValue(dataBlockGpu.GetBuffer());
            chunkProcessor.Parameters["voxels"].SetValue(dataVoxelGpu.GetBuffer());
            chunkProcessor.Parameters["hashMap"]?.SetValue(blockHashingHandler.mapGpu);
            chunkProcessor.Parameters["hashCoefficients"].SetValue(blockHashingHandler.coefficients);
            chunkProcessor.Parameters["hashMapSize"]?.SetValue(blockHashingHandler.mapSize);
            chunkProcessor.Parameters["segmentVoxelBuffer"]?.SetValue(segmentVoxelBuffer);
            chunkProcessor.Parameters["segmentSizeInChunks"]?.SetValue(worldGenSegmentSizeInChunks);

            chunkProcessor.Techniques[0].Passes["VoxelHash"].ApplyCompute();
            App.graphicsDevice.DispatchCompute(worldGenSegmentSizeInChunks, worldGenSegmentSizeInChunks, worldGenSegmentSizeInChunks);
        }

        public void Dispose()
        {
            dataVoxelGpu.GetBuffer().Dispose();
            dataChunkGpu.Dispose();
            dataBlockGpu.GetBuffer().Dispose();
            segmentVoxelBuffer?.Dispose();
            blockHashingHandler?.Dispose();
            boundHandler?.Dispose();
            entityHandler?.Dispose();
            changeHandler?.Dispose();
            blockVoxelCountGpu?.Dispose();
        }
    }
}
