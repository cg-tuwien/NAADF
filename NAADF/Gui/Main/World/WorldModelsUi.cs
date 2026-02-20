using ImGuiNET;
using NAADF.World.Data;
using NAADF.World.Model;
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
        private static int selectedModel = -1;

        public static void DrawModels()
        {
            WorldData worldData = App.worldHandler.worldData;
            ModelHandler handler = App.worldHandler.modelHandler;

            if (ImGui.Button("Import Model"))
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    //openFileDialog.InitialDirectory = "c:\\";
                    openFileDialog.Filter = "cvox files (*.cvox)|*.cvox|All files (*.*)|*.*";
                    openFileDialog.FilterIndex = 1;
                    openFileDialog.RestoreDirectory = true;

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        ModelData modelData = ModelData.Load(openFileDialog.FileName);
                        handler.AddModel(modelData);
                    }
                }
            }

            if (ImGui.BeginListBox("Models"))
            {
                for (int i = 0; i < handler.models.Count; i++)
                {
                    bool isSelected = (i == selectedModel);

                    if (ImGui.Selectable(handler.models[i].name + "##" + i, isSelected))
                        selectedModel = i;

                    if (BuildFlags.Entities && ImGui.BeginPopupContextItem("ModelContext" + i))
                    {
                        if (ImGui.MenuItem("Create Entity"))
                        {
                            EntityData entityData = new EntityData(handler.models[i]);
                            worldData.entityHandler.addEntity(entityData);
                        }
                        ImGui.EndPopup();
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndListBox();
            }

            string sizeText = selectedModel != -1 ? 
                (handler.models[selectedModel].size.X + " | " + handler.models[selectedModel].size.Y + " | " + handler.models[selectedModel].size.Z) 
                : "<Select a model to show>";
            ImGui.Text("Size: " + sizeText);

            if (BuildFlags.Entities)
            {
                ImGui.Spacing();
                ImGui.TextWrapped("INFO: Right click a model to create an entity type");
            }
        }
    }
}
