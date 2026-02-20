#include "../../settings.fxh"
#include "boundsCommon.fxh"

#define BoundsIdX 0
#define BoundsIdY 1
#define BoundsIdZ 2
#define BLOCK_STATE_CHILD 2
#define BLOCK_STATE_UNIFORM_EMPTY 0
#define BLOCK_STATE_UNIFORM_FULL 1

#define BOUND_INFO_GROUPS 0

struct BoundQueueInfo
{
    uint start;
    uint size;
};

RWTexture3D<CHUNKTYPE> chunks;

uint chunkSizeX, chunkSizeY, chunkSizeZ;
uint groupSizeX, groupSizeY, groupSizeZ;

RWStructuredBuffer<BoundQueueInfo> boundQueueInfo;
RWStructuredBuffer<uint> boundGroupQueues;
RWStructuredBuffer<uint> boundRefinedInfo;

RWStructuredBuffer<uint3> boundGroupMasks;

RWByteAddressBuffer boundGroupQueueDispatchCount;

uint boundsInitOffset, maxGroupBoundDispatch, boundGroupQueueMaxSize;

groupshared bool anyBoundsIncrease = false;
uint frameIndex;


[numthreads(64, 1, 1)]
void addInitialGroupsToBoundQueue(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint groupIndex = boundsInitOffset + globalID.x;
    uint3 groupPos = int3(groupIndex % groupSizeX, (groupIndex / groupSizeX) % groupSizeY, groupIndex / (groupSizeX * groupSizeY));
    
    boundGroupMasks[groupIndex] = uint3(1, 1, 1);
    boundGroupQueues[boundGroupQueueMaxSize * 0 + groupIndex] = groupPos.x | (groupPos.y << 11) | (groupPos.z << 21); // X
    boundGroupQueues[boundGroupQueueMaxSize * 1 + groupIndex] = groupPos.x | (groupPos.y << 11) | (groupPos.z << 21); // Y
    boundGroupQueues[boundGroupQueueMaxSize * 2 + groupIndex] = groupPos.x | (groupPos.y << 11) | (groupPos.z << 21); // Z
}


[numthreads(1, 1, 1)]
void prepareGroupBounds(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    // Find valid queue info
    BoundQueueInfo queueInfo;
    bool hasFoundBoundsToProcess = false;
    uint foundBoundSize, foundXYZ;
    for (uint i = 0; i < 32 && !hasFoundBoundsToProcess; ++i)
    {
        uint iMod = i % 32;
        for (uint xyz = 0; xyz < 3; ++xyz)
        {
            queueInfo = boundQueueInfo[BOUND_INFO_GROUPS + iMod * 3 + xyz];
            if (queueInfo.size > 0)
            {
                hasFoundBoundsToProcess = true;
                foundBoundSize = iMod;
                foundXYZ = xyz;
                break;
            }
        }
    }
    
    uint groupAmountToProcess = 0;
    if (hasFoundBoundsToProcess)
    {
        // Process group queue info
        groupAmountToProcess = min(maxGroupBoundDispatch, queueInfo.size);
        boundRefinedInfo[0] = queueInfo.start % boundGroupQueueMaxSize;
        boundRefinedInfo[1] = groupAmountToProcess;
        boundRefinedInfo[2] = foundBoundSize | (foundXYZ << 16);
        
        // Update info for next frame
        BoundQueueInfo newQueueInfo;
        newQueueInfo.start = (queueInfo.start + groupAmountToProcess) % boundGroupQueueMaxSize;
        newQueueInfo.size = queueInfo.size - groupAmountToProcess;
        boundQueueInfo[BOUND_INFO_GROUPS + foundBoundSize * 3 + foundXYZ] = newQueueInfo;
    }
    else
        boundRefinedInfo[1] = 0;
    
    boundGroupQueueDispatchCount.Store(0, max(1, groupAmountToProcess));
}

void addBoundsGroup(int3 chunkPos, int3 directionOffset, uint mask, uint boundsLocation, uint curBound, inout uint curChunk)
{
    int3 neighbourChunkPos = chunkPos + directionOffset;
    if (any(neighbourChunkPos < 0) || neighbourChunkPos.x >= (int) chunkSizeX || neighbourChunkPos.y >= (int) chunkSizeY || neighbourChunkPos.z >= (int) chunkSizeZ)
    {
        if (((curChunk >> boundsLocation) & 0x1F) == curBound)
            curChunk += 1 << boundsLocation;
        return;
    }
    
#ifdef ENTITIES
    uint2 neighbour = chunks[neighbourChunkPos];
#else
    uint2 neighbour = uint2(chunks[neighbourChunkPos], 0);
#endif // ENTITIES
    
    if ((neighbour.x >> 30) == BLOCK_STATE_UNIFORM_EMPTY && neighbour.y == 0 && ((curChunk >> boundsLocation) & 0x1F) == curBound)
    {
        if ((checkMatchingBounds(neighbour.x, curChunk, 0, 5, 0x1F) & mask) == mask)
            curChunk += 1 << boundsLocation;
    }
}

