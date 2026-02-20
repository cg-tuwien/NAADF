#include "../../common/common.fxh"


StructuredBuffer<uint4> firstHitData;

RWStructuredBuffer<uint2> globalIlumBucketInfo;
StructuredBuffer<SampleValid> globalIlumValidSamples;
RWStructuredBuffer<uint4> globalIlumValidSamplesRefined;
RWStructuredBuffer<uint4> globalIlumValidSamplesCompressed;
StructuredBuffer<uint4> globalIlumInvalidSamples;

RWStructuredBuffer<uint2> globalIlumSampleCounts;
StructuredBuffer<uint2> taaDistMinMax;

RWByteAddressBuffer globalIlumValidDispatch, globalIlumInvalidDispatch;
StructuredBuffer<uint4> entityInstancesHistory;

RWByteAddressBuffer groupCount;


float3 camPosFrac;
int camPosIntX, camPosIntY, camPosIntZ;
matrix camMatrix;
matrix camRotOld[64];
float3 taaOldCamPosFromCurCamInt[64];
uint sampleMaxAccum, validSampleStorageCount, invalidSampleStorageCount, bucketStorageCount, refinedBucketStorageCount, accumIndex;
uint randCounter, randCounter2, taaIndex;

uint screenWidth, screenHeight, globalIlumBucketSizeX, globalIlumBucketSizeY, globalIlumBucketCount;
bool isSampleLeveling;

[numthreads(64, 1, 1)]
void clearBucketsAndCalcMask(uint3 globalID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex)
{
    if (globalID.x == 0)
    {
        globalIlumSampleCounts[3 + accumIndex] = uint2(0, 0);
        globalIlumSampleCounts[2].x = 0;
        groupCount.Store(0, 0);
    }
    
    if (globalID.x >= globalIlumBucketCount)
        return;
    
    uint normalMask = 0;
    uint minTang = 16383, maxTang = 0;
    uint2 bucketPos = uint2(globalID.x % globalIlumBucketSizeX, globalID.x / globalIlumBucketSizeX);
    float minDist = 9999999, maxDist = 0;
    for (int y = 0; y < 8; ++y)
    {
        for (int x = 0; x < 8; ++x)
        {
            uint2 pixelPos = bucketPos * 8 + uint2(x, y);
            if (pixelPos.x >= screenWidth || pixelPos.y >= screenHeight)
                continue;
            uint4 firstHit = firstHitData[pixelPos.x + pixelPos.y * screenWidth];
            float dist = f16tof32(firstHit.w & 0x7FFF);
            minDist = min(minDist, dist);
            maxDist = max(maxDist, dist);
            
            uint normalTangComp = getTang(firstHit);
            uint normalComp = normalTangComp & 0x7;
            minTang = min(minTang, normalTangComp >> 3);
            maxTang = max(maxTang, normalTangComp >> 3);
            normalMask |= 1 << normalComp;
        }
    }
    globalIlumBucketInfo[globalID.x] = uint2((normalMask >> 1) & 0x3F, f32tof16(minDist) | (f32tof16(maxDist) << 16));
}

[numthreads(1, 1, 1)]
void computeValidHistory(uint3 globalID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex)
{
    uint2 maxSize = uint2(validSampleStorageCount, invalidSampleStorageCount) * screenWidth * screenHeight;
    uint2 totalCounts = int2(0, 0);
    
    for (uint i = 0; i < sampleMaxAccum; ++i)
    {
        uint2 nextCounts = globalIlumSampleCounts[3 + ((accumIndex + i) % sampleMaxAccum)];
        totalCounts += nextCounts;
        if (totalCounts.x > maxSize.x || totalCounts.y > maxSize.y)
        {
            totalCounts -= nextCounts;
            break;
        }
    }
    
    uint2 curSampleIndices = globalIlumSampleCounts[0];
    uint2 curSampleCounts = globalIlumSampleCounts[3 + accumIndex];
    curSampleIndices.x = (curSampleIndices.x + maxSize.x - curSampleCounts.x) % maxSize.x;
    curSampleIndices.y = (curSampleIndices.y + maxSize.y - curSampleCounts.y) % maxSize.y;
    globalIlumSampleCounts[0] = curSampleIndices;
    globalIlumSampleCounts[1] = totalCounts;
    globalIlumValidDispatch.Store(0, (totalCounts.x + 63) / 64);
    globalIlumInvalidDispatch.Store(0, (totalCounts.y + 63) / 64);
}

