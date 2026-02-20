#include "../common.fxh"

#ifndef __ATMOSPHERE_RAW__
#define __ATMOSPHERE_RAW__

float3 skySunDir;
float3 skyRayleighScatter;
float skyMieScatter;
float3 skyOzoneAbsorb;
float3 skySunColor;
float skySphereRadius;
float skyAtmosphereThickness;
float skyAtmosphereAveragePoint;
float skyAtmosphereDensity;
float skyAbsorbIntensity;
float skyScatterIntensity;
float skyMieFactor;
int skyMainRaySteps;
int skySubScatterSteps;

float rayleigh(float angle)
{
    return (3.0f / 4.0f) * ((1 + pow(angle, 2)));
}

float phaseFunction(float angle, float g)
{
    return ((3.0f * (1.0f - g * g)) / (2.0f * (2.0f + g * g))) * ((1 + angle * angle) / pow(abs(1 + g * g - 2 * g * angle), 3.0f / 2.0f));
}

float3 densityAtHeight(float height)
{
    float3 density;
    density.x = exp(-height / 0.3f) * (1 - height); //Rayleigh
    density.y = exp(-height / 0.2f) * (1 - height); //Mie
    density.z = max(0, 1 - (abs(height - 0.25f) / 0.15f)); //Ozone
    return density * skyAtmosphereDensity;
}

float2 raySphere(float3 sphereOrigin, float sphereRadius, float3 rayOrigin, float3 rayDir)
{
    float3 dif = rayOrigin - sphereOrigin;
    float a = 1;
    float b = 2 * dot(dif, rayDir);
    float c = dot(dif, dif) - sphereRadius * sphereRadius;
    float d = b * b - 4 * a * c;

    if (d > 0)
    {
        float s = sqrt(d);
        float dstNear = max(0, (-b - s) / (2 * a));
        float dstFar = (-b + s) / (2 * a);

        if (dstFar >= 0)
            return float2(dstNear, dstFar - dstNear);
    }
    return float2(0, 0);
}

float3 scatterForDensities(float3 densities)
{
    return (densities.x * skyRayleighScatter + densities.y * skyMieScatter + densities.z * skyOzoneAbsorb * 1) * skyScatterIntensity;
}


float3 getScatterDensitiesAtPoint(float3 pos, const int secondIterationCount)
{
    float earthWithAtmoRadius = skySphereRadius + skyAtmosphereThickness;

    float2 rayResult = raySphere(float3(0, 0, 0), earthWithAtmoRadius, pos, skySunDir);
    if (rayResult.y == 0)
        return 0;
    float scale = rayResult.y / secondIterationCount;

    float3 totalDensities;
    for (int i = secondIterationCount - 1; i >= 0; --i)
    {
        float factor = i / float(secondIterationCount);

        float3 curSamplePoint = pos + skySunDir * rayResult.x + skySunDir * rayResult.y * factor;
        float height = max(0, sqrt(dot(curSamplePoint, curSamplePoint)) - skySphereRadius) / skyAtmosphereThickness;
        float3 densities = densityAtHeight(height) * scale;
        totalDensities += densities;
    }
    return totalDensities;
}


void addLightForDirection(float3 pos, float3 dir, float maxLength, inout float3 absorption, inout float3 light, const bool includeMie, const int mainIterationCount, const int secondIterationCount, const bool includeSun = false)
{
    float earthWithAtmoRadius = skySphereRadius + skyAtmosphereThickness;

    float3 rayOrigin = pos + float3(0, skySphereRadius, 0);
    float2 rayResult = raySphere(float3(0, 0, 0), earthWithAtmoRadius, rayOrigin, dir);
    float2 rayResultPlanet = raySphere(float3(0, 0, 0), skySphereRadius, rayOrigin, dir);
    if (rayResult.y > 0)
    {
        if (rayResultPlanet.y > 0)
            rayResult.y = rayResultPlanet.x - rayResult.x;

        rayResult.y = min(rayResult.y, maxLength);

        float scale = rayResult.y / mainIterationCount;
        float angle = max(0, dot(dir, skySunDir));
        float3 radiance = 0;
        float3 totalDensities = float3(0, 0, 0);
        float3 testRayleigh = float3(0, 0, 0);
        float3 testMie = float3(0, 0, 0);
        
        float rayleighMul = rayleigh(angle);
        float mieMul = phaseFunction(angle, skyMieFactor);
        
        for (int i = 1; i <= mainIterationCount; ++i)
        {
            float factor = i / float(mainIterationCount);
            float3 curSamplePoint = rayOrigin + dir * rayResult.x + dir * rayResult.y * factor;
            float height = max(0, sqrt(dot(curSamplePoint, curSamplePoint)) - skySphereRadius) / skyAtmosphereThickness;
            float3 curDensity = densityAtHeight(height) * scale;
            totalDensities += curDensity;
            float3 densitiesScatter = getScatterDensitiesAtPoint(curSamplePoint, secondIterationCount);
            float3 Tscatter = exp(-scatterForDensities(densitiesScatter) * skyAbsorbIntensity);
            float3 scatterRadiance = Tscatter * skySunColor;
            
            float3 Tprimary = exp(-scatterForDensities(totalDensities) * skyAbsorbIntensity);
            radiance += rayleighMul * scatterRadiance * Tprimary * curDensity.x * skyRayleighScatter * skyScatterIntensity;
            if (includeMie)
                radiance += mieMul * scatterRadiance * Tprimary * curDensity.x * skyMieScatter * skyScatterIntensity;
        }
        
        light += radiance;
        absorption *= exp(-scatterForDensities(totalDensities) * skyAbsorbIntensity);
    }
   
}

#endif // __ATMOSPHERE_RAW__