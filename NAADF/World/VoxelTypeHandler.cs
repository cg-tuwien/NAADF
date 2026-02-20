using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.World.Render;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.World
{
    public enum MaterialTypeBase
    {
        Diffuse = 0,
        Emissive = 1,
        MetallicRough = 2,
        MetallicMirror = 3,
    }

    public enum MaterialTypeLayer
    {
        None = 0,
        MetallicRough = 2,
        MetallicMirror = 3,
    }

    public struct VoxelType
    {
        public string ID;
        public uint renderIndex;
        public Vector3 colorBase;
        public Vector3 colorLayered;
        public MaterialTypeBase materialBase;
        public MaterialTypeLayer materialLayer;
        public float roughness;

        public VoxelType()
        {
            
        }

        public Uint4 compressForRender()
        {
            Uint4 res = new();
            res.data1 = ((uint)materialBase) | ((uint)materialLayer << 2) | ((uint)BitConverter.HalfToUInt16Bits((Half)roughness) << 16);
            res.data2 = ((uint)BitConverter.HalfToUInt16Bits((Half)colorBase.X)) | ((uint)BitConverter.HalfToUInt16Bits((Half)colorBase.Y) << 16);
            res.data3 = ((uint)BitConverter.HalfToUInt16Bits((Half)colorBase.Z)) | ((uint)BitConverter.HalfToUInt16Bits((Half)colorLayered.X) << 16);
            res.data4 = ((uint)BitConverter.HalfToUInt16Bits((Half)colorLayered.Y)) | ((uint)BitConverter.HalfToUInt16Bits((Half)colorLayered.Z) << 16);
            return res;
        }
    }

    public class VoxelTypeHandler
    {
        public DynamicStructuredBuffer typesRenderGpu;

        private int extraIdCount = 0;
        public Dictionary<string, VoxelType> typesById = new();
        public List<Uint4> typesRender = new();
        private bool needsSyncGpu = false;

        private Effect mapTypeEffect;

        public VoxelTypeHandler()
        {
            typesRenderGpu = new DynamicStructuredBuffer(App.graphicsDevice, typeof(Uint4), 5000, BufferUsage.None, ShaderAccess.ReadWrite);
            mapTypeEffect = App.contentManager.Load<Effect>("shaders/world/model/typeMapping");
            Clear();
        }

        public VoxelType ApplyVoxelType(VoxelType type)
        {
            if (string.IsNullOrEmpty(type.ID))
                type.ID = "_" + extraIdCount++;

            if (typesById.TryGetValue(type.ID, out var existing))
                return existing;

            type.renderIndex = (uint)typesRender.Count;
            typesById[type.ID] = type;
            typesRender.Add(type.compressForRender());
            needsSyncGpu = true;
            return type;
        }

        public void MapTypes16bits(VoxelType[] types, StructuredBuffer data, int count)
        {
            uint[] mapping = new uint[300];
            for (int i = 0; i < types.Length; ++i)
            {
                mapping[i] = types[i].renderIndex;
            }

            int voxelMapCount = 0;
            while (true)
            {
                int curVoxelMapCount = Math.Min(1024 * 1024 * 32, count - voxelMapCount);
                if (curVoxelMapCount <= 0)
                    break;

                mapTypeEffect.Parameters["mapping"].SetValue(mapping);
                mapTypeEffect.Parameters["voxelData"].SetValue(data);
                mapTypeEffect.Parameters["offset"].SetValue(voxelMapCount);
                mapTypeEffect.Parameters["count"].SetValue(curVoxelMapCount);

                mapTypeEffect.Techniques[0].Passes["MapTypes16"].ApplyCompute();
                App.graphicsDevice.DispatchCompute((curVoxelMapCount + 63) / 64, 1, 1);

                voxelMapCount += curVoxelMapCount;
            }

        }

        public void MapTypesWithState(VoxelType[] types, StructuredBuffer data, int offset, int count)
        {
            uint[] mapping = new uint[300];
            for (int i = 0; i < types.Length; ++i)
            {
                mapping[i] = types[i].renderIndex;
            }
            int voxelMapCount = 0;
            while (true)
            {
                int curVoxelMapCount = Math.Min(1024 * 1024 * 32, count - voxelMapCount);
                if (curVoxelMapCount <= 0)
                    break;

                mapTypeEffect.Parameters["mapping"].SetValue(mapping);
                mapTypeEffect.Parameters["voxelData"].SetValue(data);
                mapTypeEffect.Parameters["offset"].SetValue(offset + voxelMapCount);
                mapTypeEffect.Parameters["count"].SetValue(curVoxelMapCount);

                mapTypeEffect.Techniques[0].Passes["MapTypesState"].ApplyCompute();
                App.graphicsDevice.DispatchCompute((curVoxelMapCount + 63) / 64, 1, 1);

                voxelMapCount += curVoxelMapCount;
            }
        }

        public void UpdateType(VoxelType type, string? oldId = null)
        {
            if (oldId != null && oldId != type.ID)
                typesById.Remove(oldId);
            typesById[type.ID] = type;
            typesRender[(int)type.renderIndex] = type.compressForRender();
            needsSyncGpu = true;
        }

        public void Update()
        {
            if (needsSyncGpu)
            {
                typesRenderGpu.SetNewMinCount(typesRender.Count, 2);
                typesRenderGpu.SetData(typesRender.ToArray(), 0, typesRender.Count);
                needsSyncGpu = false;
            }
        }

        public void Clear()
        {
            extraIdCount = 0;
            typesRender.Clear();
            typesRender.Add(new Uint4()); // Element 0 is a placeholder as it is considered empty
            typesById.Clear();
        }
    }
}
