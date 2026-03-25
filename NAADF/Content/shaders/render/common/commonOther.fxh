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

uint gcd(uint a, uint b)
{
    while (b != 0)
    {
        uint t = b;
        b = a % b;
        a = t;
    }
    return a;
}

uint findCoprime(uint n, uint seed)
{
    uint A = (seed | 1);
    while (gcd(A, n) != 1)
        A += 2;
    return A;
}

uint nextPow2(uint v)
{
    if (v <= 1)
        return 1;

    v--;
    v |= v >> 1;
    v |= v >> 2;
    v |= v >> 4;
    v |= v >> 8;
    v |= v >> 16;
    v++;
    return v;
}

#endif // __COMMON_OTHER__