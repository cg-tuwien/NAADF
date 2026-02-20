using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.World.Model;
using NAADF.World.Render;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NAADF.World.Data.Editing
{
    public class EditingToolModel : EditingTool
    {
        ModelData selectedModelData = null;
        bool placeCamera = false;
        public EditingToolModel()
        {

        }

        public override void ApplyAnyInput(float gameTime)
        {
            WorldData worldData = (WorldData)App.worldHandler.worldData;
            if (IO.MOStates.IsLeftButtonToggleOn() && selectedModelData != null)
            {
                Vector3 rayDir = WorldRender.camera.getRayDir(IO.MOStates.New.Position);
                float hitLength;
                Point3 voxelPosTemp, normal;
                uint hitType = worldData.RayTraversal(WorldRender.camera.GetPos().toVector3(), rayDir, out hitLength, out voxelPosTemp, out normal);
                Vector3 hitPos = WorldRender.camera.GetPos().toVector3() + rayDir * hitLength;
                Point3 pos = Point3.FromVector3(hitPos + normal.ToVector3() * 0.5f);
                if (placeCamera)
                {
                    pos = (Point3.FromVector3(WorldRender.camera.GetPos().toVector3()) / 4) * 4;
                    hitType = 1;
                }

                if (hitType != 0)
                {
                    Point3 minChunkPos = pos / 16;
                    minChunkPos = new Point3(Math.Max(0, minChunkPos.X), Math.Max(0, minChunkPos.Y), Math.Max(0, minChunkPos.Z));
                    Point3 maxChunkPos = (pos + (selectedModelData.size - new Point3(1))) / 16;
                    maxChunkPos = new Point3(Math.Min(worldData.sizeInChunks.X - 1, maxChunkPos.X), Math.Min(worldData.sizeInChunks.Y - 1, maxChunkPos.Y), Math.Min(worldData.sizeInChunks.Z - 1, maxChunkPos.Z));

                    // Check each chunk
                    Point3 chunkCountSize = (maxChunkPos + new Point3(1)) - minChunkPos;
                    int chunkCount = chunkCountSize.X * chunkCountSize.Y * chunkCountSize.Z;
                    Parallel.For(0, chunkCount, (c) =>
                    {
                        worldData.editingHandler.editLock.EnterReadLock();
                        Point3 chunkPos = minChunkPos + new Point3(c % chunkCountSize.X, (c / chunkCountSize.X) % chunkCountSize.Y, c / (chunkCountSize.X * chunkCountSize.Y));
                        uint pointer = 0xFFFFFFFF;
                        for (int b = 0; b < 64; ++b)
                        {
                            Point3 blockPosInChunk = new Point3(b % 4, (b / 4) % 4, b / 16);

                            for (int v = 0; v < 64; ++v)
                            {
                                Point3 voxelPosInBlock = new Point3(v % 4, (v / 4) % 4, v / 16);
                                Point3 voxelPos = chunkPos * 16 + blockPosInChunk * 4 + voxelPosInBlock;
                                Point3 voxelPosInModel = voxelPos - pos;
                                if (voxelPosInModel.X < 0 || voxelPosInModel.Y < 0 || voxelPosInModel.Z < 0 || voxelPosInModel.X >= selectedModelData.size.X || voxelPosInModel.Y >= selectedModelData.size.Y || voxelPosInModel.Z >= selectedModelData.size.Z)
                                    continue;

                                uint typeFromModel = 0;

                                Point3 modelChunkPos = voxelPosInModel / 16;
                                int modelChunkIndex = modelChunkPos.X + modelChunkPos.Y * selectedModelData.sizeInChunks.X + modelChunkPos.Z * selectedModelData.sizeInChunks.X * selectedModelData.sizeInChunks.Y;
                                uint modelChunk = selectedModelData.dataChunk[modelChunkIndex];
                                if ((modelChunk >> 30) == 2)
                                {
                                    Point3 modelBlockPosInChunk = (voxelPosInModel % 16) / 4;
                                    int modelBlockIndex = modelBlockPosInChunk.X + modelBlockPosInChunk.Y * 4 + modelBlockPosInChunk.Z * 16;
                                    uint modelBlock = selectedModelData.dataBlock[(modelChunk & 0x3FFFFFFF) + modelBlockIndex];
                                    if ((modelBlock >> 30) == 2)
                                    {
                                        Point3 modelVoxelPosInChunk = voxelPosInModel % 4;
                                        int modelVoxelIndex = modelVoxelPosInChunk.X + modelVoxelPosInChunk.Y * 4 + modelVoxelPosInChunk.Z * 16;
                                        uint modelVoxelComp = selectedModelData.dataVoxel[(modelBlock & 0x3FFFFFFF) + modelVoxelIndex / 2];
                                        typeFromModel = (((modelVoxelIndex % 2) == 0) ? modelVoxelComp : (modelVoxelComp >> 16)) & 0x7FFF;
                                    }
                                    else if ((modelBlock >> 30) == 1)
                                        typeFromModel = modelBlock & 0x3FFFFFFF;
                                }
                                else if ((modelChunk >> 30) == 1)
                                    typeFromModel = modelChunk & 0x3FFFFFFF;

                                if (typeFromModel != 0)
                                {
                                    if (pointer == 0xFFFFFFFF)
                                    {
                                        worldData.editingHandler.editLock.ExitReadLock();
                                        pointer = worldData.editingHandler.getChunkDataToEdit(chunkPos);
                                        worldData.editingHandler.editLock.EnterReadLock();
                                    }
                                    worldData.editingHandler.setVoxelData(pointer, blockPosInChunk * 4 + voxelPosInBlock, typeFromModel | (1 << 15));
                                }
                            }
                        }
                        worldData.editingHandler.editLock.ExitReadLock();
                    });


                }
            }

        }

        public override void DrawUi()
        {
            ImGui.Checkbox("Place at camera", ref placeCamera);
            if (ImGui.BeginCombo("Model", selectedModelData?.name ?? "Select"))
            {
                foreach (ModelData modelData in App.worldHandler.modelHandler.models)
                {
                    bool isSelected = selectedModelData == modelData;
                    if (ImGui.Selectable(modelData.name, isSelected))
                    {
                        selectedModelData = modelData;
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
    }
}
