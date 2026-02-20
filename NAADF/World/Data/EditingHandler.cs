using Microsoft.Xna.Framework;
using NAADF.Common;
using NAADF.Gui;
using NAADF.World.Data.Editing;
using NAADF.World.Render;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;

namespace NAADF.World.Data
{
    public class EditingHandler
    {
        public EditingTool tool = null;
        private WorldData worldData;

        public uint[] editData;
        private List<int> editedChunks;
        private uint[] editChunkDataPointer;

        private int editDataCount = 0;

        public bool isMaterialSelect = false;
        public uint selectedTypeRenderIndex = uint.MaxValue;
        public VoxelType selectedType = new();

        private readonly object _editDataLock = new object();
        private readonly object _editProcessInternalLock = new object();

        public ReaderWriterLockSlim editLock = new ReaderWriterLockSlim();

        public EditingHandler(WorldData worldData)
        {
            this.worldData = worldData;
            editData = new uint[1024 * 256];
            editedChunks = new();
            editChunkDataPointer = new uint[worldData.chunkCount];
            Array.Fill(editChunkDataPointer, 0xFFFFFFFF);
        }

        public void Update(float gameTime)
        {
            if (tool != null && !GuiHandler.IsUiActive && !isMaterialSelect)
            {
                tool?.ApplyAnyInput(gameTime);
            }

            if (isMaterialSelect)
            {
                if (IO.KBStates.IsKeyToggleDown(Microsoft.Xna.Framework.Input.Keys.Escape))
                    isMaterialSelect = false;
                if (IO.MOStates.IsLeftButtonToggleOff())
                {
                    Point pixelPos = IO.MOStates.New.Position;
                    Vector3 rayDir = WorldRender.camera.getRayDir(IO.MOStates.New.Position);
                    float hitLength;
                    Point3 voxelPos, normal;
                    uint hitType = worldData.RayTraversal(WorldRender.camera.GetPos().toVector3(), rayDir, out hitLength, out voxelPos, out normal);
                    if (hitType != 0)
                    {
                        selectedTypeRenderIndex = hitType;
                        selectedType = App.worldHandler.voxelTypeHandler.typesById.FirstOrDefault(v => v.Value.renderIndex == selectedTypeRenderIndex).Value;
                        isMaterialSelect = false;
                        WorldUi.typeSelectedExtern = true;
                    }
                }
            }

            processChunks();
        }

        public void processChunks(bool syncGpu = false)
        {

            editLock.EnterWriteLock();
            var changeHandler = worldData.changeHandler;

            // Apply edited chunks
            Parallel.For(0, editedChunks.Count, i =>
            {
                uint newChunk = 0;
                int chunkIndex = editedChunks[i];
                uint dataPointer = editChunkDataPointer[chunkIndex];
                uint curChunk = worldData.dataChunk[chunkIndex];

                uint referenceBlock = 0;
                bool isAllBlocksSame = true;
                Span<uint> newBlocks = stackalloc uint[64];
                for (int b = 0; b < 64; ++b)
                {
                    // Hash block and see if it is uniform solid, already exists or needs be allocated
                    bool isAllVoxelsInBlockSame;
                    uint hash = worldData.blockHashingHandler.getHashOfBlock(editData, dataPointer + (uint)b * 32, out isAllVoxelsInBlockSame);
                    if (isAllVoxelsInBlockSame)
                    {
                        uint firstVoxelType = editData[dataPointer + b * 32] & 0x7FFF;
                        newBlocks[b] = firstVoxelType | ((firstVoxelType == 0 ? 0u : 1u) << 30);
                    }
                    else
                    {
                        bool isNew = false;
                        uint pointer = 0;
                        lock (_editProcessInternalLock)
                        {
                            pointer = worldData.blockHashingHandler.AddBlock(hash, editData, (int)dataPointer + b * 32, out isNew);
                            if (isNew)
                            {
                                int newVoxelCount = Interlocked.Add(ref changeHandler.changedVoxelCount, 1);
                                if (newVoxelCount * 33 > changeHandler.changedVoxels.Length)
                                    Array.Resize(ref changeHandler.changedVoxels, changeHandler.changedVoxels.Length * 2);
                                int oldVoxelCount = newVoxelCount - 1;
                                changeHandler.changedVoxels[oldVoxelCount * 33] = pointer;
                                Array.Copy(worldData.dataVoxel, pointer, changeHandler.changedVoxels, oldVoxelCount * 33 + 1, 32);
                            }
                        }
                        newBlocks[b] = (pointer) | (2u << 30);
                    }
                    if (b == 0)
                        referenceBlock = newBlocks[0];
                    isAllBlocksSame &= referenceBlock == newBlocks[b] && isAllVoxelsInBlockSame;
                }

                // Check if any voxels can be freed
                if ((curChunk >> 30) == 2)
                {
                    uint oldBlockPointer = curChunk & 0x3FFFFFFF;
                    for (int b = 0; b < 64; ++b)
                    {
                        uint oldBlock = worldData.dataBlock[oldBlockPointer + b];
                        if ((oldBlock >> 30) == 2)
                        {
                            uint oldVoxelPointer = oldBlock & 0x3FFFFFFF;
                            uint hash = worldData.blockHashingHandler.getHashOfBlock(worldData.dataVoxel, oldVoxelPointer, out _);
                            lock (_editProcessInternalLock)
                            {
                                if (worldData.blockHashingHandler.DeleteBlock(hash, oldVoxelPointer))
                                    worldData.freeVoxelSlots.Enqueue(oldVoxelPointer);
                            }
                        }
                    }
                }

                if (isAllBlocksSame)
                    newChunk = referenceBlock;
                else
                {
                    uint pointer = worldData.SetBlocks(chunkIndex, newBlocks);
                    int newBlockCount = Interlocked.Add(ref changeHandler.changedBlockCount, 1);
                    lock (_editProcessInternalLock)
                    {
                        if (newBlockCount * 65 > changeHandler.changedBlocks.Length)
                            Array.Resize(ref changeHandler.changedBlocks, changeHandler.changedBlocks.Length * 2);
                    }
                    int oldBlockCount = newBlockCount - 1;
                    changeHandler.changedBlocks[oldBlockCount * 65] = pointer;
                    newBlocks.CopyTo(new Span<uint>(changeHandler.changedBlocks, oldBlockCount * 65 + 1, 64));
                    newChunk = pointer | (2u << 30);
                }
                Point3 chunkPos = new Point3(chunkIndex % worldData.sizeInChunks.X, (chunkIndex / worldData.sizeInChunks.X) % worldData.sizeInChunks.Y, chunkIndex / (worldData.sizeInChunks.X * worldData.sizeInChunks.Y));
                uint chunkPosComp = (uint)chunkPos.X | ((uint)chunkPos.Y << 11) | ((uint)chunkPos.Z << 21);
                int oldChunkCount = Interlocked.Add(ref changeHandler.changedChunkCount, 1) - 1;
                changeHandler.changedChunks[oldChunkCount] = new Uint2(chunkPosComp, newChunk);
                worldData.SetChunk(chunkIndex, newChunk);
            });

            for (int i = 0; i < editedChunks.Count; i++)
            {
                editChunkDataPointer[editedChunks[i]] = 0xFFFFFFFF;
            }
            editedChunks.Clear();
            editDataCount = 0;

            editLock.ExitWriteLock();

            if (syncGpu)
                changeHandler.UpdateWorld();
        }

