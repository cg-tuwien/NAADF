#include "../../settings.fxh"
#include "boundsCommon.fxh"

#define BLOCK_STATE_CHILD 2
#define BLOCK_STATE_UNIFORM_EMPTY 0
#define BLOCK_STATE_UNIFORM_FULL 1

struct BoundQueueInfo
{
    uint start;
    uint size;
};


uint chunkSizeX, chunkSizeY, chunkSizeZ;
uint groupSizeX, groupSizeY, groupSizeZ;

uint boundGroupQueueMaxSize;
uint changedChunkCount;


RWTexture3D<CHUNKTYPE> chunks;
RWStructuredBuffer<uint> blocks;
RWStructuredBuffer<uint> voxels;

StructuredBuffer<uint2> changedGroupsDynamic;
StructuredBuffer<uint2> changedChunksDynamic;
StructuredBuffer<uint> changedBlocksDynamic;
StructuredBuffer<uint> changedVoxelsDynamic;

RWStructuredBuffer<BoundQueueInfo> boundQueueInfo;
RWStructuredBuffer<uint> boundGroupQueues;
RWStructuredBuffer<uint3> boundGroupMasks;

groupshared uint lowestBoundsShared[3] = { 31, 31, 31 };

[numthreads(4, 4, 4)]
void applyGroupChange(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint2 change = changedGroupsDynamic[groupID.x];
    uint3 groupPosition = uint3(change.x & 0x7FF, (change.x >> 11) & 0x3FF, change.x >> 21);
    uint groupIndex = groupPosition.x + groupPosition.y * groupSizeX + groupPosition.z * groupSizeX * groupSizeY;
    uint3 chunkPos = groupPosition * 4 + localID;
    CHUNKTYPE curChunk = chunks[chunkPos];
    uint chunkState = curChunk.x >> 30;
    bool isResetCompletely = change.y >> 30;
    
    uint lowestX = isResetCompletely ? 0 : 31;
    uint lowestY = isResetCompletely ? 0 : 31;
    uint lowestZ = isResetCompletely ? 0 : 31;
    
    if (chunkState == BLOCK_STATE_UNIFORM_EMPTY)
    {
        uint newChunk = chunkState;

        uint changeBoundXM = (change.y & 0x1F) + localID.x;
        uint changeBoundXP = ((change.y >> 5) & 0x1F) + (3 - localID.x);
        uint changeBoundYM = ((change.y >> 10) & 0x1F) + localID.y;
        uint changeBoundYP = ((change.y >> 15) & 0x1F) + (3 - localID.y);
        uint changeBoundZM = ((change.y >> 20) & 0x1F) + localID.z;
        uint changeBoundZP = ((change.y >> 25) & 0x1F) + (3 - localID.z);
        uint changeAll = min(min(changeBoundXM, changeBoundXP), min(min(changeBoundYM, changeBoundYP), min(changeBoundZM, changeBoundZP)));
        
        uint newBoundXM = min(curChunk.x & 0x1F, changeAll);
        uint newBoundXP = min((curChunk.x >> 5) & 0x1F, changeAll);
        uint newBoundYM = min((curChunk.x >> 10) & 0x1F, changeAll);
        uint newBoundYP = min((curChunk.x >> 15) & 0x1F, changeAll);
        uint newBoundZM = min((curChunk.x >> 20) & 0x1F, changeAll);
        uint newBoundZP = min((curChunk.x >> 25) & 0x1F, changeAll);
        if (!isResetCompletely)
            newChunk |= newBoundXM | (newBoundXP << 5) | (newBoundYM << 10) | (newBoundYP << 15) | (newBoundZM << 20) | (newBoundZP << 25);
        
        lowestX = min(lowestX, min(newBoundXM, newBoundXP));
        lowestY = min(lowestY, min(newBoundYM, newBoundYP));
        lowestZ = min(lowestZ, min(newBoundZM, newBoundZP));
        
#ifdef ENTITIES
        chunks[chunkPos] = uint2(newChunk, curChunk.y);
#else
        chunks[chunkPos] = newChunk;
#endif // ENTITIES
    }
    
    GroupMemoryBarrierWithGroupSync();
    
    InterlockedMin(lowestBoundsShared[0], lowestX);
    InterlockedMin(lowestBoundsShared[1], lowestY);
    InterlockedMin(lowestBoundsShared[2], lowestZ);
    
    GroupMemoryBarrierWithGroupSync();
    // Add chunk group to queue for next bound size
    if (localIndex < 3)
    {
        // Check if chunk group is already in next queue
        uint xyz = localIndex;
        uint nextBoundSize = lowestBoundsShared[localIndex];
        if (isResetCompletely)
            nextBoundSize = 0;
        bool isAlreadyInQueue = (boundGroupMasks[groupIndex][xyz] >> (nextBoundSize)) & 0x1;
        if (!isAlreadyInQueue && nextBoundSize < 31)
        {
            // Increase next Queue by 1
            boundGroupMasks[groupIndex][xyz] |= 1 << nextBoundSize;
            uint originalQueueSize;
            InterlockedAdd(boundQueueInfo[nextBoundSize * 3 + xyz].size, 1, originalQueueSize);
            
            // Store group into queue
            uint queueStartIndex = boundQueueInfo[nextBoundSize * 3 + xyz].start;
            uint queueIndex = (nextBoundSize * 3 + xyz) * boundGroupQueueMaxSize + ((queueStartIndex + originalQueueSize) % boundGroupQueueMaxSize);
            boundGroupQueues[queueIndex] = change.x;
        }
    }
}

