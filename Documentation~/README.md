# Film Grain (URP 17 RenderGraph)

This package provides a Film Grain render feature for URP 17 (Unity 6.3). It follows the statistical model from:

- "Film Grain Rendering and Parameter Estimation" (Zhang et al., 2023)

The runtime path is optimized: per-pixel standard deviation is read from a LUT texture, and correlated noise is sampled from a prefiltered noise texture.

## Usage

1. Add `FilmGrainRendererFeature` to your URP Renderer asset.
2. Keep the default LUT/noise resources in the package, or assign a `FilmGrainLutAsset`.
3. Tune `Intensity`, `Grain Scale`, and `Noise Speed`.

The shader is stored in `Resources`, so it is always included in builds without adding it to "Always Included Shaders".

## Default Resources and When to Create a LUT Asset

The package ships with default data in `Runtime/Resources/FilmGrain/`:

- `FilmGrainStdLut.bytes` (R16, size 256) encodes per-luma standard deviation.
- `FilmGrainNoise.bytes` (R8, size 256x256) is tileable correlated noise.

Defaults come from the generator with `mu_r=0.05`, `sigma_r=0.25`, `sigma=1.0`,
and use `noiseDecodeScale=6.01576`. If you are only tweaking intensity, grain size,
or speed, you can rely on these defaults and skip creating a `FilmGrainLutAsset`.

Create a `FilmGrainLutAsset` when you need to change the statistical model or quality:

- Different grain statistics (radius mean/std, correlation sigma).
- Different LUT or noise sizes (quality vs memory/perf tradeoff).
- Multiple looks for different scenes or cameras.

## Parameters (Runtime)

- `Intensity`: linear amplitude multiplier for the LUT-driven standard deviation.
- `Grain Scale`: scales the correlation length in screen pixels (larger value = larger grains).
- `Noise Speed`: pattern change rate per second for temporal variation.

## LUT Generation (Editor)

Create a `FilmGrainLutAsset` and click **Generate LUT and Noise**:

- Grain model parameters: `grainRadiusMean`, `grainRadiusStd`, `filterSigma`
- LUT size: `lutSize` (1D R16)
- Noise: `noiseSize`, `noiseSigma` (tileable, gaussian-filtered, stored as R8)

The generator writes `.bytes` files (LUT in R16, noise in R8) next to the asset and updates the fields:

- `stdLutBytes`
- `noiseBytes`
- `maxStd`
- `noiseDecodeScale`

## Notes

- The LUT stores std computed from Eq. (13) using the Boolean model covariance (Eq. 4) and the overlap term (Eq. 5).
- Runtime applies additive noise scaled by the LUT-derived std and `Intensity`.
- Correlation uses the paper's approximation: `exp(-||Δ||^2 / (4 * sigma^2))`, modeled with a Gaussian-filtered noise texture.
- Default resources were generated for: `mu_r=0.05`, `sigma_r=0.25`, `sigma=1.0`.
- If `grainRadiusStd` is 0, the generator falls back to the constant-radius case.
