#include "../../common/common.fxh"
#include "../../rayTracing.fxh"

StructuredBuffer<uint4> firstHitData;
StructuredBuffer<uint2> firstHitAbsorption;

StructuredBuffer<uint2> globalIlumBucketInfo;
StructuredBuffer<uint4> globalIlumValidSamplesCompressed;

StructuredBuffer<uint2> taaSampleAccum;
RWStructuredBuffer<uint2> finalColor;

RWStructuredBuffer<uint3> denoisePreprocessed;

float3 camPosFrac;
int camPosIntX, camPosIntY, camPosIntZ;
matrix invCamMatrix;
float test1;
uint screenWidth, screenHeight;
uint globalIlumBucketSizeX, globalIlumBucketSizeY;
uint randCounter;
uint spatialVisibilityCount;
float3 skySunDir, sunColor;
uint bucketStorageCount;
uint frameCount, taaIndex;
float2 taaJitter;
bool isDenoise, colorCorrection;
matrix camRotOld;

void getSampleData(uint4 res, out int3 visPosInt, out float3 visPosFrac, out float3 sampleDir, out float sampleDist, out float3 sampleNormal, out bool isDiffuse)
{
    uint3 visPosComp = uint3((res.z >> 11) & 0x7FFFF, res.x >> 15, (res.w >> 11) & 0x7FFFF);
    visPosInt = visPosComp / 32;
    visPosFrac = (visPosComp % 32) / 32.0f;
    sampleDist = f16tof32(res.y >> 16);
    isDiffuse = (res.w >> 30) & 0x1;
    sampleDir = octDecode(uint2(res.z & 0x7FF, res.w & 0x7FF) / 2047.5f);
    sampleNormal = octDecode(uint2(res.y & 0xFF, (res.y >> 8) & 0xFF) / 255.0f);
}

float3 getBRDF(float roughness, float3 ior, float3 normal, float3 lightDir, float3 rayDir)
{
    float GI = geometryTerm(roughness, dot(lightDir, normal));
    float GO = geometryTerm(roughness, dot(rayDir, normal));
    float D = pow(roughness, 2) / (PI * pow(pow(dot(normal, normalize(lightDir + rayDir)), 2) * (pow(roughness, 2) - 1) + 1, 2));
    float3 F = getReflectanceFresnel(ior, dot(lightDir, normal));
    return (D * GI * GO * F) / (4 * dot(rayDir, normal));
}

float getTargetFunctionNew(float3 sampleDir, float3 visNormal, float3 radiance, float3 brdf)
{
    //return length(radiance);
    float3 brdfCos = clamp(brdf /* * saturate(dot(visNormal, sampleDir))*/, 0, 100.0f);
    return length(radiance * brdfCos);
}

