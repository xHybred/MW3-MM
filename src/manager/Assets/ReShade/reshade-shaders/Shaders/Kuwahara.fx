/*
A slightly altered version of the basic Kuwahara filter

source:
Anisotropic Kuwahara Filtering on the GPU
www.kyprianidis.com
Paper on the related shader : http://www.kyprianidis.com/p/pg2009/jkyprian-pg2009.pdf
Preview App' : https://code.google.com/archive/p/gpuakf/downloads
Code : https://code.google.com/archive/p/gpuakf/source/default/source
Use this code in accordance to the license holder's license !

Ported over to Reshade by Eideren
*/


uniform int Iterations <
	ui_type = "drag";
	ui_min = 0.0;
	ui_max = 16.0;
	ui_label = "Iterations";
> = 4;

uniform float SampleDistance <
	ui_type = "drag";
	ui_min = 0.0;
	ui_max = 10.0;
	ui_label = "SampleDistance";
> = 4.0;

#include "ReShade.fxh"

#define TexChannel0 (ReShade::BackBuffer)
#define TexChannel0_size (ReShade::ScreenSize.xy)

// Force lod for lower Shader Models
#define SAMPLE(s,uv) tex2Dlod(s, float4(uv, 0, 0))

float4 KuwaharaModPass(float4 vpos : SV_Position, float2 tex : TexCoord) : SV_Target
{
	float2 uv = tex.xy;
	float n = float((Iterations + 1) * (Iterations + 1));
	
	float3 m[4];
	float3 s[4];
	for (int k = 0; k < 4; ++k) {
		m[k] = (0.0);
		s[k] = (0.0);
	}

	float scaledSampleDistance = SampleDistance / (Iterations+1);

	for (int j = -Iterations; j <= 0; ++j)  {
		for (int i = -Iterations; i <= 0; ++i)  {
			float3 c = SAMPLE(TexChannel0, uv + float2(i,j) / TexChannel0_size * scaledSampleDistance).rgb;
			m[0] += c;
			s[0] += c * c;
		}
	}
	
	for (int j = -Iterations; j <= 0; ++j)  {
		for (int i = 0; i <= Iterations; ++i)  {
			float3 c = SAMPLE(TexChannel0, uv + float2(i,j) / TexChannel0_size * scaledSampleDistance).rgb;
			m[1] += c;
			s[1] += c * c;
		}
	}
	
	for (int j = 0; j <= Iterations; ++j)  {
		for (int i = 0; i <= Iterations; ++i)  {
			float3 c = SAMPLE(TexChannel0, uv + float2(i,j) / TexChannel0_size * scaledSampleDistance).rgb;
			m[2] += c;
			s[2] += c * c;
		}
	}
	
	for (int j = 0; j <= Iterations; ++j)  {
		for (int i = -Iterations; i <= 0; ++i)  {
			float3 c = SAMPLE(TexChannel0, uv + float2(i,j) / TexChannel0_size * scaledSampleDistance).rgb;
			m[3] += c;
			s[3] += c * c;
		}
	}
	
	float4 fragColor = 0;
	float min_sigma2 = 1e+2;
	float3 median = 0;
	for (int k = 0; k < 4; ++k) {
		m[k] /= n;
		s[k] = abs(s[k] / n - m[k] * m[k]);
		
		float sigma2 = s[k].r + s[k].g + s[k].b;
		median += m[k] / 4;
		if (sigma2 < min_sigma2) {
			min_sigma2 = sigma2;
			fragColor = float4(m[k], 1.0);
		}
	}
    
	return fragColor;
}

technique KuwaharaMod
{
	pass
	{
		VertexShader = PostProcessVS;
		PixelShader = KuwaharaModPass;
	}
}
