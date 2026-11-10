// 독 포탄(PoisonProjectile) 전용 셰이더. URP Unlit 기반 (빛 계산 없이 스스로 빛나는 독액).
// 효과 4가지를 겹친다. 각 효과의 세기는 머티리얼 Inspector 슬라이더로 조절한다.
//   1. 꿀렁임   : 정점을 노이즈만큼 법선 방향으로 밀어 공 표면이 부글거린다 (Vertex 단계).
//   2. 독액 무늬 : 시간에 따라 흐르는 노이즈로 진한 초록 ↔ 밝은 연두를 섞는다.
//   3. 거품 반점 : 노이즈가 높은 곳만 더 밝게 → 표면에 떠오르는 독 거품처럼 보인다.
//   4. 테두리 발광: 시선과 표면이 비스듬한 가장자리를 밝힌다 (Fresnel).
// 색 값이 1 을 넘으면(HDR) 카메라 Post Processing + Bloom 을 켰을 때 빛이 번진다.
Shader "HybridShooter/PoisonBlob"
{
    Properties
    {
        [Header(Color)]
        _DeepColor("진한 독 색 (무늬 어두운 부분)", Color) = (0.05, 0.35, 0.02, 1)
        _ToxicColor("밝은 독 색 (무늬 밝은 부분)", Color) = (0.4, 1, 0.1, 1)
        _GlowIntensity("전체 밝기 (1 초과 = HDR 발광)", Range(0, 5)) = 1.5

        [Header(Flow Pattern)]
        _NoiseScale("무늬 크기 (클수록 잘게)", Range(0.5, 10)) = 3
        _FlowSpeed("무늬 흐르는 속도", Range(0, 3)) = 0.6
        _BubbleThreshold("거품 반점 기준 (낮을수록 반점 많음)", Range(0, 1)) = 0.65
        _BubbleBrightness("거품 반점 밝기", Range(0, 5)) = 1.5

        [Header(Wobble)]
        _WobbleStrength("꿀렁임 세기 (m, 오브젝트 기준)", Range(0, 0.2)) = 0.05
        _WobbleScale("꿀렁임 덩어리 크기 (클수록 잘게)", Range(0.5, 10)) = 2.5
        _WobbleSpeed("꿀렁임 속도", Range(0, 5)) = 2

        [Header(Rim)]
        [HDR] _RimColor("테두리 발광 색", Color) = (0.7, 1, 0.3, 1)
        _RimPower("테두리 두께 (클수록 얇음)", Range(0.5, 8)) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // SRP Batcher 호환: 머티리얼 프로퍼티는 이 버퍼 안에 둔다.
        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor;
            half4 _ToxicColor;
            half _GlowIntensity;
            float _NoiseScale;
            float _FlowSpeed;
            half _BubbleThreshold;
            half _BubbleBrightness;
            float _WobbleStrength;
            float _WobbleScale;
            float _WobbleSpeed;
            half4 _RimColor;
            half _RimPower;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionOS : TEXCOORD0;  // 무늬 계산용. 오브젝트 기준이라 공이 날아가도 무늬가 공에 붙어 있다.
            float3 positionWS : TEXCOORD1;
            float3 normalWS : TEXCOORD2;
        };

        // 3D 좌표 → 0~1 의사 난수. 같은 좌표는 항상 같은 값.
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

        // 꿀렁임: 정점을 법선 방향으로 -세기 ~ +세기 만큼 밀어낸다. 색·깊이 패스가 같은 모양을 쓰도록 공용.
        float3 WobblePositionOS(float3 positionOS, float3 normalOS)
        {
            float3 samplePoint = positionOS * _WobbleScale + _Time.y * _WobbleSpeed;
            float wobble = ValueNoise3D(samplePoint) * 2.0 - 1.0;
            return positionOS + normalOS * wobble * _WobbleStrength;
        }

        Varyings Vertex(Attributes input)
        {
            Varyings output;
            float3 wobbledPositionOS = WobblePositionOS(input.positionOS.xyz, input.normalOS);

            output.positionOS = input.positionOS.xyz;
            output.positionWS = TransformObjectToWorld(wobbledPositionOS);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "PoisonForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            half4 Fragment(Varyings input) : SV_Target
            {
                // 1) 독액 무늬: 크고 작은 노이즈 2겹을 서로 다른 방향으로 흘려 섞는다.
                float3 flowOffset = float3(0, _Time.y * _FlowSpeed, _Time.y * _FlowSpeed * 0.5);
                float largePattern = ValueNoise3D(input.positionOS * _NoiseScale + flowOffset);
                float smallPattern = ValueNoise3D(input.positionOS * _NoiseScale * 2.3 - flowOffset * 1.7);
                float pattern = largePattern * 0.65 + smallPattern * 0.35;
                half3 color = lerp(_DeepColor.rgb, _ToxicColor.rgb, pattern);

                // 2) 거품 반점: 무늬가 기준보다 높은 곳만 밝은 독 색을 더한다.
                float bubble = smoothstep(_BubbleThreshold, _BubbleThreshold + 0.12, pattern);
                color += _ToxicColor.rgb * bubble * _BubbleBrightness;

                // 3) 테두리 발광: 시선과 표면이 수직에 가까울수록(가장자리) 1 에 가깝다.
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirectionWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float rim = pow(1.0 - saturate(dot(normalWS, viewDirectionWS)), _RimPower);
                color += _RimColor.rgb * rim;

                return half4(color * _GlowIntensity, 1);
            }
            ENDHLSL
        }

        // 깊이 텍스처에도 꿀렁인 모양 그대로 기록한다 (Depth Texture 를 쓰는 효과용).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment DepthFragment

            half DepthFragment(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
