# Zombies 5.5 - Graphics Settings and In-Game Benchmark: Research and Design

Status: design only. No project file other than this document was changed. The Unity Editor and CLI were not used.
Engine: Unity 6000.6.9f1, URP 17.6.0 (`Packages/manifest.json`; package cache `com.unity.render-pipelines.universal@2a6014b851d8`), deferred renderer, D3D12 on Windows (from the existing `benchmark.json`).

Contents
1. Research summary (comparable games, frequency table, costs, presets, display settings, benchmarks)
2. Unity 6000.6 / URP 17.6 mapping (what can change live, what cannot)
3. Current project state (what is already configurable, what is game-specific)
4. Proposed settings list (26 rows in 5 groups)
5. Preset table
6. In-game benchmark design
7. Implementation plan
8. Risks, open decisions, acceptance tests
9. Sources

A note on evidence. PCGamingWiki returned HTTP 403 to every fetch, and NVIDIA only has full guides for some of these games, so I did not read the per-game Video tables directly. Every per-game fact below is tagged **[S]** (taken from a page or search snippet I read; URL in section 9) or **[K]** (my background knowledge of that game's menu, not re-verified today). Measured fps costs are only quoted where an NVIDIA guide printed a number; everything else is marked "estimate".

---

## 1. Research summary

### 1.1 Games surveyed (9 menus, plus one extra guide)

GTA V, Red Dead Redemption 2, Watch Dogs 2, Mafia: Definitive Edition, Cyberpunk 2077, Max Payne 3, The Division 2, Sleeping Dogs: Definitive Edition, Call of Duty: Black Ops 6 (the Zombies mode uses the shared PC menu; no Zombies-specific menu exists). The original The Division's NVIDIA guide is used as a measurement source for the Division 2 engine.

### 1.2 Frequency table (n of 9 games)

"Confirmed" counts games where I have a source or snippet for that setting in that game; the rest are [K]. Treat the number as approximate to +/-1.

| Setting | n/9 | Confirmed in | Notes |
|---|---|---|---|
| Display mode (windowed / borderless / exclusive) | 9 | Mafia DE, BO6 [S] | Universal. BO6 guides recommend Exclusive for latency, Borderless for multitasking. |
| Resolution | 9 | all [K] | Universal. |
| VSync | 9 | Mafia DE, Division 2 [S] | Universal. |
| Anti-aliasing (FXAA / SMAA / TAA / MSAA) | 9 | GTA V, WD2, MP3, Mafia, BO6 [S] | MSAA is in the older titles (GTA V, WD2, MP3); every guide says it is the most expensive option. |
| Shadow quality | 9 | GTA V, WD2, MP3, Mafia, Div2, BO6 [S] | Always the single biggest quality-for-fps trade. |
| Ambient occlusion (SSAO/HBAO+/HDAO) | 8 | GTA V, WD2, MP3, Div2, SD, BO6 [S] | Cheap to mid. |
| Reflection quality (incl. SSR) | 8 | GTA V, WD2, MP3, Mafia, Div2, CP77 [S] | SSR is the heavy one in Cyberpunk. |
| Brightness / gamma | 7 | Mafia, Div2 [S] | Cheap; usually a calibration slider. |
| Texture quality | 7 | GTA V, MP3, BO6 [S] | VRAM, not fps. Division 2 style engines stream instead. |
| Motion blur | 7 | WD2, Mafia, SD [S] | Toggle or strength. ~1 fps in WD2. |
| FOV | 6 | WD2 (up to 110), Mafia (40-90) [S] | Third-person games often fix it (RDR2). |
| Render scale / upscaler (DLSS, FSR, pixel density, resolution scale) | 6 | WD2 "Pixel Density", Div2 "Resolution Scale 50-100%", BO6 [S] | Recent games all have one; Div2 also has a Sharpening slider. |
| Depth of field | 6 | GTA V, WD2, Mafia, Div2, BO6 [S] | Cheap in gameplay (WD2 ~2 fps, GTA V <=1.6 fps). |
| Draw distance / LOD / object detail | 6 | GTA V (Distance Scaling), WD2, Mafia (Geometry), Div2 (Object Detail), SD (World Density) [S] | Often the biggest CPU lever. |
| Anisotropic filtering | 5 | GTA V, MP3, Div2 [S] | Negligible cost (Div2: 0.6 fps from 1x to 16x). |
| Vegetation / grass | 5 | GTA V, WD2, Mafia, Div2 [S] | GTA V grass is expensive in forests. |
| Volumetric fog / effects | 5 | Mafia, Div2, CP77, RDR2, BO6 [S] | High cost at top tiers. |
| Built-in benchmark | 5 | GTA V, RDR2, CP77, Div2, SD [S] | See 1.6. |
| Population / crowd / clutter density | 4 | GTA V, SD (World Density), WD2 (Extra Details) [S] | WD2 calls Extra Details its most CPU-intensive setting. Relevant to this game. |
| Particle quality | 4 | GTA V, Div2, BO6 [S] | GTA V: at most 6-10 fps in the heaviest scenes. |
| Film effects (grain, chromatic aberration, lens flare, vignette) | 4 | Div2, CP77 [S/K] | Almost always cheap; grouped for convenience. |
| Frame-rate limit | 3 | Div2 (30-200), SD, BO6 [S] | Less common than VSync; most rely on the driver. |
| Quality presets | 7 | Mafia (Low/Med/High/Optimal/Custom), Div2, BO6 (5 presets) [S] | MP3 and GTA V have none [S]. |
| Lighting / GI quality | 3 | RDR2, Mafia [S] | RDR2: Medium vs High lighting recovers 25-40% on one guide (unmeasured, see below). |
| Ray tracing | 1 | CP77 | Out of scope. |

Take-aways for this game: every game has display mode, resolution, VSync, AA, shadows. The next tier (AO, reflections, texture, motion blur, FOV, brightness, render scale, DoF, draw distance) is present in 6-8 of 9. Frame cap is in only 3 of 9 but this project already has it, and a high-refresh monitor makes it more useful than usual. Population density is the genre-specific analogue of this game's "street clutter".

### 1.3 Typical options and typical relative cost (measured where noted)

Costs are fps or percent as printed in the NVIDIA GeForce guides (GTA V, Watch Dogs 2, The Division). "Est." is a third-party or my own estimate.

| Setting | Typical options | Typical cost |
|---|---|---|
| Shadow quality / resolution | Off, Low-Ultra; soft-shadow modes (PCSS/HFTS) | GTA V soft shadows: 6.4 fps at most (PCSS) [S]. WD2: HFTS 15 fps more than Ultra, itself 4.4 fps slower than Very High [S]. Div2 (Division 1 engine): spot shadow count High 6.6 fps, resolution max just under 5 fps [S]. Mafia: Low vs High ~15-20% (est.) [S]. BO6: shadows reported as ~10-12% [S]. |
| Shadow distance / cascades | Slider or via preset | GTA V "Extended Shadow Distance" described as insignificant, Distance Scaling "moderate" [S]. |
| Ambient occlusion | Off / Low / Medium / High / HBAO+ | WD2: at most 5 fps; Div2: Low-Ultra 1-3 fps, HBAO+ +4.5 fps at 1080p [S]. |
| Anti-aliasing | Off / FXAA / SMAA / TAA / MSAA 2-8x | WD2: SMAA 2 fps, FXAA 1.2 fps, MSAA 2x 11 fps ("over 10"), 4x nearly 20 fps [S]. MP3: MSAA vs FXAA costs 40-50% [S]. Rule: post/temporal AA is cheap, MSAA is not. |
| Anisotropic filtering | Off, 2x-16x | Div2: 0.6 fps 1x to 16x; WD2 about 2 fps [S]. |
| Reflections | Off-Ultra, SSR | WD2: reflections <3 fps, SSR Very High 9.3 fps / Ultra 11.4 fps on wet nights; Div2: Very High vs Off 7.1 fps [S]. |
| Depth of field | Off / Medium / High | WD2 ~2 fps; GTA V <=1.6 fps; Div2 recommends Medium [S]. |
| Motion blur / bloom | On/Off | WD2: ~1 fps each [S]. |
| Lens flare | On/Off | Div2: "a few fps" [S]. |
| Particles | Low-Ultra | GTA V: 6-10 fps in the heaviest scenes; Div2: "heavy toll" at max [S]. |
| Density / extra details / object detail | slider 0-100% | WD2 Extra Details: 16.4 fps on an i7-6700K, the most CPU-heavy [S]. Div2 Object Detail: up to 11 fps in a distant view, more on slower CPUs [S]. GTA V population density: gameplay spread 2-3x the benchmark spread of a few fps [S]. |
| Extra streaming / view distance | slider | Div2: up to 6.7 fps (more on slow CPUs) [S]. GTA V Extended Distance Scaling is its largest single cost [S]. |
| Subsurface scattering, vignette, wind snow | toggles | Div2: ~2.5 fps, fraction of a fps, 1 fps [S]. |
| Texture quality | Low-Ultra | Mostly VRAM; GTA V "nearly identical" fps across levels [S]. BO6 needs medium on 8 GB cards [S]. |
| Render scale / upscaler | 50-200%, FSR/DLSS tiers | WD2 pixel density 1.25x: 3.2 fps with temporal filtering [S]. Recent guides recommend DLSS Quality/Balanced as the largest free gain (est.). |

Two lessons that drive this design: (a) the cheap effects (bloom, motion blur, DoF, grain, lens flare, AF) are each ~1-3 fps, so they are best exposed as plain toggles; (b) the heavy items are shadows, AA mode, volumetrics/SSR, render scale, and anything that scales draw-call or CPU load (density, object detail, streaming). In this game, being CPU main-thread bound, the CPU-scaling items are the ones that matter on the developer's rig and the GPU items matter on modest PCs.

### 1.4 Presets

- Typical set: Low / Medium / High / Ultra plus Custom (Mafia DE adds "Optimal", which looks at the GPU; BO6 has five presets; RDR2 uses a "quality preset" slider that moves many sub-settings) [S].
- Selecting a preset overwrites the dependent settings; changing any dependent setting afterwards flips the preset label to Custom. Settings that are not quality-related (display mode, resolution, VSync, FOV, brightness, frame cap) are not touched by presets.
- GTA V and Max Payne 3 ship no presets and are widely criticised for it in guides [S]; do not copy that.
- A first-launch auto-pick (Mafia "Optimal", GeForce Optimize) is nice to have; see open decisions.

### 1.5 Display settings

- Display mode: windowed / borderless / exclusive fullscreen. BO6 guides recommend Exclusive for latency and Borderless for convenience [S]. Resolution list is shown in windowed and exclusive; in borderless it is the desktop resolution and the **render scale** slider replaces it (render scale is how modern games let you render lower in borderless).
- Refresh rate selector appears next to resolution in exclusive mode (BO6 lists monitor, adapter, refresh rate, resolution) [S].
- Frame cap: Div2 30-200 custom [S]; BO6 custom FPS limit [S]; the in-game cap exists so the player does not need the driver panel.
- VSync: on/off. Some games add "adaptive"; not needed here.
- FOV: Mafia 40-90, WD2 up to 110 [S]. For a third-person camera a modest range (45-80) is enough.
- Brightness: Div2 0-20 with contrast [S]; Mafia has a brightness slider [S]. Exposed as one slider.

### 1.6 Built-in benchmarks: what they run and show

| Game | What it runs | What it shows |
|---|---|---|
| GTA V | Scripted 5-pass benchmark (four scenes plus a repeated pass); NVIDIA advises halving the result to estimate heavy single-player moments [S] | Text log in Documents\Rockstar Games\GTA V\Benchmarks with per-pass min/max/avg fps, frame-time percentiles (ms), and counts of frames under 16 ms and 33 ms [S]. |
| Red Dead Redemption 2 | Scripted multi-scene flythrough | Min/max/average fps, saved to a file; the community pastes these three numbers [S]. Settings are saved alongside (system.xml) [S]. |
| Cyberpunk 2077 | Short canned city flythrough | Frames, total time, average, min, max, 1% low and 0.1% low fps [S]. Community caveat: the average is not representative of the heaviest areas [S]. |
| The Division 2 | Scripted run | Average, CPU and GPU performance, and second-by-second FPS with CPU and GPU load graphs; results saved as .xml [S]. No source describes a literal "CPU-bound" label; the load graphs are how a player sees it. |
| Sleeping Dogs | Scripted drive | Dialog with min / max / average fps, the current graphics settings, and GPU details [S]. |
| Shadow of the Tomb Raider (extra, [K]) | Three-scene run | Shows CPU game / CPU render / GPU bound percentage and a "GPU bound / CPU bound" summary; the model for the verdict line below. |

What good ones have in common, and what this design copies: fixed deterministic content, results that include avg + lows + min/max, a settings snapshot with the result, a saved file, and (in the better ones) a CPU/GPU split. What the old ones lack: frame-time graph, history, comparison. We add those.

---

## 2. Unity 6000.6 / URP 17.6 mapping

Verification method: URP source under `Library/PackageCache/com.unity.render-pipelines.universal@2a6014b851d8` (read directly) and the Unity 6000.6 script reference pages (Screen.SetResolution, FullScreenMode, FrameTimingManager, QualitySettings) fetched from docs.unity.com (the docs.unity3d.com URLs redirect there). Compile symbols were checked in `Library/Bee/artifacts/*/Unity.RenderPipelines.Universal.Runtime.rsp`: **`ENABLE_UPSCALER_FRAMEWORK` and `MODERN_SSAO` are not defined in this project**, so (a) `UniversalRenderPipelineAsset.upscalingFilter` is a normal (non-obsolete) property with the enum `Auto, Linear, Point, FSR, STP`, and (b) there is no SSAO Volume override or GTAO; SSAO is controlled only through the renderer feature. Re-check this if the engine is upgraded.

### 2.1 Pipeline asset (`UniversalRenderPipelineAsset`)

| Setting | API | Setter | Live? | Notes |
|---|---|---|---|---|
| Render scale | `asset.renderScale` (clamped by `UniversalRenderPipeline.minRenderScale`..`maxRenderScale`, typically 0.1..2) | public | Live, next frame | Per-camera `UniversalAdditionalCameraData` has no render-scale override in this path; use the asset. |
| Upscaler | `asset.upscalingFilter = UpscalingFilterSelection.{Auto, Linear, FSR, STP}` | public | Live | `Point` is the only upscaler whose shader variants are stripped when unused (`PointSamplingUpsampling` in `ShaderBuildPreprocessor.cs`); do not expose it. FSR 1 is a spatial filter; STP is compute-based and **requires TAA on the camera** (`IsSTPEnabled` needs TAA; if TAA is off STP silently falls back). STP falls back to Auto without compute support. FSR sharpness: `asset.fsrOverrideSharpness`, `asset.fsrSharpness` (public). |
| MSAA | `asset.msaaSampleCount` (1/2/4/8, public) | public | Live | **Not usable here:** Unity's rendering-path comparison lists MSAA as unsupported in the Deferred path (Forward: yes, Forward+: yes, Deferred: no), and MSAA cannot be combined with TAA. This project is Deferred (`m_RenderingMode: 2`) with TAA. Do not offer MSAA. |
| Shadow distance | `asset.shadowDistance` (clamped >= 0) | public | Live | Also bounded by camera far clip (400). |
| Cascade count | `asset.shadowCascadeCount` (1-4, throws `ArgumentException` outside) | public | Live | Never use 1: the non-cascade keyword variant would be stripped (see 2.7). Use 2-4. Splits: `cascade2Split`, `cascade3Split`, `cascade4Split` public. |
| Main light shadow map resolution | `asset.mainLightShadowmapResolution` (int; 256..8192) | public | Live | Reallocates the shadow RT on change (one-frame hitch). 8192 = 128 MB at 16-bit depth. |
| Additional light shadow atlas | `asset.additionalLightsShadowmapResolution` | public | Live | Tier sizes (`...ResolutionTierLow/Medium/High`) have `internal set`. |
| Additional lights per object | `asset.maxAdditionalLightsCount` | public | Live | **Forward only.** Deferred has no per-object light cap, so this does nothing here. |
| Additional/main light shadows on/off | `supportsMainLightShadows`, `supportsAdditionalLightShadows` | `internal set` | Reflection only | Alternative: `Light.shadows = LightShadows.None` per light (public, live). Use that. |
| Soft shadow on/off | `asset.supportsSoftShadows` | `internal set` | Reflection only | Alternative: `Light.shadows = Hard / Soft` on the sun (public, live). |
| Soft shadow quality | `asset.softShadowQuality` is `internal` | internal | Reflection only | **Public alternative:** `UniversalAdditionalLightData.softShadowQuality` (public get/set, enum `UsePipelineSettings, Low, Medium, High`) on the sun. The PC asset has `m_SoftShadowQuality: 3` (High); set the sun's value explicitly. |
| HDR | `asset.supportsHDR` | public | Live | Low-end saver: keep HDR on (tonemapping needs it) but `asset.hdrColorBufferPrecision = HDRColorBufferPrecision._32Bits` (project already uses `_32Bits`, value 0). `_64Bits` is a candidate for Ultra (less banding, more bandwidth). |
| Opaque texture | `asset.supportsCameraOpaqueTexture` | public | Live | **Finding:** `m_RequireOpaqueTexture: 1` in PC_RPAsset, but no shader or script in `Assets` references `_CameraOpaqueTexture` or `SampleSceneColor` (grep). It is a wasted full-screen copy; recommend off in all presets (not a player setting). Re-verify before changing. |
| Depth texture | `asset.supportsCameraDepthTexture` | public | Live | Needed (soft particles, DoF). Keep. |
| LOD cross-fade | `asset.enableLODCrossFade` and `lodCrossFadeDitheringType` | getter only | No | Leave as authored. Fence far LOD uses cross-fade (`AddFenceFarLod`), parked cars use `LODFadeMode.None`. |
| Reflection probe blending / atlas | `asset.reflectionProbeBlending`, `reflectionProbeAtlas` | `internal set` | No | Not exposed. Probes are baked (`BuildReflectionProbes`). |
| Rendering mode (Deferred/Forward+) | `ScriptableRendererData` | - | Restart / renderer switch | Not a player setting. |
| GPU Resident Drawer | `asset.gpuResidentDrawerMode` | public | Off in project (`m_GPUResidentDrawerMode: 0`) | Out of scope; would also give `shadowSmallMeshScreenPercentages`, which only applies to GRD. |

### 2.2 Per-camera anti-aliasing (`UniversalAdditionalCameraData` on the main camera)

- `camData.antialiasing = AntialiasingMode.{None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing}`, `camData.antialiasingQuality = AntialiasingQuality.{Low, Medium, High}`: public, live. Quality maps to SMAA quality, and to the TAA variant quality.
- TAA tuning is `camData.taaSettings` (struct; `ref` property): `baseBlendFactor`, `jitterScale`, `mipBias`, `varianceClampScale`, `contrastAdaptiveSharpening`. SceneBuilder sets `contrastAdaptiveSharpening 0.35`, `baseBlendFactor 0.97`, `jitterScale 0.5`. Keep these for TAA at every level; they are look decisions.
- Switching between modes is live. TAA needs motion vectors; this project already renders them.
- FXAA and SMAA run on the (possibly upscaled) final image; FXAA before FSR upscaling has not been checked visually. Test Low preset (FSR + FXAA) in a build before shipping.

### 2.3 QualitySettings (static, all public, all live)

| Setting | API | Notes |
|---|---|---|
| LOD bias | `QualitySettings.lodBias` (float multiplier; higher = keep detail longer) | Project's PC level has `lodBias: 2`. Cars use custom LOD distances (`CarLod1Dist 12`, `CarLod2Dist 35`, `CarCullDist 150`), so the bias scales those too. |
| Max LOD level | `QualitySettings.maximumLODLevel` | Forces the lowest LOD index (skips LOD0). A harsher alternative at Low; leave 0. |
| Texture quality | `QualitySettings.globalTextureMipmapLimit` (int; 0 full, 1 half, 2 quarter) | Live, but already-loaded textures are re-uploaded: expect a hitch of tens to hundreds of ms. Apply from the menu, not mid-run. Streaming mipmaps would need per-texture import flags (rebuild); not planned. |
| Anisotropic filtering | `QualitySettings.anisotropicFiltering = AnisotropicFiltering.{Disable, Enable, ForceEnable}` plus `Texture.SetGlobalAnisotropicFilteringLimits(forcedMin, globalMax)` | Project is `ForceEnable` (value 2 in the PC level). To get discrete 2x/4x/8x/16x set `ForceEnable` and `SetGlobalAnisotropicFilteringLimits(n, n)`. Calibrate "High" against a screenshot of the current look, since ForceEnable without limits uses each texture's own level. |
| Realtime reflection probes | `QualitySettings.realtimeReflectionProbes` | Already off in the PC level; all probes are baked. Leave off. |
| Skin weights | `QualitySettings.skinWeights = SkinWeights.{OneBone, TwoBones, FourBones, Unlimited}` | Live. PC level uses `FourBones`. Mostly GPU/vertex cost on 48 skinned enemies. |
| Particle raycast budget | `QualitySettings.particleRaycastBudget` | Only matters for particle systems using the Collision module "World" in Approximate mode. Cheap, set per preset (16/64/256/512). |
| Soft particles | `QualitySettings.softParticles` | PC level has it off; URP uses the depth texture. Leave. |
| Frame pacing | `QualitySettings.vSyncCount`, `Application.targetFrameRate` | Already handled by `FramePacing.Resolve` (see 3.3). |
| Pipeline asset | `QualitySettings.renderPipeline` (get/set) | Used to install the runtime clone (see 7.3). |
| Quality level | `QualitySettings.SetQualityLevel(i, applyExpensiveChanges)` | **Not used for presets.** It would swap pipeline assets/renderers wholesale and resets values the applier sets. |

### 2.4 Screen: display mode, resolution, refresh rate (docs.unity.com, 6000.6)

- `Screen.fullScreenMode = FullScreenMode.{ExclusiveFullScreen, FullScreenWindow, MaximizedWindow, Windowed}`: applied at the end of the current frame. `ExclusiveFullScreen` is Windows-only in the standalone player.
- `Screen.SetResolution(int w, int h, FullScreenMode mode, RefreshRate preferredRefreshRate)`: **the only overload that takes a refresh rate**; applied at end of frame; picks the closest resolution; if the rate is not supported it picks the highest available; **the refresh rate is only honoured in ExclusiveFullScreen**. `RefreshRate` is `{ numerator, denominator }` (use the ratio from `Screen.resolutions[i].refreshRateRatio` to hit 299.97 Hz exactly).
- `Screen.resolutions` (array of `Resolution` with `width`, `height`, `refreshRateRatio`) enumerates the modes of the current monitor. Filter to the selected width x height for the refresh list; de-duplicate by rounded Hz.
- **DX12 caveat (needs a test on a high-refresh monitor):** Direct3D 12 has no true exclusive fullscreen; `IDXGISwapChain::SetFullscreenState` only changes resolution/refresh and enables fullscreen optimisations (Microsoft swap-chain docs). Unity's `PlayerSettings.useFlipModelSwapchain` (the project has it on) is documented as D3D11-only. So on this project's D3D12 the "Exclusive" option may behave as a mode-switching flip-model fullscreen. Whether `SetResolution(..., ExclusiveFullScreen, maxHz)` actually changes the monitor's refresh rate under DX12 must be confirmed empirically. If it does not, the UI should show the refresh the OS reports (`Screen.currentResolution.refreshRateRatio`) and a hint "set the refresh rate in Windows display settings".
- In Borderless (`FullScreenWindow`) the refresh rate is the desktop's; Unity cannot change it. Design decision: in Borderless the resolution row is read-only (native), and render scale takes over.
- `FramePacing` already re-evaluates VSync divisors when the refresh rate, mode or size changes, so the apply layer just calls `Screen.SetResolution` and lets `FramePacing.Update` re-resolve.

### 2.5 Renderer feature: SSAO

- PC_Renderer has exactly one feature, `ScreenSpaceAmbientOcclusion`, `Source: DepthNormals`, `Samples: 1 (Medium, 8 samples)`, `BlurQuality: 0 (High/bilateral)`, `Downsample: 0`, `Intensity 0.7`, `Radius 0.6`, `DirectLightingStrength 0.22`.
- Public: `ScriptableRendererFeature.SetActive(bool)` and `isActive` (on/off at runtime, no reflection). The feature class `ScreenSpaceAmbientOcclusion` and its `ScreenSpaceAmbientOcclusionSettings` (`Samples`, `BlurQuality`, `Downsample`, `Source`, `NormalSamples`, `AfterOpaque`, `Intensity`, `Radius`) are **internal** with `[SerializeField] internal` fields; the settings are read every frame in `AddRenderPasses`, so quality changes via reflection on `m_Settings` apply next frame. The project already uses this pattern (`SceneBuilder` editor code and `BenchmarkRunner` "nossao" experiment).
- **Danger:** `ScriptableRendererData`/features are shared ScriptableObjects. Mutating them at runtime in the Editor modifies the project asset in memory and can be saved. The applier must work on a runtime clone of the renderer data and its features (7.3).
- Alternative without reflection: ship several renderer data assets (AO Low/Medium/High/Off) in the URP asset's renderer list and switch with the public `UniversalAdditionalCameraData.SetRenderer(int)`. This also solves the shader-stripping problem (2.7). Cost: 4 near-duplicate renderer assets.

### 2.6 Volume overrides (runtime copy of the profile)

`ImageQualityFX` already instantiates the scene volume's profile (`volume.profile` returns a runtime copy) and toggles `DepthOfField.active` and `ScreenSpaceLensFlare.active`. All overrides are public classes with `VolumeParameter` fields:

| Effect | Type / parameters | Live | State in Zombies55Volume |
|---|---|---|---|
| Bloom | `Bloom`: `intensity`, `highQualityFiltering`, `downscale`, `maxIterations`, `dirtIntensity` | Yes | Present: intensity 0.38, HQ off, maxIterations 6. |
| Depth of field | `DepthOfField`: `mode` (Gaussian=1 here), `gaussianStart 70`, `gaussianEnd 190`, `gaussianMaxRadius 0.9`, `highQualitySampling` | Yes | Present, Gaussian far blur. Bokeh mode is not used. |
| Motion blur | `MotionBlur`: `mode` (CameraOnly / CameraAndObjects), `quality`, `intensity`, `clamp` | Yes | **Absent** in Zombies55Volume. Present in DefaultVolumeProfile with `intensity 0`. |
| Film grain | `FilmGrain`: `type`, `intensity` (0.1), `response` | Yes | Present. |
| Chromatic aberration | `ChromaticAberration`: `intensity` | Yes | Absent (DefaultVolumeProfile has it at 0). |
| Vignette | `Vignette`: `intensity` | Yes | Present. |
| Screen-space lens flare | `ScreenSpaceLensFlare`: `intensity`, `samples`, `sampleDimmer`, `bloomMip`... | Yes | Present. |
| SRP lens flare (sun flare) | `LensFlareComponentSRP.enabled` (and `intensity`; `ImageQualityFX` fades it with rays) | Yes | Handled by `ImageQualityFX`. Requires `asset.m_SupportDataDrivenLensFlare` (true). |
| Exposure / brightness | `ColorAdjustments.postExposure` | Yes | Present: `postExposure 0.45`, contrast 16, saturation -15. |

Post-processing bundle caveats: `m_StripUnusedPostProcessingVariants` is on in Global Settings, and `ShaderBuildPreprocessor.GetSupportedFeaturesFromVolumes` only keeps variants for overrides where `IsActive()` is true in a scanned profile. Motion blur at intensity 0 and chromatic aberration at 0 are inactive, so **their shader variants are stripped from a build**. See 2.7.

### 2.7 Shader variant stripping (the main hidden trap for runtime switching)

URP prefilters variants at build time from the URP assets listed in Quality Settings: soft shadow quality levels, SSAO sample counts, bloom HQ, motion blur, chromatic aberration, point-sampling upscale, etc. (`ShaderBuildPreprocessor.cs`; `m_PrefilterSSAOSampleCount*` in PC_RPAsset currently strip Low and High sample counts and keep Medium; `s_UseSoftShadowQualityLevelKeywords` per asset). Consequence: a runtime switch to a variant that no scanned asset requests renders as if the keyword were absent or as pink/black in a **player build, while working in the Editor**. Options:

1. (Recommended first try) Turn off "Strip Unused Variants" and "Strip Unused Post-processing Variants" in URP Global Settings (`m_StripUnusedVariants`, `m_StripUnusedPostProcessingVariants`). Build time and size grow; this project has few shaders, so it is likely cheap. Needs the user's OK because it edits project settings.
2. Keep stripping on and author the coverage: (a) renderer data assets for each SSAO quality in the URP asset's renderer list; (b) a hidden "Ultra" Volume profile in a scanned location (e.g. the Ultra URP asset's `volumeProfile`) with Motion Blur, Chromatic Aberration, Bloom HQ and DoF active; (c) per-quality URP assets in Quality Settings covering every soft-shadow level.

