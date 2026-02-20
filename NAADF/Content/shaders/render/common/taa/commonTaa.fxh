#include "../common.fxh"

#ifndef __COMMON_TAA__
#define __COMMON_TAA__

static const int2 neighborOffsets[9] =
{
    int2(0, 0),
    int2(0, -1),
    int2(-1, 0),
    int2(1, 0),
    int2(0, 1),
    
    int2(-1, -1),
    int2(1, -1),
    int2(-1, 1),
    int2(1, 1),
};

uint getHashFromData(uint isDiffuse, uint specularNormals, uint entity)
{
    uint hash = isDiffuse | (entity << 1) | (specularNormals << 15);
    hash ^= hash >> 17;
    hash *= 0xed5ad4bb;
    hash ^= hash >> 11;
    hash *= 0xac4c1b51;
    return hash;
}

uint2 compressSample(uint distComp, float3 color, uint normalComp, uint isDiffuse, uint specularNormals, uint extraData, uint entity)
{
    float maxColorChannel = max(color.r, max(color.g, color.b));
    if (maxColorChannel > 100)
        color *= (100.0f / maxColorChannel);
    uint3 colorComp = 12 * log2(color + pow(2, -255.0f / 12.0f) * 100) + (255.0f - 12.0f * log2(100.0f));

    uint hash = getHashFromData(isDiffuse, specularNormals, entity);
    
    uint2 sampleComp = 0;
    sampleComp.x = distComp | ((hash & 0xFFFF) << 16);
    sampleComp.y = colorComp.x | (colorComp.y << 8) | (colorComp.z << 16) | (normalComp << 24) | (extraData << 27);
    return sampleComp;
}

void decompressSample(uint2 sampleComp, out float dist, out float4 color, out uint normalComp, out uint extraData, out uint hash)
{
    dist = f16tof32(sampleComp.x & 0x7FFF);
    uint3 colComp = uint3(sampleComp.y & 0xFF, (sampleComp.y >> 8) & 0xFF, (sampleComp.y >> 16) & 0xFF);
    color = float4(100.0f * pow(2, (colComp - 255.0f) / 12.0f), 1);
    normalComp = (sampleComp.y >> 24) & 0x7;
    extraData = sampleComp.y >> 27;
    hash = sampleComp.x >> 16;
}

#endif // __COMMON_TAA__