//Stochastic Screen Space Ray Tracing
//Written by MJ_Ehsan for Reshade
//Version 1.3 - Configs

#define AspectRatio BUFFER_WIDTH/BUFFER_HEIGHT

//Depth Buffer res for ray marching
#define RES_M 1

//=============================================================//
//==================UI PreCompile Settings=====================//
//=============================================================//

#ifndef C_RT_UI_DIFFICULTY
 #define C_RT_UI_DIFFICULTY 0
#endif

#ifndef C_RT_HYBRID_MODE
 #define C_RT_HYBRID_MODE 0
#endif

#ifndef C_RT_USE_LAUNCHPAD_MOTIONS
 #define C_RT_USE_LAUNCHPAD_MOTIONS 0
#endif

#ifndef C_RT_LITE
 #define C_RT_LITE 0
#endif

//=============================================================//
//======================Other Settings=========================//
//=============================================================//

#define HighPrecisionMode 0

#if !HighPrecisionMode
 #define TexP RGBA16f
#else
 #define TexP RGBA32f
#endif 

#define DevMode 1

#if __RENDERER__ < 0xa000
 #undef C_RT_HYBRID_MODE
 #define C_RT_HYBRID_MODE 0
#endif

#define NO_ROUGH_TEX          0
#define SMOOTHNORMALS_ENABLED 1

//Finding the max allowed mip level
#if BUFFER_WIDTH < BUFFER_HEIGHT
 #define MIN_DIM BUFFER_WIDTH
#else
 #define MIN_DIM BUFFER_HEIGHT
#endif

//=============================================================//
//====================Smooth Normals===========================//
//=============================================================//

static const float SN_DThreshold <ui_type="drag";> = 0.01;//max d diff
static const float SN_NThreshold <ui_type="drag";> = 0.7; //max n diff
static const float SN_Radius  <ui_type="drag";> = 32;     //radius
static const int   SN_DirCount = 16;                         //dirs

//=============================================================//
//====================Color Conversion=========================//
//=============================================================//

//Tonemapping mode : 1 = Timothy Lottes || 0 = Reinhardt
#define TM_Mode 1
#define IT_Intensity 0.96
//clamps the maximum luma of pixels to avoid unsolvable fireflies
#define LUM_MAX 25.0

//=============================================================//
//=====================Simple UI Preset========================//
//=============================================================//

#define STEPNOISE 0.67
#define SkyDepth 0.99

#if !C_RT_UI_DIFFICULTY

 //simple UI mode preset
 #define fov 50
 #define UI_RayDepth 3
 #define UI_MaxFrames 32 
 #define UI_Sthreshold 1
 #define UI_ReuseSamples 9
 #define UI_MVErrorT 0.03
 #define UI_SmoothNormals 3.0
 #define UI_AutoExposure 1
 #define UI_SkyColorIntensity 0
 #define UI_SkyColorMode 0
 #define UI_SkyColorTint 0

 //assuming newer games use the more accurate one
 #if __RENDERER__ >= 0xb000
  #define UI_FadeMode 0
 #else
  #define UI_FadeMode 1
 #endif

#endif

//=============================================================//
//===================Temporal Stabilizer=======================//
//=============================================================//

//0: disable || 1: enable
#define Enable_TAA 1
//Temporal stabilizer Intensity
#define TSIntensity 0.9
#define TS_Clamp 1
#define TS_JitterUpscale 1

//=============================================================//
//========================Filtering============================//
//=============================================================//

#define ReuseProbability 0.667
#define TemporalReservoireDecay 0.9

#define Spatial_Filter_Radius 1 //1 or 2, don't go bigger. Massively affects performance
#define VarianceEstimationSearchRadius 3
//to toggle each spatial pass
#define SF0 1
#define SF1 1
#define SF2 1
#define SF3 1
#define SF4 1

#if C_RT_LITE //saves up to 8736bpp data reads, along with a lot of ALU reads
//disables use of catrom sampling in temporal accumulation (saves 1024bpp data reads)
//decreases spatial variance search radius from 3 to 1 (saves up to 5120bpp data reads)
//disables the last pass of spatial filter (saves up to 1440bpp data reads)
//diables Temporal Stabilizer (saves up to 1152bpp data reads)
 #undef VarianceEstimationSearchRadius
 #undef SF4
 #undef Enable_TAA
 #undef TS_JitterUpscale
 
 #define VarianceEstimationSearchRadius 1
 #define SF4 0
 #define Enable_TAA 0
 #define TS_JitterUpscale 0
#endif


#define Spatial_Filter_NormalT           100.0//100
#define Spatial_Filter_DepthT            100.0//100
#define Spatial_Filter_LuminanceT        (1.0 * UI_Sthreshold)

#define Temporal_Filter_MVErrorT   (sqrt(UI_MVErrorT)) //0.015 //lower = more sensitive
#define Temporal_Filter_DepthT     0.010 //0.010 //lower = more sensitive
#define Temporal_Filter_LuminanceT 0.25 //4.00 //higher = more sentsitive
#define Temporal_Filter_MinClamp   0.000 //0.5? //higher = more history clamping in stationary scene
#define Temporal_Filter_MaxClamp   1.000
#define Temporal_Filter_NoClampOnOutput 0 //disables clamping of the output lighting. no energy loss but more flicker and possibly more ghosting.
#define Temporal_Filter_Recurrence_MaxFrames 20 //20