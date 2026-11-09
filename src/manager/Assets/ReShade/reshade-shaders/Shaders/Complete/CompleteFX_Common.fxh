#define vec2 float2
static const vec2 poissonDisk[64] =
{	
	vec2(-0.613392, 0.6174810),vec2(0.1700190, -0.040254),vec2(-0.299417, 0.7919250),vec2(0.6456800, 0.4932100),
	vec2(-0.651784, 0.7178870),vec2(0.4210030, 0.0270700),vec2(-0.817194, -0.271096),vec2(-0.705374, -0.668203),
	vec2(0.9770500, -0.108615),vec2(0.0633260, 0.1423690),vec2(0.2035280, 0.2143310),vec2(-0.667531, 0.3260900),
	vec2(-0.098422, -0.295755),vec2(-0.885922, 0.2153690),vec2(0.5666370, 0.6052130),vec2(0.0397660, -0.396100),
	vec2(0.7519460, 0.4533520),vec2(0.0787070, -0.715323),vec2(-0.075838, -0.529344),vec2(0.7244790, -0.580798),
	vec2(0.2229990, -0.215125),vec2(-0.467574, -0.405438),vec2(-0.248268, -0.814753),vec2(0.3544110, -0.887570),
	vec2(0.1758170, 0.3823660),vec2(0.4874720, -0.063082),vec2(-0.084078, 0.8983120),vec2(0.4888760, -0.783441),
	vec2(0.4700160, 0.2179330),vec2(-0.696890, -0.549791),vec2(-0.149693, 0.6057620),vec2(0.0342110, 0.9799800),
	vec2(0.5030980, -0.308878),vec2(-0.016205, -0.872921),vec2(0.3857840, -0.393902),vec2(-0.146886, -0.859249),
	vec2(0.6433610, 0.1640980),vec2(0.6343880, -0.049471),vec2(-0.688894, 0.0078430),vec2(0.4640340, -0.188818),
	vec2(-0.440840, 0.1374860),vec2(0.3644830, 0.5117040),vec2(0.0340280, 0.3259680),vec2(0.0990940, -0.308023),
	vec2(0.6939600, -0.366253),vec2(0.6788840, -0.204688),vec2(0.0018010, 0.7803280),vec2(0.1451770, -0.898984),
	vec2(0.0626550, -0.611866),vec2(0.3152260, -0.604297),vec2(-0.780145, 0.4862510),vec2(-0.371868, 0.8821380),
	vec2(0.2004760, 0.4944300),vec2(-0.494552, -0.711051),vec2(0.6124760, 0.7052520),vec2(-0.578845, -0.768792),
	vec2(-0.772454, -0.090976),vec2(0.5044400, 0.3722950),vec2(0.1557360, 0.0651570),vec2(0.3915220, 0.8496050),
	vec2(-0.620106, -0.328104),vec2(0.7892390, -0.419965),vec2(-0.545396, 0.5381330),vec2(-0.178564, -0.596057)
};
#undef vec2

float2 EncodeNormals(float3 normalInput)
{
	normalInput *= rcp(abs(normalInput.x) + abs(normalInput.y) + abs(normalInput.z));
	normalInput.xy = mad(normalInput.xy, 0.5f, 0.5f);
	return normalInput.xy;
}
 
float3 DecodeNormals(float2 encodedNormalInput)
{
	encodedNormalInput = mad(encodedNormalInput, 2.0f, -1.0f);
	 
	// https://twitter.com/Stubbesaurus/status/937994790553227264
	float3 normalOut = float3(encodedNormalInput.xy, 1.0 - abs(encodedNormalInput.x) - abs(encodedNormalInput.y));
	float t = saturate(-normalOut.z);
	normalOut.xy += normalOut.xy >= 0.0 ? -t : t;
	return normalize(normalOut);
}

#if __RENDERER__ > 0xa000
//Bitpack codec: more precise, and faster
float2 EncodeDepth(float value)
{
	// Convert float to float16
	static const uint floatBits = asuint(value);
	static const uint exponent = (floatBits >> 23) & 0x1FF;
	static const uint mantissa = floatBits & 0x7FFFFF;
	
	static const uint float16Bits = ((exponent - 112) << 10) | (mantissa >> 13);
	
	return float2(int(float16Bits & 0xFF), int((float16Bits >> 8) & 0xFF)) / 255;
}

