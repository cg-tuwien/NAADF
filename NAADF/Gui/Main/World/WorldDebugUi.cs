using ImGuiNET;
using NAADF.World.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Gui
{
    public static partial class WorldUi
    {
        public static void DrawDebug()
        {
            WorldData worldData = App.worldHandler.worldData;
            EntityHandler handler = worldData.entityHandler;

            ImGui.Text("Entity Cpu - Gpu Copy: " + (handler.bytesCpuGpuCopy / 1000.0f).ToString("0.00") + " KB");
            ImGui.Text("Processing time entities: " + handler.chunkProcessingTime.ToString("0.00") + " ms");
            ImGui.Text("Processing time data change: " + worldData.changeHandler.changeProcessingTime.ToString("0.00") + " ms");
        }
    }
}
