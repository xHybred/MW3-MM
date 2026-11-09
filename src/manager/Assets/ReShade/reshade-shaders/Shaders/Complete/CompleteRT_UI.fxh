//Stochastic Screen Space Ray Tracing
//Written by MJ_Ehsan for Reshade
//Version 1.3 - User Interface

#if C_RT_UI_DIFFICULTY == 1

uniform int Hints <
	ui_text = "1- Use with a motion estimation shader such as:\n"
	
	          "    A: RECOMMENDED - iMMERSE Launchpad (by Marty McFly)\n"
	          "       To use it, you should set C_RT_USE_LAUNCHPAD to 1 in preprocessor settings.\n\n"
	          
	          "    B: DRME (by JacobPCoder)\n\n"
	          
	          "    C: qUINT_MotionVectors (an optimized version of DRME by Marty McFly)\n\n"
	          
	          "    D: ReShade_MotionVectors (same as C but works without dependencies of quint library)\n\n"
	          
	          "    E: DH_UBER_MOTION (by AlucardDH)\n\n"
	          
	          "2- Read the PreProcessor Settings tooltip before changing them.\n"
	          "3- Enjoy! :)";
			  
	ui_category = "Hints - Please Read for good results.";
	ui_category_closed = false;
	ui_label = " ";
	ui_type = "radio";
>;

#if !C_RT_HYBRID_MODE
uniform int UI_Mode <
	ui_type = "combo";
	ui_label = "Mode";
	ui_tooltip = "You want Reflection or GI? Choose!\n"
	             "Both?! Set C_RT_HYBRID_MODE to one!";
	ui_items = "Reflection\0GI\0";
> = 1;
#else
uniform int UI_Mode <
	ui_type = "radio";
	ui_label = "Mode";
	ui_text = "///////////////////////////\n"
	          "//Hybrid Mode is enabled!//\n"
	          "///////////////////////////";
	ui_tooltip = "Disable C_RT_HYBRID_MODE to have only one effect.";
> = 1;
#endif

uniform float fov <
	ui_label = "Field of View";
	ui_type = "slider";
	ui_category = "Ray Tracing";
	ui_tooltip = "Set it according to the game's field of view.";
	hidden = true;
	ui_min = 50;
	ui_max = 120;
> = 70;

//Normal Filtering
uniform int NormalsHint <
	ui_label = " ";
	ui_category = "Normal Filtering";
	ui_category_closed = true;
	ui_text = "In ReShade, normal vectors are generated using "
	           "the depth buffer, hence why they look low-poly."
	           "So they need some post processing to reduce the "
               "blockyness and low-poly appearance, and to also "
               "add back the lost tiny details from normal maps.";
	ui_type = "radio";
>;

uniform float UI_BumpStrength <
	ui_label = "Bump mapping strengh";
	ui_type = "slider";
	ui_category = "Normal Filtering";
	ui_tooltip = "Adds tiny details to the lighting.";
	ui_min = 0.0;
	ui_max = 1.0;
> = 0.4;

uniform int UI_SmoothNormals <
	ui_label = "Smooth normals quality";
	ui_type  = "slider";
	ui_category = "Normal Filtering";
	ui_tooltip = "Lower values are for older games with larger polygons.";
	ui_min = 0;
	ui_max = 10;
> = 3;

//Ray Tracing (Advanced)
uniform int UI_ResolutionScale <
	ui_type = "combo";
	ui_min = 0.5;
	ui_category = "Ray Tracing";
	ui_label = "Resolution Scale";
	ui_items = "100%\0""50%\0";
> = 1;

uniform float UI_RayLength <
	ui_label = "Ray Length";
	ui_type = "slider";
	ui_category = "Ray Tracing";
	ui_tooltip = "Increases ray length at the cost of accuracy.";
	ui_category_closed = true;
	ui_min = 0;
	ui_max = 1;
> = 1.0;

uniform uint UI_RaySteps <
	ui_label = "Max Steps"; 
	ui_type = "slider";
	ui_category = "Ray Tracing";
	ui_tooltip = "Increases precision at the cost of performance.";
	ui_category_closed = true;
	ui_min = 1;
	ui_max = 64;
> = 64;