### 2.8 Other mappings

| Item | Mapping | Live |
|---|---|---|
| FOV | `CameraOrbit` stores `_baseFov` from `vcam.Lens.FieldOfView` once (line ~73) and lerps to aim FOV. Change it to read `GameSettings.Fov` (e.g. 55 default) each frame, scale `aimFov` by the same ratio. `SurvivalHud` reads `_cam.fieldOfView`, so the HUD follows. | Live |
| Reflection probe update/resolution | Probes are baked (`ReflectionProbeMode.Baked`, `BuildReflectionProbes`); `QualitySettings.realtimeReflectionProbes` is off. Nothing to expose. A resolution change needs a re-bake. | No |
| Decals | The project uses alpha-blended **mesh decals** (merged per block; `DecalMat`), not URP Decal projectors; PC_Renderer has no Decal feature. No runtime knob; "decal density" is a Street Detail toggle (3.5). | n/a |
| Shadow casting of small objects | URP's `shadowSmallMeshScreenPercentages` only affects the GPU Resident Drawer (off). Public alternatives: `Light.layerShadowCullDistances` (per-layer shadow distance on the sun, needs props on their own layer) or `Renderer.shadowCastingMode` per group. Several density groups are already shadowless (`NoShadows`: Signs, Steam, CrossCables, VaultGrates). | Live |
| Per-layer render distance | `Camera.layerCullDistances` (float[32]) with `layerCullSpherical`: native, cheap culling of far clutter, needs a clutter layer. | Live |
| Light count | Deferred has no per-object limit. Game side: `CombatFX` creates a point "BlastLight"; scale `range` / `shadows` from the setting. | Live |
| Occlusion culling | Baked Umbra (`BakeOcclusion`). Always on; not a setting. | n/a |
| Fog | `RenderSettings.fog` exponential 0.0036: part of the look. Not exposed. Volumetric fog is not used. | n/a |
| Frame timing | `FrameTimingManager.IsFeatureEnabled()`, `CaptureFrameTimings()`, `GetLatestTimings(n, FrameTiming[])` (CPU main, CPU render, GPU ms; four-frame delay). In release players it requires **Player Settings > Frame Timing Stats** (`enableFrameTimingStats: 0` now), **or** an attached `ProfilerRecorder` ("attaching a ProfilerRecorder enables FrameTimingManager automatically, even in release builds", docs.unity.com 6000.6). Development players always have it. | n/a |

