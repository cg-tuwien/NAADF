using ImGuiNET;
using Microsoft.Xna.Framework;
using NAADF.Common;
using NAADF.Gui.Main.Debug;
using NAADF.World.Data;
using NAADF.World.Generator;
using NAADF.World.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.Gui
{
    public static partial class UiHeaderBar
    {
        public static int meshImportSize = 1024;
        public static string meshImportPermutation = "xzy";
        public static int meshImportPaletteSize = 256;

        public static void Draw()
        {
            Vector2 projectBarPos = Vector2.Zero;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new System.Numerics.Vector2(0, 0));
            if (ImGui.BeginMainMenuBar())
            {
                ImGui.PopStyleVar(2);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(8, 8));
                bool openWorldSizePopup = false, openImportMeshPopup = false;
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.BeginMenu("Open"))
                    {
                        if (ImGui.MenuItem("Fit to Model"))
                        {
                            string fileName = UserSelectFromCvoxFile();
                            if (fileName != null)
                            {
                                ModelData modelData = ModelData.Load(fileName);
                                WorldGeneratorModel worldGenerator = new WorldGeneratorModel();
                                worldGenerator.SetModel(modelData);
                                WorldData worldData = new WorldData(modelData.size, 4);



                                App.worldHandler.ApplyAndGenerateNewWorldData(worldData, worldGenerator);
                            }
                        }

                        if (ImGui.MenuItem("Fixed size"))
                            openWorldSizePopup = true;

                        ImGui.EndMenu();
                    }
                    if (ImGui.MenuItem("Save As"))
                    {
                        string fileName = UserSelectNewCvoxFile();
                        if (fileName != null)
                        {
                            ModelData modelData = ModelData.CreateFromWorldData(fileName, App.worldHandler.worldData);
                            modelData.Save();
                            modelData.Dispose();
                        }
                    }
                    if (ImGui.MenuItem("Import Vox"))
                    {
                        string fileName = UserSelectFromVoxFile();
                        if (fileName != null)
                        {
                            ModelData modelData = ModelData.ImportFromVox(fileName);
                            WorldGeneratorModel worldGenerator = new WorldGeneratorModel();
                            worldGenerator.SetModel(modelData);
                            App.worldHandler.ApplyAndGenerateNewWorldData(new WorldData(modelData.size, 4), worldGenerator);
                        }
                    }
                    if (ImGui.MenuItem("Import VL32"))
                    {
                        string fileName = UserSelectFromVL32File();
                        if (fileName != null)
                        {
                            ModelData modelData = ModelData.ImportFromVL32(fileName);
                            WorldGeneratorModel worldGenerator = new WorldGeneratorModel();
                            worldGenerator.SetModel(modelData);
                            App.worldHandler.ApplyAndGenerateNewWorldData(new WorldData(modelData.size, 4), worldGenerator);
                        }
                    }
                    if (ImGui.MenuItem("Import Mesh"))
                        openImportMeshPopup = true;
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("View"))
                {
                    if (ImGui.MenuItem("Settings", "", ref Settings.isOpen)) { }
                    if (ImGui.MenuItem("Debug", "", ref UiDebug.isOpen)) { }
                    if (ImGui.MenuItem("Sky Debug", "", ref UiSkyDebug.isOpen)) { }
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Tools"))
                {
                    if (ImGui.MenuItem("World", "", ref WorldUi.isOpen)) { }
                    if (ImGui.MenuItem("Path Builder", "", ref PathHandler.isUiOpen)) { }
                    ImGui.EndMenu();
                }
                ImGui.Spacing();

                ImGui.EndMainMenuBar();
                ImGui.PopStyleVar(1);

                // World Size Popup
                if (openWorldSizePopup)
                    ImGui.OpenPopup("Load World##WorldSizePopup");
                var center = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(500, 160), ImGuiCond.Appearing);
                if (ImGui.BeginPopupModal("Load World##WorldSizePopup", ImGuiWindowFlags.Popup))
                {
                    ImGui.Text("Set World Dimensions:");
                    int worldGenSegmentSize = App.worldHandler.worldGenSegmentSizeInGroups;
                    ImGui.SliderInt("Segment size", ref worldGenSegmentSize, 1, 8);
                    ImGuiCommon.HelperIcon($"Worlds are generated segment by segment. The segment size is defined by the number of (4 * chunks) in a direction. Currently ({worldGenSegmentSize * 4} chunks)^3.", 500);
                    App.worldHandler.worldGenSegmentSizeInGroups = worldGenSegmentSize;
                    ImGui.SliderInt3("Amount of segments", ref App.worldHandler.worldSizeToUseInWorldGenSegments.X, 1, 32);
                    Point3 finalSize = App.worldHandler.worldSizeToUseInWorldGenSegments * App.worldHandler.worldGenSegmentSizeInGroups * 64;
                    ImGui.Text("Final size: " + finalSize.X + ", " + finalSize.Y + ", " + finalSize.Z);
                    ImGuiCommon.HelperIcon("It is recommeded to keep the final size below 16384x4096x16384", 500);
                    long voxelAmount = (long)finalSize.X * (long)finalSize.Y * (long)finalSize.Z;
                    ImGui.Text("Voxel amount: " + (voxelAmount / 1000000000.0).ToString("N2") + " billion");

                    ImGui.Separator();

                    if (ImGui.Button("Reset to Default"))
                    {
                        App.worldHandler.worldGenSegmentSizeInGroups = 4;
                        App.worldHandler.worldSizeToUseInWorldGenSegments = new Point3(16, 2, 16);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Load"))
                    {
                        string fileName = UserSelectFromCvoxFile();
                        if (fileName != null)
                        {
                            ModelData modelData = ModelData.Load(fileName);
                            WorldGeneratorModel worldGenerator = new WorldGeneratorModel();
                            worldGenerator.SetModel(modelData);
                            WorldData worldData = new WorldData(App.worldHandler.worldSizeToUseInWorldGenSegments * App.worldHandler.worldGenSegmentSizeInGroups * 64, App.worldHandler.worldGenSegmentSizeInGroups);

                            App.worldHandler.ApplyAndGenerateNewWorldData(worldData, worldGenerator);
                        }

                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                        ImGui.CloseCurrentPopup();

                    ImGui.EndPopup();
                }

                // Import Mesh Popup
                if (openImportMeshPopup)
                    ImGui.OpenPopup("Import Mesh##ImportMeshPopup");
                ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
                ImGui.SetNextWindowSize(new System.Numerics.Vector2(700, 150), ImGuiCond.Appearing);
                if (ImGui.BeginPopupModal("Import Mesh##ImportMeshPopup", ImGuiWindowFlags.Popup))
                {
                    ImGui.SliderInt("Max size", ref meshImportSize, 16, 8192);
                    ImGuiCommon.HelperIcon($"Defines the maximum size in the largest axis. The other dimensions depend on the mesh. A large size requires a lot of memory and can take a long time.", 500);
                    ImGui.InputText("Axis Permutation", ref meshImportPermutation, 3);
                    ImGuiCommon.HelperIcon($"The default is xzy. Another order such as xyz may be specified to reorder axes. Capital letters flip axes.", 500);
                    ImGui.SliderInt("Palette size", ref meshImportPaletteSize, 4, 4096);
                    ImGui.Text("The importer creates a temporal .vl32 file, which can be many GBs large depending on the size.");
                    ImGui.Separator();

                    if (ImGui.Button("Reset to Default"))
                    {
                        meshImportSize = 1024;
                        meshImportPermutation = "xzy";
                        meshImportPaletteSize = 256;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Import"))
                    {
                        string fileName = UserSelectFromMeshFile();
                        if (fileName != null)
                        {
                            ModelData modelData = ModelData.ImportFromMesh(fileName);
                            WorldGeneratorModel worldGenerator = new WorldGeneratorModel();
                            worldGenerator.SetModel(modelData);
                            App.worldHandler.ApplyAndGenerateNewWorldData(new WorldData(modelData.size, 4), worldGenerator);
                        }

                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                        ImGui.CloseCurrentPopup();

                    ImGui.EndPopup();
                }
            }
            else
                ImGui.PopStyleVar(2);
        }
    }
}
