//Stochastic Screen Space Ray Tracing
//Written by MJ_Ehsan for Reshade
//Version 1.3 - Main
/*******************************************************************
 Copyright (c) MohammadJavad Ehsan. All rights reserved.
    
    * Unauthorized copying of this file, via any medium is strictly prohibited
 	* Proprietary and confidential

 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL
 THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 DEALINGS IN THE SOFTWARE.
 *******************************************************************/

///////////////Include/////////////////////

#include "ReShade.fxh"

uniform float Timer < source = "timer"; >;
uniform float Frame < source = "framecount"; >;

static const float2 pix = float2(BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT);

#define FAR_PLANE RESHADE_DEPTH_LINEARIZATION_FAR_PLANE 

#define PI 3.1415927
#define PI2 PI*2
static const float PI2div360 = PI/180;
#define rad(x) x*PI2div360

///////////////Include/////////////////////
///////////////PreProcessor-Definitions////

#include "CompleteRT_UI.fxh"
#include "CompleteRT_Configs.fxh"

#include "CompleteFX_Common.fxh"

///////////////PreProcessor-Definitions////
///////////////Textures-Samplers///////////
#define POINT_FILTER MipFilter = Point; MinFilter = Point; MagFilter = Point;
//1
//Blue noise texture from CompleteFX_Common.fxh
//2
#define sTexColor ReShade::BackBuffer
//3
#define LDepth ReShade::GetLinearizedDepth
//4
#if !C_RT_USE_LAUNCHPAD_MOTIONS
texture texMotionVectors     { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = RG16F; };
sampler SamplerMotionVectors { Texture = texMotionVectors; AddressU = Clamp; AddressV = Clamp; POINT_FILTER };
float2 sampleMotion(float2 texcoord){return tex2D(SamplerMotionVectors, texcoord).rg;}
#else
namespace Deferred
{
	texture MotionVectorsTex        { Width = BUFFER_WIDTH;   Height = BUFFER_HEIGHT;   Format = RG16F;     };
	sampler sMotionVectorsTex       { Texture = MotionVectorsTex; };
}
float2 sampleMotion(float2 texcoord){return tex2D(Deferred::sMotionVectorsTex, texcoord).rg;}
#endif
//5
texture RT_POGColTex  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = RGBA16f; };
sampler sRT_POGColTex { Texture = RT_POGColTex; };
//6
texture RT_FilterTex0  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; };
sampler sRT_FilterTex0 { Texture = RT_FilterTex0; POINT_FILTER };
//7
texture RT_FilterTex1  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP;};
sampler sRT_FilterTex1 { Texture = RT_FilterTex1; POINT_FILTER };
//8
texture RT_HistoryTex1  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; };
sampler sRT_HistoryTex1 { Texture = RT_HistoryTex1; };
//9
texture RT_HistoryTex0  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; };
sampler sRT_HistoryTex0 { Texture = RT_HistoryTex0; };
//10
texture RT_NormTex  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = RGBA8; };
sampler sRT_NormTex { Texture = RT_NormTex; POINT_FILTER };
//11
texture RT_RoughnessTex  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = RGBA16f; };
sampler sRT_RoughnessTex { Texture = RT_RoughnessTex; POINT_FILTER };

#if C_RT_HYBRID_MODE && __RENDERER__ >= 0xa000
//DX9 fix (todo) => pack with floor-frac. Multiply by 65536 (or 4096?) before packing for even precision.
//12
texture RT_FilterTex0_2  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP;};
sampler sRT_FilterTex0_2 { Texture = RT_FilterTex0_2; POINT_FILTER };
//13
texture RT_FilterTex1_2  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP;};
sampler sRT_FilterTex1_2 { Texture = RT_FilterTex1_2; POINT_FILTER };
//14
texture RT_HistoryTex0_2  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; };
sampler sRT_HistoryTex0_2 { Texture = RT_HistoryTex0_2; };
//15
texture RT_HistoryTex1_2  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; };
sampler sRT_HistoryTex1_2 { Texture = RT_HistoryTex1_2; };

#endif
//16-12
texture RT_HLTex0  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; };
sampler sRT_HLTex0 { Texture = RT_HLTex0; };
//17-13
texture RT_HLTex1  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; };
sampler sRT_HLTex1 { Texture = RT_HLTex1; };

#define LowResMul (RES_M)
//18-14
texture RT_LowResDepthTex  { Width = BUFFER_WIDTH * LowResMul; Height = BUFFER_HEIGHT * LowResMul; Format = R16f; };
sampler sRT_LowResDepthTex { Texture = RT_LowResDepthTex; };
//19-15
texture RT_HDRBackBufferTex  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; MipLevels = 4; };
sampler sRT_HDRBackBufferTex { Texture = RT_HDRBackBufferTex; AddressU = BORDER; AddressV = BORDER; };
//20-16
texture RT_TReservoire  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; };
sampler sRT_TReservoire { Texture = RT_TReservoire; MipFilter = Point; MagFilter = Point; };
//21-17
texture RT_TReservoireHistory  { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = TexP; };
sampler sRT_TReservoireHistory { Texture = RT_TReservoireHistory; MipFilter = Point; MagFilter = Point; };

///////////////Textures-Samplers///////////
///////////////UI//////////////////////////
///////////////UI//////////////////////////
///////////////Functions///////////////////

int getReuseSampleCount()
{
#if __RENDERER__ >= 0xa000
 #if C_RT_UI_DIFFICULTY == 1
	return UI_ReuseSamples;
 #else
	return exp2(max(0, UI_QualityPreset));
 #endif
#else
	return 1;
#endif
}

float getResolutionScale()
{
#if C_RT_UI_DIFFICULTY
	float s = UI_ResolutionScale == 1 ? 0.5 : 1.0;
#else
	float s = UI_QualityPreset > 2 ? 1.0 : 0.5;
#endif
	return clamp(s, 1e-6, 1);
}

float GetRoughTex(float2 uv, float4 normal)
{
	if(!UI_Mode||C_RT_HYBRID_MODE)
	{
		if(!NO_ROUGH_TEX) return tex2D(sRT_RoughnessTex, uv).r + 1e-7;
		else return UI_Roughness + 1e-7; 
	}
	else return 1e-7;//RoughnessTex
}

float4 SobelBump(float2 texcoord, float height)
{
	if(height == 0 && UI_Roughness == 0) return float4(0,0,1,0);
	float2 p = pix * exp2(-clamp(LDepth(texcoord), 1e-7, 1.0) * 2.0);
	float2 
	s  = float2(lum(tex2D(sTexColor, texcoord + float2(   0,   0)).rgb).xx) * float2( 0, 0);//c
	s += float2(lum(tex2D(sTexColor, texcoord + float2( p.x, p.y)).rgb).xx) * float2( 1, 1);//rd
	s += float2(lum(tex2D(sTexColor, texcoord + float2(-p.x,-p.y)).rgb).xx) * float2(-1,-1);//lu
	s += float2(lum(tex2D(sTexColor, texcoord + float2( p.x,   0)).rgb).xx) * float2( 2, 0);//r
	s += float2(lum(tex2D(sTexColor, texcoord + float2(-p.x,   0)).rgb).xx) * float2(-2, 0);//l
	s += float2(lum(tex2D(sTexColor, texcoord + float2(   0, p.y)).rgb).xx) * float2( 0, 2);//d
	s += float2(lum(tex2D(sTexColor, texcoord + float2(   0,-p.y)).rgb).xx) * float2( 0,-2);//u
	s += float2(lum(tex2D(sTexColor, texcoord + float2( p.x,-p.y)).rgb).xx) * float2( 1,-1);//ru
	s += float2(lum(tex2D(sTexColor, texcoord + float2(-p.x, p.y)).rgb).xx) * float2(-1, 1);//ld
	
	float3 bump = float3(s.xy * height, 1);
	bump = normalize(bump);
	
	float roughness = sqrt(length(s));
	float minR = UI_Roughness * UI_Roughness;
	float maxR = sqrt(UI_Roughness);
	roughness  = roughness * (maxR - minR) + minR;
	
	return float4(bump, roughness);
}

