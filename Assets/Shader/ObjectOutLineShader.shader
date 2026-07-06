Shader "Custom/ObjectOutLineShader"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}

        // -- アウトライン用の設定項目 --
        _OutlineColor("Outline Color", Color) = (1, 1, 0, 1) // デフォルトは黄色
        _OutlineWidth("Outline Width", Range(0.0, 0.1)) = 0.0 // 初期の太さは0（非表示）にしておく

        // ★ 角割れ防止用の追加設定
        [Header(Outline Settings)]
        _ExpandMode("Expand Mode (0:Normal, 1:Center)", Range(0, 1)) = 1.0

        // -- もやもや（ノイズ）設定 --
        [Header(Noise Settings)]
        _NoiseScale("Noise Scale", Float) = 8.0            // ノイズの細かさ
        _NoiseSpeed("Noise Speed", Float) = 1.0            // もやもやの動く速さ
        _NoiseWidthStrength("Width Wobble", Range(0, 1)) = 0.5  // 輪郭の太さの揺らぎ量
        _NoiseDissolve("Edge Dissolve", Range(0, 1)) = 0.4      // 縁のかすれ（欠け）具合（オブジェクト際の基準値）
        _FadeRange("Fade Range", Range(0.01, 1)) = 0.5          // 外側へ向かって散り切るまでの範囲
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        // ==========================================
        // 1パス目: アウトラインを描画する「縁取り処理」
        // ==========================================
        Pass
        {
            Name "Outline"

            // 表面のポリゴンを描画せず、裏面のポリゴンだけを描画する（背面押し出し法）
            Cull Front

            HLSLPROGRAM
            #pragma vertex vertOutline
#pragma fragment fragOutline

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION; // オブジェクト空間の頂点位置
                float3 normalOS : NORMAL; // オブジェクト空間の法線（面が向いている方向）
            };

            struct Varyings {
                float4 positionHCS : SV_POSITION; // 画面(クリップ)空間の頂点位置
                float3 positionWS : TEXCOORD0; // フラグメントノイズ用のワールド座標
                float3 fadeDirWS : TEXCOORD1; // 押し出し方向（外縁フェードの距離推定に使用）
            };

            // Inspectorから設定できる変数をシェーダー内部と紐づける
            // ※SRPバッチャーを有効にするため構造を2パス目で揃えています
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _ExpandMode; // 追加
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseWidthStrength;
                float _NoiseDissolve;
                float _FadeRange;
            CBUFFER_END

            float3 _GradHash(float3 p) {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
               dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
            }

            float GradNoise(float3 p) {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                return lerp(
                        lerp(lerp(dot(_GradHash(i + float3(0, 0, 0)), f - float3(0, 0, 0)),
                      dot(_GradHash(i + float3(1, 0, 0)), f - float3(1, 0, 0)), u.x),
                 lerp(dot(_GradHash(i + float3(0, 1, 0)), f - float3(0, 1, 0)),
          dot(_GradHash(i + float3(1, 1, 0)), f - float3(1, 1, 0)), u.x),
                 u.y),
                        lerp(lerp(dot(_GradHash(i + float3(0, 0, 1)), f - float3(0, 0, 1)),
                      dot(_GradHash(i + float3(1, 0, 1)), f - float3(1, 0, 1)), u.x),
                 lerp(dot(_GradHash(i + float3(0, 1, 1)), f - float3(0, 1, 1)),
          dot(_GradHash(i + float3(1, 1, 1)), f - float3(1, 1, 1)), u.x), u.y),
                        u.z);
            }

            Varyings vertOutline(Attributes IN) {
                Varyings OUT;

                // ★ 角割れ対策：押し出す方向の計算 ★
                // 正方形のような「カクカクしたモデル(ハードエッジ)」は面ごとに法線が完全に分離しているため、
                // 法線方向に押し出すと角が裂けてしまいます。
                // そこで、「モデルの中心から外側に向かう方向」を使って膨らませるモードを作ります。

                // モデル中心からの外向きベクトル
                float3 centerDir = normalize(IN.positionOS.xyz);

                // _ExpandMode(0.0 〜 1.0) で「法線」か「中心からの方向」かをブレンドして決める
                float3 expandDir = normalize(lerp(IN.normalOS, centerDir, _ExpandMode));

                // ★ もやもや(1)：ワールド座標＋時間でノイズを引き、押し出し幅を揺らす。
                // ワールド座標基準なので、同じ位置の頂点は同じ揺れ方をして角割れしない。
                float3 baseWS = TransformObjectToWorld(IN.positionOS.xyz);
                float widthNoise = GradNoise(baseWS * _NoiseScale + _Time.y * _NoiseSpeed); // -1〜1
                float wobbledWidth = _OutlineWidth * (1.0 + widthNoise * _NoiseWidthStrength);

                // ブレンドされた方向へ頂点を押し出す
                float3 expandedPosition = IN.positionOS.xyz + expandDir * wobbledWidth;

                OUT.positionHCS = TransformObjectToHClip(expandedPosition);
                OUT.positionWS = TransformObjectToWorld(expandedPosition);
                OUT.fadeDirWS = TransformObjectToWorldDir(expandDir);
                return OUT;
            }

            half4 fragOutline(Varyings IN) : SV_Target {
                // ★ もやもや(2)：ピクセル単位のノイズで縁を雲状に欠けさせる。
                // 頂点密度に依存しないので、ローポリのモデルでももやもやが出る。
                float dissolveNoise = GradNoise(IN.positionWS * _NoiseScale * 2.0
                    + float3(0.0, _Time.y * _NoiseSpeed, _Time.y * _NoiseSpeed * 0.7));
                float noise01 = dissolveNoise * 0.5 + 0.5; // 0〜1に正規化

                // ★ もやもや(3)：オブジェクトから離れるほど閾値を上げて散らす。
                // 殻の外縁ほど押し出し方向が視線と直交する性質を利用し、
                // |dot(押し出し方向, 視線)| を「オブジェクトへの近さ」として使う。
                // 外縁(=0)では閾値が1になり必ず散るので、殻の角ばった輪郭は見えない。
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float nearness = abs(dot(normalize(IN.fadeDirWS), viewDirWS)); // 1:オブジェクト際 0:外縁
                float distanceFade = smoothstep(0.0, _FadeRange, nearness);
                float threshold = lerp(1.0, _NoiseDissolve, distanceFade);
                clip(noise01 - threshold); // しきい値未満のピクセルを捨てる

                return _OutlineColor;
            }
            ENDHLSL
        }

        // ==========================================
        // 2パス目: オブジェクトそのものの「通常描画」
        // ==========================================
        Pass
        {
            Name "ForwardLit"

            // 通常の描画なので裏面を非表示にする（デフォルト）
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
#pragma fragment frag

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _ExpandMode; // 追加
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseWidthStrength;
                float _NoiseDissolve;
                float _FadeRange;
            CBUFFER_END


            Varyings vert(Attributes IN) {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return color;
            }
            ENDHLSL
        }
    }
}
