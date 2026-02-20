using ImGuiNET;
using Microsoft.Xna.Framework;
using NAADF.Common;
using NAADF.World.Render;
using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;

namespace NAADF.World.Data.Editing
{
    public class EditingToolFloodFill : EditingTool
    {
        public EditingToolFloodFill()
        {

        }

        public override void ApplyAnyInput(float gameTime)
        {
            if (IO.MOStates.IsLeftButtonToggleOn())
            {
                WorldData worldData = (WorldData)App.worldHandler.worldData;
                Vector3 rayDir = WorldRender.camera.getRayDir(IO.MOStates.New.Position);
                float hitLength;
                Point3 voxelPos, normal;
                uint hitType = worldData.RayTraversal(WorldRender.camera.GetPos().toVector3(), rayDir, out hitLength, out voxelPos, out normal);
                Vector3 hitPos = WorldRender.camera.GetPos().toVector3() + rayDir * hitLength;

                if (hitType != 0)
                {
                    uint newType = worldData.editingHandler.selectedTypeRenderIndex;
                    Queue<Point3> queue = new Queue<Point3>();
                    HashSet<Point3> visited = new HashSet<Point3>();

                    queue.Enqueue(voxelPos);
                    visited.Add(voxelPos);

                    Point3[] directions = new Point3[]
                    {
                        new Point3(1, 0, 0), new Point3(-1, 0, 0),
                        new Point3(0, 1, 0), new Point3(0, -1, 0),
                        new Point3(0, 0, 1), new Point3(0, 0, -1)
                    };

                    while (queue.Count > 0)
                    {
                        var curPos = queue.Dequeue();
                        if (SetVoxelIfType(worldData, curPos, hitType, newType))
                        {
                            foreach (var d in directions)
                            {
                                Point3 nextPos = curPos + d;
                                if (nextPos.X < 0 || nextPos.Y < 0 || nextPos.Z < 0 || nextPos.X >= worldData.sizeInVoxels.X || nextPos.Y >= worldData.sizeInVoxels.Y || nextPos.Z >= worldData.sizeInVoxels.Z)
                                    continue;
                                if (!visited.Contains(nextPos))
                                {
                                    visited.Add(nextPos);
                                    queue.Enqueue(nextPos);
                                }
                            }
                        }
                    }
                }
            }

        }

        public override void DrawUi()
        {

        }

        private bool SetVoxelIfType(WorldData worldData, Point3 pos, uint typeToReplace, uint newType)
        {
            Point3 chunkPos = pos / 16;
            Point3 voxelPosInChunk = pos - chunkPos * 16;
            uint pointer = worldData.editingHandler.getChunkDataToEdit(chunkPos);
            uint curType = worldData.editingHandler.getVoxelData(pointer, voxelPosInChunk) & 0x7FFF;
            if (curType == typeToReplace)
            {
                worldData.editingHandler.setVoxelData(pointer, voxelPosInChunk, (1 << 15) | newType);
                return true;
            }
            return false;
        }
    }
}
