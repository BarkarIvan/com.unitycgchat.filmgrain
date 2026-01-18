using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace UnityCgChat.FilmGrain
{
    public sealed class FilmGrainRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class FilmGrainSettings
        {
            [Tooltip("When to inject the film grain pass in the URP pipeline.")]
            public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

            [Tooltip("Optional LUT/noise asset. If null, defaults are loaded from Resources.")]
            public FilmGrainLutAsset lutAsset;

            [Tooltip("Linear multiplier for grain standard deviation.")]
            [Range(0.0f, 1.0f)]
            public float intensity = 1.0f;

            [Tooltip("Grain size in screen pixels (higher = larger grains).")]
            [Min(0.01f)]
            public float grainScale = 1.0f;

            [Tooltip("Pattern changes per second (0 disables temporal variation).")]
            [Min(0.0f)]
            public float noiseSpeed = 60.0f;
        }

        private static readonly int ShaderPropertyBlitTexture = Shader.PropertyToID("_BlitTexture");
        private static readonly int ShaderPropertyBlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int ShaderPropertyStdLut = Shader.PropertyToID("_StdLut");
        private static readonly int ShaderPropertyNoiseTex = Shader.PropertyToID("_NoiseTex");
        private static readonly int ShaderPropertyNoiseParamsA = Shader.PropertyToID("_NoiseParamsA");
        private static readonly int ShaderPropertyNoiseParamsB = Shader.PropertyToID("_NoiseParamsB");
        private static readonly int ShaderPropertyNoiseTransformA = Shader.PropertyToID("_NoiseTransformA");
        private static readonly int ShaderPropertyNoiseTransformB = Shader.PropertyToID("_NoiseTransformB");
        private static readonly int ShaderPropertyGrainParams = Shader.PropertyToID("_GrainParams");

        public FilmGrainSettings settings = new FilmGrainSettings();

        private FilmGrainRenderPass m_Pass;
        private Material m_Material;

        private Texture2D m_StdLut;
        private Texture2D m_NoiseTex;

        private TextAsset m_StdBytes;
        private TextAsset m_NoiseBytes;

        private int m_LutSize;
        private int m_NoiseSize;

        private bool m_MissingResourcesLogged;

        public override void Create()
        {
            if (m_Pass == null)
                m_Pass = new FilmGrainRenderPass(name);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
            CoreUtils.Destroy(m_StdLut);
            CoreUtils.Destroy(m_NoiseTex);

            m_Material = null;
            m_StdLut = null;
            m_NoiseTex = null;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null || settings.intensity <= 0.0f)
                return;

            if (renderingData.cameraData.cameraType == CameraType.Preview
                || renderingData.cameraData.cameraType == CameraType.SceneView
                || renderingData.cameraData.cameraType == CameraType.Reflection
                || renderingData.cameraData.isSceneViewCamera
                || UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
                return;

            EnsureMaterial();
            if (m_Material == null)
                return;

            if (!EnsureTextures(out var filterSigma, out var noiseSigma, out var noiseDecodeScale, out var noiseSize))
                return;

            m_Pass.renderPassEvent = settings.injectionPoint;
            m_Pass.requiresIntermediateTexture = true;
            m_Pass.Setup(m_Material, m_StdLut, m_NoiseTex, filterSigma, noiseSigma, noiseDecodeScale, noiseSize, settings);

            renderer.EnqueuePass(m_Pass);
        }

        private void EnsureMaterial()
        {
            if (m_Material != null)
                return;

            Shader shader = Resources.Load<Shader>(FilmGrainResources.DefaultShaderPath);
            if (shader == null)
                shader = Shader.Find("Hidden/UnityCgChat/FilmGrain");
            if (shader == null)
            {
                Debug.LogWarning("FilmGrain: shader not found.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        private bool EnsureTextures(out float filterSigma, out float noiseSigma, out float noiseDecodeScale, out int noiseSize)
        {
            FilmGrainLutAsset lutAsset = settings.lutAsset;

            TextAsset stdBytes = lutAsset != null ? lutAsset.stdLutBytes : null;
            TextAsset noiseBytes = lutAsset != null ? lutAsset.noiseBytes : null;

            int lutSize = lutAsset != null && lutAsset.lutSize > 0 ? lutAsset.lutSize : FilmGrainResources.DefaultLutSize;
            noiseSize = lutAsset != null && lutAsset.noiseSize > 0 ? lutAsset.noiseSize : FilmGrainResources.DefaultNoiseSize;

            filterSigma = lutAsset != null && lutAsset.filterSigma > 0.0f ? lutAsset.filterSigma : FilmGrainResources.DefaultFilterSigma;
            noiseSigma = lutAsset != null && lutAsset.noiseSigma > 0.0f ? lutAsset.noiseSigma : FilmGrainResources.DefaultNoiseSigma;
            noiseDecodeScale = lutAsset != null && lutAsset.noiseDecodeScale > 0.0f ? lutAsset.noiseDecodeScale : FilmGrainResources.DefaultNoiseDecodeScale;

            if (stdBytes == null)
                stdBytes = Resources.Load<TextAsset>(FilmGrainResources.DefaultStdLutPath);
            if (noiseBytes == null)
                noiseBytes = Resources.Load<TextAsset>(FilmGrainResources.DefaultNoisePath);

            if (stdBytes == null || noiseBytes == null)
            {
                if (!m_MissingResourcesLogged)
                {
                    Debug.LogWarning("FilmGrain: missing std LUT or noise data. Assign a FilmGrainLutAsset or keep default resources in the package.");
                    m_MissingResourcesLogged = true;
                }
                return false;
            }

            if (m_StdLut == null || m_StdBytes != stdBytes || m_LutSize != lutSize)
            {
                CoreUtils.Destroy(m_StdLut);
                m_StdLut = CreateR16Texture(stdBytes, lutSize, 1, TextureWrapMode.Clamp, "FilmGrainStdLut");
                m_StdBytes = stdBytes;
                m_LutSize = lutSize;
            }

            if (m_NoiseTex == null || m_NoiseBytes != noiseBytes || m_NoiseSize != noiseSize)
            {
                CoreUtils.Destroy(m_NoiseTex);
                m_NoiseTex = CreateNoiseTexture(noiseBytes, noiseSize, TextureWrapMode.Repeat, "FilmGrainNoise");
                m_NoiseBytes = noiseBytes;
                m_NoiseSize = noiseSize;
            }

            return m_StdLut != null && m_NoiseTex != null;
        }

        private static Texture2D CreateR16Texture(TextAsset bytes, int width, int height, TextureWrapMode wrapMode, string name)
        {
            if (bytes == null)
                return null;

            byte[] raw = bytes.bytes;
            if (raw == null)
                return null;

            int pixelCount = width * height;
            int expectedR8Size = pixelCount;
            int expectedR16Size = expectedR8Size * 2;

            bool hasR16 = raw.Length >= expectedR16Size;
            bool hasR8 = raw.Length >= expectedR8Size;

            if (!hasR16 && !hasR8)
            {
                Debug.LogWarningFormat("FilmGrain: raw texture data size mismatch for {0}. Expected {1} or {2} bytes, got {3}.",
                    name, expectedR8Size, expectedR16Size, raw.Length);
                return null;
            }

            int inputSize = hasR16 ? expectedR16Size : expectedR8Size;
            if (raw.Length != inputSize)
            {
                var trimmed = new byte[inputSize];
                Buffer.BlockCopy(raw, 0, trimmed, 0, inputSize);
                raw = trimmed;
            }

            TextureFormat format = hasR16 ? TextureFormat.R16 : TextureFormat.R8;
            if (format == TextureFormat.R16 && !SystemInfo.SupportsTextureFormat(TextureFormat.R16))
            {
                if (SystemInfo.SupportsTextureFormat(TextureFormat.R8))
                {
                    raw = ConvertR16ToR8(raw, pixelCount);
                    format = TextureFormat.R8;
                }
                else
                {
                    raw = ConvertR16ToR8(raw, pixelCount);
                    raw = ConvertR8ToRGBA32(raw, pixelCount);
                    format = TextureFormat.RGBA32;
                }
            }
            else if (format == TextureFormat.R8 && !SystemInfo.SupportsTextureFormat(TextureFormat.R8))
            {
                raw = ConvertR8ToRGBA32(raw, pixelCount);
                format = TextureFormat.RGBA32;
            }

            int expectedSize = format == TextureFormat.R16
                ? expectedR16Size
                : (format == TextureFormat.R8 ? expectedR8Size : pixelCount * 4);

            if (raw.Length != expectedSize)
            {
                var trimmed = new byte[expectedSize];
                Buffer.BlockCopy(raw, 0, trimmed, 0, expectedSize);
                raw = trimmed;
            }

            var tex = new Texture2D(width, height, format, false, true);
            tex.name = name;
            tex.wrapMode = wrapMode;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 0;
            tex.LoadRawTextureData(raw);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Texture2D CreateNoiseTexture(TextAsset bytes, int size, TextureWrapMode wrapMode, string name)
        {
            if (bytes == null)
                return null;

            int expectedR8Size = size * size;
            int expectedR16Size = expectedR8Size * 2;
            byte[] raw = bytes.bytes;
            if (raw == null)
                return null;

            TextureFormat format;
            bool hasR16 = raw.Length >= expectedR16Size;
            bool hasR8 = raw.Length >= expectedR8Size;

            if (!hasR16 && !hasR8)
            {
                Debug.LogWarningFormat("FilmGrain: raw noise data size mismatch for {0}. Expected {1} or {2} bytes, got {3}.",
                    name, expectedR8Size, expectedR16Size, raw.Length);
                return null;
            }

            int inputSize = hasR16 ? expectedR16Size : expectedR8Size;
            if (raw.Length != inputSize)
            {
                var trimmed = new byte[inputSize];
                Buffer.BlockCopy(raw, 0, trimmed, 0, inputSize);
                raw = trimmed;
            }

            format = hasR16 ? TextureFormat.R16 : TextureFormat.R8;
            int pixelCount = size * size;

            if (format == TextureFormat.R16 && !SystemInfo.SupportsTextureFormat(TextureFormat.R16))
            {
                if (SystemInfo.SupportsTextureFormat(TextureFormat.R8))
                {
                    raw = ConvertR16ToR8(raw, pixelCount);
                    format = TextureFormat.R8;
                }
                else
                {
                    raw = ConvertR16ToR8(raw, pixelCount);
                    raw = ConvertR8ToRGBA32(raw, pixelCount);
                    format = TextureFormat.RGBA32;
                }
            }
            else if (format == TextureFormat.R8 && !SystemInfo.SupportsTextureFormat(TextureFormat.R8))
            {
                raw = ConvertR8ToRGBA32(raw, pixelCount);
                format = TextureFormat.RGBA32;
            }

            int expectedSize = format == TextureFormat.R16
                ? expectedR16Size
                : (format == TextureFormat.R8 ? expectedR8Size : pixelCount * 4);

            if (raw.Length != expectedSize)
            {
                var trimmed = new byte[expectedSize];
                Buffer.BlockCopy(raw, 0, trimmed, 0, expectedSize);
                raw = trimmed;
            }

            var tex = new Texture2D(size, size, format, false, true);
            tex.name = name;
            tex.wrapMode = wrapMode;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 0;
            tex.LoadRawTextureData(raw);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static byte[] ConvertR16ToR8(byte[] raw, int pixelCount)
        {
            var output = new byte[pixelCount];
            int srcIndex = 0;
            for (int i = 0; i < pixelCount; ++i)
            {
                int lo = raw[srcIndex++];
                int hi = raw[srcIndex++];
                int value = lo | (hi << 8);
                int quantized = (value * 255 + 32767) / 65535;
                output[i] = (byte)quantized;
            }
            return output;
        }

        private static byte[] ConvertR8ToRGBA32(byte[] raw, int pixelCount)
        {
            var output = new byte[pixelCount * 4];
            int dstIndex = 0;
            for (int i = 0; i < pixelCount; ++i)
            {
                byte value = raw[i];
                output[dstIndex++] = value;
                output[dstIndex++] = 0;
                output[dstIndex++] = 0;
                output[dstIndex++] = 255;
            }
            return output;
        }

        private sealed class FilmGrainRenderPass : ScriptableRenderPass
        {
            private static readonly MaterialPropertyBlock s_PropertyBlock = new MaterialPropertyBlock();

            private Material m_Material;
            private Texture m_StdLut;
            private Texture m_NoiseTex;

            private float m_FilterSigma;
            private float m_NoiseSigma;
            private float m_NoiseDecodeScale;
            private int m_NoiseSize;

            private FilmGrainSettings m_Settings;

            public FilmGrainRenderPass(string passName)
            {
                profilingSampler = new ProfilingSampler(passName);
            }

            public void Setup(Material material, Texture stdLut, Texture noiseTex, float filterSigma, float noiseSigma, float noiseDecodeScale, int noiseSize, FilmGrainSettings settings)
            {
                m_Material = material;
                m_StdLut = stdLut;
                m_NoiseTex = noiseTex;
                m_FilterSigma = filterSigma;
                m_NoiseSigma = noiseSigma;
                m_NoiseDecodeScale = noiseDecodeScale;
                m_NoiseSize = noiseSize;
                m_Settings = settings;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (m_Material == null || m_StdLut == null || m_NoiseTex == null)
                    return;

                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                if (!resources.cameraColor.IsValid())
                    return;

                TextureHandle source;
                TextureHandle destination = resources.activeColorTexture;

                var desc = renderGraph.GetTextureDesc(destination);
                desc.name = "_CameraColorFilmGrain";
                desc.clearBuffer = false;
                source = renderGraph.CreateTexture(desc);

                renderGraph.AddBlitPass(destination, source, Vector2.one, Vector2.zero, passName: "Copy Color Film Grain");

                float sigma = Mathf.Max(0.0001f, m_FilterSigma);
                float noiseSigma = Mathf.Max(0.0001f, m_NoiseSigma);
                float grainScale = Mathf.Max(0.01f, m_Settings.grainScale);

                float noiseScale = (noiseSigma / sigma) / (grainScale * m_NoiseSize);

                float t = m_Settings.noiseSpeed > 0.0f
                    ? Time.unscaledTime * m_Settings.noiseSpeed
                    : 0.0f;

                int frameIndex = Mathf.FloorToInt(t);

                uint seedA = Hash((uint)frameIndex + 1u);
                uint seedB = Hash((uint)frameIndex + 2u);

                Vector2 offsetA = GetNoiseOffset(seedA);
                Vector2 offsetB = GetNoiseOffset(seedB);

                GetNoiseBasis((int)(seedA & 7u), out Vector2 basisAX, out Vector2 basisAY);
                GetNoiseBasis((int)(seedB & 7u), out Vector2 basisBX, out Vector2 basisBY);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Film Grain", out var passData, profilingSampler))
                {
                    passData.material = m_Material;
                    passData.source = source;
                    passData.stdLut = m_StdLut;
                    passData.noiseTex = m_NoiseTex;
                    passData.noiseParamsA = new Vector4(noiseScale, noiseScale, offsetA.x, offsetA.y);
                    passData.noiseParamsB = new Vector4(noiseScale, noiseScale, offsetB.x, offsetB.y);
                    passData.noiseTransformA = new Vector4(basisAX.x, basisAX.y, basisAY.x, basisAY.y);
                    passData.noiseTransformB = new Vector4(basisBX.x, basisBX.y, basisBY.x, basisBY.y);
                    passData.grainParams = new Vector4(m_Settings.intensity, m_NoiseDecodeScale, 0.0f, 0.0f);

                    builder.UseTexture(passData.source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        ExecutePass(data, context);
                    });
                }
            }

            private static void ExecutePass(PassData data, RasterGraphContext context)
            {
                s_PropertyBlock.Clear();
                s_PropertyBlock.SetTexture(ShaderPropertyBlitTexture, data.source);
                s_PropertyBlock.SetVector(ShaderPropertyBlitScaleBias, new Vector4(1, 1, 0, 0));
                s_PropertyBlock.SetTexture(ShaderPropertyStdLut, data.stdLut);
                s_PropertyBlock.SetTexture(ShaderPropertyNoiseTex, data.noiseTex);
                s_PropertyBlock.SetVector(ShaderPropertyNoiseParamsA, data.noiseParamsA);
                s_PropertyBlock.SetVector(ShaderPropertyNoiseParamsB, data.noiseParamsB);
                s_PropertyBlock.SetVector(ShaderPropertyNoiseTransformA, data.noiseTransformA);
                s_PropertyBlock.SetVector(ShaderPropertyNoiseTransformB, data.noiseTransformB);
                s_PropertyBlock.SetVector(ShaderPropertyGrainParams, data.grainParams);

                context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
            }

            private sealed class PassData
            {
                public Material material;
                public TextureHandle source;
                public Texture stdLut;
                public Texture noiseTex;
                public Vector4 noiseParamsA;
                public Vector4 noiseParamsB;
                public Vector4 noiseTransformA;
                public Vector4 noiseTransformB;
                public Vector4 grainParams;
            }

            private static uint Hash(uint x)
            {
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }

            private static Vector2 GetNoiseOffset(uint seed)
            {
                uint seed2 = Hash(seed ^ 0x9E3779B9u);
                float offsetX = (seed & 0xFFFFu) / 65536.0f;
                float offsetY = (seed2 & 0xFFFFu) / 65536.0f;
                return new Vector2(offsetX, offsetY);
            }

            private static void GetNoiseBasis(int variant, out Vector2 basisX, out Vector2 basisY)
            {
                int rot = variant & 3;
                bool flip = (variant & 4) != 0;

                switch (rot)
                {
                    case 1:
                        basisX = new Vector2(0.0f, 1.0f);
                        basisY = new Vector2(-1.0f, 0.0f);
                        break;
                    case 2:
                        basisX = new Vector2(-1.0f, 0.0f);
                        basisY = new Vector2(0.0f, -1.0f);
                        break;
                    case 3:
                        basisX = new Vector2(0.0f, -1.0f);
                        basisY = new Vector2(1.0f, 0.0f);
                        break;
                    default:
                        basisX = new Vector2(1.0f, 0.0f);
                        basisY = new Vector2(0.0f, 1.0f);
                        break;
                }

                if (flip)
                    basisX = -basisX;
            }
        }
    }
}
