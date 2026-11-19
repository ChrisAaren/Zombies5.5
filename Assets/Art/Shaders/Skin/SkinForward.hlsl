#ifndef UNIVERSAL_FORWARD_LIT_PASS_INCLUDED
#define UNIVERSAL_FORWARD_LIT_PASS_INCLUDED

#include "SkinInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#if defined(_PARALLAXMAP)
#define REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
#endif

#if (defined(_NORMALMAP) || (defined(_PARALLAXMAP) && !defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR))) || defined(_DETAIL)
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR
#endif

// keep this file in sync with LitGBufferPass.hlsl

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float2 staticLightmapUV   : TEXCOORD1;
    float2 dynamicLightmapUV  : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv                       : TEXCOORD0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    float3 positionWS               : TEXCOORD1;
#endif

    float3 normalWS                 : TEXCOORD2;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    half4 tangentWS                : TEXCOORD3;    // xyz: tangent, w: sign
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight   : TEXCOORD5; // x: fogFactor, yzw: vertex light
#else
    half  fogFactor                 : TEXCOORD5;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord              : TEXCOORD6;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS                : TEXCOORD7;
#endif

    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef DYNAMICLIGHTMAP_ON
    float2  dynamicLightmapUV : TEXCOORD9; // Dynamic lightmap UVs
#endif

#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD10;
#endif

    float4 positionCS               : SV_POSITION;

    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    inputData.positionWS = input.positionWS;
#endif

#if defined(DEBUG_DISPLAY)
    inputData.positionCS = input.positionCS;
#endif

    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(_NORMALMAP) || defined(_DETAIL)
    float sgn = input.tangentWS.w;      // should be either +1 or -1
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);

    #if defined(_NORMALMAP)
    inputData.tangentToWorld = tangentToWorld;
    #endif
    inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
#else
    inputData.normalWS = input.normalWS;
#endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirWS;

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif
                           
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);  

    #if defined(DEBUG_DISPLAY)
    #if defined(DYNAMICLIGHTMAP_ON)
    inputData.dynamicLightmapUV = input.dynamicLightmapUV;
    #endif
    #if defined(LIGHTMAP_ON)
    inputData.staticLightmapUV = input.staticLightmapUV;
    #else
    inputData.vertexSH = input.vertexSH;
    #endif
    #if defined(USE_APV_PROBE_OCCLUSION)
    inputData.probeOcclusion = input.probeOcclusion;
    #endif
    #endif
}

void InitializeBakedGIData(Varyings input, inout InputData inputData)
{
    #if defined(_SCREEN_SPACE_IRRADIANCE)
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy, inputData.normalWS);
    #elif defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        input.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
    #else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #endif
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Physically Based) shader
Varyings LitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

    // normalWS and tangentWS already normalize.
    // this is required to avoid skewing the direction during interpolation
    // also required for per-vertex lighting and SH evaluation
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);

    half fogFactor = 0;
    #if !defined(_FOG_FRAGMENT)
        fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
    #endif

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

    // already normalized from normal transform to WS.
    output.normalWS = normalInput.normalWS;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR) || defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    real sign = input.tangentOS.w * GetOddNegativeScale();
    half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
#endif
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    output.tangentWS = tangentWS;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
    output.viewDirTS = viewDirTS;
#endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
    output.fogFactor = fogFactor;
#endif

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    output.positionWS = vertexInput.positionWS;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif

    output.positionCS = vertexInput.positionCS;

    return output;
}

