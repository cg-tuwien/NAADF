#include "../../common/common.fxh"

struct VertexShaderInput
{
    float4 Position : SV_POSITION;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
};

StructuredBuffer<uint2> taaSampleAccum;
StructuredBuffer<uint4> firstHitData;

matrix WorldProjection;
int screenWidth, screenHeight;
float exposure, toneMappingFac;
bool showRayStep;


VertexShaderOutput MainVS(in VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput) 0;

    output.Position = mul(input.Position, WorldProjection);

    return output;
}

float4 MainPS(VertexShaderOutput input) : SV_Target
{
    int2 pixelPos = input.Position.xy;
    int pixelIndex = pixelPos.x + pixelPos.y * screenWidth;
    
    
    float3 curColor = float3(0, 0, 0);
    uint2 colSamples = taaSampleAccum[pixelIndex];
    float weight = f16tof32(colSamples.x & 0xFFFF);
    curColor = float3(f16tof32(colSamples.x >> 16), f16tof32(colSamples.y & 0xFFFF), f16tof32(colSamples.y >> 16)) / max(1, weight);
    
    if (showRayStep)
    {
        uint raySteps = firstHitData[pixelIndex].z & 0x7FFF;
        float intensity = raySteps * 0.01f;
        curColor = float3(intensity, intensity, intensity);
    }
    
#ifdef HDR
    curColor /= 10.0f;
#endif // HDR
    
    // Tone mapping
    float luminace = curColor.r * 0.2126 + curColor.g * 0.7152 + curColor.b * 0.0722;
    float3 tv = curColor / (toneMappingFac + curColor);
    float3 colorNormalized = lerp(curColor / (exposure + luminace), tv, tv);
    
#ifdef HDR
    colorNormalized *= 10.0f;
#endif // HDR
    
    return float4(colorNormalized, 1);
}

technique SpriteDrawing
{
	pass P0
	{
        VertexShader = compile vs_5_0 MainVS();
		PixelShader = compile ps_5_0 MainPS();
	}
};
