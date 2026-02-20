#ifndef __COMMON_RAY_TRACING__
#define __COMMON_RAY_TRACING__

#include "commonConstants.fxh"

// https://jcgt.org/published/0009/03/02/
uint pcg_hash(uint input)
{
    uint state = input * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

uint2 initRand(uint3 data)
{
    uint2 rng;
   
    uint seed_x = pcg_hash(data.x + pcg_hash(data.y + data.z));
    uint seed_y = pcg_hash(seed_x + data.z);

    rng.x = seed_x;
    rng.y = (seed_y == 0u) ? 0xa7e2bf31 : seed_y;
    
    return rng;
}

// xoroshiro64star 1.0 - 32-bit generator
uint rotl(uint x, uint k)
{
    return (x << k) | (x >> (32 - k));
}

uint xoroshiro64star(inout uint2 state)
{
    const uint s0 = state.x;
    uint s1 = state.y;
    const uint result = s0 * 0x9E3779BBU;
    s1 ^= s0;
    state.x = rotl(s0, 26) ^ s1 ^ (s1 << 9);
    state.y = rotl(s1, 13);
    return result;
}
uint2 xoroshiro64star2(inout uint2 state)
{
    const uint s0 = state.x;
    uint s1 = state.y;
    const uint2 result = uint2(s0 * 0x9E3779BBU, s1 * 0x9E3779BBU);
    s1 ^= s0;
    state.x = rotl(s0, 26) ^ s1 ^ (s1 << 9);
    state.y = rotl(s1, 13);
    return result;
}

float nextRand(inout uint2 state)
{
    return (float) xoroshiro64star(state) * 2.3283064365387e-10f;
}
float2 nextRand2(inout uint2 state)
{
    return (float2) xoroshiro64star2(state) * 2.3283064365387e-10f;
}


// From "Efficient Construction of Perpendicular Vectors Without Branching"
float3 getPerpendicularVector(float3 u)
{
    float3 a = abs(u);
    uint xm = ((a.x - a.y) < 0 && (a.x - a.z) < 0) ? 1 : 0;
    uint ym = (a.y - a.z) < 0 ? (1 ^ xm) : 0;
    uint zm = 1 ^ (xm | ym);
    return cross(u, float3(xm, ym, zm));
}

// Uniform hemisphere sampling for normal
float3 getUniformHemisphereSample(float2 rand, float3 hitNorm, float deviation = 0)
{
    float3 bitangent = getPerpendicularVector(hitNorm);
    float3 tangent = cross(bitangent, hitNorm);
    float z = deviation + (1.0f - deviation) * rand.x;
    float r = sqrt(1.0f - z * z);
    float phi = 2.0f * PI * rand.y;

    return normalize(tangent * (r * cos(phi)) + bitangent * (r * sin(phi)) + hitNorm.xyz * z);
}

// Importance sampling a VNDF (GGX-Smith) isotropic distribution
// https://community.intel.com/t5/Blogs/Tech-Innovation/Artificial-Intelligence-AI/VNDF-importance-sampling-for-an-isotropic-Smith-GGX-distribution/post/1599836
float3 sample_vndf_isotropic(float2 u, float3 wi, float alpha, float3 n)
{
    // decompose the floattor in parallel and perpendicular components
    float3 wi_z = -n * dot(wi, n);
    float3 wi_xy = wi + wi_z;

    // warp to the hemisphere configuration
    float3 wiStd = -normalize(alpha * wi_xy + wi_z);

    // sample a spherical cap in (-wiStd.z, 1]
    float wiStd_z = dot(wiStd, n);
    float z = 1.0 - u.y * (1.0 + wiStd_z);
    float sinTheta = sqrt(saturate(1.0f - z * z));
    float phi = 2 * PI * u.x - PI;
    float x = sinTheta * cos(phi);
    float y = sinTheta * sin(phi);
    float3 cStd = float3(x, y, z);

    // reflect sample to align with normal
    float3 up = float3(0, 0, 1.000001); // Used for the singularity
    float3 wr = n + up;
    float3 c = dot(wr, cStd) * wr / wr.z - cStd;

    // compute halfway direction as standard normal
    float3 wmStd = c + wiStd;
    float3 wmStd_z = n * dot(n, wmStd);
    float3 wmStd_xy = wmStd_z - wmStd;
     
    // return final normal
    return normalize(alpha * wmStd_xy + wmStd_z);
}

// https://auzaiffe.wordpress.com/2024/04/15/vndf-importance-sampling-an-isotropic-distribution/
float pdf_vndf_isotropic(float3 wo, float3 wi, float alpha, float3 n)
{
    float alphaSquare = alpha * alpha;
    float3 wm = normalize(wo + wi);
    float zm = dot(wm, n);
    float zi = dot(wi, n);
    float nrm = rsqrt((zi * zi) * (1.0f - alphaSquare) + alphaSquare);
    float sigmaStd = (zi * nrm) * 0.5f + 0.5f;
    float sigmaI = sigmaStd / nrm;
    float nrmN = (zm * zm) * (alphaSquare - 1.0f) + 1.0f;
    return alphaSquare / (PI * 4.0f * nrmN * nrmN * sigmaI);
}

float geometryTerm(float roughess, float cosTheta)
{
    return (2.0f * cosTheta) / (cosTheta + sqrt(roughess * roughess + (1 - roughess * roughess) * cosTheta * cosTheta));
}

float2 octWrap(float2 v)
{
    return (1.0 - abs(v.yx)) * (v.xy >= 0.0 ? 1.0 : -1.0);
}
 
// https://knarkowicz.wordpress.com/2014/04/16/octahedron-normal-vector-encoding/
float2 octEncode(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    n.xy = n.z >= 0.0 ? n.xy : octWrap(n.xy);
    n.xy = n.xy * 0.5 + 0.5;
    return n.xy;
}

// https://knarkowicz.wordpress.com/2014/04/16/octahedron-normal-vector-encoding/
float3 octDecode(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-n.z);
    n.xy += n.xy >= 0.0 ? -t : t;
    return normalize(n);
}

