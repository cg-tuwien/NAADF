#include "../../common/common.fxh"
#include "../../common/atmosphere/atmosphereRaw.fxh"
#include "../../common/atmosphere/atmospherePrecomputed.fxh"
#include "../../rayTracing.fxh"

RWStructuredBuffer<uint4> firstHitData;
RWStructuredBuffer<uint2> firstHitAbsorption;
RWStructuredBuffer<uint2> finalColor;

matrix invCamMatrix;
int camPosIntX, camPosIntY, camPosIntZ;
float3 camPosFrac;
bool showRayStep, isAtmosphereInteraction;
uint screenWidth, screenHeight;
uint randCounter, frameCount;
float2 taaJitter;

uint4 compressFirstHitData(float dist, uint4 normTangs, uint voxelTypeRaw, uint isDiffuse, uint entity)
{
    uint4 firstHit;
    firstHit.x = entity | (normTangs.x << 15);
    firstHit.y = isDiffuse | (normTangs.y << 15);
    firstHit.z = voxelTypeRaw | (normTangs.z << 15);
    firstHit.w = (f32tof16(dist) & 0x7FFF) | (normTangs.w << 15);
    return firstHit;
}

[numthreads(64, 1, 1)]
void calcFirstHit(uint3 globalID : SV_DispatchThreadID)
{
    int3 camPosInt = int3(camPosIntX, camPosIntY, camPosIntZ);
    if (globalID.x >= screenWidth * screenHeight)
        return;
   
    uint2 pixelPos = uint2(globalID.x % screenWidth, globalID.x / screenWidth);
    uint2 rand = initRand(uint3(pixelPos, randCounter));
    float3 rayDir = getRayDir(invCamMatrix, pixelPos, screenWidth, screenHeight, taaJitter);
    float3 rayDirNoJitter = getRayDir(invCamMatrix, pixelPos, screenWidth, screenHeight, 0);

    float2 rayMinMax;
    float3 maskVolume;
    bool isVolumeHit = rayAABB(camPosInt + camPosFrac, rayDir, boundingBoxMin, boundingBoxMax, rayMinMax, maskVolume);
    
    float3 absorption = float3(1, 1, 1);
    float3 light = float3(0, 0, 0);
    uint4 normTangs = uint4(HIT_NOTHING, HIT_UNDEFINED, HIT_UNDEFINED, HIT_UNDEFINED);
    uint voxelTypeRaw = 0;
    float distanceRay = -1;
    RayResult rayResult;
    rayResult.stepCount = 0;
    uint isDiffuse = 1;
    int3 curPosInt = camPosInt;
    float3 curPosFrac = camPosFrac;
    uint entity = ENTITY_FREE;
    
    if (isVolumeHit)
    {
        float3 oldPos = curPosInt + curPosFrac;
        curPosFrac = camPosFrac + rayDir * rayMinMax.x;
        curPosInt += floor(curPosFrac);
        curPosFrac = curPosFrac - floor(curPosFrac);
    
        float dist = 0;
        int i;
        [unroll]
        for (i = 0; i < 4; ++i)
        {
            bool isHit = shootRay(curPosInt, curPosFrac, rayDir, MAX_RAY_STEPS_PRIMARY, rayResult);
            normTangs[i] = rayResult.normalComp;

            if (!isHit)
            {
                applyAtmosphere(oldPos, i == 0 ? rayDirNoJitter : rayDir, absorption, light);
                break;
            }
            
            // Advance ray to new surface
            dist += rayResult.length;
            curPosFrac += rayDir * rayResult.length + rayResult.normal * 0.01f;
            curPosInt += floor(curPosFrac);
            curPosFrac = curPosFrac - floor(curPosFrac);
            float cosTheta = saturate(dot(rayResult.normal, -rayDir));

            // Add light from current point with scattering and absorption applied
            if (isAtmosphereInteraction)
                addLightForDirection(oldPos, rayDir, distance(curPosInt + curPosFrac, oldPos), absorption, light, false, 3, 3);
            
            
            VoxelType voxelType = decompressVoxelType(voxelTypeData[rayResult.type]);
            float3 ior = voxelType.colorBase;
            
            // Surface is not mirror like -> terminate primary ray bounces
            if (voxelType.materialBase != SURFACE_SPECULAR_MIRROR)
            {
                if (voxelType.materialBase != SURFACE_SPECULAR_ROUGH) // Apply albedo
                    absorption *= voxelType.colorBase;
                
                if (voxelType.materialBase == SURFACE_EMISSIVE) // Emissive
                    light += absorption * voxelType.colorLayer.r;
                
                distanceRay = dist + rayMinMax.x;
                voxelTypeRaw = rayResult.type;
                isDiffuse = voxelType.materialBase != SURFACE_SPECULAR_ROUGH;
#ifdef ENTITIES
                entity = rayResult.entity;
#endif // ENTITIES
                break;
            }
            
            float3 R = getReflectanceFresnel(ior, cosTheta);   
            absorption *= R;
                
            rayDir = reflect(rayDir, rayResult.normal);
            oldPos = curPosInt + curPosFrac;
        }
        
        if (i == 4)
        {
            normTangs[3] = 0x1FFFF;
            distanceRay = -1;
        }
    }
    else
        applyAtmosphere(camPosInt + camPosFrac, rayDir, absorption, light);
    
    firstHitData[globalID.x] = compressFirstHitData(distanceRay, normTangs, showRayStep ? rayResult.stepCount : voxelTypeRaw, isDiffuse, entity);
    firstHitAbsorption[globalID.x] = uint2(f32tof16(absorption.x) | (f32tof16(absorption.y) << 16), f32tof16(absorption.z));
    finalColor[globalID.x] = uint2(f32tof16(light.x) | (f32tof16(light.y) << 16), f32tof16(light.z));
}

technique ComputeCalc
{
    pass FirstHit
    {
        ComputeShader = compile cs_5_0 calcFirstHit();
    }
};
