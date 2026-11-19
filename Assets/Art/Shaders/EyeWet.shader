// Wet-eye shell: drawn over the eyeball mesh (same skinned mesh, vertices pushed 0.4 mm along the normal).
//  LidShadow - multiply pass: the upper lid and lashes shade the top of the eyeball.
//  Wet       - additive pass: cornea reflection of the sky, a sharp sun glint, and a faint tear line along the lower lid.
Shader "Zombies55/EyeWet"
{
    Properties
    {
        _Wet ("Wet reflection strength", Range(0, 4)) = 2.5
        _Glint ("Sun glint strength", Range(0, 20)) = 6
        _LidShadow ("Lid / lash shadow", Range(0, 1)) = 0.5
        _Tear ("Tear line strength", Range(0, 1)) = 0.18
        _Offset ("Shell offset (m)", Float) = 0.0004
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest LEqual
        Cull Back

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half _Wet, _Glint, _LidShadow, _Tear;
            float _Offset;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
        Varyings Vert(Attributes i)
        {
            Varyings o;
            float3 p = i.positionOS.xyz + i.normalOS * _Offset;
            o.positionWS = TransformObjectToWorld(p);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "LidShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor Zero
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                half ny = normalize(i.normalWS).y;
                half top = smoothstep(0.0h, 0.62h, ny);
                half shade = 1.0h - _LidShadow * top;
                return half4(shade, shade, shade, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Wet"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            half4 Frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 r = reflect(-v, n);
                half fres = 0.04h + 0.96h * pow(1.0h - saturate(dot(n, v)), 5.0h);
                half3 env = GlossyEnvironmentReflection(r, 0.03h, 1.0h);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half spec = pow(saturate(dot(r, l.direction)), 900.0h) * _Glint * l.shadowAttenuation;
                half3 col = env * fres * _Wet + l.color * spec;
                half tear = smoothstep(0.1h, 0.0h, abs(n.y + 0.64h)) * _Tear;     // thin wet meniscus along the lower lid
                col += tear * (env * 0.5h + 0.03h);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
