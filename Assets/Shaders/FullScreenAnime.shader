// Full-screen anime post effect for URP (use with a Full Screen Pass Renderer Feature).
// Requirements on the feature: Color + Depth + Normal.
// Injection point: "After Rendering Post Processing".
// Look: clean 3-tone cel shading with hue-shifted shadows, hard highlight band, rim light,
// thin colored line art, vivid colors and a soft bloom.
Shader "Prototype19/FullScreenAnime"
{
    Properties
    {
        [Header(Cel Shading)]
        _ShadowThreshold("Shadow Threshold", Range(0,1)) = 0.32
        _HighlightThreshold("Highlight Threshold", Range(0,1)) = 0.78
        _EdgeSoftness("Tone Edge Softness", Range(0.001,0.2)) = 0.025
        _ShadowBrightness("Shadow Brightness", Range(0,1)) = 0.62
        _MidBrightness("Mid Brightness", Range(0,1.5)) = 0.95
        _HighlightBrightness("Highlight Brightness", Range(0.8,2)) = 1.2

        [Header(Color)]
        _Saturation("Saturation", Range(0,2.5)) = 1.3
        _ShadowTint("Shadow Tint", Color) = (0.55,0.5,0.9,1)
        _ShadowTintStrength("Shadow Tint Strength", Range(0,1)) = 0.45
        _HighlightTint("Highlight Tint", Color) = (1.0,0.96,0.88,1)

        [Header(Rim Light)]
        _RimStrength("Rim Strength", Range(0,1)) = 0.35
        _RimPower("Rim Power", Range(1,8)) = 3.5
        _RimThreshold("Rim Threshold", Range(0,1)) = 0.55

        [Header(Line Art)]
        _LineThickness("Line Thickness (px)", Range(0.5,3)) = 1.0
        _LineDarkness("Line Darkness", Range(0,1)) = 0.8
        _DepthThreshold("Depth Threshold", Range(0.0,2.0)) = 0.8
        _NormalThreshold("Normal Threshold", Range(0.0,2.0)) = 1.2
        _LineFadeDistance("Line Fade Distance", Float) = 160

        [Header(Bloom)]
        _BloomThreshold("Bloom Threshold", Range(0,1)) = 0.7
        _BloomStrength("Bloom Strength", Range(0,1.5)) = 0.4
        _BloomRadius("Bloom Radius (px)", Range(2,30)) = 12
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "FullScreenAnime"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _ShadowThreshold;
            float _HighlightThreshold;
            float _EdgeSoftness;
            float _ShadowBrightness;
            float _MidBrightness;
            float _HighlightBrightness;
            float _Saturation;
            float4 _ShadowTint;
            float _ShadowTintStrength;
            float4 _HighlightTint;
            float _RimStrength;
            float _RimPower;
            float _RimThreshold;
            float _LineThickness;
            float _LineDarkness;
            float _DepthThreshold;
            float _NormalThreshold;
            float _LineFadeDistance;
            float _BloomThreshold;
            float _BloomStrength;
            float _BloomRadius;

            static const float3 LUMA = float3(0.2126, 0.7152, 0.0722);

            float3 SampleColor(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 px = 1.0 / _ScreenParams.xy;

                float3 col = SampleColor(uv);
                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 0.0001;
                #else
                    bool isSky = rawDepth >= 0.9999;
                #endif

                // ---- 3-tone cel shading driven by the lit luminance ----
                float luma = max(dot(col, LUMA), 1e-4);
                float s = _EdgeSoftness;
                float mid = smoothstep(_ShadowThreshold - s, _ShadowThreshold + s, luma);
                float hi = smoothstep(_HighlightThreshold - s, _HighlightThreshold + s, luma);
                float tone = lerp(_ShadowBrightness, _MidBrightness, mid);
                tone = lerp(tone, _HighlightBrightness, hi);

                float3 shaded = col * min(tone / luma, 5.0);

                // Shadows lean toward a cool hue, highlights toward a warm one.
                float shadowAmt = (1.0 - mid) * _ShadowTintStrength;
                shaded = lerp(shaded, shaded * _ShadowTint.rgb * 1.4, shadowAmt);
                shaded = lerp(shaded, shaded * _HighlightTint.rgb, hi * 0.6);

                float g = dot(shaded, LUMA);
                shaded = lerp(g.xxx, shaded, _Saturation);

                // ---- Rim light from normals + view direction ----
                if (!isSky && _RimStrength > 0.0)
                {
                    float3 n = SampleSceneNormals(uv);
                    float3 posWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                    float3 v = normalize(GetCameraPositionWS() - posWS);
                    float fres = pow(1.0 - saturate(dot(n, v)), _RimPower);
                    float rim = smoothstep(_RimThreshold - s * 2.0, _RimThreshold + s * 2.0, fres);
                    // Only light rims on surfaces that aren't in shadow.
                    rim *= mid;
                    shaded += shaded * rim * _RimStrength + rim * _RimStrength * 0.15;
                }

                // ---- Bloom ----
                if (_BloomStrength > 0.0)
                {
                    float2 r = px * _BloomRadius;
                    float3 b = 0;
                    b += SampleColor(uv + float2( r.x,  0));
                    b += SampleColor(uv + float2(-r.x,  0));
                    b += SampleColor(uv + float2( 0,  r.y));
                    b += SampleColor(uv + float2( 0, -r.y));
                    b += SampleColor(uv + r * 0.7071);
                    b += SampleColor(uv - r * 0.7071);
                    b += SampleColor(uv + float2( r.x, -r.y) * 0.7071);
                    b += SampleColor(uv + float2(-r.x,  r.y) * 0.7071);
                    b *= 0.125;
                    float bl = dot(b, LUMA);
                    shaded += b * smoothstep(_BloomThreshold, 1.0, bl) * _BloomStrength;
                }

                // ---- Line art: Roberts cross on depth + normals, thin and colored ----
                float2 texel = _LineThickness * px;
                float2 uv0 = uv + texel * float2(-1, -1);
                float2 uv1 = uv + texel * float2( 1,  1);
                float2 uv2 = uv + texel * float2( 1, -1);
                float2 uv3 = uv + texel * float2(-1,  1);

                float d0 = EyeDepth(uv0), d1 = EyeDepth(uv1), d2 = EyeDepth(uv2), d3 = EyeDepth(uv3);
                float dc = EyeDepth(uv);
                float depthMag = sqrt(pow(d1 - d0, 2) + pow(d3 - d2, 2));
                float depthEdge = step(_DepthThreshold * dc * 0.1, depthMag);

                float3 dn1 = SampleSceneNormals(uv1) - SampleSceneNormals(uv0);
                float3 dn2 = SampleSceneNormals(uv3) - SampleSceneNormals(uv2);
                float normalEdge = step(_NormalThreshold, sqrt(dot(dn1, dn1) + dot(dn2, dn2)));

                float edge = saturate(max(depthEdge, normalEdge));
                float fade = isSky ? 0.0 : saturate(1.0 - dc / max(_LineFadeDistance, 0.01));

                // Line color: deep, desaturated version of the local color with a cool lean.
                float3 lineCol = col * lerp(float3(1, 1, 1), _ShadowTint.rgb, 0.6) * (1.0 - _LineDarkness);
                shaded = lerp(shaded, lineCol, edge * fade);

                return float4(saturate(shaded), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
