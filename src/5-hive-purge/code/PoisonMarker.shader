// 독 포탄 착탄 예고 표식(PoisonFogTargetMarker) 전용 셰이더. URP Unlit + 반투명.
// PoisonAreaAttack.OnWindup 이 TerrainDiscMesh 로 지름 1m 원판을 Terrain 곡면에 맞추고 (지름, 1, 지름) 으로 넓혀 착탄 범위를 보여 준다.
//   1. 테두리 링   : 원판 가장자리를 진하게 → 착탄 범위 경계.
//   2. 조여드는 링 : 가장자리에서 중심으로 반복해서 좁혀 오는 링 → "곧 떨어진다" 경고.
//   3. 깜빡임      : 전체 밝기가 주기적으로 오르내린다.
//   4. 안쪽 채움   : 아주 옅게만 칠해 아래(지형·기존 독장판)가 비쳐 보인다.
// 독장판(PoisonPuddle)과 겹쳐도 둘 다 보이게 하는 장치:
//   - 색 대비: 장판은 초록, 표식은 붉은 주황.
//   - 그리는 순서 고정: Queue = Transparent+10 → 항상 장판보다 나중에 그려 위에 덮인다 (거리 정렬로 깜빡이지 않음).
//   - 섞는 방식: Premultiplied(Blend One OneMinusSrcAlpha) + 빛 더하기. 안쪽은 거의 더하기만 해서 장판을 가리지 않고,
//     테두리·링만 덮어 그린다. 빨강·초록이 섞여 탁한 갈색이 되는 것을 줄인다.
// 링 위치는 오브젝트 공간 반경 비율(0 = 중심, 1 = 가장자리)로 계산 → 표식 크기가 달라도 링 개수가 같다.
Shader "HybridShooter/PoisonMarker"
{
    Properties
    {
        [Header(Color)]
        [HDR] _WarningColor("경고 색", Color) = (1.2, 0.25, 0.1, 1)
        _FillAlpha("안쪽 채움 불투명도 (0 = 투명)", Range(0, 1)) = 0.12
        _GlowIntensity("전체 밝기 (1 초과 = HDR 발광)", Range(0, 5)) = 1.5

        [Header(Edge Ring)]
        _EdgeWidth("테두리 링 두께 (반경 비율)", Range(0.01, 0.5)) = 0.08
        _EdgeAlpha("테두리 링 불투명도", Range(0, 1)) = 0.85

        [Header(Closing Rings)]
        _RingCount("조여드는 링 개수 (반경 안)", Range(0, 6)) = 2
        _RingWidth("조여드는 링 두께 (0~1)", Range(0.02, 0.5)) = 0.12
        _RingSpeed("조여드는 속도 (초당 링 이동, 반경 비율)", Range(0, 3)) = 0.8
        _RingAlpha("조여드는 링 불투명도", Range(0, 1)) = 0.5

        [Header(Pulse)]
        _PulseSpeed("깜빡임 속도 (초당 횟수)", Range(0, 5)) = 1.5
        _PulseAmount("깜빡임 세기 (0 = 깜빡이지 않음)", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PoisonMarkerForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher 호환: 머티리얼 프로퍼티는 이 버퍼 안에 둔다.
            CBUFFER_START(UnityPerMaterial)
                half4 _WarningColor;
                half _FillAlpha;
                half _GlowIntensity;
                half _EdgeWidth;
                half _EdgeAlpha;
                half _RingCount;
                half _RingWidth;
                half _RingSpeed;
                half _RingAlpha;
                half _PulseSpeed;
                half _PulseAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 positionXZ : TEXCOORD0;  // 오브젝트 공간 XZ. 픽셀마다 거리를 구해야 링이 매끈하다
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionXZ = input.positionOS.xz;
                return output;
            }

            // 가장자리에서 중심 쪽으로 좁혀 오는 반복 링. 입력: 반경 비율, 출력: 링 위면 1, 아니면 0 (부드러운 경계).
            float ClosingRings(float radiusRatio)
            {
                float ringPhase = frac(radiusRatio * _RingCount + _Time.y * _RingSpeed);
                float distanceToRingCenter = abs(ringPhase - 0.5);
                return 1.0 - smoothstep(_RingWidth * 0.5 - 0.02, _RingWidth * 0.5, distanceToRingCenter);
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // 반경 비율: 0 = 중심, 1 = 가장자리 (오브젝트 XZ 거리 ÷ 원판 반지름 0.5)
                float radiusRatio = saturate(length(input.positionXZ) / 0.5);

                // 1) 테두리 링: 가장자리에서 _EdgeWidth 안쪽까지.
                float edge = smoothstep(1.0 - _EdgeWidth, 1.0 - _EdgeWidth * 0.5, radiusRatio);

                // 2) 조여드는 링: 중심 가까이에서는 흐리게 사라진다.
                float rings = ClosingRings(radiusRatio) * smoothstep(0.05, 0.3, radiusRatio);

                // 3) 깜빡임: 0.5 + 0.5·sin → 1 - 세기 ~ 1 사이를 오간다.
                float pulse = 1.0 - _PulseAmount * (0.5 + 0.5 * sin(_Time.y * _PulseSpeed * 6.2831853));

                // 4) 불투명도: 가장 진한 요소를 따른다. 안쪽 채움은 아주 옅다.
                float alpha = max(_FillAlpha, max(edge * _EdgeAlpha, rings * _RingAlpha)) * pulse;

                // Premultiplied: 색에 alpha 를 곱해 덮고, 빛나는 만큼은 더한다 (알파와 무관하게 장판 위에서도 보인다).
                half3 color = _WarningColor.rgb * _GlowIntensity;
                half3 coverColor = color * alpha;
                half3 glowAdd = color * (edge + rings * 0.5) * 0.25 * pulse;
                return half4(coverColor + glowAdd, alpha);
            }
            ENDHLSL
        }
    }
}
