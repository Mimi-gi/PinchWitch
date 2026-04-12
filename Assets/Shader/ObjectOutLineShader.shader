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
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

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

            struct Attributes
            {
                float4 positionOS : POSITION; // オブジェクト空間の頂点位置
                float3 normalOS : NORMAL;     // オブジェクト空間の法線（面が向いている方向）
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION; // 画面(クリップ)空間の頂点位置
            };

            // Inspectorから設定できる変数をシェーダー内部と紐づける
            // ※SRPバッチャーを有効にするため構造を2パス目で揃えています
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _ExpandMode; // 追加
            CBUFFER_END

            Varyings vertOutline(Attributes IN)
            {
                Varyings OUT;
                
                // ★ 角割れ対策：押し出す方向の計算 ★
                // 正方形のような「カクカクしたモデル(ハードエッジ)」は面ごとに法線が完全に分離しているため、
                // 法線方向に押し出すと角が裂けてしまいます。
                // そこで、「モデルの中心から外側に向かう方向」を使って膨らませるモードを作ります。
                
                // モデル中心からの外向きベクトル
                float3 centerDir = normalize(IN.positionOS.xyz);
                
                // _ExpandMode(0.0 〜 1.0) で「法線」か「中心からの方向」かをブレンドして決める
                float3 expandDir = normalize(lerp(IN.normalOS, centerDir, _ExpandMode));
                
                // ブレンドされた方向へ頂点を押し出す
                float3 expandedPosition = IN.positionOS.xyz + expandDir * _OutlineWidth;
                
                OUT.positionHCS = TransformObjectToHClip(expandedPosition);
                return OUT;
            }

            half4 fragOutline(Varyings IN) : SV_Target
            {
                // ただベタ塗りするだけ
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ==========================================
        // 2パス目: オブジェクトそのものの描画＋UV疑似エッジ合成
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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _ExpandMode;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 元のテクスチャの色
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                // アウトラインがOFF（0）の場合はそのまま返す
                if (_OutlineWidth <= 0.0001)
                {
                    return baseColor;
                }

                // --- UVの端を使って全ての辺（内側のエッジ）を検知する（Mac対応版） ---
                // テクスチャのUV座標の端（0 または 1 に近ければ辺とみなす）
                float edgeU = min(IN.uv.x, 1.0 - frac(IN.uv.x));
                float edgeV = min(IN.uv.y, 1.0 - frac(IN.uv.y));
                float edgeDist = min(edgeU, edgeV);

                // _OutlineWidth (通常0.02〜0.08) に基づいて、UV上での端からの閾値を決める
                float edgeThickness = _OutlineWidth * 0.5;

                // 端からの距離が edgeThickness よりも小さければ色を重ねる
                if (edgeDist < edgeThickness)
                {
                    return _OutlineColor;
                }

                return baseColor;
            }
            ENDHLSL
        }
    }
}