[numthreads(64, 1, 1)]
void applyChunkChange(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    if (globalID.x >= changedChunkCount)
        return;
    
    uint2 change = changedChunksDynamic[globalID.x];
    uint3 chunkPos = uint3(change.x & 0x7FF, (change.x >> 11) & 0x3FF, change.x >> 21);
#ifdef ENTITIES
    chunks[chunkPos] = uint2(change.y, chunks[chunkPos].y);
#else
    chunks[chunkPos] = change.y;
#endif // ENTITIES
}

[numthreads(4, 4, 4)]
void applyBlockChange(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint changePointer = changedBlocksDynamic[groupID.x * (64 + 1)];
    
    uint curBlock = changedBlocksDynamic[groupID.x * (64 + 1) + 1 + localIndex];
    cachedCell[localIndex] = curBlock;
    
    uint3 blockPosInChunk = int3(localIndex % 4, (localIndex / 4) % 4, (localIndex / 16) % 4);
    ComputeBounds4(localIndex, blockPosInChunk, 30, 0x3, curBlock);
    
    if ((curBlock >> 30) != 0)
        cachedCell[localIndex] = curBlock;
    
    GroupMemoryBarrierWithGroupSync();
    
    blocks[changePointer + localIndex] = cachedCell[localIndex];
}

[numthreads(4, 4, 4)]
void applyVoxelChange(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint changePointer = changedVoxelsDynamic[groupID.x * (32 + 1)];
    
    uint curVoxelPair = changedVoxelsDynamic[groupID.x * (32 + 1) + 1 + localIndex / 2];
    uint curVoxel = localIndex % 2 == 0 ? (curVoxelPair & 0xFFFF) : (curVoxelPair >> 16);
    cachedCell[localIndex] = curVoxel;
    
    uint3 voxelPosInBlock = int3(localIndex % 4, (localIndex / 4) % 4, (localIndex / 16) % 4);
    ComputeBounds4(localIndex, voxelPosInBlock, 15, 0x1, curVoxel);
    
    if (curVoxel >> 15)
        cachedCell[localIndex] = curVoxel;
    
    GroupMemoryBarrierWithGroupSync();
    
    if (localIndex < 32)
        voxels[changePointer + localIndex] = cachedCell[localIndex * 2] | (cachedCell[localIndex * 2 + 1] << 16);
}

technique Tech0
{
    pass ApplyGroupChange
    {
        ComputeShader = compile cs_5_0 applyGroupChange();
    }

    pass ApplyChunkChange
    {
        ComputeShader = compile cs_5_0 applyChunkChange();
    }

    pass ApplyBlockChange
    {
        ComputeShader = compile cs_5_0 applyBlockChange();
    }

    pass ApplyVoxelChange
    {
        ComputeShader = compile cs_5_0 applyVoxelChange();
    }
}