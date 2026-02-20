using ImGuiNET;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Gui
{
    public class ImGuiStyleConfig
    {
        public static ImGuiStylePtr style;

        public static Vector2 ItemMargin = new Vector2(5, 3);

        public static void SetStyle()
        {
            Vector4 hoverColor = new Vector4(0.6f, 0.4f, 0.1f, 0.25f);
            style = ImGui.GetStyle();
            style.WindowPadding = new Vector2(10, 10);
            style.WindowMinSize = new Vector2(10, 10);
            style.Colors[(int)ImGuiCol.Text] = new Vector4(1, 1, 1, 1);
            style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.25f, 0.25f, 0.25f, 1);
            style.Colors[(int)ImGuiCol.Border] = new Vector4(0.25f, 0.25f, 0.25f, 1);
            style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.125f, 0.125f, 0.125f, 0.95f);
            style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.062f, 0.062f, 0.062f, 0.877f);
            style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.033f, 0.033f, 0.033f, 0.75f);
            style.Colors[(int)ImGuiCol.FrameBgHovered] = hoverColor;
            style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.033f, 0.033f, 0.033f, 0.75f);

            style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
            style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.5f, 0.5f, 0.5f, 0.75f);
            style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.5f, 0.5f, 0.5f, 0.75f);

            style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.75f, 0.75f, 0.75f, 0.75f);

            style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(1.0f, 0.75f, 0.125f, 0.25f);

            style.Colors[(int)ImGuiCol.Button] = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
            style.Colors[(int)ImGuiCol.ButtonHovered] = hoverColor;
            style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.5f, 0.5f, 0.5f, 0.75f);

            style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
            style.Colors[(int)ImGuiCol.TabHovered] = hoverColor;
            style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.5f, 0.5f, 0.5f, 0.75f);

            style.Colors[(int)ImGuiCol.Header] = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
            style.Colors[(int)ImGuiCol.HeaderHovered] = hoverColor;
            style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.5f, 0.5f, 0.5f, 0.75f);

            style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
            style.Colors[(int)ImGuiCol.ResizeGripHovered] = hoverColor;
            style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.5f, 0.5f, 0.5f, 0.75f);

            style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = hoverColor;
            style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.5f, 0.5f, 0.5f, 0.75f);

            // Apply gamma correction
            for (int i = 0; i < style.Colors.Count; i++)
            {
                Vector4 color = style.Colors[i];
                color = new Vector4(ToLinear(color.X), ToLinear(color.Y), ToLinear(color.Z), (float)Math.Pow(color.W, 1.0f / 2.2f));
                style.Colors[i] = color;
            }
        }

        private static float ToLinear(float value)
        {
            return (value < 0.04045f)
                ? value / 12.92f
                : (float)Math.Pow((value + 0.055f) / 1.055f, 2.4f);
        }
    }
}
