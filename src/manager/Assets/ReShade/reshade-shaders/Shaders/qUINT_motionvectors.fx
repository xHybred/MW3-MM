/*=============================================================================
This work is licensed under the 
Creative Commons Attribution-NonCommercial 4.0 International (CC BY-NC 4.0) License
https://creativecommons.org/licenses/by-nc/4.0/	  

Original developer: Jak0bPCoder
Optimization by : Marty McFly
Compatibility by : MJ_Ehsan
=============================================================================*/

/*=============================================================================
	Preprocessor settings
=============================================================================*/
 
#define UI_ME_LAYER_MIN                                 6
#define UI_ME_MAX_ITERATIONS_PER_LEVEL                  2
#define UI_ME_SAMPLES_PER_ITERATION                     5

uniform int UI_ME_LAYER_MAX <
	ui_type = "combo";
	ui_items = "Full Resolution\0Half Resolution\0Quarter Resolution\0";
	ui_min = 0;
	ui_max = 2;	
> = 1;

uniform float UI_ME_PYRAMID_UPSCALE_FILTER_RADIUS <
	ui_type = "drag";
	ui_label = "Filter Smoothness";
	ui_min = 0.0;
	ui_max = 6.0;	
> = 4.0;

uniform bool SHOWME <
	ui_label = "Debug Output";	
> =false;

/*=============================================================================
	Textures, Samplers, Globals, Structs
=============================================================================*/

//do NOT change anything here. "hurr durr I changed this and now it works"
//you ARE breaking things down the line, if the shader does not work without changes
//here, it's by design.

texture ColorInputTex : COLOR;
texture DepthInputTex : DEPTH;
sampler ColorInput 	{ Texture = ColorInputTex; };
sampler DepthInput  { Texture = DepthInputTex; };

//#include "qUINT\Global.fxh"
//#include "qUINT\Depth.fxh"

#include "ReShadeUI.fxh"
#include "ReShade.fxh"

//integer divide, rounding up
#define CEIL_DIV(num, denom) (((num - 1) / denom) + 1)
#define PI 3.14159265
uniform uint FRAME_COUNT < source = "framecount"; >;

#define M_PI 3.1415926535

#ifndef PRE_BLOCK_SIZE_2_TO_7
 #define PRE_BLOCK_SIZE_2_TO_7	3   //[2 - 7]     
#endif

#define BLOCK_SIZE (PRE_BLOCK_SIZE_2_TO_7) //4

//NEVER change these!!!
#define BLOCK_SIZE_HALF (BLOCK_SIZE * 0.5 - 0.5)//(int(BLOCK_SIZE / 2)) //2
#define BLOCK_AREA 		(BLOCK_SIZE * BLOCK_SIZE) //16

//smpG samplers are .r = Grayscale, g = depth
//smgM samplers are .r = motion x, .g = motion y, .b = feature level, .a = loss;

texture FeatureCurr          { Width = BUFFER_WIDTH;   Height = BUFFER_HEIGHT;   Format = RG16F; MipLevels = 8; };
sampler sFeatureCurr         { Texture = FeatureCurr;  };
texture FeaturePrev          { Width = BUFFER_WIDTH;   Height = BUFFER_HEIGHT;   Format = RG16F; MipLevels = 8; };
sampler sFeaturePrev         { Texture = FeaturePrev;   };

texture texMotionVectors          { Width = BUFFER_WIDTH;   Height = BUFFER_HEIGHT;   Format = RG16F; };
sampler sMotionVectorTex         { Texture = texMotionVectors;  };

texture MotionTexCur7               { Width = BUFFER_WIDTH >> 7;   Height = BUFFER_HEIGHT >> 7;   Format = RGBA16F;  };
sampler sMotionTexCur7              { Texture = MotionTexCur7;};
texture MotionTexCur6               { Width = BUFFER_WIDTH >> 6;   Height = BUFFER_HEIGHT >> 6;   Format = RGBA16F;  };
sampler sMotionTexCur6              { Texture = MotionTexCur6;};
texture MotionTexCur5               { Width = BUFFER_WIDTH >> 5;   Height = BUFFER_HEIGHT >> 5;   Format = RGBA16F;  };
sampler sMotionTexCur5              { Texture = MotionTexCur5;};
texture MotionTexCur4               { Width = BUFFER_WIDTH >> 4;   Height = BUFFER_HEIGHT >> 4;   Format = RGBA16F;  };
sampler sMotionTexCur4              { Texture = MotionTexCur4;};
texture MotionTexCur3               { Width = BUFFER_WIDTH >> 3;   Height = BUFFER_HEIGHT >> 3;   Format = RGBA16F;  };
sampler sMotionTexCur3              { Texture = MotionTexCur3;};
texture MotionTexCur2               { Width = BUFFER_WIDTH >> 2;   Height = BUFFER_HEIGHT >> 2;   Format = RGBA16F;  };
sampler sMotionTexCur2              { Texture = MotionTexCur2;};
texture MotionTexCur1               { Width = BUFFER_WIDTH >> 1;   Height = BUFFER_HEIGHT >> 1;   Format = RGBA16F;  };
sampler sMotionTexCur1              { Texture = MotionTexCur1;};
texture MotionTexCur0               { Width = BUFFER_WIDTH >> 0;   Height = BUFFER_HEIGHT >> 0;   Format = RGBA16F;  };
sampler sMotionTexCur0              { Texture = MotionTexCur0;};