float3 BlendBump(float3 n1, float3 n2)
{
	n1.z++;
	return n1 * dot(n1, n2) / n1.z - n2;
}

bool IsSaturated(float2 coord)
{
	return coord.x > 1 || coord.x < 0 || coord.y > 1 || coord.y < 0;
}

#define VARIANCE_INTERSECTION_MAX_T 1
float3 ClipToAABB( float3 inHistoryColor, float3 inCurrentColor, float3 inBBCenter, float3 inBBExtents )
{
	float3 direction = inCurrentColor - inHistoryColor;
	direction = direction == 0 ? 1e-7 : direction;
	const float3 intersection = ((inBBCenter - sign(direction) * inBBExtents) - inHistoryColor) / direction;
	const float3 possibleT = (intersection >= 0.0f) ? intersection : VARIANCE_INTERSECTION_MAX_T + 1.0f;
	const float3 t = min(VARIANCE_INTERSECTION_MAX_T, min(possibleT.x, min(possibleT.y, possibleT.z)));
	
	return float3(t < VARIANCE_INTERSECTION_MAX_T ? (inHistoryColor + direction * t) : inHistoryColor);
}

float GetVariance(float2 texcoord, sampler color, float HL)
{
	float2 p = pix/2;
	float Input;
	Input  = tex2D(color, texcoord + p).a;
	Input += tex2D(color, texcoord - p).a;
	Input += tex2D(color, texcoord + float2(p.x, -p.y)).a;
	Input += tex2D(color, texcoord - float2(p.x, -p.y)).a;
	Input /= 4;
	const float mul = 1 + max(0, 16 - HL);
	
	return sqrt(Input) * mul;
}

void GetNormalAndDepth(in float2 texcoord, out float3 normal, out float depth)
{
	texcoord /= getResolutionScale();
	float4 Geometry = tex2Dlod(sRT_NormTex, float4(texcoord,0,0));
	normal = DecodeNormals(Geometry.xy);
	depth  = DecodeDepth  (Geometry.zw);
}

float GetWeights(float wn, float wd, float clum, float slum, float lT)
{
	float wl = abs(clum - slum) * lT;
	return saturate(exp(-wl - wd) * wn);
}

float getDepthFade(float depth)
{
	float close_plane = 0.3 * sqrt(UI_DepthFade);
	depth = saturate(depth - close_plane);
	if(UI_FadeMode==0)
	return saturate(pow(abs(UI_DepthFade * UI_DepthFade * UI_DepthFade * UI_DepthFade), depth));
	if(UI_FadeMode==1)
	return saturate((1 - depth / max(1e-7, InvTonemapper(UI_DepthFade))));
	return 0;
}

//cosine weighted hemisphere
float3 getCosineLambert(in float3 noise, float3 normal, out float weight)
{
	noise *= 0.999; //to make sure it's [0,1) and not [0,1]   
    float cosTheta2 = noise.x;
    float cosTheta = sqrt(cosTheta2);
    float sinTheta = sqrt(1.0 - cosTheta2);
	
    float phi = 2.0 * PI * noise.y;
    
    // Spherical to cartesian
    float3 t = normalize(cross(normal.yzx, normal));
    float3 b = cross(normal, t);
    
	float3 l = (t * cos(phi) + b * sin(phi)) * sinTheta + normal * cosTheta;
    
    // Sample weight
    float pdf = dot(l, normal);
    weight = 0.5 / (pdf + 1e-6);
    weight = saturate(weight);
    noise = -l;
    return noise;
}

float3 getHemisphereGGXSample(float2 noise, float3 normal, float3 eyedir, float roughness, out float weight)
{
	noise *= 0.999; 
	float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    
    float epsilon = clamp(noise.x, 0.001, 1.);
    float cosTheta2 = (1. - epsilon) / (epsilon * (alpha2 - 1.) + 1.);
    float cosTheta = sqrt(cosTheta2);
    float sinTheta = sqrt(1. - cosTheta2);
    
    float phi = 2.0 * PI * noise.y;
    
    // Spherical to cartesian
    float3 t = normalize(cross(normal.yzx, normal));
    float3 b = cross(normal, t);
    
	float3 microNormal = (t * cos(phi) + b * sin(phi)) * sinTheta + normal * cosTheta;
    
    float3 l = reflect(-eyedir, microNormal);
    
    // Sample weight
    float den = (alpha2 - 1.) * cosTheta2 + 1.;
    float D = alpha2 / (PI * den * den);
    float pdf = D * cosTheta / (4. * dot(microNormal, eyedir));
    weight = (.5 / PI) / (pdf + 1e-6);
    
    if (dot(l, normal) < 0.)
        weight = 0.;
    
    return -l;
}

float3 ggx(float3 n, float3 v, float3 l, float roughness, float3 F0)
{
	roughness = max(1e-7, roughness);
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    
    float dotNL = saturate(dot(n, l));
    float dotNV = saturate(dot(n, v));
    
    float3 h = normalize(v + l);
    float dotNH = saturate(dot(n, h));
    float dotLH = saturate(dot(l, h));
    
    // GGX microfacet distribution function
    float den = (alpha2 - 1.) * dotNH * dotNH + 1.;
    float D = alpha2 / (PI * den * den);
    
    // Fresnel with Schlick approximation
    //float3 F = F0 + (1.0 - F0) * pow(1. - dotLH, 5.);
    
    // Smith joint masking-shadowing function
    float k = .5 * alpha;
    float G = 1. / ((dotNL * (1.0 - k) + k) * (dotNV * (1. - k) + k));
    
    return D * G;
}

float3 getTemporalJitter()
{
	float2 Jitter = poissonDisk[int(Frame % 64)] - 0.5;
	Jitter *= (1 - getResolutionScale());
	float  jitterLength = length(Jitter);
	
	Jitter  = Jitter * pix * TS_JitterUpscale;
	return float3(Jitter, jitterLength);
}

///////////////Functions///////////////////
///////////////Vertex Shader///////////////
///////////////Vertex Shader///////////////
///////////////Pixel Shader////////////////

struct i
{
	float4 vpos : SV_Position;
	float2 uv : TexCoord0;
};

void GBuffer1(i i, out float4 Geometry : SV_Target0)
{
	//if(i.uv.x > getResolutionScale() || i.uv.y > getResolutionScale())discard;
	i.uv += getTemporalJitter().xy;
	//i.uv /= getResolutionScale();
	Geometry.xyz = ComputeNormal(i.uv);
	Geometry.w = clamp(LDepth(i.uv), 1e-7, 1.0);
}

