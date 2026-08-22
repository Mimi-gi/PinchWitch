Shader "PinchWitch/Planar Projection Shadow"
{
    Properties
    {
        _ShadowColor("Shadow Color", Color) = (0.04, 0.025, 0.06, 0.32)
        _ProjectionPlane("Projection Plane", Vector) = (0, 1, 0, 0)
        _ProjectionDirection("Projection Direction", Vector) = (0, -1, 0, 0)
        _SurfaceOffset("Surface Offset", Float) = 0.003
        [IntRange] _StencilRef("Stencil Reference", Range(1, 255)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-20"
        }

        Pass
        {
            Name "PlanarProjectionShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            // 同じメッシュの三角形が重なるたびに影が濃くなるのを防ぐ。
            Stencil
            {
                Ref [_StencilRef]
                Comp NotEqual
                Pass Replace
                ReadMask 255
                WriteMask 255
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                float4 _ProjectionPlane;
                float4 _ProjectionDirection;
                float _SurfaceOffset;
                float _StencilRef;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 planeNormal = normalize(_ProjectionPlane.xyz);
                float3 projectionDirection = normalize(_ProjectionDirection.xyz);
                float denominator = dot(planeNormal, projectionDirection);

                // plane: dot(normal, position) + w = 0
                float distanceAlongDirection =
                    -(dot(planeNormal, positionWS) + _ProjectionPlane.w) /
                    (abs(denominator) > 1e-5 ? denominator : -1.0);

                float3 projectedPositionWS =
                    positionWS + projectionDirection * distanceAlongDirection;
                projectedPositionWS += planeNormal * _SurfaceOffset;
                output.positionHCS = TransformWorldToHClip(projectedPositionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _ShadowColor;
            }
            ENDHLSL
        }
    }
}
