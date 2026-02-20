#include "../../settings.fxh"
#include "boundsCommon.fxh"

#define EMPTY_BLOCK 0x0
#define BLOCK_STATE_CHILD 2
#define BLOCK_STATE_UNIFORM_EMPTY 0
#define BLOCK_STATE_UNIFORM_FULL 1

#define BOUND_INFO_GROUPS 0
#define BOUND_INFO_BLOCKS 32 * 3
#define BOUND_INFO_CHUNKS 32 * 3 + 1



struct HashValue
{
    uint voxelPointer;
    uint useCount;
    uint hashRaw;
};

struct BoundQueueInfo
{
    int start;
    int size;
};

RWStructuredBuffer<BoundQueueInfo> boundQueueInfo;
RWStructuredBuffer<uint> boundBlockQueues;
RWStructuredBuffer<uint> boundChunkQueues;


RWTexture3D<CHUNKTYPE> chunks;
RWStructuredBuffer<uint> blocks;
RWStructuredBuffer<uint> voxels;

RWStructuredBuffer<uint> blockVoxelCount;
StructuredBuffer<uint> segmentVoxelBuffer;
RWStructuredBuffer<HashValue> hashMap;
uint hashCoefficients[65];

uint boundBlockQueueMaxSize;
uint boundChunkQueueMaxSize;
uint hashMapSize, segmentSizeInChunks;
uint chunkOffsetX, chunkOffsetY, chunkOffsetZ;
uint sizeInChunksX, sizeInChunksY, sizeInChunksZ;
uint queueStart;

groupshared uint referenceBlock;
groupshared bool isAllBlocksEqual;
groupshared uint insertBlockIndex = 0;

RWStructuredBuffer<uint> gpuCpuSyncBuffer;

uint copyOffset, copyMaxCount;

uint GetVoxelPointer(uint hash, uint voxelRawStart)
{
    uint hashBounds = hash & (hashMapSize - 1);
    uint count = 0;
    [allow_uav_condition][loop]
    while (count < 250)
    {
        uint voxelPointer = 0;
        uint originalPointer;
        
        InterlockedCompareExchange(hashMap[hashBounds].voxelPointer, EMPTY_BLOCK, 0x80000000 | voxelRawStart, originalPointer);
        
        if (originalPointer == EMPTY_BLOCK) // Found empty hash slot
        {
            uint originalIndex;
            InterlockedAdd(hashMap[hashBounds].useCount, 1);
            InterlockedAdd(blockVoxelCount[0], 64, originalIndex);
            originalIndex /= 2;
            for (int i = 0; i < 32; ++i)
            {
                voxels[originalIndex + i] = segmentVoxelBuffer[voxelRawStart + i];
            }
            hashMap[hashBounds].hashRaw = hash;
            uint test;
            InterlockedExchange(hashMap[hashBounds].voxelPointer, originalIndex, test);
            voxelPointer = originalIndex;
        }
        else // Fully written hash slot
        {
            uint voxelPointerCur = 0, c = 0;
                
            InterlockedOr(hashMap[hashBounds].voxelPointer, 0, voxelPointerCur);
            while ((voxelPointerCur & 0x80000000) != 0 && ++c < 2000) // Wait for voxel pointer to be written
            {
                InterlockedOr(hashMap[hashBounds].voxelPointer, 0, voxelPointerCur);
            }
            
            if (hashMap[hashBounds].hashRaw == hash)
            {
                bool isAllEqual = true;
                for (uint i = 0; i < 32; ++i)
                {
                    isAllEqual = isAllEqual && segmentVoxelBuffer[voxelRawStart + i] == voxels[voxelPointerCur + i];
                }
                if (isAllEqual)
                {
                    InterlockedAdd(hashMap[hashBounds].useCount, 1);
                    voxelPointer = voxelPointerCur;
                }
            }
        }
        if (voxelPointer > 0)
            return voxelPointer;
        hashBounds = (hashBounds + 1) & (hashMapSize - 1);
        count++;

    }
    return 2;
}

