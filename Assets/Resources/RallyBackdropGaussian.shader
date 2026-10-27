Shader "Hidden/Naispo/PaintedBackdropGaussian"
{
    Properties
    {
        _MainTex("Source", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float4 _UvRect;
        float _Sigma;
        int _KernelRadius;

        float4 Gaussian(float2 uv, float2 direction, bool premultiply)
        {
            float4 accumulated = 0;
            float totalWeight = 0;
            // Unit texel spacing avoids sparse repeats of tiny stars and painted
            // edges. Three sigma captures the smooth Gaussian tail at full size.
            [loop]
            for (int offset = -_KernelRadius; offset <= _KernelRadius; offset++)
            {
                float weight = exp(-0.5 * offset * offset / (_Sigma * _Sigma));
                float2 sampleUv = clamp(uv + direction * offset, _UvRect.xy, _UvRect.zw);
                float4 color = tex2D(_MainTex, sampleUv);
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