[numthreads(64, 1, 1)]
void countValidDataAndRefine(uint3 globalID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex)
{
    int3 camPosInt = int3(camPosIntX, camPosIntY, camPosIntZ);
    uint startIndex = globalIlumSampleCounts[0].x;
    uint totalCount = globalIlumSampleCounts[1].x;
    uint maxSize = validSampleStorageCount * screenWidth * screenHeight;
    if (globalID.x >= totalCount)
        return;
    
    SampleValid sample = globalIlumValidSamples[(startIndex + globalID.x) % maxSize];
    uint2 pixelPosOld = uint2(sample.data1.y & 0x7FFF, sample.data1.z & 0x7FFF);
    uint frameIndexOld = sample.data1.w & 0x3F;
    
    float3 rayDirOld = getRayDir(camRotOld[frameIndexOld], pixelPosOld, screenWidth, screenHeight);
    
    float3 camPosOldFrac = camPosFrac + taaOldCamPosFromCurCamInt[frameIndexOld];
    int3 camPosOldInt = camPosInt + floor(camPosOldFrac);
    camPosOldFrac = camPosOldFrac - floor(camPosOldFrac);
    
    FirstHitResult firstHitResult = getHitDataFromPlanes(entityInstancesHistory, frameIndexOld, sample.data1, camPosOldInt, camPosOldFrac, rayDirOld);
    uint surfaceRoughnessComp = (sample.data1.w >> 7) & 0xFF;
    float3 surfacePosVirtual = taaOldCamPosFromCurCamInt[frameIndexOld] + rayDirOld * firstHitResult.dist;
    uint specularNormalsOld = getSpecularNormals(sample.data1);
   
    
    float3 surfaceHitPosNew = (camPosOldInt - camPosInt) + firstHitResult.pos;
    
#ifdef ENTITIES
    uint surfaceEntity = sample.data1.x & 0x3FFF;
    if (surfaceEntity != 0x3FFF)
    {
        EntityInstance entityInstanceOld = decompressEntityInstanceFromHistory(entityInstancesHistory[frameIndexOld * 16384 + surfaceEntity]);
        float3 posInEntity = firstHitResult.pos - (entityInstanceOld.position - camPosOldInt);
        posInEntity = applyRotation(posInEntity, quaternionInverse(entityInstanceOld.quaternion));
        EntityInstance entityInstanceNew = decompressEntityInstanceFromHistory(entityInstancesHistory[taaIndex * 16384 + surfaceEntity]);
        surfaceHitPosNew = applyRotation(posInEntity, entityInstanceNew.quaternion) + (entityInstanceNew.position - camPosOldInt);
        surfacePosVirtual += (surfaceHitPosNew - firstHitResult.pos) * firstHitResult.normalMirrorFac;
        surfaceHitPosNew += (camPosOldInt - camPosInt);
    }
#endif // ENTITIES
    
    // Reproject
    uint materialState = surfaceRoughnessComp == 0 ? 1 : 0;
    
    float4 screenProjection = mul(float4(surfacePosVirtual, 1), camMatrix);
    float3 ndc = screenProjection.xyz / screenProjection.w;
    if (ndc.x < -1.0f || ndc.x > 1.0f || ndc.y < -1.0f || ndc.y > 1.0f || ndc.z < 0 || ndc.z > 1.0f)
        return;
    
    if (materialState == 0)
    {
        float roughness = surfaceRoughnessComp / 255.0f;
        float pdfOld = pdf_vndf_isotropic(reflect(rayDirOld, firstHitResult.normal), -rayDirOld, roughness, firstHitResult.normal);
        float3 rayDirNow = normalize(surfacePosVirtual);
        float pdfNow = pdf_vndf_isotropic(reflect(rayDirOld, firstHitResult.normal), -rayDirNow, roughness, firstHitResult.normal);
        float fac = clamp(pdfNow / pdfOld, 0, 1);
        if (fac < 0.1f)
            return;
    }
    
    ndc.y *= -1;
    float2 ndc01 = (ndc.xy + 1.0f) * 0.5f;
    
    int2 screenPosBucket = ndc01 * float2(globalIlumBucketSizeX, globalIlumBucketSizeY);
    int2 screenPos = ndc01 * float2(screenWidth, screenHeight);
    uint bucketIndex = screenPosBucket.x + screenPosBucket.y * globalIlumBucketSizeX;
    uint screenIndexWithType = screenPos.x + screenPos.y * screenWidth;
    
    uint2 minMax = taaDistMinMax[screenIndexWithType];
    float2 distMinMax = float2(f16tof32(minMax.x & 0xFFFF), f16tof32(minMax.x >> 16));
    float distCur = distance(surfacePosVirtual, 0);
    
    uint3 specularNormalsMask;
    specularNormalsMask.x = 1 << (specularNormalsOld & 0x7);
    specularNormalsMask.y = 1 << ((specularNormalsOld >> 3) & 0x7);
    specularNormalsMask.z = 1 << ((specularNormalsOld >> 6) & 0x7);
    uint3 validSpecularNormals = uint3(minMax.y & 0x7F, (minMax.y >> 7) & 0x7F, (minMax.y >> 14) & 0x7F);
    if (distCur < distMinMax.x * (1022.0f / 1024.0f) || distCur > distMinMax.y * (1026.0f / 1024.0f) || any((specularNormalsMask & validSpecularNormals) == 0))
        return;
    
    uint oldBucketValue;
    InterlockedAdd(globalIlumBucketInfo[bucketIndex].x, 1 << 6, oldBucketValue);
    uint oldBucketValid = (oldBucketValue >> 6) & 0x7FF;
    
    if (oldBucketValid < bucketStorageCount)
    {
        uint2 sampleDirComp = uint2(sample.data2.w >> 10, (sample.data2.w & 0x3FF) | ((sample.data2.z & 0xFFF) << 10));
        float3 sampleDir = octDecode(sampleDirComp / pow(2, 22));
        uint4 data2 = sample.data2;
        data2.w = 0;
        
        float3 firstHitPosFrac = firstHitResult.pos;
        int3 firstHitPosInt = camPosOldInt + floor(firstHitPosFrac);
        firstHitPosFrac = firstHitPosFrac - floor(firstHitPosFrac);
        
        FirstHitResult sampleResult = getHitDataFromPlanes(entityInstancesHistory, frameIndexOld, data2, firstHitPosInt, firstHitPosFrac, sampleDir);
        float3 samplePosVirtual = (camPosOldInt - camPosInt) + firstHitResult.pos + sampleDir * sampleResult.dist;
        
        if (sampleResult.normalTang == 0x1FFFF)
            sampleResult.dist = 0;
        
#ifdef ENTITIES
        uint sampleEntity = sample.data2.x & 0x3FFF;
        if (sampleEntity != 0x3FFF)
        {
            EntityInstance entityInstanceOld = decompressEntityInstanceFromHistory(entityInstancesHistory[frameIndexOld * 16384 + sampleEntity]);
            float3 posInEntity = sampleResult.pos - (entityInstanceOld.position - firstHitPosInt);
            posInEntity = applyRotation(posInEntity, quaternionInverse(entityInstanceOld.quaternion));
            EntityInstance entityInstanceNew = decompressEntityInstanceFromHistory(entityInstancesHistory[taaIndex * 16384 + sampleEntity]);
            float3 sampleHitPosNew = applyRotation(posInEntity, entityInstanceNew.quaternion) + (entityInstanceNew.position - firstHitPosInt);
            samplePosVirtual += (sampleHitPosNew - sampleResult.pos) * sampleResult.normalMirrorFac;
            sampleResult.normal = applyRotation(NORMAL[sampleResult.normalTang & 0x7], entityInstanceNew.quaternion);
        }
#endif // ENTITIES
        
        if (sampleResult.normalTang != 0x1FFFF)
        {
            sampleDir = samplePosVirtual - surfaceHitPosNew;
            sampleResult.dist = length(sampleDir);
            sampleDir = normalize(sampleDir);
        }
        
        uint4 refinedSample;
        float3 surfaceHitPosNewFrac = surfaceHitPosNew;
        int3 surfaceHitPosNewInt = camPosInt + floor(surfaceHitPosNewFrac);
        surfaceHitPosNewFrac = surfaceHitPosNewFrac - floor(surfaceHitPosNewFrac);
        uint3 surfacePosInt = surfaceHitPosNewInt * 32 + (uint3) (surfaceHitPosNewFrac * 32);
        float2 sampleNormalOct = octEncode(sampleResult.normal);
        uint2 sampleNormalComp = sampleNormalOct * 255.0f;
        float2 sampleDirOct = octEncode(sampleDir);
        uint2 sampleDirComp2 = sampleDirOct * 2048.0f;
        refinedSample.x = (sample.data2.y & 0x7FFF) | (surfacePosInt.y << 15);
        refinedSample.y = sampleNormalComp.x | (sampleNormalComp.y << 8) | (f32tof16(sampleResult.dist) << 16);
        refinedSample.z = sampleDirComp2.x | (surfacePosInt.x << 11) | (((sample.data2.y >> 15) != 0) << 30) | (((sample.data2.z >> 15) != 0) << 30) | (((sample.data2.x >> 14) & 0x1) << 31);
        refinedSample.w = sampleDirComp2.y | (surfacePosInt.z << 11) | (materialState << 30);
        globalIlumValidSamplesRefined[bucketIndex * bucketStorageCount + oldBucketValid] = refinedSample;

    }

}

