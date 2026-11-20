using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Zombies55
{
    // Turns the graphics settings into engine state. A hidden persistent object (like FramePacing) that listens to GameSettings.Changed.
    //
    // How a setting reaches the screen:
    //   1. The settings choose a base quality level (the preset, or the last preset while Custom). Switching the Unity quality level swaps
    //      in that level's own URP pipeline asset, which is what keeps every shader variant the level needs in a build.
    //   2. The level's asset is cloned once at runtime and the clone is installed in QualitySettings; every override (render scale,
    //      shadows, upscaler) is written to the clone. The assets on disk are never modified, and the original assets are put back on quit.
    //   3. Camera, sun, SSAO renderer, street detail and effects detail are applied to the scene objects (re-bound after every scene load).
    public class GraphicsApplier : MonoBehaviour
    {
        // Renderer list order in every URP asset (SceneBuilder.Graphics.cs): High AO first, then Off / Low / Medium / Ultra.
        // Five renderers (SceneBuilder.Graphics.cs). Order: High AO first, then Off / Low / Medium / Ultra.
        static readonly int[] AoRendererIndex = { 1, 2, 3, 0, 4 };   // by AMBIENT OCCLUSION option: Off, Low, Medium, High, Ultra
        static readonly int[] ShadowRes = { 1024, 2048, 4096, 8192 };
        static readonly int[] ShadowCascades = { 2, 3, 4, 4 };
        static readonly int[] AddShadowRes = { 1024, 1024, 2048, 4096 };
        static readonly float[] LodBias = { 1f, 1.5f, 2f, 3f };
        static readonly int[] RaycastBudget = { 16, 64, 256, 512 };
        // Distance beyond which collider-free street clutter / decals are culled (0 = far clip, i.e. never).
        static readonly float[] ClutterDistance = { 25f, 60f, 0f, 0f };
        static readonly float[] DecalDistance = { 12f, 40f, 0f, 0f };

        static GraphicsApplier s_Instance;

        // What is installed right now (for the benchmark report and the test hook).
        public static UniversalRenderPipelineAsset Asset { get; private set; }
        public static string LevelName => QualitySettings.names[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, QualitySettings.names.Length - 1)];
        // Benchmark bottleneck probe: multiplies the render scale for a few seconds.
        public static float ProbeScale = 1f;
        public static int AppliedAoRenderer { get; private set; } = -1;

        UniversalRenderPipelineAsset _clone;
        RenderPipelineAsset[] _project;   // the project's own pipeline asset of every quality level, recorded at first boot
        int _cloneLevel = -1;
        int _originalLevel = -1;
        bool _dirty;
        float _nextApply;
        int _lastMip = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_Instance = null; Asset = null; AppliedAoRenderer = -1; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (s_Instance != null) return;
            foreach (var stale in Resources.FindObjectsOfTypeAll<GraphicsApplier>())
                if (stale != null && stale.gameObject.scene.IsValid()) Destroy(stale.gameObject);
            var go = new GameObject("GraphicsApplier") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<GraphicsApplier>();
        }

        void OnEnable()
        {
            GameSettings.Changed += MarkDirty;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += Restore;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayMode;
#endif
            if (_originalLevel < 0) _originalLevel = QualitySettings.GetQualityLevel();
            RecordProjectAssets();
            ApplyAll();
        }

        void OnDisable()
        {
            GameSettings.Changed -= MarkDirty;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.quitting -= Restore;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayMode;
#endif
        }

#if UNITY_EDITOR
        void OnPlayMode(UnityEditor.PlayModeStateChange c) { if (c == UnityEditor.PlayModeStateChange.ExitingPlayMode) Restore(); }