void SmoothNormals
(
	i i
	, out float4 Geometry      : SV_Target0
	, out float4 HQGeometry    : SV_Target1
	, out float4 Roughness     : SV_Target2
	, out float4 HDRBackBuffer : SV_Target3
)
{
	float4 csample = tex2Dlod(sRT_FilterTex0, float4(i.uv,0,0));
	Geometry = csample;
	
	if(SMOOTHNORMALS_ENABLED && UI_SmoothNormals >= 1)
	{
		float2 offset;
		float angle = PI2 / SN_DirCount;
		
		float2 p = pix * SN_Radius / UI_SmoothNormals;
		float4 nsample;
		float  wSum;
		
		[loop]for(float direction = 0; direction < PI2; direction += angle)
		{
			sincos(direction, offset.x, offset.y);
			offset *= p;
			
			[loop]for(float distance = 1; distance <= UI_SmoothNormals; distance++)
			{
				nsample = tex2Dlod(sRT_FilterTex0, float4(mad(offset, distance, i.uv), 0, 0));
				float dnormals = dot(csample.xyz, nsample.xyz);
				if(dnormals > SN_NThreshold && abs(csample.w - nsample.w) < SN_DThreshold)//break;
				{
					wSum++;
					Geometry.xyz += nsample.xyz;
					break;
				}
			}
		}
		
		Geometry.xyz /= (wSum+1);
		Geometry.xyz = normalize(Geometry.xyz);
	}
	
	float4 BumpNRoughness = SobelBump(i.uv, UI_BumpStrength);
	Geometry.xyz = BlendBump(Geometry.xyz, BumpNRoughness.xyz);
	HQGeometry = Geometry;
	
	Roughness.x = BumpNRoughness.w;
	Roughness.yzw = Geometry.xyz;
	
	Geometry.xy  = EncodeNormals(Geometry.xyz);
	Geometry.zw  = EncodeDepth(Geometry.w);
	
	
	HDRBackBuffer.rgb = tex2D(sTexColor, i.uv).rgb;
	HDRBackBuffer.a   = lum(HDRBackBuffer.rgb);
	HDRBackBuffer.rgb = InvTonemapper(HDRBackBuffer.rgb);
}

void CopyGBufferLowRes(i i, out float Depth : SV_Target0)
{
	Depth = clamp(LDepth(i.uv), 1e-7, 1.0) * FAR_PLANE;
}

#define LDepthLoRes(texcoord) tex2Dlod(sRT_LowResDepthTex, float4(texcoord.xy, 0, 0)).r

void Sky(i i, out float4 SkyColor : SV_Target0)
{
	if(UI_SkyColorIntensity <= 0 || (!UI_Mode && !C_RT_HYBRID_MODE))discard;
	if(i.vpos.x >= 1 || i.vpos.y >= 1)discard;
	SkyColor = 0;

	float4 Color = 0;
	
	if(UI_SkyColorMode)
	{
		static const float2 screen_size   = BUFFER_SCREEN_SIZE;
		static const float2 search_iter   = float2(floor(9.0 * AspectRatio), 9.0);
		static const float2 search_offset = screen_size / search_iter;
		float2 p = pix;
		
		[loop]for(float x = 1.0; x <= search_iter.x; x++){
		[loop]for(float y = 1.0; y <= search_iter.y; y++)
		{
			float2 pixel = search_offset * float2(x,y) * p;
			
			float depth = LDepthLoRes(pixel);
			if(depth > (SkyDepth - 0.01))
				Color += float4(tex2Dlod(sTexColor, float4(pixel,0,0)).rgb, 1);
		}}
		Color.rgb /= (Color.a + 1e-6);
		Color.a   /= float(search_iter.x * search_iter.y + 1e-6);
	}
	
	if(i.vpos.x < 1 && i.vpos.x >= 0 && i.vpos.y < 1 && i.vpos.y >= 0)
	{
		SkyColor.a = lerp(0.0, 0.01, Color.a);
		
		SkyColor.rgb = Color.rgb;
		if(UI_SkyColorMode)SkyColor.rgb *= UI_SkyColorTint;
		else SkyColor.rgb = UI_SkyColorTint;
	}
}

float4 getSkyColor()
{
	float4 SkyColor = tex2Dfetch(sRT_HDRBackBufferTex, 0);
	SkyColor = InvTonemapper(SkyColor.rgb);
	SkyColor = ClampLuma(SkyColor.rgb, LUM_MAX) * UI_SkyColorIntensity;
	return SkyColor;
}

void DoRayMarch(bool mode, float3 noise, float3 position, float3 raydir, out float3 Reflection, out float HitDistance, out bool IsHit) 
{
	float2 UVraypos; float Error; bool hit;
	const float bias = -position.z * rcp(FAR_PLANE);
	float2 p = pix; float plength = length(p);
	float2 halfp = p / 2.0;
	
	float raydepth = -UI_RayDepth * 2 * (1 - min(position.z / FAR_PLANE, 0.8));
	
	float RaySteps, RayLength;
#if C_RT_UI_DIFFICULTY == 0
	float2 Q_Preset[6] = 
	{
		float2(13,  0.30),
		float2(17,  0.40),
		float2(65,  1.00),
		float2(161, 1.00),
		float2(321, 1.00),
		float2(501, 1.00),
	};
	RaySteps = Q_Preset[UI_QualityPreset].x;
	RayLength = mode ? Q_Preset[UI_QualityPreset].y : 1.3;
#else
	RaySteps = min(1648, UI_RaySteps);
	RayLength = mode ? UI_RayLength * 1.3 : 1.3;
#endif
	float steplength = (1+(noise.x * STEPNOISE));
	float RayInc = 1 + 1 / sqrt(RaySteps);
	float l;
	float sl = 1;
	//calculate ray length with init step length of 1,
	//then divide the init step length by it so the ray length is 1
	[loop]for(int i = 0; i < RaySteps; i++)
	{
		l += sl;
		sl *= RayInc;
	}
	steplength /= length(float3(l.xxx));
	steplength *= (RayLength * RayLength * FAR_PLANE * 1.5);
	steplength = max(plength, steplength);
	
	float3 raypos = position + raydir * steplength;
	
	[loop]for(int i = 0; i < RaySteps; i++)
	{
		UVraypos = PostoUV(raypos);
		float2 depthUV = round(UVraypos * BUFFER_SCREEN_SIZE) / BUFFER_SCREEN_SIZE;
		Error = LDepthLoRes(depthUV) - raypos.z;
		
		if(Error < bias && Error > raydepth * steplength)
		{
			IsHit = 1.0;
			break;
		}
		
		raypos = mad(raydir, steplength, raypos);
		if(raypos.z < 0.0)break;
		steplength *= RayInc;
	}
	HitDistance = IsHit ? distance(raypos, position) : FAR_PLANE;
	float3 HitNormal = DecodeNormals(tex2D(sRT_NormTex, UVraypos.xy).xy);
	float  HitFacing = dot(raydir, HitNormal);

	Reflection  = tex2Dlod(sRT_HDRBackBufferTex, float4(UVraypos.xy,0,0)).rgb * IsHit;
	if(lum(Reflection) == 0)IsHit = 0;
	Reflection *= ((HitFacing > 0 || !mode) ? 1 : 0.15);

	if(mode)
	{
		if(UI_MaskSky)
			Reflection *= raypos.z < (SkyDepth * FAR_PLANE);
			
		if(UI_MaskDirect < 1)
		{
			float SDRReflection = lum(tex2Dlod(sTexColor, float4(UVraypos.xy,0,0)).rgb);
			Reflection *= SDRReflection < UI_MaskDirect;
		}
	}
}

float3 getNoise(float2 uv, float2 HL, bool mode)
{
	float h = mode ? UI_MaxFrames : HL.g;
	float3 bn = BN3dts(uv, h);
	if(h > 64) 
	return frac(bn + floor(Frame / 64) * 3.1415925635);
	return bn;
}

