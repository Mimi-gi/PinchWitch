Shader "Hidden/OutlineSDF/Silhouette"
{
    // OutlineSDFFeature 用のシルエット(マスク)パス。
    // オブジェクト自身のマテリアルではなく、RenderFeature側でオーバーライドマテリアルとして
    // Grabbableレイヤーのオブジェクトに強制適用される。
    // 表示色・太さは OutlineController が Renderer の MaterialPropertyBlock に
    // 書き込む _OutlineColor / _OutlineWidth をそのまま読む。
    Properties
    {
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth("Outline Width (0-1)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            // Rendererごとに異なる値（MaterialPropertyBlock経由）
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            // OutlineSDFFeatureがフレーム毎に設定するグローバル値
            float _OutlineWidthMultiplier;
            float _OutlineWidthPx;
            float3 _OutlinePlayerWorldPos;
            float _OutlinePlayerFadeStart;
            float _OutlinePlayerFadeEnd;
            float _OutlinePlayerFadeStrength;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            void frag(Varyings IN, out half4 seedOut : SV_Target0, out half4 payloadOut : SV_Target1)
            {
                // プレイヤーが近いほどアウトラインを控えめにする
                float distToPlayer = distance(IN.positionWS, _OutlinePlayerWorldPos);
                float fadeRange = max(_OutlinePlayerFadeEnd - _OutlinePlayerFadeStart, 0.0001);
                float fadeT = saturate((distToPlayer - _OutlinePlayerFadeStart) / fadeRange);
                float playerFade = lerp(1.0 - _OutlinePlayerFadeStrength, 1.0, fadeT);

                // _OutlineWidth は OutlineController 側で「表示ON/OFF」の意味で使われている値
                // (旧・頂点押し出し方式での世界単位の太さ(例: 0.08)を流用しているため、
                // ここでpxの太さへそのまま掛け算すると極端に細くなってしまう)。
                // 太さ自体は _OutlineWidthPx(グローバル設定)基準とし、_OutlineWidth はゲートとしてのみ使う。
                float widthPx = _OutlineWidthPx * _OutlineWidthMultiplier * playerFade;

                // 太さ0、または非ハイライト(_OutlineWidth<=0)のオブジェクトはマスクに書き込まない
                clip(_OutlineWidth - 0.0001);
                clip(widthPx - 0.001);

                seedOut = half4(IN.positionHCS.xy, 0, 1);
                payloadOut = half4(_OutlineColor.rgb, widthPx);
            }
            ENDHLSL
        }
    }
}
