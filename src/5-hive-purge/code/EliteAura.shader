// 강화·자폭 변형종 전용 덧씌우기 셰이더. 원래 몸 재질 뒤에 "추가 재질"로 붙인다 (Renderer 의 Materials 2번째 칸).
// 원래 재질은 그대로 그려지고, 이 재질이 같은 메시를 한 번 더 살짝 부풀려 그려 몸을 덮는다.
// 효과 4가지를 겹친다. 각 효과의 세기는 머티리얼 Inspector 슬라이더로 조절한다.
//   1. 그을림   : 알파만큼 원래 몸을 어둡게 눌러 "검게 탄 몸"처럼 보인다.
//   2. 테두리 발광: 시선과 표면이 비스듬한 가장자리를 HDR 색으로 밝힌다 (Fresnel).
//   3. 핏줄 무늬 : 위로 흐르는 노이즈의 능선만 남겨 몸 위를 기어오르는 빛줄기를 만든다.
//   4. 심장 박동 : "쿵-쿵" 두 번 뛰는 박동에 맞춰 발광이 세지고 몸이 숨쉬듯 부푼다.
// 색 값이 1 을 넘으면(HDR) 카메라 Post Processing + Bloom 을 켰을 때 빛이 번진다.
Shader "HybridShooter/Monster/EliteAura"
{
    Properties
    {
        [Header(Darken)]
        _Darken("몸 어둡게 (0 = 원래 색, 1 = 검정)", Range(0, 1)) = 0.35

        [Header(Rim)]
        [HDR] _RimColor("테두리 발광 색", Color) = (4, 0.25, 0.15, 1)
        _RimPower("테두리 두께 (클수록 얇음)", Range(0.5, 8)) = 2.5

        [Header(Veins)]
        [HDR] _VeinColor("핏줄 발광 색", Color) = (2.5, 0.1, 0.6, 1)
        _VeinScale("핏줄 촘촘함 (m 당 개수)", Range(0.5, 20)) = 6
        _VeinWidth("핏줄 굵기", Range(0.01, 0.5)) = 0.08
        _VeinSpeed("핏줄 올라가는 속도 (m/s)", Range(0, 3)) = 0.4

        [Header(Pulse)]
        _PulseRate("박동 빠르기 (초당 박동 수)", Range(0.2, 6)) = 1.1
        _PulseStrength("박동 세기", Range(0, 2)) = 0.6

        [Header(Shell)]
        _ShellThickness("껍질 부풀림 (m)", Range(0, 0.05)) = 0.008
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "EliteAura"
            Tags { "LightMode" = "UniversalForward" }

            // 프리멀티플라이 블렌드: 결과 = 발광(RGB) + 원래 색 × (1 - 알파). 알파가 몸을 어둡게 누른다.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher 호환: 머티리얼 프로퍼티는 이 버퍼 안에 둔다.
            CBUFFER_START(UnityPerMaterial)
                half _Darken;
                half4 _RimColor;
                half _RimPower;
                half4 _VeinColor;
                float _VeinScale;
                half _VeinWidth;
                float _VeinSpeed;
                float _PulseRate;
                half _PulseStrength;
                float _ShellThickness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 localWS : TEXCOORD2; // 몬스터 중심 기준 위치 (m). 핏줄 무늬가 이동에 끌려가지 않게 한다
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // 3D 값 노이즈. 격자 꼭짓점 난수 8개를 부드럽게 섞는다. 결과 0~1
            float ValueNoise3(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(cell + float3(0, 0, 0));
                float n100 = Hash31(cell + float3(1, 0, 0));
                float n010 = Hash31(cell + float3(0, 1, 0));
                float n110 = Hash31(cell + float3(1, 1, 0));
                float n001 = Hash31(cell + float3(0, 0, 1));
                float n101 = Hash31(cell + float3(1, 0, 1));
                float n011 = Hash31(cell + float3(0, 1, 1));
                float n111 = Hash31(cell + float3(1, 1, 1));

                float x00 = lerp(n000, n100, u.x);
                float x10 = lerp(n010, n110, u.x);
                float x01 = lerp(n001, n101, u.x);
                float x11 = lerp(n011, n111, u.x);
                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
            }

            // 심장 박동: 한 주기 안에 "쿵"(큰 박동) 뒤 0.18 주기 늦게 "쿵"(작은 박동). 결과 0~1
            float Heartbeat(float time)
            {
                float t = frac(time * _PulseRate);
                float firstBeat = exp(-t * 14.0);
                float secondBeat = 0.6 * exp(-max(t - 0.18, 0.0) * 14.0) * step(0.18, t);
                return saturate(firstBeat + secondBeat);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // 껍질: 법선 방향으로 부풀린다. 박동 순간 조금 더 부푼다
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float swell = 1.0 + Heartbeat(_Time.y) * _PulseStrength;
                positionWS += normalWS * (_ShellThickness * swell);

                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.localWS = positionWS - originWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float pulse = Heartbeat(_Time.y);

                // 테두리 발광
                float fresnel = pow(1.0 - saturate(dot(n, v)), _RimPower);

                // 핏줄: 노이즈 0.5 등고선 근처만 남기면 가는 줄무늬가 된다. 시간에 따라 위로 흐른다
                float3 veinPoint = input.localWS * _VeinScale - float3(0, _Time.y * _VeinSpeed * _VeinScale, 0);
                float ridge = 1.0 - abs(ValueNoise3(veinPoint) * 2.0 - 1.0);
                float vein = smoothstep(1.0 - _VeinWidth, 1.0, ridge);

                // 합치기: 박동이 테두리와 핏줄을 함께 밝힌다
                float glow = 0.6 + pulse * _PulseStrength;
                half3 emission = _RimColor.rgb * fresnel * glow
                               + _VeinColor.rgb * vein * (0.4 + pulse * _PulseStrength);
                half alpha = _Darken * (1.0 - fresnel * 0.5);
                return half4(emission, alpha);
            }
            ENDHLSL
        }
    }
}
