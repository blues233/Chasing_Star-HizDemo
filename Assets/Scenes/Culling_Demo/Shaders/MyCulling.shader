Shader "URP/Instanced/CustomInstanced"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
    }
    SubShader
    {
        Tags {
            "RenderType"="Opaque"
            "Queue" = "Geometry+0"
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            StructuredBuffer<float4x4> positionBuffer;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float4x4 data = positionBuffer[instanceID];
                float3 pos = IN.positionOS * data._11;
                float3 worldPosition = data._14_24_34 + pos;
                OUT.positionCS = TransformWorldToHClip(worldPosition);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return col;
            }
            ENDHLSL
        }
    }
}
