// Sprite shader with a solid-colour flash blend.
//
// Why this exists: SpriteRenderer.color MULTIPLIES the texture, so it can only darken or tint —
// setting it to white is a no-op, and you can never brighten toward white. A "white hit flash",
// the standard 2D impact cue, therefore needs a shader that LERPs the output toward a flat colour.
//
// _FlashAmount 0 = normal sprite, 1 = fully the flash colour (silhouette).
//
// _PaintMode 1 reproduces HeroEditor's "Gray Paint" shader (EyesPaint, EquipmentPaint) underneath the
// flash. Those materials colour only some pixels — the eyes paint the saturated iris and leave the
// grey and white ones (the whites, the outline) alone — and HitFeedback moves every renderer onto
// this shader at the first hit. Before paint mode, that plain multiply tinted the whole eye sprite, so
// a hero's eye whites turned the colour of their irises the moment combat began.
Shader "Sprites/Flash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        // Gray Paint, as HeroEditor's shader has it: which pixels take the renderer's colour.
        _PaintMode ("Gray Paint Mode", Float) = 0
        _SaturationBound ("Saturation Bound", Range(0, 1)) = 0.1
        _ColorMultiplier ("Color Multiplier", Range(1, 2)) = 1
        _Inverse ("Inverse", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha   // premultiplied, matching Unity's sprite default

        Pass
        {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            fixed4 _FlashColor;
            float  _FlashAmount;
            float  _PaintMode;
            float  _SaturationBound;
            float  _ColorMultiplier;
            float  _Inverse;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            sampler2D _MainTex;

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, IN.texcoord);
                fixed4 c = t * IN.color;

                if (_PaintMode > 0.5)
                {
                    // Gray Paint: a pixel is painted when its saturation is on the painted side of
                    // the bound (above it normally, below it when inverse); painted pixels become
                    // colour x grey x multiplier, the rest keep their own colour — tinted only by
                    // _Color, the lasting stain HitFeedback.SetTint lays on the whole body.
                    float hi = max(t.r, max(t.g, t.b));
                    float lo = min(t.r, min(t.g, t.b));
                    float saturation = hi > 0 ? (hi - lo) / hi : 0;
                    bool painted = _Inverse > 0.5 ? saturation > _SaturationBound : saturation < _SaturationBound;
                    float gray = 0.3 * t.r + 0.59 * t.g + 0.11 * t.b;
                    c.rgb = painted ? _ColorMultiplier * IN.color.rgb * gray : t.rgb * _Color.rgb;
                }

                // Blend RGB toward the flash colour; alpha (the silhouette) is untouched.
                // Lerp in STRAIGHT colour, not premultiplied: the premultiply below applies to the
                // blended result. Multiplying the flash target by c.a here as well squared the alpha
                // on flashed pixels, so the flash only ever reached full strength where alpha was
                // exactly 1 and faded out early everywhere else — soft edges and any part-transparent
                // artwork flashed visibly weaker than opaque ones on the same unit.
                c.rgb = lerp(c.rgb, _FlashColor.rgb, _FlashAmount);

                c.rgb *= c.a;   // premultiply for the blend mode above
                return c;
            }
        ENDCG
        }
    }
}