uniform int UI_ReuseSamples <
	ui_label    = "Sample Count";
	ui_type     = "slider";
	ui_category = "Ray Tracing";
	ui_tooltip  = "Decreases noise and increases accuracy at the cost of performance\n"
	              "The performance difference from 1 to 2 is high. But not as noticeable afterwards.";
	ui_category_closed = true;
	ui_min      = 1;
	ui_max      = 64;
> = 6;

uniform float UI_RayDepth <
	ui_label = "Surface thickness";
	ui_type = "slider";
	ui_category = "Ray Tracing";
	ui_tooltip = "How thick are surfaces in the scene in average?";
	ui_category_closed = true;
	ui_min = 0.05;
	ui_max = 10;
> = 2;

//Denoiser (Advanced)
uniform float UI_MVErrorT <
	ui_label = "Motion Vector\nError Tolerance";
	ui_type = "slider";
	ui_category = "Denoiser (Advanced)";
	ui_tooltip = "Lower values result in less ghosting\n"
	             "Higher va result in more stability\n"
	             "If the game has unstable image - such as\n"
	             "intensive film grain - increase the value a bit.";
	ui_category_closed = true;
	//hidden = true;
	ui_min = 0;
	ui_max = 0.1;
> = 0.03;

//Denoiser (Advanced)
uniform int UI_MaxFrames <
	ui_label = "History Length";
	ui_type = "slider";
	ui_category = "Denoiser (Advanced)";
	ui_tooltip = "Higher values increase smoothness\n"
				 "while preserves more details; But\n"
				 "also introduces a bit more temporal lag.";
	ui_category_closed = true;
	//hidden = true;
	ui_min = 1;
	ui_max = 64;
> = 64;

uniform float UI_Sthreshold <
	ui_label = "Spatial Denoiser\nThreshold";
	ui_type = "slider";
	ui_category = "Denoiser (Advanced)";
	ui_tooltip = "MODIFICATION IS NOT RECOMMENDED!!\nbtw, lower = less noise, higher = more details.";
	ui_category_closed = true;
	//hidden = true;
	ui_min = 0.001;
	ui_max = 4;
> = 1;

//Reflection
uniform float UI_Roughness <
	ui_label = "Roughness";
	ui_type = "slider";
	ui_category = "Reflection";
	ui_category_closed = true;
	ui_min = 0.0;
	ui_max = 0.999;
> = 0.4;

uniform float UI_SpecularIntensity <
	ui_label = "Fadeout";
	ui_type = "slider";
	ui_category = "Reflection";
> = 0.5;

//Ambient Occlusion
uniform float UI_AORadius <
	ui_label = "Radius";
	ui_type = "slider";
	ui_category = "Ambient Occlusion";
	ui_category_closed = true;
	ui_tooltip = "Maximum distance to find occlusions.";
> = 0.25;

uniform float UI_AOIntensity <
	ui_label = "Intensity";
	ui_type = "slider";
	ui_category = "Ambient Occlusion";
> = 0.5;

//Diffuse Illumination
uniform int UI_RTColorSubCategory <
	ui_label = " ";
	ui_text = "=== RT COLOR ============";
	ui_category = "Diffuse Illumination";
	ui_type = "radio";
> = 0;

uniform float UI_Exposure <
	ui_type = "slider";
	ui_label = "Exposure";
	ui_category = "Diffuse Illumination";
	ui_category_closed = true;
	ui_min = 0;
	ui_max = 4;
> = 1;

uniform float UI_Saturation <
	ui_type = "slider";
	ui_label = "Saturation";
	ui_category = "Diffuse Illumination";
	ui_min = 0;
	ui_max = 2;
> = 1;

uniform int UI_GIMaskSubCategory <
	ui_label = " ";
	ui_text = "=== LIGHT MAP MASKING ===";
	ui_category = "Diffuse Illumination";
	ui_type = "radio";
> = 0;

uniform float UI_MaskDirect <
	ui_type     = "slider";
	ui_label    = "Exclude Direct Lights";
	ui_category = "Diffuse Illumination";
	ui_tooltip  = "Direct lights (sun, lamps, etc.) will not affect the Diffuse Illumination.\n"
	              "Lower values detects direct lights more sensitively.";
	ui_min      = 0.5;
> = 1;

uniform bool UI_MaskSky <
	ui_label    = "Exclude Sky from RT";
	ui_category = "Diffuse Illumination";
	ui_tooltip  = "Sky will not affect the ray traced lighting if enabled.";
> = 1;

