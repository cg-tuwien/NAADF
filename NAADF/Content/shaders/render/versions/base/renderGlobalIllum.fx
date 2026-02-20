#include "../../common/common.fxh"
#include "../../common/atmosphere/atmospherePrecomputed.fxh"
#include "../../rayTracing.fxh"


StructuredBuffer<uint4> firstHitData;
RWStructuredBuffer<uint2> firstHitAbsorption;

RWStructuredBuffer<SampleValid> globalIlumValidSamples;
RWStructuredBuffer<uint4> globalIlumInvalidSamples;
RWStructuredBuffer<uint2> globalIlumSampleCounts;
RWStructuredBuffer<uint2> finalColor;

StructuredBuffer<uint> pixelsToRender;

float3 camPosFrac;
int camPosIntX, camPosIntY, camPosIntZ;
matrix invCamMatrix;
float3 skySunDir;
int screenWidth, screenHeight;
int randCounter, maxBounceCount, frameCount;
float2 taaJitter;
float3 sunColor;
int validSampleStorageCount, invalidSampleStorageCount, accumIndex, taaIndex;

matrix camRotOld[64];
float3 taaOldCamPosFromCurCamInt[64];
float2 taaJitterOld[64];

groupshared uint sharedResCount = 0;
groupshared uint globalResCountValid = 0;
groupshared uint globalResCountInvalid = 0;

SampleValid compressSampleValid(uint2 pixelPos, uint4 firstHit, float3 sampleDir, uint compColor, uint3 sampleSpecularNormals, uint entitySample, uint roughness, uint isFirst)
{
    uint2 sampleDirOct = octEncode(sampleDir) * pow(2, 22);
    
    SampleValid sampleValid;
    sampleValid.data1.x = firstHit.x;
    sampleValid.data1.y = pixelPos.x | (firstHit.y & 0xFFFF8000);
    sampleValid.data1.z = pixelPos.y | (firstHit.z & 0xFFFF8000);
    sampleValid.data1.w = taaIndex | (roughness << 7) | (firstHit.w & 0xFFFF8000);
    sampleValid.data2.x = entitySample | (isFirst << 14) | (sampleSpecularNormals.x << 15);
    sampleValid.data2.y = compColor | (sampleSpecularNormals.y << 15);
    sampleValid.data2.z = (sampleDirOct.y >> 10) | (sampleSpecularNormals.z << 15);
    sampleValid.data2.w = (sampleDirOct.y & 0x3FF) | (sampleDirOct.x << 10);
    return sampleValid;
}

uint4 compressSampleInvalid(uint2 pixelPos, uint4 firstHit, uint roughness)
{
    uint4 sampleInvalid;
    sampleInvalid.x = firstHit.x;
    sampleInvalid.y = pixelPos.x | (firstHit.y & 0xFFFF8000);
    sampleInvalid.z = pixelPos.y | (firstHit.z & 0xFFFF8000);
    sampleInvalid.w = taaIndex | (roughness << 7) | (firstHit.w & 0xFFFF8000);
    return sampleInvalid;
}

