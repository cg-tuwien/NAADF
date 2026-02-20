
RWStructuredBuffer<uint> voxelData;

uint offset, count;
uint mapping[300];

[numthreads(64, 1, 1)]
void mapTypes16(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    if (globalID.x >= count)
        return;
    uint data = voxelData[offset + globalID.x];
    uint dataNew = 0;
    uint type1 = data & 0xFFFF;
    uint type2 = data >> 16;
    if (type1 != 0)
        dataNew |= mapping[type1];
    if (type2 != 0)
        dataNew |= mapping[type2] << 16;
    voxelData[offset + globalID.x] = dataNew;
}

[numthreads(64, 1, 1)]
void mapTypesWithState(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    if (globalID.x >= count)
        return;
    uint data = voxelData[offset + globalID.x];
    if (data & 0x80000000)
        data = 0x80000000 | mapping[data & 0x7FFFFFFF];
    voxelData[offset + globalID.x] = data;
}

technique Tech0
{
    pass MapTypesState
    {
        ComputeShader = compile cs_5_0 mapTypesWithState();
    }

    pass MapTypes16
    {
        ComputeShader = compile cs_5_0 mapTypes16();
    }
}