uniform int UI_SkySubCategory <
	ui_label = " ";
	ui_text = "=== SKY COLOR ===========";
	ui_category = "Diffuse Illumination";
	ui_type = "radio";
> = 0;

uniform float UI_SkyColorIntensity <
	ui_type = "slider";
	ui_label = "Intensity";
	ui_category = "Diffuse Illumination";
	ui_min = -4;
	ui_max = 4;
> = 0;

uniform float3 UI_SkyColorTint <
	ui_type = "color";
	ui_label = "Tint";
	ui_category = "Diffuse Illumination";
> = float3(1.0, 1.0, 1.0);

uniform bool UI_SkyColorMode <
	ui_type = "radio";
	ui_label = "Auto Detect Color";
	ui_category = "Diffuse Illumination";
> = 1;

//Masking
uniform float UI_DepthFade <
	ui_label = "Depth Fade";
	ui_type = "slider";
	ui_category = "Masking";
	ui_category_closed = true;
	ui_tooltip = "Higher values decrease the intesity on distant objects.\n"
				 "Reduces blending issues within in-game fogs.";
	ui_min = 0;
	ui_max = 1;
> = 0.75;

uniform int UI_FadeMode <
	ui_label = "Fade Mode";
	ui_type = "combo";
	ui_category = "Masking";
	ui_tooltip = "Exponential: Good for modern games with pyshical and volumetric fog\n"
	             "Linear: Good for older games with simple and fake fog";
	ui_items = "Exponential\0Linear\0";
#if __RENDERER__ >= 0xb000
> = 0;
#else
> = 1;
#endif

uniform uint UI_Debug <
	ui_type = "combo";
	ui_label = "Debug mode";
	ui_items = "None\0"                              //0
	           "Lighting\0Lightmap\0"                //1-2
	           "Depth\0Normal\0Accumulation\0"       //3-4-5
	           "Roughness\0Variance\0Motion\0";      //6-7-8   for DEV_MODE only
	ui_category = "Extra";
#ifndef DevMode
	ui_max = 5;
#endif
	ui_category_closed = true;
> = 0;

uniform int Preprocessordefinitionstooltip<
	ui_text = "C_RT_RESOLUTION_SCALE:\nLower values are much faster but a bit blurrier and noisier.\n\n"
			  
			  "C_RT_UI_DIFFICULTY:\n0 is EZ Setup. While 1 gives access to more settings.\n\n"

			  "C_RT_HYBRID_MODE:\n0 means you can use only one effect at a time. Either GI or Reflection. 1 means you have both effects simultaniously.\n\n"
			  
	          "C_RT_USE_LAUNCHPAD_MOTIONS:\nIf you want to use CompleteRT with iMMERESE Launchpad's motion vectors, set this to one.";
	          
	ui_category = "PreProcessor Settings tooltip";
	ui_category_closed = true;
	ui_label = " ";
	ui_type = "radio";
>;

#else //EZ Mode

uniform int Hints<
	ui_text = "1- Use with a motion estimation shader such as:\n"
	
	          "    A: RECOMMENDED - iMMERSE Launchpad (by Marty McFly)\n"
	          "       To use it, you should set C_RT_USE_LAUNCHPAD to 1 in preprocessor settings.\n\n"
	          
	          "    B: DRME (by JacobPCoder)\n\n"
	          
	          "    C: qUINT_MotionVectors (an optimized version of DRME by Marty McFly)\n\n"
	          
	          "    D: ReShade_MotionVectors (same as C but works without dependencies of quint library)\n\n"
	          
	          "    E: DH_UBER_MOTION (by AlucardDH)\n\n"
	          
	          "2- Read the PreProcessor Settings tooltip before changing them.\n"
	          "3- Enjoy! :)";
	ui_category = "Hints - Please Read!";
	ui_label = " ";
	ui_type = "radio";
>;

#if !C_RT_HYBRID_MODE
uniform int UI_Mode <
	ui_type = "combo";
	ui_label = "Mode";
	ui_tooltip = "You want Reflection or GI? Choose!\n"
	             "Both?! Set C_RT_HYBRID_MODE to one!";
	ui_items = "Reflection\0GI\0";
> = 1;
#else
uniform int UI_Mode <
	ui_type = "radio";
	ui_label = "Mode";
	ui_text = "///////////////////////////\n"
	          "//Hybrid Mode is enabled!//\n"
	          "///////////////////////////";
	ui_tooltip = "Disable C_RT_HYBRID_MODE to have only one effect.";