float3 sampleNeighbors(uint2 pixelPos, uint sampleCount, FirstHitResult firstHit, uint typeIndex)
{
    uint2 rand = initRand(uint3(pixelPos, randCounter));
    int3 camPosInt = int3(camPosIntX, camPosIntY, camPosIntZ);
    float sumWeight = 0;
    
    float3 selectedColor = float3(0, 0, 0);
    float3 selectedRayDir = float3(0, 0, 0);
    float selectedPSum = 0;
    float selectedLengthToSampleSquaredNow = 0, selectedJacobian = 1;
    bool selectedIsSky = false;
    uint selectedBounceState = 0;
    
    float3 firstHitPos = firstHit.pos + firstHit.normal * 0.02f;
    float3 firstHitPosFrac = frac(firstHitPos);
    int3 firstHitPosInt = camPosInt + (int3) floor(firstHitPos);
    
    VoxelType firstHitType = decompressVoxelType(voxelTypeData[typeIndex]);
    bool firstHitIsDiffuse = firstHitType.materialBase != SURFACE_SPECULAR_ROUGH;
    
    float radius = test1;
    float extraSumSamples = 0;
    float sumSamplesSky = 0, sumSamplesNoSky = 0, sampleCountSky = 0, sampleCountNoSky = 0;
    
    int bucketIndexCur = (pixelPos.x / 8) + (pixelPos.y / 8) * globalIlumBucketSizeX;
    uint2 bucketInfoCur = globalIlumBucketInfo[bucketIndexCur];
    float bucketLitRatioCur = f16tof32((bucketInfoCur.x >> 9) & 0x7FFF);
    
    for (uint i = 0; i < sampleCount; ++i)
    {
        float x = -radius * 0.5 + radius * nextRand(rand);
        float y = -radius * 0.5 + radius * nextRand(rand);
        
        radius = lerp(radius, test1, 0.04f);
        radius = clamp(radius, test1 * 0.2f, test1 * 1.5f);
        
        int2 neighborIndex = (int2) pixelPos + int2(x, y);
        neighborIndex.x = neighborIndex.x < 0 ? -neighborIndex.x : (neighborIndex.x > (int) screenWidth ? 2 * screenWidth - neighborIndex.x : neighborIndex.x);
        neighborIndex.y = neighborIndex.y < 0 ? -neighborIndex.y : (neighborIndex.y > (int) screenHeight ? 2 * screenHeight - neighborIndex.y : neighborIndex.y);
        
        uint2 bucketPos = (uint2) neighborIndex / 8;
        uint bucketIndex = bucketPos.x + bucketPos.y * globalIlumBucketSizeX;
        uint2 bucketInfo = globalIlumBucketInfo[bucketIndex];
            
        
        uint firstHitNormal = (firstHit.normalTang & 0x7) - 1;
        uint normalMask = bucketInfo.x & 0x3F;
        if ((normalMask & (1 << firstHitNormal)) == 0)
            continue;
        
        float bucketMinDist = f16tof32(bucketInfo.y & 0xFFFF);
        float bucketMaxDist = f16tof32(bucketInfo.y >> 16);
        float distFac = 0.95f * max(0.2f, pow(max(dot(firstHit.normal, -firstHit.rayDir), 0), 0.25f));
        
        if (firstHit.dist < bucketMinDist * distFac || firstHit.dist > bucketMaxDist / distFac)
            continue;
        
        uint bucketValidStored = (bucketInfo.x >> 6) & 0x7;
        float bucketLitRatio = f16tof32((bucketInfo.x >> 9) & 0x7FFF);
        
        float distRadiusFac = 0.4f * (1.0f / (1.0f + firstHit.dist * 0.002f));

        radius *= 0.8f;
        radius *= 1.0f - min(0.3f, lerp(bucketLitRatioCur, bucketLitRatio, 0.25f) * 10) * 0.4f + (1.0f - bucketValidStored * 0.125f) * 0.2f;
        
        if (bucketValidStored == 0)
        {
            sumSamplesSky += 0.5f;
            sumSamplesNoSky += 0.5f;
            continue;
        }
        
        uint randSampleIndex = bucketValidStored * nextRand(rand);
        uint4 neighborRes = globalIlumValidSamplesCompressed[bucketIndex * 8 + randSampleIndex];
        
        int3 neighborVisiblePosInt;
        float3 neighborVisiblePosFrac, neighborFirstBounceDir, neighborSampleNormal;
        bool isDiffuse;
        float neighborFirstBounceDist;
        getSampleData(neighborRes, neighborVisiblePosInt, neighborVisiblePosFrac, neighborFirstBounceDir, neighborFirstBounceDist, neighborSampleNormal, isDiffuse);
        
        float bucketMinSampleDist = 100000.0f * pow(2, ((bucketInfo.x >> 24) - 255.0f) / 12.0f);
        if (neighborFirstBounceDist > bucketMinSampleDist * 2)
            continue;
        
        // Different material
        if (firstHitIsDiffuse != isDiffuse)
            continue;
            
        bool isSky = neighborFirstBounceDist == 0;
        if (isSky)
            sampleCountSky++;
        else
            sampleCountNoSky++;
        
        float3 pathToSampleNeighbor = neighborFirstBounceDir * neighborFirstBounceDist;
        float3 pathToSampleNowFrac = (neighborVisiblePosFrac + pathToSampleNeighbor) - firstHit.pos;
        float3 pathToSampleNow = (neighborVisiblePosInt - camPosInt) + pathToSampleNowFrac;
            
        float lengthToSampleSquaredNow = dot(pathToSampleNow, pathToSampleNow);
        float lengthToSampleSquaredNeighbor = dot(pathToSampleNeighbor, pathToSampleNeighbor);
            
        float3 dirToSampleNow = pathToSampleNow * rsqrt(lengthToSampleSquaredNow);
        float3 dirToSampleNowOrSun = isSky ? neighborFirstBounceDir : dirToSampleNow;
        float cosTheta = dot(firstHit.normal, dirToSampleNowOrSun);
        
        if (cosTheta < 0.0001f)
            continue;
        
        
        float pdfNow = (firstHitIsDiffuse ? (1.0f / (2.0f * PI)) : pdf_vndf_isotropic(dirToSampleNowOrSun, -firstHit.rayDir, firstHitType.roughness, firstHit.normal));
        float pdfThen = (firstHitIsDiffuse ? (1.0f / (2.0f * PI)) : pdf_vndf_isotropic(neighborFirstBounceDir, normalize((camPosInt - neighborVisiblePosInt) + (camPosFrac - neighborVisiblePosFrac)), firstHitType.roughness, firstHit.normal));
        float pdfRatio = pdfNow / pdfThen;
        
        if (pdfRatio < 0.25f || pdfRatio > 2.0f || pdfThen < 0.01f)
            continue;
        
        
        // Calculate jacobian to compensate for the spatial differences
        float jacobianNow = dot(neighborSampleNormal, dirToSampleNow) * lengthToSampleSquaredNeighbor;
        float jacobianNeighbor = dot(neighborSampleNormal, neighborFirstBounceDir) * lengthToSampleSquaredNow;
        float jacobianRaw = jacobianNow / (0.00000001f + jacobianNeighbor);
        float jacobian = clamp(jacobianRaw, 0, 4);
        
        
        if (isSky)
            jacobian = 1;
        
        if (jacobian > 2.5f || jacobian < 0.3f)
            continue;
        
        // Compute base color of sample
        uint compColor = neighborRes.x & 0x7FFF;
        float3 neighborColor = float3(COLORS[compColor & 0x1F], COLORS[(compColor >> 5) & 0x1F], COLORS[compColor >> 10]);
        neighborColor *= bucketLitRatio;
        
        float3 brdfNeighbor = firstHitIsDiffuse ? 1 : getBRDF(firstHitType.roughness, firstHitType.colorBase, firstHit.normal, dirToSampleNowOrSun, -firstHit.rayDir);
        float targetFunctionNeighbor = getTargetFunctionNew(dirToSampleNowOrSun, firstHit.normal, neighborColor, brdfNeighbor);
        float weight = max(0, (1.0f / pdfThen) * targetFunctionNeighbor * jacobian);
            
        sumWeight += weight;
        if (isSky)
            sumSamplesSky++;
        else
            sumSamplesNoSky++;
        
    
        bool isUpdate = nextRand(rand) * sumWeight < weight;
        if (isUpdate)
        {
            selectedColor = neighborColor * 1;
            selectedPSum = weight;
            selectedRayDir = dirToSampleNowOrSun;
            selectedLengthToSampleSquaredNow = lengthToSampleSquaredNow;
            selectedIsSky = isSky;
            selectedBounceState = neighborRes.z >> 30;
            selectedJacobian = jacobian;

        }
        radius /= 0.7f;
    }

    // Check for visibility
    float totalHitLength = 0;
    bool isHit;
    int3 curTestPosInt = firstHitPosInt;
    float3 curTestPosFrac = firstHitPosFrac;
    float3 curTestRayDir = selectedRayDir;
    for (i = 0; i < 3; ++i)
    {
        RayResult rayResult;
        isHit = shootRay(curTestPosInt, curTestPosFrac, curTestRayDir, MAX_RAY_STEPS_VISIBILITY, rayResult);
        if (!isHit || selectedIsSky)
            break;
            
        totalHitLength += rayResult.length;
        VoxelType curVoxelType = decompressVoxelType(voxelTypeData[rayResult.type]);
        curTestPosFrac += curTestRayDir * rayResult.length + rayResult.normal * 0.01f;
        curTestPosInt += floor(curTestPosFrac);
        curTestPosFrac = curTestPosFrac - floor(curTestPosFrac);
        
        uint curBounceState = (selectedBounceState >> i) & 0x1;
        if (curBounceState == 0)
            break;
                
        bool hasSpecular = curVoxelType.materialBase == SURFACE_SPECULAR_MIRROR || curVoxelType.materialLayer == SURFACE_SPECULAR_MIRROR;
        if (!hasSpecular)
            break;
        
        curTestRayDir = reflect(curTestRayDir, rayResult.normal);
    }
    totalHitLength += 0.15f;
    totalHitLength *= 1.04f;
    bool isVisible = totalHitLength * totalHitLength - selectedLengthToSampleSquaredNow >= 0;
        
    if (selectedIsSky)
        isVisible = !isHit;
    
    if (!isVisible)
        sumWeight = 0;
    
    float sumSamples = sumSamplesSky + sumSamplesNoSky;
    float3 brdf = firstHitIsDiffuse ? 1 : getBRDF(firstHitType.roughness, firstHitType.colorBase, firstHit.normal, selectedRayDir, -firstHit.rayDir);
    float targetFunctionNew = getTargetFunctionNew(selectedRayDir, firstHit.normal, selectedColor, brdf);
    float averageWeightNew = sumWeight / max(0.0000000000001f, sumSamples * targetFunctionNew);
    float3 color = averageWeightNew * selectedColor;
    if (!firstHitIsDiffuse)
    {
        float GI = geometryTerm(firstHitType.roughness, dot(selectedRayDir, firstHit.normal));
        float GO = geometryTerm(firstHitType.roughness, dot(-firstHit.rayDir, firstHit.normal));
        float D = pow(firstHitType.roughness, 2) / (PI * pow(pow(dot(firstHit.normal, normalize(selectedRayDir + -firstHit.rayDir)), 2) * (pow(firstHitType.roughness, 2) - 1) + 1, 2));
        float3 F = getReflectanceFresnel(firstHitType.colorBase, dot(selectedRayDir, firstHit.normal));
        color *= (D * GI * GO * F) / (4 * dot(-firstHit.rayDir, firstHit.normal));
    }
    else
    {
        color *= saturate(dot(firstHit.normal, selectedRayDir)) * (1.0f / PI);
    }
    
    
    // Boost color based on resampling difficulty.
    // Sky samples are inverse boosted as they tend to dominate in difficult areas.
    if (colorCorrection)
    {
        float skyValidRatio = sampleCount / max(1, sampleCountSky);
        float noSkyValidRatio = sampleCount / max(1, sampleCountNoSky);
        float boostSky = 1.0f + (2.0f / max(1, sumSamplesSky * skyValidRatio));
        float boostNoSky = 1.0f + 4.0f * (1.0f / max(1, sumSamplesNoSky * noSkyValidRatio));
        if (selectedIsSky)
            color *= boostSky;
        else
            color *= boostNoSky;
    }

    // Sample the sun
    float3 sunDirRand = getUniformHemisphereSample(float2(nextRand(rand), nextRand(rand)), skySunDir, 0.9999f);
    RayResult temp;
    bool isSunBlocked = shootRay(firstHitPosInt, firstHitPosFrac, sunDirRand, MAX_RAY_STEPS_SUN, temp);
    float sunDirCosTheta = saturate(dot(sunDirRand, firstHit.normal));
    if (!isSunBlocked && firstHit.normalTang != HIT_NOTHING && sunDirCosTheta > 0.001f)
    {
        float3 weight = 2.0f * sunDirCosTheta;
        
        if (firstHitType.materialBase == 2)
        {
            float GI = geometryTerm(firstHitType.roughness, sunDirCosTheta);
            float GO = geometryTerm(firstHitType.roughness, dot(-firstHit.rayDir, firstHit.normal));
            float D = pow(firstHitType.roughness, 2) / (PI * pow(pow(dot(firstHit.normal, normalize(sunDirRand + -firstHit.rayDir)), 2) * (pow(firstHitType.roughness, 2) - 1) + 1, 2));
            float3 F = getReflectanceFresnel(firstHitType.colorBase, sunDirCosTheta);
            weight *= (0.5f * D * GI * GO * F) / (4 * sunDirCosTheta * dot(-firstHit.rayDir, firstHit.normal));
        }
        color += sunColor * weight;
    }
    
    return color;
}

