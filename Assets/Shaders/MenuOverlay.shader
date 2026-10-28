// Menu panels and controller beams, drawn on top of everything (no depth test) so cockpit parts between the
// pilot and the menu never cover them. Supports single-pass instanced VR.
//
// Shape = 0: flat color (beams).
// Shape = 1: holographic panel on a quad: cut top-left / bottom-right corners, thin outline, thicker corner
//            brackets, inner edge glow and (optionally) scanlines with a slow sweep. Sizes are in meters,
//            so the lines keep the same thickness on panels of any size.
Shader "Game/Menu Overlay"
{
    Properties
    {
        _BaseColor ("Fill color", Color) = (1, 1, 1, 1)
        _EdgeColor ("Edge color", Color) = (1, 1, 1, 1)
        _Shape ("Shape (0 = flat, 1 = panel)", Float) = 0
        _Size ("Size in meters (xy)", Vector) = (1, 1, 0, 0)
        _Edge ("Outline width (m)", Float) = 0.0015
        _Corner ("Corner bracket length (m)", Float) = 0.03
        _Chamfer ("Cut corner size (m)", Float) = 0.02
        _Glow ("Inner edge glow width (m)", Float) = 0.02
        _Scanlines ("Scanlines and sweep strength", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Overlay" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "MenuOverlay"
            ZTest Always
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EdgeColor;
                float _Shape;
                float4 _Size;
                float _Edge;
                float _Corner;
                float _Chamfer;
                float _Glow;
                float _Scanlines;
            CBUFFER_END

            // unscaled time, set by GameFlow (the game is paused while a menu is open)
            float _MenuTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_Shape < 0.5) return _BaseColor;

                float2 size = _Size.xy;
                float2 p = input.uv * size;           // meters from the bottom-left corner
                float2 d = min(p, size - p);          // distance to the nearest vertical / horizontal edge

                // distance to the border, including the two cut corners (top-left and bottom-right)
                float topLeft = (p.x + (size.y - p.y) - _Chamfer) * 0.70710678;
                float bottomRight = ((size.x - p.x) + p.y - _Chamfer) * 0.70710678;
                float edge = min(min(d.x, d.y), min(topLeft, bottomRight));

                float aa = max(fwidth(edge), 1e-5);
                clip(edge + aa);
                float inside = saturate(edge / aa + 0.5);

                half4 c = _BaseColor;
                c.rgb *= lerp(0.8, 1.2, input.uv.y); // a little lighter at the top

                if (_Scanlines > 0)
                {
                    float lines = 0.5 + 0.5 * sin(p.y * 6.2831853 / 0.004); // one line every 4 mm
                    c.rgb *= 1 - _Scanlines * 0.35 * lines;

                    float sweepY = 1 - frac(_MenuTime * 0.22);              // a soft band moving down
                    float band = exp(-pow((input.uv.y - sweepY) * 9, 2));
                    c.rgb += _EdgeColor.rgb * band * _Scanlines * 0.12;
                }

                // inner glow along the border
                float glow = _Glow > 0 ? exp(-edge / _Glow) : 0;
                c.rgb = lerp(c.rgb, _EdgeColor.rgb, glow * 0.35);
                c.a = max(c.a, _EdgeColor.a * glow * 0.4);

                // thin outline, thicker brackets near the four corners
                float outline = 1 - smoothstep(_Edge - aa, _Edge + aa, edge);
                bool nearCorner = d.x < _Corner + _Chamfer && d.y < _Corner + _Chamfer;
                float bracketWidth = _Edge * 2.5;
                float bracket = nearCorner ? 1 - smoothstep(bracketWidth - aa, bracketWidth + aa, edge) : 0;

                float stroke = max(outline * 0.55, bracket);
                c.rgb = lerp(c.rgb, _EdgeColor.rgb, stroke);
                c.a = lerp(c.a, _EdgeColor.a, stroke);

                c.a *= inside;
                return c;
            }
            ENDHLSL
        }
    }
}
