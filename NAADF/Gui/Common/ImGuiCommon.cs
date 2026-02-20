using ImGuiNET;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Gui
{
    public static class ImGuiCommon
    {
        public static bool ColorEdit3(string label, ref Vector3 color, ImGuiColorEditFlags flags = ImGuiColorEditFlags.None)
        {
            var vec = color.ToNumerics();
            bool res =  ImGui.ColorEdit3(label, ref vec, flags);
            color = vec;
            return res;
        }

        public static bool SliderFloat3(string label, ref Vector3 v, float v_min = 0, float v_max = 1, string format = "%.3f", ImGuiSliderFlags flags = ImGuiSliderFlags.None)
        {
            var vec = v.ToNumerics();
            bool res = ImGui.SliderFloat3(label, ref vec, v_min, v_max, format, flags);
            v = vec;
            return res;
        }

        public static void HelperIcon(string text, float maxWidth, bool SameLine = true)
        {
            if (SameLine)
                ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            Tooltip(text, maxWidth, 100);
        }

        public static void Tooltip(string text, float maxWidth, double minTime = -1)
        {
            if (ImGui.IsItemHovered() && IO.mouseNoMoveTime > minTime)
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(maxWidth);
                ImGui.TextUnformatted(text);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }

    }
}