---

## 3. Current project state

### 3.1 Pipeline and quality assets

- `ProjectSettings/QualitySettings.asset`: two levels, **Mobile** (excluded from Standalone, uses Mobile_RPAsset) and **PC** (current, default for Standalone, `customRenderPipeline` = PC_RPAsset guid 4b83...). PC level: `shadowDistance 40` (ignored by URP, which uses the asset), `lodBias 2`, `skinWeights 4`, `anisotropicTextures 2` (ForceEnable), `realtimeReflectionProbes 0`, `softParticles 0`, `particleRaycastBudget 256`, `vSyncCount 1`, `enableLODCrossFade 1`.
- `Assets/Settings/PC_RPAsset.asset`: Deferred (renderer's `m_RenderingMode: 2`), HDR on 32-bit, MSAA 1 (off), render scale 1.0, upscaler Auto, main shadow 4096 / 4 cascades / distance 140 / soft quality High, additional lights per-pixel with 2048 shadow atlas, depth and opaque texture both on, LOD cross-fade on (blue-noise dithering), SRP batcher on, lens flare (data-driven and screen-space) supported, Adaptive Performance flag on (`m_UseAdaptivePerformance: 1`; verify it is not active in the desktop build), volume profile `DefaultVolumeProfile`-family asset.
- `PC_Renderer.asset`: one feature (SSAO as above), depth priming off, deferred, accurate G-buffer normals off.
- `Mobile_RPAsset.asset`: render scale 0.8, shadow distance 50, 1 cascade, Forward. Not reachable on Standalone.
- `Zombies55Volume.asset`: WhiteBalance, ScreenSpaceLensFlare, SplitToning, Tonemapping, ShadowsMidtonesHighlights, Bloom, DepthOfField (Gaussian 70-190), Vignette, FilmGrain (0.1), ColorAdjustments (post exposure 0.45). No motion blur or chromatic aberration.
- Camera (`SceneBuilder.BuildCamera`): near 0.1, far 400, `renderPostProcessing`, **TAA High**, CAS 0.35, base blend 0.97, jitter 0.5; Cinemachine FOV 55; Third-person follow with obstacle avoidance. Fog: exponential 0.0036 (SceneBuilder.cs:296).

### 3.2 Settings code today

- `GameSettings` (static class, `PlayerPrefs`, prefix `Zombies55.Settings.`): `SettingId` enum with a parallel `Defs[]` array (`Min, Max, Default`), values held as floats, `Changed` event, `Reset()`, `Format(id)`. `Clamp()` decides "integer-ness" using hard-coded index ranges, which will not scale to new ids (replace with a per-Def flag, see 7.1). Existing graphics ids: `DepthOfField`, `LensFlare`, `FrameRate` (index 0..3 into `{30,60,90,120}`, default 3), `VSync`, `FpsCounter`.
- `ImageQualityFX`: applies `DepthOfField` and `LensFlare` to the runtime-copied scene volume and the sun's SRP flare; fades the flare by raycasts. Subscribes to `GameSettings.Changed`.
- `FramePacing`: hidden persistent object (BeforeSceneLoad). `Resolve(refresh, cap, vsync, ...)` picks `vSyncCount` or `targetFrameRate`; re-evaluates on refresh/mode/size change; `ForceUncapped` for the benchmark; FPS counter via `OnGUI`. On a high-refresh monitor, cap 120 maps to no VSync divisor (e.g. 165/120 = 1.375) so it uses `targetFrameRate=120` with VSync off and can tear (documented in code).
- `SettingsMenu` + rows: tabs CONTROLS / CROSSHAIR / AUDIO / GRAPHICS built by `ShellBuilder.BuildSettings` with **absolute-positioned rows** on a 1560x940 card (rows at y = 190, 100, 10, -80, -170 in the Graphics tab, height 70, width 1200). Row components `SettingsSliderRow`, `SettingsStepperRow` (prev/next, also Left/Right/Submit), `SettingsToggleRow`, each bound to a `SettingId`. `OptionCount(id)` is hard-coded to three cases. No scrolling, no description line. `GameShell.OpenSettings` is allowed from Ready and Paused only; Esc/B handled in `GameShell`; LB/RB switch tabs.
- `BenchmarkRunner` (`-benchmark [-benchmarkSeconds N]`): at `AfterSceneLoad`, sets `FramePacing.ForceUncapped`, `PlayerHealth.GodMode`, `RoundManager.BenchmarkMode` (48 alive, `BenchmarkRound`), `PlayerController.ScriptedInput`, `CameraOrbit.ScriptedLook/Pitch/Yaw`, `ZoneManager.UnlockAll()`, `GameLoop.StartRun()`, warms up 5 s, measures N s (default 60) along an 8-waypoint loop of the city (57/19 unit grid), records frame times (`Time.unscaledDeltaTime`), 1% / 0.1% low as "mean fps of the slowest 1% / 0.1% of frames", percentiles, frames over 8.3/16.6 ms, per-second lines, slowest frames with location, optional FrameTimingManager CPU/GPU averages **only if the feature is enabled**, writes `benchmark.json`, then `Application.Quit()` (editor path resets flags and destroys itself). `-bn=` experiment flags (noshadow, notaa, nossao, ...) mutate `GraphicsSettings.currentRenderPipeline` **directly**, i.e. the project asset (in the Editor this can persist; repoint to the runtime clone).
- `GameLoop`: single scene `Assets/Scenes/Zombies55.unity`; `Restart()` / `ReturnToTitle()` reload the scene; `s_StartImmediately` skips the title.

### 3.3 Baseline numbers (from the task brief)

Development PC: ~260 fps average, 1% low ~129, CPU main-thread bound with 48 enemies. An earlier editor-windowed `benchmark.json` agrees in shape (main thread ~10 ms vs GPU ~1.2 ms). So on the developer's PC, GPU-only settings (render scale, AA, AO, shadow resolution) change almost nothing; CPU-side settings (cascades, shadow distance, object detail, street detail, particles, character IK) change the result. On modest GPUs the opposite is true, which is why both classes are in the list and the cost column says which.

### 3.4 Scene systems relevant to settings

- Street detail is **baked into the scene by editor tools** and merged into per-block meshes: Density (vault grates, steam, A-frame/blade signs, cardboard, bins, bags, puddles, cross-street cables; root `Density`, groups `Signs`, `Steam`, `CrossCables`, `VaultGrates`, `Litter`; spots chosen by `DensitySpotFree`), Dirt (oil, patches, cracks, gutter litter and gum; root `Dirt`, `TrashBags`), StreetNoise (posters, tags, stickers, tufts, door litter; root `StreetNoise`, "NoiseDecals" meshes), road grime and markings (`RoadGrime`, `RoadMarkingsV2`, `SideAds`). All are non-colliding shadowless decals or cards unless noted (props like bins/dumpsters have colliders; check before disabling).
- There is no density parameter at runtime. Runtime options are therefore group activation by distance or on/off, not procedural density.
- Parked cars: 3 LODs with `CarLod1Dist 12 / CarLod2Dist 35 / CarCullDist 150`; LOD2 casts no shadow. Fences: near cutout LOD plus far blended LOD that casts no shadow, with cross-fade. Buildings: far welded meshes (`MergeBuildingRenderers`). Umbra occlusion baked. Trees, lamps, poles exist under `Trees`, `Lamps`, `UtilityPoles`, `Wires`.
- Particles come from `CombatFX`, `ExplosiveProp`, `EnemyEffects`, `FeelFX`, `PickupDirector`, `ZoneManager` (all create or play `ParticleSystem`s); `CombatFX` owns the blast point light.
- Characters: `KneeAlign.Disabled` (static bool) already exists as a CPU switch for leg IK; 48 skinned enemies with Animators.

### 3.5 Game-specific settings worth exposing

1. **Street Detail** (clutter/decals/litter/steam): the closest analogue of GTA V population density and Watch Dogs 2 "Extra Details"; biggest CPU/draw-call lever after shadows.
2. **Particle & Explosion Detail**: scales emission and max particles, explosion light shadows, particle raycast budget.
3. **Character Detail**: skin weights and the existing `KneeAlign` IK switch; 48 skinned enemies.
4. **Shadow Distance + Cascades** (as part of the Shadow quality composite): the enemies are drawn once per cascade, so this scales both CPU and GPU.
5. Not exposed: fog, occlusion culling, reflection probes, decal projector quality, MSAA, LOD cross-fade.

---

## 4. Proposed settings list

26 rows. Estimated cost columns: **GPU** and **CPU** each low / medium / high, with game-specific reasoning (the project is CPU-main-thread bound on the dev PC; most GPU costs are estimates scaled from the NVIDIA numbers in 1.3, to be replaced by measurements; see 8.3). "Live" means no restart and no loading; "Live*" means live with a one-frame or short hitch.

Defaults are the **High** column of the preset table (today's look), except display options.

### 4.1 Display

| # | Setting | Options (default) | Cost | Unity implementation | Live? | Performance note |
|---|---|---|---|---|---|---|
| 1 | Display Mode | Windowed / Borderless / Exclusive (default Borderless = `FullScreenWindow`) | none | `Screen.fullScreenMode`; via `Screen.SetResolution(w, h, mode, rate)` when resolution changes too | Live, applied end of frame, confirm dialog | Exclusive is the only mode that honours refresh rate; on D3D12 it may not be true exclusive (2.4). Persisted separately from presets. |
| 2 | Resolution | list from `Screen.resolutions` (default: desktop native). Read-only in Borderless | GPU high at high res, CPU none | `Screen.SetResolution(w, h, mode, new RefreshRate{...})` | Live, 15 s revert confirm | Store width and height prefs; if the stored mode vanishes (monitor change) fall back to native. |
| 3 | Refresh Rate | list of Hz available at the chosen resolution; Exclusive only (default: current) | none | the `RefreshRate` argument of `SetResolution`, values from `Screen.resolutions[i].refreshRateRatio` | Live, applied with row 2 | Greyed with hint in Borderless/Windowed. Needed on a high-refresh monitor so VSync divisors can reach 150/100/75 (`FramePacing.Resolve` allows n = 2..4). Confirm on DX12. |
| 4 | VSync | On / Off (default On) | none | existing `FramePacing` | Live | Unchanged. On a 165 Hz display with cap 120 it uses cap-only mode (can tear), which the UI should flag using `FramePacing.CanTear`. |
| 5 | Frame Rate Cap | 30, 60, 90, 120, 144, 165, 240, 300, Unlimited (default 120, as today) | none | extend `GameSettings.FrameRateValues`; keep indexes 0-3 stable for saved prefs; `0` = Unlimited (`targetFrameRate = -1`, `vSyncCount 0`) | Live | `FramePacing.Resolve` takes `cap == 0` as "unlimited" when VSync off, "every refresh" when VSync on. |
| 6 | Field of View | 45-80 deg, step 1 (default 55) | CPU low-medium (wider view = more visible objects/shadow casters), GPU low | `CameraOrbit` reads `GameSettings.Fov` instead of the one-time `_baseFov`; aim FOV scales proportionally | Live | At FOV 80 culling and shadow draws rise measurably; verify with the benchmark. |
| 7 | Brightness | -1.0 to +1.0 EV, step 0.05 (default 0) | none | runtime copy of the volume profile: `colorAdjustments.postExposure.value = 0.45f + brightness` in `ImageQualityFX` (keep the authored 0.45 as the reference) | Live | Do not use `Screen.brightness` (mobile only). |
| 8 | FPS Counter | Off / On (existing) | none | existing `FramePacing` OnGUI | Live | Optionally add a frame-time sparkline later. |

### 4.2 Quality preset

| # | Setting | Options (default) | Unity implementation | Live? |
|---|---|---|---|---|
| 9 | Quality Preset | Low / Medium / High / Ultra / Custom (default High on upgrade; first-run auto-pick: see open decisions) | `GameSettings.ApplyPreset(i)` writes all preset-controlled ids in one batch (suppress `Changed` until done, then fire once). Any later change to a preset-controlled id sets the preset to Custom (value 4). | Live* (applier may hitch once: shadow map and texture mip changes) |

### 4.3 Rendering

| # | Setting | Options | Cost | Unity implementation | Live? | Performance note |
|---|---|---|---|---|---|---|
| 10 | Render Scale | 50 / 67 / 75 / 85 / 100 / 125 % (High: 100) | GPU high (linear in pixels); CPU none | `asset.renderScale = x` on the runtime clone | Live | Pure GPU lever, no gain on this CPU-bound dev PC. 125% supersamples (Ultra). |
| 11 | Upscaler | Off (Auto) / FSR 1.0 / STP (default Off at 100%) | GPU low-medium; CPU none | `asset.upscalingFilter = Auto / FSR / STP`; STP also forces TAA (row 12). Upscaler row is disabled when render scale >= 100%. | Live | FSR1 is a cheap spatial filter, best at >= 67%. STP is better quality but needs TAA and compute; if AA != TAA the applier maps STP to FSR. Optional "Sharpness" (`fsrSharpness`) only if testing shows softness. |
| 12 | Anti-Aliasing | Off / FXAA / SMAA / TAA (default TAA High) | GPU low (FXAA) to medium (TAA/STP); CPU none; TAA also needs object motion vectors (CPU/GPU cost on 48 skinned enemies, already paid today) | `camData.antialiasing`, `camData.antialiasingQuality` (Low/Med/High); keep SceneBuilder's `taaSettings` for TAA | Live | MSAA is deliberately absent (Deferred does not support it). Keep the existing TAA tuning at all levels. Show a 4th level "TAA (High)" vs "TAA (Medium)" via quality only if needed. |
| 13 | Shadow Quality | Low / Medium / High / Ultra | GPU medium-high; **CPU high** (each cascade re-draws visible casters; 48 enemies + parked cars) | composite: `asset.mainLightShadowmapResolution` (1024/2048/4096/8192), `asset.shadowCascadeCount` (2/3/4/4), sun `UniversalAdditionalLightData.softShadowQuality` (Low/Medium/High/High), `asset.additionalLightsShadowmapResolution` (1024/1024/2048/4096) and `CombatFX` blast light shadows (off/off/on/on) | Live* (shadow map realloc hitch) | Biggest quality-for-speed trade in every game surveyed. Never set cascades to 1 (variant stripping). Ultra 8192 costs 128 MB VRAM. |
| 14 | Ambient Occlusion | Off / Low / Medium / High (default High = today) | GPU low-medium; CPU none | SSAO feature on a runtime-cloned renderer: `SetActive(bool)` for Off; reflection on `m_Settings` for Low (`Samples=Low`, `Downsample=true`, `BlurQuality=Low`), Medium (`Samples=Low`, `Downsample=false`, `BlurQuality=Medium`), High (today: `Samples=Medium`, `BlurQuality=High`) | Live | 1-5 fps in the games measured. Needs variant coverage (2.7). Small props rely on SSAO for contact shadows (Density.cs comment), so Off is visibly flatter. |
| 15 | Texture Quality | Low (half res) / High (full res) (default High) | GPU memory; fps ~0 | `QualitySettings.globalTextureMipmapLimit = 1 / 0` | Live* (re-uploads textures, hitch) | VRAM relief for 4-6 GB cards. Apply only from the menu, never mid-wave; the benchmark launch applies it before loading. |
| 16 | Object Detail (LOD) | Low / Medium / High / Ultra | CPU medium (more distant LOD0 draws and triangles), GPU low-medium | `QualitySettings.lodBias` = 1.0 / 1.5 / 2.0 / 3.0 (today 2.0) | Live | Parked cars use custom LOD distances that this bias scales. Fence far LODs remain cross-faded. |

### 4.4 Post-processing

| # | Setting | Options | Cost | Unity implementation | Live? | Performance note |
|---|---|---|---|---|---|---|
| 17 | Bloom | Off / On / High Quality (default On) | GPU low (<1 fps in WD2); HQ medium | `bloom.active`, `bloom.highQualityFiltering.value`, `bloom.maxIterations` (4 / 6 / 8) on the runtime volume profile | Live | HQ filtering variants must survive stripping (2.7). |
| 18 | Depth of Field | Off / On (existing; default On) | GPU low-medium | existing `ImageQualityFX` (`DepthOfField.active`); Gaussian far blur, `highQualitySampling` false stays | Live | Existing row; keep. |
| 19 | Motion Blur | Off / Camera / Camera+Objects (default Off, not in today's look) | GPU low (camera) to medium (objects); CPU none | `MotionBlur` override on the runtime profile: `mode`, `intensity` 0.5 camera-only, `quality` Low | Live | Absent from Zombies55Volume today: author it (inactive) plus variant coverage (2.7) or add at runtime with `profile.Add<MotionBlur>(true)`. |
| 20 | Lens Flare | Off / On (existing; default On) | GPU low (Div2: "a few fps") | existing `ImageQualityFX` (screen-space flare + sun SRP flare) | Live | Existing row. |
| 21 | Film Grain | Off / On (default On) | GPU very low | `FilmGrain.active` on the runtime profile (intensity stays 0.1) | Live | Pure taste; Low preset turns it off. |

### 4.5 Advanced

| # | Setting | Options | Cost | Unity implementation | Live? | Performance note |
|---|---|---|---|---|---|---|
| 22 | Shadow Distance | 40 / 70 / 100 / 140 / 200 m (default 140) | GPU low-medium; **CPU high** (more casters submitted to every cascade) | `asset.shadowDistance` | Live | Top CPU lever in this game. Camera far clip is 400 so 200 is fine. |
| 23 | Street Detail | Low / Medium / High / Ultra (High = today) | **CPU medium-high** (draw calls, culling), GPU low | new `StreetDetail` component: (a) group on/off for Dirt, StreetNoise, TrashBags, Litter and Density sub-groups with no colliders; (b) distance cull via `Camera.layerCullDistances` if SceneBuilder assigns a "Clutter" layer, otherwise a timer-based `Renderer.enabled` pass by block distance | Live | Cost estimate: the dev benchmark's draw-call and batch counters (`-benchmarkMarkers`, per-second stats) should be recorded per level to fill the real number. Ultra = nothing hidden plus extra far distance for decals. Not procedural density: scene is baked. |
| 24 | Particle & Explosion Detail | Low / Medium / High / Ultra | CPU medium during fights (particle sim on main thread), GPU medium (overdraw) | `ParticleDetail` multiplier (0.35 / 0.65 / 1.0 / 1.25) applied where systems are created or played (`CombatFX`, `ExplosiveProp`, `EnemyEffects`, `FeelFX`, `PickupDirector`, `ZoneManager`): `emission.rateOverTimeMultiplier`, `main.maxParticles`, `burst` counts; plus `QualitySettings.particleRaycastBudget` (16 / 64 / 256 / 512) | Live (next spawn) | GTA V: up to 6-10 fps in particle-heavy scenes (1.3). Keep gameplay-critical telegraphs (explosion warning, hit confirm) at 1.0 regardless. |
| 25 | Character Detail | Low / Medium / High / Ultra | CPU medium (IK pass for 48 enemies), GPU low (skinning) | `QualitySettings.skinWeights` (OneBone... Low=TwoBones, Medium=TwoBones, High/Ultra=FourBones) and `KneeAlign.Disabled = (level == Low)` | Live | KneeAlign is already a CPU toggle used by the benchmark experiments. |
| 26 | Anisotropic Filtering | Off / 2x / 4x / 8x / 16x (default 8x, calibrate to today) | GPU very low (0.6-2 fps in guides) | `QualitySettings.anisotropicFiltering` + `Texture.SetGlobalAnisotropicFilteringLimits(n, n)` | Live | Near-free; grouped under Advanced because nobody should need to lower it. |

Not exposed on purpose: MSAA (unsupported in Deferred), LOD cross-fade, reflection quality (baked probes), volumetric fog (unused), decal projectors (not used), additional lights per object (no effect in Deferred), HDR on/off (tonemapping needs it), opaque texture (not a player choice, turn off in the asset).

---

## 5. Preset table

Display and frame-pacing settings (1-8) are outside presets. Values refer to the rows in section 4. "High" reproduces today's look, so existing players see no change after the update. Targets: Low runs on a modest GPU (about GTX 1650 / RX 6500 class) and a 4-core CPU; Ultra goes beyond today.

| # | Setting | Low | Medium | High (today) | Ultra |
|---|---|---|---|---|---|
| 10 | Render Scale | 67% | 85% | 100% | 125% |
| 11 | Upscaler | FSR 1.0 | STP | Off | Off |
| 12 | Anti-Aliasing | FXAA | TAA (Medium quality; with STP) | TAA High (CAS 0.35) | TAA High (CAS 0.35) |
| 13 | Shadow Quality | Low: 1024, 2 cascades, soft Low, add-light atlas 1024, blast shadows off | Medium: 2048, 3 cascades, soft Medium, 1024, off | High: 4096, 4 cascades, soft High, 2048, on | Ultra: 8192, 4 cascades, soft High, 4096, on |
| 22 | Shadow Distance | 40 m | 70 m | 140 m | 200 m |
| 14 | Ambient Occlusion | Low (4 samples, downsampled, Kawase blur) | Medium (4 samples, Gaussian) | High (8 samples, bilateral; today) | High+ (12 samples `Samples=High`, `NormalSamples=High`, bilateral) |
| 15 | Texture Quality | Low (half res) | High | High | High |
| 16 | Object Detail (`lodBias`) | 1.0 | 1.5 | 2.0 | 3.0 |
| 26 | Anisotropic | 2x | 4x | 8x (calibrate) | 16x |
| 17 | Bloom | On, 4 iterations | On, 6 | On, 6 (today) | HQ, 8 |
| 18 | Depth of Field | Off | On | On | On |
| 19 | Motion Blur | Off | Off | Off | Camera, intensity 0.5 |
| 20 | Lens Flare | Off | On | On | On |
| 21 | Film Grain | Off | Off | On | On |
| 23 | Street Detail | Low: hide StreetNoise, Dirt decals, litter/bag piles; cull remaining clutter beyond 25 m | Medium: hide StreetNoise tufts and door litter; clutter cull 40 m | High: everything (today) | Ultra: everything, clutter cull 120 m (beyond today) |
| 24 | Particle & Explosion | 0.35, raycast 16 | 0.65, 64 | 1.0, 256 | 1.25, 512 |
| 25 | Character Detail | 2 bones, KneeAlign off | 2 bones, KneeAlign on | 4 bones, KneeAlign on | 4 bones, KneeAlign on |
| - | Not player-visible (asset defaults, all presets) | `supportsCameraOpaqueTexture=false` (verify, 2.1), HDR 32-bit | same | same | HDR 64-bit |

Custom: when the player changes any row 10-26 after choosing a preset, the label becomes "Custom" and all values are kept (they are always stored individually). "Reset to defaults" in the existing Reset button sets the preset to High and all rows to the High column.

Costs of "Low" on this game are expected to come mostly from: render scale + FSR (GPU), shadow cascades 4 to 2 and distance 140 to 40 (CPU render thread and GPU), Street Detail Low (draw calls) and KneeAlign off (main thread). Fill the final fps numbers from the benchmark (8.3).

---

## 6. In-game benchmark design

### 6.1 Entry point and options

A new fifth settings tab **BENCHMARK** (tabs become CONTROLS, CROSSHAIR, AUDIO, GRAPHICS, BENCHMARK; 5 x 285 px at 305 px pitch fits the 1560 px card), with:

- Header: current settings summary (preset name, resolution, mode, refresh, render scale, upscaler) so the player sees what will be measured.
- Options: Duration (30 s / 60 s, default 60 s), "Ignore frame cap and VSync" (default On; sets `FramePacing.ForceUncapped`, already supported), "Save result to history" (On).
- Buttons: RUN BENCHMARK, VIEW LAST RESULT, and a history list (up to 20 rows: date, preset, avg/1% low, resolution), each openable, with COMPARE.
- The row is also reachable from the Graphics tab bottom ("RUN BENCHMARK ->" button that switches tabs).
- Allowed from Ready (title) and Paused only (same rule as Settings). If paused mid-run, the benchmark warns that the current run will be abandoned.

### 6.2 What it runs

Reuse `BenchmarkRunner` unchanged in content: every district unlocked (`ZoneManager.UnlockAll`), 48 enemies (`RoundManager.BenchmarkMode`), `PlayerHealth.GodMode`, scripted player loop through the 8 waypoints, `CameraOrbit.ScriptedLook`, fixed `Random.InitState(12345)`, 5 s warm-up then N s measure. Refactor to:

```csharp
public struct BenchmarkOptions { public float Seconds; public bool Uncapped; public bool QuitWhenDone; public bool SaveHistory; }
public static void BeginFromMenu(BenchmarkOptions o);   // used by the menu
public static event Action<BenchmarkResult> Finished;   // GameShell shows the results screen
public static void Cancel();
```

The `-benchmark` command-line path remains: same code, `QuitWhenDone = true`, still writes `benchmark.json` (extended with the new fields), and uses whatever settings are in PlayerPrefs. Menu runs use **the player's current settings**, with no overrides except optionally the frame cap.

### 6.3 Loading and return flow

1. Menu: RUN BENCHMARK -> confirm dialog ("Takes 70 s. The screen will show the game from a fixed route. Esc cancels. Frame cap is ignored.").
2. Apply any pending display-mode change first (before loading), then call `GraphicsApplier.ApplyAll()` so the run measures the saved state.
3. `BenchmarkRunner.BeginFromMenu`: creates the persistent runner (as now: `DontDestroyOnLoad`), sets a static `GameShell.BenchmarkActive = true`, shows a "LOADING" overlay, and calls `SceneManager.LoadSceneAsync` of the active scene (fresh enemies, zones, round state; the same reload `GameLoop.ReturnToTitle` uses). Statics set by the runner (`RoundManager.BenchmarkMode`, `PlayerController.ScriptedInput`, `FramePacing.ForceUncapped`) survive scene reload because the runner owns them; `RuntimeInitializeOnLoadMethod` hooks do not re-fire on reload, which is why the runner must be started explicitly rather than relying on `Boot()`.
4. After `sceneLoaded`, wait for the singletons (`GameLoop`, `ZoneManager`, `PlayerHealth`, `RoundManager`) as the current `Start()` does, run `StartRun()`.
5. While running: `GameShell` keeps title, HUD, pause and end roots disabled (`BenchmarkActive`), the cursor is hidden, and only a one-line progress label ("BENCHMARK 23 / 60 s  -  Esc to cancel") is visible, updated twice per second. This is a TextMeshPro label on the existing canvas; no IMGUI. The FPS counter is forced off during the measure window.
6. On finish: stop recorders, compute the result, restore all flags (the editor-only cleanup block becomes unconditional: `GodMode`, `BenchmarkMode`, `ScriptedInput`, `ScriptedLook`, `ForceUncapped`, `FramePacing.Apply()`), save history, set a static `BenchmarkResults.Pending = result`, then `GameLoop.ReturnToTitle()` (scene reload to the title). `GameShell.Start` sees `Pending` and opens Settings > BENCHMARK > Results.
7. Cancel (Esc/B at any time, or window focus lost for more than 2 s, or resolution/mode change during the run): discard, same restore path, toast "Benchmark cancelled".

### 6.4 Keeping the measurement honest

- **Menu closed:** the run happens after a scene reload, in `Running` state with all menus disabled; `Time.timeScale` is 1. The only UI is the progress label. The FPS overlay and any settings-menu object are disabled.
- **Shader warm-up and first-use hitches:** (a) measure only after the warm-up (extend 5 s to 8 s so the route passes through two districts; the existing 5 s is kept for the CLI default); (b) discard any frame in the first 2 s after warm-up end if its time is over 5x the running median (counted as "warm-up hitches" and shown, not folded into the lows); (c) recommended, to verify: pre-warm graphics pipeline state objects at scene load with a `ShaderVariantCollection.WarmUp()` or `GraphicsStateCollection` if the project has one (Unity 6 D3D12 PSO compilation is the usual source of the first-lap hitches); (d) the "load" frames and the first frame after reload are never in the sample list because recording starts after warm-up.
- **Determinism:** `Random.InitState(12345)`, the same scripted loop and enemy count; the player warps to the next waypoint when blocked 2 s (existing logic). Two runs with the same settings should land within about 3% on average fps; show a stability line if a rerun differs by more.
- **Cost of measuring:** record `dt` into a pre-sized `List<float>` (capacity 1<<16, as today) and do all aggregation after the loop. No per-frame `FindObjectsByType` (the existing runner does one every 10th frame for enemy stats; keep that only under `-benchmarkMarkers`, not in menu runs). Disable the per-frame profiler recorders (the render-stat counters only exist in development builds anyway).
- **Focus:** if `Application.isFocused` is false for over 2 s, mark the run invalid.

### 6.5 Metrics: with and without Frame Timing Stats

Without any Player setting change (always available, in release players):

- Frame times from `Time.unscaledDeltaTime`: average fps, minimum fps (max frame time), maximum fps (min frame time), 1% low and 0.1% low (existing definition "mean fps of the slowest 1% / 0.1% of frames"; label the definition in the UI), frame-time percentiles p50/p95/p99, frames over 8.3 / 16.7 / 33 ms, hitch count (frames over 3x the median and over 25 ms), per-second min/avg/max, the frame-time graph, GC collection count during the run, `GC.GetTotalMemory` growth.
- System info: `SystemInfo.graphicsDeviceName`, `graphicsMemorySize`, `graphicsDeviceType` (API), `processorType`, `processorCount`, `systemMemorySize`, `operatingSystem`, `Screen.currentResolution` with refresh, mode, render resolution after scale.
- **CPU-vs-GPU verdict without FrameTimingManager:** use a **bottleneck probe** after the main run (optional, 2 x 8 s): repeat the same route segment at the current render scale and at 60% of it; compare average fps. Less than 5% gain means CPU-bound; more than 25% gain means GPU-bound; in between, balanced. (The crowd is not bit-identical between segments, so treat as an indication.) Optional extra signal, unverified in release players: a `ProfilerRecorder` on the `Main Thread` Internal-category marker gives main-thread time per frame; a main-thread time close to the frame time points at a CPU-main bound.

With Frame Timing enabled (Player setting **Frame Timing Stats** on, or a `ProfilerRecorder` attached per the 6000.6 docs, or a development player), `BenchmarkRunner` already collects via `FrameTimingManager.GetLatestTimings`:

- CPU main-thread ms, CPU render-thread ms, GPU ms per frame (averages and p95), with the four-frame delay handled by aligning on frame count (use averages rather than per-frame pairing).
- A proper verdict: GPU-bound if gpu ms >= 90% of frame ms; CPU-main-bound if main ms >= 90% of frame ms and gpu < 70%; render-thread-bound if render ms leads; otherwise balanced. Percent bars "GPU 38% / CPU main 96% / render 55% of frame time" like Shadow of the Tomb Raider.
- GPU memory timers via `FrameTimingManager.GetGpuTimerFrequency()` are unnecessary; GPU time is already in ms.
- Implementation detail to verify before relying on it: the docs say attaching a `ProfilerRecorder` enables `FrameTimingManager` in release builds. The marker names to attach to are not stated on that page; enumerate with `ProfilerRecorderHandle.GetAvailable` in a development player to find them, then confirm in a release build that `FrameTimingManager.IsFeatureEnabled()` flips to true. If it does not, the choice is the Player setting (open decision 1). Until verified, the design ships with the probe-based verdict and upgrades itself automatically when `IsFeatureEnabled()` is true (existing code path).

### 6.6 Results screen

Layout (inside the BENCHMARK tab, replaces the run controls when a result is shown):

- Left column, big numbers: **AVG fps**, **1% LOW**, **0.1% LOW**, **MIN**, **MAX** (fps), plus avg frame time ms and hitch count.
- Centre: **frame-time graph** over the 60 s run: line chart of frame time (ms) downsampled to 240 buckets, each bucket showing the max frame time of its frames (so spikes are not averaged away), with horizontal guide lines for 16.7 ms (60 fps), 8.3 ms (120 fps) and the target of the monitor refresh (e.g. 6.1 ms for 165 Hz), shaded band for p1-p99. Drawn with a `Texture2D` line plot or a uGUI `Graphic` mesh, built once on show.
- Right column: **settings used** (preset name plus the 18 values, compact two-column list), **system** (CPU, GPU, VRAM, RAM, API, OS), **display** (resolution, render resolution, mode, refresh, VSync/cap state during the run, "uncapped").
- Bottom: **verdict line** (see 6.7) and buttons: COMPARE, SAVE, RUN AGAIN, BACK.
- Optional CPU/GPU/render-thread bars when Frame Timing is available.

### 6.7 Verdict line

Single sentence, generated from avg, 1% low, 0.1% low, hitches and the bottleneck result:

| Condition (checked top to bottom) | Verdict |
|---|---|
| run invalid (focus lost, hitches > N from warm-up) | "Result may be unreliable: the window lost focus or the system was busy." |
| 0.1% low < 35% of avg or hitch count > 20 | "Smooth on average but with stutters (0.1% low NN fps). Try reducing Shadow Quality or Street Detail." |
| 1% low >= refresh rate x 0.9 | "Locked to your monitor's refresh rate." |
| avg >= 144 and 1% low >= 90 | "Excellent. Handles a 144 Hz display." |
| avg >= 90 and 1% low >= 60 | "Very smooth (90+ fps)." |
| avg >= 60 and 1% low >= 45 | "Smooth at 60 fps." |
| avg >= 45 | "Playable; consider the Medium preset or lower render scale." |
| otherwise | "Below 45 fps; try the Low preset." |

Append the bottleneck sentence: "Limited by your CPU: lowering resolution will not help; reduce Shadow Distance, Street Detail or Character Detail." or "Limited by your GPU: lower Render Scale or Shadow Quality." Add "Results are from the heaviest scenario (all districts open, 48 enemies); typical play is lighter."

### 6.8 History and comparison

- Storage: `Application.persistentDataPath/benchmark_history.json` (JsonUtility, a `List<BenchmarkResult>`; not PlayerPrefs because of size). Keep the latest 20, newest first; write to a temp file and move over the old one; tolerate a corrupt file by renaming it to `.bak` and starting empty. `BenchmarkResult` stores everything needed to re-display: summary numbers, 240-bucket graph, settings snapshot (id -> value plus preset name), system info, scenario version string (so changes to the route or enemy count mark older runs "not comparable"), UTC timestamp, game version (`Application.version`), run options.
- Compare: from the results screen COMPARE opens a picker (default: the previous run). Show a delta table (AVG, 1% low, 0.1% low, MIN, MAX, avg ms; with signed percent and green/red), a **settings diff** (only settings whose values differ, e.g. "Shadow Quality: High -> Medium"), and an **overlaid** frame-time graph (the route is time-deterministic, so graphs align by time; current run solid, other dashed).
- Warn when scenario versions or resolutions differ. Allow deleting entries.
- The result is still written to `benchmark.json` when run from the command line, and optionally copied to clipboard as text from the results screen ("COPY") for sharing.

---

## 7. Implementation plan

### 7.1 Data model (extend `GameSettings`)

Keep the style: static class, floats, PlayerPrefs under `Zombies55.Settings.`, `Changed` event, `Defs[]` parallel to `SettingId`. Changes:

1. Replace the hard-coded index ranges in `Clamp()` with an `Integer` flag in `Def` (constructor parameter), because the new ids make the ranges unmaintainable and the current ranges rely on the order of ids.
2. **Append** new ids after `FpsCounter` (never reorder; saved prefs are keyed by name, but the arrays are by index). Suggested new ids (all float-stored): `DisplayMode, ResolutionW, ResolutionH, RefreshRate, Fov, Brightness, QualityPreset, RenderScale, Upscaler, AntiAliasing, ShadowQuality, ShadowDistance, AmbientOcclusion, TextureQuality, ObjectDetail, Bloom, MotionBlur, FilmGrain, StreetDetail, ParticleDetail, CharacterDetail, Anisotropic`. Existing `DepthOfField`, `LensFlare`, `FrameRate`, `VSync`, `FpsCounter` keep their ids. Convert `DepthOfField`/`LensFlare` from toggles unchanged.
3. `FrameRateValues` becomes `{30, 60, 90, 120, 144, 165, 240, 300, 0}` (0 = unlimited); keep `Def` default index 3; widen `Max` to 8; update `FramePacing.Resolve` for `cap == 0`.
4. Option tables for steppers: replace `OptionCount(id)` with a table `static readonly string[][] OptionNames` keyed by id (plus `GameSettings.OptionCount(id)` reading it). Dynamic lists (resolution, refresh) get `GraphicsDisplayOptions.Resolutions()` and a `GetOptions(id)` hook the stepper calls.
5. Preset support: `static readonly int[][] PresetValues` (Low/Medium/High/Ultra x preset-controlled ids from the table in section 5). `ApplyPreset(int i)` sets all with a re-entrancy guard (`_applyingPreset = true`), saves, raises `Changed` once. `SetFloat` on a preset-controlled id while `!_applyingPreset` sets `QualityPreset` to 4 (Custom). A `MatchesPreset()` helper re-detects an exact match so toggling back restores the label.
6. Migration: add pref `Zombies55.Settings.GraphicsVersion`; on first run of the new version leave existing values and set `QualityPreset = High` (so upgrades keep today's look) if nothing else is stored; fresh installs may run the auto-pick (open decision).
7. `Format(id)` gets cases for the new ids (percentages, "FSR 1.0", "125%", "-0.50 EV").
8. `Reset()` keeps its behaviour for audio and controls, and resets graphics to High.

Not stored in `GameSettings` (not player choices): the benchmark history (JSON file) and last-applied snapshot.

### 7.2 Apply layer: `GraphicsApplier`

New file `Assets/Scripts/GraphicsApplier.cs`, namespace `Zombies55`, modelled on `FramePacing`: a hidden persistent object created at `RuntimeInitializeLoadType.BeforeSceneLoad`, subscribed to `GameSettings.Changed` and to `SceneManager.sceneLoaded`.

Rules:

- **Idempotent and safe on startup and on change.** `ApplyAll()` can be called at any time and any number of times; it diffs against a stored `_applied` snapshot and only runs the groups whose inputs changed; null checks for everything scene-bound (camera, volume, sun, flare, SSAO); no exception can escape to the game loop (wrap each group in try/catch and log once).
- **Groups** (order matters): Display (`Screen`), Pipeline asset (render scale, upscaler, shadows, HDR, aniso/mip/LOD, skin, particle budget), Camera (AA, FOV via `CameraOrbit`), Renderer (SSAO), Volume (bloom, DoF, motion blur, grain, flare, brightness), Scene (Street Detail, Particle multiplier, Character Detail, blast light), then `FramePacing.Apply()` last.
- **Scene re-binding:** on `sceneLoaded` (scene reloads on Restart / Return to title) re-find the main camera, `UniversalAdditionalCameraData`, the global `Volume`, the sun `Light` and its `UniversalAdditionalLightData`, `StreetDetail` roots, and re-apply the scene-bound groups. The pipeline clone and renderer clone live for the whole process (created once).
- **Display changes use a staged flow** (not live-on-click): the rows modify "pending" values; an APPLY button calls `Screen.SetResolution`, starts a 15 s confirmation countdown dialog ("Keep these display settings?") and reverts on timeout or Esc. `FramePacing` already re-resolves VSync when mode, size or refresh change. The first run (and upgrades) never change the display.
- **Hitch-prone changes** (`globalTextureMipmapLimit`, shadow resolution): the applier coalesces changes within 0.25 s and applies them once.
- The applier never writes to project assets (see 7.3), never calls `QualitySettings.SetQualityLevel`, and restores the original pipeline asset and renderer on `Application.quitting`.

API mapping inside the applier (single place):

```csharp
// Pipeline (runtime clone)
asset.renderScale = scale;
asset.upscalingFilter = filter;                 // Auto / FSR / STP
asset.mainLightShadowmapResolution = res;       // 1024/2048/4096/8192
asset.shadowCascadeCount = cascades;            // 2..4 only
asset.shadowDistance = dist;
asset.additionalLightsShadowmapResolution = addRes;
// Sun
sunData.softShadowQuality = SoftShadowQuality.{Low,Medium,High};   // UniversalAdditionalLightData
// Camera
camData.antialiasing = mode; camData.antialiasingQuality = q;
// Quality
QualitySettings.lodBias = ...; QualitySettings.globalTextureMipmapLimit = ...;
QualitySettings.skinWeights = ...; QualitySettings.particleRaycastBudget = ...;
QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
Texture.SetGlobalAnisotropicFilteringLimits(n, n);
```

### 7.3 URP asset and renderer handling (never touch the asset on disk)

1. At boot (BeforeSceneLoad), take `var src = QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;` and create `_asset = Object.Instantiate(src)` (hideFlags `DontSave`). Install it with `QualitySettings.renderPipeline = _asset;`. All asset-level setters in 2.1 are called on `_asset`. On `Application.quitting` restore `QualitySettings.renderPipeline = original` and destroy the clone (matters in the Editor).
2. **Renderer / SSAO clone.** `Instantiate` of the URP asset does not clone the `ScriptableRendererData` it references, and `m_RendererDataList` is `[SerializeField] internal`. One reflection hop is needed: instantiate the renderer data (`Object.Instantiate(rendererData)`), instantiate each feature in its `rendererFeatures` list and assign the clones back (public list), then set `_asset.m_RendererDataList[0]` through reflection. Keep the reflection in one method with a startup self-test (log an error and fall back to "SSAO on/off only via `SetActive` on the clone, no quality levels" if the field is missing in a future package). `BenchmarkRunner.ApplyExperiments` already uses this reflection field name, so the precedent is in the codebase.
   - Alternative that avoids reflection: author renderer assets `PC_Renderer_AO_{Low,Medium,High}` and a no-SSAO one, add them to the URP asset's renderer list, and switch with `UniversalAdditionalCameraData.SetRenderer(index)`. Cleaner and keeps shader prefilter data correct, but needs four renderer assets and an editor script to create them (SceneBuilder currently builds the single SSAO feature).
3. Repoint `BenchmarkRunner.ApplyExperiments` to use `GraphicsApplier.Asset` instead of `GraphicsSettings.currentRenderPipeline`, otherwise `-bn=` runs in the Editor edit PC_RPAsset in memory and may leave it dirty.
4. Volume overrides: continue using the runtime copy of `volume.profile` as `ImageQualityFX` does (the property returns an instance; never use `sharedProfile`). Motion blur, chromatic aberration if added: `profile.TryGet` else `profile.Add<T>(true)`.
5. **Shader variant coverage** (2.7): decide between disabling stripping or authoring coverage before any content shipping. Without it, Low-preset FXAA, SSAO Low/High samples, Bloom HQ, Motion Blur, Chromatic Aberration and soft-shadow Low/Medium can render incorrectly in a build while looking fine in the Editor. Add a build validation step: after the first build, run each preset in the player and compare screenshots against the Editor.

### 7.4 UI changes (`ShellBuilder.BuildSettings`, `SettingsMenu`, row scripts)

Current constraint: rows are absolutely positioned inside a fixed 1560x940 card. 26 rows plus headers need scrolling and a description line.

1. **Scroll container per tab.** For the GRAPHICS tab build `ScrollRect` (vertical, `MovementType.Clamped`, scrollbar optional) with a `Viewport` (`RectMask2D`) and a `Content` using `VerticalLayoutGroup` (spacing 8, `childForceExpandWidth`, `ContentSizeFitter` vertical preferred size). Rows created by the existing `AddSliderRow/AddStepperRow/AddToggleRow` currently call `Place(...)` with an absolute position; add overloads (or a `layout: true` argument) that give each row a `LayoutElement` (preferred height 64, width 1200) instead of a position. Keep the old signatures for Controls/Crosshair/Audio.
2. **Group headers:** a `SettingsHeaderRow` (non-selectable label: DISPLAY, QUALITY PRESET, RENDERING, POST-PROCESSING, ADVANCED) with a thin accent line.
3. **Gamepad scrolling:** `ScrollToSelected` component on the scroll rect: on `EventSystem.current.currentSelectedGameObject` change, move `verticalNormalizedPosition` so the selected row is fully visible (use `RectTransformUtility` on the content/viewport corners). Navigation order is already explicit via `Selectable` neighbours; set vertical navigation on rows.
4. **Description line:** a one-line TMP text at the card bottom (`DescriptionText`, between the rows and the Reset/Back buttons). Add a base interface `ISettingRow { string Description }`; each row component stores a `[SerializeField] string description` and calls `SettingsMenu.ShowDescription(text)` from `OnSelect` and `OnPointerEnter`. Texts follow the pattern "SHADOW QUALITY - Resolution, softness and number of shadow cascades. Biggest effect on performance (CPU and GPU)." Cost tags can be shown as small [GPU] [CPU] chips next to the label.
5. **New row types:**
   - `SettingsButtonRow` for APPLY, RUN BENCHMARK, COMPARE (action callback, submit/click).
   - `SettingsStepperRow` generalisation: reads `GameSettings.GetOptions(id)` (string[]) so resolution and refresh lists work; `Format(id)` for display; grey-out state (`interactable=false` plus tooltip) driven by `GraphicsApplier` rules (e.g. Refresh Rate disabled unless Exclusive; Upscaler disabled at 100%; Resolution read-only in Borderless).
   - A preset stepper row (cycles Low/Medium/High/Ultra; shows "CUSTOM" label, which is reached only by editing rows).
6. **Benchmark tab:** build it as a fifth tab with its own panel: options (stepper for duration, toggle for uncapped), RUN button, history list (`ScrollRect` with row prefabs), and a results panel that is hidden until `BenchmarkResults.Pending`/selected history entry exists; the frame-time graph is a custom `MaskableGraphic` (`GraphicsGraph : Graphic`, `OnPopulateMesh` building a line strip from `float[240]`, two series for comparison). Tab buttons: spacing becomes `-610 + i * 305`, width 285; `SettingsMenu` arrays (`tabButtons`, `tabPanels`, `firstSelectable`) grow from 4 to 5; LB/RB cycling already uses `tabPanels.Length`.
7. **`SettingsMenu` changes:** `Wire()` unchanged; add `ShowDescription`, `ShowBenchmarkResults(BenchmarkResult)` and the "Pending result" check in `Open()`; `GameShell.OpenSettings` must allow opening directly to the BENCHMARK tab after a run (pass a tab index).
8. **GameShell:** `BenchmarkActive` flag so `ApplyState` shows nothing during a benchmark (see 6.3); open the results after return.
9. `ShellBuilder` stays the single place that generates the menu; the whole thing is regenerated by running the existing build menu command. Because it uses `SerializedObject` to wire fields, new serialized fields on the new components must be wired the same way (`Wire(so, "description", ...)` pattern) in ShellBuilder.

### 7.5 Game-side hooks needed (small)

- `CameraOrbit`: read `GameSettings.Fov` (and recompute `_baseFov` on `GameSettings.Changed`).
- `StreetDetail` (new component on a scene root created by SceneBuilder, or created by `GraphicsApplier` at scene load): registry of renderer groups by name (`Dirt`, `TrashBags`, `StreetNoise`, `Density/Litter`, `Density/Signs`, `Density/Steam`, ...) with a `Tier` each and per-level on/off and distance culling. **SceneBuilder dependency (optional):** assign clutter groups to a dedicated "Clutter" layer (same collision matrix as Default) so `Camera.layerCullDistances` can do the distance cull natively; until then do a timer-based (every 0.25 s) `Renderer.enabled` pass by block distance, which is cheap because the groups are merged per block. Verify each group for colliders and gameplay (cover, navmesh) before disabling.
- `ParticleDetail` helper: static `ParticleDetail.Scale(ParticleSystem ps)` called where systems are created/played; skip gameplay-critical systems.
- `CombatFX`: blast light shadow mode and range factor from the setting.
- `KneeAlign.Disabled` is already public static.
- `FramePacing`: handle `cap == 0`; expose `RefreshRate` string for the UI; re-resolve after `GraphicsApplier` display changes (already automatic).
- `BenchmarkRunner`: refactor per 6.2; add result model (`BenchmarkResult`), history store, shared `Fill` helpers; keep `benchmark.json` fields and add `graph[]`, `settings[]`, `bottleneck`, `systemInfo`.

### 7.6 Suggested build order

1. `GameSettings` data model + presets + `Format`/options tables (no UI), with edit-mode tests for preset application and Custom detection.
2. `GraphicsApplier` with the pipeline clone and the public-API groups (render scale, upscaler, shadows, LOD, aniso, skin, AA, volume effects); verify in Editor Play mode that `PC_RPAsset.asset` and `ProjectSettings/QualitySettings.asset` bytes are unchanged afterwards.
3. Display group with the staged Apply and revert; test on a high-refresh monitor in DX12 (refresh behaviour, exclusive vs borderless).
4. SSAO clone/renderer switching and decide on shader variant stripping; build a player and verify each preset.
5. Menu: scroll container, headers, description line, new rows, gamepad scrolling.
6. Game-side hooks: FOV, Street Detail, Particle Detail, Character Detail.
7. Benchmark refactor, results screen, history, compare; Frame Timing decision.
8. Measure costs per setting with the existing `-benchmark` and fill the real numbers into section 4 and the preset table.

---

## 8. Risks, open decisions, acceptance tests

### 8.1 Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Shader variant stripping removes variants for runtime-selected options (SSAO sample counts, soft shadow levels, bloom HQ, motion blur, CA) | Correct in Editor, wrong in a player build | Section 2.7 option 1 or 2; test every preset in a built player. |
| Runtime mutation of shared ScriptableObjects (URP asset, renderer data, SSAO feature) | In the Editor the project assets change on disk; in a player no persistence | Clone the asset and renderer data and features; add the "assets unchanged after Play" check; repoint `-bn=` experiments. |
| URP internals are `internal` (SSAO settings, soft shadow quality on the asset, supportsSoftShadows, shadows supported flags, `m_RendererDataList`) | Reflection can break on package upgrade | Use public alternatives where they exist (per-light soft shadow quality, `Light.shadows`, `SetActive`); isolate the one reflection hop with a self-test and graceful fallback. Re-check `ENABLE_UPSCALER_FRAMEWORK` / `MODERN_SSAO` on upgrade. |
| MSAA is impossible in Deferred | Players expect it | Not offered; TAA/SMAA/FXAA cover it; mention in the description. |
| STP requires TAA and compute; FSR/FXAA ordering | Wrong combos produce soft or unchanged output | Applier rules: STP forces TAA; Upscaler row disabled at 100%; Low = FSR + FXAA to be visually verified. |
| Exclusive fullscreen on D3D12 is not true exclusive; refresh rate may not change | The high-refresh use case | Test on the target monitor; the UI shows the actual `Screen.currentResolution` refresh; fallback hint to Windows display settings; consider offering D3D11 only if needed (user decision). |
| Texture mip limit and shadow-resolution changes hitch | Perceived stutter | Apply from menus only, coalesce, benchmark applies before loading. |
| Street Detail depends on scene structure; some groups have colliders/gameplay roles | Disabling cover or nav-relevant props | Only decorate-only groups; verify colliders; keep Density props with colliders always on. Optional SceneBuilder layer change needs a scene rebuild (Editor). |
| Frame cap options on high-refresh displays | Cap 120 and VSync on tears when 120 does not divide the refresh rate | UI warns (`FramePacing.CanTear`); recommend "Unlimited" or a divisor of the refresh rate (refresh/2, refresh/3) via the Refresh Rate row; add divisor values as extras if desired. |
| Frame Timing disabled in release | No GPU/CPU split | Probe-based verdict; auto-upgrade when available; open decision 1. |
| Adaptive Performance is flagged on in PC_RPAsset | Could alter quality at runtime on some devices | Confirm it is inactive on desktop; set false in the clone to be safe. |
| First-lap D3D12 PSO compilation hitches in benchmark | Inflated 0.1% lows | 8 s warm-up, discarded warm-up hitches, PSO pre-warm (6.4). |

### 8.2 Open decisions for the user

1. **Frame Timing Stats in Player settings** (`enableFrameTimingStats` is 0). Options: (a) enable it (adds ~tiny overhead, gives exact CPU/render/GPU ms and a precise verdict), (b) leave it off and attach a `ProfilerRecorder` at benchmark time (documented to enable it in release builds; needs one verification), or (c) ship the probe-only verdict. Not changed here.
2. **Exclusive fullscreen on DX12.** Offer the option and test refresh behaviour on a high-refresh monitor, or offer only Borderless + Windowed and tell players to set the refresh in Windows. Related: whether a D3D11 fallback is wanted for true exclusive mode.
3. **Shader stripping.** Allow turning off URP "Strip Unused Variants" and "Strip Unused Post-processing Variants" (simple, bigger builds) vs authoring coverage assets (more work, smaller builds).
4. **Reflection vs multiple renderer assets** for SSAO quality levels (one reflection hop vs four renderer assets and an editor script).
5. **First-run auto-detect.** Default new installs to High (today's look) or auto-pick Low/Medium/High from `SystemInfo.graphicsMemorySize`, `processorCount`, GPU name.
6. **SceneBuilder change for a "Clutter" layer** (native distance culling, needs a scene rebuild with the Editor) vs runtime timer-based culling now.
7. **Default frame cap.** Keep 120 (today) or default to "Unlimited with VSync off"; and whether to add refresh/2 and refresh/3 as quick VSync-divisor values for high-refresh screens.
8. **Ultra shadow map 8192** (128 MB VRAM) and render scale 125%: acceptable for "Ultra", or cap at 4096 and 110%.
9. **Opaque texture off** in PC_RPAsset (found unused): approve the change to the project asset (it is a project file change outside this task).
10. **Benchmark duration default** 60 s (matches CLI) or 45 s to shorten the loop.

### 8.3 Cost calibration (do this before finalising the numbers in section 4)

The estimates above come from the NVIDIA guides and engine reasoning, not from this game. The existing `-benchmark` can measure each setting once the applier exists: run `-benchmark` at each preset and flip one row at a time, on (a) the dev rig (CPU-bound, shows CPU cost) and (b) a GPU-limited configuration (render scale 150% or a lower-end GPU) to show GPU cost. Record avg and 1% low per row and replace the low/medium/high labels with "x% avg / y% 1% low on CPU-bound, z% on GPU-bound".

### 8.4 Acceptance tests

- After Editor Play mode with many setting changes, the bytes of `Assets/Settings/PC_RPAsset.asset`, `PC_Renderer.asset`, `Zombies55Volume.asset` and `ProjectSettings/QualitySettings.asset` are unchanged.
- Launch with empty PlayerPrefs: look identical to today (High preset values equal current assets within rounding).
- Each preset renders correctly in a built player (screenshot compare against the Editor), including AA Low and Ultra bloom/motion blur.
- Changing any preset-controlled row switches the label to Custom; selecting a preset sets all of them; restart keeps the values.
- Display change with revert timeout works; refresh list matches `Screen.resolutions`; on a high-refresh display each Exclusive refresh rate behaves as documented.
- Benchmark from the menu: loads, measures N s, returns to the Graphics settings with results; two consecutive runs with unchanged settings agree within about 3% avg fps; history keeps 20; compare shows the settings diff; the run is cancelled cleanly by Esc; `-benchmark` CLI still writes `benchmark.json` and quits.
- Controller-only: every row reachable, scrolling follows selection, description line updates, LB/RB cycles five tabs.

---

## 9. Sources

Games and measurements (all fetched or returned by search on 2026-11-19):

- NVIDIA, Grand Theft Auto V PC Graphics & Performance Guide: https://www.nvidia.com/en-us/geforce/news/grand-theft-auto-v-pc-graphics-and-performance-guide/
- NVIDIA, Watch Dogs 2 Graphics and Performance Guide: https://www.nvidia.com/en-us/geforce/news/watch-dogs-2-graphics-and-performance-guide/
- NVIDIA, Tom Clancy's The Division Graphics & Performance Guide: https://www.nvidia.com/en-gb/geforce/news/tom-clancys-the-division-graphics-and-performance-guide/
- NVIDIA, Cyberpunk 2077 optimize RTX settings: https://www.nvidia.com/en-us/geforce/news/cyberpunk-2077-optimize-rtx-settings
- NVIDIA, Red Dead Redemption 2 recommended GPUs: https://www.nvidia.com/en-us/geforce/news/red-dead-redemption-2-pc-nvidia-recommended-gpus/
- NVIDIA, Max Payne 3 benchmarked: https://www.nvidia.com/en-gb/geforce/news/max-payne-3-benchmarked-in-a-new-york-minute/
- Rockstar support, Max Payne 3 command-line parameters (quality settings list): https://support.rockstargames.com/articles/3haiwo12XG2Tn5pse42JdG/full-list-of-command-line-parameters-for-max-payne-3-on-pc
- Techgage, A Technical Look at Max Payne 3 (MSAA vs FXAA cost, HDAO): https://techgage.com/article/a_technical_look_at_max_payne_3/
- Gamepressure, The Division 2 graphics options: https://www.gamepressure.com/tom-clancys-the-division-2/graphics-options/z9c015
- Notebookcheck, The Division 2 benchmark: https://www.notebookcheck.net/The-Division-2-Laptop-Desktop-Benchmarks.414654.0.html
- Wccftech, The Division 2 PC performance explored (settings list): https://wccftech.com/the-division-2-pc-performance-explored/
- DSOGaming, Mafia: Definitive Edition PC Performance Analysis: https://www.dsogaming.com/pc-performance-analyses/mafia-definitive-edition-pc-performance-analysis/
- PCInvasion, Mafia: Definitive Edition PC review: https://www.pcinvasion.com/mafia-definitive-edition-review-pc/
- ResetEra, Mafia: Definitive Edition PC performance thread: https://www.resetera.com/threads/mafia-definitive-edition-pc-performance-thread.294524/
- HardForum, Cyberpunk 2077 benchmark output (avg/min/max/1%/0.1%): https://hardforum.com/threads/what-is-min-max-framerate.2033196/
- TweakTown, Cyberpunk 2077 built-in benchmark: https://www.tweaktown.com/articles/10049/cyberpunk-2077-benchmarked-again-v1-update-with-built-in-benchmark/index.html
- Guru3D, GTA V benchmark log thread (per-pass min/max/avg, percentiles): https://forums.guru3d.com/threads/gta-v-benchmark.398478/
- HardForum, GTA V benchmark results: https://hardforum.com/threads/post-your-gtav-benchmark-results.1861011/
- Steam community, RDR2 benchmark results discussion: https://steamcommunity.com/app/1174180/discussions/0/3145132893571247619
- Prima Games, Sleeping Dogs: Definitive Edition PC impressions: https://primagames.com/gaming/sleeping-dogs-definitive-edition-pc-impressions
- Feral Interactive, Sleeping Dogs FAQ (benchmark and results dialog): https://support.feralinteractive.com/docs/en/sleepingdogs/1.1/steam/faqs/?access=PlFRBewWDM
- Tier1Settings, Black Ops 6 settings: https://tier1settings.com/best-black-ops-6-settings-for-fps/
- PCOptimizedSettings, Black Ops 6: https://pcoptimizedsettings.com/best-graphics-settings-for-call-of-duty-black-ops-6-on-pc-optimized-for-200-fps/
- Hone, Black Ops 6 settings: https://hone.gg/blog/best-black-ops-6-settings-for-fps/
- Shacknews, RDR2 PC graphics settings guide: https://www.shacknews.com/article/115067/red-dead-redemption-2-pc-graphics-settings-guide
- Switchblade Gaming, RDR2 best settings: https://www.switchbladegaming.com/game-settings/rdr2-best-settings/
- TargetFPS (per-setting percentage estimates; generic, not benchmarks): https://targetfps.com/games/red-dead-redemption-2/
- PCGamingWiki per-game pages (GTA V, RDR2, WD2, Mafia DE, Cyberpunk 2077, Max Payne 3, Division 2, Sleeping Dogs DE) returned HTTP 403 to automated fetches and were not used.

Unity:

- Screen.SetResolution (6000.6): https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/screen/setresolution
- FullScreenMode (6000.6): https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/fullscreenmode
- FrameTimingManager (6000.6): https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/frametimingmanager
- QualitySettings (6000.6): https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/qualitysettings
- URP rendering path comparison (MSAA not supported in Deferred): https://docs.unity3d.com/6000.1/Documentation/Manual/urp/rendering-paths-comparison.html and https://docs.unity.com/en-us/engine/6000.7/manual/render-pipelines/universal-render-pipeline/introduction/urp-concepts/rendering-paths/comparison
- URP anti-aliasing (MSAA cannot combine with TAA): https://docs.unity3d.com/6000.1/Documentation/Manual/urp/anti-aliasing.html
- Unity PlayerSettings.useFlipModelSwapchain (D3D11 only): https://docs.unity3d.com/2020.1/Documentation/ScriptReference/PlayerSettings-useFlipModelSwapchain.html
- Microsoft, DXGI/D3D12 swap chains (no exclusive fullscreen in D3D12): https://learn.microsoft.com/windows/win32/direct3d12/swap-chains
- URP package source read locally: `Library/PackageCache/com.unity.render-pipelines.universal@2a6014b851d8/Runtime/Data/UniversalRenderPipelineAsset.cs`, `Runtime/UniversalAdditionalCameraData.cs`, `Runtime/UniversalAdditionalLightData.cs`, `Runtime/RendererFeatures/ScreenSpaceAmbientOcclusion.cs`, `Runtime/ScriptableRendererFeature.cs`, `Runtime/UniversalRenderPipeline.cs`, `Editor/ShaderBuildPreprocessor.cs`.

Project files read: `Assets/Settings/*.asset` (PC_RPAsset, PC_Renderer, Mobile_RPAsset, Zombies55Volume, DefaultVolumeProfile, UniversalRenderPipelineGlobalSettings), `ProjectSettings/QualitySettings.asset`, `ProjectSettings/ProjectSettings.asset`, `Assets/Scripts/ImageQualityFX.cs`, `Assets/Scripts/FramePacing.cs`, `Assets/Scripts/BenchmarkRunner.cs`, `Assets/Scripts/GameLoop.cs`, `Assets/Scripts/UI/GameSettings.cs`, `SettingsMenu.cs`, `SettingsSliderRow.cs`, `SettingsStepperRow.cs`, `SettingsToggleRow.cs`, `Assets/Editor/ShellBuilder.cs`, `Assets/Editor/SceneBuilder.cs` (+ `.Street`, `.Density`, `.Zones`, `.Perf`, `.Dirt`, `.Noise`), `benchmark.json`.
