Shader "Hidden/OutlineSDF/Composite"
{
    // Jump Flooding Algorithm によるSDF生成と、画面への合成を行うシェーダー。
    // Pass 0 (JFAStep) : シード座標とペイロード(色・太さ)を最近傍探索で伝播させる
    // Pass 1 (Composite): 最終SDFから距離を求め、グラディエントノイズで輪郭を溶かしながら合成する
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    TEXTURE2D(_SeedTex);
    SAMPLER(sampler_SeedTex);
    TEXTURE2D(_PayloadTex);
    SAMPLER(sampler_PayloadTex);

    float _JFAStepPx;
    float _EdgeSoftnessPx;
    float _NoiseScale;
    float _NoiseSpeed;
    float _NoiseWidthStrength;
    float _BaseDissolve;
    float _FogOpacity;

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

            void frag(Varyings IN, out half4 seedOut : SV_Target0, out half4 payloadOut : SV_Target1)
            {
                float2 pixelPos = IN.texcoord * _ScreenParams.xy;
                float2 texelStep = _JFAStepPx / _ScreenParams.xy;

                float bestDistSq = 1e20;
                float2 bestSeed = float2(INVALID_SEED_X, INVALID_SEED_X);
                half4 bestPayload = 0;

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

                seedOut = half4(bestSeed, 0, 1);
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

                float dist = distance(pixelPos, seed);

                // シード(=シルエット縁の位置)を基準にノイズを取ることで、輪郭に沿って太さが
                // なめらかに波打つようにする(現在のピクセル座標基準だと放射状にしか揺れない)。
                float2 seedUV = seed / _ScreenParams.xy;
                float widthNoise = GradNoise2D(seedUV * _NoiseScale + _Time.y * _NoiseSpeed * 0.5);
                float wobbledWidthPx = max(ringWidthPx * (1.0 + widthNoise * _NoiseWidthStrength), 0.0);

                float ring = 1.0 - smoothstep(max(wobbledWidthPx - _EdgeSoftnessPx, 0.0), wobbledWidthPx, dist);
                ring *= step(0.001, dist); // オブジェクト内部(距離0)は塗らない

                // ノイズで輪郭を「もやもや」と溶かす。
                // 内側(dist=0付近)は残りやすく、外側(輪の外縁)ほど閾値が上がって散りやすくする。
                float n = GradNoise2D(IN.texcoord * _NoiseScale + _Time.y * _NoiseSpeed) * 0.5 + 0.5;
                float edgeT = saturate(dist / max(wobbledWidthPx, 0.0001));
                float dissolveThreshold = lerp(0.0, _BaseDissolve, edgeT);
                float dissolve = smoothstep(dissolveThreshold - 0.15, dissolveThreshold + 0.15, n);
                ring *= dissolve;

                float alpha = ring * _FogOpacity;
                return half4(payload.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
