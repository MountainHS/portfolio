// 독장판(PoisonZone) 전용 셰이더. URP Unlit + 반투명 (아래 지형이 비쳐 보이는 독 웅덩이).
// 메시는 TerrainDiscMesh 가 만든 지름 1m 원판(Terrain 곡면을 따름)이고, PoisonZone.Begin 이 (지름, 1, 지름) 으로 넓힌다.
// 무늬는 스케일에 끌려가지 않도록 월드 기준, 테두리·들썩임은 오브젝트 XZ 반경 비율(0 = 중심, 1 = 가장자리) 기준으로 계산한다.
//   1. 끓는 독액 무늬: 월드 XZ 좌표로 노이즈를 계산 → 장판 반경이 달라도 무늬 크기가 같다.
//   2. 거품 반점   : 잘게 끓는 노이즈의 높은 곳만 밝고 진하게 → 표면에서 터지는 독 거품.
//   3. 꿀렁임      : 중심 쪽일수록 위아래로 조금 들썩인다. 가장자리는 땅에 붙어 있다.
//   4. 테두리 발광 : 원판 가장자리를 밝게 → 피해 범위 경계가 잘 보인다 (예전 돔 가장자리와 같은 곡선).
// 색 값이 1 을 넘으면(HDR) 카메라 Post Processing + Bloom 을 켰을 때 빛이 번진다.
Shader "HybridShooter/PoisonPuddle"
{
    Properties
    {
        [Header(Color)]
        _DeepColor("진한 독 색 (무늬 어두운 부분)", Color) = (0.04, 0.25, 0.02, 1)
        _ToxicColor("밝은 독 색 (무늬 밝은 부분)", Color) = (0.35, 0.9, 0.1, 1)
        _GlowIntensity("전체 밝기 (1 초과 = HDR 발광)", Range(0, 5)) = 1.2
        _BaseAlpha("기본 불투명도 (0 = 투명)", Range(0, 1)) = 0.55

        [Header(Boiling Pattern)]
        _NoiseScale("무늬 크기 (1m 당 반복, 클수록 잘게)", Range(0.1, 5)) = 0.8
        _BoilSpeed("무늬 끓는 속도", Range(0, 3)) = 0.4
        _BubbleScale("거품 크기 (1m 당 반복, 클수록 잘게)", Range(0.5, 10)) = 3
        _BubbleThreshold("거품 기준 (낮을수록 거품 많음)", Range(0, 1)) = 0.7
        _BubbleBrightness("거품 밝기", Range(0, 5)) = 2

        [Header(Wobble)]
        _WobbleStrength("윗면 들썩임 세기 (m)", Range(0, 0.2)) = 0.04
        _WobbleScale("들썩임 덩어리 크기 (1m 당 반복)", Range(0.1, 5)) = 1
        _WobbleSpeed("들썩임 속도", Range(0, 5)) = 1.5

        [Header(Edge)]
        [HDR] _EdgeColor("테두리 발광 색", Color) = (0.6, 1, 0.25, 1)
        _EdgePower("테두리 두께 (클수록 얇음)", Range(0.5, 8)) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PoisonPuddleForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher 호환: 머티리얼 프로퍼티는 이 버퍼 안에 둔다.
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ToxicColor;
                half _GlowIntensity;
                half _BaseAlpha;
                float _NoiseScale;
                float _BoilSpeed;
                float _BubbleScale;
                half _BubbleThreshold;
                half _BubbleBrightness;
                float _WobbleStrength;
                float _WobbleScale;
                float _WobbleSpeed;
                half4 _EdgeColor;
                half _EdgePower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 positionXZ : TEXCOORD1;  // 오브젝트 공간 XZ. 픽셀마다 반경 비율을 구해야 테두리가 매끈하다
            };

            // 3D 좌표 → 0~1 의사 난수. 같은 좌표는 항상 같은 값. (PoisonBlob.shader 와 같은 함수)
            float Hash3D(float3 position)
            {
                position = frac(position * 0.3183099 + 0.1);
                position *= 17.0;
                return frac(position.x * position.y * position.z * (position.x + position.y + position.z));
            }

            // 3D Value Noise. 입력: 좌표, 출력: 0~1 부드러운 값.
            // 아이디어: 정수 격자 8 꼭짓점의 난수를 부드럽게(smoothstep 곡선) 보간한다.
            float ValueNoise3D(float3 position)
            {
                float3 cell = floor(position);
                float3 local = frac(position);
                float3 blend = local * local * (3.0 - 2.0 * local);

                float bottomFront = lerp(Hash3D(cell + float3(0, 0, 0)), Hash3D(cell + float3(1, 0, 0)), blend.x);
                float bottomBack  = lerp(Hash3D(cell + float3(0, 0, 1)), Hash3D(cell + float3(1, 0, 1)), blend.x);
                float topFront    = lerp(Hash3D(cell + float3(0, 1, 0)), Hash3D(cell + float3(1, 1, 0)), blend.x);
                float topBack     = lerp(Hash3D(cell + float3(0, 1, 1)), Hash3D(cell + float3(1, 1, 1)), blend.x);

                float bottom = lerp(bottomFront, bottomBack, blend.z);
                float top = lerp(topFront, topBack, blend.z);
                return lerp(bottom, top, blend.y);
            }

            // 월드 XZ 위치 + 시간 → 0~1 노이즈. 시간 축을 세 번째 좌표로 써서 제자리에서 끓듯 바뀐다.
            float BoilingNoise(float2 positionXZ, float scale, float speed)
            {
                return ValueNoise3D(float3(positionXZ * scale, _Time.y * speed));
            }

            // 반경 비율: 0 = 중심, 1 = 가장자리 (오브젝트 XZ 거리 ÷ 원판 반지름 0.5)
            float RadiusRatio(float2 positionXZ)
            {
                return saturate(length(positionXZ) / 0.5);
            }

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // 중심 쪽만 들썩임: 중심이면 최대, 가장자리면 0.
                float topAmount = 1.0 - RadiusRatio(input.positionOS.xz);
                float wobble = BoilingNoise(positionWS.xz, _WobbleScale, _WobbleSpeed) * 2.0 - 1.0;
                positionWS.y += wobble * _WobbleStrength * topAmount;

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionXZ = input.positionOS.xz;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // 1) 끓는 독액 무늬: 큰 덩어리 + 작은 덩어리 2겹.
                float largePattern = BoilingNoise(input.positionWS.xz, _NoiseScale, _BoilSpeed);
                float smallPattern = BoilingNoise(input.positionWS.xz + 17.3, _NoiseScale * 2.3, _BoilSpeed * 1.6);
                float pattern = largePattern * 0.65 + smallPattern * 0.35;
                half3 color = lerp(_DeepColor.rgb, _ToxicColor.rgb, pattern);

                // 2) 거품 반점: 잘게 끓는 노이즈가 기준보다 높은 곳만 밝게, 덜 투명하게.
                float bubbleNoise = BoilingNoise(input.positionWS.xz, _BubbleScale, _BoilSpeed * 3.0);
                float bubble = smoothstep(_BubbleThreshold, _BubbleThreshold + 0.08, bubbleNoise);
                color += _ToxicColor.rgb * bubble * _BubbleBrightness;

                // 3) 테두리 발광: 가장자리에 가까울수록 1. 예전 돔(반구) 가장자리의 "1 - 법선 Y" 곡선을 반경 비율로 그대로 계산한다.
                float radiusRatio = RadiusRatio(input.positionXZ);
                float domeSideAmount = 1.0 - sqrt(1.0 - radiusRatio * radiusRatio);
                float edge = pow(domeSideAmount, _EdgePower);
                color += _EdgeColor.rgb * edge;

                half alpha = saturate(_BaseAlpha + edge + bubble * 0.5);
                return half4(color * _GlowIntensity, alpha);
            }
            ENDHLSL
        }
    }
}
