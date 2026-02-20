#include "common/common.fxh"
#include "common/commonEntities.fxh"

#ifndef __RAY_TRACING__
#define __RAY_TRACING__

#define MAX_RAY_STEPS_PRIMARY 120
#define MAX_RAY_STEPS_SECONDARY 100
#define MAX_RAY_STEPS_SUN 120
#define MAX_RAY_STEPS_SUN_SECONDARY 80
#define MAX_RAY_STEPS_VISIBILITY 60

#define BLOCK_STATE_CHILD 2
#define BLOCK_STATE_UNIFORM_EMPTY 0
#define BLOCK_STATE_UNIFORM_FULL 1

struct RayResult
{
    uint type;
    float length;
    float3 normal;
    int3 voxelPos;
    int stepCount;
    uint normalComp;
    int entity;
};

int groupSizeX, groupSizeY, groupSizeZ;
float3 boundingBoxMin, boundingBoxMax;

#ifndef __VARIABLES_VOXEL_TYPE_DATA__
#define __VARIABLES_VOXEL_TYPE_DATA__
StructuredBuffer<uint4> voxelTypeData;
#endif // __VARIABLES_VOXEL_TYPE_DATA__

StructuredBuffer<uint> voxels;
StructuredBuffer<uint> blocks;
Texture3D<CHUNKTYPE> chunks;

#ifdef ENTITIES
StructuredBuffer<EntityChunkInstance> entityChunkInstances;
StructuredBuffer<uint> entityVoxelData;
#endif // ENTITIS


#ifndef __VARIABLES_ENTITY_INSTANCE_HISTORY__
#define __VARIABLES_ENTITY_INSTANCE_HISTORY__
StructuredBuffer<uint4> entityInstancesHistory;
#endif // __VARIABLES_ENTITY_INSTANCE_HISTORY__

bool rayAABB(float3 rayOrigin, float3 rayDir, float3 recMin, float3 recMax, out float2 distMinMax, out float3 normalMask)
{
    float3 rayDirFrac = rcp(rayDir);

    float3 recMinDist = (recMin - rayOrigin) * rayDirFrac;
    float3 recMaxDist = (recMax - rayOrigin) * rayDirFrac;

    float3 t1 = min(recMinDist, recMaxDist);
    float3 t2 = max(recMinDist, recMaxDist);
    float tNear = max(max(t1.x, t1.y), t1.z);
    float tFar = min(min(t2.x, t2.y), t2.z);
    normalMask = step(tNear, t1);
    
    tNear = max(0, tNear);
    distMinMax = float2(tNear, tFar);

    if (tFar < 0.0 || tNear > tFar)
        return false;

    return true;
}

