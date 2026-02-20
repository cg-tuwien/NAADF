
RWStructuredBuffer<uint> chunkData;
RWTexture3D<uint> chunkDataTex3D;
StructuredBuffer<uint> modelDataChunk, modelDataBlock, modelDataVoxel;

uint chunkPosX, chunkPosY, chunkPosZ;
uint sizeInVoxelsX, sizeInVoxelsY, sizeInVoxelsZ;
uint chunkSizeX, chunkSizeY, chunkSizeZ;
uint voxelGroupSize, worldSizeY;

uint modelSizeInChunksX, modelSizeInChunksY, modelSizeInChunksZ;

uint groupOffsetInChunksX, groupOffsetInChunksY, groupOffsetInChunksZ;
uint groupSizeInChunksX, groupSizeInChunksY;

uint getVoxelDataInModel(uint3 voxelPos)
{
    if (any(voxelPos >= uint3(sizeInVoxelsX, sizeInVoxelsY, sizeInVoxelsZ)))
        return 0;
    uint3 voxelPosInModel = voxelPos % (int3(modelSizeInChunksX, modelSizeInChunksY, modelSizeInChunksZ) * 16);
    uint modelIndexY = voxelPos.y / (modelSizeInChunksY * 16);
   
    // Get block from voxel pos in model
    uint3 chunkPosInModel = voxelPosInModel / 16;
    uint chunkIndexInModel = chunkPosInModel.x + chunkPosInModel.y * modelSizeInChunksX + chunkPosInModel.z * modelSizeInChunksX * modelSizeInChunksY;
    uint chunk = modelDataChunk[chunkIndexInModel];
    
    uint type = 0;
    
    if ((chunk >> 30) == 2)
    {
        uint3 modelBlockPosInChunk = (voxelPosInModel % 16) / 4;
        uint modelBlockIndex = modelBlockPosInChunk.x + modelBlockPosInChunk.y * 4 + modelBlockPosInChunk.z * 16;
        uint block = modelDataBlock[(chunk & 0x3FFFFFFF) + modelBlockIndex];
        if ((block >> 30) == 2)
        {
            uint3 modelVoxelPosInChunk = voxelPosInModel % 4;
            uint modelVoxelIndex = modelVoxelPosInChunk.x + modelVoxelPosInChunk.y * 4 + modelVoxelPosInChunk.z * 16;
            uint modelVoxelComp = modelDataVoxel[(block & 0x3FFFFFFF) + modelVoxelIndex / 2];
            type = (((modelVoxelIndex % 2) == 0) ? modelVoxelComp : (modelVoxelComp >> 16)) & 0x7FFF;
        }
        else if ((block >> 30) == 1)
            type = block & 0x3FFFFFFF;
    }
    else if ((chunk >> 30) == 1)
        type = chunk & 0x3FFFFFFF;
    
    if (modelIndexY > 0)
        type = 0;
    
    return type;
}

[numthreads(4, 4, 4)]
void fillChunkDataWithModelData16(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    uint groupIndex = groupID.x + groupID.y * groupSizeInChunksX + groupID.z * groupSizeInChunksX * groupSizeInChunksY;
    for (uint i = 0; i < 32; ++i)
    {
        uint i2 = i * 2;
        uint3 voxelPosInBlock = uint3(i2 % 4, (i2 / 4) % 4, i2 / 16);
        uint3 voxelPos = (uint3(groupOffsetInChunksX, groupOffsetInChunksY, groupOffsetInChunksZ) + groupID) * 16 + localID * 4 + voxelPosInBlock;
        
        uint voxel1 = getVoxelDataInModel(voxelPos);
        uint voxel2 = getVoxelDataInModel(voxelPos + uint3(1, 0, 0));
        
        voxel1 |= voxel1 > 0 ? (1 << 15) : 0;
        voxel2 |= voxel2 > 0 ? (1 << 15) : 0;
        
        chunkData[groupIndex * 2048 + localIndex * 32 + i] = voxel1 | (voxel2 << 16);
    }
}

technique Tech0
{
    pass CopyData16
    {
        ComputeShader = compile cs_5_0 fillChunkDataWithModelData16();
    }
}