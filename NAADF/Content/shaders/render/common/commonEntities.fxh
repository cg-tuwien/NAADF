#ifndef __COMMON_ENTITIES__
#define __COMMON_ENTITIES__

#include "common.fxh"


struct EntityInstance
{
    float3 position;
    float4 quaternion;
    uint voxelStart;
    uint entity;
    uint3 size;
};

struct EntityChunkInstance
{
    uint4 data1;
    uint data2;
};


uint4 compressEntityInstanceToHistory(EntityInstance entityInstance)
{   
    uint3 posComp = entityInstance.position * 128;
    uint2 quaternionComp = compressQuaternion(entityInstance.quaternion);
    
    uint4 comp;
    comp.x = posComp.x | ((posComp.y & 0x7FF) << 21);
    comp.y = posComp.z | ((posComp.y >> 11) << 21);
    comp.z = quaternionComp.x;
    comp.w = quaternionComp.y;
    return comp;
}

EntityChunkInstance compressEntityInstanceToChunk(EntityInstance entityInstance)
{
    uint3 posComp = entityInstance.position * 128;
    uint2 quaternionComp = compressQuaternion(entityInstance.quaternion);
        
    EntityChunkInstance comp;
    comp.data1.x = posComp.x | ((posComp.y & 0x7FF) << 21);
    comp.data1.y = posComp.z | ((posComp.y >> 11) << 21) | ((entityInstance.size.z >> 4) << 29);
    comp.data1.z = quaternionComp.x;
    comp.data1.w = quaternionComp.y | (entityInstance.voxelStart << 12);
    comp.data2 = entityInstance.entity | (entityInstance.size.x << 14) | (entityInstance.size.y << 21) | ((entityInstance.size.z & 0xF) << 28);
    return comp;
}

EntityInstance decompressEntityInstanceFromHistory(uint4 comp)
{
    EntityInstance instance;
    instance.position = float3((comp.x & 0x1FFFFF) / 128.0f, (((comp.x >> 21) & 0x7FF) | (((comp.y >> 21) & 0xFF) << 11)) / 128.0f, (comp.y & 0x1FFFFF) / 128.0f);
    instance.quaternion = decompressQuaternion(comp.zw);
    instance.voxelStart = 0;
    instance.entity = 0;
    instance.size = uint3(0, 0, 0);
    return instance;
}

EntityInstance decompressEntityInstanceFromChunk(EntityChunkInstance comp)
{
    EntityInstance instance;
    instance.position = float3((comp.data1.x & 0x1FFFFF) / 128.0f, (((comp.data1.x >> 21) & 0x7FF) | (((comp.data1.y >> 21) & 0xFF) << 11)) / 128.0f, (comp.data1.y & 0x1FFFFF) / 128.0f);
    instance.quaternion = decompressQuaternion(comp.data1.zw);
    instance.voxelStart = comp.data1.w >> 12;
    instance.entity = comp.data2 & 0x3FFF;
    instance.size = uint3((comp.data2 >> 14) & 0x7F, (comp.data2 >> 21) & 0x7F, ((comp.data2 >> 28) & 0xF) | ((comp.data1.y >> 29) << 4));
    return instance;
}

#endif // __COMMON_ENTITIES__