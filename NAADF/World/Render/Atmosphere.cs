using Microsoft.Xna.Framework;
using NAADF.Gui.Main.Debug;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.World.Render
{
    public static class Atmosphere
    {
        static Vector3 DensityAtHeight(float height)
        {
            Vector3 density;
            density.X = (float)Math.Exp(-height / 0.3f) * (1 - height); //Rayleigh
            density.Y = (float)Math.Exp(-height / 0.2f) * (1 - height); //Mie
            density.Z = Math.Max(0, 1 - (Math.Abs(height - 0.25f) / 0.15f)); //Ozone
            return density * UiSkyDebug.skyAtmosphereDensity * 0.01f;
        }

        static Vector2 RaySphere(Vector3 sphereOrigin, float sphereRadius, Vector3 rayOrigin, Vector3 rayDir)
        {
            Vector3 dif = rayOrigin - sphereOrigin;
            float a = 1;
            float b = 2 * Vector3.Dot(dif, rayDir);
            float c = Vector3.Dot(dif, dif) - sphereRadius * sphereRadius;
            float d = b * b - 4 * a * c;

            if (d > 0)
            {
                float s = (float)Math.Sqrt(d);
                float dstNear = Math.Max(0, (-b - s) / (2 * a));
                float dstFar = (-b + s) / (2 * a);

                if (dstFar >= 0)
                    return new Vector2(dstNear, dstFar - dstNear);
            }
            return Vector2.Zero;
        }

        static Vector3 ScatterForDensities(Vector3 densities)
        {
            return (densities.X * UiSkyDebug.skyRayleighScatter + new Vector3(densities.Y * UiSkyDebug.skyMieScatter) + densities.Z * UiSkyDebug.skyOzoneAbsorb) * UiSkyDebug.skyScatterIntensity * 0.000001f;
        }

        static Vector3 getScatterDensitiesAtPoint(Vector3 pos)
        {
            float earthWithAtmoRadius = UiSkyDebug.skySphereRadius + UiSkyDebug.skyAtmosphereThickness;

            Vector2 rayResult = RaySphere(Vector3.Zero, earthWithAtmoRadius, pos, UiSkyDebug.skySunDir);
            if (rayResult.Y == 0)
                return Vector3.Zero;
            const int scatterSteps = 20;
            float scale = rayResult.Y / scatterSteps;

            Vector3 totalDensities = Vector3.Zero;
            for (int i = scatterSteps - 1; i >= 0; --i)
            {
                float factor = i / (float)scatterSteps;

                Vector3 curSamplePoint = pos + UiSkyDebug.skySunDir * rayResult.X + UiSkyDebug.skySunDir * rayResult.Y * factor;
                float height = Math.Max(0, (float)Math.Sqrt(Vector3.Dot(curSamplePoint, curSamplePoint)) - UiSkyDebug.skySphereRadius) / UiSkyDebug.skyAtmosphereThickness;
                Vector3 densities = DensityAtHeight(height) * scale;
                totalDensities += densities;
            }
            return totalDensities;
        }

        public static Vector3 GetLightForPoint(Vector3 pos)
        {
            Vector3 densitiesScatter = getScatterDensitiesAtPoint(pos + new Vector3(0, UiSkyDebug.skySphereRadius, 0));
            Vector3 scatterForExp = -ScatterForDensities(densitiesScatter) * UiSkyDebug.skyAbsorbIntensity;
            Vector3 T = new Vector3((float)Math.Exp(scatterForExp.X), (float)Math.Exp(scatterForExp.Y), (float)Math.Exp(scatterForExp.Z));
            return T * UiSkyDebug.skySunColor * UiSkyDebug.skySunIntensity;
        }
    }
}
