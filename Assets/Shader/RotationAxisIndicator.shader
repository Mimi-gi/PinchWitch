Shader "PinchWitch/RotationAxisIndicator"
{
    // 掴み中の回転方向表示(GrabRotationAxisIndicator)用の半透明シェーダー。
    // 全体は薄い白の半透明で、視線に対して横を向いた面(= 見た目の輪郭付近)ほど
    // _RimColor の不透明な白へ近づける(フレネル)。細い管に使うと縁取りのように見える。
    //
    // 掴んだブロックを構成する立方体に近い部分は、ノイズで縁を崩しながら消し(Dissolve)、
    // 消える境目を _EdgeColor で光らせる。立方体の情報は GrabRotationAxisIndicator が
    // MaterialPropertyBlock で毎フレーム渡す(_IndicatorBox*)。
    // 距離・ノイズの値は「矢印の拡大率 1 のとき」の大きさで指定し、拡大率(_IndicatorScale)に合わせて伸縮する。
    // ライティングは受けない。
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 0.2)
        _RimColor("Rim Color", Color) = (1, 1, 1, 0.9)
        // 大きいほど縁の白が細くなる。
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5

        [Header(Dissolve)]
        // 立方体の表面からこの距離より近い部分を消す。
        _DissolveDistance("Dissolve Distance", Range(0, 1)) = 0.3
        // 消える境目の光る帯の幅。
        _EdgeWidth("Edge Width", Range(0.001, 0.5)) = 0.15
        // 1 より大きい値はブルームがある画面でにじんで光る(無ければ白として出る)。
        [HDR] _EdgeColor("Edge Color", Color) = (3, 3, 3, 1)
        // ノイズの細かさ(大きいほど細かい)。
        _NoiseScale("Noise Scale", Float) = 4
        // ノイズで境目を揺らす量。大きいほどギザギザに崩れる。
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.6
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // GrabTargetShape.MaxBoxes と合わせる。
            #define MAX_BOXES 16

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _RimColor;
                half _RimPower;
                float _DissolveDistance;
                float _EdgeWidth;
                half4 _EdgeColor;
                float _NoiseScale;
                float _NoiseStrength;
            CBUFFER_END

            // Renderer ごとの値(MaterialPropertyBlock 経由)。
            float _IndicatorBoxCount;
            float _IndicatorScale;
            float4x4 _IndicatorBoxWorldToLocal[MAX_BOXES];
            float4 _IndicatorBoxHalfExtents[MAX_BOXES];

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            // 箱の表面までの符号付き距離(内側は負)。p は箱の中心・向き基準の座標。
            float BoxSdf(float3 p, float3 halfExtents)
            {
                float3 q = abs(p) - halfExtents;
                return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
            }

            // 掴んだブロックを構成する立方体のうち、最も近いものまでの距離(ワールド単位)。
            float DistanceToBoxes(float3 positionWS)
            {
                float nearest = 1e5;
                int count = (int)_IndicatorBoxCount;
                for (int i = 0; i < MAX_BOXES; i++)
                {
                    if (i >= count)
                    {
                        break;
                    }

                    float3 p = mul(_IndicatorBoxWorldToLocal[i], float4(positionWS, 1.0)).xyz;
                    nearest = min(nearest, BoxSdf(p, _IndicatorBoxHalfExtents[i].xyz));
                }

                return nearest;
            }

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i);
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, u.x);
                float nx10 = lerp(n010, n110, u.x);
                float nx01 = lerp(n001, n101, u.x);
                float nx11 = lerp(n011, n111, u.x);
                return lerp(lerp(nx00, nx10, u.y), lerp(nx01, nx11, u.y), u.z);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                half rim = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower);
                half4 color = lerp(_BaseColor, _RimColor, rim);

                // 拡大率 1 の大きさに直してから判定し、ブロックの大きさに関係なく同じ見た目にする。
                float scale = max(_IndicatorScale, 1e-4);
                float3 noisePosition = IN.positionWS / scale * _NoiseScale;
                float noise = ValueNoise(noisePosition) * 0.65 + ValueNoise(noisePosition * 2.3 + 17.0) * 0.35;
                float boxDistance = DistanceToBoxes(IN.positionWS) / scale;
                float dissolve = boxDistance - _DissolveDistance + (noise - 0.5) * _NoiseStrength;

                clip(dissolve);

                // 境目の帯は輪郭がはっきり出るよう、ほぼ段差で光る色に切り替える。
                half edge = 1.0h - smoothstep(_EdgeWidth * 0.7, _EdgeWidth, dissolve);
                return lerp(color, _EdgeColor, edge);
            }
            ENDHLSL
        }
    }
}
