using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NAADF.Gui
{
    public static partial class WorldUi
    {
        public static bool isOpen = true;

        public static void Draw()
        {
            if (!isOpen || App.worldHandler.worldData == null)
                return;

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 400), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(App.ScreenWidth - 300 - 5, 18 + 5), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("World", ref isOpen))
            {

                if (ImGui.BeginTabBar("TabsEditing"))
                {
                    if (ImGui.BeginTabItem("Models"))
                    {
                        DrawModels();
                        ImGui.EndTabItem();
                    }
                    if (BuildFlags.Entities && ImGui.BeginTabItem("Entities"))
                    {
                        DrawEntities();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Editing"))
                    {
                        DrawEditing();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Debug"))
                    {
                        DrawDebug();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
        }
    }
}
