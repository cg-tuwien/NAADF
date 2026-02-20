uint offsetSrc, count, offsetDst;
StructuredBuffer<uint> srcData;
RWStructuredBuffer<uint> dstData;

[numthreads(64, 1, 1)]
void copyData(uint3 localID : SV_GroupThreadID, uint3 groupID : SV_GroupID, uint localIndex : SV_GroupIndex, uint3 globalID : SV_DispatchThreadID)
{
    if (globalID.x >= count)
        return;
    dstData[offsetDst + globalID.x] = srcData[offsetSrc + globalID.x];
}

technique Tech0
{
    pass CopyData
    {
        ComputeShader = compile cs_5_0 copyData();
    }
}
