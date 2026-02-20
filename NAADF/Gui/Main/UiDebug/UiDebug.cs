using ImGuiNET;
using Microsoft.Xna.Framework;
using NAADF.Gui.Main.Debug;
using NAADF.World.Render;

namespace NAADF.Gui
{
    public static class UiDebug
    {
        public static bool isOpen = true;

        public static int boundsCalculationCount = 64;

        public static void Draw()
        {
            if (!isOpen)
                return;

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(400, 250), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(5, 18 + 5), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Debug", ref isOpen))
            {
                ImGui.Text("FPS: " + (1000.0f / WorldRender.frameTime).ToString("N1"));
                ImGui.Text("Frametime (ms): " + (WorldRender.frameTime).ToString("N3"));
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                if (App.worldHandler?.worldData != null)
                {
                    ImGui.Text("Size: [" + App.worldHandler.worldData.sizeInVoxels.X + ", " + App.worldHandler.worldData.sizeInVoxels.Y + ", " + App.worldHandler.worldData.sizeInVoxels.Z + "]");
                    double volumeGVoxels = ((long)App.worldHandler.worldData.sizeInVoxels.X * (long)App.worldHandler.worldData.sizeInVoxels.Y * (long)App.worldHandler.worldData.sizeInVoxels.Z) / 1_000_000_000.0;
                    ImGui.Text(volumeGVoxels.ToString("N3") + " Billion Voxels");
                    App.worldHandler.worldData.DrawDebugInfo();
                }
            }
            ImGui.End();
        }
    }
}