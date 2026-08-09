Shader "Custom/ToonShader"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        [Header(Shade)]
        _ShadeColor("Shade Color", Color) = (0.6, 0.6, 0.65, 1)
        _ShadeThreshold("Shade Threshold", Range(0.0, 1.0)) = 0.5
        _ShadeFeather("Shade Feather", Range(0.001, 0.5)) = 0.05

        [Header(Rim Light)]
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(0.1, 8.0)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadeColor;
                float _ShadeThreshold;
                float _ShadeFeather;
                half4 _RimColor;
                float _RimPower;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                Light mainLight = GetMainLight();

                // ライトの当たり具合を0〜1の2諧調にざっくり分けるだけのシンプルなトゥーン処理
                float ndotl01 = dot(normalWS, mainLight.direction) * 0.5 + 0.5;
                float shade = smoothstep(_ShadeThreshold - _ShadeFeather, _ShadeThreshold + _ShadeFeather, ndotl01);

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                half3 litColor = lerp(_ShadeColor.rgb * texColor.rgb, texColor.rgb, shade) * mainLight.color;

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float rim = pow(saturate(1.0 - dot(normalWS, viewDirWS)), _RimPower);
                litColor += rim * _RimColor.rgb;

                return half4(litColor, texColor.a);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