[numthreads(64, 1, 1)]
void countInvalidData(uint3 globalID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex)
{
    int3 camPosInt = int3(camPosIntX, camPosIntY, camPosIntZ);
    uint startIndex = globalIlumSampleCounts[0].y;
    uint totalCount = globalIlumSampleCounts[1].y;
    uint maxSize = invalidSampleStorageCount * screenWidth * screenHeight;
    if (globalID.x >= totalCount)
        return;
    
    uint4 sample = globalIlumInvalidSamples[(startIndex + globalID.x) % maxSize];
    uint2 pixelPosOld = uint2(sample.y & 0x7FFF, sample.z & 0x7FFF);
    uint frameIndexOld = sample.w & 0x3F;
    
    float3 rayDirOld = getRayDir(camRotOld[frameIndexOld], pixelPosOld, screenWidth, screenHeight);
    
    float3 camPosOldFrac = camPosFrac + taaOldCamPosFromCurCamInt[frameIndexOld];
    int3 camPosOldInt = camPosInt + floor(camPosOldFrac);
    camPosOldFrac = camPosOldFrac - floor(camPosOldFrac);
    
    FirstHitResult firstHitResult = getHitDataFromPlanes(entityInstancesHistory, frameIndexOld, sample, camPosOldInt, camPosOldFrac, rayDirOld);
    uint surfaceRoughnessComp = (sample.w >> 7) & 0xFF;
    float3 surfacePosVirtual = taaOldCamPosFromCurCamInt[frameIndexOld] + rayDirOld * firstHitResult.dist;
    uint specularNormalsOld = getSpecularNormals(sample);
    
    #ifdef ENTITIES
    uint surfaceEntity = sample.x & 0x3FFF;
    if (surfaceEntity != 0x3FFF)
    {
        EntityInstance entityInstanceOld = decompressEntityInstanceFromHistory(entityInstancesHistory[frameIndexOld * 16384 + surfaceEntity]);
        float3 posInEntity = firstHitResult.pos - (entityInstanceOld.position - camPosOldInt);
        posInEntity = applyRotation(posInEntity, quaternionInverse(entityInstanceOld.quaternion));
        EntityInstance entityInstanceNew = decompressEntityInstanceFromHistory(entityInstancesHistory[taaIndex * 16384 + surfaceEntity]);
        float3 surfaceHitPosNew = applyRotation(posInEntity, entityInstanceNew.quaternion) + (entityInstanceNew.position - camPosOldInt);
        surfacePosVirtual += (surfaceHitPosNew - firstHitResult.pos) * firstHitResult.normalMirrorFac;
    }
    #endif // ENTITIES
    
    // Reproject
    uint materialState = surfaceRoughnessComp == 0 ? 1 : 0;
    
    float4 screenProjection = mul(float4(surfacePosVirtual, 1), camMatrix);
    float3 ndc = screenProjection.xyz / screenProjection.w;
    if (ndc.x < -1.0f || ndc.x > 1.0f || ndc.y < -1.0f || ndc.y > 1.0f || ndc.z < 0 || ndc.z > 1.0f)
        return;
    
    if (materialState == 0)
    {
        float roughness = surfaceRoughnessComp / 255.0f;
        float pdfOld = pdf_vndf_isotropic(reflect(rayDirOld, firstHitResult.normal), -rayDirOld, roughness, firstHitResult.normal);
        float3 rayDirNow = normalize(surfacePosVirtual);
        float pdfNow = pdf_vndf_isotropic(reflect(rayDirOld, firstHitResult.normal), -rayDirNow, roughness, firstHitResult.normal);
        float fac = clamp(pdfNow / pdfOld, 0, 1);
        if (fac < 0.1f)
            return;
    }
    
    ndc.y *= -1;
    float2 ndc01 = (ndc.xy + 1.0f) * 0.5f;
    
    int2 screenPosBucket = ndc01 * float2(globalIlumBucketSizeX, globalIlumBucketSizeY);
    int2 screenPos = ndc01 * float2(screenWidth, screenHeight);
    uint bucketIndex = screenPosBucket.x + screenPosBucket.y * globalIlumBucketSizeX;
    uint screenIndexWithType = screenPos.x + screenPos.y * screenWidth;

    uint2 minMax = taaDistMinMax[screenIndexWithType];
    float2 distMinMax = float2(f16tof32(minMax.x & 0xFFFF), f16tof32(minMax.x >> 16));
    float distCur = distance(surfacePosVirtual, 0);
    
    uint3 specularNormalsMask;
    specularNormalsMask.x = 1 << (specularNormalsOld & 0x7);
    specularNormalsMask.y = 1 << ((specularNormalsOld >> 3) & 0x7);
    specularNormalsMask.z = 1 << ((specularNormalsOld >> 6) & 0x7);
    uint3 validSpecularNormals = uint3(minMax.y & 0x7F, (minMax.y >> 7) & 0x7F, (minMax.y >> 14) & 0x7F);
    if (distCur < distMinMax.x * (1022.0f / 1024.0f) || distCur > distMinMax.y * (1026.0f / 1024.0f) || any((specularNormalsMask & validSpecularNormals) == 0))
        return;
    
    InterlockedAdd(globalIlumBucketInfo[bucketIndex].x, 1 << 17);
}

