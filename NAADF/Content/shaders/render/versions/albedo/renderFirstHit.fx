#include "../../common/common.fxh"
#include "../../rayTracing.fxh"
#include "../../common/taa/commonTaa.fxh"

RWStructuredBuffer<uint4> firstHitData;
RWStructuredBuffer<uint2> taaSampleAccum;
RWStructuredBuffer<uint2> taaSamples;

matrix invCamMatrix;
int camPosIntX, camPosIntY, camPosIntZ;
float3 camPosFrac;

bool showRayStep, checkSun, isTAA;
uint screenWidth, screenHeight;
uint randCounter, frameCount, taaIndex;
float3 skySunDir, sunColor;
float2 taaJitter;

uint4 compressFirstHitData(float dist, uint4 normTangs, uint voxelTypeRaw, uint entity)
{
    uint4 firstHit;
    firstHit.x = entity | (normTangs.x << 15);
    firstHit.y = true | (normTangs.y << 15);
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

    float2 rayMinMax;
    float3 maskVolume;
    bool isVolumeHit = rayAABB(camPosInt + camPosFrac, rayDir, boundingBoxMin, boundingBoxMax, rayMinMax, maskVolume);
    
    float3 absorption = float3(1, 1, 1);
    float3 light = float3(0, 0, 0);
    uint4 normTangs = uint4(HIT_NOTHING, HIT_UNDEFINED, HIT_UNDEFINED, HIT_UNDEFINED);
    uint voxelTypeRaw = 0;
    float3 firstHitNormal = 0;
    uint firstHitNormalTang = 0;
    float distanceRay = -1;
    RayResult rayResult;
    rayResult.stepCount = 0;
    int3 curPosInt = camPosInt;
    float3 curPosFrac = camPosFrac;
    uint entity = ENTITY_FREE;
    
    if (isVolumeHit)
    {
        curPosFrac = camPosFrac + rayDir * rayMinMax.x;
        curPosInt += floor(curPosFrac);
        curPosFrac = curPosFrac - floor(curPosFrac);
    
        bool isHit = shootRay(curPosInt, curPosFrac, rayDir, MAX_RAY_STEPS_PRIMARY, rayResult);
        normTangs[0] = rayResult.normalComp;

        if (isHit)
        {
            curPosFrac += rayDir * rayResult.length + rayResult.normal * 0.01f;
            curPosInt += floor(curPosFrac);
            curPosFrac = curPosFrac - floor(curPosFrac);
            
            VoxelType voxelType = decompressVoxelType(voxelTypeData[rayResult.type]);
            distanceRay = rayResult.length + rayMinMax.x;
            firstHitNormal = rayResult.normal;
            firstHitNormalTang = rayResult.normalComp;
            voxelTypeRaw = rayResult.type;
            
            absorption *= voxelType.colorBase;
            if (voxelType.materialBase == SURFACE_EMISSIVE) // Emissive
                light += absorption * voxelType.colorLayer.r;
            
#ifdef ENTITIES
            entity = rayResult.entity;
#endif // ENTITIES
        }
    }
    
    // Sample the sun
    if (distanceRay > 0)
    {
        if (checkSun)
        {
            RayResult temp;
            bool isSunBlocked = shootRay(curPosInt, curPosFrac + firstHitNormal * 0.01f, skySunDir, MAX_RAY_STEPS_SUN, temp);
            float sunDirCosTheta = saturate(dot(skySunDir, firstHitNormal));
            if (!isSunBlocked && sunDirCosTheta > 0.001f)
            {
                float sunDirCosTheta = saturate(dot(skySunDir, firstHitNormal));
                float3 weight = 2.0f * sunDirCosTheta;
                    
                light += sunColor * weight * absorption;
            }
        }
        float3 dirForAmbient = normalize(firstHitNormal + skySunDir * 1.01f);
        light += absorption * sunColor * 0.2f * dot(skySunDir, dirForAmbient);
    }

   
    if (isTAA)
    {
        uint4 firstHit = compressFirstHitData(distanceRay, normTangs, voxelTypeRaw, entity);
        firstHitData[globalID.x] = firstHit;
    
        uint specularNormals = getSpecularNormals(firstHit);
        uint2 sampleComp = compressSample(f32tof16(voxelTypeRaw == 0 ? 65520 : distanceRay), light, firstHitNormalTang & 0x7, true, specularNormals, 0, entity);
        taaSamples[(taaIndex % 32) * screenWidth * screenHeight + globalID.x] = sampleComp;
    }
    
    uint2 newColorComp = uint2(0, 0);
    newColorComp.x = f32tof16(1.0f) | (f32tof16(light.r) << 16);
    newColorComp.y = f32tof16(light.g) | (f32tof16(light.b) << 16);
    if (showRayStep)
        newColorComp.x = rayResult.stepCount;
    taaSampleAccum[globalID.x] = newColorComp;
}

technique ComputeCalc
{
    pass FirstHit
    {
        ComputeShader = compile cs_5_0 calcFirstHit();
    }
};
