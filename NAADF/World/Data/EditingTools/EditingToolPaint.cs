using ImGuiNET;
using Microsoft.Xna.Framework;
using NAADF.Common;
using NAADF.World.Render;
using System;
using System.Threading.Tasks;

namespace NAADF.World.Data.Editing
{
    public class EditingToolPaint : EditingTool
    {
        private Vector3 pos;
        float radius = 10;
        Point3[] chunksToEdit = new Point3[150000];

        public EditingToolPaint()
        {

        }

        public override void ApplyAnyInput(float gameTime)
        {
            if (IO.MOStates.New.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                WorldData worldData = (WorldData)App.worldHandler.worldData;
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

                    Point3 minChunkPos = Point3.FromVector3(new Vector3(pos.X - radius, pos.Y - radius, pos.Z - radius) / 16);
                    minChunkPos = new Point3(Math.Max(0, minChunkPos.X), Math.Max(0, minChunkPos.Y), Math.Max(0, minChunkPos.Z));
                    Point3 maxChunkPos = Point3.FromVector3(new Vector3(pos.X + radius, pos.Y + radius, pos.Z + radius) / 16);
                    maxChunkPos = new Point3(Math.Min(worldData.sizeInChunks.X - 1, maxChunkPos.X), Math.Min(worldData.sizeInChunks.Y - 1, maxChunkPos.Y), Math.Min(worldData.sizeInChunks.Z - 1, maxChunkPos.Z));

                    float radiusSqr = radius * radius;
                    float radiusOutsideSqr = (float)Math.Pow(Math.Max(0, radius + (new Vector3(7.5f)).Length()), 2);

                    // Check each chunk
                    Point3 chunkCountSize = (maxChunkPos + new Point3(1)) - minChunkPos;
                    int chunkCount = chunkCountSize.X * chunkCountSize.Y * chunkCountSize.Z;
                    int chunkCountToEdit = 0;
                    for (int c = 0; c < chunkCount; c++)
                    {
                        Point3 chunkPos = minChunkPos + new Point3(c % chunkCountSize.X, (c / chunkCountSize.X) % chunkCountSize.Y, c / (chunkCountSize.X * chunkCountSize.Y));
                        float distToPosSqr = (((chunkPos * 16).ToVector3() + new Vector3(8)) - pos).LengthSquared();
                        if (distToPosSqr < radiusOutsideSqr)
                            chunksToEdit[chunkCountToEdit++] = chunkPos;
                    }
                    if (chunkCountToEdit > 0)
                    {
                        Parallel.For(0, chunkCountToEdit, (c) =>
                        {
                            Point3 chunkPos = chunksToEdit[c];
                            uint pointer = worldData.editingHandler.getChunkDataToEdit(chunkPos);
                            worldData.editingHandler.editLock.EnterReadLock();
                            Vector3 posToChunk = (new Vector3(0.5f) + (chunkPos * 16).ToVector3()) - pos;
                            for (int i = 0; i < 4096; ++i)
                            {
                                Point3 voxelPosInChunk = new Point3(i % 16, (i / 16) % 16, i / 256);
                                float distToPos = (voxelPosInChunk.ToVector3() + posToChunk).LengthSquared();
                                if (distToPos < radiusSqr)
                                {
                                    uint curType = worldData.editingHandler.getVoxelData(pointer, voxelPosInChunk);
                                    if (curType != 0)
                                        worldData.editingHandler.setVoxelData(pointer, voxelPosInChunk, (1 << 15) | worldData.editingHandler.selectedTypeRenderIndex);
                                }
                            }
                            worldData.editingHandler.editLock.ExitReadLock();
                        });
                    }
                    

                }
            }

        }

        public override void DrawUi()
        {
            ImGui.SliderFloat("Radius", ref radius, 1, 400, "%2.f", ImGuiSliderFlags.Logarithmic);
        }
    }
}
