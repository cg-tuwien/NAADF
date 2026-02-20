using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.Gui;
using NAADF.World.Data;
using NAADF.World.Render;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NAADF.World.Data
{

    public struct EntityInstance
    {
        public Vector3 position;
        public Vector4 quaternion;
        public uint voxelStart;
        public uint entity;
        public Point3 size;
    };

    public struct EntityChunkInstanceGpu
    {
        public Uint4 data;
        public uint data2;
    };

    public struct EntityChunkInstanceHash
    {
        public int hash;
        public uint data, newPointer;

        public EntityChunkInstanceHash(uint data, int hash)
        {
            this.data = data;
            this.hash = hash;
            newPointer = 0;
        }
    };

    public class EntityChunkInstanceHashComparer : IEqualityComparer<EntityChunkInstanceHash>
    {
        List<uint> entityChunkInstances;

        public EntityChunkInstanceHashComparer(List<uint> entityChunkInstances)
        {
            this.entityChunkInstances = entityChunkInstances;
        }

        public bool Equals(EntityChunkInstanceHash x, EntityChunkInstanceHash y)
        {
            if (x.hash != y.hash) return false;

            uint sizeX = x.data & 0xFF;
            uint sizeY = y.data & 0xFF;
            if (sizeX != sizeY) return false;

            int pointerX = (int)(x.data >> 8);
            int pointerY = (int)(y.data >> 8);
            for (int i = 0; i < sizeX; ++i)
            {
                if (entityChunkInstances[pointerX + i] != entityChunkInstances[pointerY + i])
                    return false;
            }
            return true;
        }

        public int GetHashCode(EntityChunkInstanceHash obj)
        {
            return obj.hash;
        }
    }

    public class EntityHandler : IDisposable
    {
        // CPU
        public List<EntityData> entities;
        public List<EntityInstance> entityInstances;
        int entityVoxelDataPointer = 0;

        // TODO remove most of them by writing directly into the dynamic buffer
        private List<uint> entityChunkInstancesInfo, entityChunkInstancesInfoOld, entityChunkInstances;
        private EntityChunkInstanceGpu[] entityChunkInstancesProcessed;
        private Uint2[] chunkUpdate;
        private uint[] chunkEntityData;
        private Uint4[] entityInstancesHistory;
        private HashSet<EntityChunkInstanceHash> entityChunkInstancesHashed;
        private uint[] hashCoefficients;
        private BitArray chunkChanges;

        // CPU -> GPU
        private StructuredBuffer entityChunkInstancesDynamic, entityInstancesHistoryDynamic;
        private StructuredBuffer chunkUpdateDynamic;

        // GPU
        public StructuredBuffer entityChunkInstancesGpu, entityInstancesHistoryGpu;
        public StructuredBuffer entityVoxelDataGpu;


        private WorldData worldData;
        private Effect entityUpdateEffect;
        private long timeTicks = 0;
        public int bytesCpuGpuCopy;
        public float chunkProcessingTime = 0;

        public EntityHandler(WorldData worldData)
        {
            if (!BuildFlags.Entities)
                return;
            this.worldData = worldData;
            entityUpdateEffect = App.contentManager.Load<Effect>("shaders/world/data/entityUpdate");

            hashCoefficients = new uint[256 + 1];
            hashCoefficients[256] = 1;
            for (int i = 256 - 1; i >= 0; --i)
            {
                hashCoefficients[i] = 31 * hashCoefficients[i + 1];
            }

            entities = new List<EntityData>();
            entityInstances = new List<EntityInstance>();
            chunkEntityData = new uint[worldData.chunkCount];
            entityChunkInstances = new(new uint[2000000]);
            chunkUpdate = new Uint2[2000000];
            entityChunkInstancesProcessed = new EntityChunkInstanceGpu[2000000];
            entityChunkInstancesProcessed = new EntityChunkInstanceGpu[2000000];
            entityInstancesHistory = new Uint4[16384 * 64];
            entityChunkInstancesInfo = new();
            entityChunkInstancesInfoOld = new();
            entityChunkInstancesHashed = new HashSet<EntityChunkInstanceHash>(new EntityChunkInstanceHashComparer(entityChunkInstances));
            chunkChanges = new BitArray(worldData.chunkCount);

            entityInstancesHistoryDynamic = new StructuredBuffer(App.graphicsDevice, typeof(Uint4), 16384 * 64, BufferUsage.None, ShaderAccess.Read, StructuredBufferType.Basic, -1, true);
            entityChunkInstancesDynamic = new StructuredBuffer(App.graphicsDevice, typeof(EntityChunkInstanceGpu), 2000000, BufferUsage.None, ShaderAccess.Read, StructuredBufferType.Basic, -1, true);
            chunkUpdateDynamic = new StructuredBuffer(App.graphicsDevice, typeof(Uint2), 2000000, BufferUsage.None, ShaderAccess.ReadWrite);

            entityVoxelDataGpu = new StructuredBuffer(App.graphicsDevice, typeof(uint), 100_000_000, BufferUsage.None, ShaderAccess.ReadWrite);
            entityInstancesHistoryGpu = new StructuredBuffer(App.graphicsDevice, typeof(Uint4), 16384 * 64, BufferUsage.None, ShaderAccess.ReadWrite);
            entityChunkInstancesGpu = new StructuredBuffer(App.graphicsDevice, typeof(EntityChunkInstanceGpu), 2000000, BufferUsage.None, ShaderAccess.ReadWrite);

            string[] modelsToLoad = ["car1", "car2", "cars"];
            //foreach (string modelName in modelsToLoad)
            //{
            //    ModelData modelData = ModelData.Load(".\\Content\\Samples\\" + modelName + ".cvox");
            //    if (modelData != null)
            //    {
            //        App.worldHandler.modelHandler.AddModel(modelData);
            //        var newEntity = new EntityData(modelData);
            //        addEntity(newEntity);
            //    }
            //}

        }

        public void Update(float gameTime, int taaIndex)
        {
            if (!BuildFlags.Entities)
                return;
            timeTicks += (long)(gameTime * 1000);

            for (int i = 0; i < entityInstances.Count; i++)
            {
                updateEntityRotation(i, Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), 2 + (float)((timeTicks / 4000000.0) * (double)WorldUi.rotationSpeed)));
            }

            // Reset counters
            Stopwatch sw = Stopwatch.StartNew();
            entityChunkInstancesInfo.Clear();
            entityChunkInstancesHashed.Clear();
            for (int i = 0; i < entityChunkInstancesInfoOld.Count; ++i)
            {
                chunkEntityData[entityChunkInstancesInfoOld[i]] = 0;
            }

            // Count instances for relevant chunks
            for (int i = 0; i < entityInstances.Count; ++i)
            {
                var entityInstance = entityInstances[i];
                Vector3 minPos = new Vector3(99999999), maxPos = new Vector3(-99999999);
                for (int z = 0; z < 2; ++z)
                {
                    for (int y = 0; y < 2; ++y)
                    {
                        for (int x = 0; x < 2; ++x)
                        {
                            Vector3 cornerPos = entityInstance.size.ToVector3() * new Vector3(x, y, z);
                            Vector3 rotatedCornerPos = Vector3.Transform(cornerPos, new Quaternion(entityInstance.quaternion));
                            minPos.X = Math.Min(minPos.X, rotatedCornerPos.X);
                            minPos.Y = Math.Min(minPos.Y, rotatedCornerPos.Y);
                            minPos.Z = Math.Min(minPos.Z, rotatedCornerPos.Z);
                            maxPos.X = Math.Max(maxPos.X, rotatedCornerPos.X);
                            maxPos.Y = Math.Max(maxPos.Y, rotatedCornerPos.Y);
                            maxPos.Z = Math.Max(maxPos.Z, rotatedCornerPos.Z);
                        }
                    }
                }

                Vector3 minChunkPos = (entityInstance.position + minPos) / 16.0f;
                Vector3 maxChunkPos = (entityInstance.position + maxPos) / 16.0f;
                Point3 boxSize = new();
                boxSize.X = (int)maxChunkPos.X - (int)minChunkPos.X;
                boxSize.Y = (int)maxChunkPos.Y - (int)minChunkPos.Y;
                boxSize.Z = (int)maxChunkPos.Z - (int)minChunkPos.Z;

                for (int z = 0; z <= boxSize.Z; ++z)
                {
                    for (int y = 0; y <= boxSize.Y; ++y)
                    {
                        for (int x = 0; x <= boxSize.X; ++x)
                        {
                            Point3 chunkPos = new Point3((int)minChunkPos.X + x, (int)minChunkPos.Y + y, (int)minChunkPos.Z + z);
                            int chunkIndex = chunkPos.X + chunkPos.Y * worldData.sizeInChunks.X + chunkPos.Z * worldData.sizeInChunks.X * worldData.sizeInChunks.Y;

                            if (chunkPos.X < 0 || chunkPos.Y < 0 || chunkPos.Z < 0 || chunkPos.X >= worldData.sizeInChunks.X || chunkPos.Y >= worldData.sizeInChunks.Y || chunkPos.Z >= worldData.sizeInChunks.Z)
                                continue;
                            uint oldEntityData = chunkEntityData[chunkIndex]++;
                            if (oldEntityData == 0)
                                entityChunkInstancesInfo.Add((uint)chunkIndex);
                        }
                    }
                }
            }

            uint chunkInstanceCounter = 0;
            for (int i = 0; i < entityChunkInstancesInfo.Count; ++i)
            {
                uint chunkIndex = entityChunkInstancesInfo[i];
                uint count = chunkEntityData[chunkIndex];
                chunkEntityData[chunkIndex] = chunkInstanceCounter << 8;
                chunkInstanceCounter += count;
            }

            // Count instances again and create entityChunkInstances
            for (int i = 0; i < entityInstances.Count; ++i)
            {
                var entityInstance = entityInstances[i];
                Vector3 minPos = new Vector3(99999999), maxPos = new Vector3(-99999999);
                for (int z = 0; z < 2; ++z)
                {
                    for (int y = 0; y < 2; ++y)
                    {
                        for (int x = 0; x < 2; ++x)
                        {
                            Vector3 cornerPos = entityInstance.size.ToVector3() * new Vector3(x, y, z);
                            Vector3 rotatedCornerPos = Vector3.Transform(cornerPos, new Quaternion(entityInstance.quaternion));
                            minPos.X = Math.Min(minPos.X, rotatedCornerPos.X);
                            minPos.Y = Math.Min(minPos.Y, rotatedCornerPos.Y);
                            minPos.Z = Math.Min(minPos.Z, rotatedCornerPos.Z);
                            maxPos.X = Math.Max(maxPos.X, rotatedCornerPos.X);
                            maxPos.Y = Math.Max(maxPos.Y, rotatedCornerPos.Y);
                            maxPos.Z = Math.Max(maxPos.Z, rotatedCornerPos.Z);
                        }
                    }
                }

                Vector3 minChunkPos = (entityInstance.position + minPos) / 16.0f;
                Vector3 maxChunkPos = (entityInstance.position + maxPos) / 16.0f;
                Point3 boxSize = new();
                boxSize.X = (int)maxChunkPos.X - (int)minChunkPos.X;
                boxSize.Y = (int)maxChunkPos.Y - (int)minChunkPos.Y;
                boxSize.Z = (int)maxChunkPos.Z - (int)minChunkPos.Z;

                for (int z = 0; z <= boxSize.Z; ++z)
                {
                    for (int y = 0; y <= boxSize.Y; ++y)
                    {
                        for (int x = 0; x <= boxSize.X; ++x)
                        {
                            Point3 chunkPos = new Point3((int)minChunkPos.X + x, (int)minChunkPos.Y + y, (int)minChunkPos.Z + z);
                            int chunkIndex = chunkPos.X + chunkPos.Y * worldData.sizeInChunks.X + chunkPos.Z * worldData.sizeInChunks.X * worldData.sizeInChunks.Y; 

                            if (chunkPos.X < 0 || chunkPos.Y < 0 || chunkPos.Z < 0 || chunkPos.X >= worldData.sizeInChunks.X || chunkPos.Y >= worldData.sizeInChunks.Y || chunkPos.Z >= worldData.sizeInChunks.Z)
                                continue;
                            uint oldEntityData = chunkEntityData[chunkIndex]++;
                            uint pointer = oldEntityData >> 8;
                            entityChunkInstances[(int)(pointer + (oldEntityData & 0xFF))] = (uint)i;
                        }
                    }
                }
            }
            // Create final entityChunkInstances and chunk update list
            int entityChunkInstanceCount = 0;
            for (int i = 0; i < entityChunkInstancesInfo.Count; ++i)
            {
                uint chunkIndex = entityChunkInstancesInfo[i];
                uint entityData = chunkEntityData[chunkIndex];
                uint entityDataPointer = entityData >> 8;
                uint entityDataSize = entityData & 0xFF;

                int hash = 0;
                for (int e = 0; e < entityDataSize; ++e)
                {
                    hash += (int)(hashCoefficients[e] * entityChunkInstances[(int)(entityDataPointer + e)]);
                }

                uint finalPointerAndSize = 0;
                var newEntityChunkInstanceHash = new EntityChunkInstanceHash(entityData, hash);
                EntityChunkInstanceHash existingEntityChunkInstanceHash;
                if (entityChunkInstancesHashed.TryGetValue(newEntityChunkInstanceHash, out existingEntityChunkInstanceHash))
                {
                    finalPointerAndSize = existingEntityChunkInstanceHash.newPointer;
                }
                else
                {
                    finalPointerAndSize = ((uint)entityChunkInstanceCount << 8) | entityDataSize;
                    newEntityChunkInstanceHash.newPointer = finalPointerAndSize;
                    for (int e = 0; e < entityDataSize; ++e)
                    {
                        uint entityInstanceID = entityChunkInstances[(int)(entityDataPointer + e)];
                        EntityInstance entityInstance = entityInstances[(int)entityInstanceID];
                        Point3 posComp = Point3.FromVector3(entityInstance.position * 128);
                        Uint2 quaternionComp = compressQuaternion(Quaternion.Inverse(new Quaternion(entityInstance.quaternion)).ToVector4());

                        EntityChunkInstanceGpu instance = new EntityChunkInstanceGpu();
                        instance.data.data1 = (uint)posComp.X | (((uint)posComp.Y & 0x7FF) << 21);
                        instance.data.data2 = (uint)posComp.Z | (((uint)posComp.Y >> 11) << 21) | (((uint)entityInstance.size.Z >> 4) << 29);
                        instance.data.data3 = quaternionComp.data1;
                        instance.data.data4 = quaternionComp.data2 | (entityInstance.voxelStart << 12);
                        instance.data2 = entityInstance.entity | ((uint)entityInstance.size.X << 14) | ((uint)entityInstance.size.Y << 21) | (((uint)entityInstance.size.Z & 0xF) << 28);
                        entityChunkInstancesProcessed[entityChunkInstanceCount++] = instance;
                    }
                    entityChunkInstancesHashed.Add(newEntityChunkInstanceHash);
                }

                Point3 chunkPos = new Point3((int)chunkIndex % worldData.sizeInChunks.X, ((int)chunkIndex / worldData.sizeInChunks.X) % worldData.sizeInChunks.Y, (int)chunkIndex / (worldData.sizeInChunks.X * worldData.sizeInChunks.Y));
                uint chunkPosComp = (uint)chunkPos.X | ((uint)chunkPos.Y << 11) | ((uint)chunkPos.Z << 21);

                Uint2 update;
                update.data1 = chunkPosComp;
                update.data2 = finalPointerAndSize;
                chunkUpdate[i] = update;
            }
            int updateCount = entityChunkInstancesInfo.Count;
            for (int i = 0; i < entityChunkInstancesInfoOld.Count; ++i)
            {
                uint chunkIndex = entityChunkInstancesInfoOld[i];
                uint entityData = chunkEntityData[chunkIndex];
                uint entityDataSize = entityData & 0xFF;
                if (entityDataSize == 0)
                {
                    Point3 chunkPos = new Point3((int)chunkIndex % worldData.sizeInChunks.X, ((int)chunkIndex / worldData.sizeInChunks.X) % worldData.sizeInChunks.Y, (int)chunkIndex / (worldData.sizeInChunks.X * worldData.sizeInChunks.Y));
                    uint chunkPosComp = (uint)chunkPos.X | ((uint)chunkPos.Y << 11) | ((uint)chunkPos.Z << 21);

                    Uint2 update;
                    update.data1 = chunkPosComp;
                    update.data2 = 0;
                    chunkUpdate[updateCount++] = update;
                }
            }

            for (int i = 0; i < entityInstances.Count; ++i)
            {
                EntityInstance entityInstance = entityInstances[i];
                Point3 posComp = Point3.FromVector3(entityInstance.position * 128);
                Uint2 quaternionComp = compressQuaternion(entityInstance.quaternion);

                Uint4 comp;
                comp.data1 = (uint)posComp.X | (((uint)posComp.Y & 0x7FF) << 21);
                comp.data2 = (uint)posComp.Z | (((uint)posComp.Y >> 11) << 21);
                comp.data3 = quaternionComp.data1;
                comp.data4 = quaternionComp.data2;
                entityInstancesHistory[i] = comp;
            }

            if (updateCount > 0)
                chunkUpdateDynamic.SetData(chunkUpdate, 0, updateCount);
            if (entityChunkInstanceCount > 0)
                entityChunkInstancesDynamic.SetData(entityChunkInstancesProcessed, 0, entityChunkInstanceCount);
            if (entityInstances.Count > 0)
                entityInstancesHistoryDynamic.SetData(entityInstancesHistory, 0, entityInstances.Count);

            sw.Stop();
            chunkProcessingTime = chunkProcessingTime * 0.99f + (float)sw.Elapsed.TotalMilliseconds * 0.01f;

            entityUpdateEffect.Parameters["chunkUpdatesDynamic"].SetValue(chunkUpdateDynamic);
            entityUpdateEffect.Parameters["entityChunkInstancesDynamic"].SetValue(entityChunkInstancesDynamic);
            entityUpdateEffect.Parameters["entityHistoryDynamic"].SetValue(entityInstancesHistoryDynamic);
            entityUpdateEffect.Parameters["chunks"].SetValue(worldData.dataChunkGpu);
            entityUpdateEffect.Parameters["entityChunkInstances"].SetValue(entityChunkInstancesGpu);
            entityUpdateEffect.Parameters["entityInstancesHistory"].SetValue(entityInstancesHistoryGpu);
            entityUpdateEffect.Parameters["taaIndex"].SetValue(taaIndex);
            entityUpdateEffect.Parameters["entityInstanceCount"].SetValue(entityInstances.Count);
            entityUpdateEffect.Parameters["entityChunkInstanceCount"].SetValue(entityChunkInstanceCount);
            entityUpdateEffect.Parameters["updateCount"].SetValue(updateCount);

            if (updateCount > 0)
            {
                entityUpdateEffect.Techniques[0].Passes["UpdateChunks"].ApplyCompute();
                App.graphicsDevice.DispatchCompute((updateCount + 63) / 64, 1, 1);
            }

            if (entityChunkInstanceCount > 0)
            {
                entityUpdateEffect.Techniques[0].Passes["CopyEntityChunkInstances"].ApplyCompute();
                App.graphicsDevice.DispatchCompute((entityChunkInstanceCount + 63) / 64, 1, 1);
            }

            if (entityInstances.Count > 0)
            {
                entityUpdateEffect.Techniques[0].Passes["CopyEntityHistory"].ApplyCompute();
                App.graphicsDevice.DispatchCompute((entityInstances.Count + 63) / 64, 1, 1);
            }

            // Apply changed chunks
            for (int i = 0; i < entityChunkInstancesInfo.Count; ++i)
            {
                int chunkIndex = (int)entityChunkInstancesInfo[i];
                chunkChanges.Set(chunkIndex, true);
            }
            for (int i = 0; i < entityChunkInstancesInfoOld.Count; ++i)
            {
                int chunkIndex = (int)entityChunkInstancesInfoOld[i];
                bool curState = chunkChanges.Get(chunkIndex);
                if (!curState)
                    worldData.changeHandler.AddChangedChunk(chunkIndex);
                else
                    chunkChanges.Set(chunkIndex, false);
            }
            for (int i = 0; i < entityChunkInstancesInfo.Count; ++i)
            {
                int chunkIndex = (int)entityChunkInstancesInfo[i];
                bool curState = chunkChanges.Get(chunkIndex);
                if (curState)
                {
                    worldData.changeHandler.AddChangedChunk(chunkIndex);
                    chunkChanges.Set(chunkIndex, false);
                }
            }

            bytesCpuGpuCopy = updateCount * 8 + entityChunkInstanceCount * 5 * 4 + entityInstances.Count * 16;

            (entityChunkInstancesInfo, entityChunkInstancesInfoOld) = (entityChunkInstancesInfoOld, entityChunkInstancesInfo);
        }

        public void addEntity(EntityData entity)
        {
            int entityVoxelCount = entity.size.X * entity.size.Y * entity.size.Z;
            int entityVoxelCountForBuffer = (entityVoxelCount + 63) / 64;
            entities.Add(entity);
            entityVoxelDataGpu.SetData(entityVoxelDataPointer * 64 * 4, entity.voxels, 0, entityVoxelCount, 4);
            entity.voxelStartIndexGpu = entityVoxelDataPointer;
            entityVoxelDataPointer += entityVoxelCountForBuffer;
            //App.worldHandler.voxelTypeHandler.MapTypesWithState(entity.model.types, entityVoxelDataGpu, entity.voxelStartIndexGpu * 64, entityVoxelCount);
        }

        public int addEntityInstance(int entityID, Vector3 pos)
        {
            EntityData curEntity = entities[entityID];
            EntityInstance entityInstance = new EntityInstance();
            entityInstance.position = pos;
            entityInstance.quaternion = Quaternion.Identity.ToVector4();
            entityInstance.voxelStart = (uint)curEntity.voxelStartIndexGpu;
            entityInstance.entity = (uint)entityInstances.Count;
            entityInstance.size = curEntity.size;

            entityInstances.Add(entityInstance);
            return entityInstances.Count - 1;
        }

        //public int removeEntityInstance(int entityInstanceID)
        //{
        //    EntityData curEntity = entities[entityID];
        //    EntityInstance entityInstance = new EntityInstance();
        //    entityInstance.position = pos;
        //    entityInstance.quaternion = Quaternion.Identity.ToVector4();
        //    entityInstance.voxelStart = (uint)curEntity.voxelStartIndexGpu;
        //    entityInstance.entity = (uint)entityInstances.Count;
        //    entityInstance.size = curEntity.size;

        //    entityInstances.Add(entityInstance);
        //    return entityInstances.Count - 1;
        //}

        public void updateEntityPos(int entityInstanceID, Vector3 pos)
        {
            EntityInstance instance = entityInstances[entityInstanceID];
            instance.position = pos;
            entityInstances[entityInstanceID] = instance;
        }

        public void updateEntityRotation(int entityInstanceID, Quaternion quaternion)
        {
            EntityInstance instance = entityInstances[entityInstanceID];
            quaternion.Normalize();
            instance.quaternion = quaternion.ToVector4();
            entityInstances[entityInstanceID] = instance;
        }

        private Uint2 compressQuaternion(Vector4 q)
        {
            int maxIndex = 0;
            float maxAbs = Math.Abs(q.X);
            bool isNeg = q.X < 0;
            if (Math.Abs(q.Y) > maxAbs)
            {
                maxAbs = Math.Abs(q.Y);
                maxIndex = 1;
                isNeg = q.Y < 0;
            }
            if (Math.Abs(q.Z) > maxAbs)
            {
                maxAbs = Math.Abs(q.Z);
                maxIndex = 2;
                isNeg = q.Z < 0;
            }
            if (Math.Abs(q.W) > maxAbs)
            {
                maxAbs = Math.Abs(q.W);
                maxIndex = 3;
                isNeg = q.W < 0;
            }

            // Store the smallest three
            Vector3 small;
            if (maxIndex == 0)
                small = new Vector3(q.Y, q.Z, q.W);
            else if (maxIndex == 1)
                small = new Vector3(q.X, q.Z, q.W);
            else if (maxIndex == 2)
                small = new Vector3(q.X, q.Y, q.W);
            else
                small = new Vector3(q.X, q.Y, q.Z);

            if (isNeg)
                small = -small;
            
            Point3 smallInt = Point3.FromVector3((small + new Vector3(1.0f)) * 8192 + new Vector3(0.5f));
            smallInt.X = MathHelper.Clamp(smallInt.X, 0, 16383);
            smallInt.Y = MathHelper.Clamp(smallInt.Y, 0, 16383);
            smallInt.Z = MathHelper.Clamp(smallInt.Z, 0, 16383);

            Uint2 res = new();
            res.data1 = (uint)smallInt.X | ((uint)smallInt.Y << 14) | (((uint)smallInt.Z & 0xF) << 28);
            res.data2 = (uint)(smallInt.Z >> 4) | (uint)((maxIndex & 3) << 10);
            return res;
        }

        public void Dispose()
        {
            worldData = null;
            entityChunkInstancesDynamic?.Dispose();
            entityInstancesHistoryDynamic?.Dispose();
            chunkUpdateDynamic?.Dispose();
            entityChunkInstancesGpu?.Dispose();
            entityInstancesHistoryGpu?.Dispose();
            entityVoxelDataGpu?.Dispose();
        }
    }
}
