using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityCgChat.FilmGrain;

namespace UnityCgChat.FilmGrain.Editor
{
    [CustomEditor(typeof(FilmGrainLutAsset))]
    public sealed class FilmGrainLutAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            if (GUILayout.Button("Generate LUT and Noise"))
            {
                var asset = (FilmGrainLutAsset)target;
                Generate(asset);
            }
        }

        private static void Generate(FilmGrainLutAsset asset)
        {
            if (asset == null)
                return;

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("FilmGrain: asset must be saved to disk before generating data.");
                return;
            }

            string directory = Path.GetDirectoryName(assetPath);
            string lutPath = Path.Combine(directory, asset.name + "_StdLut.bytes");
            string noisePath = Path.Combine(directory, asset.name + "_Noise.bytes");

            int lutSize = Mathf.Max(16, asset.lutSize);
            int noiseSize = Mathf.Max(16, asset.noiseSize);

            double muR = Math.Max(0.0001, asset.grainRadiusMean);
            double sigmaR = Math.Max(0.0, asset.grainRadiusStd);
            double sigma = Math.Max(0.0001, asset.filterSigma);

            byte[] lutBytes = GenerateStdLut(lutSize, muR, sigmaR, sigma, out double maxStd);
            byte[] noiseBytes = GenerateNoise(noiseSize, Math.Max(0.0001, asset.noiseSigma), out double noiseDecodeScale);

            File.WriteAllBytes(lutPath, lutBytes);
            File.WriteAllBytes(noisePath, noiseBytes);

            AssetDatabase.ImportAsset(lutPath);
            AssetDatabase.ImportAsset(noisePath);

            asset.stdLutBytes = AssetDatabase.LoadAssetAtPath<TextAsset>(lutPath);
            asset.noiseBytes = AssetDatabase.LoadAssetAtPath<TextAsset>(noisePath);
            asset.maxStd = (float)maxStd;
            asset.noiseDecodeScale = (float)noiseDecodeScale;

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static byte[] GenerateStdLut(int lutSize, double muR, double sigmaR, double sigma, out double maxStd)
        {
            double varianceR = sigmaR * sigmaR;
            double sigmaLn = 0.0;
            double muLn = Math.Log(muR);

            if (sigmaR > 0.0)
            {
                sigmaLn = Math.Sqrt(Math.Log(1.0 + varianceR / (muR * muR)));
                muLn = Math.Log(muR) - 0.5 * sigmaLn * sigmaLn;
            }

            double er2 = muR * muR + varianceR;
            double rMax = sigmaR > 0.0 ? Math.Exp(muLn + 3.0 * sigmaLn) : muR;

            int gammaSamples = Mathf.Clamp(lutSize * 2, 256, 2048);
            int radiusSamples = 256;

            double xMax = Math.Max(6.0 * sigma, 2.0 * rMax);
            double dx = xMax / (gammaSamples - 1);

            double[] gamma = new double[gammaSamples];

            if (sigmaR <= 0.0)
            {
                for (int i = 0; i < gammaSamples; ++i)
                {
                    double d = i * dx;
                    gamma[i] = OverlapArea(d, muR);
                }
            }
            else
            {
                for (int i = 0; i < gammaSamples; ++i)
                {
                    double d = i * dx;
                    double rMin = Math.Max(d * 0.5, 1e-6);
                    if (rMin >= rMax)
                    {
                        gamma[i] = 0.0;
                        continue;
                    }

                    double dr = (rMax - rMin) / (radiusSamples - 1);
                    double sum = 0.0;
                    for (int j = 0; j < radiusSamples; ++j)
                    {
                        double r = rMin + j * dr;
                        double pdf = LogNormalPdf(r, muLn, sigmaLn);
                        double area = OverlapArea(d, r);
                        double w = (j == 0 || j == radiusSamples - 1) ? 0.5 : 1.0;
                        sum += w * area * pdf;
                    }
                    gamma[i] = sum * dr;
                }
            }

            byte[] bytes = new byte[lutSize * 2];
            maxStd = 0.0;

            for (int i = 0; i < lutSize; ++i)
            {
                double u = i / (double)(lutSize - 1);
                if (u >= 1.0)
                    u = 1.0 - 1e-6;

                double lambda = (1.0 / (Math.PI * er2)) * Math.Log(1.0 / (1.0 - u));

                double sum = 0.0;
                for (int k = 0; k < gammaSamples; ++k)
                {
                    double x = k * dx;
                    double t = lambda * gamma[k];
                    if (t > 50.0)
                        t = 50.0;
                    double cb = (1.0 - u) * (1.0 - u) * (Math.Exp(t) - 1.0);
                    double w = Math.Exp(-(x * x) / (4.0 * sigma * sigma)) * cb * x;
                    double factor = (k == 0 || k == gammaSamples - 1) ? 0.5 : 1.0;
                    sum += factor * w;
                }

                double integral = sum * dx;
                double var = (1.0 / (2.0 * sigma * sigma)) * integral;
                double std = Math.Sqrt(Math.Max(0.0, var));
                if (std > maxStd)
                    maxStd = std;

                double clamped = Math.Min(1.0, Math.Max(0.0, std));
                ushort value = (ushort)Math.Round(clamped * 65535.0);
                bytes[i * 2 + 0] = (byte)(value & 0xFF);
                bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            return bytes;
        }

        private static byte[] GenerateNoise(int size, double sigma, out double noiseDecodeScale)
        {
            int count = size * size;
            double[] noise = new double[count];

            var rng = new System.Random(12345);
            for (int i = 0; i < count; ++i)
                noise[i] = NextGaussian(rng);

            double[] kernel = BuildGaussianKernel(sigma);
            int radius = (kernel.Length - 1) / 2;

            double[] temp = new double[count];
            double[] filtered = new double[count];

            for (int y = 0; y < size; ++y)
            {
                int row = y * size;
                for (int x = 0; x < size; ++x)
                {
                    double sum = 0.0;
                    for (int k = -radius; k <= radius; ++k)
                    {
                        int xx = (x + k + size) % size;
                        sum += noise[row + xx] * kernel[k + radius];
                    }
                    temp[row + x] = sum;
                }
            }

            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    double sum = 0.0;
                    for (int k = -radius; k <= radius; ++k)
                    {
                        int yy = (y + k + size) % size;
                        sum += temp[yy * size + x] * kernel[k + radius];
                    }
                    filtered[y * size + x] = sum;
                }
            }

            double mean = 0.0;
            for (int i = 0; i < count; ++i)
                mean += filtered[i];
            mean /= count;

            double var = 0.0;
            for (int i = 0; i < count; ++i)
            {
                filtered[i] -= mean;
                var += filtered[i] * filtered[i];
            }
            var /= count;
            double std = Math.Sqrt(var);
            if (std > 0.0)
            {
                for (int i = 0; i < count; ++i)
                    filtered[i] /= std;
            }

            const double encodeScale = 0.5 / 3.0;
            byte[] bytes = new byte[count];

            double encodedMean = 0.0;
            double encodedVar = 0.0;

            for (int i = 0; i < count; ++i)
            {
                double encoded = filtered[i] * encodeScale + 0.5;
                if (encoded < 0.0)
                    encoded = 0.0;
                if (encoded > 1.0)
                    encoded = 1.0;

                byte value = (byte)Math.Round(encoded * 255.0);
                bytes[i] = value;

                double quantized = value / 255.0;
                double zeroMean = quantized - 0.5;
                encodedMean += zeroMean;
                encodedVar += zeroMean * zeroMean;
            }

            encodedMean /= count;
            encodedVar = encodedVar / count - encodedMean * encodedMean;
            double encodedStd = Math.Sqrt(Math.Max(0.0, encodedVar));
            noiseDecodeScale = encodedStd > 0.0 ? 1.0 / encodedStd : 1.0;

            return bytes;
        }

        private static double[] BuildGaussianKernel(double sigma)
        {
            int radius = Mathf.Clamp((int)Math.Ceiling(3.0 * sigma), 1, 64);
            int size = radius * 2 + 1;
            double[] kernel = new double[size];
            double sum = 0.0;

            for (int i = 0; i < size; ++i)
            {
                int x = i - radius;
                double v = Math.Exp(-(x * x) / (2.0 * sigma * sigma));
                kernel[i] = v;
                sum += v;
            }

            if (sum > 0.0)
            {
                for (int i = 0; i < size; ++i)
                    kernel[i] /= sum;
            }

            return kernel;
        }

        private static double NextGaussian(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static double OverlapArea(double d, double r)
        {
            if (d >= 2.0 * r)
                return 0.0;

            double t = d / (2.0 * r);
            t = Math.Max(-1.0, Math.Min(1.0, t));

            return 2.0 * r * r * Math.Acos(t) - 0.5 * d * Math.Sqrt(Math.Max(0.0, 4.0 * r * r - d * d));
        }

        private static double LogNormalPdf(double r, double mu, double sigma)
        {
            if (r <= 0.0 || sigma <= 0.0)
                return 0.0;

            double inv = 1.0 / (r * sigma * Math.Sqrt(2.0 * Math.PI));
            double z = (Math.Log(r) - mu) / sigma;
            return inv * Math.Exp(-0.5 * z * z);
        }
    }
}
