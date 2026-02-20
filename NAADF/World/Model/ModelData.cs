using Accord.MachineLearning;
using Accord.MachineLearning.Clustering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NAADF.Common;
using NAADF.Common.Extensions;
using NAADF.Gui;
using NAADF.World.Data;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Voxels;

namespace NAADF.World.Model
{

    public class ModelData : IDisposable
    {
        public StructuredBuffer dataChunkGpu, dataBlockGpu, dataVoxelGpu;
        public uint[] dataChunk, dataBlock, dataVoxel;
        public Point3 size, sizeInChunks;
        public VoxelType[] types;
        public uint chunkCount, blockCount, voxelCount;
        public string curFilePath = null;
        public string name = null;

        private ModelData(string fileName, Point3 modelSize, VoxelType[] types, uint[] dataChunk, uint[] dataBlock, uint[] dataVoxel, uint blockCount, uint voxelCount, bool isTemp = false)
        {
            this.size = modelSize;
            this.types = types;
            this.dataChunk = dataChunk;
            this.dataBlock = dataBlock;
            this.dataVoxel = dataVoxel;
            this.sizeInChunks = new Point3((modelSize.X + 15) / 16, (modelSize.Y + 15) / 16, (modelSize.Z + 15) / 16);
            this.chunkCount = (uint)(sizeInChunks.X * sizeInChunks.Y * sizeInChunks.Z);
            this.blockCount = blockCount;
            this.voxelCount = voxelCount;
            name = Path.GetFileNameWithoutExtension(fileName);
            curFilePath = fileName.Substring(0, fileName.LastIndexOf("."));
            if (!isTemp)
                CreateDataForRender();
        }

        private static void SaveVoxelType(Stream stream, VoxelType type)
        {
            string id = (type.ID?.StartsWith("_") ?? true) ? "_" : type.ID;
            stream.WriteNullTerminated(id);
            stream.WriteVector3(type.colorBase);
            stream.WriteVector3(type.colorLayered);
            stream.WriteInt((int)type.materialBase);
            stream.WriteInt((int)type.materialLayer);
            stream.WriteFloat(type.roughness);
        }

        private static VoxelType LoadVoxelType(Stream stream)
        {
            VoxelType type = new VoxelType();
            string id = stream.ReadNullTerminated();
            type.ID = id.StartsWith("_") ? null : id;
            type.colorBase = stream.ReadVector3();
            type.colorLayered = stream.ReadVector3();
            type.materialBase = (MaterialTypeBase)stream.ReadInt();
            type.materialLayer = (MaterialTypeLayer)stream.ReadInt();
            type.roughness = stream.ReadFloat();
            return type;
        }

        private void CreateDataForRender()
        {
            if (dataChunkGpu != null)
                dataChunkGpu.Dispose();
            if (dataBlockGpu != null)
                dataBlockGpu.Dispose();
            if (dataVoxelGpu != null)
                dataVoxelGpu.Dispose();

            // Map types on CPU
            Console.WriteLine("Preparing model data for rendering");
            for(int i = 0; i < chunkCount; ++i)
            {
                uint curChunk = dataChunk[i];
                if ((curChunk >> 30) == 1)
                    dataChunk[i] = (1 << 30) | types[curChunk & 0x3FFFFFFF].renderIndex;
            }
            for (int i = 0; i < blockCount; ++i)
            {
                uint curBlock = dataBlock[i];
                if ((curBlock >> 30) == 1)
                    dataBlock[i] = (1 << 30) | types[curBlock & 0x3FFFFFFF].renderIndex;
            }

            for (int i = 0; i < voxelCount / 2; ++i)
            {
                uint curVoxelComp = dataVoxel[i];
                uint voxel1 = curVoxelComp & 0xFFFF;
                uint voxel2 = curVoxelComp >> 16;
                if ((voxel1 >> 15) != 0)
                    voxel1 = (1 << 15) | types[voxel1 & 0x7FFF].renderIndex;
                if ((voxel2 >> 15) != 0)
                    voxel2 = (1 << 15) | types[voxel2 & 0x7FFF].renderIndex;
                dataVoxel[i] = voxel1 | (voxel2 << 16);
            }

            Console.WriteLine("Model chunk count: " + chunkCount);
            dataChunkGpu = new StructuredBuffer(App.graphicsDevice, typeof(uint), (int)chunkCount, BufferUsage.None, ShaderAccess.ReadWrite);
            dataChunkGpu.SetData(dataChunk, 0, (int)chunkCount);


            Console.WriteLine("Model block count: " + blockCount);
            int bufferSize = blockCount > 0x1FFF0000 ? 0x3FFF0000 : (int)blockCount;
            dataBlockGpu = new StructuredBuffer(App.graphicsDevice, typeof(uint), bufferSize, BufferUsage.None, ShaderAccess.ReadWrite);
            App.helper.CopyIntoStructuredBufferLarge(dataBlockGpu, dataBlock, bufferSize);

            Console.WriteLine("Model voxel count: " + voxelCount);
            bufferSize = (voxelCount / 2) > 0x1FFF0000 ? 0x3FFF0000 : (int)(voxelCount / 2);
            dataVoxelGpu = new StructuredBuffer(App.graphicsDevice, typeof(uint), bufferSize, BufferUsage.None, ShaderAccess.ReadWrite);
            App.helper.CopyIntoStructuredBufferLarge(dataVoxelGpu, dataVoxel, (int)(voxelCount / 2));
        }

