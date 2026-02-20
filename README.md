# NAADF: Globally Illuminated Voxel Worlds Accelerated with Nested Axis-Aligned Distance Fields
This voxel engine is an implementation of our research paper "NAADF: Globally Illuminated Voxel Worlds Accelerated with Nested Axis-Aligned Distance Fields".\
It supports dynamic entites and editing with a world size of up to 16384^3 voxels.

## Implementation Details

The engine is using the [MonoGame](https://monogame.net/) framework. In order to support compute shaders, the fork from [cpt-max](https://github.com/cpt-max/MonoGame) is used.

The data structure consists of 3 layers. 4^3^ voxels are grouped into blocks, and 4^3 blocks are again grouped into a chunk. Empty space is using AADFs to accelerate ray traversal. In contrast to Signed Distance Fields (SDF), AADFs are directional along each axis (x-, x+, y-, y+, z-, z+) resulting in significantly improved performance.

The engine has its own voxel format (*.cvox) which is simply the chunk, block and voxel buffer compressed as a ZIP archive.\
World generation happens on the GPU. Editing and entity logic is done on the CPU and then synchronized with the GPU.

### Points of interest
 - **Content/shaders/render/rayTracing.fxh:** Ray traversal logic using the NAADFs
 - **Content/shaders/render/versions/base/renderFirstHit.fx:** Primary ray traversal
 - **Content/shaders/render/versions/base/renderGlobalIllum.fx:** Secondary ray traversal
 - **Content/shaders/render/versions/base/renderSampleRefine.fx:** Temporal accumulation for resampling
 - **Content/shaders/render/versions/base/renderSpatialResampling.fx:** Spatial resampling
 - **Content/shaders/render/versions/base/renderTaaSampleReverse.fx:** Our TAA approach
 - **Content/shaders/world/data/chunkCalc.fx:** Data structure generation from raw voxels. Also contains AADF generation for voxels and blocks.
 - **Content/shaders/world/data/boundsCalc.fx:** AADF generation for chunks.
 - **World/Render/Versions/WorldRenderBase.cs:** Render logic on CPU
 - **World/Data/WorldData.cs:** Data structure logic on CPU

## Usage

Voxel models with the format_\*.vox_* and _\*.vl32_ voxel models can be imported. Meshes (_\*.obj_ and _\*.stl_) can also be voxelized directly.\
When doing any expensive operations (e.g. importing, saving, ...), look into the console window for progress.

Example voxel models can be found from [vengi-voxel](https://github.com/vengi-voxel/voxels).\
The default scene is [Oasis](https://github.com/Phyronnaz/VoxelAssets/tree/master/Oasis) from [Hard Cover](https://x.com/Hard_Cover).

### Controls

 - **WASD**: Main movement
 - **Shift**: Speed increase
 - **Left Control**: Down
 - **Space**: Up
 - **Left/Right Arrows**: Sun inclination
 - **F1**: Toggle GUI
 - **Right click**: Camera rotation

## Building from source

### Requirements
 - Visual Studio with the ".Net desktop development" package, ".Net Runtime 8.0" and ".Net Runtime 6.0" component
 - "MonoGame Framework C# project templates" from extensions

In "Settings.cs" and "Content/shaders/settings.fxh" some build flags can be configured.

**Currently only Windows is supported.**
