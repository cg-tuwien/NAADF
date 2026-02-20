using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using NAADF.Gui.Main.Debug;

namespace NAADF.Gui
{
    public class GuiHandler
    {
        public static bool IsUiActive = false;
        public static bool ShowUi = true;

        public static ImGuiRenderer GuiRenderer;
        private App app;

        public GuiHandler(App app)
        {
            this.app = app;
            GuiRenderer = new ImGuiRenderer(app);
            GuiRenderer.RebuildFontAtlas();

            ImGuiStyleConfig.SetStyle();
        }

        public void SetupUi()
        {

        }

        public static void Update()
        {
            if (IO.KBStates.IsKeyToggleDown(Keys.F1))
                ShowUi ^= true;
        }


        public void BeginDraw(GameTime gameTime)
        {
            GuiRenderer.BeforeLayout(gameTime);
        }

        public void Draw()
        {
            if (ShowUi)
            {
                UiHeaderBar.Draw();
                UiDebug.Draw();
                UiSkyDebug.Draw();
                Settings.RenderImGui();
                WorldUi.Draw();
            }
        }

        public void EndDraw()
        {
            GuiRenderer.AfterLayout();

            bool isModalOpen = false;
            IsUiActive = isModalOpen || ImGui.IsAnyItemActive() || ImGui.IsAnyItemHovered() || ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByPopup);
        }
    }
}