// Used in Standard (Physically Based) shader
void LitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(_PARALLAXMAP)
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
#else
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
#endif
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    half4 zones = SAMPLE_TEXTURE2D(_SkinZones, sampler_SkinZones, input.uv);
    half4 zones2 = SAMPLE_TEXTURE2D(_SkinZones2, sampler_SkinZones2, input.uv);
    half3 pore = UnpackNormal(SAMPLE_TEXTURE2D_BIAS(_PoreMap, sampler_PoreMap, input.uv * _PoreTiling, -1.0h));
    half3 pore2 = UnpackNormal(SAMPLE_TEXTURE2D_BIAS(_PoreMap, sampler_PoreMap, input.uv * (_PoreTiling * 2.9h) + 0.37h, -1.0h));
    surfaceData.normalTS = normalize(half3(pore.xy * _PoreStrength + pore2.xy * _MicroStrength, 1.0h));
    half lum = dot(surfaceData.albedo, half3(0.3h, 0.55h, 0.15h));
    surfaceData.albedo = lerp(surfaceData.albedo, lum.xxx, _Desaturate);
    // pores read as tiny darker pits in the albedo too, so skin never looks like smooth plastic at mid range
    surfaceData.albedo *= 1.0h + (pore.x * 0.5h + pore2.y * 0.5h) * 0.12h * (_MicroStrength > 0.001h ? 1.0h : 0.0h);
    // stubble and beard shadow along the jaw, upper lip and chin; hairline and brow-ridge shading
    half grain = saturate(0.55h + pore2.x * 1.4h + pore.y * 0.6h);
    half stub = zones2.r * _StubbleStrength * (0.55h + 0.6h * grain);
    surfaceData.albedo = lerp(surfaceData.albedo, surfaceData.albedo * _StubbleColor.rgb, saturate(stub));
    // one or two days of growth: individual dark hair dots over the beard zone
    {
        float2 sp = input.uv * 700.0;
        float2 sc = floor(sp);
        half h1 = frac(sin(dot(sc, float2(41.3, 289.1))) * 43758.5453);
        half h2 = frac(sin(dot(sc, float2(127.1, 311.7))) * 24634.6345);
        half h3 = frac(sin(dot(sc, float2(269.5, 183.3))) * 12345.6789);
        float2 cell = frac(sp) - 0.5 - (float2(h2, h3) - 0.5) * 0.5;
        half dot_ = (h1 > 0.45h ? 1.0h : 0.0h) * (1.0h - smoothstep(0.08h, 0.30h, length(cell))) * (0.5h + 0.5h * h2);
        surfaceData.albedo *= 1.0h - saturate(zones2.r * 1.4h) * _StubbleDots * dot_;
    }
    surfaceData.albedo *= 1.0h - saturate(zones2.g + zones2.b * 0.7h) * _HairlineStrength * 0.38h;
    // lips (and the inner lip): less pink, less glossy. Redness picks the lip pixels; the zone alpha is the mouth area.
    half redness = smoothstep(0.58h, 0.8h, (surfaceData.albedo.r - surfaceData.albedo.g) / max(surfaceData.albedo.r, 0.02h));
    half lipMask = saturate(max(zones.a * redness, redness));
    surfaceData.albedo = lerp(surfaceData.albedo, lum.xxx * half3(1.7h, 1.0h, 0.8h), lipMask * 0.85h * _LipDesat);
    // mottled, bruised variation (low-frequency value noise)
    {
        float2 mp = input.uv * 95.0;
        float2 ip = floor(mp), fp = frac(mp);
        fp = fp * fp * (3.0 - 2.0 * fp);
        float n00 = frac(sin(dot(ip, float2(12.9898, 78.233))) * 43758.5453);
        float n10 = frac(sin(dot(ip + float2(1, 0), float2(12.9898, 78.233))) * 43758.5453);
        float n01 = frac(sin(dot(ip + float2(0, 1), float2(12.9898, 78.233))) * 43758.5453);
        float n11 = frac(sin(dot(ip + float2(1, 1), float2(12.9898, 78.233))) * 43758.5453);
        half vn = lerp(lerp(n00, n10, fp.x), lerp(n01, n11, fp.x), fp.y);
        half mott = smoothstep(0.5h, 0.82h, vn) * _MottleAmount;
        surfaceData.albedo = lerp(surfaceData.albedo, surfaceData.albedo * _MottleColor.rgb, mott);
    }
    surfaceData.albedo *= lerp(1.0h, zones.g, _CavityStrength);
    surfaceData.albedo = lerp(surfaceData.albedo, surfaceData.albedo * _SocketColor.rgb, zones.b * _SocketAmount);
    surfaceData.smoothness = saturate(surfaceData.smoothness * (1.0h - _ShineBoost * 0.5h + _ShineBoost * 1.3h * zones.r));
    surfaceData.smoothness *= lerp(1.0h, _LipSmooth, saturate(zones.a + redness));
    surfaceData.occlusion *= lerp(1.0h, zones.g, _CavityStrength * 0.5h);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    {
        Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
        half ndl = dot(inputData.normalWS, mainLight.direction);
        half wrapped = saturate((ndl + _Wrap) / (1.0h + _Wrap));
        half extra = max(wrapped - saturate(ndl), 0.0h);
        half terminator = extra * (1.0h - extra * 0.6h);
        half3 scatter = lerp(half3(1, 1, 1), _SSSColor.rgb, 0.85h);
        color.rgb += surfaceData.albedo * mainLight.color * mainLight.shadowAttenuation * mainLight.distanceAttenuation * terminator * scatter * _SSSStrength * 1.6h;
    }
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent());

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