struct VSOUT
{
    float4 vpos : SV_Position;
    float2 uv   : TEXCOORD0;
};

struct CSIN 
{
    uint3 groupthreadid     : SV_GroupThreadID;         //XYZ idx of thread inside group
    uint3 groupid           : SV_GroupID;               //XYZ idx of group inside dispatch
    uint3 dispatchthreadid  : SV_DispatchThreadID;      //XYZ idx of thread inside dispatch
    uint threadid           : SV_GroupIndex;            //flattened idx of thread inside group
};

/*=============================================================================
	Functions
=============================================================================*/

float noise(float2 co)
{
  return frac(sin(dot(co.xy ,float2(1.0,73))) * 437580.5453);
}

float3 noise3d(float2 co)
{
	return float3( noise(co), noise(co+0.6432168421), noise(co+0.19216811));
}

float2 pixel_idx_to_uv(uint2 pos, float2 texture_size)
{
    float2 inv_texture_size = rcp(texture_size);
    return pos * inv_texture_size + 0.5 * inv_texture_size;
}

float4 CalcMotionLayer(VSOUT i, int mip_gcurr, float2 searchStart, sampler sFeatureCurr, sampler sFeaturePrev, in float2 texcoord)
{	
	float2 texelsize = rcp(BUFFER_SCREEN_SIZE / exp2(mip_gcurr));
	float2 localBlock[BLOCK_AREA];
	float2 searchBlock[BLOCK_AREA];	 

	float4 moments_local = 0;
	float4 moments_search = 0;
	float2 moment_covariate = 0;

	i.uv -= texelsize * BLOCK_SIZE_HALF; //since we only use to sample the blocks now, offset by half a block so we can do it easier inline

	for(uint k = 0; k < BLOCK_SIZE * BLOCK_SIZE; k++)
	{
		float2 tuv = i.uv + float2(k / BLOCK_SIZE, k % BLOCK_SIZE) * texelsize;
		float2 t_local = tex2Dlod(sFeatureCurr, float4(tuv, 0, mip_gcurr)).xy;
		float2 t_search = tex2Dlod(sFeaturePrev, float4(tuv + searchStart, 0, mip_gcurr)).xy;

		localBlock[k] = t_local; 
		searchBlock[k] = t_search;

		moments_local += float4(t_local, t_local * t_local);			
		moments_search += float4(t_search, t_search * t_search);
		moment_covariate += t_local * t_search;
	}

	moments_local /= BLOCK_AREA;
	moments_search /= BLOCK_AREA;
	moment_covariate /= BLOCK_AREA;

	float2 cossim = moment_covariate * rsqrt(moments_local.zw * moments_search.zw);
	float best_sim = saturate(min(cossim.x, cossim.y));

	float local_features = abs(moments_local.x * moments_local.x - moments_local.z);
	float best_features = abs(moments_search.x * moments_search.x - moments_search.z);

	float2 bestMotion = 0;
	float2 searchCenter = searchStart;    

	float randseed = noise(texcoord);
	randseed = frac(randseed + (FRAME_COUNT % 16) * UI_ME_SAMPLES_PER_ITERATION * UI_ME_MAX_ITERATIONS_PER_LEVEL * 0.6180339887498);
	float2 randdir; sincos(randseed * 6.283, randdir.x, randdir.y); //yo dawg, I heard you like golden ratios
	float2 scale = texelsize;

	[loop]
	for(int j = 0; j < UI_ME_MAX_ITERATIONS_PER_LEVEL && best_sim < 0.999999; j++)
	{
		[loop]
		for (int s = 1; s < UI_ME_SAMPLES_PER_ITERATION && best_sim < 0.999999; s++) 
		{
			randdir = mul(randdir, float2x2(-0.7373688, 0.6754903, -0.6754903, -0.7373688));//rotate by larger golden angle			
			float2 pixelOffset = randdir * scale;
			float2 samplePos = i.uv + searchCenter + pixelOffset;			 

			float4 moments_candidate = 0;			
			moment_covariate = 0;

			[loop]
			for(uint k = 0; k < BLOCK_SIZE * BLOCK_SIZE; k++)
			{
				float2 t = tex2Dlod(sFeaturePrev, float4(samplePos + float2(k / BLOCK_SIZE, k % BLOCK_SIZE) * texelsize, 0, mip_gcurr)).xy;
				moments_candidate += float4(t, t * t);
				moment_covariate += t * localBlock[k];
			}
			moments_candidate /= BLOCK_AREA;
			moment_covariate /= BLOCK_AREA;

			cossim = moment_covariate * rsqrt(moments_local.zw * moments_candidate.zw); 
			float candidate_similarity = saturate(min(cossim.x, cossim.y));

			[flatten]
			if(candidate_similarity > best_sim)					
			{
				best_sim = candidate_similarity;
				bestMotion = pixelOffset;
				best_features = abs(moments_candidate.x * moments_candidate.x - moments_candidate.z);
			}			
		}
		searchCenter += bestMotion;
		bestMotion = 0;
		scale *= 0.5;
	}

	return float4(searchCenter, sqrt(best_features), best_sim * best_sim * best_sim * best_sim);  //delayed sqrt for variance -> stddev, cossim^4 for filter
}

