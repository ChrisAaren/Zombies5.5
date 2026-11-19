Shader "Zombies55/GradientSky"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.37, 0.61, 0.84, 1)
        _HorizonColor ("Horizon Color", Color) = (1, 0.84, 0.65, 1)
        _BottomColor ("Bottom Color", Color) = (0.35, 0.27, 0.21, 1)
        _Exponent ("Gradient Exponent", Range(0.1, 4)) = 0.6
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TopColor;
            fixed4 _HorizonColor;
            fixed4 _BottomColor;
            half _Exponent;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float h = normalize(i.dir).y;
                float up = pow(saturate(h), _Exponent);
                float down = pow(saturate(-h), _Exponent);
                fixed4 skyCol = lerp(_HorizonColor, _TopColor, up);
                fixed4 groundCol = lerp(_HorizonColor, _BottomColor, down);
                return h >= 0 ? skyCol : groundCol;
            }
            ENDCG
        }
    }
    Fallback Off
}