float DecodeDepth(float2 value)
{
	value *= 255;
	// Unpack int8 values to form float16Bits
	static const uint float16Bits = uint(value.x) | (uint(value.y) << 8);
	
	// Reconstruct float16Bits into float16
	static const uint mantissa = (float16Bits & 0x3FF);
	static const uint exponent = ((float16Bits >> 10) & 0x1F) + 112;
	
	//This case is always positive. 0x0000000 for "-"
	static const uint floatBits = 0x8000000 | (exponent << 23) | (mantissa << 13);
	
	return asfloat(floatBits);
}

float encode2to1(in float a, in float b)
{
	uint uinta = asuint(a);
	uint uintb = asuint(b);

	return asfloat((uinta << 16) | uintb);
}

float decode1to2(in float input, out float a, out float b)
{
	uint uinti = asuint(input);
	uint uinta = uinti >> 16;
	uint uintb = uinti & 0xFF;
	
	a = asfloat(uinta);
	b = asfloat(uintb);

	return 0;
}

#else //dx9 does not support bitwise Ops

static const float rcp255 = 0.003921568627451f;
//floor-frac codec
float2 EncodeDepth(float depth)
{
	depth *= 255;
    const float floor = floor(depth);
    const float frac  = depth - floor;
    
    return float2(floor * rcp255, frac);
}

float DecodeDepth(float2 encodedDepth)
{
	//.x is the integer part, .y is the fraction.
	//.x is scaled down in the encoder. .y needs to be scaled by rcp(255) as well
	return mad(encodedDepth.y, rcp255, encodedDepth.x);
}

float encode2to1(in float a, in float b)
{
	float afrac = a / (LUM_MAX * UI_Exposure);
	float bfloor = floor(b) * 65536.0f;

	return bfloor + afrac;
}

float decode1to2(in float input, out float b, out float c)
{
	b = frac(input);
	c = input - b;

	return 0;
}

#endif

texture SSSR_BlueNoise <source="BlueNoise-64frames128x128.png";> { Width = 1024; Height = 1024; Format = RGBA8;};
sampler sSSSR_BlueNoise { Texture = SSSR_BlueNoise; AddressU = REPEAT; AddressV = REPEAT; MipFilter = Point; MinFilter = Point; MagFilter = Point; };

float WN(float2 co)
{
  return frac(sin(dot(co.xy ,float2(1.0,73))) * 437580.5453);
}

float3 WN3dts(float2 co, float HL)
{
	co += (Frame%HL)/120.3476687;
	return float3( WN(co), WN(co+0.6432168421), WN(co+0.19216811));
}

float IGN(float2 n)
{
    float f = 0.06711056 * n.x + 0.00583715 * n.y;
    return frac(52.9829189 * frac(f));
}

float3 IGN3dts(float2 texcoord, float HL)
{
	float3 Noise;
	const float2 seed = texcoord * BUFFER_SCREEN_SIZE + (Frame % HL) * 5.588238;
	Noise.r = IGN(seed);
	Noise.g = IGN(seed + 281.220051);
	Noise.b = IGN(seed + 449.269620);
	
	float3 OutColor = 1;
	sincos(Noise.x * PI * 2, OutColor.x, OutColor.y);
	OutColor.z = Noise.y * 2.0 - 0.5;
	OutColor  *= Noise.z;
	
	return OutColor * 0.5 + 0.5;
}

float3 BN3dts(float2 texcoord, float HL)
{
	texcoord *= BUFFER_SCREEN_SIZE;
	texcoord = texcoord%128;
	
	const float frame = Frame%HL;
	int2 F;
	F.x = frame%8;
	F.y = floor(frame/8)%8;
	F *= 128;
	texcoord += F;
	texcoord /= 1024;
	
	float3 Tex = tex2Dlod(sSSSR_BlueNoise, float4(texcoord,0,0)).rgb;
	return Tex;
}


float3 BN3dts(float2 texcoord, int iter)
{
	texcoord *= BUFFER_SCREEN_SIZE;
	texcoord = texcoord%128;
	
	const float frame = iter;
	int2 F;
	F.x = frame%8;
	F.y = floor(frame/8)%8;
	F *= 128;
	texcoord += F;
	texcoord /= 1024;
	
	float3 Tex = tex2Dlod(sSSSR_BlueNoise, float4(texcoord,0,0)).rgb;
	return Tex;
}