[numthreads(64, 1, 1)]
void refineBuckets(uint3 globalID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex)
{
    uint bucketIndex = globalID.x;
    if (globalID.x >= globalIlumBucketCount)
        return;
    
    uint2 rand = initRand(uint3(globalID.x, randCounter, randCounter2));
    
    uint2 curBucket = globalIlumBucketInfo[bucketIndex];
    uint validCount = (curBucket.x >> 6) & 0x7FF;
    uint invalidCount = (curBucket.x >> 17) * 8;
    int effectiveValidCount = min(validCount, bucketStorageCount - 1);
    float effectiveInvalidCount = invalidCount * (effectiveValidCount / (0.00000001f + validCount));
    if (validCount == 0)
        effectiveInvalidCount = invalidCount;
    
    uint samplesCompColorMax = 0;
    static uint compColorMaxStorage[32];

    for (int i = 0; i < effectiveValidCount; ++i)
    {
        uint4 curSample = globalIlumValidSamplesRefined[bucketIndex * bucketStorageCount + i];
        uint compColor = curSample.x & 0x7FFF;
        uint compColorMax = max(compColor & 0x1F, max((compColor >> 5) & 0x1F, (compColor >> 10) & 0x1F));
        samplesCompColorMax = max(samplesCompColorMax, compColorMax);
        compColorMaxStorage[i] = compColor | (compColorMax << 16);
    }

    uint curCompressedIndex = 0;
    int extraInvalidSamples = 0;
    float minBucketSampleDist = 100000;
    for (i = 0; i < effectiveValidCount; ++i)
    {
        uint curStorage = compColorMaxStorage[i];
        uint compColor = curStorage & 0xFFFF;
        uint compColorMax = curStorage >> 16;
        int maxColorDif = isSampleLeveling ? samplesCompColorMax - compColorMax : 0;
        float removeProb = COLOR_DIF_PROB[maxColorDif];
        if (removeProb > nextRand(rand))
            extraInvalidSamples++;
        else if (curCompressedIndex < 7)
        {
            uint4 curSample = globalIlumValidSamplesRefined[bucketIndex * bucketStorageCount + i];
            curSample.x &= 0xFFFF8000;
            compColor += maxColorDif | (maxColorDif << 5) | (maxColorDif << 10);
            curSample.x |= compColor;
            float curDist = f16tof32(curSample.y >> 16);
            if (curDist > 0 && (curSample.z >> 31))
                minBucketSampleDist = min(minBucketSampleDist, f16tof32(curSample.y >> 16));
            globalIlumValidSamplesCompressed[bucketIndex * refinedBucketStorageCount + curCompressedIndex++] = curSample;
        }
    }
    
    int newValidCount = effectiveValidCount - extraInvalidSamples;
    float newInvalidCountFloat = effectiveInvalidCount + extraInvalidSamples;
    int newInvalidCount = ((int) newInvalidCountFloat) + (frac(newInvalidCountFloat) > nextRand(rand) ? 1 : 0);
    float validInvalidRatio = newValidCount / (float) max(1, newValidCount + newInvalidCount);
    uint minBucketSampleDistComp = (log2(0.0401f + minBucketSampleDist) - log2(100000)) * 12 + 255.5f;
    if (newValidCount + newInvalidCount < 12)
        curCompressedIndex = 0;
    globalIlumBucketInfo[bucketIndex].x = (curBucket.x & 0x3F) | (curCompressedIndex << 6) | (f32tof16(validInvalidRatio) << 9) | (minBucketSampleDistComp << 24);
    
}

technique MainTechnique
{
    pass ClearBucketsAndCalcMask
    {
        ComputeShader = compile cs_5_0 clearBucketsAndCalcMask();
    }
    pass ValidHistory
    {
        ComputeShader = compile cs_5_0 computeValidHistory();
    }
    pass CountValidAndRefine
    {
        ComputeShader = compile cs_5_0 countValidDataAndRefine();
    }
    pass CountInvalid
    {
        ComputeShader = compile cs_5_0 countInvalidData();
    }
    pass RefineBuckets
    {
        ComputeShader = compile cs_5_0 refineBuckets();
    }
};
