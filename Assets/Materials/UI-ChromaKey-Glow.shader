Shader "Custom/UI-ChromaKey-Glow" {
    Properties {
        _MainTex ("Video Texture", 2D) = "white" {}
        _KeyColor ("Key Color", Color) = (0, 1, 0, 1)
        _Threshold ("Threshold", Range(0, 2)) = 0.4
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        _Despill ("Despill (remove key tint on edges)", Range(0, 1)) = 0.5
        _GlowCut ("Glow Cut (key-dominant edge removal)", Range(0, 1)) = 0.5

        // UI requirements
        _Stencil ("Stencil ID", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader {
        Tags {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float3 _KeyColor;
            float _Threshold;
            float _Smoothness;
            float _Despill;
            float _GlowCut;

            // RGB -> YCbCr (BT.601)
            float2 RGBtoCbCr(float3 rgb) {
                float cb = -0.168736 * rgb.r - 0.331264 * rgb.g + 0.5      * rgb.b + 0.5;
                float cr =  0.5      * rgb.r - 0.418688 * rgb.g - 0.081312 * rgb.b + 0.5;
                return float2(cb, cr);
            }

            // color-difference key: how much the key channel dominates
            float keyDominance(float3 rgb, float3 key) {
                float dominant;
                float other;
                if (key.g >= key.r && key.g >= key.b) {
                    dominant = rgb.g;
                    other = max(rgb.r, rgb.b);
                } else if (key.r >= key.g && key.r >= key.b) {
                    dominant = rgb.r;
                    other = max(rgb.g, rgb.b);
                } else {
                    dominant = rgb.b;
                    other = max(rgb.r, rgb.g);
                }
                return saturate(dominant - other);
            }

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            float4 frag (v2f i) : SV_Target {
                float4 col = tex2D(_MainTex, i.uv);

                // --- layer 1: chroma distance (YCbCr) ---
                float2 pixCbCr = RGBtoCbCr(col.rgb);
                float2 keyCbCr = RGBtoCbCr(_KeyColor.rgb);
                float chromaDist = length(pixCbCr - keyCbCr);

                float edge0 = _Threshold * (1.0 - _Smoothness);
                float chromaAlpha = smoothstep(edge0, _Threshold, chromaDist);

                // --- layer 2: color-difference (glow cut) ---
                float dom = keyDominance(col.rgb, _KeyColor.rgb);
                float glowAlpha = 1.0 - saturate(dom / max(0.01, 1.0 - _GlowCut));

                // combine: must pass both layers
                float alpha = min(chromaAlpha, glowAlpha);

                // --- despill: suppress key tint on semi-transparent edges ---
                float spillMask = (1.0 - alpha) * _Despill;
                float3 despilled = col.rgb;
                if (_KeyColor.g >= _KeyColor.r && _KeyColor.g >= _KeyColor.b)
                    despilled.g = lerp(col.g, min(col.g, (col.r + col.b) * 0.5), spillMask);
                else if (_KeyColor.r >= _KeyColor.g && _KeyColor.r >= _KeyColor.b)
                    despilled.r = lerp(col.r, min(col.r, (col.g + col.b) * 0.5), spillMask);
                else
                    despilled.b = lerp(col.b, min(col.b, (col.r + col.g) * 0.5), spillMask);

                return float4(despilled, alpha * col.a * i.color.a);
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