float3 UVtoPos(float2 texcoord)
{
	float3 scrncoord = float3(texcoord.xy*2-1, ReShade::GetLinearizedDepth(texcoord) * RESHADE_DEPTH_LINEARIZATION_FAR_PLANE);
	scrncoord.xy *= scrncoord.z;
	scrncoord.x *= AspectRatio;
	scrncoord.xy *= rad(fov);
	
	return scrncoord.xyz;
}

float3 UVtoPos(float2 texcoord, float depth)
{
	float3 scrncoord = float3(texcoord.xy*2-1, depth * RESHADE_DEPTH_LINEARIZATION_FAR_PLANE);
	scrncoord.xy *= scrncoord.z;
	scrncoord.x *= AspectRatio;
	scrncoord *= rad(fov);
	
	return scrncoord.xyz;
}

float2 PostoUV(float3 position)
{
    float2 scrnpos = position.xy / position.z;
    scrnpos.x /= AspectRatio;
    scrnpos /= rad(fov);
    
    return scrnpos / 2 + 0.5;
}
	
float3 ComputeNormal(float2 texcoord)
{
	float2 p = pix;
	float3 u2,d2,l2,r2;
	
	const float3 u = UVtoPos( texcoord + float2( 0, p.y));
	const float3 d = UVtoPos( texcoord - float2( 0, p.y));
	const float3 l = UVtoPos( texcoord + float2( p.x, 0));
	const float3 r = UVtoPos( texcoord - float2( p.x, 0));
	
	p *= 2;
	
	u2 = UVtoPos( texcoord + float2( 0, p.y));
	d2 = UVtoPos( texcoord - float2( 0, p.y));
	l2 = UVtoPos( texcoord + float2( p.x, 0));
	r2 = UVtoPos( texcoord - float2( p.x, 0));
	
	u2 = u + (u - u2);
	d2 = d + (d - d2);
	l2 = l + (l - l2);
	r2 = r + (r - r2);
	
	const float3 c = UVtoPos( texcoord);
	
	float3 v = u-c; float3 h = r-c;
	
	if( abs(d2.z-c.z) < abs(u2.z-c.z) ) v = c-d;
	if( abs(l2.z-c.z) < abs(r2.z-c.z) ) h = c-l;
	
	return normalize(cross( v, h));
}

float3 toYCC(float3 rgb)
{
	const float Y  = mad(rgb.r,  0.25f, mad(rgb.g, 0.5f, rgb.b *  0.25f));
	const float Co = mad(rgb.r,  0.50f,                  rgb.b * -0.50f);
	const float Cg = mad(rgb.r, -0.25f, mad(rgb.g, 0.5f, rgb.b * -0.25f));
	
	return float3(Y,Co,Cg);
}
float3 toRGB(float3 ycc)
{
	const float R = ycc.r + ycc.g - ycc.b;
	const float G = ycc.r + ycc.b;
	const float B = ycc.r - ycc.g - ycc.b;
	
	return float3(R,G,B);
}

float lum(in float3 color)
{
	return (color.r + color.g + color.b) * 0.33333333;
}

float3 ClampLuma(float3 color, float luma)
{
	const float L = lum(color);
	color /= L;
	color *= min(luma,L);
	return color;
}

static const float LinearGamma = 0.447;
static const float sRGBGamma = 2.233;

#define GetL() max(max(color.r, color.g), color.b)

float3 InvTonemapper(float3 color)
{
	const float3 L = GetL();
	color = color / ((1.0 + max(1-IT_Intensity,0.00001)) - L);
	return color;
}

float3 Tonemapper(float3 color)
{
	const float3 L = GetL();
	color = color / ((1.0 + max(1-IT_Intensity,0.00001)) + L);
	return color;
}

float InvTonemapper(float color)
{
	return color / (1.001 - color);
}

float3 RITM(in float3 color)
{
	return color/max(1 - color, 0.001);
}

float3 RTM(in float3 color)
{
	return color / (1 + color);
}

float3 AdjustSaturation(float3 color)
{
	return lerp(dot(color, float3(0.25, 0.50, 0.25)), color, UI_Saturation);
}

