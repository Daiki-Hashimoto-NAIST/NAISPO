Shader "Naispo/PaintedBackdropSoftening"
{
    Properties
    {
        [PerRendererData] _MainTex("Background", 2D) = "white" {}
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        _BlurPixels("Blur pixels at 1280 width", Range(0,4)) = 2
        _Saturation("Saturation", Range(0,1)) = 0.72
        _Brightness("Brightness", Range(0.2,1)) = 0.8
        _HighlightCompression("Highlight compression", Range(0,1)) = 0.65
        [HideInInspector] _UvRect("Source crop", Vector) = (0,0,1,1)
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _UvRect;
                float _BlurPixels;
                half _Saturation, _Brightness, _HighlightCompression;
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

            half4 WeightedBackground(float2 uv)
            {
                half4 sampleColor = ReadBackground(uv);
                return half4(sampleColor.rgb * sampleColor.a, sampleColor.a);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // UV derivatives keep the softness in screen space regardless of
                // import size or the cover crop. Resolution scaling keeps the same
                // composition at 720p and larger window sizes.
                float radius = _BlurPixels * (_ScreenParams.x / 1280.0);
                float2 dx = ddx(input.uv) * (radius * 0.5);
                float2 dy = ddy(input.uv) * (radius * 0.5);
                half4 center = ReadBackground(input.uv);
                // Dense 5x5 binomial weights (1,4,6,4,1) avoid separate ghost
                // copies of tiny painted stars at larger window sizes. The center
                // sample is reused so the full kernel still takes only 25 reads.
                half4 blurred = half4(center.rgb * center.a, center.a) * (36.0h / 256.0h);
                [unroll]
                for (int y = -2; y <= 2; y++)
                {
                    half weightY = y == 0 ? 6.0h : (abs(y) == 1 ? 4.0h : 1.0h);
                    [unroll]
                    for (int x = -2; x <= 2; x++)
                    {
                        if (x != 0 || y != 0)
                        {
                            half weightX = x == 0 ? 6.0h : (abs(x) == 1 ? 4.0h : 1.0h);
                            blurred += WeightedBackground(input.uv + dx * x + dy * y) *
                                (weightX * weightY / 256.0h);
                        }
                    }
                }
                half3 color = blurred.rgb / max(blurred.a, 0.0001h);
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
