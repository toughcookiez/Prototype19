// Full-screen "digital illustration" post effect for URP (use with a Full Screen Pass Renderer Feature).
// Requirements on the feature: Color + Depth + Normal.
// Injection point: "After Rendering Post Processing".
// Look: soft painted shading, tinted shadows / warm highlights, vivid color, colored line art, gentle glow.
Shader "Prototype19/FullScreenDigital"
{
    Properties
    {
        [Header(Softening)]
        _SoftRadius("Paint Softness (px)", Range(0,4)) = 1.5

        [Header(Shading)]
        _Steps("Shading Steps", Range(2,10)) = 5
        _StepBlend("Step Blend (0 = hard, 1 = smooth)", Range(0,1)) = 0.65
        _ShadingAmount("Shading Amount", Range(0,1)) = 0.7
        _ShadowFloor("Shadow Floor", Range(0,1)) = 0.3

        [Header(Color)]
        _Saturation("Saturation", Range(0,2.5)) = 1.35
        _Contrast("Contrast", Range(0.5,2)) = 1.1
        _ShadowTint("Shadow Tint", Color) = (0.42,0.4,0.75,1)
        _HighlightTint("Highlight Tint", Color) = (1.0,0.93,0.8,1)
        _TintStrength("Tint Strength", Range(0,1)) = 0.5

        [Header(Line Art)]
        _LineThickness("Line Thickness (px)", Range(0.5,4)) = 1.2
        _LineDarkness("Line Darkness", Range(0,1)) = 0.65
        _LineSoftness("Line Softness", Range(0.05,1)) = 0.35
        _DepthThreshold("Depth Threshold", Range(0.0,2.0)) = 0.8
        _NormalThreshold("Normal Threshold", Range(0.0,2.0)) = 1.1
        _LineFadeDistance("Line Fade Distance", Float) = 150

        [Header(Glow)]
        _GlowThreshold("Glow Threshold", Range(0,1)) = 0.65
        _GlowStrength("Glow Strength", Range(0,2)) = 0.5
        _GlowRadius("Glow Radius (px)", Range(2,24)) = 10
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "FullScreenDigital"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _SoftRadius;
            float _Steps;
            float _StepBlend;
            float _ShadingAmount;
            float _ShadowFloor;
            float _Saturation;
            float _Contrast;
            float4 _ShadowTint;
            float4 _HighlightTint;
            float _TintStrength;
            float _LineThickness;
            float _LineDarkness;
            float _LineSoftness;
            float _DepthThreshold;
            float _NormalThreshold;
            float _LineFadeDistance;
            float _GlowThreshold;
            float _GlowStrength;
            float _GlowRadius;

            static const float3 LUMA = float3(0.2126, 0.7152, 0.0722);

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            float3 SampleColor(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            // Staircase with an adjustable blend between steps (smooth = painted gradient, hard = cel).
            float SoftSteps(float x, float steps, float blend)
            {
                float s = x * steps;
                float f = floor(s);
                float t = frac(s);
                float w = max(blend * 0.5, 0.0001);
                return (f + smoothstep(0.5 - w, 0.5 + w, t)) / steps;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 px = 1.0 / _ScreenParams.xy;

                // ---- Paint softness: small weighted blur removes texture noise ----
                float2 o = px * _SoftRadius;
                float3 col = SampleColor(uv) * 4.0;
                col += SampleColor(uv + float2( o.x, 0)) * 2.0;
                col += SampleColor(uv + float2(-o.x, 0)) * 2.0;
                col += SampleColor(uv + float2(0,  o.y)) * 2.0;
                col += SampleColor(uv + float2(0, -o.y)) * 2.0;
                col += SampleColor(uv + float2( o.x,  o.y));
                col += SampleColor(uv + float2(-o.x,  o.y));
                col += SampleColor(uv + float2( o.x, -o.y));
                col += SampleColor(uv + float2(-o.x, -o.y));
                col /= 16.0;

                // ---- Painted shading: soft steps on luminance, hue preserved ----
                float luma = max(dot(col, LUMA), 1e-4);
                float stepped = SoftSteps(saturate(luma), _Steps, _StepBlend);
                stepped = lerp(_ShadowFloor, 1.0, stepped);
                float target = lerp(luma, stepped, _ShadingAmount);
                col *= min(target / luma, 4.0);

                // ---- Color grade: cool tinted shadows, warm highlights, vivid saturation ----
                float l = saturate(dot(col, LUMA));
                float3 tint = lerp(_ShadowTint.rgb, _HighlightTint.rgb, smoothstep(0.2, 0.8, l));
                col = lerp(col, col * tint * 1.25, _TintStrength);
                float g = dot(col, LUMA);
                col = lerp(g.xxx, col, _Saturation);
                col = saturate((col - 0.5) * _Contrast + 0.5);

                // ---- Glow: soft bloom from bright areas ----
                if (_GlowStrength > 0.0)
                {
                    float2 r = px * _GlowRadius;
                    float3 glow = 0;
                    glow += SampleColor(uv + float2( r.x,  0));
                    glow += SampleColor(uv + float2(-r.x,  0));
                    glow += SampleColor(uv + float2( 0,  r.y));
                    glow += SampleColor(uv + float2( 0, -r.y));
                    glow += SampleColor(uv + r * 0.7071);
                    glow += SampleColor(uv - r * 0.7071);
                    glow += SampleColor(uv + float2( r.x, -r.y) * 0.7071);
                    glow += SampleColor(uv + float2(-r.x,  r.y) * 0.7071);
                    glow *= 0.125;
                    float gl = dot(glow, LUMA);
                    col += glow * smoothstep(_GlowThreshold, 1.0, gl) * _GlowStrength;
                }

                // ---- Line art: Roberts cross on depth + normals, drawn as a darker shade of the local color ----
                float2 texel = _LineThickness * px;
                float2 uv0 = uv + texel * float2(-1, -1);
                float2 uv1 = uv + texel * float2( 1,  1);
                float2 uv2 = uv + texel * float2( 1, -1);
                float2 uv3 = uv + texel * float2(-1,  1);

                float d0 = EyeDepth(uv0), d1 = EyeDepth(uv1), d2 = EyeDepth(uv2), d3 = EyeDepth(uv3);
                float dc = EyeDepth(uv);
                float depthMag = sqrt(pow(d1 - d0, 2) + pow(d3 - d2, 2));
                float depthT = _DepthThreshold * dc * 0.1;
                float depthEdge = smoothstep(depthT, depthT * (1.0 + _LineSoftness * 2.0) + 1e-4, depthMag);

                float3 dn1 = SampleSceneNormals(uv1) - SampleSceneNormals(uv0);
                float3 dn2 = SampleSceneNormals(uv3) - SampleSceneNormals(uv2);
                float normalMag = sqrt(dot(dn1, dn1) + dot(dn2, dn2));
                float normalEdge = smoothstep(_NormalThreshold, _NormalThreshold * (1.0 + _LineSoftness * 2.0) + 1e-4, normalMag);

                float edge = saturate(max(depthEdge, normalEdge));

                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 0.0001;
                #else
                    bool isSky = rawDepth >= 0.9999;
                #endif
                float fade = isSky ? 0.0 : saturate(1.0 - dc / max(_LineFadeDistance, 0.01));

                // Line color: the local color, darkened and pushed toward the shadow tint.
                float3 lineCol = col * lerp(float3(1, 1, 1), _ShadowTint.rgb, 0.5) * (1.0 - _LineDarkness);
                col = lerp(col, lineCol, edge * fade);

                return float4(saturate(col), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
