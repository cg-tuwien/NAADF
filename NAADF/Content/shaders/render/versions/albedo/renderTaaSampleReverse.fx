#include "../../common/common.fxh"
#include "../../common/taa/commonTaa.fxh"
#include "../../rayTracing.fxh"

StructuredBuffer<uint4> firstHitData;

RWStructuredBuffer<uint2> taaSamples;
RWStructuredBuffer<uint2> taaSampleAccum;

float3 camPosFrac;
int camPosIntX, camPosIntY, camPosIntZ;

matrix invCamMatrix, camMatrix;
matrix camRotOld[64];
float3 taaOldCamPosFromCurCamInt[64];
float2 taaJitterOld[64];

float2 taaJitter;
uint screenWidth, screenHeight, frameCount, sampleAge;
uint taaFinalIndex, taaFinalIndexTest, taaIndex;

[numthreads(64, 1, 1)]
void reprojectOldSamples(uint3 globalID : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex)
{
    int3 camPosInt = int3(camPosIntX, camPosIntY, camPosIntZ);
    if (globalID.x >= screenWidth * screenHeight)
        return;
    
    uint2 pixelPos = uint2(globalID.x % screenWidth, globalID.x / screenWidth);
    float3 rayDir = getRayDir(invCamMatrix, pixelPos, screenWidth, screenHeight);

    uint validHashesComp[4] = { 0, 0, 0, 0 };
    uint validHashCenter = 0, validNormalsSpec = 0;
    float2 distMinMax = float2(999999.9f, 0);
    
    float firstHitDist = 99999999;
    uint firstHitEntity = ENTITY_FREE;
    float3 firstHitPos = float3(0, 0, 0);
    float3 firstHitMirrorFac = float3(1, 1, 1);
    
    // Precompute 3x3 screen region
    for (uint i = 0; i < 9; ++i)
    {
        uint2 curPixelPos = pixelPos + neighborOffsets[i];
        uint4 curFirstHit = firstHitData[curPixelPos.x + curPixelPos.y * screenWidth];
        FirstHitResult curFirstHitResult = getHitDataFromPlanes(entityInstancesHistory, taaIndex, curFirstHit, camPosInt, camPosFrac, rayDir);
        
        uint curFirstHitSpecularNormals = getSpecularNormals(curFirstHit);
        uint curFirstHitEntity = curFirstHit.x & 0x3FFF;
        uint curFirstHitIsDiffuse = curFirstHit.y & 0x1;
        float curDist = f16tof32(curFirstHit.w & 0x7FFF);
        if ((curFirstHit.z & 0x7FFF) == 0)
            curDist = 65520;
        
        if (curDist < firstHitDist)
        {
            firstHitDist = curDist;
            firstHitEntity = curFirstHitEntity;
            firstHitPos = curFirstHitResult.pos;
            firstHitMirrorFac = curFirstHitResult.normalMirrorFac;
        }

        distMinMax.x = min(distMinMax.x, curDist);
        distMinMax.y = max(distMinMax.y, curDist);
        validNormalsSpec |= (1 << (curFirstHitSpecularNormals & 0x7));
        validNormalsSpec |= (1 << ((curFirstHitSpecularNormals >> 3) & 0x7)) << 7;
        validNormalsSpec |= (1 << ((curFirstHitSpecularNormals >> 6) & 0x7)) << 14;
        
        uint curHash = getHashFromData(curFirstHitIsDiffuse, curFirstHitSpecularNormals, curFirstHitEntity) & 0xFFFF;
        if (i == 0)
            validHashCenter = curHash;
        else
            validHashesComp[(i - 1) / 2] |= curHash << (16 * ((i - 1) % 2));

    }
#ifdef ENTITIES
    float3 posForEntity = float3(0, 0, 0);
    if (firstHitEntity != ENTITY_FREE)
    {
        EntityInstance entityInstance = decompressEntityInstanceFromHistory(entityInstancesHistory[taaIndex * 16384 + firstHitEntity]);
        posForEntity = firstHitPos - (entityInstance.position - camPosInt);
        posForEntity = applyRotation(posForEntity, quaternionInverse(entityInstance.quaternion));
    }
#endif // ENTITIES
    
    float3 posVirtual = rayDir * firstHitDist;
    float4 colorSum = float4(0, 0, 0, 0);
    for (i = 1; i < sampleAge; ++i)
    {
        uint curHistoryIndex = (taaIndex + i) % 64;
        uint curTaaIndex = (taaIndex + i) % 32;
        
        // Get virtual position for reprojection
        float3 curPosVirtual = posVirtual;
        float3 entityPosChange = float3(0, 0, 0);
#ifdef ENTITIES
        if (firstHitEntity != ENTITY_FREE)
        {
            EntityInstance entityInstanceOld = decompressEntityInstanceFromHistory(entityInstancesHistory[curHistoryIndex * 16384 + firstHitEntity]);
            float3 posForEntityOld = applyRotation(posForEntity, entityInstanceOld.quaternion) + (entityInstanceOld.position - camPosInt);
            entityPosChange = (posForEntityOld - firstHitPos) * firstHitMirrorFac;
            curPosVirtual += entityPosChange;
        }
#endif // ENTITIES
        
        // Apply reprojection to get past texture coordinate
        float2 curTaaJitter = taaJitterOld[curHistoryIndex];
        uint screenIndex;
        if (!getScreenIndexProjection(screenWidth, screenHeight, curPosVirtual - taaOldCamPosFromCurCamInt[curHistoryIndex], camRotOld[curHistoryIndex], screenIndex, -curTaaJitter))
            continue;
        
        // Fetch and decompress past sample
        uint2 curSamp = taaSamples[screenIndex + curTaaIndex * screenWidth * screenHeight];
        float sampleDist;
        float4 color;
        uint hash, extraData, normalComp;
        decompressSample(curSamp, sampleDist, color, normalComp, extraData, hash);
        
        // Verify past sample using distance check
        float3 rayDirOld = normalize(curPosVirtual - taaOldCamPosFromCurCamInt[curHistoryIndex]);
        float3 oldVirtualPos = taaOldCamPosFromCurCamInt[curHistoryIndex] + rayDirOld * sampleDist - entityPosChange;
        float distCur = distance(oldVirtualPos, 0);
        if (distCur < distMinMax.x * (1022.0f / 1024.0f) || distCur > distMinMax.y * (1026.0f / 1024.0f) || sampleDist > distMinMax.y * 2)
            continue;
        
        // Project past sample into current camera to ensure screen position similarity
        float4 screenProjectionNew = mul(float4(oldVirtualPos, 1), camMatrix);
        float3 ndcNew = screenProjectionNew.xyz / screenProjectionNew.w;
        ndcNew.y *= -1;
        float2 ndc01New = mad(ndcNew.xy, 0.5f, 0.5f);
        float2 screenPosNew = ndc01New * float2(screenWidth, screenHeight);
        float2 screenPosDif = screenPosNew - pixelPos;
        float screenPosDistanceSqr = dot(screenPosDif, screenPosDif);
        if (screenPosDistanceSqr > 1.0f)
            continue;
        
        // Check if rough specular reflection is valid
        if (extraData != 0)
        {
            float3 normal = NORMAL[normalComp];
            float roughness = pow(extraData / 31.0f, 2);
            float pdfOld = pdf_vndf_isotropic(reflect(rayDirOld, normal), -rayDirOld, roughness, normal);
            float pdfNow = pdf_vndf_isotropic(reflect(rayDirOld, normal), -rayDir, roughness, normal);
            float fac = pow(clamp(pdfNow / pdfOld, 0, 1), 2);
            if (fac < 0.01f)
                continue;
            color *= fac;
        }
        
        // Check if any of the current 3x3 hashes matches with the past sample
        if (hash != validHashCenter)
        {
            bool isHashValid = false;
            for (uint h = 0; h < 8; ++h)
                isHashValid = isHashValid || hash == ((validHashesComp[h / 2] >> (16 * (h % 2))) & 0xFFFF);
            if (!isHashValid)
                continue;
        }
        
        colorSum += color;
    }
    
    uint2 taaColorComp = taaSampleAccum[globalID.x];
    float sampleWeight = f16tof32(taaColorComp.x & 0xFFFF);
    float3 taaColor = float3(f16tof32(taaColorComp.x >> 16), f16tof32(taaColorComp.y & 0xFFFF), f16tof32(taaColorComp.y >> 16));
    taaColor += colorSum.rgb;
    
    uint2 newColorComp = uint2(0, 0);
    newColorComp.x = f32tof16(sampleWeight + colorSum.a) | (f32tof16(taaColor.r) << 16);
    newColorComp.y = f32tof16(taaColor.g) | (f32tof16(taaColor.b) << 16);
    taaSampleAccum[globalID.x] = newColorComp;
    
}

technique ComputeCalc
{
    pass ReprojectOld
    {
        ComputeShader = compile cs_5_0 reprojectOldSamples();
    }
};
