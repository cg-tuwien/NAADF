using ImGuiNET;
using Microsoft.Xna.Framework;
using NAADF.Common;
using NAADF.World.Render;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.World.Data.Editing
{
    public class EditingToolSphere : EditingTool
    {

        private Vector3 pos;
        float radius = 10;
        Point3[] chunksToEditInside = new Point3[150000];
        Point3[] chunksToEditMixed = new Point3[150000];
        bool isErase = false, isContinuous = true;

        public EditingToolSphere()
        {

        }

        public override void ApplyAnyInput(float gameTime)
        {
            WorldData worldData = (WorldData)App.worldHandler.worldData;
            if (!isErase && worldData.editingHandler.selectedTypeRenderIndex == 0)
                return;
            if (IO.MOStates.New.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                Vector3 rayDir = WorldRender.camera.getRayDir(IO.MOStates.New.Position);
                float hitLength;
                Point3 voxelPos, normal;
                uint hitType = worldData.RayTraversal(WorldRender.camera.GetPos().toVector3(), rayDir, out hitLength, out voxelPos, out normal);
                Vector3 hitPos = WorldRender.camera.GetPos().toVector3() + rayDir * hitLength;

                if (hitType != 0)
                {
                    if (IO.MOStates.IsLeftButtonToggleOn())
                        pos = hitPos;
                    else
                    {
                        float lerpValue = Math.Min(1, 1.0f - 1.0f / (1 + gameTime * 0.15f / radius));
                        pos = hitPos * lerpValue + pos * (1.0f - lerpValue);
                    }

                    if (!isContinuous && IO.MOStates.Old.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
                        return;

                    Point3 minChunkPos = Point3.FromVector3(new Vector3(pos.X - radius, pos.Y - radius, pos.Z - radius) / 16);
                    minChunkPos = new Point3(Math.Max(0, minChunkPos.X), Math.Max(0, minChunkPos.Y), Math.Max(0, minChunkPos.Z));
                    Point3 maxChunkPos = Point3.FromVector3(new Vector3(pos.X + radius, pos.Y + radius, pos.Z + radius) / 16);
                    maxChunkPos = new Point3(Math.Min(worldData.sizeInChunks.X - 1, maxChunkPos.X), Math.Min(worldData.sizeInChunks.Y - 1, maxChunkPos.Y), Math.Min(worldData.sizeInChunks.Z - 1, maxChunkPos.Z));

                    float radiusSqr = radius * radius;
                    float radiusInsideSqr = (float)Math.Pow(Math.Max(0, radius - (new Vector3(7.5f)).Length()), 2);
                    float radiusOutsideSqr = (float)Math.Pow(Math.Max(0, radius + (new Vector3(7.5f)).Length()), 2);

                    // Check each chunk
                    Point3 chunkCountSize = (maxChunkPos + new Point3(1)) - minChunkPos;
                    int chunkCount = chunkCountSize.X * chunkCountSize.Y * chunkCountSize.Z;
                    int chunkCountInside = 0, chunkCountMixed = 0;
                    for (int c = 0; c < chunkCount; c++)
                    {
                        Point3 chunkPos = minChunkPos + new Point3(c % chunkCountSize.X, (c / chunkCountSize.X) % chunkCountSize.Y, c / (chunkCountSize.X * chunkCountSize.Y));
                        float distToPosSqr = (((chunkPos * 16).ToVector3() + new Vector3(8)) - pos).LengthSquared();
                        if (distToPosSqr < radiusInsideSqr)
                            chunksToEditInside[chunkCountInside++] = chunkPos;
                        else if (distToPosSqr < radiusOutsideSqr)
                            chunksToEditMixed[chunkCountMixed++] = chunkPos;
                    }
                    if (chunkCountMixed > 0)
                    {
                        Parallel.For(0, chunkCountMixed, (c) =>
                        {
                            Point3 chunkPos = chunksToEditMixed[c];
                            uint pointer = worldData.editingHandler.getChunkDataToEdit(chunkPos);
                            Vector3 posToChunk = (new Vector3(0.5f) + (chunkPos * 16).ToVector3()) - pos;
                            for (int i = 0; i < 4096; ++i)
                            {
                                Point3 voxelPosInChunk = new Point3(i % 16, (i / 16) % 16, i / 256);
                                float distToPos = (voxelPosInChunk.ToVector3() + posToChunk).LengthSquared();
                                if (distToPos < radiusSqr)
                                    worldData.editingHandler.setVoxelData(pointer, voxelPosInChunk, isErase ? 0 : (1 << 15) | worldData.editingHandler.selectedTypeRenderIndex);
                            }
                        });
                    }
                    if (chunkCountInside > 0)
                    {
                        Parallel.For(0, chunkCountInside, (c) =>
                        {
                            Point3 chunkPos = chunksToEditInside[c];
                            uint pointer = worldData.editingHandler.getChunkDataToEdit(chunkPos);
                            uint type = isErase ? 0 : (1 << 15) | worldData.editingHandler.selectedTypeRenderIndex;
                            Array.Fill(worldData.editingHandler.editData, type | (type << 16), (int)pointer, 2048);
                        });
                    }

                }
            }

        }

        public override void DrawUi()
        {
            ImGui.Checkbox("Erase", ref isErase);
            ImGui.Checkbox("Continuous", ref isContinuous);
            ImGui.SliderFloat("Radius", ref radius, 1, 400, "%2.f", ImGuiSliderFlags.Logarithmic);
        }
    }
}
