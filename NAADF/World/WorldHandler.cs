using NAADF.Common;
using NAADF.Gui;
using NAADF.World.Data;
using NAADF.World.Generator;
using NAADF.World.Model;
using NAADF.World.Render;
using System.IO;

namespace NAADF.World
{
    public class WorldHandler
    {
        public WorldGeneratorModel worldGenerator;
        public WorldData worldData;
        public PathHandler pathHandler;
        public VoxelTypeHandler voxelTypeHandler;
        public ModelHandler modelHandler;
        public int worldGenSegmentSizeInGroups = 4; // A group is 4^3 chunks or 64^3 voxels
        public Point3 worldSizeToUseInWorldGenSegments = new Point3(16, 2, 16);

        public WorldHandler()
        {
            modelHandler = new ModelHandler();
            voxelTypeHandler = new VoxelTypeHandler();
            WorldRender.Initialize();
            pathHandler = new PathHandler();
        }

        public void Initialize()
        {
            worldData = new WorldData(worldSizeToUseInWorldGenSegments * worldGenSegmentSizeInGroups * 64, worldGenSegmentSizeInGroups);

            // Try to load default sample scene
            LoadModelScene("Content\\oasis.cvox");
        }

        public void LoadModelScene(string fileName)
        {
            worldGenerator = new WorldGeneratorModel();

            if (File.Exists(fileName))
            {
                ModelData modelData = null;
                if (fileName.EndsWith(".cvox"))
                    modelData = ModelData.Load(fileName);
                else if (fileName.EndsWith(".vox"))
                    modelData = ModelData.ImportFromVox(fileName);
                else if (fileName.EndsWith(".vl32"))
                    modelData = ModelData.ImportFromVL32(fileName);

                worldGenerator.SetModel(modelData);
            }

            worldData.GenerateWorld(worldGenerator);
        }

        public void ApplyAndGenerateNewWorldData(WorldData newWorldData, WorldGenerator generator)
        {
            worldData?.Dispose();
            worldData = newWorldData;
            worldData.GenerateWorld(generator);
        }

        public void ScreenUpdate()
        {
            WorldRender.render.ScreenUpdate();
        }

        public void Update(float gameTime)
        {
            voxelTypeHandler.Update();
            pathHandler.Update(gameTime);
            WorldRender.render.Update(gameTime);
            worldData?.Update(gameTime);
        }

        public void Render(float gameTime)
        {
            if (worldData != null)
                WorldRender.render.Render(worldData, gameTime);
        }

        public void RenderUi()
        {
            if (GuiHandler.ShowUi)
            {
                pathHandler.RenderUi();
            }
        }
    }
}