        public uint getChunkDataToEdit(Point3 chunkPos)
        {
            uint editDataPointer = 0;
            int chunkIndex = chunkPos.X + chunkPos.Y * worldData.sizeInChunks.X + chunkPos.Z * worldData.sizeInChunks.X * worldData.sizeInChunks.Y;
            lock (_editDataLock)
            {
                uint existingPointer = editChunkDataPointer[chunkIndex];
                if (existingPointer == 0xFFFFFFFF)
                {
                    if (editDataCount >= 1024 * 1024 * 256)
                    {
                        Console.WriteLine("Editing too much, forcing processing");
                        processChunks(true);
                    }
                    editedChunks.Add(chunkIndex);
                    if (editData.Length < editDataCount + 2048)
                        Array.Resize(ref editData, editData.Length * 2);
                    editDataPointer = (uint)editDataCount;
                    editDataCount += 2048;
                    editChunkDataPointer[chunkIndex] = editDataPointer;


                }
                else
                    return existingPointer;
            }

            worldData.FillChunkData(chunkIndex, editData, (int)editDataPointer);
            return editDataPointer;
        }

        public uint getVoxelData(uint chunkPointer, Point3 voxelPos)
        {
            Point3 blockPosInChunk = voxelPos / 4;
            Point3 voxelPosInBlock = (voxelPos - (voxelPos / 4) * 4);
            int blockIndexInChunk = blockPosInChunk.X + blockPosInChunk.Y * 4 + blockPosInChunk.Z * 16;
            int voxelIndexInBlock = voxelPosInBlock.X + voxelPosInBlock.Y * 4 + voxelPosInBlock.Z * 16;
            uint voxelComp = editData[chunkPointer + blockIndexInChunk * 32 + voxelIndexInBlock / 2];
            uint voxel1 = voxelComp & 0xFFFF;
            uint voxel2 = voxelComp >> 16;
            if ((voxelIndexInBlock % 2) == 0)
                return voxel1;
            else
                return voxel2;
        }

        public void setVoxelData(uint chunkPointer, Point3 voxelPos, uint type)
        {
            Point3 blockPosInChunk = voxelPos / 4;
            Point3 voxelPosInBlock = (voxelPos - (voxelPos / 4) * 4);
            int blockIndexInChunk = blockPosInChunk.X + blockPosInChunk.Y * 4 + blockPosInChunk.Z * 16;
            int voxelIndexInBlock = voxelPosInBlock.X + voxelPosInBlock.Y * 4 + voxelPosInBlock.Z * 16;
            uint voxelComp = editData[chunkPointer + blockIndexInChunk * 32 + voxelIndexInBlock / 2];
            uint voxel1 = voxelComp & 0xFFFF;
            uint voxel2 = voxelComp >> 16;
             if ((voxelIndexInBlock % 2) == 0)
                voxel1 = type;
            else
                voxel2 = type;
            editData[chunkPointer + blockIndexInChunk * 32 + voxelIndexInBlock / 2] = voxel1 | (voxel2 << 16);
        }

        public void DrawUi()
        {
            tool?.DrawUi();
        }
    }
}
