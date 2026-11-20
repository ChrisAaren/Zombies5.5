using System;
using System.Globalization;
using UnityEngine;

namespace Zombies55
{
    public enum CrosshairStyle { Classic, CrossDot, Dot, Circle, TShape, CircleDot }

    public enum SettingId
    {
        MouseSensitivity, AimSensitivity, ControllerSensitivity, InvertY,
        CrosshairStyle, CrosshairColor, CrosshairSize, CrosshairGap, CrosshairLength,
        CrosshairThickness, CrosshairOpacity, CrosshairOutline, CrosshairDot, CrosshairDynamic,
        MusicVolume, SfxVolume,
        DepthOfField, LensFlare,
        FrameRate, VSync, FpsCounter,
        // Display (DisplayManager owns the state and saves it itself; these ids are indices into its option lists).
        Monitor, DisplayMode, Resolution, RefreshRate,
        // Graphics (appended after the display ids: saved prefs are keyed by name, but the arrays are by index).
        Fov, Brightness, QualityPreset,
        RenderScale, Upscaler, AntiAliasing, ShadowQuality, ShadowDistance, AmbientOcclusion, TextureQuality, ObjectDetail, Anisotropic,
        Bloom, MotionBlur, FilmGrain,
        StreetDetail, EffectsQuality, CharacterDetail,
        // Benchmark options (not part of presets).
        BenchUncapped, BenchExplosions,
    }

    // Player-facing settings, stored in PlayerPrefs. Loads lazily on first read.
    // Every setter clamps, saves the value to PlayerPrefs (flushed to disk by Save) and raises Changed.
    // Music and SFX volume live in GameAudio's statics; this class only routes to them.
    public static class GameSettings
    {
        struct Def
        {
            public string Key;
            public float Min, Max, Default;
            public bool Integer;
            public Def(string key, float min, float max, float def, bool integer = false) { Key = key; Min = min; Max = max; Default = def; Integer = integer; }
        }

        const string Prefix = "Zombies55.Settings.";

        // Order matches SettingId.
        static readonly Def[] Defs =
        {
            new Def(Prefix + "MouseSensitivity", 0.1f, 5f, 1f),
            new Def(Prefix + "AimSensitivity", 0.2f, 1.5f, 0.6f),
            new Def(Prefix + "ControllerSensitivity", 0.2f, 3f, 1f),
            new Def(Prefix + "InvertY", 0f, 1f, 0f, true),
            new Def(Prefix + "CrosshairStyle", 0f, 5f, 0f, true),
            new Def(Prefix + "CrosshairColor", 0f, 7f, 0f, true),
            new Def(Prefix + "CrosshairSize", 0.5f, 2f, 1f),
            new Def(Prefix + "CrosshairGap", 0f, 20f, 7f),
            new Def(Prefix + "CrosshairLength", 4f, 40f, 16f),
            new Def(Prefix + "CrosshairThickness", 1f, 10f, 4f),
            new Def(Prefix + "CrosshairOpacity", 0.2f, 1f, 1f),
            new Def(Prefix + "CrosshairOutline", 0f, 1f, 1f, true),
            new Def(Prefix + "CrosshairDot", 0f, 1f, 0f, true),
            new Def(Prefix + "CrosshairDynamic", 0f, 1f, 1f, true),
            new Def("Zombies55.MusicVolume", 0f, 1f, 0.8f), // GameAudio's own keys and defaults
            new Def("Zombies55.SfxVolume", 0f, 1f, 0.9f),
            new Def(Prefix + "DepthOfField", 0f, 1f, 1f, true),
            new Def(Prefix + "LensFlare", 0f, 1f, 1f, true),
            new Def(Prefix + "FrameRate", 0f, 4f, 3f, true), // index into FrameRateValues; default 120
            new Def(Prefix + "VSync", 0f, 1f, 1f, true),
            new Def(Prefix + "FpsCounter", 0f, 1f, 0f, true),
            new Def(Prefix + "Monitor", 0f, 15f, 0f, true),      // display ids: routed to DisplayManager, never stored here
            new Def(Prefix + "DisplayMode", 0f, 2f, 0f, true),
            new Def(Prefix + "Resolution", 0f, 63f, 0f, true),
            new Def(Prefix + "RefreshRate", 0f, 31f, 0f, true),
            new Def(Prefix + "Fov", 45f, 80f, 55f, true),
            new Def(Prefix + "Brightness", -1f, 1f, 0f),
            new Def(Prefix + "QualityPreset", 0f, 4f, 2f, true),       // 0..3 = Low..Ultra, 4 = Custom
            new Def(Prefix + "RenderScale", 0f, 5f, 4f, true),
            new Def(Prefix + "Upscaler", 0f, 1f, 0f, true),
            new Def(Prefix + "AntiAliasing", 0f, 4f, 4f, true),
            new Def(Prefix + "ShadowQuality", 0f, 3f, 2f, true),
            new Def(Prefix + "ShadowDistance", 0f, 4f, 3f, true),
            new Def(Prefix + "AmbientOcclusion", 0f, 4f, 3f, true),
            new Def(Prefix + "TextureQuality", 0f, 1f, 1f, true),
            new Def(Prefix + "ObjectDetail", 0f, 3f, 2f, true),
            new Def(Prefix + "Anisotropic", 0f, 4f, 3f, true),
            new Def(Prefix + "Bloom", 0f, 2f, 1f, true),
            new Def(Prefix + "MotionBlur", 0f, 1f, 0f, true),
            new Def(Prefix + "FilmGrain", 0f, 1f, 1f, true),
            new Def(Prefix + "StreetDetail", 0f, 3f, 2f, true),
            new Def(Prefix + "EffectsQuality", 0f, 3f, 2f, true),
            new Def(Prefix + "CharacterDetail", 0f, 2f, 2f, true),
            new Def(Prefix + "BenchUncapped", 0f, 1f, 1f, true),
            new Def(Prefix + "BenchExplosions", 0f, 1f, 0f, true),
        };

