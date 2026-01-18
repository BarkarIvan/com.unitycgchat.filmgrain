Shader "Hidden/UnityCgChat/FilmGrain"
{
    Properties
    {
        _BlitTexture ("Source", 2D) = "white" {}
        _StdLut ("Std LUT", 2D) = "gray" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "FilmGrain"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            TEXTURE2D(_StdLut);
            SAMPLER(sampler_StdLut);

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            half4 _NoiseParamsA; // xy: scale, zw: offset
            half4 _NoiseParamsB; // xy: scale, zw: offset
            half4 _NoiseTransformA; // xy: basisX, zw: basisY
            half4 _NoiseTransformB; // xy: basisX, zw: basisY
            half4 _GrainParams; // x: intensity, y: noise decode scale

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_Position;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);

                half luma = saturate(dot(color.rgb, half3(0.2126h, 0.7152h, 0.0722h)));
                half std = SAMPLE_TEXTURE2D(_StdLut, sampler_StdLut, half2(luma, 0.5h)).r;

                half2 basePos = input.positionCS.xy;

                half2 noiseUvA = basePos * _NoiseParamsA.xy;
                half2 basisAX = _NoiseTransformA.xy;
                half2 basisAY = _NoiseTransformA.zw;
                noiseUvA = half2(dot(noiseUvA, basisAX), dot(noiseUvA, basisAY)) + _NoiseParamsA.zw;

                half2 noiseUvB = basePos * _NoiseParamsB.xy;
                half2 basisBX = _NoiseTransformB.xy;
                half2 basisBY = _NoiseTransformB.zw;
                noiseUvB = half2(dot(noiseUvB, basisBX), dot(noiseUvB, basisBY)) + _NoiseParamsB.zw;

                half noiseA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUvA).r;
                half noiseB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUvB).r;

                half noise = ((noiseA - 0.5h) + (noiseB - 0.5h)) * (_GrainParams.y * 0.70710678h);

                half grain = noise * std * _GrainParams.x;
                color.rgb = saturate(color.rgb + grain);

                return color;
            }
            ENDHLSL
        }
    }
}
