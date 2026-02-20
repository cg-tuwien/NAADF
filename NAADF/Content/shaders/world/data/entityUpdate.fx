#include "../../render/common/common.fxh"

StructuredBuffer<uint2> chunkUpdatesDynamic;
StructuredBuffer<EntityChunkInstance> entityChunkInstancesDynamic;
StructuredBuffer<uint4> entityHistoryDynamic;

RWTexture3D<uint2> chunks;
RWStructuredBuffer<EntityChunkInstance> entityChunkInstances;
RWStructuredBuffer<uint4> entityInstancesHistory;


uint entityInstanceCount, entityChunkInstanceCount, taaIndex, updateCount;


[numthreads(64, 1, 1)]
void updateChunks(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    if (globalID.x >= updateCount)
        return;
    
    uint2 update = chunkUpdatesDynamic[globalID.x];
    uint3 chunkPos = uint3(update.x & 0x7FF, (update.x >> 11) & 0x3FF, update.x >> 21);
    chunks[chunkPos] = uint2(chunks[chunkPos].x, update.y);
}

[numthreads(64, 1, 1)]
void copyEntityChunkInstances(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    if (globalID.x >= entityChunkInstanceCount)
        return;
    
    entityChunkInstances[globalID.x] = entityChunkInstancesDynamic[globalID.x];
}

[numthreads(64, 1, 1)]
void copyEntityHistory(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    if (globalID.x >= entityInstanceCount)
        return;
    
    entityInstancesHistory[taaIndex * 16384 + globalID.x] = entityHistoryDynamic[globalID.x];
}

technique Tech0
{
    pass UpdateChunks
    {
        ComputeShader = compile cs_5_0 updateChunks();
    }

    pass CopyEntityChunkInstances
    {
        ComputeShader = compile cs_5_0 copyEntityChunkInstances();
    }

    pass CopyEntityHistory
    {
        ComputeShader = compile cs_5_0 copyEntityHistory();
    }
}