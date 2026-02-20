#ifndef __COMMON_RENDER_PIPELINE__
#define __COMMON_RENDER_PIPELINE__

#include "commonConstants.fxh"
#include "commonEntities.fxh"

#define HIT_NOTHING 0x1FFFF
#define HIT_UNDEFINED 0

#define ENTITY_FREE 0x3FFF

#define SURFACE_DIFFUSE 0
#define SURFACE_EMISSIVE 1
#define SURFACE_SPECULAR_ROUGH 2
#define SURFACE_SPECULAR_MIRROR 3

struct VoxelType
{
    uint materialBase;
    uint materialLayer;
    float3 colorBase;
    float3 colorLayer;
    float roughness;
};

VoxelType decompressVoxelType(uint4 comp)
{
    VoxelType type;
    type.materialBase = comp.x & 0x3;
    type.materialLayer = (comp.x >> 2) & 0x3;
    type.colorBase = float3(f16tof32(comp.y & 0xFFFF), f16tof32(comp.y >> 16), f16tof32(comp.z & 0xFFFF));
    type.colorLayer = float3(f16tof32(comp.z >> 16), f16tof32(comp.w & 0xFFFF), f16tof32(comp.w >> 16));
    type.roughness = f16tof32(comp.x >> 16);
    return type;

}

struct SampleValid
{
    uint4 data1;
    uint4 data2;
};

struct FirstHitResult
{
    float3 pos, normal, normalMirrorFac;
    float dist;
    uint normalTang;
    float3 rayDir;
};

static const float3 NORMAL[8] =
{
    float3(0, 0, 0),
    float3(-1, 0, 0),
    float3(1, 0, 0),
    float3(0, -1, 0),
    float3(0, 1, 0),
    float3(0, 0, -1),
    float3(0, 0, 1),
    float3(0, 0, 0)
};

static const float3 SPECULAR_MIRROR_FAC[7] =
{
    float3(1, 1, 1),
    float3(-1, 1, 1),
    float3(-1, 1, 1),
    float3(1, -1, 1),
    float3(1, -1, 1),
    float3(1, 1, -1),
    float3(1, 1, -1)
};

float3 getRayDir(matrix camTransform, uint2 pixelPos, uint screenWidth, uint screenHeight, float2 pixelOffset = float2(0, 0))
{
    float2 screenPos = (pixelPos + float2(0.5f, 0.5f) + pixelOffset) / float2(screenWidth, screenHeight);
    return normalize(mul(float4((screenPos * 2 - 1) * float2(1, -1), 1, 1), camTransform).xyz);
}

float3 getReflectanceFresnel(float3 ior, float cosTheta)
{
    float3 R0 = pow((1 - ior) / (1 + ior), 2);
    return R0 + (1 - R0) * pow(1.0f - cosTheta, 5);
}

float4 quaternionMul(float4 q1, float4 q2)
{
    return float4(
        q1.w * q2.xyz + q2.w * q1.xyz + cross(q1.xyz, q2.xyz),
        q1.w * q2.w - dot(q1.xyz, q2.xyz)
    );
}

float3 applyRotation(float3 vec, float4 q)
{
    float w1 = -dot(vec, -q.xyz);
    float3 xyz1 = q.w * vec + cross(vec, -q.xyz);
    return q.w * xyz1 + w1 * q.xyz + cross(q.xyz, xyz1);
}

