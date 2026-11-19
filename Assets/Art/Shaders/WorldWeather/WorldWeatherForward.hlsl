#ifndef UNIVERSAL_FORWARD_LIT_PASS_INCLUDED
#define UNIVERSAL_FORWARD_LIT_PASS_INCLUDED

#include "WorldWeatherInput.hlsl"
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

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// World-space facade and paving weathering: no second UV set, no decals; one mask fetch per surface type.
void ApplyWeathering(float3 posWS, float3 normalWS, inout SurfaceData s)
{
    float3 n = normalize(normalWS);
    float up = saturate((n.y - 0.55h) * 4.0h);
    float side = 1.0h - saturate((abs(n.y) - 0.35h) * 4.0h);
    float3 albedo = s.albedo;
    float smooth = s.smoothness;
    if (side > 0.001h)
    {
        float2 wuv = abs(n.x) > abs(n.z) ? float2(posWS.z, posWS.y) : float2(posWS.x, posWS.y);
        half4 g = SAMPLE_TEXTURE2D(_GrimeMask, sampler_GrimeMask, wuv * _GrimeScale);
        half4 g2 = SAMPLE_TEXTURE2D(_GrimeMask, sampler_GrimeMask, wuv * (_GrimeScale * 0.31h) + float2(0.37, 0.61));
        half ground = 1.0h - smoothstep(0.1h, _GroundGrime, posWS.y - 0.15h);
        half streak = g.r * (0.55h + 0.45h * g2.g);
        half blot = g2.g * g.g;
        half soil = saturate(streak * 0.9h + blot * 0.5h + ground * (0.45h + 0.4h * g.g));
        soil = saturate(soil * _GrimeAmount * 1.5h) * side;
        // rust bleeds in the narrow drips under metal
        half rust = smoothstep(0.55h, 0.9h, g.b * g.r) * _RustAmount * side;
        // Gameplay-distance grime: wall-wide sun-faded / water-stained patches, vertical stain bands, a dirt gradient at street level.
        half4 gL = SAMPLE_TEXTURE2D(_GrimeMask, sampler_GrimeMask, wuv * float2(0.022h, 0.016h) + float2(0.13, 0.71));
        half4 gB = SAMPLE_TEXTURE2D(_GrimeMask, sampler_GrimeMask, wuv * float2(0.09h, 0.011h) + float2(0.53, 0.29));
        half fadeAmt = smoothstep(0.35h, 0.75h, gL.g);
        half stainBand = smoothstep(0.45h, 0.85h, gB.r * 0.7h + gB.g * 0.5h);
        half lum0 = dot(albedo, half3(0.299h, 0.587h, 0.114h));
        albedo = lerp(albedo, lerp(half3(lum0, lum0, lum0), albedo, 0.6h) * 1.16h, fadeAmt * 0.7h);
        albedo *= 1.0h - stainBand * 0.32h * saturate(_GrimeAmount);
        half street = 1.0h - smoothstep(0.0h, 5.5h, posWS.y);
        albedo *= 1.0h - street * (0.22h + 0.18h * g2.g);
        albedo = lerp(albedo, albedo * half3(0.5h, 0.46h, 0.42h), soil * 0.8h);
        albedo = lerp(albedo, _GrimeColor.rgb, soil * 0.3h);
        albedo = lerp(albedo, half3(0.32h, 0.14h, 0.06h), rust * 0.6h);
        smooth *= 1.0h - soil * 0.5h;
    }
    if (up > 0.001h && _SlabMode > 0.001h)
    {
        float2 p = posWS.xz / _SlabSize;
        float2 cell = floor(p);
        float2 f = frac(p);
        float h = Hash21(cell);
        float h2 = Hash21(cell + 17.3);
        float3 tone = lerp(float3(0.78, 0.76, 0.74), float3(1.12, 1.08, 1.0), h);
        tone *= lerp(float3(1.0, 1.0, 1.0), float3(1.04, 0.96, 0.9), h2);
        float2 e = min(f, 1.0 - f) * _SlabSize;
        float d = min(e.x, e.y);
        half joint = 1.0h - smoothstep(0.012h, 0.05h, d);
        half4 g = SAMPLE_TEXTURE2D(_GrimeMask, sampler_GrimeMask, posWS.xz * 0.19h);
        half4 g3 = SAMPLE_TEXTURE2D(_GrimeMask, sampler_GrimeMask, posWS.xz * 0.043h + float2(0.2, 0.7));
        half stain = saturate((g.g * g3.g * 1.8h + g.b * 0.25h) * _GrimeAmount);
        // gum spots: sparse dark dots on a 0.5 m grid
        float2 gp = posWS.xz * 2.0;
        float2 gc = floor(gp);
        float gh = Hash21(gc + 3.1);
        float2 off = float2(Hash21(gc + 7.7), Hash21(gc + 9.1)) * 0.6 + 0.2;
        half gum = (gh > 0.9 ? 1.0h : 0.0h) * (1.0h - smoothstep(0.045h, 0.085h, length(frac(gp) - off)));
        float3 a = albedo * tone;
        a = lerp(a, a * float3(0.62, 0.58, 0.54), stain * 0.8h);
        a = lerp(a, a * 0.38, joint * 0.85h);
        a = lerp(a, float3(0.2, 0.19, 0.18), gum * 0.8h);
        albedo = lerp(albedo, a, up * _SlabMode);
        smooth = lerp(smooth, smooth * (1.0h - stain * 0.5h) * (1.0h - joint * 0.4h), up * _SlabMode);
    }
    half lumA = dot(albedo, half3(0.299h, 0.587h, 0.114h));
    albedo = lerp(half3(lumA, lumA, lumA), albedo, 0.78h) * half3(1.02h, 1.0h, 0.96h);   // duller red-brown and tan, a faint warm cast
    s.albedo = albedo;
    s.smoothness = saturate(smooth);
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
    // Distance-dependent mip bias and normal fade: the running-bond joints otherwise alias into a diagonal zig-zag at mid range.
    float camDist = distance(_WorldSpaceCameraPos, input.positionWS);
    float lodBias = saturate((camDist - 8.0) / 45.0) * 2.2;
    surfaceData.albedo = SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, input.uv, lodBias).rgb * _BaseColor.rgb;
#if defined(_NORMALMAP)
    surfaceData.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_BumpMap, sampler_BumpMap, input.uv, lodBias), _BumpScale * (1.0 - 0.75 * saturate((camDist - 9.0) / 36.0)));
#endif
    ApplyWeathering(input.positionWS, input.normalWS, surfaceData);

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
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent());

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
