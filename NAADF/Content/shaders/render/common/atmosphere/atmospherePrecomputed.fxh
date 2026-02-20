#include "../common.fxh"

#ifndef __ATMOSPHERE_PRECOMPUTED__
#define __ATMOSPHERE_PRECOMPUTED__

int atmosphereTexSizeX, atmosphereTexSizeY;
StructuredBuffer<uint3> atmosphereComp;

void applyAtmosphere(float3 pos, float3 rayDir, inout float3 absorption, inout float3 light, const float atmoMul = 1)
{
    rayDir.y = pow(abs(rayDir.y), 0.5) * sign(rayDir.y);
    rayDir.xz *= sqrt((1 - rayDir.y * rayDir.y) / (rayDir.x * rayDir.x + rayDir.z * rayDir.z));
    
    float2 oct = octEncode(rayDir);
    uint2 compPos = uint2(oct.x * (atmosphereTexSizeX - 1), oct.y * (atmosphereTexSizeY - 1));
    uint3 atmoComp = atmosphereComp[compPos.x + compPos.y * atmosphereTexSizeX];
    float3 atmoLight = float3(f16tof32(atmoComp.x & 0xFFFF), f16tof32(atmoComp.x >> 16), f16tof32(atmoComp.y & 0xFFFF)) * 1;
    float3 atmoAbsorption = lerp(float3(1, 1, 1), float3(f16tof32(atmoComp.y >> 16), f16tof32(atmoComp.z & 0xFFFF), f16tof32(atmoComp.z >> 16)), 1);
    
    light += absorption * atmoLight * atmoMul;
    absorption *= atmoAbsorption;
}

#endif // __ATMOSPHERE_PRECOMPUTED__