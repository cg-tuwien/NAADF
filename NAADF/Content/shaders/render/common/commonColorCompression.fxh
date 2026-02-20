#ifndef __COMMON_COLOR_COMPRESSION__
#define __COMMON_COLOR_COMPRESSION__
#include "commonRayTracing.fxh"

#define MAX_COLOR_LEVELING 20

static const float COLOR_EXP = pow(2, 0.6f);
static const float COLOR_START = 1.0f / 64.0f;

static const float COLORS[32] =
{
    0,
    COLOR_START * pow(COLOR_EXP, 0),
    COLOR_START * pow(COLOR_EXP, 1),
    COLOR_START * pow(COLOR_EXP, 2),
    COLOR_START * pow(COLOR_EXP, 3),
    COLOR_START * pow(COLOR_EXP, 4),
    COLOR_START * pow(COLOR_EXP, 5),
    COLOR_START * pow(COLOR_EXP, 6),
    COLOR_START * pow(COLOR_EXP, 7),
    COLOR_START * pow(COLOR_EXP, 8),
    COLOR_START * pow(COLOR_EXP, 9),
    COLOR_START * pow(COLOR_EXP, 10),
    COLOR_START * pow(COLOR_EXP, 11),
    COLOR_START * pow(COLOR_EXP, 12),
    COLOR_START * pow(COLOR_EXP, 13),
    COLOR_START * pow(COLOR_EXP, 14),
    COLOR_START * pow(COLOR_EXP, 15),
    COLOR_START * pow(COLOR_EXP, 16),
    COLOR_START * pow(COLOR_EXP, 17),
    COLOR_START * pow(COLOR_EXP, 18),
    COLOR_START * pow(COLOR_EXP, 19),
    COLOR_START * pow(COLOR_EXP, 20),
    COLOR_START * pow(COLOR_EXP, 21),
    COLOR_START * pow(COLOR_EXP, 22),
    COLOR_START * pow(COLOR_EXP, 23),
    COLOR_START * pow(COLOR_EXP, 24),
    COLOR_START * pow(COLOR_EXP, 25),
    COLOR_START * pow(COLOR_EXP, 26),
    COLOR_START * pow(COLOR_EXP, 27),
    COLOR_START * pow(COLOR_EXP, 28),
    COLOR_START * pow(COLOR_EXP, 29),
    COLOR_START * pow(COLOR_EXP, 30),
};

static const float COLOR_DIF_PROB[31] =
{
    1 - 1.0f / pow(COLOR_EXP, 0),
    1 - 1.0f / pow(COLOR_EXP, 1),
    1 - 1.0f / pow(COLOR_EXP, 2),
    1 - 1.0f / pow(COLOR_EXP, 3),
    1 - 1.0f / pow(COLOR_EXP, 4),
    1 - 1.0f / pow(COLOR_EXP, 5),
    1 - 1.0f / pow(COLOR_EXP, 6),
    1 - 1.0f / pow(COLOR_EXP, 7),
    1 - 1.0f / pow(COLOR_EXP, 8),
    1 - 1.0f / pow(COLOR_EXP, 9),
    1 - 1.0f / pow(COLOR_EXP, 10),
    1 - 1.0f / pow(COLOR_EXP, 11),
    1 - 1.0f / pow(COLOR_EXP, 12),
    1 - 1.0f / pow(COLOR_EXP, 13),
    1 - 1.0f / pow(COLOR_EXP, 14),
    1 - 1.0f / pow(COLOR_EXP, 15),
    1 - 1.0f / pow(COLOR_EXP, 16),
    1 - 1.0f / pow(COLOR_EXP, 17),
    1 - 1.0f / pow(COLOR_EXP, 18),
    1 - 1.0f / pow(COLOR_EXP, 19),
    1 - 1.0f / pow(COLOR_EXP, 20),
    1 - 1.0f / pow(COLOR_EXP, 21),
    1 - 1.0f / pow(COLOR_EXP, 22),
    1 - 1.0f / pow(COLOR_EXP, 23),
    1 - 1.0f / pow(COLOR_EXP, 24),
    1 - 1.0f / pow(COLOR_EXP, 25),
    1 - 1.0f / pow(COLOR_EXP, 26),
    1 - 1.0f / pow(COLOR_EXP, 27),
    1 - 1.0f / pow(COLOR_EXP, 28),
    1 - 1.0f / pow(COLOR_EXP, 29),
    1 - 1.0f / pow(COLOR_EXP, 30),
};


// Refines compressed color to converge to actual color
void refineCompColor(inout uint compColor, inout uint2 rand, float actualColor)
{
    float curCol = COLORS[compColor];
    float prevCol = COLORS[compColor - 1];
    
    if ((actualColor - prevCol) / (curCol - prevCol) < nextRand(rand))
        compColor--;
}

// Compresses color into an exponential format with 5 bits per channel
uint compressColor(float3 color, inout uint2 rand)
{
    color = min(color, COLORS[31]);
    float3 colorSq = pow(abs(color * 64), 1.6666666f);
    uint compColorR = 1 + firstbithigh(max(1, uint(colorSq.r) * 2));
    uint compColorG = 1 + firstbithigh(max(1, uint(colorSq.g) * 2));
    uint compColorB = 1 + firstbithigh(max(1, uint(colorSq.b) * 2));
    
    refineCompColor(compColorR, rand, color.r);
    refineCompColor(compColorG, rand, color.g);
    refineCompColor(compColorB, rand, color.b);
   
    return compColorR | (compColorG << 5) | (compColorB << 10);
}

#endif // __COMMON_COLOR_COMPRESSION__