bool shootRay(int3 rayOriginInt, float3 rayOriginFrac, float3 rayDir, int maxStepCount, out RayResult rayResult)
{
    float3 invRayDirAbs = abs(1 / (0.000000001f + rayDir));
    
    uint3 isNegative = step(rayDir, 0);
    uint3 shiftMaskVoxelAndBlocks = uint3(isNegative.x ? 0 : 2, isNegative.y ? 4 : 6, isNegative.z ? 8 : 10);
    uint3 shiftMaskChunk = uint3(isNegative.x ? 0 : 5, isNegative.y ? 10 : 15, isNegative.z ? 20 : 25);
    
    #ifdef ENTITIES
    static uint chunksWithEntities[16];
    uint chunksWithEntitiesCount = 0;
    #endif // ENTITIS
    
    float3 startPos = rayOriginFrac;
    float curDist = 0;
    float3 mask = float3(0, 0, 0);
    rayResult.length = -1;
    rayResult.entity = 0x3FFF;
    rayResult.normalComp = 0x1FFFF;
    int stepCount = 0;
    while (stepCount < maxStepCount)
    {
        float3 curPos = mad(rayDir, curDist, startPos);
        uint3 curCell = (uint3) ((int3) floor(mad(mask, sign(rayDir) * 0.5f, curPos)) + rayOriginInt);
            
        if (any((float3) curCell >= boundingBoxMax))
            break;

        // Get data from chunk
        uint3 chunkPos = curCell / 16;
        uint3 voxelPosInChunk = curCell % 16;
        
        CHUNKTYPE curNode = chunks[chunkPos];
        #ifdef ENTITIES
        if (curNode.y != 0 && chunksWithEntitiesCount < 16)
        {
            if (chunksWithEntitiesCount == 0 || chunksWithEntities[chunksWithEntitiesCount - 1] != curNode.y)
                chunksWithEntities[chunksWithEntitiesCount++] = curNode.y;
        }
        #endif // ENTITIS
        
        uint3 boundsInDir = 1;
        if (curNode.x >> 31)
        {
            uint3 blockPosInChunk = voxelPosInChunk / 4;
            uint blockIndex = (curNode.x & 0x3FFFFFFF) + FLATTEN_INDEX(blockPosInChunk, 4, 16);
            curNode.x = blocks[blockIndex];
            uint3 voxelPosInBlock = curCell % 4;
        
            bool blockIsParent = curNode.x >> 31;
            if (blockIsParent)
            {
                uint voxelIndexInBlock = FLATTEN_INDEX(voxelPosInBlock, 4, 16);
                uint voxelStartIndex = (curNode.x & 0x3FFFFFFF) + voxelIndexInBlock / 2;
                uint curVoxelPair = voxels[voxelStartIndex];
                curNode.x = ((curVoxelPair >> (16 * (voxelIndexInBlock & 0x1))) & 0xFFFF);
                if (curNode.x >> 15)
                    curNode.x |= (BLOCK_STATE_UNIFORM_FULL << 30);
            }
            boundsInDir = uint3((curNode.x >> shiftMaskVoxelAndBlocks.x) & 0x3, (curNode.x >> shiftMaskVoxelAndBlocks.y) & 0x3, (curNode.x >> shiftMaskVoxelAndBlocks.z) & 0x3);
            if (!blockIsParent)
                boundsInDir = boundsInDir * 4u + (isNegative ? voxelPosInBlock : 3u - voxelPosInBlock);

        }
        else
            boundsInDir = (isNegative ? voxelPosInChunk : 15u - voxelPosInChunk) + 16u * uint3((curNode.x >> shiftMaskChunk.x) & 0x1F, (curNode.x >> shiftMaskChunk.y) & 0x1F, (curNode.x >> shiftMaskChunk.z) & 0x1F);
            
        if (curNode.x & 0x40000000)
        {
            rayResult.type = curNode.x & 0x7FFF;
            rayResult.length = curDist;
            rayResult.voxelPos = curCell;
            break;
        }
        float3 distForIntersect = (1 + boundsInDir - (1 - mask) * abs(isNegative - frac(curPos))) * invRayDirAbs;
        float minDist = min(distForIntersect.x, min(distForIntersect.y, distForIntersect.z));
        mask = step(distForIntersect, minDist);
        curDist += max(minDist, 0.0001f);
        stepCount++;
    }
    
    #ifdef ENTITIES
    uint curEntityBlockIndex = 0;
    uint curEntityBlockEntityIndex = 0;
    bool isHitEntity = false;
    while (curEntityBlockIndex < chunksWithEntitiesCount)
    {
        uint curEntityBlockData = chunksWithEntities[curEntityBlockIndex];
        
        uint entityBlockDataIndex = (curEntityBlockData >> 8) + curEntityBlockEntityIndex;
        EntityChunkInstance entityInstanceComp = entityChunkInstances[entityBlockDataIndex];
        EntityInstance entityInstance = decompressEntityInstanceFromChunk(entityInstanceComp);
        float3 rayOriginEntity = rayOriginFrac - (entityInstance.position - rayOriginInt);
        rayOriginEntity = applyRotation(rayOriginEntity, entityInstance.quaternion);
        float3 rayDirEntity = applyRotation(rayDir, entityInstance.quaternion);
        
        
        
        float2 entityHitDist;
        float3 maskAABB;
        if (rayAABB(rayOriginEntity, rayDirEntity, float3(0, 0, 0), entityInstance.size, entityHitDist, maskAABB))
        {
            int stepCountEntity = 0;
            curDist = entityHitDist.x;
            
            float3 invRayDirAbsEntity = abs(1.0f / (0.000000001f + rayDirEntity));
            int3 isPositive = step(0, rayDirEntity);

            uint3 shiftMaskVoxel = uint3(isPositive.x ? 5 : 0, isPositive.y ? 15 : 10, isPositive.z ? 25 : 20);
            RayResult newRayResult;
            newRayResult.length = -1;
            
            
            float3 voxelPosStart = rayOriginEntity + rayDirEntity * entityHitDist.x;
            int3 mask = maskAABB;
            int3 voxelPos = (int3) ((entityHitDist.x > 0 ? mask * sign(rayDirEntity) * 0.5f : 0) + voxelPosStart);
            while (stepCountEntity < 20)
            {
                uint voxelIndex = voxelPos.x + voxelPos.y * entityInstance.size.x + voxelPos.z * entityInstance.size.x * entityInstance.size.y;
                uint curVoxel = entityVoxelData[entityInstance.voxelStart * 64 + voxelIndex];
                if (curVoxel & 0x80000000)
                {
                    newRayResult.type = curVoxel & 0x7FFFFFFF;
                    newRayResult.length = curDist;
                    newRayResult.normal = mask * -sign(rayDirEntity);
                    float normalDot = dot(newRayResult.normal, int3(1, 3, 5));
                    newRayResult.normalComp = (abs(normalDot) - (normalDot > 0 ? 0 : 1)) + 1 + (abs(dot(voxelPos, newRayResult.normal)) + max(0, dot(newRayResult.normal, float3(1, 1, 1)))) * 8;
                    newRayResult.voxelPos = voxelPos;
                    newRayResult.stepCount = stepCountEntity;
                    newRayResult.entity = entityInstance.entity;
                    break;
                }
                
                int3 boundsInDir = int3((curVoxel >> shiftMaskVoxel.x) & 0x1F, (curVoxel >> shiftMaskVoxel.y) & 0x1F, (curVoxel >> shiftMaskVoxel.z) & 0x1F);
                float3 distForRay = (abs((voxelPos + isPositive) - voxelPosStart) + boundsInDir) * invRayDirAbsEntity;
                float minDistForRay = min(distForRay.x, min(distForRay.y, distForRay.z));
                mask = step(distForRay, minDistForRay);
                voxelPos = (int3) floor(voxelPosStart + rayDirEntity * minDistForRay + mask * sign(rayDirEntity) * 0.5f);
                curDist = entityHitDist.x + minDistForRay;
                
                if (any(voxelPos < int3(0, 0, 0)) || any((uint3) voxelPos >= entityInstance.size))
                    break;
            
                stepCountEntity++;
                stepCount++;
            }
            
            if (newRayResult.length > 0 && (newRayResult.length < rayResult.length || rayResult.length < 0))
            {
                isHitEntity = true;
                rayResult = newRayResult;
                rayResult.normal = applyRotation(rayResult.normal, quaternionInverse(entityInstance.quaternion));

            }
        }
        
        
        
        curEntityBlockEntityIndex++;
        if (curEntityBlockEntityIndex >= (curEntityBlockData & 0xFF))
        {
            //if (isHitEntity) // TODO needs chunk check
            //    break;
            curEntityBlockIndex++;
            curEntityBlockEntityIndex = 0;
        }
    }
    #endif // ENTITIS
    
    rayResult.stepCount = stepCount;
    if (rayResult.length <= 0)
    {
        rayResult.length = 99999999;
        return stepCount == 0;
    }
    
#ifdef ENTITIES
    if (isHitEntity)
        return true;
#endif // ENTITIS
    
    rayResult.normal = mask * -sign(rayDir);
    float normalDot = dot(rayResult.normal, int3(1, 3, 5));
    rayResult.normalComp = (abs(normalDot) - (normalDot > 0 ? 0 : 1)) + 1 + (abs(dot(rayResult.voxelPos, rayResult.normal)) + max(0, dot(rayResult.normal, float3(1, 1, 1)))) * 8;
    return true;
}

// Simple Ray traversal without split position
bool shootRay(float3 rayOrigin, float3 rayDir, int maxStepCount, out RayResult rayResult)
{
    return shootRay(trunc(rayOrigin), frac(rayOrigin), rayDir, maxStepCount, rayResult);
}


#endif // __RAY_TRACING__