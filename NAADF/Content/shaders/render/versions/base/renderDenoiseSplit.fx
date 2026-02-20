#include "../../common/common.fxh"
#include "../../rayTracing.fxh"

StructuredBuffer<uint2> firstHitAbsorption;

StructuredBuffer<uint3> denoisePreprocessed;
RWStructuredBuffer<uint3> denoisePreprocessedHorizontal;

RWStructuredBuffer<uint2> finalColor;

int screenWidth, screenHeight;
int randCounter;
float denoiseThresh;

[numthreads(64, 1, 1)]
void calcDenoiseHorizontal(uint3 globalID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint3 localID : SV_GroupThreadID, uint localIndex : SV_GroupIndex)
{
    uint2 pixelPos = uint2(globalID.x / screenHeight, globalID.x % screenHeight);
    uint2 rand = initRand(uint3(pixelPos, randCounter));
    
    uint3 processed = denoisePreprocessed[globalID.x];
    
    float3 colorOrig = float3(f16tof32(processed.x & 0xFFFF), f16tof32((processed.x >> 16) & 0xFFFF), f16tof32(processed.y & 0xFFFF));
    float taaWeight = f16tof32(processed.y >> 16);
    
    uint normalTangComp = processed.z & 0x7FFFF;
    if (normalTangComp == 0)
        return;
    float3 normal = NORMAL[normalTangComp & 0x7];
    float3 color = float3(0, 0, 0);
    
    uint state = processed.z >> 19;
    
    
    float weight = 0.000001f; 
    float totalTaaWeight = 0.000001f;
    

    for (int y = -10; y <= 10; ++y)
    {  
        int x = nextRand(rand) < 0.5f && y != 0 ? 1 : 0;
        int2 curPixelPos = pixelPos + int2(x, y * 2);
            
        if (curPixelPos.x >= 0 && curPixelPos.x < screenWidth && curPixelPos.y >= 0 && curPixelPos.y < screenHeight)
        {
            int curIndex = curPixelPos.y + curPixelPos.x * screenHeight;
        
            uint3 curProcessed = denoisePreprocessed[curIndex];
               
            float3 curColor = float3(f16tof32(curProcessed.x & 0xFFFF), f16tof32((curProcessed.x >> 16) & 0xFFFF), f16tof32(curProcessed.y & 0xFFFF));
            float curTaaWeight = f16tof32(curProcessed.y >> 16);
        
            float bilateralFac = rcp(1.0f + abs(curTaaWeight - taaWeight) * denoiseThresh);
            float fac = bilateralFac * (normalTangComp == (curProcessed.z & 0x7FFFF) && state == (curProcessed.z >> 19));
            fac *= gaussianF(y, 10);
            color += curColor * fac;
            totalTaaWeight += curTaaWeight * fac;
            weight += fac;
        }
    }
    
    totalTaaWeight /= weight;
    color /= weight;
    
    uint2 curColorComp = uint2(f32tof16(color.x) | (f32tof16(color.y) << 16), f32tof16(color.z));
    
    uint3 newDenoiseProcessed;
    newDenoiseProcessed.x = curColorComp.x;
    newDenoiseProcessed.y = (curColorComp.y & 0xFFFF) | (f32tof16(totalTaaWeight) << 16);
    newDenoiseProcessed.z = processed.z;
    
    denoisePreprocessedHorizontal[pixelPos.x + pixelPos.y * screenWidth] = newDenoiseProcessed;
}

[numthreads(64, 1, 1)]
void calcDenoiseVertical(uint3 globalID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint3 localID : SV_GroupThreadID, uint localIndex : SV_GroupIndex)
{
    uint2 pixelPos = uint2(globalID.x % screenWidth, globalID.x / screenWidth);
    uint2 rand = initRand(uint3(pixelPos, randCounter + 11));
    
    uint3 processed = denoisePreprocessed[pixelPos.y + pixelPos.x * screenHeight];
    uint3 processed2 = denoisePreprocessedHorizontal[globalID.x];
    
    float3 colorOrig = float3(f16tof32(processed.x & 0xFFFF), f16tof32((processed.x >> 16) & 0xFFFF), f16tof32(processed.y & 0xFFFF));
    float taaWeight = f16tof32(processed2.y >> 16);
    
    uint normalTangComp = processed.z & 0xFFFFF;
    if (normalTangComp == 0)
        return;
    float3 normal = NORMAL[normalTangComp & 0x7];
    float3 color = float3(0, 0, 0);
    
    uint state = processed.z >> 19;
    uint metalicNormal = (processed.z >> 20) & 0x7;
    
    
    float weight = 0.000001f;
    
    for (int x = -10; x <= 10; ++x)
    {
        int y = nextRand(rand) < 0.5f && x != 0 ? 1 : 0;
        int2 curPixelPos = pixelPos + int2(x * 2, y);
            
        if (curPixelPos.x >= 0 && curPixelPos.x < screenWidth && curPixelPos.y >= 0 && curPixelPos.y < screenHeight)
        {
            int curIndex = curPixelPos.x + curPixelPos.y * screenWidth;
        
            uint3 curProcessed = denoisePreprocessedHorizontal[curIndex];
               
            float3 curColor = float3(f16tof32(curProcessed.x & 0xFFFF), f16tof32((curProcessed.x >> 16) & 0xFFFF), f16tof32(curProcessed.y & 0xFFFF));
            float curTaaWeight = f16tof32(curProcessed.y >> 16);
        
            float bilateralFac = rcp(1.0f + abs(curTaaWeight - taaWeight) * denoiseThresh);
            float fac = bilateralFac * (normalTangComp == (curProcessed.z & 0x7FFFF) && state == (curProcessed.z >> 19));
            fac *= gaussianF(x, 10);
            color += curColor * fac;
            weight += fac;
        }
    }

    
    
    color /= weight;
    float3 finalCol = lerp(colorOrig, color, 0.92f);
    
    uint2 absorptionComp = firstHitAbsorption[globalID.x];
    float3 absorption = float3(f16tof32(absorptionComp.x & 0xFFFF), f16tof32(absorptionComp.x >> 16), f16tof32(absorptionComp.y));
    uint2 finalColComp = finalColor[globalID.x];
    finalCol *= absorption;
    finalCol += float3(f16tof32(finalColComp.x & 0xFFFF), f16tof32(finalColComp.x >> 16), f16tof32(finalColComp.y));
    finalColor[globalID.x] = uint2(f32tof16(finalCol.x) | (f32tof16(finalCol.y) << 16), f32tof16(finalCol.z));
}

technique Compute
{

    pass CalcDenoiseHorizontal
    {
        ComputeShader = compile cs_5_0 calcDenoiseHorizontal();
    }

    pass CalcDenoiseVertical
    {
        ComputeShader = compile cs_5_0 calcDenoiseVertical();
    }
};
