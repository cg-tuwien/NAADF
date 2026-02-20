#define GroupSizeX 64
#define EMPTY_BLOCK 0x0

struct HashValue
{
    uint voxelPointer;
    uint useCount;
    uint hashRaw;
};

int oldSize, newSize;
StructuredBuffer<HashValue> oldMap;
RWStructuredBuffer<HashValue> newMap;

uint hashCoefficients[65];
uint voxelsToHash[32];
RWStructuredBuffer<uint> resultHash;

[numthreads(GroupSizeX, 1, 1)]
void copyMap(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    int ID = globalID.x;
    
    if (ID >= oldSize)
        return;
    
    HashValue hashValue = oldMap[ID];
    if (hashValue.voxelPointer != EMPTY_BLOCK)
    {
        uint newHashBound = hashValue.hashRaw & (newSize - 1);
        int count = 0;
        while (++count < 50)
        {
            uint originalPointer;
            InterlockedCompareExchange(newMap[newHashBound].voxelPointer, EMPTY_BLOCK, hashValue.voxelPointer, originalPointer);
            if (originalPointer == EMPTY_BLOCK)
                break;
            newHashBound = (newHashBound + 1) & (newSize - 1);
        }
        newMap[newHashBound].hashRaw = hashValue.hashRaw;
        newMap[newHashBound].useCount = hashValue.useCount;
    }
}

[numthreads(1, 1, 1)]
void testHash(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint hash = hashCoefficients[0];
    for (uint i = 0; i < 32; ++i)
    {
        uint voxelComp = voxelsToHash[i];
        hash += hashCoefficients[i * 2 + 1] * (voxelComp & 0x7FFF);
        hash += hashCoefficients[i * 2 + 2] * ((voxelComp >> 16) & 0x7FFF);
    }
    resultHash[0] = hash;

}

technique Tech0
{
    pass CopyMap
    {
        ComputeShader = compile cs_5_0 copyMap();
    }

    pass TestHash
    {
        ComputeShader = compile cs_5_0 testHash();
    }
}