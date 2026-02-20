#ifndef __COMMON_OTHER__
#define __COMMON_OTHER__

groupshared uint indexGroup = 0, indexGroupBase = 0;

int addToCounterAddressBuffer(RWByteAddressBuffer buffer, uint localIndex, uint addCount)
{
    GroupMemoryBarrierWithGroupSync();
    
    uint index = 0;
    if (addCount > 0)
        InterlockedAdd(indexGroup, addCount, index);
    
    GroupMemoryBarrierWithGroupSync();
    
    if (localIndex == 0)
        buffer.InterlockedAdd(0, indexGroup, indexGroupBase);
    
    GroupMemoryBarrierWithGroupSync();
    
    return index + indexGroupBase;
}

int addToCounterStructuredBuffer(RWStructuredBuffer<uint> buffer, uint localIndex, uint addCount, uint bufferIndex)
{
    GroupMemoryBarrierWithGroupSync();
    
    uint index = 0;
    if (addCount > 0)
        InterlockedAdd(indexGroup, addCount, index);
    
    GroupMemoryBarrierWithGroupSync();
    
    if (localIndex == 0)
        InterlockedAdd(buffer[bufferIndex], indexGroup, indexGroupBase);
    
    GroupMemoryBarrierWithGroupSync();
    
    return index + indexGroupBase;
}

float gaussianF(float x, float sigma)
{
    return exp(-(pow(x, 2.f)) / (2.f * pow(sigma, 2.f))) / (2.f * 3.14159265f * pow(sigma, 2.f));
}

#endif // __COMMON_OTHER__