        public static readonly string[] StyleNames =
            { "CLASSIC", "CROSS + DOT", "DOT", "CIRCLE", "T-SHAPE", "CIRCLE + DOT" };

        public static readonly string[] ColorNames =
            { "WHITE", "YELLOW", "GREEN", "CYAN", "MAGENTA", "RED", "ORANGE", "TEAL" };

        public static readonly int[] FrameRateValues = { 30, 60, 90, 120, 0 }; // 0 = unlimited

        static readonly Color[] ColorValues =
        {
            Palette.UiLight, Palette.Accent, Palette.CrosshairGreen, Palette.CrosshairCyan,
            Palette.CrosshairMagenta, Palette.Danger, Palette.FireOrange, Palette.CharacterPrimary,
        };

        static float[] _values;
        static int _baseLevel = 2;   // the preset the values were last derived from (the quality level the applier starts from while Custom)

        public static event Action Changed;

        // Test runs (-gfxTest, -benchmarkPreset): nothing is read from or written to PlayerPrefs, so a test never leaks into the real settings.
        public static bool Ephemeral { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _values = null;
            Changed = null;
            _baseLevel = 2;
            // Test runs never read or write the player's saved settings.
            Ephemeral = false;
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-gfxTest", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-benchmarkPreset", StringComparison.OrdinalIgnoreCase))
                    Ephemeral = true;
        }

        // Called before the first read by the test hooks.
        public static void SetEphemeral()
        {
            Ephemeral = true;
            _values = null;
        }

        static void EnsureLoaded()
        {
            if (_values != null) return;
            _values = new float[Defs.Length];
            for (int i = 0; i < Defs.Length; i++)
            {
                if (IsAudio((SettingId)i) || IsDisplay((SettingId)i)) continue;
                _values[i] = Clamp(i, Ephemeral ? Defs[i].Default : PlayerPrefs.GetFloat(Defs[i].Key, Defs[i].Default));
            }
            _baseLevel = Ephemeral ? 2 : Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "BaseLevel", 2), 0, 3);
            int p = Mathf.RoundToInt(_values[(int)SettingId.QualityPreset]);
            if (p < 4) _baseLevel = p;
            RelabelPreset(false); // prefs from before the presets existed (for example depth of field off) show as Custom, an exact match as its preset
        }

        // ---------------------------------------------------------------- presets

        public const int PresetCustom = 4;
        public static readonly string[] PresetNames = { "LOW", "MEDIUM", "HIGH", "ULTRA", "CUSTOM" };

        // The settings a preset overwrites (display, frame pacing, FOV and brightness are never touched).
        public static readonly SettingId[] PresetIds =
        {
            SettingId.RenderScale, SettingId.Upscaler, SettingId.AntiAliasing, SettingId.ShadowQuality, SettingId.ShadowDistance,
            SettingId.AmbientOcclusion, SettingId.TextureQuality, SettingId.ObjectDetail, SettingId.Anisotropic,
            SettingId.Bloom, SettingId.DepthOfField, SettingId.MotionBlur, SettingId.LensFlare, SettingId.FilmGrain,
            SettingId.StreetDetail, SettingId.EffectsQuality, SettingId.CharacterDetail,
        };

        // Rows follow PresetIds. High is the look the game shipped with.
        static readonly int[][] PresetValues =
        {
            //  scale up aa shq dist ao tex lod aniso bloom dof mb flare grain street fx char
            new[] { 1, 1, 1, 0, 0, 1, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0 }, // Low
            new[] { 3, 1, 3, 1, 1, 2, 1, 1, 2, 1, 1, 0, 1, 0, 1, 1, 1 }, // Medium
            new[] { 4, 0, 4, 2, 3, 3, 1, 2, 3, 1, 1, 0, 1, 1, 2, 2, 2 }, // High
            new[] { 5, 0, 4, 3, 4, 4, 1, 3, 4, 2, 1, 1, 1, 1, 3, 3, 2 }, // Ultra
        };

        static bool IsPresetId(SettingId id) => Array.IndexOf(PresetIds, id) >= 0;

        // The quality level (0..3) the applier starts from: the preset, or the last preset while Custom.
        public static int BaseLevel { get { EnsureLoaded(); return _baseLevel; } }
        public static int QualityPreset => GetInt(SettingId.QualityPreset);

        public static void ApplyPreset(int preset)
        {
            EnsureLoaded();
            preset = Mathf.Clamp(preset, 0, 3);
            for (int k = 0; k < PresetIds.Length; k++) StoreValue(PresetIds[k], PresetValues[preset][k]);
            StoreValue(SettingId.QualityPreset, preset);
            _baseLevel = preset;
            if (!Ephemeral) PlayerPrefs.SetInt(Prefix + "BaseLevel", _baseLevel);
            Changed?.Invoke();
        }

        static void StoreValue(SettingId id, float v, bool persist = true)
        {
            int i = (int)id;
            v = Clamp(i, v);
            _values[i] = v;
            if (persist && !Ephemeral) PlayerPrefs.SetFloat(Defs[i].Key, v);
        }

        // After a preset-controlled value changed: the label follows (an exact match shows that preset, anything else is Custom).
        static void RelabelPreset(bool persist = true)
        {
            int match = PresetCustom;
            for (int p = 0; p < 4 && match == PresetCustom; p++)
            {
                bool all = true;
                for (int k = 0; k < PresetIds.Length && all; k++) all = Mathf.RoundToInt(_values[(int)PresetIds[k]]) == PresetValues[p][k];
                if (all) match = p;
            }
            StoreValue(SettingId.QualityPreset, match, persist);
            if (match < 4) { _baseLevel = match; if (persist && !Ephemeral) PlayerPrefs.SetInt(Prefix + "BaseLevel", _baseLevel); }
        }

        public static int PresetValue(int preset, SettingId id) => PresetValues[preset][Array.IndexOf(PresetIds, id)];

        static bool IsDisplay(SettingId id) => id >= SettingId.Monitor && id <= SettingId.RefreshRate;

        // DisplayManager finished a move or mode change: let the rows and FramePacing refresh.
        public static void NotifyExternalChange() => Changed?.Invoke();

        // Rows that can be greyed out (the monitor row with one display, the refresh row outside exclusive fullscreen).
        public static bool IsEnabled(SettingId id)
        {
            if (id == SettingId.Upscaler) return GetInt(SettingId.RenderScale) < 4;   // upscaling only happens below 100%
            return !IsDisplay(id) || DisplayManager.IsEnabled(id);
        }

        static bool IsAudio(SettingId id) => id == SettingId.MusicVolume || id == SettingId.SfxVolume;

        static float Clamp(int i, float v)
        {
            v = Mathf.Clamp(v, Defs[i].Min, Defs[i].Max);
            return Defs[i].Integer ? Mathf.Round(v) : v;
        }

        // ---------------------------------------------------------------- generic access (used by the menu rows)

        public static float Min(SettingId id) => Defs[(int)id].Min;
        public static float Max(SettingId id) => Defs[(int)id].Max;
        public static float Default(SettingId id) => Defs[(int)id].Default;

        public static float GetFloat(SettingId id)
        {
            EnsureLoaded();
            if (id == SettingId.MusicVolume) return GameAudio.MusicVolume;
            if (id == SettingId.SfxVolume) return GameAudio.SfxVolume;
            if (IsDisplay(id)) return DisplayManager.GetIndex(id);
            return _values[(int)id];
        }

        public static void SetFloat(SettingId id, float value)
        {
            EnsureLoaded();
            int i = (int)id;
            if (IsDisplay(id)) { DisplayManager.SetIndex(id, Mathf.RoundToInt(value)); Changed?.Invoke(); return; }
            value = Clamp(i, value);
            if (id == SettingId.QualityPreset) { if (value < PresetCustom) ApplyPreset((int)value); return; }
            if (id == SettingId.MusicVolume) GameAudio.MusicVolume = value;
            else if (id == SettingId.SfxVolume) GameAudio.SfxVolume = value;
            else
            {
                if (Mathf.Approximately(_values[i], value)) return;
                _values[i] = value;
                if (!Ephemeral) PlayerPrefs.SetFloat(Defs[i].Key, value);
                if (IsPresetId(id)) RelabelPreset();
            }
            Changed?.Invoke();
        }

        public static bool GetBool(SettingId id) => GetFloat(id) > 0.5f;
        public static void SetBool(SettingId id, bool value) => SetFloat(id, value ? 1f : 0f);
        public static int GetInt(SettingId id) => Mathf.RoundToInt(GetFloat(id));
        public static void SetInt(SettingId id, int value) => SetFloat(id, value);

        // Number of choices for a stepper setting.
        public static int OptionCount(SettingId id)
        {
            if (IsDisplay(id)) return DisplayManager.OptionCount(id);
            if (id == SettingId.CrosshairColor) return ColorNames.Length;
            if (id == SettingId.FrameRate) return FrameRateValues.Length;
            if (id == SettingId.QualityPreset) return 4;   // Custom is only reached by editing a row
            var names = Options(id);
            return names != null ? names.Length : StyleNames.Length;
        }

        static readonly string[] LevelNames = { "LOW", "MEDIUM", "HIGH", "ULTRA" };
        public static readonly int[] RenderScalePercent = { 50, 67, 75, 85, 100, 125 };
        public static readonly int[] ShadowDistanceMeters = { 40, 70, 100, 140, 200 };
        public static readonly int[] AnisoLevels = { 0, 2, 4, 8, 16 };

        static readonly string[] RenderScaleNames = { "50%", "67%", "75%", "85%", "100%", "125%" };
        static readonly string[] UpscalerNames = { "OFF", "FSR 1.0" };
        static readonly string[] AaNames = { "OFF", "FXAA", "SMAA", "TAA", "TAA HIGH" };
        static readonly string[] AoNames = { "OFF", "LOW", "MEDIUM", "HIGH", "ULTRA" };
        static readonly string[] TextureNames = { "LOW (HALF RES)", "HIGH" };
        static readonly string[] ShadowDistNames = { "40 M", "70 M", "100 M", "140 M", "200 M" };
        static readonly string[] AnisoNames = { "OFF", "2X", "4X", "8X", "16X" };
        static readonly string[] BloomNames = { "OFF", "ON", "HIGH QUALITY" };
        static readonly string[] CharNames = { "LOW", "MEDIUM", "HIGH" };

        // Names of the choices of a stepper setting, or null for sliders and toggles.
        public static string[] Options(SettingId id)
        {
            switch (id)
            {
                case SettingId.QualityPreset: return PresetNames;
                case SettingId.RenderScale: return RenderScaleNames;
                case SettingId.Upscaler: return UpscalerNames;
                case SettingId.AntiAliasing: return AaNames;
                case SettingId.ShadowQuality:
                case SettingId.ObjectDetail:
                case SettingId.StreetDetail:
                case SettingId.EffectsQuality: return LevelNames;
                case SettingId.ShadowDistance: return ShadowDistNames;
                case SettingId.AmbientOcclusion: return AoNames;
                case SettingId.TextureQuality: return TextureNames;
                case SettingId.Anisotropic: return AnisoNames;
                case SettingId.Bloom: return BloomNames;
                case SettingId.CharacterDetail: return CharNames;
                default: return null;
            }
        }

        public static string Format(SettingId id)
        {
            if (IsDisplay(id)) return DisplayManager.Format(id);
            float v = GetFloat(id);
            var ci = CultureInfo.InvariantCulture;
            switch (id)
            {
                case SettingId.QualityPreset: return PresetNames[Mathf.Clamp(GetInt(id), 0, 4)];
                case SettingId.RenderScale:
                case SettingId.Upscaler:
                case SettingId.AntiAliasing:
                case SettingId.ShadowQuality:
                case SettingId.ShadowDistance:
                case SettingId.AmbientOcclusion:
                case SettingId.TextureQuality:
                case SettingId.ObjectDetail:
                case SettingId.Anisotropic:
                case SettingId.Bloom:
                case SettingId.StreetDetail:
                case SettingId.EffectsQuality:
                case SettingId.CharacterDetail: { var o = Options(id); return o[Mathf.Clamp(GetInt(id), 0, o.Length - 1)]; }
                case SettingId.CrosshairStyle: return StyleNames[Mathf.Clamp(GetInt(id), 0, StyleNames.Length - 1)];
                case SettingId.CrosshairColor: return ColorNames[Mathf.Clamp(GetInt(id), 0, ColorNames.Length - 1)];
                case SettingId.FrameRate:
                {
                    int cap = FrameRateValues[Mathf.Clamp(GetInt(id), 0, FrameRateValues.Length - 1)];
                    return cap <= 0 ? "UNLIMITED" : cap.ToString(ci);
                }
                case SettingId.Fov: return Mathf.RoundToInt(v).ToString(ci) + "°";
                case SettingId.Brightness: return (v >= 0f ? "+" : "") + v.ToString("0.00", ci) + " EV";
                case SettingId.MotionBlur:
                case SettingId.FilmGrain:
                case SettingId.BenchUncapped:
                case SettingId.BenchExplosions:
                case SettingId.VSync:
                case SettingId.FpsCounter:
                case SettingId.InvertY:
                case SettingId.CrosshairOutline:
                case SettingId.CrosshairDot:
                case SettingId.CrosshairDynamic:
                case SettingId.DepthOfField:
                case SettingId.LensFlare: return v > 0.5f ? "ON" : "OFF";
                case SettingId.CrosshairOpacity:
                case SettingId.MusicVolume:
                case SettingId.SfxVolume: return Mathf.RoundToInt(v * 100f).ToString(ci) + "%";
                case SettingId.CrosshairGap:
                case SettingId.CrosshairLength: return Mathf.RoundToInt(v).ToString(ci);
                case SettingId.CrosshairThickness: return v.ToString("0.0", ci);
                default: return v.ToString("0.00", ci);
            }
        }

        public static void Reset()
        {
            EnsureLoaded();
            for (int i = 0; i < Defs.Length; i++)
            {
                var id = (SettingId)i;
                if (IsDisplay(id)) continue; // resetting must not move the window
                if (id == SettingId.MusicVolume) GameAudio.MusicVolume = Defs[i].Default;
                else if (id == SettingId.SfxVolume) GameAudio.SfxVolume = Defs[i].Default;
                else
                {
                    _values[i] = Defs[i].Default;
                    if (!Ephemeral) PlayerPrefs.SetFloat(Defs[i].Key, Defs[i].Default);
                }
            }
            _baseLevel = 2;   // every graphics default is the High column
            if (!Ephemeral) PlayerPrefs.SetInt(Prefix + "BaseLevel", _baseLevel);
            Save();
            Changed?.Invoke();
        }

        public static void Save() { if (!Ephemeral) PlayerPrefs.Save(); }

        // ---------------------------------------------------------------- typed accessors

        public static float MouseSensitivity { get => GetFloat(SettingId.MouseSensitivity); set => SetFloat(SettingId.MouseSensitivity, value); }
        public static float AimSensitivity { get => GetFloat(SettingId.AimSensitivity); set => SetFloat(SettingId.AimSensitivity, value); }
        public static float ControllerSensitivity { get => GetFloat(SettingId.ControllerSensitivity); set => SetFloat(SettingId.ControllerSensitivity, value); }
        public static bool InvertY { get => GetBool(SettingId.InvertY); set => SetBool(SettingId.InvertY, value); }

        public static CrosshairStyle CrosshairStyle
        {
            get => (CrosshairStyle)Mathf.Clamp(GetInt(SettingId.CrosshairStyle), 0, StyleNames.Length - 1);
            set => SetInt(SettingId.CrosshairStyle, (int)value);
        }

        public static int CrosshairColorIndex { get => GetInt(SettingId.CrosshairColor); set => SetInt(SettingId.CrosshairColor, value); }
        public static Color CrosshairColor => ColorValues[Mathf.Clamp(CrosshairColorIndex, 0, ColorValues.Length - 1)];
        public static float CrosshairSize { get => GetFloat(SettingId.CrosshairSize); set => SetFloat(SettingId.CrosshairSize, value); }
        public static float CrosshairGap { get => GetFloat(SettingId.CrosshairGap); set => SetFloat(SettingId.CrosshairGap, value); }
        public static float CrosshairLength { get => GetFloat(SettingId.CrosshairLength); set => SetFloat(SettingId.CrosshairLength, value); }
        public static float CrosshairThickness { get => GetFloat(SettingId.CrosshairThickness); set => SetFloat(SettingId.CrosshairThickness, value); }
        public static float CrosshairOpacity { get => GetFloat(SettingId.CrosshairOpacity); set => SetFloat(SettingId.CrosshairOpacity, value); }
        public static bool CrosshairOutline { get => GetBool(SettingId.CrosshairOutline); set => SetBool(SettingId.CrosshairOutline, value); }
        public static bool CrosshairDot { get => GetBool(SettingId.CrosshairDot); set => SetBool(SettingId.CrosshairDot, value); }
        public static bool CrosshairDynamic { get => GetBool(SettingId.CrosshairDynamic); set => SetBool(SettingId.CrosshairDynamic, value); }

        public static bool DepthOfField { get => GetBool(SettingId.DepthOfField); set => SetBool(SettingId.DepthOfField, value); }
        public static bool LensFlare { get => GetBool(SettingId.LensFlare); set => SetBool(SettingId.LensFlare, value); }

        public static float Fov => GetFloat(SettingId.Fov);
        public static float Brightness => GetFloat(SettingId.Brightness);
        public static bool MotionBlur => GetBool(SettingId.MotionBlur);
        public static bool FilmGrain => GetBool(SettingId.FilmGrain);

        public static int FrameRateIndex { get => GetInt(SettingId.FrameRate); set => SetInt(SettingId.FrameRate, value); }
        public static int FrameRateCap => FrameRateValues[Mathf.Clamp(FrameRateIndex, 0, FrameRateValues.Length - 1)];
        public static bool VSync { get => GetBool(SettingId.VSync); set => SetBool(SettingId.VSync, value); }
        public static bool FpsCounter { get => GetBool(SettingId.FpsCounter); set => SetBool(SettingId.FpsCounter, value); }

        public static float MusicVolume { get => GetFloat(SettingId.MusicVolume); set => SetFloat(SettingId.MusicVolume, value); }
        public static float SfxVolume { get => GetFloat(SettingId.SfxVolume); set => SetFloat(SettingId.SfxVolume, value); }
    }
}
