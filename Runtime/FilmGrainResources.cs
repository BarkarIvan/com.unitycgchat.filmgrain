using UnityEngine;

namespace UnityCgChat.FilmGrain
{
    internal static class FilmGrainResources
    {
        public const string DefaultShaderPath = "FilmGrain/FilmGrain";
        public const string DefaultStdLutPath = "FilmGrain/FilmGrainStdLut";
        public const string DefaultNoisePath = "FilmGrain/FilmGrainNoise";

        public const int DefaultLutSize = 256;
        public const int DefaultNoiseSize = 256;

        public const float DefaultFilterSigma = 1.0f;
        public const float DefaultNoiseSigma = 1.0f;
        public const float DefaultNoiseDecodeScale = 6.01576f;
    }
}
