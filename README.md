# Film Grain (URP 17 RenderGraph)

Film Grain render feature for URP 17 (Unity 6.3), using a LUT-driven statistical model and correlated noise to approximate film grain efficiently.

Based on:
- "Film Grain Rendering and Parameter Estimation" (Zhang et al., 2023)  
  https://dl.acm.org/doi/10.1145/3592127

See `Documentation~/README.md` for usage, parameters, and generation details.

Usage: add `FilmGrainRendererFeature` to your URP Renderer asset. Default LUT/Noise are loaded from `Resources`, or assign a `FilmGrainLutAsset` to override them.
See `Documentation~/README.md` for when to keep defaults vs. generate a custom LUT/Noise asset.
Runtime applies additive noise scaled by the LUT-derived std and `Intensity`.

Examples:
![Film grain example](https://github.com/BarkarIvan/com.unitycgchat.filmgrain/blob/main/Documentation~/Images/grain.gif)
![Film grain example 2](https://github.com/BarkarIvan/com.unitycgchat.filmgrain/blob/main/Documentation~/Images/grain%202.gif)


