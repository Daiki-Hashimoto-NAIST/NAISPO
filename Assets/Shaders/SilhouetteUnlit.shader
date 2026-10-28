Shader "Naispo/SilhouetteUnlit"
{
    Properties
    {
        [PerRendererData] _MainTex("Body colors", 2D) = "white" {}
        _UseVertexColor("Use vertex tint", Float) = 0
        _TintColor("Renderer tint", Color) = (1,1,1,1)
        _OutlineColor("Inner contour", Color) = (1,1,1,0)
        _OutlineWidth("Contour screen pixels", Range(0,4)) = 0
        [Header(Court edge fade)]
        _FadeCenterX("Court centre X", Float) = 0
        _FadeHalfWidth("Court half width", Float) = 1000
        _FadeSide("Court side sign", Float) = 0
        _FadeWidth("Fade width", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
                float _UseVertexColor;
                half4 _TintColor;
                float _FadeCenterX;
                float _FadeHalfWidth;
                float _FadeSide;
                float _FadeWidth;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half4 color : COLOR; };
            Varyings Vert(Attributes input) {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 body = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                body *= lerp(half4(1,1,1,1),input.color,saturate(_UseVertexColor));
                body *= _TintColor;
                // Screen-space derivatives keep the line narrow for both mask resolutions
                // and window sizes. No outer glow or alpha expansion obscures the court.
                float2 stepUV = (abs(ddx(input.uv)) + abs(ddy(input.uv))) * _OutlineWidth;
                if (_OutlineWidth > 0 && body.a > 0)
                {
                    // Only the contour's neighborhood is linear-filtered. Point-sampled
                    // body RGB/alpha above retain the original mask, while interpolation
                    // prevents the narrow line from skipping between enlarged mask texels.
                    // sampler_LinearClamp is supplied by URP Core's GlobalSamplers include.
                    half neighbor = SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv + float2(stepUV.x,0)).a;
                    neighbor = min(neighbor, SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv - float2(stepUV.x,0)).a);
                    neighbor = min(neighbor, SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv + float2(0,stepUV.y)).a);
                    neighbor = min(neighbor, SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv - float2(0,stepUV.y)).a);
                    float2 diagonal = stepUV * .70710678;
                    neighbor = min(neighbor, SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv + diagonal).a);
                    neighbor = min(neighbor, SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv - diagonal).a);
                    neighbor = min(neighbor, SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv + float2(diagonal.x,-diagonal.y)).a);
                    neighbor = min(neighbor, SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv + float2(-diagonal.x,diagonal.y)).a);
                    half edge = saturate(body.a - neighbor) * _OutlineColor.a;
                    body.rgb = lerp(body.rgb, _OutlineColor.rgb, edge);
                }
                // Opt-in world-X fade. Home court is opaque; only overflow beyond the
                // centre or the configured outer edge disappears. Not a collision rule.
                if (_FadeWidth > 0.00001)
                {
                    float inward = _FadeSide * (input.positionWS.x - _FadeCenterX);
                    float overflow = inward >= 0 ? max(0, inward - _FadeHalfWidth) : -inward;
                    body.a *= 1 - smoothstep(0, _FadeWidth, overflow);
                }
                return body;
            }
            ENDHLSL
        }
    }
}