float4 ProcessDiffuse(inout float4 FinalColor, in float2 uv, in float HL, in float HitDistance, in float3 position, in bool IsHit, in float weight, in float FadeFac, in float4 SkyColor)
{
	//if ReSTIR => use the data from the initalization pass instead to avoid biased AO
	//Enable the If statement again for the final product.
	//Ensure the AO is working correctly with sample count <= 1 and MaxFrames > 0
	//if(getReuseSampleCount() > 1)
	HitDistance = distance(position, tex2Dfetch(sRT_FilterTex0, uv * BUFFER_SCREEN_SIZE * getResolutionScale()).yzw)*1.001;
	
	float accumulatedReservoires = getReuseSampleCount() > 1 ? 1 : HL.x > 2 ? HL.x : getReuseSampleCount();
	accumulatedReservoires = sqrt(accumulatedReservoires);
	float LumMax = LUM_MAX;// / accumulatedReservoires;
	
	FinalColor.rgb = 
		IsHit ? 
			FinalColor.rgb : 
			UI_SkyColorIntensity >= 0 ? 
				SkyColor.rgb :
				0;
				
	FinalColor.rgb = max(ClampLuma(FinalColor.rgb, LumMax),0) * UI_Exposure * 2.0f;
	
	float AORadius = min(0.99, UI_AORadius * UI_AORadius * 0.99) * getResolutionScale();
	float3 AO = (HitDistance / FAR_PLANE) >= AORadius;
	
	FinalColor.rgb *= weight;
	FinalColor.rgb  = AdjustSaturation(FinalColor.rgb);
	//apply AO
	FinalColor.rgb = FinalColor.rgb * lerp(1.0, AO, 0.8 * saturate(UI_AOIntensity)) + AO;
	
	FinalColor     = lerp(1, FinalColor, FadeFac);
	return FinalColor;
}

void RayMarchPrep(i i, out float4 FinalColor : SV_Target0)
{
	if(getReuseSampleCount() < 1 || (!UI_Mode && !C_RT_HYBRID_MODE))discard;
	//float2 p = pix * BUFFER_HEIGHT * 0.05;
	//float2 history_uv = (getNoise(i.uv, 64, 1).xy * 2.0 - 1.0) * p * 0.5 + i.uv + sampleMotion(i.uv * getResolutionScale());
	//bool hist_mask = (tex2D(sRT_HLTex0, history_uv).r - 1);
	//if(!hist_mask)discard;
	if(i.uv.x > getResolutionScale() || i.uv.y > getResolutionScale())discard;
	
	i.uv /= getResolutionScale();
	
	float depth = clamp(LDepth(i.uv), 1e-7, 1.0);
	float3 normal = tex2D(sRT_FilterTex1, i.uv).xyz;
	const float FadeFac = getDepthFade(depth);
	
	FinalColor = float4(1,0,0,1);
	
	if(FadeFac >= 0.01 && depth <= SkyDepth)
	{
		float2 HL = tex2D(sRT_HLTex0, i.uv).rb;
		float3 noise = getNoise(i.uv, HL, 1);
				   
		float3 position  = UVtoPos(i.uv);
		float3 eyedir    = normalize(position);
		
		float weight;
		float3 raydir;
		float4 reflection;
		float IsHit;
		float HitDistance;
		
		raydir = getCosineLambert(noise.xyz, normal, weight);
		
		DoRayMarch(UI_Mode, noise.z, position, raydir, reflection.rgb, HitDistance, IsHit);
		FinalColor.rgb  = max(ClampLuma(reflection.rgb, LUM_MAX),0);
		FinalColor.rgb  = FinalColor.rgb * IsHit * weight;
		
		FinalColor.r = toYCC(FinalColor.rgb).r;
		//reproducing hitpos cuz I don't want to write a new raymarch function just for this.
		FinalColor.yzw = raydir * HitDistance + position;
	}
}

void GetDiffuseNoise(in sampler RT, in float3 normals, in float2 uv, in float2 HL, inout float3 ReuseNoise, out float weight, out float3 reuseHitPos, out float maxLum)
{
	float2 p = pix * BUFFER_HEIGHT * 0.1;
	
	float3 RandomNoise = ReuseNoise;
	float3 samplNorm    = normals;
	float3 n = RandomNoise;
	
	float3 initDir = getCosineLambert(n.xyz, samplNorm, weight);
	ReuseNoise = initDir;
	float J = 1;
	
	if(getReuseSampleCount() > 1)
	{
		uv *= getResolutionScale();
		float3 cn;
		float  cd;
		GetNormalAndDepth(uv, cn, cd);
		
		float2 history_uv = sampleMotion(uv / getResolutionScale());
		history_uv += (frac(ReuseNoise.xy + 1.618033988 * floor(Frame/64.0)) * 2.0 - 1.0) * p + uv;
		
		float4 history = tex2Dlod(sRT_TReservoireHistory, float4(history_uv,0,0));
		float4 center  = tex2Dlod(RT, float4(uv,0,0));
			
		bool hist_mask = (tex2D(sRT_HLTex0, history_uv).r - 1);
		hist_mask = hist_mask && (history.x > center.x);
		if(hist_mask) center = history;
			
		float2 BestUV = uv;
		maxLum = center.x;
		reuseHitPos = center.yzw;
		float sampleCount = min(64, getReuseSampleCount());
		float rcpSampleCount = rcp(sampleCount);
		
		if(!hist_mask)
		{
			for(int x; x < sampleCount; x++)
			{
				float2 poisson = poissonDisk[x];
				float rot = (poisson.x + RandomNoise.z + x) * rcpSampleCount;
				rot = rot * 2.0f * PI;
				
				float2 offset;
				sincos(rot, offset.x, offset.y);
				offset *= sqrt(poisson.y * RandomNoise.y);
				
				float2 tap_uv = offset * p + uv;
				
				float3 normal; float depth;
				GetNormalAndDepth(tap_uv, normal, depth);
				
				if(!IsSaturated(tap_uv) && dot(cn, normal) > 0.9 && abs(cd - depth) < 0.05)
				{
					float4 reservoir;
					reservoir = tex2Dlod(RT, float4(tap_uv,0,0));
	
					if(reservoir.x > maxLum)
					{
						reuseHitPos = reservoir.yzw;
						maxLum = reservoir.x;
						BestUV = tap_uv;
					}
				}
			}
		}
		//reproducing necessary data
		float3 samplPos     = UVtoPos(uv / getResolutionScale());
		float3 reusePos     = UVtoPos(BestUV / getResolutionScale());
		float3 reuseHitNorm = DecodeNormals(tex2D(sRT_NormTex, PostoUV(reuseHitPos)).xy);
		
		float3 samplRay = samplPos - reuseHitPos;
		float3 reuseRay = reusePos - reuseHitPos;
		
		//calculating jacobian determinant
		{
			float JM1 = abs(dot(reuseHitNorm, samplRay)) / abs(dot(reuseHitNorm, reuseRay));
			float JM2;
			{
				float JM2n = length(reusePos - reuseHitPos); JM2n *= JM2n;
				float JM2d = length(samplPos - reuseHitPos); JM2d *= JM2d;
				
				JM2 = JM2n / JM2d;
			}
			
			J = JM1 * JM2 + 1e-6;
		}
		
		ReuseNoise = -normalize(samplRay);
		ReuseNoise = dot(-ReuseNoise, samplNorm) <= 0 ? initDir : ReuseNoise;
	}
	
	float cosTheta = dot(-ReuseNoise, samplNorm);
	float pdf      = cosTheta / J;
	weight = 0.5 / (pdf + 1e-6);
	weight = clamp(weight, 1e-6, 1);
}