        public void Save()
        {
            if (string.IsNullOrWhiteSpace(curFilePath))
                return;

            Console.WriteLine("Saving...");

            using (var fileStream = File.Open(curFilePath + ".cvox", FileMode.Create))
            {
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, false))
                {
                    var entry = archive.CreateEntry("data");
                    using (var stream = entry.Open())
                    {
                        stream.WriteInt(3); // Version

                        stream.WriteInt(size.X);
                        stream.WriteInt(size.Y);
                        stream.WriteInt(size.Z);

                        stream.WriteInt(types.Length);
                        stream.WriteUInt(chunkCount);
                        stream.WriteUInt(blockCount);
                        stream.WriteUInt(voxelCount);

                        // Types
                        for (int i = 0; i < types.Length; ++i)
                        {
                            SaveVoxelType(stream, types[i]);
                        }

                        // Data
                        ReadOnlySpan<byte> dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataChunk));
                        stream.Write(dataAsBytes);
                        dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataBlock));
                        stream.Write(dataAsBytes);
                        if(dataVoxel.Length > 0x1FFF0000)
                        {
                            dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataVoxel, 0, 0x1FFF0000));
                            stream.Write(dataAsBytes);
                            dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataVoxel, 0x1FFF0000, dataVoxel.Length - 0x1FFF0000));
                            stream.Write(dataAsBytes);
                        }
                        else
                        {
                            dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataVoxel));
                            stream.Write(dataAsBytes);
                        }

                    }
                }
            }
            Console.WriteLine("Succesfully saved \"" + curFilePath + ".cvox\"");
        }

        public static ModelData Load(string fileName)
        {
            Point3 modelSize;
            VoxelType[] types;
            uint[] dataVoxel, dataBlock, dataChunk;
            uint chunkCount, blockCount, voxelCount;

            if (!File.Exists(fileName))
                return null;

            Console.WriteLine("Loading...");
            using (var fileStream = File.Open(fileName, FileMode.Open))
            {
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, false))
                {
                    var entry = archive.GetEntry("data");
                    using (var zipStream = entry.Open())
                    {
                        int version = zipStream.ReadInt();

                        int modelSizeX = zipStream.ReadInt();
                        int modelSizeY = zipStream.ReadInt();
                        int modelSizeZ = zipStream.ReadInt();
                        modelSize = new Point3(modelSizeX, modelSizeY, modelSizeZ);

                        int typeCount = zipStream.ReadInt();
                        chunkCount = zipStream.ReadUInt();
                        blockCount = zipStream.ReadUInt();
                        voxelCount = zipStream.ReadUInt();

                        // Types
                        types = new VoxelType[typeCount];
                        for (int i = 0; i < typeCount; ++i)
                        {
                            types[i] = App.worldHandler.voxelTypeHandler.ApplyVoxelType(LoadVoxelType(zipStream));
                        }

                        // Voxel data
                        dataChunk = new uint[chunkCount];
                        Span<byte> dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataChunk));
                        zipStream.ReadExactly(dataAsBytes);

                        dataBlock = new uint[blockCount];
                        dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataBlock));
                        zipStream.ReadExactly(dataAsBytes);

                        int voxelDataCount = (int)(voxelCount / 2);
                        dataVoxel = new uint[voxelDataCount];
                        if (voxelDataCount > 0x1FFF0000)
                        {
                            dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataVoxel, 0, 0x1FFF0000));
                            zipStream.ReadExactly(dataAsBytes);
                            dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataVoxel, 0x1FFF0000, (int)voxelDataCount - 0x1FFF0000));
                            zipStream.ReadExactly(dataAsBytes);
                        }
                        else
                        {
                            dataAsBytes = MemoryMarshal.AsBytes(new Span<uint>(dataVoxel));
                            zipStream.ReadExactly(dataAsBytes);
                        }

                        if (version < 3)
                        {
                            for (int i = 0; i < blockCount; ++i)
                            {
                                uint curBlock = dataBlock[i];
                                if ((curBlock >> 31) == 1)
                                    dataBlock[i] = ((curBlock & 0x3FFFFFFF) / 2) | (1u << 31);
                            }
                        }


                    }
                }
            }
            Console.WriteLine("Succesfully loaded \"" + fileName + "\"");
            return new ModelData(fileName, modelSize, types, dataChunk, dataBlock, dataVoxel, blockCount, voxelCount);
        }

        public static ModelData CreateFromWorldData(string fileName, WorldData worldData)
        {
            Point3 modelSize = worldData.actualSizeInVoxels;
            Point3 sizeInChunks = new Point3((modelSize.X + 15) / 16, (modelSize.Y + 15) / 16, (modelSize.Z + 15) / 16);

            uint chunkCount = (uint)(sizeInChunks.X * sizeInChunks.Y * sizeInChunks.Z);

            uint[] dataChunk = new uint[chunkCount];
            uint[] dataBlock = new uint[worldData.blockCount];
            Array.Copy(worldData.dataBlock, dataBlock, worldData.blockCount);
            uint[] dataVoxel = new uint[worldData.voxelCount / 2];
            Array.Copy(worldData.dataVoxel, dataVoxel, worldData.voxelCount / 2);

            uint[] typeMapping = new uint[App.worldHandler.voxelTypeHandler.typesRender.Count];
            for (int z = 0; z < sizeInChunks.Z; z++)
            {
                for (int y = 0; y < sizeInChunks.Y; y++)
                {
                    for (int x = 0; x < sizeInChunks.X; x++)
                    {
                        int worldChunkIndex = x + y * worldData.sizeInChunks.X + z * worldData.sizeInChunks.X * worldData.sizeInChunks.Y;
                        uint worldChunk = worldData.dataChunk[worldChunkIndex];
                        int modelChunkIndex = x + y * sizeInChunks.X + z * sizeInChunks.X * sizeInChunks.Y;
                        dataChunk[modelChunkIndex] = worldChunk;
                    }
                }
            }

            for (int i = 0; i < chunkCount; ++i)
            {
                uint curChunk = dataChunk[i];
                if ((curChunk >> 30) == 1)
                    typeMapping[curChunk & 0x3FFFFFFF] = uint.MaxValue;
            }
            for (int i = 0; i < worldData.blockCount; ++i)
            {
                uint curBlock = dataBlock[i];
                if ((curBlock >> 30) == 1)
                    typeMapping[curBlock & 0x3FFFFFFF] = uint.MaxValue;
            }

            for (int i = 0; i < worldData.voxelCount / 2; ++i)
            {
                uint curVoxelComp = dataVoxel[i];
                uint voxel1 = curVoxelComp & 0xFFFF;
                uint voxel2 = curVoxelComp >> 16;
                if ((voxel1 >> 15) != 0)
                    typeMapping[voxel1 & 0x7FFF] = uint.MaxValue;
                if ((voxel2 >> 15) != 0)
                    typeMapping[voxel2 & 0x7FFF] = uint.MaxValue;
            }


            List<VoxelType> voxelTypes = new List<VoxelType>();
            VoxelType newType = new VoxelType();
            newType.ID = "_";
            voxelTypes.Add(newType);
            int curMapIndex = 1;
            for (int i = 1; i < typeMapping.Length; ++i)
            {
                if (typeMapping[i] != 0)
                {
                    VoxelType existingType = App.worldHandler.voxelTypeHandler.typesById.FirstOrDefault(v => v.Value.renderIndex == i).Value;
                    voxelTypes.Add(existingType);
                    typeMapping[i] = (uint)curMapIndex++;
                }
            }

            for (int i = 0; i < chunkCount; ++i)
            {
                uint curChunk = dataChunk[i];
                if ((curChunk >> 30) == 1)
                    dataChunk[i] = (1 << 30) | typeMapping[curChunk & 0x3FFFFFFF];
            }
            for (int i = 0; i < worldData.blockCount; ++i)
            {
                uint curBlock = dataBlock[i];
                if ((curBlock >> 30) == 1)
                    dataBlock[i] = (1 << 30) | typeMapping[curBlock & 0x3FFFFFFF];
            }

            for (int i = 0; i < worldData.voxelCount / 2; ++i)
            {
                uint curVoxelComp = dataVoxel[i];
                uint voxel1 = curVoxelComp & 0xFFFF;
                uint voxel2 = curVoxelComp >> 16;
                if ((voxel1 >> 15) != 0)
                    voxel1 = (1 << 15) | typeMapping[voxel1 & 0x7FFF];
                if ((voxel2 >> 15) != 0)
                    voxel2 = (1 << 15) | typeMapping[voxel2 & 0x7FFF];
                dataVoxel[i] = voxel1 | (voxel2 << 16);
            }

            return new ModelData(fileName, modelSize, voxelTypes.ToArray(), dataChunk, dataBlock, dataVoxel, worldData.blockCount, worldData.voxelCount, true);
        }

        public static ModelData ImportFromVox(string filename)
        {
            Voxels.VoxelDataBytes dataImport = (Voxels.VoxelDataBytes)Voxels.VoxelImport.Import(filename);

            Point3 modelSize = new Point3(dataImport.Size.X, dataImport.Size.Z, dataImport.Size.Y);
            Point3 sizeInChunks = new Point3((modelSize.X + 15) / 16, (modelSize.Y + 15) / 16, (modelSize.Z + 15) / 16);

            uint chunkCount = (uint)(sizeInChunks.X * sizeInChunks.Y * sizeInChunks.Z);

            uint[] dataChunk = new uint[chunkCount];
            List<uint> dataBlock = new List<uint>();
            List<uint> dataVoxel = new List<uint>();

            Span<uint> newVoxels = stackalloc uint[32];
            Span<uint> newBlocks = stackalloc uint[64];


            uint[] coefficients = new uint[65];
            coefficients[64] = 1;
            for (int i = 64 - 1; i >= 0; --i)
            {
                coefficients[i] = 31 * coefficients[i + 1];
            }

            int mapSize = 1024 * 1024 * 32;
            BlockValue[] map = new BlockValue[mapSize];

            for (int c = 0; c < chunkCount; ++c)
            {
                Point3 chunkPos = new Point3(c % sizeInChunks.X, (c / sizeInChunks.X) % sizeInChunks.Y, c / (sizeInChunks.X * sizeInChunks.Y));
                bool isAnyBlock = false;
                for (int b = 0; b < 64; ++b)
                {
                    Point3 blockPosInChunk = new Point3(b % 4, (b / 4) % 4, b / 16);
                    bool isAnyVoxel = false;
                    uint hash = coefficients[0];
                    for (int v = 0; v < 64; v += 2)
                    {
                        Point3 voxelPosInBlock = new Point3(v % 4, (v / 4) % 4, v / 16);
                        Point3 voxelPos = chunkPos * 16 + blockPosInChunk * 4 + voxelPosInBlock;
                        uint typeImport1 = dataImport[new Voxels.XYZ(voxelPos.X, voxelPos.Z, voxelPos.Y)].Index;
                        uint typeImport2 = dataImport[new Voxels.XYZ(voxelPos.X + 1, voxelPos.Z, voxelPos.Y)].Index;
                        hash += coefficients[v + 1] * typeImport1;
                        hash += coefficients[v + 2] * typeImport2;

                        typeImport1 = typeImport1 | (typeImport1 > 0 ? (1u << 15) : 0);
                        typeImport2 = typeImport2 | (typeImport2 > 0 ? (1u << 15) : 0);

                        newVoxels[v / 2] = typeImport1 | (typeImport2 << 16);
                        isAnyVoxel |= typeImport1 > 0 || typeImport2 > 0;
                    }
                    isAnyBlock |= isAnyVoxel;
                    if (isAnyVoxel)
                    {
                        uint hashBounds = hash & ((uint)mapSize - 1);
                        int count = 0;
                        bool isNew = false;
                        uint voxelPointer = 0;
                        while (count < 250)
                        {
                            BlockValue value = map[hashBounds];
                            if (value.voxelsPointer == 0) // Found empty hash slot
                            {
                                uint newVoxelPointer = (uint)dataVoxel.Count;
                                map[hashBounds].hash = hash;
                                map[hashBounds].voxelsPointer = newVoxelPointer;
                                voxelPointer = newVoxelPointer;
                                isNew = true;
                            }
                            else // Fully written hash slot
                            {
                                if (map[hashBounds].hash == hash)
                                {
                                    bool isAllEqual = true;
                                    for (int i = 0; i < 32; ++i)
                                    {
                                        uint voxelComp = dataVoxel[(int)value.voxelsPointer + i];
                                        isAllEqual = isAllEqual && newVoxels[i] == voxelComp;
                                    }
                                    if (isAllEqual)
                                        voxelPointer = value.voxelsPointer;
                                }
                            }
                            if (voxelPointer != 0)
                                break;
                            hashBounds = (hashBounds + 1) & ((uint)mapSize - 1);
                            count++;
                        }
                        if (isNew)
                        {
                            dataVoxel.AddRange(newVoxels);
                        }
                        newBlocks[b] = (voxelPointer) | (2u << 30);
                    }
                    else
                        newBlocks[b] = 0;

                }
                dataChunk[c] = isAnyBlock ? ((uint)dataBlock.Count | (2u << 30)) : 0;
                if (isAnyBlock)
                    dataBlock.AddRange(newBlocks);
            }

            // Parse types
            VoxelType[] types = new VoxelType[dataImport.Colors.Length];
            for (int i = 0; i < dataImport.Colors.Length; i++)
            {
                VoxelType type = new();
                Vector3 colSRGB = new Vector3(dataImport.Colors[i].R, dataImport.Colors[i].G, dataImport.Colors[i].B) / 255;
                type.colorBase = new Vector3((float)Math.Pow(colSRGB.X, 2.2f), (float)Math.Pow(colSRGB.Y, 2.2f), (float)Math.Pow(colSRGB.Z, 2.2f));
                float emission = dataImport.Materials[i].emit * (float)Math.Pow(1 + dataImport.Materials[i].flux, 2) * 5;
                if (emission > 0)
                {
                    type.colorLayered.X = emission;
                    type.materialBase = MaterialTypeBase.Emissive;
                }
                else
                    type.materialBase = MaterialTypeBase.Diffuse;
                type.ID = null;
                types[i] = App.worldHandler.voxelTypeHandler.ApplyVoxelType(type);
            }

            return new ModelData(filename, modelSize, types, dataChunk, dataBlock.ToArray(), dataVoxel.ToArray(), (uint)dataBlock.Count, (uint)dataVoxel.Count * 2);
        }

        public static Dictionary<XYZ, ushort> MapColorsToPaletteIndices(Dictionary<XYZ, XYZ> voxels, HashSet<XYZ> uniqueColors, out List<XYZ> palette, int maxColors = 254)
        {
            Console.WriteLine($"Creating palette [{uniqueColors.Count} -> {maxColors}] (This might take some time)");
            double[][] colorVectors = uniqueColors
                .Select(c => new double[] { c.X, c.Y, c.Z })
                .ToArray();

            KMeans kmeans;
            if (uniqueColors.Count > 50000)
                kmeans = new MiniBatchKMeans(k: Math.Min(maxColors, uniqueColors.Count), 50000);
            else
                kmeans = new KMeans(k: Math.Min(maxColors, uniqueColors.Count));
            kmeans.Tolerance = 0.1f;
            var clusters = kmeans.Learn(colorVectors);

            Console.WriteLine("Applying palette (This might take some time)");
            palette = clusters.Centroids
                .Select(c =>new XYZ((int)c[0], (int)c[1], (int)c[2]))
                .ToList();

            var colorToIndex = new Dictionary<XYZ, ushort>();
            for (int i = 0; i < palette.Count; i++)
            {
                colorToIndex[palette[i]] = (ushort)(i + 1);
            }

            var voxelIndexMap = new Dictionary<XYZ, ushort>();
            foreach (var (pos, color) in voxels)
            {
                if (!colorToIndex.TryGetValue(color, out ushort index))
                {
                    // Find closest color in palette
                    int bestIndex = 0;
                    double bestDistance = double.MaxValue;

                    for (int i = 0; i < palette.Count; i++)
                    {
                        double dist = ColorDistance(color, palette[i]);
                        if (dist < bestDistance)
                        {
                            bestDistance = dist;
                            bestIndex = i;
                        }
                    }

                    index = (ushort)(bestIndex + 1);
                    colorToIndex[color] = index;
                }

                voxelIndexMap[pos] = index;
            }
            return voxelIndexMap;
        }

        private static double ColorDistance(XYZ a, XYZ b)
        {
            return Math.Max(Math.Pow(a.X - b.X, 2), Math.Max(Math.Pow(a.Y - b.Y, 2), Math.Pow(a.Z - b.Z, 2)));
        }

        public static ModelData ImportFromVL32(string filename, bool deleteFile = false)
        {

            using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            Point3 minPos = new Point3(999999), maxPos = new Point3(-999999);
            Dictionary<XYZ, XYZ> colorMap = new Dictionary<XYZ, XYZ>();
            HashSet<XYZ> uniqueColors = new HashSet<XYZ>();

            Console.WriteLine("Reading voxels from file");
            while (br.BaseStream.Position + 10 <= br.BaseStream.Length) // 3x int32 + 4 bytes = 16 bytes per voxel
            {
                int x = ReadBigEndianInt32(br);
                int y = ReadBigEndianInt32(br);
                int z = ReadBigEndianInt32(br);

                minPos.X = Math.Min(minPos.X, x);
                minPos.Y = Math.Min(minPos.Y, y);
                minPos.Z = Math.Min(minPos.Z, z);

                maxPos.X = Math.Max(maxPos.X, x);
                maxPos.Y = Math.Max(maxPos.Y, y);
                maxPos.Z = Math.Max(maxPos.Z, z);

                byte a = br.ReadByte();
                byte r = br.ReadByte();
                byte g = br.ReadByte();
                byte b = br.ReadByte();
                colorMap.Add(new XYZ(x, y, z), new XYZ(r, g, b));
                uniqueColors.Add(new XYZ(r, g, b));
            }
            fs.Close();
            if (deleteFile && File.Exists(filename))
            {
                try { File.Delete(filename); }
                catch { }
            }

            Point3 modelSize = (maxPos - minPos) + new Point3(1);
            int test = modelSize.Z;
            modelSize.Z = modelSize.Y;
            modelSize.Y = test;
            Console.WriteLine("Size of model: " + modelSize.X + " | " + modelSize.Y + " | " + modelSize.Z);
            Point3 sizeInChunks = new Point3((modelSize.X + 15) / 16, (modelSize.Y + 15) / 16, (modelSize.Z + 15) / 16);
            uint chunkCount = (uint)(sizeInChunks.X * sizeInChunks.Y * sizeInChunks.Z);

            List<XYZ> palette = new List<XYZ>();
            Dictionary<XYZ, ushort> typeMap = MapColorsToPaletteIndices(colorMap, uniqueColors, out palette, UiHeaderBar.meshImportPaletteSize);
            List<XYZ> solidVoxelPos = colorMap.Keys.ToList();

            bool[] chunkValid = new bool[chunkCount];
            Parallel.For(0, solidVoxelPos.Count, i =>
            {
                XYZ curPos = solidVoxelPos[i];
                XYZ chunkPosInModel = (curPos - new XYZ(minPos.X, minPos.Y, minPos.Z)) / 16;
                chunkValid[chunkPosInModel.X + chunkPosInModel.Z * sizeInChunks.X + chunkPosInModel.Y * sizeInChunks.X * sizeInChunks.Y] = true;
            });
            Console.WriteLine("Creating internal model data (This might take some time)");

            uint[] dataChunk = new uint[chunkCount];
            List<uint> dataBlock = new List<uint>();
            List<uint> dataVoxel = new List<uint>();

            Span<uint> newVoxels = stackalloc uint[32];
            Span<uint> newBlocks = stackalloc uint[64];

            uint[]coefficients = new uint[65];
            coefficients[64] = 1;
            for (int i = 64 - 1; i >= 0; --i)
            {
                coefficients[i] = 31 * coefficients[i + 1];
            }

            int mapSize = 1024 * 1024 * 32;
            BlockValue[] map = new BlockValue[mapSize];

            for (int c = 0; c < chunkCount; ++c)
            {
                if (!chunkValid[c])
                    continue;
                Point3 chunkPos = new Point3(c % sizeInChunks.X, (c / sizeInChunks.X) % sizeInChunks.Y, c / (sizeInChunks.X * sizeInChunks.Y));

                bool isAnyBlock = false;
                for (int b = 0; b < 64; ++b)
                {
                    Point3 blockPosInChunk = new Point3(b % 4, (b / 4) % 4, b / 16);
                    bool isAnyVoxel = false;
                    uint hash = coefficients[0];
                    for (int v = 0; v < 64; v += 2)
                    {
                        Point3 voxelPosInBlock = new Point3(v % 4, (v / 4) % 4, v / 16);
                        Point3 voxelPos = minPos + chunkPos * 16 + blockPosInChunk * 4 + voxelPosInBlock;
                        ushort typeImport1byte = 0;
                        if (!typeMap.TryGetValue(new Voxels.XYZ(voxelPos.X, voxelPos.Z, voxelPos.Y), out typeImport1byte))
                            typeImport1byte = 0;
                        ushort typeImport2byte = 0;
                        if (!typeMap.TryGetValue(new Voxels.XYZ(voxelPos.X + 1, voxelPos.Z, voxelPos.Y), out typeImport2byte))
                            typeImport2byte = 0;

                        hash += coefficients[v + 1] * typeImport1byte;
                        hash += coefficients[v + 2] * typeImport2byte;

                        uint typeImport1 = typeImport1byte | (typeImport1byte > 0 ? (1u << 15) : 0);
                        uint typeImport2 = typeImport2byte | (typeImport2byte > 0 ? (1u << 15) : 0);
                        newVoxels[v / 2] = typeImport1 | (typeImport2 << 16);
                        isAnyVoxel |= typeImport1 > 0 || typeImport2 > 0;
                    }
                    isAnyBlock |= isAnyVoxel;
                    if (isAnyVoxel)
                    {
                        uint hashBounds = hash & ((uint)mapSize - 1);
                        int count = 0;
                        bool isNew = false;
                        uint voxelPointer = 0;
                        while (count < 250)
                        {
                            BlockValue value = map[hashBounds];
                            if (value.voxelsPointer == 0) // Found empty hash slot
                            {
                                uint newVoxelPointer = (uint)dataVoxel.Count;
                                map[hashBounds].hash = hash;
                                map[hashBounds].voxelsPointer = newVoxelPointer;
                                voxelPointer = newVoxelPointer;
                                isNew = true;
                            }
                            else // Fully written hash slot
                            {
                                if (map[hashBounds].hash == hash)
                                {
                                    bool isAllEqual = true;
                                    for (int i = 0; i < 32; ++i)
                                    {
                                        uint voxelComp = dataVoxel[(int)value.voxelsPointer + i];
                                        isAllEqual = isAllEqual && newVoxels[i] == voxelComp;
                                    }
                                    if (isAllEqual)
                                        voxelPointer = value.voxelsPointer;
                                }
                            }
                            if (voxelPointer != 0)
                                break;
                            hashBounds = (hashBounds + 1) & ((uint)mapSize - 1);
                            count++;
                        }
                        if (isNew)
                        {
                            dataVoxel.AddRange(newVoxels);
                        }
                        newBlocks[b] = (voxelPointer) | (2u << 30);
                    }
                    else
                        newBlocks[b] = 0;
                }
                dataChunk[c] = isAnyBlock ? ((uint)dataBlock.Count | (2u << 30)) : 0;
                if (isAnyBlock)
                    dataBlock.AddRange(newBlocks);
                if (c % 2000 == 0)
                    Console.WriteLine("Converting chunks (" + c + " of " + chunkCount + ")");
            }

            Console.WriteLine("Converting to custom palette");
            VoxelType[] types = new VoxelType[palette.Count + 1];
            for (int i = 0; i < palette.Count; i++)
            {
                VoxelType type = new();
                Vector3 colSRGB = new Vector3(palette[i].X, palette[i].Y, palette[i].Z) / 255.0f;
                type.colorBase = new Vector3((float)Math.Pow(colSRGB.X, 2.2f), (float)Math.Pow(colSRGB.Y, 2.2f), (float)Math.Pow(colSRGB.Z, 2.2f));
                type.materialBase = MaterialTypeBase.Diffuse;
                type.ID = null;
                types[i + 1] = App.worldHandler.voxelTypeHandler.ApplyVoxelType(type);
            }

            Console.WriteLine("Succesfully imported \"" + filename + "\"");
            return new ModelData(filename, modelSize, types, dataChunk, dataBlock.ToArray(), dataVoxel.ToArray(), (uint)dataBlock.Count, ((uint)dataVoxel.Count) * 2u);
        }

        public static ModelData ImportFromMesh(string filename)
        {
            if (!File.Exists(filename))
                throw new FileNotFoundException("Mesh file not found.", filename);

            string fullPath = Path.GetFullPath(filename);
            string meshDirectory = Path.GetDirectoryName(fullPath);

            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string obj2VoxelPath = Path.Combine(appDirectory, "obj2voxel.exe");

            if (!File.Exists(obj2VoxelPath))
                throw new FileNotFoundException("obj2voxel executable not found in working directory.", obj2VoxelPath);

            // Create temporary output file
            string tempOutputFile = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid()}.vl32");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = obj2VoxelPath,
                    Arguments = $"\"{filename}\" \"{tempOutputFile}\" -r {UiHeaderBar.meshImportSize} -p {UiHeaderBar.meshImportPermutation}",
                    WorkingDirectory = meshDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var process = new Process();
                process.StartInfo = startInfo;

                process.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                        Console.WriteLine($"[obj2voxel] {args.Data}");
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                        Console.Error.WriteLine($"[obj2voxel ERROR] {args.Data}");
                };

                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit(1000 * 60 * 4); // Wait up to 4 minutes
                process.Dispose();

                if (!File.Exists(tempOutputFile))
                    throw new Exception("obj2voxel did not produce an output file.");

                return ImportFromVL32(tempOutputFile, true);
            }
            finally
            {
                if (File.Exists(tempOutputFile))
                {
                    try { File.Delete(tempOutputFile); }
                    catch { }
                }
            }
        }

        private static int ReadBigEndianInt32(BinaryReader br)
        {
            byte[] bytes = br.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        public void Dispose()
        {
            dataChunkGpu?.Dispose();
            dataBlockGpu?.Dispose();
            dataVoxelGpu?.Dispose();
        }
    }
}
