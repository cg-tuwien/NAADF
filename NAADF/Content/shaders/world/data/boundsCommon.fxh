
#ifndef __COMMON_BOUNDS__
#define __COMMON_BOUNDS__


#define MASK_MX 0x3D //0b111101
#define MASK_PX 0x3E //0b111110
#define MASK_MY 0x37 //0b110111
#define MASK_PY 0x3B //0b111011
#define MASK_MZ 0x1F //0b011111
#define MASK_PZ 0x2F //0b101111

groupshared uint cachedCell[64];

uint checkMatchingBounds(uint neighbour, uint curVoxel, const uint shiftOffset, const uint shiftCount, const uint shiftMask)
{
    uint mask = 0;
    mask |= (((neighbour >> (shiftOffset + shiftCount * 0)) & shiftMask) >= ((curVoxel >> (shiftOffset + shiftCount * 0)) & shiftMask)) << 0; //-x  
    mask |= (((neighbour >> (shiftOffset + shiftCount * 1)) & shiftMask) >= ((curVoxel >> (shiftOffset + shiftCount * 1)) & shiftMask)) << 1; //+x
    mask |= (((neighbour >> (shiftOffset + shiftCount * 2)) & shiftMask) >= ((curVoxel >> (shiftOffset + shiftCount * 2)) & shiftMask)) << 2; //-y
    mask |= (((neighbour >> (shiftOffset + shiftCount * 3)) & shiftMask) >= ((curVoxel >> (shiftOffset + shiftCount * 3)) & shiftMask)) << 3; //+y
    mask |= (((neighbour >> (shiftOffset + shiftCount * 4)) & shiftMask) >= ((curVoxel >> (shiftOffset + shiftCount * 4)) & shiftMask)) << 4; //-z
    mask |= (((neighbour >> (shiftOffset + shiftCount * 5)) & shiftMask) >= ((curVoxel >> (shiftOffset + shiftCount * 5)) & shiftMask)) << 5; //+z
    
    return mask;
}

void addBoundsVoxelsOrBlocks(uint localIndex, uint mask, int directionOffset, uint boundsLocation, const uint stateLocation, const uint stateMask, inout uint curVoxel)
{
    uint neighbour = cachedCell[localIndex + directionOffset];
    if (((neighbour >> stateLocation) & stateMask) == 0)
    {
        if ((checkMatchingBounds(neighbour, curVoxel, 0, 2, 0x3) & mask) == mask)
            curVoxel += 1 << boundsLocation;
    }
}

void ComputeBounds4(int localIndex, int3 posInVolume, const uint stateLocation, const uint stateMask, uint curCell)
{
    GroupMemoryBarrierWithGroupSync();
    for (int i = 0; i < 3; i++)
    {
        if (posInVolume.x > 0)
            addBoundsVoxelsOrBlocks(localIndex, MASK_MX, -1, 0, stateLocation, stateMask, curCell);
        if (posInVolume.x + 1 < 4)
            addBoundsVoxelsOrBlocks(localIndex, MASK_PX, 1, 2, stateLocation, stateMask, curCell);
        cachedCell[localIndex] = curCell;
        GroupMemoryBarrierWithGroupSync();

        if (posInVolume.y > 0)
            addBoundsVoxelsOrBlocks(localIndex, MASK_MY, -4, 4, stateLocation, stateMask, curCell);
        if (posInVolume.y + 1 < 4)
            addBoundsVoxelsOrBlocks(localIndex, MASK_PY, 4, 6, stateLocation, stateMask, curCell);
        cachedCell[localIndex] = curCell;
        GroupMemoryBarrierWithGroupSync();

        if (posInVolume.z > 0)
            addBoundsVoxelsOrBlocks(localIndex, MASK_MZ, -16, 8, stateLocation, stateMask, curCell);
        if (posInVolume.z + 1 < 4)
            addBoundsVoxelsOrBlocks(localIndex, MASK_PZ, 16, 10, stateLocation, stateMask, curCell);
        cachedCell[localIndex] = curCell;
        GroupMemoryBarrierWithGroupSync();
    }
}


#endif // __COMMON_BOUNDS__