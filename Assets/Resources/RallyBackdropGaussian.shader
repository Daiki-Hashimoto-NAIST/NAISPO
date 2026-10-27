Shader "Hidden/Naispo/PaintedBackdropGaussian"
{
    Properties
    {
        _MainTex("Source", 2D) = "white" {}
        _DepthTex("Painted scene depth", 2D) = "white" {}
        _HasDepthTex("Scene depth available", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _DepthTex;
        float4 _MainTex_TexelSize;
        float4 _UvRect;
        float _Sigma;
        float _HasDepthTex;
        int _KernelRadius;

        float ReadDepth(float2 uv)
        {
            return _HasDepthTex > 0.5 ? saturate(tex2Dlod(_DepthTex, float4(uv, 0, 0)).r) : 1.0;
        }

        float4 Gaussian(float2 uv, float2 direction, bool premultiply)
        {
            uv = clamp(uv, _UvRect.xy, _UvRect.zw);
            float depth = ReadDepth(uv);
            if (depth <= 0.025)
            {
                float4 sharp = tex2Dlod(_MainTex, float4(uv, 0, 0));
                if (premultiply) sharp.rgb *= sharp.a;
                return sharp;
            }
            // The footprint itself grows with painted depth. Mixing a sharp
            // image with one fixed blur would leave fine details doubled.
            float sigma = max(0.01, _Sigma * depth);
            int radius = min(_KernelRadius, (int)ceil(3.0 * sigma));
            float4 accumulated = 0;
            float totalWeight = 0;
            // Unit texel spacing avoids sparse repeats of tiny stars and painted
            // edges. Three sigma captures the smooth Gaussian tail at full size.
            [loop]
            for (int offset = -radius; offset <= radius; offset++)
            {
                float weight = exp(-0.5 * offset * offset / (sigma * sigma));
                float2 sampleUv = clamp(uv + direction * offset, _UvRect.xy, _UvRect.zw);
                // Keep foreground columns from bleeding across their edges into
                // the distant room, without interrupting gradual depth changes.
                float depthDifference = abs(ReadDepth(sampleUv) - depth);
                weight *= 1.0 - smoothstep(0.06, 0.22, depthDifference);
                // Explicit base LOD keeps derivative sampling out of this
                // per-pixel variable-length loop (all bakes use full resolution).
                float4 color = tex2Dlod(_MainTex, float4(sampleUv, 0, 0));
                if (premultiply) color.rgb *= color.a;
                accumulated += color * weight;
                totalWeight += weight;
            }
            return accumulated / max(totalWeight, 0.00001);
        }

        float4 Horizontal(v2f_img input) : SV_Target
        {
            return Gaussian(input.uv, float2(abs(_MainTex_TexelSize.x), 0), true);
        }

        float4 Vertical(v2f_img input) : SV_Target
        {
            return Gaussian(input.uv, float2(0, abs(_MainTex_TexelSize.y)), false);
        }
        ENDHLSL

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Horizontal
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Vertical
            ENDHLSL
        }
    }
}