void RayMarch(i i, out float4 Reservoire : SV_Target0, out float4 FinalColor : SV_Target1
#if C_RT_HYBRID_MODE
, out float4 FinalColor2 : SV_Target2
#endif
)
{
	if(i.uv.x > getResolutionScale() || i.uv.y > getResolutionScale())discard;
	i.uv += getTemporalJitter().xy;
	i.uv *= rcp(getResolutionScale());
	
	
	Reservoire = 0.0;
	
	float depth;
	float3 normal;
	GetNormalAndDepth(i.uv, normal, depth);
	normal = tex2D(sRT_RoughnessTex, i.uv).yzw;
	depth  = clamp(LDepth(i.uv), 1e-7, 1.0);
	
	const float FadeFac = getDepthFade(depth);

	FinalColor = float4(1,0,0,1);
#if C_RT_HYBRID_MODE
	FinalColor2 = float4(1,0,0,1);	
#endif
	if(FadeFac >= 0.01 && depth <= SkyDepth)
	{
		float2 HL = tex2D(sRT_HLTex1, i.uv).rb;
		float3 noise = getNoise(i.uv, HL, 1);
		
		float3 DiffuseNoise = noise, ReservoireHitPos = 0;
		float weight, ReservoireLum;
		if((UI_Mode || C_RT_HYBRID_MODE) && (getReuseSampleCount() > 1 || UI_MaxFrames > 0))
			GetDiffuseNoise(sRT_FilterTex0, normal, i.uv, HL, DiffuseNoise, weight, ReservoireHitPos, ReservoireLum);
		else DiffuseNoise = getCosineLambert(noise.xyz, normal, weight);
		
		float accumulatedReservoires = getReuseSampleCount() > 1 ? 1 : HL.x > 2 ? HL.x : getReuseSampleCount();
		accumulatedReservoires = sqrt(accumulatedReservoires);
		float LumMax = LUM_MAX / accumulatedReservoires;
		float4 SkyColor = getSkyColor();
		
		float3 position  = UVtoPos(i.uv);
		float  Roughness = GetRoughTex(i.uv, float4(normal, depth)).x;
		float3 eyedir    = normalize(position);
		
		float3 raydir;
		float4 reflection;
		float IsHit;
		float HitDistance;
		
#if C_RT_HYBRID_MODE
		//diffuse
		raydir = DiffuseNoise;
		
		DoRayMarch(1, noise.z, position, raydir, reflection.rgb, HitDistance, IsHit);
		FinalColor.rgb = max(ClampLuma(reflection.rgb, LUM_MAX),0);
		ProcessDiffuse(FinalColor, i.uv, HL.x, HitDistance, position, IsHit, weight, FadeFac, SkyColor);
		
		FinalColor.a   = depth;
		FinalColor.rgb = toYCC(FinalColor.rgb);

		//specular
		//noise = getNoise(i.uv, HL.yy, 0);
		raydir = getHemisphereGGXSample(noise.xy, normal, eyedir, Roughness, weight);
		DoRayMarch(0, noise.z, position, raydir, reflection.rgb, HitDistance, IsHit);
		
		FinalColor2.rgb = max(ClampLuma(reflection.rgb, LUM_MAX),0);
		if(!IsHit)FinalColor2.rgb = tex2D(sTexColor, i.uv).rgb;
		
		FinalColor2.rgb = toYCC(FinalColor2.rgb);
		
#else
		if(UI_Mode)raydir = DiffuseNoise;
		else raydir = getHemisphereGGXSample(noise.xy, normal, eyedir, Roughness, weight);
		
		DoRayMarch(UI_Mode, noise.z, position, raydir, reflection.rgb, HitDistance, IsHit);
		//reflection.rgb = tex2D(sRT_HDRBackBufferTex, PostoUV(ReservoireHitPos)).rgb;
		FinalColor.rgb = max(ClampLuma(reflection.rgb, LUM_MAX),0);
		
		if(UI_Mode)
		{
			ProcessDiffuse(FinalColor, i.uv, HL.x, HitDistance, position, IsHit, weight, FadeFac, SkyColor);
			Reservoire.x = FinalColor.x;
			Reservoire.yzw =  raydir * HitDistance + position;
		}
		else if(!IsHit)
			FinalColor.rgb = tex2D(sTexColor, i.uv).rgb;
		
		FinalColor.a   = depth;
		FinalColor.rgb = toYCC(FinalColor.rgb);
#endif
	}//depth check if end
}//ReflectionTex

