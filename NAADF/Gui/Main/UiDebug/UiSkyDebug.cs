using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.MediaFoundation;

namespace NAADF.Gui.Main.Debug
{
    public static class UiSkyDebug
    {
        public static bool isOpen = false;

        public static Vector3 skySunDir;
        public static float skySunIntensity = 10.0f;
        public static Vector3 skyRayleighScatter = new Vector3(5.802f, 13.558f, 33.1f);
        public static float skyMieScatter = 2.5f;
        public static Vector3 skyOzoneAbsorb = new Vector3(0.650f, 1.881f, 0.085f);
        public static Vector3 skySunColor = new Vector3(1, 1, 1);
        public static float skySphereRadius = 50000.0f * 100;
        public static float skyAtmosphereThickness = 50000.0f;
        public static float skyAtmosphereAveragePoint = 0.08f;
        public static float skyAtmosphereDensity = 14.0f;
        public static float skyAbsorbIntensity = 3.0f;
        public static float skyScatterIntensity = 1.35f;
        public static float skyMieFactor = 0.85f;
        public static int skyMainRaySteps = 24;
        public static int skySubScatterSteps = 6;

        public static void Draw()
        {
            if (!isOpen)
                return;
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(250, 350), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(500, 18), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Sky Debug", ref isOpen))
            {
                ImGuiCommon.ColorEdit3("Sun Color", ref skySunColor, ImGuiColorEditFlags.PickerHueWheel);
                ImGui.SliderFloat("Sun Intensity", ref skySunIntensity, 0.1f, 100, "%.3g", ImGuiSliderFlags.Logarithmic);
                ImGuiCommon.SliderFloat3("Rayleigh scatter", ref skyRayleighScatter, 1.0f, 100.0f, "%.2f", ImGuiSliderFlags.Logarithmic);
                ImGui.SliderFloat("Mie scatter", ref skyMieScatter, 0.1f, 10, "%.1f", ImGuiSliderFlags.Logarithmic);
                ImGuiCommon.SliderFloat3("Ozone absorb", ref skyOzoneAbsorb, 0.1f, 10.0f, "%.2f", ImGuiSliderFlags.Logarithmic);
                float temp = skySphereRadius * 0.001f;
                ImGui.SliderFloat("Planet radius [km]", ref temp, 100.0f, 1000000, "%.1f", ImGuiSliderFlags.Logarithmic);
                skySphereRadius = temp * 1000.0f;
                temp = skyAtmosphereThickness * 0.001f;
                ImGui.SliderFloat("Atmosphere thickness [km]", ref temp, 0.02f, 100, "%.2f", ImGuiSliderFlags.Logarithmic);
                skyAtmosphereThickness = temp * 1000.0f;
                ImGui.SliderFloat("Atmosphere average point", ref skyAtmosphereAveragePoint, 0.01f, 1, "%.3f");
                ImGui.SliderFloat("Atmosphere density", ref skyAtmosphereDensity, 0.01f, 10, "%.2f");
                ImGui.SliderFloat("Absorb intensity", ref skyAbsorbIntensity, 0.01f, 10, "%.4f", ImGuiSliderFlags.Logarithmic);
                ImGui.SliderFloat("Scatter intensity", ref skyScatterIntensity, 0.01f, 10, "%.4f", ImGuiSliderFlags.Logarithmic);
                ImGui.SliderFloat("Mie factor", ref  skyMieFactor, -1, 0.999f, "%.3f");
                ImGui.SliderInt("Main ray steps", ref skyMainRaySteps, 1, 100);
                ImGui.SliderInt("Sub scatter steps", ref skySubScatterSteps, 1, 100);
            }
            ImGui.End();
        }

        public static void SetShaderData(Effect shader)
        {
            shader.Parameters["skySunDir"].SetValue(skySunDir);
            shader.Parameters["skyRayleighScatter"].SetValue(skyRayleighScatter);
            shader.Parameters["skyMieScatter"].SetValue(skyMieScatter);
            shader.Parameters["skyOzoneAbsorb"].SetValue(skyOzoneAbsorb);
            shader.Parameters["skySunColor"].SetValue(skySunColor * skySunIntensity);
            shader.Parameters["skySphereRadius"].SetValue(skySphereRadius);
            shader.Parameters["skyAtmosphereThickness"].SetValue(skyAtmosphereThickness);
            shader.Parameters["skyAtmosphereAveragePoint"].SetValue(skyAtmosphereAveragePoint);
            shader.Parameters["skyAtmosphereDensity"].SetValue(skyAtmosphereDensity * 0.01f);
            shader.Parameters["skyAbsorbIntensity"].SetValue(skyAbsorbIntensity);
            shader.Parameters["skyScatterIntensity"].SetValue(skyScatterIntensity * 0.000001f);
            shader.Parameters["skyMieFactor"].SetValue(skyMieFactor);
            shader.Parameters["skyMainRaySteps"].SetValue(skyMainRaySteps);
            shader.Parameters["skySubScatterSteps"].SetValue(skySubScatterSteps);
        }
    }
}
