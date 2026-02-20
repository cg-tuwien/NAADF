using ImGuiNET;
using NAADF.Common.Extensions;
using NAADF.World.Render;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace NAADF.Common
{
    public struct PathNode
    {
        public Vector3 pos;
        public Vector3 dir;
        public float speed;
        public string name;

        public PathNode(Vector3 pos, Vector3 dir, float time, string name)
        {
            this.pos = pos;
            this.dir = dir;
            this.speed = 1.0f / time;
            this.name = name;
        }

        public void addNode(ref PathNode node, float factor)
        {
            node.pos += pos * factor;
            node.dir += dir * factor;
            node.speed += speed * factor;
        }
    }

    public class PathHandler
    {
        public static bool isUiOpen = false;
        public bool isPlaying = false, screenShotAtEnd = false, isFinalFrame = false, useSingleInterpolation = false;
        float time;
        double timeSum = 0;
        int frameCount = 0;
        List<PathNode> path = new List<PathNode>();
        private int selectedIndex = -1;
        string fileName = "";
        string curNodeName = "";
        int curNodeNameIndex = 0;

        public PathHandler()
        {

        }

        public void Update(float gameTime)
        {
            if (IO.KBStates.IsKeyToggleDown(Microsoft.Xna.Framework.Input.Keys.F2))
            {
                isPlaying ^= true;
                if (isPlaying)
                    time = 0;
            }

            if (!isPlaying || path.Count == 0)
                return;

            frameCount++;
            timeSum += gameTime;

            isFinalFrame = false;

            PathNode curNode = new PathNode(Vector3.Zero, Vector3.Zero, 0, null);
            if (useSingleInterpolation)
            {
                curNode.speed = 0;
                int curNodeIndex = (int)time;
                int nextNodeIndex = (int)time + 1;
                float facCur = 1.0f - (time - curNodeIndex);
                float facNext = (time - curNodeIndex);
                if (facCur > 0)
                    path[curNodeIndex].addNode(ref curNode, facCur);
                if (facNext > 0)
                    path[nextNodeIndex].addNode(ref curNode, facNext);
            }
            else
            {
                int left1 = (int)time;
                int left2 = (int)time - 1;
                int right1 = (int)time + 1;
                int right2 = (int)time + 2;
                float facLeft1 = left1 < 0 ? 0 : fade((left1 - time) * 0.5f + 1);
                float facLeft2 = left2 < 0 ? 0 : fade((left2 - time) * 0.5f + 1);
                float facRight1 = right1 >= path.Count() ? 0 : fade((time - right1) * 0.5f + 1);
                float facRight2 = right2 >= path.Count() ? 0 : fade((time - right2) * 0.5f + 1);
                float totalFac = facLeft1 + facLeft2 + facRight1 + facRight2;

                curNode.speed = 0;
                if (facLeft1 > 0)
                    path[left1].addNode(ref curNode, facLeft1 / totalFac);
                if (facLeft2 > 0)
                    path[left2].addNode(ref curNode, facLeft2 / totalFac);
                if (facRight1 > 0)
                    path[right1].addNode(ref curNode, facRight1 / totalFac);
                if (facRight2 > 0)
                    path[right2].addNode(ref curNode, facRight2 / totalFac);
            }

            curNode.dir = Vector3.Normalize(curNode.dir);

            WorldRender.camera.SetPos(curNode.pos);
            WorldRender.camera.SetDir(curNode.dir);

            if (time >= path.Count() - 1)
            {
                isPlaying = false;
                isFinalFrame = true;
            }

            time += curNode.speed * gameTime * 0.001f;
            time = Math.Min(path.Count() - 1.0f, time);
        }

        public void RenderUi()
        {
            if (!isUiOpen)
                return;

            if (ImGui.Begin("Path Editor", ref isUiOpen))
            {
                ImGui.Text("Average ms: " + (timeSum / frameCount));
                ImGui.Checkbox("Screenshot at end", ref screenShotAtEnd);
                if (ImGui.Button("Screenshot now"))
                {
                    isFinalFrame = true;
                }

                if (ImGui.Button(isPlaying ? "Stop" : "Play"))
                {
                    isPlaying ^= true;
                    if (isPlaying)
                    {
                        frameCount = 0;
                        timeSum = 0;
                        time = 0;
                    }
                }
                float totalTime = 0.0f;
                foreach (var node in path)
                {
                    if (node.speed > 0.0001f) // Prevent divide by zero
                        totalTime += 1.0f / node.speed;
                }

                ImGui.SameLine();
                ImGui.Text("Total time: " + totalTime + "s");

                // Node List
                if (ImGui.BeginListBox("Nodes", new Vector2(300, 300)))
                {
                    for (int i = 0; i < path.Count; i++)
                    {
                        bool isSelected = (i == selectedIndex);
                        string label = path[i].name;

                        if (ImGui.Selectable(label + "##" + i, isSelected))
                            selectedIndex = i;

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndListBox();
                }

                ImGui.Separator();

                // Insert buttons
                if (selectedIndex >= 0)
                {
                    if (ImGui.Button("Insert Above"))
                    {
                        PathNode newNode = getNodeFromCurrent(1);
                        path.Insert(selectedIndex, newNode);
                        curNodeName = "";
                    }

                    ImGui.SameLine();

                    if (ImGui.Button("Insert Below"))
                    {
                        PathNode newNode = getNodeFromCurrent(1);
                        path.Insert(selectedIndex + 1, newNode);
                        curNodeName = "";
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Apply"))
                    {
                        PathNode curNode = path[selectedIndex];
                        WorldRender.camera.SetPos(curNode.pos);
                        WorldRender.camera.SetDir(curNode.dir);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Remove"))
                    {
                        path.RemoveAt(selectedIndex);
                    }
                }

                // Add at end
                if (ImGui.Button("Add Node At End"))
                {
                    PathNode newNode = getNodeFromCurrent(1);
                    curNodeName = "";
                    path.Add(newNode);
                }

                ImGui.InputText("Node Name", ref curNodeName, 256);

                ImGui.Separator();

                // Edit selected node
                if (selectedIndex >= 0 && selectedIndex < path.Count)
                {
                    PathNode selected = path[selectedIndex];

                    Vector3 pos = selected.pos;
                    Vector3 dir = selected.dir;
                    float speed = selected.speed;
                    string name = selected.name;

                    ImGui.Text("Selected Node");
                    ImGui.InputFloat3("Position", ref pos);
                    ImGui.InputFloat3("Direction", ref dir);
                    float time = 1.0f / speed;
                    ImGui.SliderFloat("Time", ref time, 0.2f, 20.0f, "%.2f");
                    ImGui.InputText("Name", ref name, 255);
                    speed = 1.0f / time;

                    selected.pos = pos;
                    selected.dir = dir;
                    selected.speed = speed;
                    selected.name = name;

                    path[selectedIndex] = selected;
                }
                else
                    selectedIndex = -1;

                ImGui.Separator();

                ImGui.InputText("File Name", ref fileName, 256);

                if (ImGui.Button("Save"))
                {
                    SavePathData(fileName + ".pnb");
                }

                ImGui.SameLine();

                if (ImGui.Button("Load"))
                {
                    LoadPathData(fileName + ".pnb");
                }
            }
            ImGui.End();
        }

        private PathNode getNodeFromCurrent(float time)
        {
            return new PathNode(WorldRender.camera.GetPos().toVector3().ToNumerics(), Vector3.Normalize(WorldRender.camera.GetDir().ToNumerics()), time, string.IsNullOrEmpty(curNodeName) ? "Node " + curNodeNameIndex : curNodeName);
        }

        private float fade(float t)
        {
            return (t * t * t * (t * (t * 6 - 15) + 10));
        }

        private void SavePathData(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            using (var stream = File.Open(filePath, FileMode.Create))
            {
                stream.WriteInt(1); // version
                stream.WriteInt(path.Count);

                for (int i = 0; i < path.Count; i++)
                {
                    stream.WriteFloat(path[i].pos.X);
                    stream.WriteFloat(path[i].pos.Y);
                    stream.WriteFloat(path[i].pos.Z);
                    stream.WriteFloat(path[i].dir.X);
                    stream.WriteFloat(path[i].dir.Y);
                    stream.WriteFloat(path[i].dir.Z);
                    stream.WriteFloat(path[i].speed);
                    stream.WriteNullTerminated(path[i].name);
                }
            }
        }

        private void LoadPathData(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            using (var stream = File.Open(filePath, FileMode.Open))
            {
                int version = stream.ReadInt();
                int count = stream.ReadInt();

                path.Clear();

                for (int i = 0; i < count; i++)
                {
                    float posX = stream.ReadFloat();
                    float posY = stream.ReadFloat();
                    float posZ = stream.ReadFloat();
                    float dirX = stream.ReadFloat();
                    float dirY = stream.ReadFloat();
                    float dirZ = stream.ReadFloat();
                    float speed = stream.ReadFloat();
                    string name = stream.ReadNullTerminated();
                    path.Add(new PathNode(new Vector3(posX, posY, posZ), new Vector3(dirX, dirY, dirZ), 1.0f / speed, name));
                }
            }
            fileName = filePath.Substring(0, filePath.LastIndexOf("."));
            selectedIndex = -1;
        }
    }
}