void TemporalFilter
(
	i i
	,out float4 HistoryLength : SV_Target0
	,out float4 FinalColor    : SV_Target1
#if C_RT_HYBRID_MODE
	,out float4 FinalColor_2   : SV_Target2
#endif
)
{
	float resolutionScale = getResolutionScale();
	if(i.uv.x > resolutionScale || i.uv.y > resolutionScale)discard;
	
	const float2 MotionVectors = sampleMotion(i.uv / resolutionScale) * resolutionScale;
	float pastResScale = tex2Dfetch(sRT_HistoryTex0, 1).x / resolutionScale;
	const float2 PastUV  = i.uv + MotionVectors;
	static const float2 p = pix;
	
	float3 normal;
	float  depth;
	GetNormalAndDepth(i.uv, normal, depth);
	
	//used to aid depth threshold of disocclusion masking
	const float  facing  = dot(normal.xyz, normalize(UVtoPos(i.uv)));
	
	//og color: backbuffer. Used to check the confidence of motion vectors.
	const float2 past_ogcolor = tex2D(sRT_POGColTex, PastUV / resolutionScale).xy;
	const float  curr_ogcolor = dot(1, toYCC(tex2D(sTexColor, i.uv / resolutionScale).rgb).gb);
	const float  local_bright = clamp(tex2Dlod(sRT_HDRBackBufferTex, float4(i.uv,0,3)).a, 0.1, 1);
	const float  past_depth   = past_ogcolor.y;
	
	//current depth is packed in the  alpha channel of ReflectionTex
	float4 Current;
	float4 History;
	if(C_RT_LITE)
	{
		Current = tex2D(sRT_FilterTex1, i.uv);
		History = tex2D(sRT_HistoryTex0, PastUV * pastResScale);
	}
	else
	{
		Current = tex2DClampedCatrom(sRT_FilterTex1, i.uv, BUFFER_SCREEN_SIZE);
		History = tex2DClampedCatrom(sRT_HistoryTex0, PastUV * pastResScale, BUFFER_SCREEN_SIZE);
	}
	
	float3 M1 = Current.rgb, M2 = Current.rgb * Current.rgb;
	float3 Var;
	float4 Clamped_History;
	
#if C_RT_HYBRID_MODE

	float4 Current_2, History_2;
	
	if(C_RT_LITE)
	{
		Current_2 = tex2D(sRT_FilterTex1_2, i.uv);
		History_2 = tex2D(sRT_HistoryTex0_2, PastUV * pastResScale);
	}
	else
	{
		Current_2 = tex2DClampedCatrom(sRT_FilterTex1_2, i.uv, BUFFER_SCREEN_SIZE);
		History_2 = tex2DClampedCatrom(sRT_HistoryTex0_2, PastUV * pastResScale, BUFFER_SCREEN_SIZE);
	}
	
	float3 M1_2 = Current_2.rgb, M2_2 = Current_2.rgb * Current_2.rgb;
	float3 Var_2;
	float4 Clamped_History_2;
#endif

	static const float r = VarianceEstimationSearchRadius;
	static const float area = pow(r * 2 + 1, 2);
	
	float mask;
	
	static const float depth_mul =   Spatial_Filter_DepthT * (dot(normalize(UVtoPos(i.uv)), normal.xyz) + 0.001);
	const float FadeFac = getDepthFade(depth);
	float wSum = 1;
	float4 sCurrent; float weight;
	if(FadeFac > 0.01 && depth < SkyDepth)
	{
		float2 suv = i.uv;
		[loop]for(int xx = -r; xx <= r; xx++){
		[loop]for(int yy = -r; yy <= r; yy++)
		{
			if(xx==0&&yy==0)continue;
			float2 uv = mad(p, int2(xx, yy), suv);
			
			sCurrent = tex2Dlod(sRT_FilterTex1, float4(uv,0,0));
			weight = abs(sCurrent.a - Current.a) < 0.01;
			weight *= !(uv.x < 0 || uv.x > resolutionScale || uv.y < 0 || uv.y > resolutionScale);
			M1 += sCurrent.rgb * weight;
			M2 += sCurrent.rgb * sCurrent.rgb * weight;	
#if C_RT_HYBRID_MODE
			sCurrent = tex2Dlod(sRT_FilterTex1_2, float4(uv,0,0));
			M1_2 += sCurrent.rgb * weight;
			M2_2 += sCurrent.rgb * sCurrent.rgb * weight;	
#endif
			wSum += weight;
		}}

		M1 /= wSum;
		M2 /= wSum;
	
		Var = sqrt(abs(M1 * M1 - M2));
		
#if C_RT_HYBRID_MODE
		M1_2 /= wSum;
		M2_2 /= wSum;
	
		Var_2 = sqrt(abs(M1_2 * M1_2 - M2_2));
		
#endif
		mask =
		    abs(depth - past_depth) * facing   < Temporal_Filter_DepthT
		 && abs(curr_ogcolor - past_ogcolor.x) < Temporal_Filter_MVErrorT * local_bright
		;
		
		bool inbound = PastUV.x < resolutionScale || PastUV.x >= 0 || PastUV.y < resolutionScale || PastUV.y >= 0;
		mask *= inbound;
	}
	else
	{
		mask = 0;
		Clamped_History = float4(1, 0.5, 0.5, 1);
	}
	//HistoryLength.r is first HL tex. HistoryLength.b is the second, only used in Hybrid Mode.
	//HistoryLength.g is first Var.    HistoryLength.g is second Var, only used in Hybrid Mode.
	HistoryLength     = tex2D(sRT_HLTex1, PastUV);
	HistoryLength.rb *= mask;
	HistoryLength.rb  = min(HistoryLength.rb, UI_MaxFrames);
	
	const float MVSpeed = distance(UVtoPos(MotionVectors + i.uv), UVtoPos(i.uv)) * 4;
	
//First effect's accumulation

	if(HistoryLength.r > 4)HistoryLength.g = lerp(HistoryLength.g, Current.x * Current.x, 1 / HistoryLength.r);//GI lum M2
	else HistoryLength.g = M2.x;
	HistoryLength.g = max(1e-6, HistoryLength.g);
	
	float3 ClampLerp = (UI_Mode || C_RT_HYBRID_MODE) ? 
		float3(clamp(MVSpeed, Temporal_Filter_MinClamp, Temporal_Filter_MaxClamp), 1, 1) : 
		1;
	
	float3 PreClamp = History.rgb;
	History.rgb = ClipToAABB(History.rgb, Current.rgb, M1.rgb, Var.rgb * 0.5);
	History.rgb = clamp(History.rgb, M1.rgb - Var.rgb, M1.rgb + Var.rgb);
	History.rgb = lerp(PreClamp.rgb, History.rgb, ClampLerp);
	float ClampAmount = abs(History.r - PreClamp.r);
	
	HistoryLength.r *= saturate(exp(-ClampAmount * Temporal_Filter_LuminanceT ));
	HistoryLength.r++;
	
	if(Temporal_Filter_NoClampOnOutput && (UI_Mode || C_RT_HYBRID_MODE))
			History.rgb    = PreClamp.rgb;
	FinalColor.rgb = lerp(History.rgb, Current.rgb, rcp(HistoryLength.r));
	FinalColor.a   = abs(HistoryLength.g - FinalColor.r * FinalColor.r);

//Second effect's accumulation
#if C_RT_HYBRID_MODE

	if(HistoryLength.b > 4)HistoryLength.a = lerp(HistoryLength.a, Current_2.x * Current_2.x, 1 / HistoryLength.b);//GI lum M2
	else HistoryLength.a = M2_2.x;
	HistoryLength.a = max(1e-6, HistoryLength.a);
	
	PreClamp = History_2.rgb;
	History_2.rgb = ClipToAABB(History_2.rgb, Current_2.rgb, M1_2.rgb, Var_2.rgb * 0.5);
	History_2.rgb = clamp(History_2.rgb, M1_2.rgb - Var_2.rgb, M1_2.rgb + Var_2.rgb);
	History.rgb = lerp(PreClamp.rgb, History_2.rgb, ClampLerp);
	ClampAmount = abs(History_2.r - PreClamp.r);
	
	HistoryLength.b *= saturate(exp(-ClampAmount * Temporal_Filter_LuminanceT ));
	HistoryLength.b++;
	
	FinalColor_2.rgb = lerp(History_2.rgb, Current_2.rgb, rcp(HistoryLength.b));
	FinalColor_2.a   = abs(HistoryLength.a - FinalColor_2.r * FinalColor_2.r);
	
#endif
	float4 TemporalReservoire = tex2D(sRT_TReservoire, i.uv);
}

struct FilterStruct
{
	float depth;
	float3 normals;
	float4 cur;
	float4 sum;
#if C_RT_HYBRID_MODE
	float4 sum2;
#endif
};

struct WeightStruct
{
	float cur;
	float sum;
#if C_RT_HYBRID_MODE
	float sum2;
#endif
	float luma;
	float depth;
	float normals;
};
    
void Filter(in int size, in sampler Tex, in float2 texcoord, out float4 FinalColor
#if C_RT_HYBRID_MODE
, in sampler Tex2, out float4 FinalColor2
#endif
)
{
	float resolutionScale = getResolutionScale();
	if(texcoord.x > resolutionScale || texcoord.y > resolutionScale)discard;
	
	FinalColor = 0; //just avoiding errors and warnings :/
#if C_RT_HYBRID_MODE
	FinalColor2 = 0;
#endif

	FilterStruct CenterData;
	GetNormalAndDepth(texcoord, CenterData.normals, CenterData.depth);
	const float FadeFac = getDepthFade(CenterData.depth);
	if(UI_Sthreshold == 0 || (CenterData.depth >= SkyDepth && FadeFac > 0.01))
	{
		FinalColor = tex2Dlod(Tex, float4(texcoord,0,0));
#if C_RT_HYBRID_MODE
		FinalColor2 = tex2Dlod(Tex2, float4(texcoord,0,0));
#endif
	}
	else
	{
		
		float2 HL = tex2D(sRT_HLTex0, texcoord).rb;
		float  Roughness = GetRoughTex(texcoord, float4(CenterData.normals.xyz, CenterData.depth.x));
		if(!C_RT_HYBRID_MODE && UI_Mode) Roughness = 1;
		Roughness = 1;
		float2 p = pix;
		p *= size;
		
		static const float depth_mul = Spatial_Filter_DepthT * (dot(normalize(UVtoPos(texcoord)), CenterData.normals) + 0.001);
		
		float4 offset = float4(0,0,0,0);
		
		CenterData.cur = tex2Dlod(Tex, float4(texcoord, 0, 0));
		float Variance = GetVariance(texcoord, Tex, HL.r) * (C_RT_HYBRID_MODE ? 1 :Roughness);
		const float lum_threshold = Spatial_Filter_LuminanceT / Variance;
		
		FilterStruct NeighborData;
		WeightStruct w;
		NeighborData.sum = CenterData.cur;
		w.sum = 1;
		float4 Min = 1e+7, Max = -1e+7;
#if C_RT_HYBRID_MODE
		float4 CenterDatacur2 = tex2Dlod(Tex2, float4(texcoord, 0, 0));
		float  Variance_2     = GetVariance(texcoord, Tex2, HL.g) * Roughness;
		const float lum_threshold_2 = Spatial_Filter_LuminanceT / Variance_2;
		
		NeighborData.sum2 = CenterDatacur2;
		w.sum2 = 1;
		float4 Min2 = 1e+7, Max2 = -1e+7;
#endif
		static const int r = Spatial_Filter_Radius;
		w.cur = 1;
		[unroll]for(int x = -r; x <= r; x++){
		[unroll]for(int y = -r; y <= r; y++){
			if(x==0&&y==0)continue;
			offset.xy = mad(float2(x,y), p, texcoord);
			w.cur = !(offset.x < 0 || offset.x > resolutionScale || offset.y < 0 || offset.y > resolutionScale);
			
			GetNormalAndDepth(offset.xy, NeighborData.normals, NeighborData.depth);
			NeighborData.cur = tex2Dlod(Tex, offset);
			
			w.normals = pow(abs(dot(NeighborData.normals, CenterData.normals)), Spatial_Filter_NormalT);
			w.depth   = abs(NeighborData.depth - CenterData.depth) * depth_mul;
			
			Min = min(Min, NeighborData.cur);
			Max = max(Max, NeighborData.cur);

			w.cur *= GetWeights(w.normals, w.depth, CenterData.cur.x, NeighborData.cur.x, lum_threshold);
			
			NeighborData.sum += NeighborData.cur * float4(w.cur.xxx, w.cur * w.cur);
			w.sum += w.cur;
			
#if C_RT_HYBRID_MODE
			NeighborData.cur = tex2Dlod(Tex2, offset);
			
			Min2 = min(Min2, NeighborData.cur);
			Max2 = max(Max2, NeighborData.cur);
			
			w.cur *= GetWeights(w.normals, w.depth, CenterDatacur2.x, NeighborData.cur.x, lum_threshold_2);
			
			NeighborData.sum2 += NeighborData.cur * float4(w.cur.xxx,  w.cur * w.cur);
			w.sum2 += w.cur;
#endif
		}}
		NeighborData.sum /= float4(w.sum.xxx, w.sum * w.sum);
		if(size<=4)NeighborData.sum.rgb = clamp(NeighborData.sum.rgb, Min.rgb, Max.rgb);
		FinalColor = max(NeighborData.sum, -1e+6);
#if C_RT_HYBRID_MODE
		NeighborData.sum2 /= float4(w.sum2.xxx, w.sum2 * w.sum2);
		if(size<=4)NeighborData.sum2.rgb = clamp(NeighborData.sum2.rgb, Min2.rgb, Max2.rgb);
		FinalColor2 = max(NeighborData.sum2, -1e+6);
#endif
	}
}