#endif

        void MarkDirty() { _dirty = true; }

        void OnSceneLoaded(Scene s, LoadSceneMode m) { ApplyScene(); _dirty = true; }

        void Update()
        {
            if (!_dirty || Time.unscaledTime < _nextApply) return;
            _dirty = false;
            _nextApply = Time.unscaledTime + 0.1f;   // coalesce bursts (a preset changes seventeen values at once)
            ApplyAll();
        }

        // Puts the project's own pipeline assets and quality level back (matters in the Editor, where QualitySettings is project state).
        void Restore()
        {
            PutProjectAssetsBack();
            _cloneLevel = -1;
            if (_originalLevel >= 0) QualitySettings.SetQualityLevel(_originalLevel, false);
            Asset = null;
        }

        // A project asset is safe to assign: alive, not one of our runtime clones, and (in the Editor) persistent on disk.
        static bool IsProjectAsset(RenderPipelineAsset a)
        {
            if (a == null || a.name.EndsWith(" (runtime)", StringComparison.Ordinal)) return false;
#if UNITY_EDITOR
            if (!UnityEditor.EditorUtility.IsPersistent(a)) return false;
#endif
            return true;
        }

        void RecordProjectAssets()
        {
            int n = QualitySettings.names.Length;
            if (_project == null || _project.Length != n) _project = new RenderPipelineAsset[n];
            for (int i = 0; i < n; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i);
                if (IsProjectAsset(a)) _project[i] = a;
            }
        }

        RenderPipelineAsset ProjectAssetFor(int level)
        {
            if (_project != null && level >= 0 && level < _project.Length && IsProjectAsset(_project[level])) return _project[level];
            return null;
        }

        // Puts the recorded project asset back on every level that holds our clone (whichever level is current), destroys the clone,
        // then returns to the level that was current.
        void PutProjectAssetsBack()
        {
            if (_clone == null) return;
            int current = QualitySettings.GetQualityLevel();
            int n = QualitySettings.names.Length;
            for (int i = 0; i < n; i++)
            {
                if (!object.ReferenceEquals(QualitySettings.GetRenderPipelineAssetAt(i), _clone)) continue;
                var proj = ProjectAssetFor(i);
                if (proj == null)
                {
                    var def = GraphicsSettings.defaultRenderPipeline;
                    proj = IsProjectAsset(def) ? def : null;
                }
                if (proj == null) { Debug.LogWarning("[Graphics] no project pipeline asset recorded for quality level " + i); continue; }
                if (QualitySettings.GetQualityLevel() != i) QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = proj;
            }
            if (QualitySettings.GetQualityLevel() != current) QualitySettings.SetQualityLevel(current, false);
            Destroy(_clone);
            _clone = null;
            _cloneLevel = -1;
        }

        // Applies everything now. Safe to call at any time and any number of times.
        public static void ApplyNow() { if (s_Instance != null) { s_Instance._dirty = false; s_Instance.ApplyAll(); } }

        static int LevelIndex(int level)
        {
            string want = GameSettings.PresetNames[Mathf.Clamp(level, 0, 3)];
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++) if (string.Equals(names[i], want, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        void ApplyAll()
        {
            Guard(ApplyLevelAndPipeline, "pipeline");
            Guard(ApplyQualityStatics, "quality");
            Guard(ApplyScene, "scene");
            FramePacing.Apply();   // SetQualityLevel resets the VSync count
        }

        static void Guard(Action a, string what)
        {
            try { a(); }
            catch (Exception e) { Debug.LogError("[Graphics] " + what + " failed: " + e); }
        }

        // ---------------------------------------------------------------- pipeline asset

        void ApplyLevelAndPipeline()
        {
            int qi = LevelIndex(GameSettings.BaseLevel);
            if (qi >= 0 && qi != QualitySettings.GetQualityLevel())
            {
                // Give the level we leave its own asset back before switching, so returning to it later finds the project asset.
                PutProjectAssetsBack();
                QualitySettings.SetQualityLevel(qi, true);
                _lastMip = -1;   // the level reset the texture limit
            }
            int level = QualitySettings.GetQualityLevel();
            if (_clone == null || _cloneLevel != level)
            {
                var source = ProjectAssetFor(level) as UniversalRenderPipelineAsset
                             ?? (IsProjectAsset(QualitySettings.renderPipeline) ? QualitySettings.renderPipeline as UniversalRenderPipelineAsset : null)
                             ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
                if (source == null) return;
                _clone = Instantiate(source);
                _clone.name = source.name + " (runtime)";
                _clone.hideFlags = HideFlags.DontSave;
                _cloneLevel = level;
                QualitySettings.renderPipeline = _clone;
            }
            Asset = _clone;
            var a = _clone;

            int scaleIdx = GameSettings.GetInt(SettingId.RenderScale);
            float scale = GameSettings.RenderScalePercent[Mathf.Clamp(scaleIdx, 0, GameSettings.RenderScalePercent.Length - 1)] / 100f;
            scale *= ProbeScale;
            a.renderScale = scale;
            // FSR only has an effect below 100%; Auto otherwise.
            a.upscalingFilter = scale < 0.999f && GameSettings.GetInt(SettingId.Upscaler) == 1 ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;

            int sh = Mathf.Clamp(GameSettings.GetInt(SettingId.ShadowQuality), 0, 3);
            a.mainLightShadowmapResolution = ShadowRes[sh];
            a.shadowCascadeCount = ShadowCascades[sh];   // never 1: the single-cascade keyword variant is not guaranteed to be kept
            a.additionalLightsShadowmapResolution = AddShadowRes[sh];
            a.shadowDistance = GameSettings.ShadowDistanceMeters[Mathf.Clamp(GameSettings.GetInt(SettingId.ShadowDistance), 0, GameSettings.ShadowDistanceMeters.Length - 1)];
            a.supportsCameraOpaqueTexture = false;
        }

        // ---------------------------------------------------------------- QualitySettings statics

        void ApplyQualityStatics()
        {
            QualitySettings.lodBias = LodBias[Mathf.Clamp(GameSettings.GetInt(SettingId.ObjectDetail), 0, 3)];
            int ch = GameSettings.GetInt(SettingId.CharacterDetail);
            QualitySettings.skinWeights = ch >= 2 ? SkinWeights.FourBones : SkinWeights.TwoBones;
            KneeAlign.Disabled = ch == 0;
            int fx = Mathf.Clamp(GameSettings.GetInt(SettingId.EffectsQuality), 0, 3);
            QualitySettings.particleRaycastBudget = RaycastBudget[fx];
            EffectsDetail.Level = fx;

            int aniso = GameSettings.AnisoLevels[Mathf.Clamp(GameSettings.GetInt(SettingId.Anisotropic), 0, GameSettings.AnisoLevels.Length - 1)];
            if (aniso <= 0) QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            else
            {
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                Texture.SetGlobalAnisotropicFilteringLimits(aniso, aniso);
            }

            int mip = GameSettings.GetInt(SettingId.TextureQuality) == 0 ? 1 : 0;
            if (mip != _lastMip)
            {
                _lastMip = mip;
                QualitySettings.globalTextureMipmapLimit = mip;   // re-uploads textures: only when it actually changes
            }
        }

        // ---------------------------------------------------------------- scene objects

        void ApplyScene()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (cam.TryGetComponent<UniversalAdditionalCameraData>(out var data))
            {
                int aa = GameSettings.GetInt(SettingId.AntiAliasing);
                switch (aa)
                {
                    case 0: data.antialiasing = AntialiasingMode.None; break;
                    case 1: data.antialiasing = AntialiasingMode.FastApproximateAntialiasing; break;
                    case 2: data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality = AntialiasingQuality.High; break;
                    case 3: data.antialiasing = AntialiasingMode.TemporalAntiAliasing; data.antialiasingQuality = AntialiasingQuality.Medium; break;
                    default: data.antialiasing = AntialiasingMode.TemporalAntiAliasing; data.antialiasingQuality = AntialiasingQuality.High; break;
                }
                int want = AoRendererIndex[Mathf.Clamp(GameSettings.GetInt(SettingId.AmbientOcclusion), 0, 4)];
                data.SetRenderer(want);
                AppliedAoRenderer = want;
            }
            var sun = RenderSettings.sun;
            if (sun != null && sun.TryGetComponent<UniversalAdditionalLightData>(out var ld))
            {
                int sh = Mathf.Clamp(GameSettings.GetInt(SettingId.ShadowQuality), 0, 3);
                ld.softShadowQuality = sh == 0 ? SoftShadowQuality.Low : sh == 1 ? SoftShadowQuality.Medium : SoftShadowQuality.High;
            }
            int sd = Mathf.Clamp(GameSettings.GetInt(SettingId.StreetDetail), 0, 3);
            var cull = cam.layerCullDistances;
            if (cull == null || cull.Length != 32) cull = new float[32];
            cull[GameLayers.Clutter] = ClutterDistance[sd];
            cull[GameLayers.ClutterDecal] = DecalDistance[sd];
            cam.layerCullDistances = cull;
        }

        // One line per setting, for the benchmark report.
        public static string[] Snapshot()
        {
            var ids = new System.Collections.Generic.List<SettingId>
            {
                SettingId.QualityPreset, SettingId.RenderScale, SettingId.Upscaler, SettingId.AntiAliasing, SettingId.ShadowQuality, SettingId.ShadowDistance,
                SettingId.AmbientOcclusion, SettingId.TextureQuality, SettingId.ObjectDetail, SettingId.Anisotropic, SettingId.Bloom, SettingId.DepthOfField,
                SettingId.MotionBlur, SettingId.LensFlare, SettingId.FilmGrain, SettingId.StreetDetail, SettingId.EffectsQuality, SettingId.CharacterDetail,
                SettingId.Fov,
            };
            var lines = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++) lines[i] = ids[i] + "=" + GameSettings.Format(ids[i]);
            return lines;
        }
    }
}