float4 atrous_upscale(VSOUT i, int mip_gcurr, sampler sMotionLow, sampler sFeatureCurr)
{	
    float2 texelsize = rcp(BUFFER_SCREEN_SIZE / exp2(mip_gcurr + 1));	
	float rand = frac(mip_gcurr * 0.2114 + (FRAME_COUNT % 16) * 0.6180339887498) * 3.1415927*0.5;
	float2x2 rotm = float2x2(cos(rand), -sin(rand), sin(rand), cos(rand)) * UI_ME_PYRAMID_UPSCALE_FILTER_RADIUS;
	const float3 gauss = float3(1, 0.85, 0.65);

	float center_z = tex2Dlod(sFeatureCurr, float4(i.uv, 0, mip_gcurr)).y;	

	float4 gbuffer_sum = 0;
	float wsum = 1e-6;

	for(int x = -1; x <= 1; x++)
	for(int y = -1; y <= 1; y++)
	{
		float2 offs = mul(float2(x, y), rotm) * texelsize;
		float2 sample_uv = i.uv + offs;

		float sample_z = tex2Dlod(sFeatureCurr, float4(sample_uv, 0, mip_gcurr + 1)).y;
		float4 sample_gbuf = tex2Dlod(sMotionLow, float4(sample_uv, 0, 0));

		float wz = abs(sample_z - center_z) * 4;
		float ws = saturate(1 - sample_gbuf.w * 4);
		float wf = saturate(1 - sample_gbuf.b * 128.0);
		float wm = dot(sample_gbuf.xy, sample_gbuf.xy) * 4;

		float weight = exp2(-(wz + ws + wm + wf)) * gauss[abs(x)] * gauss[abs(y)];
		gbuffer_sum += sample_gbuf * weight;
		wsum += weight;		
	}

	return gbuffer_sum / wsum;	
}

/*=============================================================================
	Shader Entry Points
=============================================================================*/

/*VSOUT VS_Main(in uint id : SV_VertexID)
{
    VSOUT o;
    VS_FullscreenTriangle(id, o.vpos, o.uv); 
    return o;
}*/

void PSWriteColorAndDepth(in VSOUT i, out float2 o : SV_Target0)
{    
	float depth = ReShade::GetLinearizedDepth(i.uv);
	float luma = dot(tex2D(ColorInput, i.uv).rgb, float3(0.299, 0.587, 0.114));
    o = float2(luma, depth);
}

float4 motion_estimation(in VSOUT i, sampler sMotionLow, sampler sFeatureCurr, sampler sFeaturePrev, int mip_gcurr, float2 texcoord : TexCoord)
{
	float4 upscaledLowerLayer = 0;

	[branch]
	if(UI_ME_LAYER_MIN <= (mip_gcurr-1)) 
		return 0;

	[branch]
    if(mip_gcurr < UI_ME_LAYER_MIN)
    	upscaledLowerLayer = atrous_upscale(i, mip_gcurr, sMotionLow, sFeatureCurr);
	
	[branch]
	if(mip_gcurr >= UI_ME_LAYER_MAX) 
		upscaledLowerLayer = CalcMotionLayer(i, mip_gcurr, upscaledLowerLayer.xy, sFeatureCurr, sFeaturePrev, texcoord);
	  

    return upscaledLowerLayer;
}

