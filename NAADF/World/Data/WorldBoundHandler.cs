using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.Gui;
using NAADF.World.Render;
using System;

namespace NAADF.World.Data
{
    public struct BoundQueueInfo
    {
        public int start;
        public int size;

        public BoundQueueInfo(int start, int size)
        {
            this.start = start;
            this.size = size;
        }
    }

    public class WorldBoundHandler : IDisposable
    {
        public readonly Point3 BoundGroupSize = new Point3(4, 4, 4);
        public int maxGroupBoundDispatch = 512 * 64;

        public StructuredBuffer boundQueueInfoGpu, boundRefinedInfoGpu;
        public StructuredBuffer boundGroupQueuesGpu;
        public StructuredBuffer boundGroupMasksGpu;
        public IndirectDrawBuffer boundGroupQueueDispatchCount;

        private Effect boundsCalcEffect;
        private WorldData worldData;
        private int boundGroupCount;
        private Point3 groupSize;
        private int frameIndex = int.MaxValue;

        public WorldBoundHandler(WorldData worldData)
        {
            this.worldData = worldData;
            groupSize = new Point3(worldData.sizeInChunks.X / BoundGroupSize.X, worldData.sizeInChunks.Y / BoundGroupSize.Y, worldData.sizeInChunks.Z / BoundGroupSize.Z);
            boundGroupCount = worldData.chunkCount / (BoundGroupSize.X * BoundGroupSize.Y * BoundGroupSize.Z);
            boundsCalcEffect = App.contentManager.Load<Effect>("shaders/world/data/boundsCalc");
            boundQueueInfoGpu = new StructuredBuffer(App.graphicsDevice, typeof(BoundQueueInfo), 32 * 3, BufferUsage.None, ShaderAccess.ReadWrite);
            boundRefinedInfoGpu = new StructuredBuffer(App.graphicsDevice, typeof(uint), 3, BufferUsage.None, ShaderAccess.ReadWrite);
            boundGroupQueuesGpu = new StructuredBuffer(App.graphicsDevice, typeof(uint), 32 * 3 * boundGroupCount, BufferUsage.None, ShaderAccess.ReadWrite);
            boundGroupMasksGpu = new StructuredBuffer(App.graphicsDevice, typeof(Uint3), boundGroupCount, BufferUsage.None, ShaderAccess.ReadWrite);

            boundGroupQueueDispatchCount = new IndirectDrawBuffer(App.graphicsDevice, BufferUsage.None, ShaderAccess.ReadWrite, 5);
            boundGroupQueueDispatchCount.SetData(new DispatchComputeArguments { GroupCountX = 1, GroupCountY = 1, GroupCountZ = 1 });
        }

        public void Initialize()
        {
            BoundQueueInfo[] boundQueueInfoNew = new BoundQueueInfo[32 * 3];

            // Bounds info groups
            for (int i = 0; i < 32; ++i)
            {
                for (int xyz = 0; xyz < 3; ++xyz)
                {
                    boundQueueInfoNew[i * 3 + xyz] = new BoundQueueInfo(0, i == 0 ? boundGroupCount : 0);
                }
            }

            boundQueueInfoGpu.SetData(boundQueueInfoNew);

            // Add all groups to x, y, and z bounds with size 0
            boundsCalcEffect.Parameters["groupSizeX"].SetValue(groupSize.X);
            boundsCalcEffect.Parameters["groupSizeY"].SetValue(groupSize.Y);
            boundsCalcEffect.Parameters["groupSizeZ"].SetValue(groupSize.Z);
            boundsCalcEffect.Parameters["boundGroupMasks"].SetValue(boundGroupMasksGpu);
            boundsCalcEffect.Parameters["boundGroupQueues"].SetValue(boundGroupQueuesGpu);
            boundsCalcEffect.Parameters["boundGroupQueueMaxSize"].SetValue(boundGroupCount);

            int boundsInitAmount = 0;
            while (true)
            {
                int curBoundsInitAmount = Math.Min(1024 * 1024 * 32, boundGroupCount - boundsInitAmount);
                if (curBoundsInitAmount <= 0)
                    break;

                boundsCalcEffect.Parameters["boundsInitOffset"].SetValue(boundsInitAmount);
                boundsCalcEffect.Techniques[0].Passes["AddInitialGroupsToBoundQueue"].ApplyCompute();
                App.graphicsDevice.DispatchCompute(curBoundsInitAmount / 64, 1, 1);
                boundsInitAmount += curBoundsInitAmount;
            }

        }

        public void Update()
        {

            if (maxGroupBoundDispatch == 0)
                return;
            frameIndex--;
            boundsCalcEffect.Parameters["frameIndex"].SetValue(frameIndex);
            boundsCalcEffect.Parameters["groupSizeX"].SetValue(groupSize.X);
            boundsCalcEffect.Parameters["groupSizeY"].SetValue(groupSize.Y);
            boundsCalcEffect.Parameters["groupSizeZ"].SetValue(groupSize.Z);
            boundsCalcEffect.Parameters["chunkSizeX"].SetValue(worldData.sizeInChunks.X);
            boundsCalcEffect.Parameters["chunkSizeY"].SetValue(worldData.sizeInChunks.Y);
            boundsCalcEffect.Parameters["chunkSizeZ"].SetValue(worldData.sizeInChunks.Z);
            boundsCalcEffect.Parameters["chunks"].SetValue(worldData.dataChunkGpu);
            boundsCalcEffect.Parameters["boundQueueInfo"].SetValue(boundQueueInfoGpu);
            boundsCalcEffect.Parameters["boundGroupQueues"].SetValue(boundGroupQueuesGpu);
            boundsCalcEffect.Parameters["boundGroupMasks"].SetValue(boundGroupMasksGpu);
            boundsCalcEffect.Parameters["boundRefinedInfo"].SetValue(boundRefinedInfoGpu);
            boundsCalcEffect.Parameters["boundGroupQueueDispatchCount"].SetValue(boundGroupQueueDispatchCount);
            boundsCalcEffect.Parameters["maxGroupBoundDispatch"].SetValue(maxGroupBoundDispatch);
            boundsCalcEffect.Parameters["boundGroupQueueMaxSize"].SetValue(boundGroupCount);

            for (int i = 0; i < 5; ++i)
            {
                boundsCalcEffect.Techniques[0].Passes["PrepareGroupBounds"].ApplyCompute();
                App.graphicsDevice.DispatchCompute(1, 1, 1);

                boundsCalcEffect.Techniques[0].Passes["ComputeGroupBounds"].ApplyCompute();
                App.graphicsDevice.DispatchComputeIndirect(boundGroupQueueDispatchCount);
            }
        }

        public void DrawDebugInfo()
        {
            int speedup = maxGroupBoundDispatch / 512;
            ImGui.SliderInt("AADF speedup", ref speedup, 0, 512);
            ImGuiCommon.HelperIcon("How fast the AADFs should be computed in the background", 500);
            maxGroupBoundDispatch = speedup * 512;
        }

        public void Dispose()
        {
            worldData = null;
            boundQueueInfoGpu?.Dispose();
            boundRefinedInfoGpu?.Dispose();
            boundGroupQueuesGpu?.Dispose();
            boundGroupMasksGpu?.Dispose();
            boundGroupQueueDispatchCount?.Dispose();
        }
    }
}
