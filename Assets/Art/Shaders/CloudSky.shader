// Atmospheric skybox for URP: gradient + horizon haze, sun disc/glow driven by _SunDir, soft fbm cumulus.
// Static (no time) so the ambient / reflection captures of the sky stay stable.
Shader "Zombies55/CloudSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.31, 0.43, 0.6, 1)
        _HorizonColor ("Horizon", Color) = (0.79, 0.73, 0.64, 1)
        _GroundColor ("Ground", Color) = (0.36, 0.33, 0.3, 1)
        _SunColor ("Sun", Color) = (1, 0.8, 0.56, 1)
        _CloudLit ("Cloud lit", Color) = (1, 0.93, 0.82, 1)
        _CloudShadow ("Cloud shadow", Color) = (0.5, 0.56, 0.66, 1)
        _SunDir ("Sun direction (toward the sun)", Vector) = (0.4, 0.35, 0.8, 0)
        _Exposure ("Exposure", Range(0, 4)) = 1.3
        _HazeWidth ("Horizon haze width", Range(0.02, 0.8)) = 0.28
        _SunSize ("Sun disc size", Range(0.001, 0.08)) = 0.02
        _SunIntensity ("Sun disc intensity", Range(0, 40)) = 8
        _CloudCover ("Cloud cover", Range(0, 1)) = 0.52
        _CloudSoft ("Cloud softness", Range(0.02, 0.6)) = 0.28
        _CloudScale ("Cloud scale", Range(0.2, 6)) = 1.6
        _CloudOffset ("Cloud offset", Vector) = (3.7, 1.3, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "CloudSky"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor, _CloudLit, _CloudShadow;
                float4 _SunDir, _CloudOffset;
                float _Exposure, _HazeWidth, _SunSize, _SunIntensity, _CloudCover, _CloudSoft, _CloudScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 5; k++)
                {
                    s += a * vnoise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    a *= 0.5;
                }
                return s;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sun = normalize(_SunDir.xyz);
                float h = d.y;

                // Gradient with a dusty horizon band.
                float up = saturate(h);
                float haze = exp(-up / max(_HazeWidth, 0.001));
                float3 sky = lerp(_ZenithColor.rgb, _HorizonColor.rgb, saturate(haze));
                float3 ground = lerp(_HorizonColor.rgb * 0.8, _GroundColor.rgb, saturate(-h * 4.0));
                float3 col = h >= 0 ? sky : ground;

                // Warm scatter around the sun (wide haze glow + tight core) and the disc.
                float sd = saturate(dot(d, sun));
                col += _SunColor.rgb * (pow(sd, 6.0) * 0.22 + pow(sd, 48.0) * 0.5);
                float ang = acos(clamp(dot(d, sun), -1.0, 1.0));
                float disc = 1.0 - smoothstep(_SunSize * 0.8, _SunSize * 1.15, ang);

                // Clouds: fbm on a plane projected overhead, lit by looking toward the sun.
                float cloudMask = 0;
                if (h > 0.0)
                {
                    float2 uv = d.xz / (h + 0.22) * _CloudScale + _CloudOffset.xy;
                    float n = fbm(uv);
                    n = n * 0.85 + 0.15 * fbm(uv * 3.1 + 5.0);
                    float dens = smoothstep(_CloudCover - _CloudSoft, _CloudCover + _CloudSoft, n);
                    // Cheap self shadowing: density a step toward the sun.
                    float2 sOff = normalize(sun.xz + 1e-4) * 0.1;
                    float nS = fbm(uv + sOff);
                    float shade = saturate((n - nS) * 5.0 + 0.55);
                    float3 cc = lerp(_CloudShadow.rgb, _CloudLit.rgb, shade);
                    cc += _SunColor.rgb * pow(sd, 10.0) * 0.5 * (1.0 - dens * 0.5);   // silver lining near the sun
                    float fade = smoothstep(0.0, 0.16, h);                              // melt into haze at the horizon
                    cloudMask = dens * fade;
                    cc = lerp(_HorizonColor.rgb * 0.95, cc, fade);
                    col = lerp(col, cc, cloudMask * 0.92);
                }
                col += _SunColor.rgb * _SunIntensity * disc * (1.0 - cloudMask) * step(0.0, h + 0.02);
                return half4(col * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