float4 SampleTextureCatmullRom9t(in sampler tex, in float2 uv, in float2 texSize)
{
    float2 samplePos = uv * texSize;
    float2 texPos1 = floor(samplePos - 0.5f) + 0.5f;

    float2 f = samplePos - texPos1;

    float2 w0 = f * (-0.5f + f * (1.0f - 0.5f * f));
    float2 w1 = 1.0f + f * f * (-2.5f + 1.5f * f);
    float2 w2 = f * (0.5f + f * (2.0f - 1.5f * f));
    float2 w3 = f * f * (-0.5f + 0.5f * f);

    float2 w12 = w1 + w2;
    float2 offset12 = w2 / (w1 + w2);
    
    float2 texPos0 = texPos1 - 1;
    float2 texPos3 = texPos1 + 2;
    float2 texPos12 = texPos1 + offset12;

    texPos0 /= texSize;
    texPos3 /= texSize;
    texPos12 /= texSize;

    float4 result = 0.0f;
    result += tex2Dlod(tex, float4(texPos0.x, texPos0.y,0,0)) * w0.x * w0.y;
    result += tex2Dlod(tex, float4(texPos12.x, texPos0.y,0,0)) * w12.x * w0.y;
    result += tex2Dlod(tex, float4(texPos3.x, texPos0.y,0,0)) * w3.x * w0.y;

    result += tex2Dlod(tex, float4(texPos0.x, texPos12.y,0,0)) * w0.x * w12.y;
    result += tex2Dlod(tex, float4(texPos12.x, texPos12.y,0,0)) * w12.x * w12.y;
    result += tex2Dlod(tex, float4(texPos3.x, texPos12.y,0,0)) * w3.x * w12.y;

    result += tex2Dlod(tex, float4(texPos0.x, texPos3.y,0,0)) * w0.x * w3.y;
    result += tex2Dlod(tex, float4(texPos12.x, texPos3.y,0,0)) * w12.x * w3.y;
    result += tex2Dlod(tex, float4(texPos3.x, texPos3.y,0,0)) * w3.x * w3.y;

    return result;
}

float4 tex2DClampedCatrom(in sampler tex, in float2 uv, in float2 texSize)
{
    float2 samplePos = uv * texSize;
    float2 texPos1 = floor(samplePos - 0.5f) + 0.5f;

    float2 f = samplePos - texPos1;

    float2 w0 = f * (-0.5f + f * (1.0f - 0.5f * f));
    float2 w1 = 1.0f + f * f * (-2.5f + 1.5f * f);
    float2 w2 = f * (0.5f + f * (2.0f - 1.5f * f));
    float2 w3 = f * f * (-0.5f + 0.5f * f);

    float2 w12 = w1 + w2;
    float2 offset12 = w2 / (w1 + w2);
    
    float2 texPos0 = texPos1 - 1;
    float2 texPos3 = texPos1 + 2;
    float2 texPos12 = texPos1 + offset12;

    texPos0 /= texSize;
    texPos3 /= texSize;
    texPos12 /= texSize;

    float4 result = 0.0f;
    result += tex2Dlod(tex, float4(texPos0.x, texPos0.y,0,0)) * max(0, w0.x * w0.y);
    result += tex2Dlod(tex, float4(texPos12.x, texPos0.y,0,0)) * max(0, w12.x * w0.y);
    result += tex2Dlod(tex, float4(texPos3.x, texPos0.y,0,0)) * max(0, w3.x * w0.y);

    result += tex2Dlod(tex, float4(texPos0.x, texPos12.y,0,0)) * max(0, w0.x * w12.y);
    result += tex2Dlod(tex, float4(texPos12.x, texPos12.y,0,0)) * max(0, w12.x * w12.y);
    result += tex2Dlod(tex, float4(texPos3.x, texPos12.y,0,0)) * max(0, w3.x * w12.y);

    result += tex2Dlod(tex, float4(texPos0.x, texPos3.y,0,0)) * max(0, w0.x * w3.y);
    result += tex2Dlod(tex, float4(texPos12.x, texPos3.y,0,0)) * max(0, w12.x * w3.y);
    result += tex2Dlod(tex, float4(texPos3.x, texPos3.y,0,0)) * max(0, w3.x * w3.y);

	float wsum = 
		max(0, w0.x * w0.y) + max(0, w12.x * w0.y) + max(0, w3.x * w0.y) +
		max(0, w0.x * w12.y) + max(0, w12.x * w12.y) + max(0, w3.x * w12.y) +
		max(0, w0.x * w3.y) + max(0, w12.x * w3.y) + max(0, w3.x * w3.y);
    return result/wsum;
}   