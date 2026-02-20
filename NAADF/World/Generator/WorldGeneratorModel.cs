using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.World.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.World.Generator
{
    public class WorldGeneratorModel : WorldGenerator
    {
        Effect generatorModelEffect;
        public ModelData modelData = null;

        public WorldGeneratorModel()
        {
            generatorModelEffect = App.contentManager.Load<Effect>("shaders/world/generator/generatorModel");
        }

        public void SetModel(ModelData modelData)
        {
            this.modelData = modelData;
        }

        public override bool IsValid()
        {
            return modelData != null;
        }

        public override void CopyToChunkData(Point3 groupOffsetInChunks, Point3 groupSizeInChunks, Point3 sizeInVoxels, StructuredBuffer chunkDataGpu, int worldSizeY, int voxelGroupSize = 1)
        {
            if (!IsValid())
                return;

            generatorModelEffect.Parameters["modelDataChunk"].SetValue(modelData.dataChunkGpu);
            generatorModelEffect.Parameters["modelDataBlock"].SetValue(modelData.dataBlockGpu);
            generatorModelEffect.Parameters["modelDataVoxel"].SetValue(modelData.dataVoxelGpu);
            generatorModelEffect.Parameters["modelSizeInChunksX"].SetValue(modelData.sizeInChunks.X);
            generatorModelEffect.Parameters["modelSizeInChunksY"].SetValue(modelData.sizeInChunks.Y);
            generatorModelEffect.Parameters["modelSizeInChunksZ"].SetValue(modelData.sizeInChunks.Z);

            generatorModelEffect.Parameters["chunkData"].SetValue(chunkDataGpu);
            generatorModelEffect.Parameters["groupOffsetInChunksX"].SetValue(groupOffsetInChunks.X);
            generatorModelEffect.Parameters["groupOffsetInChunksY"].SetValue(groupOffsetInChunks.Y);
            generatorModelEffect.Parameters["groupOffsetInChunksZ"].SetValue(groupOffsetInChunks.Z);
            generatorModelEffect.Parameters["groupSizeInChunksX"].SetValue(groupSizeInChunks.X);
            generatorModelEffect.Parameters["groupSizeInChunksY"].SetValue(groupSizeInChunks.Y);

            generatorModelEffect.Parameters["sizeInVoxelsX"].SetValue(sizeInVoxels.X);
            generatorModelEffect.Parameters["sizeInVoxelsY"].SetValue(sizeInVoxels.Y);
            generatorModelEffect.Parameters["sizeInVoxelsZ"].SetValue(sizeInVoxels.Z);

            generatorModelEffect.Parameters["worldSizeY"].SetValue(worldSizeY);

            generatorModelEffect.Techniques[0].Passes["CopyData16"].ApplyCompute();

            App.graphicsDevice.DispatchCompute(groupSizeInChunks.X, groupSizeInChunks.Y, groupSizeInChunks.Z);
        }
    }
}
