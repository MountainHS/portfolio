// 몬스터 약점 발광 구 전용 셰이더. MonsterWeakPoint 가 판정 구와 같은 크기의 구에 붙인다.
// 가운데는 거의 투명하고, 시선과 표면이 비스듬한 가장자리만 은은하게 빛난다 (Fresnel).
// 그래서 "꽉 찬 노랑 공"이 아니라 "빛나는 막"처럼 보이고, 뒤의 몸 무늬가 비친다.
//   _GlowColor  : 발광 색 (HDR. 1 초과면 Bloom 이 켜진 화면에서 번진다)
//   _CoreAlpha  : 가운데 불투명도 (0 = 완전 투명, 1 = 가장자리만큼 진함)
//   _RimPower   : 가장자리 빛 두께 (클수록 얇은 테두리)
//   _Brightness : 전체 밝기 0~1. MonsterWeakPoint 가 노출·비노출·맥동에 따라 매 프레임 바꾼다
// 몸통을 뚫고 보이지 않게 깊이 검사는 기본(LEqual)이다. 몸 밖으로 나온 부분만 보인다 = 맞힐 수 있는 부분.
// Resources 폴더에 있어 빌드에 포함되고, MonsterWeakPoint 가 Resources.Load 로 읽는다.
Shader "HybridShooter/Monster/WeakPointGlow"
{
    Properties
    {
        [HDR] _GlowColor("발광 색", Color) = (0.5, 1.4, 1.1, 1)
        _CoreAlpha("가운데 불투명도", Range(0, 1)) = 0.12
        _RimPower("가장자리 두께 (클수록 얇음)", Range(0.5, 8)) = 3
        _Brightness("전체 밝기", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WeakPointGlow"
            Tags { "LightMode" = "UniversalForward" }

            // 반투명 합성: 결과 = 발광 색 × 알파 + 뒤 색 × (1 - 알파)
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher 호환: 머티리얼 프로퍼티는 이 버퍼 안에 둔다.
            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _CoreAlpha;
                half _RimPower;
                half _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // 가장자리일수록 1, 정면일수록 0
                float rim = pow(1.0 - saturate(dot(normal, viewDirection)), _RimPower);

                // 가운데는 _CoreAlpha, 가장자리는 1 로 진해지고, 전체 밝기를 곱한다
                half alpha = lerp(_CoreAlpha, 1.0, rim) * _Brightness;
                return half4(_GlowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
