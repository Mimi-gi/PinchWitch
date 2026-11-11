Shader "PinchWitch/StageLockOverlay"
{
    // ステージ選択で、まだ選べないカードの上に重ねる暗い半透明(StageCardLockOverlay)。
    // カードと同じメッシュをもう一度描くので、奥行きを少し手前へずらして元の面と競合しないようにする。
    // カードの絵柄テクスチャ(_MaskTex、MaterialPropertyBlock で渡す)の透明な余白は暗くしない。
    // ライティングは受けない。
    Properties
    {
        _Color("Color", Color) = (0, 0, 0, 0.5)
        _MaskTex("Mask (alpha)", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // カード側のシェーダーと同じく UV をそのまま使う(タイリング指定なし)。
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half maskAlpha = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, IN.uv).a;
                return half4(_Color.rgb, _Color.a * maskAlpha);
            }
            ENDHLSL
        }
    }
}
