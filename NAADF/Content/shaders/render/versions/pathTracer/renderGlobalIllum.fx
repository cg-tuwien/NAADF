#include "../../common/common.fxh"
#include "../../common/atmosphere/atmospherePrecomputed.fxh"
#include "../../rayTracing.fxh"


StructuredBuffer<uint4> firstHitData;
RWStructuredBuffer<uint2> firstHitAbsorption;
RWStructuredBuffer<uint2> finalColor;
RWStructuredBuffer<float4> sampleAccumulated;

matrix invCamMatrix;
float3 camPosFrac;
int camPosIntX, camPosIntY, camPosIntZ;
float3 skySunDir;
int screenWidth, screenHeight;
int randCounter;
int maxBounceCount, frameCount;
float2 taaJitter;
float3 sunColor;
int validSampleStorageCount, invalidSampleStorageCount, accumIndex, taaIndex;
int maxSamples;

[numthreads(64, 1, 1)]
void calcGlobalIlum(uint3 globalID : SV_DispatchThreadID, uint localIndex : SV_GroupIndex)
{
    int3 camPosInt = int3(camPosIntX, camPosIntY, camPosIntZ);
    uint2 pixelPos = uint2(globalID.x % screenWidth, globalID.x / screenWidth);
    
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
   

    // Compute surface interaction for primary ray
    if (materialState == 3) // Mirror
        curDir = reflect(curDir, firstHitResult.normal);
    else if (materialState == 2)
    {
        float3 roughNormal;
        int count = 0;
        do
        {
            roughNormal = sample_vndf_isotropic(float2(nextRand(rand), nextRand(rand)), -curDir, firstHitType.roughness, firstHitResult.normal);
            curDir = reflect(curDir, roughNormal);
        } while (dot(curDir, firstHitResult.normal) <= 0 && count++ < 2);
                
        float GI = geometryTerm(firstHitType.roughness, saturate(dot(curDir, firstHitResult.normal)));
        float3 F = getReflectanceFresnel(ior, dot(curDir, roughNormal));
        extraAbsorption = GI * F;
    }
    else
        curDir = getUniformHemisphereSample(float2(nextRand(rand), nextRand(rand)), firstHitResult.normal, 0);
    float3 sampleDir = curDir;
    float sampleDist = 0;
    
    // Trace rays for global illumination
    [unroll]
    for (int bounce = 0; bounce < min(maxBounceCount, 3) && firstHitResult.normalTang != HIT_NOTHING; ++bounce)
    {
        RayResult rayResult;
        bool isHit = shootRay(curPosInt, curPosFrac, curDir, 2000, rayResult);
        if (bounce < 3 && !isFirstDiffuseHit)
            normTangs[bounce] = rayResult.normalComp;
        
        if (!isHit)
        {
            applyAtmosphere(curPosInt + curPosFrac, curDir, curAbsorbtion, radiance, 1);
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
            if (dot(sunDirRand, rayResult.normal) > 0 && !shootRay(newPosInt, newPosFrac, sunDirRand, 2000, temp))
            {
                isSunHitAfterBounce = true;
                radiance += curAbsorbtion * sunColor * fac * 1;
            }

        }

        if (materialState == 1) // Emissive
            radiance += curAbsorbtion * voxelType.colorLayer.r;
        
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
                roughNormal = sample_vndf_isotropic(float2(nextRand(rand), nextRand(rand)), -newDir, voxelType.roughness, rayResult.normal);
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
            newDir = getUniformHemisphereSample(float2(nextRand(rand), nextRand(rand)), rayResult.normal, 0);
            curAbsorbtion *= saturate(dot(rayResult.normal, newDir)) * 2;
        }

            
        if (bounce == 2 && !isFirstDiffuseHit) // No diffuse hit on last bounce check
            normTangs[2] = 0x1FFFF;
            
        curPosInt = newPosInt;
        curPosFrac = newPosFrac;
        curDir = newDir;
    }
    
    
    uint2 absorptionComp = firstHitAbsorption[globalID.x];
    float3 absorption = extraAbsorption * float3(f16tof32(absorptionComp.x & 0xFFFF), f16tof32(absorptionComp.x >> 16), f16tof32(absorptionComp.y));
    uint2 finalColComp = finalColor[globalID.x];
    float3 finalCol = float3(f16tof32(finalColComp.x & 0xFFFF), f16tof32(finalColComp.x >> 16), f16tof32(finalColComp.y));
    finalCol += radiance * absorption * 2 * saturate(dot(firstHitResult.normal, sampleDir));
    
    float3 firstHitPosFrac = firstHitResult.pos + firstHitResult.normal * 0.02f;
    int3 firstHitPosInt = camPosInt + floor(firstHitPosFrac);
    firstHitPosFrac = firstHitPosFrac - floor(firstHitPosFrac);
    float3 sunDirRand = getUniformHemisphereSample(float2(nextRand(rand), nextRand(rand)), skySunDir, 0.9999f);
    RayResult temp;
    if (firstHitResult.normalTang != HIT_NOTHING)
    {
        bool isSunBlocked = shootRay(firstHitPosInt, firstHitPosFrac, sunDirRand, 2000, temp);
        float sunDirCosTheta = saturate(dot(sunDirRand, firstHitResult.normal));
        if (!isSunBlocked && sunDirCosTheta > 0.001f)
        {
            float sunDirCosTheta = saturate(dot(sunDirRand, firstHitResult.normal));
            float3 weight = 2.0f * sunDirCosTheta;
        
            if (firstHitType.materialBase == 2)
            {
                float GI = geometryTerm(firstHitType.roughness, sunDirCosTheta);
                float GO = geometryTerm(firstHitType.roughness, dot(-firstHitResult.rayDir, firstHitResult.normal));
                float D = pow(firstHitType.roughness, 2) / (PI * pow(pow(dot(firstHitResult.normal, normalize(sunDirRand + -firstHitResult.rayDir)), 2) * (pow(firstHitType.roughness, 2) - 1) + 1, 2));
                float3 F = getReflectanceFresnel(ior, sunDirCosTheta);
                weight *= (0.5f * D * GI * GO * F) / (4 * sunDirCosTheta * dot(-firstHitResult.rayDir, firstHitResult.normal));
            }
            finalCol += sunColor * weight * absorption;
        }
    }
    
    float4 curSamples = sampleAccumulated[globalID.x];
    float newMaxSampleCount = min(maxSamples, curSamples.a + 1);
    curSamples.rgb = lerp(curSamples.rgb, finalCol, 1.0f / newMaxSampleCount);
    curSamples.a = newMaxSampleCount;
    sampleAccumulated[globalID.x] = curSamples;
}

technique MainTechnique
{
    pass GlobalIlum
    {
        ComputeShader = compile cs_5_0 calcGlobalIlum();
    }
};
