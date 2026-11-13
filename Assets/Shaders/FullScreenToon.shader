// Full-screen toon post effect for URP (use with a Full Screen Pass Renderer Feature).
// Requirements on the feature: Color + Depth + Normal.
// Injection point: "After Rendering Post Processing" (or "Before Rendering Post Processing").
Shader "Prototype19/FullScreenToon"
{
    Properties
    {
        [Header(Posterize)]
        [IntRange] _Bands("Light Bands", Range(2,12)) = 4
        _BandSmoothing("Band Edge Smoothing", Range(0.0,1.0)) = 0.15
        _Saturation("Saturation", Range(0,2)) = 1.1
        _ShadowFloor("Shadow Floor (min brightness)", Range(0,1)) = 0.45

        [Header(Outline)]
        _OutlineColor("Outline Color", Color) = (0.05,0.04,0.07,1)
        _OutlineThickness("Outline Thickness (px)", Range(0.5,4)) = 1.0
        _DepthThreshold("Depth Threshold", Range(0.0,2.0)) = 0.25
        _NormalThreshold("Normal Threshold", Range(0.0,2.0)) = 0.5
        _OutlineStrength("Outline Strength", Range(0,1)) = 1
        _OutlineFadeDistance("Outline Fade Distance", Float) = 150
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "FullScreenToon"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _Bands;
            float _BandSmoothing;
            float _Saturation;
            float _ShadowFloor;
            float4 _OutlineColor;
            float _OutlineThickness;
            float _DepthThreshold;
            float _NormalThreshold;
            float _OutlineStrength;
            float _OutlineFadeDistance;

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            // Soft staircase: floor() with a smoothed transition at each step.
            float SoftQuantize(float x, float steps, float smoothing)
            {
                float s = x * steps;
                float f = floor(s);
                float t = frac(s);
                float edge = max(smoothing, 0.0001);
                return (f + smoothstep(0.5 - edge * 0.5, 0.5 + edge * 0.5, t)) / steps;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // ---- Posterize luminance, keep hue ----
                float luma = max(dot(col, float3(0.2126, 0.7152, 0.0722)), 1e-4);
                float q = SoftQuantize(saturate(luma), _Bands, _BandSmoothing);
                // Lift the darkest band so shadows stay readable instead of crushing to black.
                q = lerp(_ShadowFloor, 1.0, q);
                // Dark areas are brightened toward the floor; clamp so near-black pixels don't explode.
                float3 toon = col * min(q / luma, 6.0);
                float gray = dot(toon, float3(0.2126, 0.7152, 0.0722));
                toon = lerp(gray.xxx, toon, _Saturation);

                // ---- Outline: Roberts cross on depth + normals ----
                float2 texel = _OutlineThickness / _ScreenParams.xy;
                float2 uv0 = uv + texel * float2(-1, -1);
                float2 uv1 = uv + texel * float2( 1,  1);
                float2 uv2 = uv + texel * float2( 1, -1);
                float2 uv3 = uv + texel * float2(-1,  1);

                float d0 = EyeDepth(uv0), d1 = EyeDepth(uv1), d2 = EyeDepth(uv2), d3 = EyeDepth(uv3);
                float dc = EyeDepth(uv);
                float depthEdge = sqrt(pow(d1 - d0, 2) + pow(d3 - d2, 2));
                // Scale by depth so distant geometry doesn't flicker into outlines.
                float depthThresh = _DepthThreshold * dc * 0.1;
                depthEdge = step(depthThresh, depthEdge);

                float3 n0 = SampleSceneNormals(uv0), n1 = SampleSceneNormals(uv1);
                float3 n2 = SampleSceneNormals(uv2), n3 = SampleSceneNormals(uv3);
                float3 dn1 = n1 - n0;
                float3 dn2 = n3 - n2;
                float normalEdge = sqrt(dot(dn1, dn1) + dot(dn2, dn2));
                normalEdge = step(_NormalThreshold, normalEdge);

                float edge = saturate(max(depthEdge, normalEdge));

                // No outlines on the sky (depth at far plane) and fade them out with distance.
                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 0.0001;
                #else
                    bool isSky = rawDepth >= 0.9999;
                #endif
                float fade = isSky ? 0.0 : saturate(1.0 - dc / max(_OutlineFadeDistance, 0.01));

                toon = lerp(toon, _OutlineColor.rgb, edge * fade * _OutlineStrength);
                return float4(toon, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
