Shader "Hidden/OutlineSDF/Composite"
{
    // Jump Flooding Algorithm によるSDF生成と、画面への合成を行うシェーダー。
    // Pass 0 (JFAStep) : シード座標とペイロード(色・太さ)を最近傍探索で伝播させる
    // Pass 1 (Composite): 最終SDFから距離を求め、グラディエントノイズで輪郭を溶かしながら合成する
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    TEXTURE2D(_SeedTex);
    SAMPLER(sampler_SeedTex);
    TEXTURE2D(_PayloadTex);
    SAMPLER(sampler_PayloadTex);

    float _JFAStepPx;
    float _EdgeSoftnessPx;
    float _NoiseScale;
    float _NoiseSpeed;
    float _NoiseScrollSpeed;
    float _NoiseWidthStrength;
    float _BaseDissolve;
    float _FogOpacity;
    float _EdgeFalloffPower;
    float _DepthOcclusionBias;
    float _InteriorOpacity;

    // 無効なシード座標の目印（クリア色と一致させる）
    #define INVALID_SEED_X -1e4

    float2 _GradHash2(float2 p)
    {
        p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
        return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
    }

    float GradNoise2D(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);

        float a = dot(_GradHash2(i + float2(0, 0)), f - float2(0, 0));
        float b = dot(_GradHash2(i + float2(1, 0)), f - float2(1, 0));
        float c = dot(_GradHash2(i + float2(0, 1)), f - float2(0, 1));
        float d = dot(_GradHash2(i + float2(1, 1)), f - float2(1, 1));

        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    }

    // シーン深度をアイ空間の線形距離で取得する。
    // LinearEyeDepth は透視投影専用なので、正射影の場合は near〜far の線形補間に切り替える。
    float SceneEyeDepth(float2 uv)
    {
        float raw = SampleSceneDepth(uv);
    #if UNITY_REVERSED_Z
        float d01 = 1.0 - raw;
    #else
        float d01 = raw;
    #endif
        float ortho = _ProjectionParams.y + d01 * (_ProjectionParams.z - _ProjectionParams.y);
        return lerp(LinearEyeDepth(raw, _ZBufferParams), ortho, unity_OrthoParams.w);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "JFAStep"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma target 3.5

            void frag(Varyings IN, out float4 seedOut : SV_Target0, out float4 payloadOut : SV_Target1)
            {
                float2 pixelPos = IN.texcoord * _ScreenParams.xy;
                float2 texelStep = _JFAStepPx / _ScreenParams.xy;

                float bestDistSq = 1e20;
                float2 bestSeed = float2(INVALID_SEED_X, INVALID_SEED_X);
                float4 bestPayload = 0;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 sampleUV = IN.texcoord + float2(x, y) * texelStep;
                        if (sampleUV.x < 0 || sampleUV.x > 1 || sampleUV.y < 0 || sampleUV.y > 1)
                            continue;

                        float2 s = SAMPLE_TEXTURE2D_LOD(_SeedTex, sampler_SeedTex, sampleUV, 0).xy;
                        if (s.x < INVALID_SEED_X * 0.5)
                            continue;

                        float distSq = dot(pixelPos - s, pixelPos - s);
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            bestSeed = s;
                            bestPayload = SAMPLE_TEXTURE2D_LOD(_PayloadTex, sampler_PayloadTex, sampleUV, 0);
                        }
                    }
                }

                seedOut = float4(bestSeed, 0, 1);
                payloadOut = bestPayload;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma target 3.5

            half4 frag(Varyings IN) : SV_Target
            {
                float2 pixelPos = IN.texcoord * _ScreenParams.xy;

                float2 seed = SAMPLE_TEXTURE2D(_SeedTex, sampler_SeedTex, IN.texcoord).xy;
                if (seed.x < INVALID_SEED_X * 0.5)
                    return half4(0, 0, 0, 0);

                half4 payload = SAMPLE_TEXTURE2D(_PayloadTex, sampler_PayloadTex, IN.texcoord);
                float ringWidthPx = payload.a;
                if (ringWidthPx <= 0.0)
                    return half4(0, 0, 0, 0);

                // (1) SDF: シルエット内部は 0、外側はシルエット縁までのピクセル距離
                float dist = distance(pixelPos, seed);

                // (2)(3) 距離場そのものにグラディエントノイズを加算する。
                // 太さに乗算すると輪郭全体が一様に膨らむだけ(板が呼吸して見える)なので、
                // 距離を局所的に押し引きして等値線そのものを歪ませる。
                //
                // 低周波はシード基準: 同じ縁を参照する画素が同じ変位を受けるため、
                //   炎の舌が輪郭に沿って伸び縮みする。
                // 高周波は画素基準: 縁がちりちりと崩れる。
                // ノイズ空間は縦解像度で正規化して正方セルにする(xyで割ると横長に歪む)。
                float2 seedNoiseUV = seed / _ScreenParams.y;
                float2 pixNoiseUV = pixelPos / _ScreenParams.y;
                float2 scroll = float2(0.0, _Time.y * _NoiseScrollSpeed);
                float phase = _Time.y * _NoiseSpeed * 0.5;
                float nLow = GradNoise2D(seedNoiseUV * _NoiseScale + scroll + phase);
                float nHigh = GradNoise2D(pixNoiseUV * _NoiseScale * 3.1 + scroll * 1.7 + phase * 1.3 + 17.0);
                float noise = nLow * 0.75 + nHigh * 0.25;

                // 変位量は帯の太さに比例させる(細いアウトラインが千切れないように)
                float distN = dist - noise * ringWidthPx * _NoiseWidthStrength;

                // (4) 閾値処理。内部(dist=0)も塗るので step によるマスクは掛けない
                float ring = 1.0 - smoothstep(max(ringWidthPx - _EdgeSoftnessPx, 0.0), ringWidthPx, distN);

                // 外縁ほど薄く。均一な不透明度の帯は輪郭から浮いた板に見えるため、
                // 根元(シルエット際)から滲み出すグラデーションにする。
                // pow(0, 0) は未定義(NaNの恐れ)のため、底を極小値でクランプする
                float edgeT = saturate(distN / max(ringWidthPx, 1e-4));
                ring *= pow(max(1.0 - edgeT, 1e-4), _EdgeFalloffPower);

                // 外縁だけを高周波ノイズで欠けさせる(根元は欠けさせない)
                float erode = saturate(nHigh * 0.5 + 0.5);
                ring *= lerp(1.0, erode, saturate(_BaseDissolve * edgeT));

                // (5) 元の3Dオブジェクトを手前にする。
                // グロウの深度 = 最近傍シルエット点のアイ深度(objEye)。
                // そこより手前にシーンが在る画素は隠す。オブジェクト自身の画素は
                // 深度が一致するため隠れ、常に3Dオブジェクトが手前に見える。
                // シルエットは専用深度バッファに描いているので、壁に隠れた部分も
                // 輪郭の形としては保たれたまま、ここで初めてシーン深度に負けて消える。
                // _InteriorOpacity を上げるとオブジェクト越しに内側の塗りが透ける。
                // 5-a) シーンがオブジェクトより「手前」にある分だけ隠す。
                // 背後の床や壁は sceneEye >= objEye なので occluded = 0 のまま残る。
                // (sceneEye - objEye > bias を表示条件にすると、オブジェクトに接した床の
                //  すぐ隣に乗る帯まで消えてアウトラインがほぼ全滅するので向きが逆)
                // オブジェクトの深度は、最近傍シルエット点(seed)の位置でシーン深度を引いて代用する。
                // seed 上には対象オブジェクト自身が写っているので、そこの深度がオブジェクトの深度になる。
                float sceneEye = SceneEyeDepth(IN.texcoord);
                float objEye = SceneEyeDepth(seed / _ScreenParams.xy);
                float occluded = smoothstep(0.0, max(_DepthOcclusionBias, 1e-4), objEye - sceneEye);
                ring *= 1.0 - occluded;

                // 5-b) シルエット内部はオブジェクト自身が手前になる。
                // ここは深度の一致で判定すると誤差に埋もれるため、距離場で判定する
                // (内部は dist = 0)。_InteriorOpacity を上げると内側の塗りが透ける。
                ring *= lerp(1.0, _InteriorOpacity, saturate(1.0 - dist));

                float alpha = ring * _FogOpacity;
                return half4(payload.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