uint2 compressQuaternion(float4 q)
{
    int maxIndex = 0;
    float maxAbs = abs(q.x);
    if (abs(q.y) > maxAbs)
    {
        maxAbs = abs(q.y);
        maxIndex = 1;
    }
    if (abs(q.z) > maxAbs)
    {
        maxAbs = abs(q.z);
        maxIndex = 2;
    }
    if (abs(q.w) > maxAbs)
    {
        maxAbs = abs(q.w);
        maxIndex = 3;
    }

    // Store the smallest three
    float3 small;
    if (maxIndex == 0)
        small = float3(q.y, q.z, q.w);
    else if (maxIndex == 1)
        small = float3(q.x, q.z, q.w);
    else if (maxIndex == 2)
        small = float3(q.x, q.y, q.w);
    else
        small = float3(q.x, q.y, q.z);
    
    if (q[maxIndex] < 0)
        small = -small;

    int3 smallInt = clamp((int3)((small * 1 + 1.0f) * 8192 + 0.5f), 0, 16384);

    
    return uint2(smallInt.x | (smallInt.y << 14) | ((smallInt.z & 0xF) << 28), (smallInt.z >> 4) | ((maxIndex & 3) << 10));
}

float4 decompressQuaternion(uint2 packed)
{
    int maxIndex = (packed.y >> 10) & 0x3;
    int3 smallInt = int3(packed.x & 0x3FFF, (packed.x >> 14) & 0x3FFF, (packed.x >> 28) | ((packed.y & 0x3FF) << 4));
    float3 small = float3((smallInt.x - 8192) / (8192.0f * 1), (smallInt.y - 8192) / (8192.0f * 1), (smallInt.z - 8192) / (8192.0f * 1));

    float4 q;
    float missing = sqrt(max(0.0, 1.0 - dot(small, small)));

    if (maxIndex == 0)
        q = float4(missing, small.x, small.y, small.z);
    else if (maxIndex == 1)
        q = float4(small.x, missing, small.y, small.z);
    else if (maxIndex == 2)
        q = float4(small.x, small.y, missing, small.z);
    else
        q = float4(small.x, small.y, small.z, missing);
    
    return q;
}

#endif // __COMMON_RAY_TRACING__