> = 1;
#endif

uniform int UI_QualityPreset <
	ui_type = "combo";
	ui_label = "Quality Preset";
	ui_items = 
"Very Low\0"
"Low\0"
"Medium\0"
"High\0"
"Very High\0";
> = 1;

uniform float UI_BumpStrength <
	ui_label = "Bump mapping strengh";
	ui_type = "slider";
	ui_category = "Ray Tracing";
	ui_tooltip = "Adds tiny details to the lighting.";
	ui_min = 0.0;
	ui_max = 1.0;
> = 0.4;

uniform float UI_Roughness <
	ui_label = "Roughness";
	ui_type = "slider";
	ui_category = "Ray Tracing";
	ui_tooltip = "Blurriness of reflections.";
	ui_min = 0.0;
	ui_max = 0.999;
> = 0.4;

uniform float UI_SpecularIntensity <
	ui_label = "Specularity";
	ui_type = "slider";
	ui_category = "Blending Options";
	ui_category_closed = true;
	ui_tooltip = "Intensity of reflections.";
	ui_min = 1;
	ui_max = 2;
> = 4;

uniform float UI_AORadius <
	ui_label = "Ambient Occlusion Radius";
	ui_type = "slider";
	ui_category = "Blending Options";
	ui_tooltip = "Maximum distance to find occlusions.";
> = 0.25;

uniform float UI_AOIntensity <
	ui_label = "Ambient Occlusion Intensity";
	ui_type = "slider";
	ui_category = "Blending Options";
> = 0.5;

uniform float UI_DepthFade <
	ui_label = "Depth Fade";
	ui_type = "slider";
	ui_category = "Blending Options";
	ui_tooltip = "Higher values decrease the intesity on distant objects.\n"
				 "Reduces blending issues with in-game fogs.";
	ui_min = 0;
	ui_max = 1;
> = 0.75;

uniform float UI_Exposure <
	ui_type = "slider";
	ui_label = "GI Exposure";
	ui_category = "Diffuse Illumination";
	ui_min = 0;
	ui_max = 4;
> = 1;

uniform float UI_Saturation <
	ui_type = "slider";
	ui_label = "GI Saturation";
	ui_category = "Diffuse Illumination";
	hidden = true;
	ui_min = 0;
	ui_max = 2;
> = 1;

uniform float UI_MaskDirect <
	ui_type     = "slider";
	ui_label    = "Exclude Direct Lights";
	ui_category = "Diffuse Illumination";
	ui_tooltip  = "Direct lights (sun, lamps, etc.) will not affect the Diffuse Illumination.\n"
	              "Lower values detects direct lights more sensitively.";
	ui_min      = 0.5;
> = 1;

uniform bool UI_MaskSky <
	ui_label    = "Exclude Sky from RT";
	ui_category = "Diffuse Illumination";
	ui_tooltip  = "Sky will not affect the ray traced lighting if enabled.";
> = 1;

uniform uint UI_Debug <
	ui_type = "combo";
	ui_label = "Debug mode";
	ui_items = "None\0"                                      // 0
	           "Lighting\0Lightmap\0"                        //1 -2
	           "Depth\0Normal\0"                             //3-4-5
	           "Accumulation\0Roughness\0Variance\0Motion\0";//6-7-8   for DEV_MODE only
	ui_category = "Debug";
	ui_category_closed = true;
#ifndef DevMode
	ui_max = 4;
#endif
> = 0;

uniform int Preprocessordefinitionstooltip<
	ui_text = "C_RT_RESOLUTION_SCALE:\nLower values are much faster but a bit blurrier and noisier.\n\n"
			  
			  "C_RT_UI_DIFFICULTY:\n0 is EZ Setup. While 1 gives access to more settings.\n\n"

			  "C_RT_HYBRID_MODE:\n0 means you can use only one effect at a time. Either GI or Reflection. 1 means you have both effects simultaniously.\n\n"
			  
	          "C_RT_USE_LAUNCHPAD_MOTIONS:\nIf you want to use CompleteRT with iMMERESE Launchpad's motion vectors, set this to one.";
	          
	ui_category = "PreProcessor Settings tooltip";
	ui_category_closed = true;
	ui_label = " ";
	ui_type = "radio";
>;

#endif
