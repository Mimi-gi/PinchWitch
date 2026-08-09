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

            // 深度バッファを一切使わない。
            // 求めているのは「完全な投影シルエット」なので深度テストは不要。
            // 遮蔽は合成パスでシーン深度と比較して行う。
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // MRT(SV_Target0/1 同時出力)と、フラグメントでの SV_POSITION 読み取りに
            // シェーダーモデル 3.0 以上が要る。未指定だとデフォルト 2.5 になり両方使えない。
            #pragma target 3.5

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

            // seed は画面ピクセル座標を保持するため half では精度が足りない(1024px を超えると
            // 刻み幅が 1px 以上になり距離場が階段状になる)。float で出力する。
            void frag(Varyings IN, out float4 seedOut : SV_Target0, out float4 payloadOut : SV_Target1)
            {
                // プレイヤーが近いほどアウトラインを控えめにする
                float distToPlayer = distance(IN.positionWS, _OutlinePlayerWorldPos);
                float fadeRange = max(_OutlinePlayerFadeEnd - _OutlinePlayerFadeStart, 0.0001);
                float fadeT = saturate((distToPlayer - _OutlinePlayerFadeStart) / fadeRange);
                float playerFade = lerp(1.0 - _OutlinePlayerFadeStrength, 1.0, fadeT);

                // _OutlineWidth は OutlineController がオブジェクト単位でトゥイーンする太さ倍率(0〜1)。
                // これにより対象ごとに独立して「ニュッと」出現/消失できる。
                // _OutlineWidthMultiplier はグローバルのマスター倍率(通常1)。
                float widthPx = _OutlineWidthPx * _OutlineWidthMultiplier * _OutlineWidth * playerFade;

                // 太さ0、または非ハイライト(_OutlineWidth<=0)のオブジェクトはマスクに書き込まない
                clip(_OutlineWidth - 0.0001);
                clip(widthPx - 0.001);

                // xy = シルエット画素のスクリーン座標(2D SDF のシード)。seed は RG のみ
                seedOut = float4(IN.positionHCS.xy, 0, 1);
                payloadOut = float4(_OutlineColor.rgb, widthPx);
            }
            ENDHLSL
        }
    }
}
