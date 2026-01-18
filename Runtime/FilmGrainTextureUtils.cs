using System;
using UnityEngine;

namespace UnityCgChat.FilmGrain
{
    internal static class FilmGrainTextureUtils
    {
        public static Texture2D CreateLutTexture(TextAsset bytes, int width, int height, TextureWrapMode wrapMode, string name)
        {
            return CreateRawTexture(bytes, width, height, wrapMode, name);
        }

        public static Texture2D CreateNoiseTexture(TextAsset bytes, int size, TextureWrapMode wrapMode, string name)
        {
            return CreateRawTexture(bytes, size, size, wrapMode, name);
        }

        private static Texture2D CreateRawTexture(TextAsset bytes, int width, int height, TextureWrapMode wrapMode, string name)
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
    }
}
