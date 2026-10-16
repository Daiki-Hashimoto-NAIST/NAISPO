Shader "Naispo/BallLightReveal"
{
    Properties
    {
        _MainTex("Silhouette",2D)="black" {}
        _ColorTex("Aligned camera",2D)="black" {}
        _LightPosition("Ball position",Vector)=(0,0,0,0)
        _Radius("Radius",Float)=1.8
        _ShadowBrightness("Shadow brightness",Float)=0.14
        _HasColor("Has current color",Float)=0
        _LightEnabled("Light enabled",Float)=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ColorTex); SAMPLER(sampler_ColorTex);
            float4 _LightPosition;
            float _Radius, _ShadowBrightness, _HasColor, _LightEnabled;
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; };
            Varyings Vert(Attributes i)
            {
                Varyings o; float3 world=TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(world); o.uv=i.uv; o.world=world.xy; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                half4 body=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                half4 camera=SAMPLE_TEXTURE2D(_ColorTex,sampler_ColorTex,i.uv);
                float distanceToBall=distance(i.world,_LightPosition.xy);
                float light=(1-smoothstep(_Radius*0.55,max(_Radius,0.01),distanceToBall))*_LightEnabled;
                half3 shadow=lerp(half3(0.004,0.006,0.012),half3(0.45,0.6,0.8)*_ShadowBrightness,body.a);
                half3 result=lerp(shadow,camera.rgb,light*_HasColor*camera.a);
                float glow=pow(saturate(1-distanceToBall/0.65),3)*_LightEnabled;
                result+=half3(1,0.78,0.35)*glow*0.25;
                return half4(result,1);
            }
            ENDHLSL
        }
    }
}