void dontFilter(in int size, in sampler Tex, in float2 texcoord, out float4 FinalColor
#if C_RT_HYBRID_MODE
, in sampler Tex2, out float4 FinalColor2
#endif
)
{
	FinalColor = tex2D(Tex, texcoord);
#if C_RT_HYBRID_MODE
	FinalColor2 = tex2D(Tex2, texcoord);
#endif
}

#if C_RT_HYBRID_MODE

 #define FilterPassOutput \
  out float4 FinalColor : SV_Target0, \
  out float4 FinalColor2 : SV_Target1
 
 #define FilterFunctionInsOuts0(size) \
  size, sRT_FilterTex0, i.uv, FinalColor, sRT_FilterTex0_2, FinalColor2
 #define FilterFunctionInsOuts1(size) \
  size, sRT_FilterTex1, i.uv, FinalColor, sRT_FilterTex1_2, FinalColor2
 
#else

 #define FilterPassOutput \
  out float4 FinalColor : SV_Target0
 
 #define FilterFunctionInsOuts0(size) \
  size, sRT_FilterTex0, i.uv, FinalColor
 #define FilterFunctionInsOuts1(size) \
  size, sRT_FilterTex1, i.uv, FinalColor
 
#endif

void SpatialFilter0(i i, FilterPassOutput){ if(SF0)Filter(FilterFunctionInsOuts0(1));else dontFilter(FilterFunctionInsOuts0(1)); }		
void SpatialFilter1(i i, FilterPassOutput){ if(SF1)Filter(FilterFunctionInsOuts1(2));else dontFilter(FilterFunctionInsOuts0(2));  }
void SpatialFilter2(i i, FilterPassOutput){ if(SF2)Filter(FilterFunctionInsOuts0(4));else dontFilter(FilterFunctionInsOuts0(4));  }
void SpatialFilter3(i i, FilterPassOutput){ if(SF3)Filter(FilterFunctionInsOuts1(8));else dontFilter(FilterFunctionInsOuts0(8));  }
void SpatialFilter4(i i, FilterPassOutput)
{
	if(SF4)Filter(FilterFunctionInsOuts0(16));else dontFilter(FilterFunctionInsOuts0(16)); 
	float2 resolutionScale = getResolutionScale();
	i.uv *= resolutionScale;
	
	const float FadeFac = getDepthFade(clamp(LDepth(i.uv), 1e-7, 1.0));
	float Fresnel;
	//calculate Fresnel
	if(!UI_Mode || C_RT_HYBRID_MODE)
	{
		//i.uv /= resolutionScale;
		float4 ND;
		GetNormalAndDepth(i.uv / resolutionScale, ND.xyz, ND.w);
		
		float3 Eyedir    = normalize(UVtoPos(i.uv));
		float  Facing    = 1-dot(Eyedir, ND.xyz);
		
		float  F0        = saturate(UI_SpecularIntensity * 0.2);
		
		float3 LightDir = reflect(Eyedir, ND.xyz);
		float3 HalfVec  = normalize(reflect(Eyedir, ND.xyz) + Eyedir);
		float dotLH = saturate(dot(LightDir, HalfVec));
		
		Fresnel = F0 + (1.0 - F0) * pow(1.0 - dotLH, 5.0);
		//Fresnel          = lerp(F0, 2, pow(abs(Facing), 2) * (1 - Roughness));
		Fresnel = saturate(Fresnel);
		if(!C_RT_HYBRID_MODE)FinalColor.a = Fresnel * FadeFac;
		//FinalColor.a = 1;//debug
	}
#if C_RT_HYBRID_MODE
	FinalColor2.a = saturate(Fresnel) * FadeFac;
#endif
}

void HistoryBuffer
(
	i i
	,out float3 Ogcol          : SV_Target0//POGColTex
	,out float4 HLOut          : SV_Target1//HLTex1
	,out float4 ColorHistory   : SV_Target2//HistoryTex
	,out float4 ReservoireHist : SV_Target3//TReservoireHistoryTex
#if C_RT_HYBRID_MODE
	,out float4 ColorHistory_2 : SV_Target4//HistoryTex_2
#endif
)
{
	HLOut = tex2D(sRT_HLTex0, i.uv);
	
	float depth;
	float3 normal;
	GetNormalAndDepth(i.uv * getResolutionScale(), normal, depth);
	
	ReservoireHist = tex2D(sRT_TReservoire, i.uv);
	
	float3 OGC = toYCC(tex2D(sTexColor, i.uv).rgb);
	Ogcol = float3(OGC.y+OGC.z, depth, OGC.x);//chroma sum, depth, luma
	
	if(HLOut.r <= Temporal_Filter_Recurrence_MaxFrames)
		ColorHistory = tex2D(sRT_FilterTex1, i.uv);
	else
		ColorHistory = tex2D(sRT_FilterTex0, i.uv);
#if C_RT_HYBRID_MODE
	if(HLOut.b <= Temporal_Filter_Recurrence_MaxFrames)
		ColorHistory_2 = tex2D(sRT_FilterTex1_2, i.uv);
	else
		ColorHistory_2 = tex2D(sRT_FilterTex0_2, i.uv);
#endif
	float resolutionScale = getResolutionScale();
	if(i.vpos.x < 2 && i.vpos.y < 2)ColorHistory = resolutionScale;  
}