[numthreads(4, 4, 4)]
void calcBlockFromRawData(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint chunkIndexInSegment = groupID.x + groupID.y * segmentSizeInChunks + groupID.z * segmentSizeInChunks * segmentSizeInChunks;
    uint voxelIndexInSegment = chunkIndexInSegment * 2048 + localIndex * 32;
    
    uint3 chunkPos = groupID + uint3(chunkOffsetX, chunkOffsetY, chunkOffsetZ);
    
    // Calculate hash and check if all voxels are the same type
    uint hash = hashCoefficients[0];
    uint firstVoxelTypeComp = segmentVoxelBuffer[voxelIndexInSegment];
    uint firstVoxelType = firstVoxelTypeComp & 0x7FFF;
    bool isAllSame = (firstVoxelTypeComp & 0xFFFF) == (firstVoxelTypeComp >> 16);
    for (uint i = 0; i < 32; ++i)
    {
        uint voxelComp = segmentVoxelBuffer[voxelIndexInSegment + i];
        hash += hashCoefficients[i * 2 + 1] * (voxelComp & 0x7FFF);
        hash += hashCoefficients[i * 2 + 2] * ((voxelComp >> 16) & 0x7FFF);
        isAllSame = isAllSame && (firstVoxelTypeComp == voxelComp);
    }
    
    uint block;
    if (isAllSame)
        block = firstVoxelType | (firstVoxelType == 0 ? BLOCK_STATE_UNIFORM_EMPTY : BLOCK_STATE_UNIFORM_FULL) << 30;
    else
        block = GetVoxelPointer(hash, voxelIndexInSegment) | BLOCK_STATE_CHILD << 30;
    
    GroupMemoryBarrierWithGroupSync();
    
    if (localIndex == 0)
    {
        referenceBlock = block;     // Thread 0 stores a reference value
        isAllBlocksEqual = true;    // Assume all are equal initially
    }
    
    GroupMemoryBarrierWithGroupSync();

    if (block != referenceBlock || !isAllSame)
        isAllBlocksEqual = false;
    
    GroupMemoryBarrierWithGroupSync();
    
    if (localIndex == 0)
    {
        uint state = 0;
        if (isAllBlocksEqual)
            state = firstVoxelType | (firstVoxelType == 0 ? BLOCK_STATE_UNIFORM_EMPTY : BLOCK_STATE_UNIFORM_FULL) << 30;
        else
        {
            InterlockedAdd(blockVoxelCount[1], 64, insertBlockIndex);
            state = insertBlockIndex | BLOCK_STATE_CHILD << 30;
        }
        
#ifdef ENTITIES
        chunks[chunkPos] = uint2(state, 0);
#else
        chunks[chunkPos] = state;
#endif // ENTITIES
    }
    
    GroupMemoryBarrierWithGroupSync();

    if (!isAllBlocksEqual)
        blocks[insertBlockIndex + localIndex] = block;
}

[numthreads(64, 1, 1)]
void chunkCopyToCpu(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint chunkIndex = copyOffset + globalID.x;
    if (chunkIndex >= copyMaxCount)
        return;
    uint3 chunkPos = int3(chunkIndex % sizeInChunksX, (chunkIndex / sizeInChunksX) % sizeInChunksY, chunkIndex / (sizeInChunksX * sizeInChunksY));
    gpuCpuSyncBuffer[globalID.x] = chunks[chunkPos].x;
}

[numthreads(64, 1, 1)]
void computeVoxelBounds(uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex)
{
    uint blockIndex = groupID.x;
    
    uint voxelIndex = blockIndex * 64 + localIndex;
    
    uint curVoxelPair = voxels[voxelIndex / 2];
    uint curVoxel = voxelIndex % 2 == 0 ? (curVoxelPair & 0xFFFF) : (curVoxelPair >> 16);
    uint origVoxel = curVoxel;
    uint state = curVoxel >> 15;
    
    int3 voxelPosInBlock = int3(localIndex % 4, (localIndex / 4) % 4, (localIndex / 16) % 4);
        
    cachedCell[localIndex] = curVoxel;
    ComputeBounds4(localIndex, voxelPosInBlock, 15, 0x1, curVoxel);
    
    if (state == 1)
        cachedCell[localIndex] = origVoxel;
    
    GroupMemoryBarrierWithGroupSync();
    
    if (localIndex % 2 == 0)
        voxels[voxelIndex / 2] = cachedCell[localIndex] | (cachedCell[localIndex + 1] << 16);
}

[numthreads(64, 1, 1)]
void computeBlockBounds(uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex)
{
    uint chunkIndex = groupID.x;
    
    uint blockIndex = chunkIndex * 64 + localIndex;
    
    uint curBlock = blocks[blockIndex];
    uint origBlock = curBlock;
    uint state = curBlock >> 30;
    
    int3 blockPosInChunk = int3(localIndex % 4, (localIndex / 4) % 4, (localIndex / 16) % 4);
        
    cachedCell[localIndex] = curBlock;
    ComputeBounds4(localIndex, blockPosInChunk, 30, 0x3, curBlock);
    
    if (state != 0)
        cachedCell[localIndex] = origBlock;
    
    GroupMemoryBarrierWithGroupSync();
    
    blocks[blockIndex] = cachedCell[localIndex];
}

technique Tech0
{

    pass VoxelHash
    {
        ComputeShader = compile cs_5_0 calcBlockFromRawData();
    }

    pass ComputeVoxelBounds
    {
        ComputeShader = compile cs_5_0 computeVoxelBounds();
    }

    pass ComputeBlockBounds
    {
        ComputeShader = compile cs_5_0 computeBlockBounds();
    }

    pass ChunkCopyToCpu
    {
        ComputeShader = compile cs_5_0 chunkCopyToCpu();
    }
}