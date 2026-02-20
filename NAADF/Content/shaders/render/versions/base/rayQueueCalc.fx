
#include "../../common/common.fxh"

StructuredBuffer<uint4> firstHitData;
RWStructuredBuffer<uint> pixelsToRender;
StructuredBuffer<uint2> taaSampleAccum;
RWByteAddressBuffer groupCount;

uint screenWidth, screenHeight, frameIndex;
bool skipSamples;

bool shouldRay(uint2 pos, float accum)
{
    if (!skipSamples)
        return true;
    
    float fac = accum / 2.0f;
    uint modSize = round(clamp(fac * 2, 0, 3) + 1);
    return ((frameIndex * 4 + pos.x + pos.y) % modSize) == 0;

}

[numthreads(64, 1, 1)]
void calcRayQueue(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint ID = globalID.x;
    
    uint2 pixelPos = uint2(ID % screenWidth, ID / screenWidth);
    bool shouldAdd = (firstHitData[ID].z & 0x7FFF) != 0 && shouldRay(pixelPos, f16tof32(taaSampleAccum[ID].x & 0xFFFF));
    int index = addToCounterAddressBuffer(groupCount, localIndex, shouldAdd);
    
    if (shouldAdd)
        pixelsToRender[index] = pixelPos.x | (pixelPos.y << 16);
}

[numthreads(1, 1, 1)]
void calcRayQueueStore(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint groupCountValue = groupCount.Load(0);
    groupCount.Store(0, (groupCountValue + 63) / 64);
}

technique Tech0
{
    pass RayQueue
    {
        ComputeShader = compile cs_5_0 calcRayQueue();
    }

    pass RayQueueStore
    {
        ComputeShader = compile cs_5_0 calcRayQueueStore();
    }
}