[numthreads(64, 1, 1)]
void calcGlobalIlum(uint3 globalID : SV_DispatchThreadID, uint localIndex : SV_GroupIndex)
{
    int3 camPosInt = int3(camPosIntX, camPosIntY, camPosIntZ);
    uint pixelPosComp = pixelsToRender[globalID.x];
    uint2 pixelPos = uint2(pixelPosComp & 0xFFFF, (pixelPosComp >> 16) & 0xFFFF);

    
    uint2 rand = initRand(uint3(pixelPos, randCounter));
    float3 rayDir = getRayDir(invCamMatrix, pixelPos, screenWidth, screenHeight, taaJitter);

    uint4 firstHit = firstHitData[pixelPos.x + pixelPos.y * screenWidth];
    FirstHitResult firstHitResult = getHitDataFromPlanes(entityInstancesHistory, taaIndex, firstHit, camPosInt, camPosFrac, rayDir);
    
    uint firstHitTypeIndex = firstHit.z & 0x7FFF;
    VoxelType firstHitType = decompressVoxelType(voxelTypeData[firstHitTypeIndex]);
    float3 ior = firstHitType.colorBase;
    uint firstHitIsDiffuse = firstHitType.materialBase != SURFACE_SPECULAR_ROUGH;
    
    
    float3 curPosFrac = firstHitResult.pos + firstHitResult.normal * 0.01f;
    int3 curPosInt = camPosInt + floor(curPosFrac);
    curPosFrac = curPosFrac - floor(curPosFrac);
    
    float3 curDir = rayDir;
    uint materialState = firstHitType.materialBase;
    bool isSunHitAfterBounce = false;
    float3 radiance = float3(0, 0, 0);
    float3 curAbsorbtion = float3(1, 1, 1);
    float3 extraAbsorption = float3(1, 1, 1);
    uint3 normTangs = uint3(HIT_NOTHING, HIT_UNDEFINED, HIT_UNDEFINED);
    bool isFirstDiffuseHit = false;
    uint sampleNormalComp = 0;
    bool hitEmitterDirectly = false;
    uint entitySample = ENTITY_FREE;
   

    // Compute surface interaction for primary ray
    if (materialState == 3) // Mirror
        curDir = reflect(curDir, firstHitResult.normal);
    else if (materialState == 2)
    {
        float3 roughNormal;
        int count = 0;
        do
        {
            roughNormal = sample_vndf_isotropic(nextRand2(rand), -curDir, firstHitType.roughness, firstHitResult.normal);
            curDir = reflect(curDir, roughNormal);
        } while (dot(curDir, firstHitResult.normal) <= 0 && count++ < 2);
                
        float GI = geometryTerm(firstHitType.roughness, saturate(dot(curDir, firstHitResult.normal)));
        float3 F = getReflectanceFresnel(ior, dot(curDir, roughNormal));
        extraAbsorption = GI * F;
    }
    else
        curDir = getUniformHemisphereSample(nextRand2(rand), firstHitResult.normal, 0);
    float3 sampleDir = curDir;
    float sampleDist = 0;

    
    // Trace rays for global illumination
    [unroll]
    for (int bounce = 0; bounce < min(maxBounceCount, 3); ++bounce)
    {
        RayResult rayResult;
        bool isHit = shootRay(curPosInt, curPosFrac, curDir, MAX_RAY_STEPS_SECONDARY, rayResult);
        if (bounce < 3 && !isFirstDiffuseHit)
            normTangs[bounce] = rayResult.normalComp;
        
        if (!isHit)
        {
            if (nextRand(rand) <= 1.0f / 16.0f)
                applyAtmosphere(curPosInt + curPosFrac, curDir, curAbsorbtion, radiance, 16);
            if (!isFirstDiffuseHit)
                sampleDist = 1024;
            break;
        }
        
        if (!isFirstDiffuseHit)
            sampleDist += rayResult.length;
        
        
        float3 newPosFrac = curPosFrac + curDir * rayResult.length + rayResult.normal * 0.01f;
        int3 newPosInt = curPosInt + floor(newPosFrac);
        newPosFrac = newPosFrac - floor(newPosFrac);
        float3 newDir = curDir;
                
        VoxelType voxelType = decompressVoxelType(voxelTypeData[rayResult.type]);
        materialState = voxelType.materialBase;
                
        // Apply albedo
        if (materialState <= 1)
            curAbsorbtion *= voxelType.colorBase;
        
        
        // Apply sun
        if (materialState <= 2)
        {
            if (!isFirstDiffuseHit)
            {
                sampleNormalComp = rayResult.normalComp & 0x7;
#ifdef ENTITIES
                entitySample = rayResult.entity;
#endif // ENTITIES
                isFirstDiffuseHit = true;
            }
                    
            // Check for sun
            float3 sunDirRand = getUniformHemisphereSample(float2(nextRand(rand), nextRand(rand)), skySunDir, 0.9999f);
            float3 fac = saturate(dot(rayResult.normal, sunDirRand)) * 2;
                    
            if (materialState == 2)
            {
                float GI = geometryTerm(voxelType.roughness, dot(sunDirRand, rayResult.normal));
                float GO = geometryTerm(voxelType.roughness, dot(-curDir, rayResult.normal));
                float D = pow(voxelType.roughness, 2) / (PI * pow(pow(dot(rayResult.normal, normalize(sunDirRand + -curDir)), 2) * (pow(voxelType.roughness, 2) - 1) + 1, 2));
                float3 F = getReflectanceFresnel(voxelType.colorBase, dot(sunDirRand, rayResult.normal));
                float normMaxD = voxelType.roughness * 500.0f + 1;
                float normD = normMaxD - normMaxD / ((1.0f / normMaxD) * D + 1.0f);
                fac *= (0.5f * normD * GI * GO * F) / (4 * 1 * dot(-curDir, rayResult.normal));
            }
            RayResult temp;
            if (dot(sunDirRand, rayResult.normal) > 0 && !shootRay(newPosInt, newPosFrac, sunDirRand, MAX_RAY_STEPS_SUN_SECONDARY, temp))
            {
                isSunHitAfterBounce = true;
                radiance += curAbsorbtion * sunColor * fac * 1;
            }
        }
        
        
        if (materialState == 1) // Emissive
        {
            hitEmitterDirectly = bounce == 0;
            radiance += curAbsorbtion * voxelType.colorLayer.r;
        }
      
        
        // Apply surface effect
        if (materialState == 3)
        {
            newDir = reflect(curDir, rayResult.normal);
            curAbsorbtion *= getReflectanceFresnel(voxelType.colorBase, dot(newDir, rayResult.normal));
        }
        else if (materialState == 2)
        {
            float3 roughNormal;
            int count = 0;
            newDir = curDir;
            do
            {
                roughNormal = sample_vndf_isotropic(nextRand2(rand), -newDir, voxelType.roughness, rayResult.normal);
                newDir = reflect(newDir, roughNormal);
            } while (dot(newDir, rayResult.normal) <= 0 && count++ < 2);
                
            if (dot(newDir, rayResult.normal) <= 0)
                break;
                
            float GI = geometryTerm(voxelType.roughness, saturate(dot(newDir, rayResult.normal)));
            float3 F = getReflectanceFresnel(voxelType.colorBase, dot(newDir, roughNormal));
            curAbsorbtion *= GI * F;
        }
        else
        {
            newDir = getUniformHemisphereSample(nextRand2(rand), rayResult.normal, 0);
            curAbsorbtion *= saturate(dot(rayResult.normal, newDir)) * 2;
        }
        
       
            
        if (bounce == 2 && !isFirstDiffuseHit) // No diffuse hit on last bounce check
            normTangs[2] = 0x1FFFF;
            
        curPosInt = newPosInt;
        curPosFrac = newPosFrac;
        curDir = newDir;
    }

    uint radianceCompWithAbsorption = compressColor(radiance * extraAbsorption, rand);

    float radianceSingle = dot(radiance, float3(1, 1, 1));
    const float RADIANCE_REDUCTION_VAL = 2.0f;
    if (radianceSingle < RADIANCE_REDUCTION_VAL)
    {
        float test = max(radianceSingle, 0.1f);
        radiance *= RADIANCE_REDUCTION_VAL / test;
        if (nextRand(rand) > test / RADIANCE_REDUCTION_VAL)
            radiance = float3(0, 0, 0);

    }
    uint radianceComp = compressColor(radiance, rand);
    
    bool isValid = radianceComp > 0;
    bool isSkip = !isValid && nextRand(rand) > 1.0f / 8.0f;
    
    GroupMemoryBarrierWithGroupSync();
    
    uint prevSampleCount = 0;
    if (!isSkip)
        InterlockedAdd(sharedResCount, isValid ? 1 : (1 << 16), prevSampleCount);
    
    GroupMemoryBarrierWithGroupSync();
    
    if (localIndex == 0)
    {
        InterlockedAdd(globalIlumSampleCounts[3 + accumIndex].x, sharedResCount & 0xFFFF, globalResCountValid);
        InterlockedAdd(globalIlumSampleCounts[3 + accumIndex].y, sharedResCount >> 16, globalResCountInvalid);
    }
    
    GroupMemoryBarrierWithGroupSync();
    
    uint2 samplesStartIndex = globalIlumSampleCounts[0];
    
    uint extraData = 0;
    if (firstHitType.materialBase == 2)
        extraData = 1 + (uint) (firstHitType.roughness * 254.5f);
    
    if (isValid)
    {
        uint maxSampleCount = validSampleStorageCount * screenWidth * screenHeight;
        uint index = prevSampleCount & 0xFFFF;
        SampleValid sampleValid = compressSampleValid(pixelPos, firstHit, sampleDir, radianceComp, normTangs, entitySample, extraData, hitEmitterDirectly);
        globalIlumValidSamples[(samplesStartIndex.x + maxSampleCount + index - (globalResCountValid + (sharedResCount & 0xFFFF))) % maxSampleCount] = sampleValid;
    }
    else if (!isSkip)
    {
        uint maxSampleCount = invalidSampleStorageCount * screenWidth * screenHeight;
        uint index = prevSampleCount >> 16;
        uint4 sampleInvalid = compressSampleInvalid(pixelPos, firstHit, extraData);
        globalIlumInvalidSamples[(samplesStartIndex.y + maxSampleCount + index - (globalResCountInvalid + (sharedResCount >> 16))) % maxSampleCount] = sampleInvalid;
    }
    
}

technique MainTechnique
{
    pass GlobalIlum
    {
        ComputeShader = compile cs_5_0 calcGlobalIlum();
    }
};