float4 TAA(in float2 uv, in sampler samplerHistory, in sampler samplerCurrent)
{
	
	float  resolutionScale = getResolutionScale();
	float2 p = pix;// / resolutionScale;
	const float2 MotionVectors = sampleMotion(uv);
	const float2 PastUV = uv + MotionVectors;
	const float3 Jitter = getTemporalJitter();
	
	const float  depth = clamp(LDepth(uv), 1e-7, 1.0);
	
	float  FadeFac = getDepthFade(depth);
	float4 current = (depth > SkyDepth  && FadeFac > 0.01) ? float4(1,0,0,0) : 
		tex2DClampedCatrom(
			samplerCurrent, 
			(uv * resolutionScale - Jitter.xy),
			tex2Dsize(samplerCurrent));
		
	current = lerp(float4(1,0,0,0), current, FadeFac);
	
	const float4 history = SampleTextureCatmullRom9t(samplerHistory, uv, tex2Dsize(samplerHistory));
	//const float4 history = tex2D(samplerHistory, uv);
	float4 Max = -1e+7, Min = 1e+7;
	float4 M1 = current, M2 = current * current;

	float4 SCurrent;
	static const int r = 1;
	static const int area = mad(r,2,1) * mad(r,2,1);
	
	uv *= resolutionScale;
	uv -= Jitter.xy;
	
	[loop]for(int x = -r; x <= r; x++){
	[loop]for(int y = -r; y <= r; y++)
	{
		if(x==0&&y==0)continue;
		const float2 nuv = mad(int2(x,y), p, uv);
		
		SCurrent = tex2Dlod(samplerCurrent, float4(nuv,0,0));
		SCurrent = lerp(float4(1,0,0,0), SCurrent, FadeFac);
		
		Max = max(SCurrent, Max);
		Min = min(SCurrent, Min);
		
		M1 += SCurrent;
		M2 += SCurrent * SCurrent;
	}}
	float4 chistory = history;
	
	M1 /= area;
	M2 /= area;
	float4 StandardDeviation = sqrt(abs(M1 * M1 - M2)) * 2;
	
	current = current + (current - M1) * (1-resolutionScale);
	
	if(TS_Clamp)
	{
		chistory = clamp(history, Min, Max);
		chistory.rgb = ClipToAABB(chistory.rgb, current.rgb, M1.rgb, StandardDeviation.rgb);
	}
	
	float4 diff = saturate((abs(chistory - history)));
	diff.r = diff.g + diff.b;
	
	float2 outbound = PastUV.x < 0 || PastUV.x > 1 || PastUV.y < 0 || PastUV.y > 1;
	
	float4 LerpFac = TSIntensity
					* (1 - outbound.r)
					* max(0.5, saturate(1 - diff.rrra * 10.0))
					* max(0.7, 1 - 5 * length(MotionVectors))
					;
					
	if(LerpFac.r < 0.7)current = M1;
	
	float4 CurrW = 1-LerpFac;
	CurrW *= (1-Jitter.z);
	LerpFac = 1-CurrW;
					
	LerpFac = saturate(LerpFac);
	//LerpFac = 0.01;
	
	return lerp(current, chistory, LerpFac);
}

void TemporalStabilizer(i i, FilterPassOutput)
{
#if Enable_TAA
	FinalColor = TAA(i.uv, sRT_HistoryTex1, sRT_FilterTex1);
	
 #if C_RT_HYBRID_MODE
	FinalColor2 = TAA(i.uv, sRT_HistoryTex1_2, sRT_FilterTex1_2);
 #endif
#else
	float  resolutionScale = getResolutionScale();
	FinalColor = tex2D(sRT_FilterTex1, i.uv * resolutionScale);
	
 #if C_RT_HYBRID_MODE
	FinalColor2 = tex2D(sRT_FilterTex1_2, i.uv * resolutionScale);
 #endif
#endif

}

void TemporalStabilizer_CopyBuffer(i i, FilterPassOutput)
{
	FinalColor = tex2D(sRT_FilterTex0, i.uv);
#if C_RT_HYBRID_MODE
	FinalColor2 = tex2D(sRT_FilterTex0_2, i.uv);
#endif
}

void Output(i i, out float3 FinalColor : SV_Target0)
{
	FinalColor = 0;
	float  resolutionScale = getResolutionScale();
	float2 p = pix;
	float3 Background = tex2D(sTexColor, i.uv).rgb;
	float  Depth      = clamp(LDepth(i.uv), 1e-7, 1.0);
	float  debug      = UI_Debug; //UI_Debug
	
	if(debug == 1)Background = 0.5;
	if(debug <= 1)
	{
		//Apply Reflection
		if(!UI_Mode || C_RT_HYBRID_MODE)
		{
#if C_RT_HYBRID_MODE
			float4 Reflection = tex2D(sRT_HistoryTex1_2, i.uv);
#else
			float4 Reflection = tex2D(sRT_HistoryTex1, i.uv);
#endif
			Reflection.rgb = max(0, toRGB(Reflection.rgb));
			Reflection.rgb = Tonemapper(Reflection.rgb);
			Reflection.rgb = RITM(Reflection.rgb);
			
			float Fresnel = Reflection.a;
			float3 Img_Reflection = lerp(RITM(Background), Reflection.rgb, Fresnel);
			Img_Reflection = RTM(Img_Reflection);
			
			FinalColor = Img_Reflection;
		}
		//Apply GI and AO
		if(UI_Mode || C_RT_HYBRID_MODE)
		{
			float4 IL = tex2D(sRT_HistoryTex1, i.uv).rgba;
			
			float  BoostedAO = pow(abs(IL.r), UI_AOIntensity * 2.0);
			float  AOwCurve = 1 - saturate(IL.r / (1 + IL.r) * 2);
			
			IL.r = lerp(IL.r, BoostedAO, saturate(AOwCurve));
			IL.rgb = toRGB(IL.rgb);
			IL.rgb = Tonemapper(IL.rgb);
			IL.rgb = RITM(IL.rgb);
			if(C_RT_HYBRID_MODE)Background = FinalColor;
			float3 HDR_Background = RITM(Background);
			
			FinalColor = RTM(IL.rgb * HDR_Background);
		}
	}
	//debug views: depth, normal, history length, roughness
	else if(debug == 2)
	{
		if(!UI_Mode && !C_RT_HYBRID_MODE) 
			FinalColor = InvTonemapper(tex2D(sTexColor, i.uv).rgb);
		else
		{	
			float3 SDRBackbuffer = tex2D(sTexColor, i.uv).rgb;
			FinalColor  = lum(SDRBackbuffer) > UI_MaskDirect ? 0 : SDRBackbuffer;
			FinalColor *= UI_MaskSky ? Depth < SkyDepth : 1;
			FinalColor  = InvTonemapper(FinalColor);
		}
	}
	else if(debug == 3) FinalColor = sqrt(Depth);
	else if(debug == 4) FinalColor = DecodeNormals(tex2D(sRT_NormTex, i.uv).xy) * 0.5 + 0.5;
	else if(debug == 5) FinalColor = tex2D(sRT_HLTex1, i.uv * resolutionScale).r/UI_MaxFrames;
	else if(debug == 6) FinalColor = GetRoughTex(i.uv, tex2D(sRT_NormTex, i.uv)).x;
	else if(debug == 7) FinalColor = GetVariance(i.uv * resolutionScale, sRT_HistoryTex1, tex2D(sRT_HLTex1, i.uv * resolutionScale).r);
	else if(debug == 8) FinalColor = abs(sampleMotion(i.uv)*400).rgr;
	//Avoids covering menues in black
	if(Depth <= 0.0001) FinalColor = Background;
	FinalColor += BN3dts(i.uv, 8)/255;
}	

///////////////Pixel Shader////////////////
///////////////Techniques//////////////////
///////////////Techniques//////////////////