[numthreads(64, 1, 1)]
void calcSpatialResampling(uint3 globalID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex)
{
    int3 camPosInt = int3(camPosIntX, camPosIntY, camPosIntZ);
    
    uint2 pixelPos = uint2(globalID.x % screenWidth, globalID.x / screenWidth);
    
    float3 rayDir = getRayDir(invCamMatrix, pixelPos, screenWidth, screenHeight, taaJitter);
    
    uint4 firstHit = firstHitData[pixelPos.x + pixelPos.y * screenWidth];
    
    float3 color = float3(0, 0, 0);
    FirstHitResult firstHitResult = getHitDataFromPlanes(entityInstancesHistory, taaIndex, firstHit, camPosInt, camPosFrac, rayDir);
    uint firstHitTypeIndex = firstHit.z & 0x7FFF;
    if (firstHitResult.normalTang != HIT_NOTHING)
        color = sampleNeighbors(pixelPos, 12, firstHitResult, firstHitTypeIndex);
    
    uint2 absorptionComp = firstHitAbsorption[globalID.x];
    float3 absorption = float3(f16tof32(absorptionComp.x & 0xFFFF), f16tof32(absorptionComp.x >> 16), f16tof32(absorptionComp.y));
    color = min(color, COLORS[26]);
    
    if (isDenoise)
    {
        VoxelType firstHitType = decompressVoxelType(voxelTypeData[firstHitTypeIndex]);
        uint firstHitIsDiffuse = firstHitType.materialBase != SURFACE_SPECULAR_ROUGH;
        uint screenIndexWithType = globalID.x;
        
        uint2 curTaaSample = taaSampleAccum[screenIndexWithType];
        float3 curTaaColor = float3(0, 0, 0);
        float accum = f16tof32(curTaaSample.x & 0xFFFF);
        if (accum <= 1)
            curTaaColor = color;
        else
        {
            curTaaColor = float3(f16tof32(curTaaSample.x >> 16), f16tof32(curTaaSample.y & 0xFFFF), f16tof32(curTaaSample.y >> 16));
            curTaaColor /= accum * dot(absorption, float3(1, 1, 1)) + 0.01f;
        }

        uint2 curColorComp = uint2(f32tof16(color.x) | (f32tof16(color.y) << 16), f32tof16(color.z));
        
        uint3 final = uint3(0, 0, 0);
        final.x = curColorComp.x;
        final.y = (curColorComp.y & 0xFFFF) | (f32tof16(dot(curTaaColor, float3(1, 1, 1))) << 16);
        uint type = firstHitIsDiffuse ? 0 : (firstHitTypeIndex & 0xFFF) + 1;
        final.z = firstHitResult.normalTang | (type << 23);
        denoisePreprocessed[pixelPos.y + pixelPos.x * screenHeight] = final;
    }
    else
    {
        uint2 finalColComp = finalColor[globalID.x];
        float3 finalCol = float3(f16tof32(finalColComp.x & 0xFFFF), f16tof32(finalColComp.x >> 16), f16tof32(finalColComp.y));
        finalCol += absorption * color;
        //finalCol = finalCol * 0.000001f + ((curBucketInfo.x >> 16) / (1 + 0.001f)) * 0.01f;
        finalCol = min(finalCol, COLORS[26]);
        finalColor[globalID.x] = uint2(f32tof16(finalCol.x) | (f32tof16(finalCol.y) << 16), f32tof16(finalCol.z));
    }
}

technique MainTechnique
{
    pass SpatialResampling
    {
        ComputeShader = compile cs_5_0 calcSpatialResampling();
    }
};