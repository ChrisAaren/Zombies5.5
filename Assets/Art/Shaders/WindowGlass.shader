// Window glass for URP: reflection-probe sampling (box projected) with a Fresnel ramp over a dimmed interior.
// Blended as premultiplied alpha, so the interior card behind it shows through, darker, with the street and sky mirrored on top.
Shader "Zombies55/WindowGlass"
{
    Properties
    {
        _BaseColor ("Glass tint", Color) = (0.05, 0.08, 0.1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.98
        _FresnelPower ("Fresnel power", Range(0.5, 8)) = 2.5
        _ReflMin ("Reflection at normal incidence", Range(0, 1)) = 0.3
        _ReflMax ("Reflection at grazing angles", Range(0, 1)) = 0.95
        _ReflIntensity ("Reflection intensity", Range(0, 3)) = 1.35
        _InteriorDim ("Interior dimming", Range(0, 1)) = 0.5
        _Waviness ("Per-pane tilt", Range(0, 0.2)) = 0.06
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #define _SCREENSPACEREFLECTIONS_OFF 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness, _FresnelPower, _ReflMin, _ReflMax, _ReflIntensity, _InteriorDim, _Waviness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 N = normalize(i.normalWS);
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // Each pane sits a hair off true, so neighbouring windows mirror slightly different slices of the street.
                float2 cell = floor(float2(i.positionWS.x + i.positionWS.z, i.positionWS.y) * float2(0.7, 0.31));
                float2 tilt = (float2(hash12(cell), hash12(cell + 17.3)) - 0.5) * 2.0 * _Waviness;
                half3 T = normalize(cross(N, half3(0, 1, 0)) + 1e-4);
                N = normalize(N + T * tilt.x + half3(0, 1, 0) * tilt.y);

                half NoV = saturate(dot(N, V));
                half rel = pow(1.0 - NoV, _FresnelPower);
                half fres = _ReflMin + (_ReflMax - _ReflMin) * rel;
                half3 R = reflect(-V, N);
                half3 env = GlossyEnvironmentReflection(R, i.positionWS, 1.0h - _Smoothness, 1.0h, GetNormalizedScreenSpaceUV(i.positionCS));

                half3 refl = env * fres * _ReflIntensity;
                // Face-on the pane is mostly clear (interior shows); toward grazing it turns into a mirror and hides the room.
                half a = saturate(_InteriorDim + (1.0h - _InteriorDim) * rel);
                half3 col = refl + _BaseColor.rgb * a;
                col = MixFogColor(col, unity_FogColor.rgb * a, i.fogFactor);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
