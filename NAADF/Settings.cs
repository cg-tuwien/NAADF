
// BUILD FLAGS START
// NOTE: Make sure they are the same as in "Content/shaders/settings.fxh"

#define ENTITIES
//#define HDR // Note that HDR requires fullscreen

// BUILD FLAGS END


using ImGuiNET;
using NAADF.Gui;
using NAADF.World.Render;
using System;
using System.IO;
using System.Text.Json;

namespace NAADF
{
    public static class BuildFlags
    {
#if ENTITIES
        public const bool Entities = true;
#else
    public const bool Entities = false;
#endif
#if HDR
        public const bool Hdr = true;
#else
        public const bool Hdr = false;
#endif
    }

    public class SettingDataGeneral
    {
        public float exposure = 1.0f;
        public float toneMappingFac = 1.5f;
        public float fov = 90;
        public bool lockTo60fps = false;

        public void RenderImGui()
        {
            if (ImGui.Checkbox("Lock to 60 fps", ref lockTo60fps))
                App.app.IsFixedTimeStep = lockTo60fps;
            ImGui.SliderFloat("Exposure", ref exposure, 0.1f, 10, "%.3g", ImGuiSliderFlags.Logarithmic);
            ImGui.SliderFloat("Tone Mapping", ref toneMappingFac, 0.1f, 10.0f, "%.3g", ImGuiSliderFlags.Logarithmic);
            ImGui.SliderFloat("FOV", ref fov, 1, 120, "%.7g", ImGuiSliderFlags.None);
        }
    }

    public class SettingDataRender
    {
        public bool showSteps = false;
        public RenderVersion version = RenderVersion.Base;
        public SettingDataRenderAlbedo renderAlbedo = new();
        public SettingDataRenderBase renderBase = new();
        public SettingDataRenderPathTracer renderPathTracer = new();

        public void RenderImGui()
        {
            ImGui.Checkbox("Show ray steps", ref showSteps);
            ImGuiCommon.HelperIcon("Shows the amount of steps done during primary ray traversal. Brighter means more steps", 500);
            if (ImGui.BeginCombo("Render version", version.ToString()))
            {
                foreach (RenderVersion curVersion in Enum.GetValues(typeof(RenderVersion)))
                {
                    bool isSelected = version == curVersion;
                    if (ImGui.Selectable(curVersion.ToString(), isSelected))
                    {
                        version = curVersion;
                        WorldRender.ApplyRenderVersion(version);
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            if (version == RenderVersion.Albedo)
                renderAlbedo.RenderImGui();
            else if (version == RenderVersion.Base)
                renderBase.RenderImGui();
            else
                renderPathTracer.RenderImGui();
        }
    }

    public class SettingData
    {
        public SettingDataGeneral general = new();
        public SettingDataRender render = new();

        public void RenderImGui()
        {
            if (ImGui.Button("Save"))
                Settings.Save();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.BeginTabBar("SettingsTabs"))
            {
                if (ImGui.BeginTabItem("Render"))
                {
                    render.RenderImGui();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("General"))
                {
                    general.RenderImGui();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        public string getJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions { IncludeFields = true });
        }

        public static SettingData fromJson(string json)
        {
            return JsonSerializer.Deserialize<SettingData>(json, new JsonSerializerOptions { IncludeFields = true });
        }
    }

    public static class Settings
    {
        public static SettingData data = new SettingData();
        public static bool isOpen = true;

        public static void Load()
        {
            if (File.Exists("settings.json"))
                data = SettingData.fromJson(File.ReadAllText("settings.json"));
        }

        public static void Save()
        {
            File.WriteAllText("settings.json", data.getJson());
        }

        public static void RenderImGui()
        {
            if (!isOpen)
                return;

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(400, 500), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(5, 260 + 18), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Settings", ref isOpen))
            {
                data.RenderImGui();
            }
            ImGui.End();
        }
    }
}
