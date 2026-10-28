Shader "Naispo/PaintedBackdropSoftening"
{
    Properties
    {
        [PerRendererData] _MainTex("Background", 2D) = "white" {}
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _BlurredTex("Cached distant room", 2D) = "black" {}
        [HideInInspector] _HasBlurredTex("Cached defocus available", Float) = 0
        _Saturation("Saturation", Range(0,1)) = 0.84
        _Brightness("Brightness", Range(0.2,1)) = 0.8
        _HighlightCompression("Highlight compression", Range(0,1)) = 0.65
        [HideInInspector] _UvRect("Source crop", Vector) = (0,0,1,1)
        [HideInInspector] _CandleMask("Painted flame motion", 2D) = "black" {}
        [HideInInspector] _CandleMotionScale("Flame image scale", Vector) = (0,0,0,0)
        [HideInInspector] _CandleStrength("Painted candles enabled", Float) = 0
        [HideInInspector] _CandleTime("Garden presentation clock", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BlurredTex);
            SAMPLER(sampler_BlurredTex);
            TEXTURE2D(_CandleMask);
            SAMPLER(sampler_CandleMask);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _UvRect;
                half _HasBlurredTex;
                half _Saturation, _Brightness, _HighlightCompression;
                float4 _CandleMotionScale;
                float _CandleTime;
                half _CandleStrength;
            CBUFFER_END

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
            };
            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color * _Color * unity_SpriteColor;
                return output;
            }

            half4 ReadBackground(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, clamp(uv, _UvRect.xy, _UvRect.zw));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half candleLight = 1;
                if (_CandleStrength > 0)
                {
                    float4 encoded = SAMPLE_TEXTURE2D(_CandleMask, sampler_CandleMask, uv);
                    float4 mask = (encoded * 255.0 - 128.0) / 127.0;
                    float2 sway = float2(sin(_CandleTime * 3.7), cos(_CandleTime * 3.7)) * .72 +
                        float2(sin(_CandleTime * 6.3), cos(_CandleTime * 6.3)) * .28;
                    float2 flutter = float2(sin(_CandleTime * 5.1), cos(_CandleTime * 5.1)) * .7 +
                        float2(sin(_CandleTime * 9.2), cos(_CandleTime * 9.2)) * .3;
                    uv += float2(dot(mask.rg, sway) * .19, dot(mask.rg, flutter) * .12) *
                        _CandleMotionScale.xy * _CandleStrength;
                    candleLight += dot(mask.ba, flutter * .15 + sway * .05) * _CandleStrength;
                }
                half4 center = ReadBackground(uv);
                half4 blurred = SAMPLE_TEXTURE2D(_BlurredTex, sampler_BlurredTex,
                    clamp(uv, _UvRect.xy, _UvRect.zw));
                // Each cached pixel already has the appropriate blur radius for
                // its painted depth; no screen-space center mask is needed.
                half3 softColor = blurred.rgb / max(blurred.a, 0.0001h);
                half3 color = lerp(center.rgb, softColor, _HasBlurredTex);
                color *= candleLight;
                half luminance = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
                color = lerp(luminance.xxx, color, _Saturation);
                // Compress only brighter painted details. Shadows keep their room
                // structure, while the brightest candle cannot compete with a ball.
                half peak = max(color.r, max(color.g, color.b));
                color *= _Brightness / (1 + max(0.0h, peak - 0.25h) * _HighlightCompression);
                // Preserve the source alpha instead of spreading its silhouette.
                return half4(color * input.color.rgb, center.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