void PSMotion6(in VSOUT i, out float4 o : SV_Target0, float2 texcoord : TexCoord){o = motion_estimation(i, sMotionTexCur7, sFeatureCurr, sFeaturePrev, 6, texcoord);}
void PSMotion5(in VSOUT i, out float4 o : SV_Target0, float2 texcoord : TexCoord){o = motion_estimation(i, sMotionTexCur6, sFeatureCurr, sFeaturePrev, 5, texcoord);}
void PSMotion4(in VSOUT i, out float4 o : SV_Target0, float2 texcoord : TexCoord){o = motion_estimation(i, sMotionTexCur5, sFeatureCurr, sFeaturePrev, 4, texcoord);}
void PSMotion3(in VSOUT i, out float4 o : SV_Target0, float2 texcoord : TexCoord){o = motion_estimation(i, sMotionTexCur4, sFeatureCurr, sFeaturePrev, 3, texcoord);}
void PSMotion2(in VSOUT i, out float4 o : SV_Target0, float2 texcoord : TexCoord){o = motion_estimation(i, sMotionTexCur3, sFeatureCurr, sFeaturePrev, 2, texcoord);}
void PSMotion1(in VSOUT i, out float4 o : SV_Target0, float2 texcoord : TexCoord){o = motion_estimation(i, sMotionTexCur2, sFeatureCurr, sFeaturePrev, 1, texcoord);}
void PSMotion0(in VSOUT i, out float4 o : SV_Target0, float2 texcoord : TexCoord){o = motion_estimation(i, sMotionTexCur1, sFeatureCurr, sFeaturePrev, 0, texcoord);}


//Show motion vectors stuff
float3 HUEtoRGB(in float H)
{
	float R = abs(H * 6.f - 3.f) - 1.f;
	float G = 2 - abs(H * 6.f - 2.f);
	float B = 2 - abs(H * 6.f - 4.f);
	return saturate(float3(R,G,B));
}

float3 HSLtoRGB(in float3 HSL)
{
	float3 RGB = HUEtoRGB(HSL.x);
	float C = (1.f - abs(2.f * HSL.z - 1.f)) * HSL.y;
	return (RGB - 0.5f) * C + HSL.z;
}

float4 motionToLgbtq(float2 motion)
{
	float angle = degrees(atan2(motion.y, motion.x));
	float dist = length(motion);
	float3 rgb = HSLtoRGB(float3((angle / 360.f) + 0.5, saturate(dist * 100.0), 0.5));
	return float4(rgb.r, rgb.g, rgb.b, 0);
}

void PSOut(in VSOUT i, out float4 o : SV_Target0)
{
	if(!SHOWME) discard;
	o = motionToLgbtq(tex2D(sMotionTexCur0, i.uv).xy);

	//o = tex2D(sMotionTexCur0, i.uv).w*10-9;
}
void PSWriteVectors(in VSOUT i, out float2 o : SV_Target0)
{
	o = tex2D(sMotionTexCur0, i.uv).xy;
}



/*=============================================================================
	Techniques
=============================================================================*/

technique ReShade_MotionVectors
{
    pass //update curr data RGB + depth
	{
		VertexShader = PostProcessVS;
		PixelShader  = PSWriteColorAndDepth; 
        RenderTarget = FeatureCurr; 

	}
    //mipmaps are being created :)
    pass {VertexShader = PostProcessVS;PixelShader = PSMotion6;RenderTarget = MotionTexCur6;}
    pass {VertexShader = PostProcessVS;PixelShader = PSMotion5;RenderTarget = MotionTexCur5;}
    pass {VertexShader = PostProcessVS;PixelShader = PSMotion4;RenderTarget = MotionTexCur4;}
    pass {VertexShader = PostProcessVS;PixelShader = PSMotion3;RenderTarget = MotionTexCur3;}
    pass {VertexShader = PostProcessVS;PixelShader = PSMotion2;RenderTarget = MotionTexCur2;}
    pass {VertexShader = PostProcessVS;PixelShader = PSMotion1;RenderTarget = MotionTexCur1;}
    pass {VertexShader = PostProcessVS;PixelShader = PSMotion0;RenderTarget = MotionTexCur0;}

    pass  
	{
		VertexShader = PostProcessVS;
		PixelShader  = PSWriteColorAndDepth; 
        RenderTarget0 = FeaturePrev; 
	}
	pass  
	{
		VertexShader = PostProcessVS;
		PixelShader  = PSWriteVectors; 
		RenderTarget = texMotionVectors;
	} 

    pass 
	{
		VertexShader = PostProcessVS;
		PixelShader  = PSOut; 
	}     
}
