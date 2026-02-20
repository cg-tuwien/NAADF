using ImGuiNET;
using Microsoft.Xna.Framework;
using NAADF.Common;
using NAADF.World.Data;
using NAADF.World.Render;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NAADF.Gui
{
    public static partial class WorldUi
    {
        public static float rotationSpeed = 1;

        public static bool isEntitySpawning = false;
        private static Point3 entityInstanceSpawnCount;
        private static int entityIdSpawn;
        private static int selectedEntity = -1;
        private static Point3 spawnAmount = new Point3(1, 1, 1);

        public static void DrawEntities()
        {
            WorldData worldData = App.worldHandler.worldData;
            EntityHandler handler = worldData.entityHandler;

            if (isEntitySpawning)
            {
                PositionEntitiesFromMouse();
                if (IO.MOStates.IsLeftButtonToggleOn())
                    isEntitySpawning = false;

                if (IO.KBStates.IsKeyToggleDown(Microsoft.Xna.Framework.Input.Keys.Escape))
                {
                    isEntitySpawning = false;
                    int entityCount = entityInstanceSpawnCount.X * entityInstanceSpawnCount.Y * entityInstanceSpawnCount.Z;
                    handler.entityInstances.RemoveRange(handler.entityInstances.Count - entityCount, entityCount);
                }

                ImGui.TextWrapped("Place entities by left clicking or esc to cancel!");
                return;
            }

            if (ImGui.BeginListBox("Entity types", new System.Numerics.Vector2(0, 0)))
            {
                for (int i = 0; i < handler.entities.Count; i++)
                {
                    bool isSelected = (i == selectedEntity);

                    if (ImGui.Selectable(handler.entities[i].name + "##" + i))
                        selectedEntity = i;

                    if (ImGui.BeginPopupContextItem("EntityContext" + i))
                    {
                        if (ImGui.MenuItem("Spawn"))
                        {
                            SpawnEntities(i);
                        }
                        ImGui.EndPopup();
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndListBox();
            }

            ImGui.SliderFloat("Rotation speed", ref rotationSpeed, 0, 100, "%.2f", ImGuiSliderFlags.Logarithmic);
            if (ImGui.Button("Clear all"))
            {
                isEntitySpawning = false;
                handler.entityInstances.Clear();
            }
            ImGui.SliderInt3("Amount to spawn", ref spawnAmount.X, 1, 25);
            if (selectedEntity != -1)
            {
                var entity = handler.entities[selectedEntity];
                ImGui.Text("Size: " + entity.size.X + " | " + entity.size.Y + " | " + entity.size.Z);
            }

            ImGui.Spacing();
            ImGui.TextWrapped("INFO: Entity types can be created in the model tab. Entity instances can be spawned by right clicking an entity type. The entity instances are then shown infront of the camera, in order to place press the left mouse button. Note that the maximum entity size is 128^3 voxels per default.");
        }

        public static void SpawnEntities(int entityId)
        {
            isEntitySpawning = true;
            entityIdSpawn = entityId;
            EntityHandler handler = App.worldHandler.worldData.entityHandler;
            entityInstanceSpawnCount = spawnAmount;
            for (int i = 0; i < spawnAmount.X * spawnAmount.Y * spawnAmount.Z; i++)
            {
                handler.addEntityInstance(entityId, Vector3.Zero);
            }
            PositionEntitiesFromMouse();
        }

        private static void PositionEntitiesFromMouse()
        {
            EntityHandler handler = App.worldHandler.worldData.entityHandler;
            EntityData entityData = handler.entities[entityIdSpawn];
            Vector3 entitySpacing = new Vector3(Math.Max(Math.Max(entityData.size.X, entityData.size.Y), entityData.size.Z)) * 1.5f;
            Vector3 entitiesSize = entitySpacing * spawnAmount.ToVector3();
            Vector3 rayDir = WorldRender.camera.getRayDir(IO.MOStates.New.Position);
            Vector3 placementPosStart = WorldRender.camera.GetPos().toVector3() + rayDir * (entitiesSize.Length() * 0.5f + 20) - (entitiesSize - entitySpacing) * 0.5f;
            int curInstanceID = handler.entityInstances.Count - entityInstanceSpawnCount.X * entityInstanceSpawnCount.Y * entityInstanceSpawnCount.Z;
            for (int x = 0; x < spawnAmount.X; x++)
            {
                for (int y = 0; y < spawnAmount.Y; y++)
                {
                    for (int z = 0; z < spawnAmount.Z; z++)
                    {
                        Vector3 offset = new Vector3(x, y, z) * entitySpacing;
                        handler.updateEntityPos(curInstanceID++, placementPosStart + offset);
                    }
                }
            }
        }
    }
}

