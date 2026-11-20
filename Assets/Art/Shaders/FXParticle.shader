// Explosion particles and ground marks. Unlit, transparent forward pass (renders on top of the deferred GBuffer scene), flipbook-friendly:
// the particle system's Texture Sheet Animation supplies the frame UVs. Stream Custom1.x ("heat") boosts the colour into HDR so a
// tone-mapped fire flipbook can burn white-hot early and fall back to plain colour as it cools.
//   _Mode 0: alpha blend (smoke, dust, debris bits, scorch)
//   _Mode 1: additive, texture is premultiplied (fire loop)
//   _Mode 2: additive, straight texture with alpha as coverage (sparks, flash, embers, shockwave)
// Particles fade out when closer than _NearFade.x to the camera (full at _NearFade.y), so a fireball at the lens never whites out the screen.
Shader "Zombies55/FXParticle"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [HDR] _Tint ("Tint", Color) = (1, 1, 1, 1)
        _HeatBoost ("Heat boost (x Custom1.x)", Float) = 0
        _Desat ("Desaturate as heat falls (0-1)", Range(0, 1)) = 0
        _Mode ("Mode (0 alpha, 1 additive premult, 2 additive straight)", Float) = 0
        _NearFade ("Near fade (start, full)", Vector) = (1.5, 4, 0, 0)
        _FogAmount ("Fog amount", Range(0, 1)) = 1
        [HideInInspector] _SrcBlend ("Src", Float) = 5
        [HideInInspector] _DstBlend ("Dst", Float) = 10
        _OffsetFactor ("Depth offset factor", Float) = 0
        _OffsetUnits ("Depth offset units", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FXForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                float _HeatBoost;
                half _Desat;
                float _Mode;
                float4 _NearFade;
                half _FogAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float3 uv : TEXCOORD0;   // xy = UV, z = Custom1.x (particle vertex stream packs it after the UV)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fade : TEXCOORD1;
                float fog : TEXCOORD2;
                half heat : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = TRANSFORM_TEX(i.uv.xy, _BaseMap);
                o.heat = saturate(i.uv.z);
                half boost = 1.0h + _HeatBoost * o.heat;
                o.color = half4(i.color.rgb * boost, i.color.a);
                float d = distance(ws, _WorldSpaceCameraPos);
                o.fade = saturate((d - _NearFade.x) / max(0.01, _NearFade.y - _NearFade.x));
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 rgb = t.rgb * i.color.rgb * _Tint.rgb;
                half lum = dot(rgb, half3(0.3h, 0.59h, 0.11h));
                rgb = lerp(rgb, lum.xxx, (1.0h - saturate(i.heat * 2.0h)) * _Desat);   // a cooled fireball turns to grey smoke
                half a = i.color.a * _Tint.a * i.fade;
                if (_Mode < 0.5)
                {
                    rgb = lerp(rgb, MixFog(rgb, i.fog), _FogAmount);
                    return half4(rgb, t.a * a);
                }
                half k = a * (_Mode < 1.5 ? 1.0h : t.a);
                rgb *= k;
                rgb = lerp(rgb, MixFogColor(rgb, half3(0, 0, 0), i.fog), _FogAmount);
                return half4(rgb, 1);
            }
            ENDHLSL
        }
    }
}
