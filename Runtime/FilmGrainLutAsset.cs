using UnityEngine;
using UnityEngine.Serialization;

namespace UnityCgChat.FilmGrain
{
    [CreateAssetMenu(fileName = "FilmGrainLut", menuName = "UnityCgChat/Film Grain LUT")]
    public sealed class FilmGrainLutAsset : ScriptableObject
    {
        [Header("Model Parameters (Paper)")]
        [Tooltip("Mean grain radius (mu_r).")]
        [Min(0.0001f)]
        public float grainRadiusMean = 0.05f;

        [Tooltip("Std deviation of grain radius (sigma_r).")]
        [Min(0.0f)]
        public float grainRadiusStd = 0.25f;

        [Tooltip("Gaussian filter sigma used for correlation (sigma).")]
        [Min(0.0001f)]
        public float filterSigma = 1.0f;

        [Header("Std LUT")]
        [Tooltip("1D LUT size (number of samples across luma).")]
        [Min(16)]
        public int lutSize = 256;

        [Tooltip("R16 raw LUT storing per-luma standard deviation.")]
        [FormerlySerializedAs("varianceLutBytes")]
        public TextAsset stdLutBytes;

        [Tooltip("Maximum std value in the LUT (for reference).")]
        [Min(0.0f)]
        public float maxStd = 0.101737f;

        [Header("Noise")]
        [Tooltip("Tile size of the correlated noise texture.")]
        [Min(16)]
        public int noiseSize = 256;

        [Tooltip("Gaussian sigma used to correlate the noise.")]
        [Min(0.0001f)]
        public float noiseSigma = 1.0f;

        [Tooltip("Scale to recover unit variance from encoded noise.")]
        [Min(0.0001f)]
        public float noiseDecodeScale = 6.01576f;

        [Tooltip("R8 raw noise texture (tileable).")]
        public TextAsset noiseBytes;

        public bool HasValidData
        {
            get
            {
                return stdLutBytes != null && noiseBytes != null;
            }
        }
    }
}
