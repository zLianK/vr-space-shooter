// Cartoon-style space skybox: dark gradient, soft nebula clouds and twinkle-free stars.
// Fully procedural (no textures), so it is cheap and works in VR / mobile.
Shader "Space/Skybox"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.02, 0.01, 0.08, 1)
        _HorizonColor ("Horizon Color", Color) = (0.08, 0.03, 0.18, 1)
        _BottomColor ("Bottom Color", Color) = (0.01, 0.01, 0.04, 1)
        _NebulaColorA ("Nebula Color A", Color) = (0.55, 0.15, 0.75, 1)
        _NebulaColorB ("Nebula Color B", Color) = (0.1, 0.4, 0.9, 1)
        _NebulaIntensity ("Nebula Intensity", Range(0, 2)) = 0.6
        _StarDensity ("Star Density", Range(0, 1)) = 0.35
        _StarBrightness ("Star Brightness", Range(0, 3)) = 1.5
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half4 _NebulaColorA;
                half4 _NebulaColorB;
                half _NebulaIntensity;
                half _StarDensity;
                half _StarBrightness;
            CBUFFER_END

            // the stereo macros let single-pass instanced VR draw it for both eyes
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float3 hash33(float3 p)
            {
                return float3(hash13(p), hash13(p + 17.1), hash13(p + 43.7));
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i);
                float n100 = hash13(i + float3(1, 0, 0));
                float n010 = hash13(i + float3(0, 1, 0));
                float n110 = hash13(i + float3(1, 1, 0));
                float n001 = hash13(i + float3(0, 0, 1));
                float n101 = hash13(i + float3(1, 0, 1));
                float n011 = hash13(i + float3(0, 1, 1));
                float n111 = hash13(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * valueNoise(p);
                    p *= 2.03;
                    a *= 0.5;
                }
                return v;
            }

            // One layer of stars: a jittered point per grid cell on the sphere.
            float3 starLayer(float3 dir, float scale, float radius, float density)
            {
                float3 p = dir * scale;
                float3 cell = floor(p);
                float3 local = frac(p);
                float3 h = hash33(cell);
                if (h.x > density) return 0;

                float3 center = 0.25 + 0.5 * hash33(cell + 7.7);
                float d = length(local - center);
                float size = radius * (0.5 + h.y);
                float s = smoothstep(size, 0.0, d);

                // slight color variation: warm, white, cool
                float3 tint = lerp(float3(1.0, 0.8, 0.6), float3(0.7, 0.85, 1.0), h.z);
                return s * tint;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 dir = normalize(i.dir);

                // vertical gradient
                float t = dir.y;
                half3 col = t >= 0 ? lerp(_HorizonColor.rgb, _TopColor.rgb, saturate(t * 1.5))
                                   : lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-t * 1.5));

                // nebula clouds
                float n1 = fbm(dir * 2.5 + 3.1);
                float n2 = fbm(dir * 3.5 - 8.4);
                float cloudA = smoothstep(0.45, 0.85, n1);
                float cloudB = smoothstep(0.5, 0.9, n2);
                col += _NebulaColorA.rgb * cloudA * _NebulaIntensity;
                col += _NebulaColorB.rgb * cloudB * _NebulaIntensity * 0.7;

                // stars (two layers: many small + few big)
                float3 stars = starLayer(dir, 70.0, 0.14, _StarDensity)
                             + starLayer(dir, 28.0, 0.12, _StarDensity * 0.6) * 1.5;
                col += stars * _StarBrightness;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
