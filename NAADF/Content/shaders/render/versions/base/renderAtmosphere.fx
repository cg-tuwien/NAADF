#include "../../common/common.fxh"
#include "../../common/atmosphere/atmosphereRaw.fxh"

RWStructuredBuffer<uint3> atmosphereComp;

float3 camPos;
uint frameCount, atmosphereTexSizeX, atmosphereTexSizeY;

[numthreads(64, 1, 1)]
void precomputeAtmosphere(uint3 globalID : SV_DispatchThreadID)
{
    uint ID = globalID.x * 4 + (frameCount % 4);
    if (ID >= atmosphereTexSizeX * atmosphereTexSizeY)
        return;
    
    uint2 texPos = uint2(ID % atmosphereTexSizeX, ID / atmosphereTexSizeX);
    float2 texPosNorm = texPos / float2(atmosphereTexSizeX, atmosphereTexSizeY);
    float2 octPos = float2(texPosNorm.x, texPosNorm.y);
    float3 rayDir = octDecode(octPos);
    rayDir.y = pow(abs(rayDir.y), 2) * sign(rayDir.y);
    rayDir.xz *= sqrt((1 - rayDir.y * rayDir.y) / (rayDir.x * rayDir.x + rayDir.z * rayDir.z));
    
    float3 absorption = float3(1, 1, 1);
    float3 light = float3(0, 0, 0);
    addLightForDirection(float3(0, camPos.y, 0), rayDir, 1000000, absorption, light, true, skyMainRaySteps, skySubScatterSteps);

    uint3 atmoComp;
    atmoComp.x = f32tof16(light.r) | (f32tof16(light.g) << 16);
    atmoComp.y = f32tof16(light.b) | (f32tof16(absorption.r) << 16);
    atmoComp.z = f32tof16(absorption.g) | (f32tof16(absorption.b) << 16);
    atmosphereComp[ID] = atmoComp;
}


technique ComputeCalc
{
    pass Atmosphere
    {
        ComputeShader = compile cs_5_0 precomputeAtmosphere();
    }
};