float4 quaternionInverse(float4 q)
{
    return float4(-q.x, -q.y, -q.z, q.w) / (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
}

uint getSpecularNormals(uint4 hit)
{
    uint normals = 0;
    for (int i = 0; i < 3; ++i)
    {
        uint nextNormalTang = hit[i + 1] >> 15;
        if (nextNormalTang != 0)
            normals |= ((hit[i] >> 15) & 0x7) << (i * 3);
    }
    return normals;
}

uint getTang(uint4 firstHit)
{
    uint normalTang = 0;
    for (int i = 0; i < 4; ++i)
    {
        uint newNormalTang = firstHit[i] >> 15;
        if (newNormalTang != 0)
            normalTang = newNormalTang;

    }
    return normalTang;
    
}

bool getScreenPosProjection(uint screenWidth, uint screenHeight, float3 pos, matrix transformation, out float2 screenPos)
{
    float4 screenProjection = mul(float4(pos, 1), transformation);
    float3 ndc = screenProjection.xyz / screenProjection.w;
    if (ndc.x < -1.0f || ndc.x > 1.0f || ndc.y < -1.0f || ndc.y > 1.0f || ndc.z < 0 || ndc.z > 1.0f)
        return false;
    ndc.y *= -1;
    float2 ndc01 = (ndc.xy + 1.0f) * 0.5f;
    screenPos = ndc01 * float2(screenWidth, screenHeight);
    return true;
}

bool getScreenIndexProjection(uint screenWidth, uint screenHeight, float3 pos, matrix transformation, out uint screenIndex, float2 pixelOffset = float2(0, 0))
{
    float2 screenPos;
    bool valid = getScreenPosProjection(screenWidth, screenHeight, pos, transformation, screenPos);
    int2 screenPosInt = clamp(screenPos + pixelOffset, 0, int2(screenWidth - 1, screenHeight - 1));
    screenIndex = screenPosInt.x + screenPosInt.y * screenWidth;
    return valid;
}

FirstHitResult getHitDataFromPlanes(const StructuredBuffer<uint4> entityInstancesHistory, uint taaIndex, uint4 firstHit, int3 camPosInt, float3 camPosFrac, float3 rayDir)
{
    FirstHitResult firstHitResult;
    firstHitResult.normal = 0;
    firstHitResult.normalTang = firstHit.x >> 15;
    firstHitResult.pos = camPosFrac;
    firstHitResult.dist = 0;
    firstHitResult.normalMirrorFac = float3(1, 1, 1);
    firstHitResult.rayDir = rayDir;
    
    for (uint i = 0; i < 3; ++i)
    {
        uint nextNormalTang = firstHit[i + 1] >> 15;
        if (nextNormalTang == HIT_UNDEFINED)
            break;
        
        // Apply reflection
        firstHitResult.normalMirrorFac *= SPECULAR_MIRROR_FAC[firstHitResult.normalTang & 0x7];
        firstHitResult.normal = NORMAL[firstHitResult.normalTang & 0x7];
        float rayDirCompForNormal = abs(dot(firstHitResult.rayDir, firstHitResult.normal));
        float distToTang = abs(dot(firstHitResult.pos, abs(firstHitResult.normal)) - (float) ((firstHitResult.normalTang >> 3) - dot(camPosInt, abs(firstHitResult.normal))));
        float distFac = distToTang / rayDirCompForNormal;
        firstHitResult.dist += distFac;
        firstHitResult.pos += firstHitResult.rayDir * distFac + firstHitResult.normal * 0.01f;
        firstHitResult.rayDir = reflect(firstHitResult.rayDir, firstHitResult.normal);
        firstHitResult.normalTang = nextNormalTang;
        
    }
    
#ifdef ENTITIES
    uint entity = firstHit.x & 0x3FFF;
    if (entity != 0x3FFF) // TODO cam pos split
    {
        EntityInstance entityInstance = decompressEntityInstanceFromHistory(entityInstancesHistory[taaIndex * 16384 + entity]);
        float4 inverseRotation = quaternionInverse(entityInstance.quaternion);
        
        float3 rayOriginEntity = firstHitResult.pos - (entityInstance.position - camPosInt);
        rayOriginEntity = applyRotation(rayOriginEntity, inverseRotation);
        float3 rayDirEntity = applyRotation(firstHitResult.rayDir, inverseRotation);
        
        firstHitResult.normal = NORMAL[firstHitResult.normalTang & 0x7];
        float rayDirCompForNormal = abs(dot(rayDirEntity, firstHitResult.normal));
        float distToTang = abs(dot(rayOriginEntity, abs(firstHitResult.normal)) - (firstHitResult.normalTang >> 3));
        float distFac = distToTang / rayDirCompForNormal;
        firstHitResult.dist += distFac;
        firstHitResult.pos += firstHitResult.rayDir * distFac;
        firstHitResult.normal = applyRotation(firstHitResult.normal, entityInstance.quaternion);
        return firstHitResult;
    }
#endif // ENTITIES
    
    firstHitResult.normal = NORMAL[firstHitResult.normalTang & 0x7];
    float rayDirCompForNormal = abs(dot(firstHitResult.rayDir, firstHitResult.normal));
    float distToTang = abs(dot(firstHitResult.pos, abs(firstHitResult.normal)) - (float) ((firstHitResult.normalTang >> 3) - dot(camPosInt, abs(firstHitResult.normal))));
    float distFac = distToTang / rayDirCompForNormal;
    firstHitResult.dist += distFac;
    firstHitResult.pos += firstHitResult.rayDir * distFac;
    return firstHitResult;

}



FirstHitResult getHitDataFromPlanes(const StructuredBuffer<uint4> entityInstancesHistory, uint taaIndex, uint4 firstHit, float3 camPos, float3 rayDir)
{
    return getHitDataFromPlanes(entityInstancesHistory, taaIndex, firstHit, trunc(camPos), frac(camPos), rayDir);
}

#endif // __COMMON_RENDER_PIPELINE__