[numthreads(4, 4, 4)]
void computeGroupBounds(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    // Read queue info
    uint start = boundRefinedInfo[0];
    uint count = boundRefinedInfo[1];
    uint boundInfo = boundRefinedInfo[2];
    uint boundSize = boundInfo & 0xFFFF;
    uint boundXYZ = boundInfo >> 16;
    
    bool isGroupActive = groupID.x < count;
    
    // Get chunk group
    uint groupPositionComp = boundGroupQueues[(boundSize * 3 + boundXYZ) * boundGroupQueueMaxSize + ((start + groupID.x) % boundGroupQueueMaxSize)];
    uint3 groupPosition = uint3(groupPositionComp & 0x7FF, (groupPositionComp >> 11) & 0x3FF, groupPositionComp >> 21);
    uint groupIndex = groupPosition.x + groupPosition.y * groupSizeX + groupPosition.z * groupSizeX * groupSizeY;
    if (isGroupActive && localIndex == 0)
        boundGroupMasks[groupIndex][boundXYZ] &= ~(1 << boundSize);
    
    // Get chunks in group
    uint3 chunkPos = groupPosition * 4 + localID;
    
#ifdef ENTITIES
    uint2 curChunk = chunks[chunkPos];
#else
    uint2 curChunk = uint2(chunks[chunkPos], 0);
#endif // ENTITIES
    
    uint curChunkCopy = curChunk.x;
    uint chunkState = curChunk.x >> 30;
    
    // Compute next bounds for current chunk
    if (chunkState == BLOCK_STATE_UNIFORM_EMPTY && curChunk.y == 0)
    {
        uint maskMinus = boundXYZ == 0 ? MASK_MX : (boundXYZ == 1 ? MASK_MY : MASK_MZ);
        uint maskPlus = boundXYZ == 0 ? MASK_PX : (boundXYZ == 1 ? MASK_PY : MASK_PZ);
        int3 dirOffsetAbs = int3(boundXYZ == 0 ? 1 : 0, boundXYZ == 1 ? 1 : 0, boundXYZ == 2 ? 1 : 0);
        
        addBoundsGroup(chunkPos, dirOffsetAbs * (-1), maskMinus, boundXYZ * 10 + 0, boundSize, curChunk.x);
        addBoundsGroup(chunkPos, dirOffsetAbs * (+1), maskPlus, boundXYZ * 10 + 5, boundSize, curChunk.x);
    }
    
    GroupMemoryBarrierWithGroupSync();
    
    if (isGroupActive && curChunkCopy != curChunk.x)
    {
#ifdef ENTITIES
        chunks[chunkPos] = curChunk;
#else
        chunks[chunkPos] = curChunk.x;
#endif // ENTITIES
        anyBoundsIncrease = true;
    }
    
    GroupMemoryBarrierWithGroupSync();
    
    // Add chunk group to queue for next bound size
    if (localIndex == 0 && boundSize < 30)
    {
        // Check if chunk group is already in next queue
        uint nextBoundSize = boundSize + 1;
        bool isAlreadyInQueue = (boundGroupMasks[groupIndex][boundXYZ] >> (nextBoundSize)) & 0x1;
        if (!isAlreadyInQueue)
        {
            // Increase next Queue by 1
            boundGroupMasks[groupIndex][boundXYZ] |= 1 << nextBoundSize;
            uint originalQueueSize;
            InterlockedAdd(boundQueueInfo[nextBoundSize * 3 + boundXYZ].size, 1, originalQueueSize);
            
            // Store group into queue
            uint queueStartIndex = boundQueueInfo[nextBoundSize * 3 + boundXYZ].start;
            uint queueIndex = (nextBoundSize * 3 + boundXYZ) * boundGroupQueueMaxSize + ((queueStartIndex + originalQueueSize) % boundGroupQueueMaxSize);
            boundGroupQueues[queueIndex] = groupPositionComp;
        }
    }
}

technique Tech0
{
    pass AddInitialGroupsToBoundQueue
    {
        ComputeShader = compile cs_5_0 addInitialGroupsToBoundQueue();
    }
    pass PrepareGroupBounds
    {
        ComputeShader = compile cs_5_0 prepareGroupBounds();
    }
    pass ComputeGroupBounds
    {
        ComputeShader = compile cs_5_0 computeGroupBounds();
    }
}