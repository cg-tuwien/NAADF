using ImGuiNET;
using NAADF.World;
using NAADF.World.Data;
using NAADF.World.Data.Editing;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Gui
{
    public static partial class WorldUi
    {
        public enum EditingToolType
        {
            None,
            Sphere,
            Cube,
            Paint,
            FloodFill,
            Model
        }

        public static EditingToolType toolType = EditingToolType.None;
        public static bool typeSelectedExtern = false;

        public static void DrawEditing()
        {
            if (ImGui.CollapsingHeader("Material", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawMaterial();
            }

            if (ImGui.CollapsingHeader("Editing tool", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.BeginCombo("Tool", toolType.ToString()))
                {
                    foreach (EditingToolType type in Enum.GetValues(typeof(EditingToolType)))
                    {
                        bool isSelected = toolType == type;
                        if (ImGui.Selectable(type.ToString(), isSelected))
                        {
                            toolType = type;
                            CreateNewTool(toolType);
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                App.worldHandler.worldData.editingHandler.DrawUi();
            }
        }

        private static void CreateNewTool(EditingToolType type)
        {
            WorldData worldData = App.worldHandler.worldData;
            if (toolType == EditingToolType.None)
                worldData.editingHandler.tool = null;
            else if (toolType == EditingToolType.Sphere)
                worldData.editingHandler.tool = new EditingToolSphere();
            else if (toolType == EditingToolType.Cube)
                worldData.editingHandler.tool = new EditingToolCube();
            else if (toolType == EditingToolType.Paint)
                worldData.editingHandler.tool = new EditingToolPaint();
            else if (toolType == EditingToolType.FloodFill)
                worldData.editingHandler.tool = new EditingToolFloodFill();
            else if (toolType == EditingToolType.Model)
                worldData.editingHandler.tool = new EditingToolModel();
        }

        private static void DrawMaterial()
        {
            WorldData worldData = App.worldHandler.worldData;
            VoxelTypeHandler voxelHandler = App.worldHandler.voxelTypeHandler;
            EditingHandler editingHandler = worldData.editingHandler;
            if (ImGui.Button("Create new type"))
            {
                editingHandler.selectedType = voxelHandler.ApplyVoxelType(new VoxelType());
                editingHandler.selectedTypeRenderIndex = editingHandler.selectedType.renderIndex;
                typeSelectedExtern = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("Select Material"))
                editingHandler.isMaterialSelect = true;
            if (ImGui.BeginListBox("Materials", new System.Numerics.Vector2(0, 0)))
            {
                foreach (var type in voxelHandler.typesById)
                {
                    bool isSelected = (type.Value.renderIndex == editingHandler.selectedTypeRenderIndex);
                    if (ImGui.Selectable(type.Key + "##" + editingHandler.selectedTypeRenderIndex, isSelected))
                    {
                        editingHandler.selectedType = type.Value;
                        editingHandler.selectedTypeRenderIndex = type.Value.renderIndex;
                    }
                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                        if (typeSelectedExtern)
                        {
                            typeSelectedExtern = false;
                            ImGui.SetScrollHereY();
                        }
                    }
                }
                ImGui.EndListBox();
            }


            if (IO.KBStates.IsKeyToggleDown(Microsoft.Xna.Framework.Input.Keys.Escape))
                editingHandler.isMaterialSelect = false;
            if (editingHandler.isMaterialSelect)
                ImGui.TextWrapped("Select a voxel for material selection or press esc to cancel");

            if (editingHandler.selectedTypeRenderIndex == uint.MaxValue)
                return;
            string oldID = voxelHandler.typesById.FirstOrDefault(v => v.Value.renderIndex == editingHandler.selectedType.renderIndex).Value.ID;
            if (ImGui.InputText("name", ref editingHandler.selectedType.ID, 64, ImGuiInputTextFlags.EnterReturnsTrue))
                voxelHandler.UpdateType(editingHandler.selectedType, oldID);

            if (ImGui.BeginCombo("Material Base", editingHandler.selectedType.materialBase.ToString()))
            {
                foreach (MaterialTypeBase type in Enum.GetValues(typeof(MaterialTypeBase)))
                {
                    if (editingHandler.selectedType.materialLayer != MaterialTypeLayer.None && (type == MaterialTypeBase.MetallicMirror))
                        continue;
                    bool isSelected = editingHandler.selectedType.materialBase == type;
                    if (ImGui.Selectable(type.ToString(), isSelected))
                    {
                        editingHandler.selectedType.materialBase = type;
                        if (editingHandler.selectedType.materialBase == MaterialTypeBase.MetallicRough)
                            editingHandler.selectedType.roughness = 0.05f;
                        voxelHandler.UpdateType(editingHandler.selectedType);
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            if (editingHandler.selectedType.materialBase == MaterialTypeBase.Diffuse)
            {
                if (ImGuiCommon.ColorEdit3("Albedo", ref editingHandler.selectedType.colorBase))
                    voxelHandler.UpdateType(editingHandler.selectedType);
            }
            else if (editingHandler.selectedType.materialBase == MaterialTypeBase.Emissive)
            {
                if (ImGuiCommon.ColorEdit3("Albedo", ref editingHandler.selectedType.colorBase))
                    voxelHandler.UpdateType(editingHandler.selectedType);
                if (ImGui.SliderFloat("Emissive strength", ref editingHandler.selectedType.colorLayered.X, 0.02f, 600, "%.2f", ImGuiSliderFlags.Logarithmic))
                    voxelHandler.UpdateType(editingHandler.selectedType);
            }
            else if (editingHandler.selectedType.materialBase == MaterialTypeBase.MetallicRough)
            {
                if (ImGuiCommon.SliderFloat3("Refractive index", ref editingHandler.selectedType.colorBase, 1.0f, 10.0f, "%.2f", ImGuiSliderFlags.Logarithmic))
                    voxelHandler.UpdateType(editingHandler.selectedType);
                if (ImGui.SliderFloat("Roughness", ref editingHandler.selectedType.roughness, 0.001f, 1, "%.5f", ImGuiSliderFlags.Logarithmic))
                    voxelHandler.UpdateType(editingHandler.selectedType);
            }
            else if (editingHandler.selectedType.materialBase == MaterialTypeBase.MetallicMirror)
            {
                if (ImGuiCommon.SliderFloat3("Refractive index", ref editingHandler.selectedType.colorBase, 1.0f, 10.0f, "%.2f", ImGuiSliderFlags.Logarithmic))
                    voxelHandler.UpdateType(editingHandler.selectedType);
            }
        }
    }
}