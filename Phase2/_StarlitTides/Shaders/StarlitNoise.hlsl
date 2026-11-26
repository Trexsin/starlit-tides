// =============================================================================
//  STARLIT TIDES - shared procedural noise helpers
//  Small, texture-free hash / value-noise / fbm functions used by the ocean
//  and sky shaders. "SN_" prefix avoids clashing with Unity's own functions.
// =============================================================================
#ifndef STARLIT_NOISE_INCLUDED
#define STARLIT_NOISE_INCLUDED

// Hash without sine (Dave Hoskins style) - stable on all GPUs.
float SN_Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float3 SN_Hash32(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

float SN_Hash13(float3 p3)
{
    p3 = frac(p3 * 0.1031);
    p3 += dot(p3, p3.zyx + 31.32);
    return frac((p3.x + p3.y) * p3.z);
}

float3 SN_Hash33(float3 p3)
{
    p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yxx) * p3.zyx);
}

// 2D value noise, smooth (quintic fade), range 0..1
float SN_Noise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float a = SN_Hash12(i);
    float b = SN_Hash12(i + float2(1, 0));
    float c = SN_Hash12(i + float2(0, 1));
    float d = SN_Hash12(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// 3D value noise, range 0..1
float SN_Noise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float n000 = SN_Hash13(i);
    float n100 = SN_Hash13(i + float3(1, 0, 0));
    float n010 = SN_Hash13(i + float3(0, 1, 0));
    float n110 = SN_Hash13(i + float3(1, 1, 0));
    float n001 = SN_Hash13(i + float3(0, 0, 1));
    float n101 = SN_Hash13(i + float3(1, 0, 1));
    float n011 = SN_Hash13(i + float3(0, 1, 1));
    float n111 = SN_Hash13(i + float3(1, 1, 1));
    float x00 = lerp(n000, n100, u.x);
    float x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x);
    float x11 = lerp(n011, n111, u.x);
    return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
}

// Fractal Brownian motion: several octaves of noise, each smaller and fainter.
float SN_Fbm2(float2 p, int octaves)
{
    float sum = 0, amp = 0.5, norm = 0;
    [loop] for (int i = 0; i < octaves; i++)
    {
        sum += SN_Noise2(p) * amp;
        norm += amp;
        p = p * 2.03 + float2(17.1, 9.7);
        amp *= 0.5;
    }
    return sum / norm;
}

float SN_Fbm3(float3 p, int octaves)
{
    float sum = 0, amp = 0.5, norm = 0;
    [loop] for (int i = 0; i < octaves; i++)
    {
        sum += SN_Noise3(p) * amp;
        norm += amp;
        p = p * 2.02 + float3(13.7, 5.3, 21.1);
        amp *= 0.5;
    }
    return sum / norm;
}

#endif // STARLIT_NOISE_INCLUDED
