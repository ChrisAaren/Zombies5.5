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
            public Def(string key, float min, float max, float def) { Key = key; Min = min; Max = max; Default = def; }
        }

        const string Prefix = "Zombies55.Settings.";

        // Order matches SettingId.
        static readonly Def[] Defs =
        {
            new Def(Prefix + "MouseSensitivity", 0.1f, 5f, 1f),
            new Def(Prefix + "AimSensitivity", 0.2f, 1.5f, 0.6f),
            new Def(Prefix + "ControllerSensitivity", 0.2f, 3f, 1f),
            new Def(Prefix + "InvertY", 0f, 1f, 0f),
            new Def(Prefix + "CrosshairStyle", 0f, 5f, 0f),
            new Def(Prefix + "CrosshairColor", 0f, 7f, 0f),
            new Def(Prefix + "CrosshairSize", 0.5f, 2f, 1f),
            new Def(Prefix + "CrosshairGap", 0f, 20f, 7f),
            new Def(Prefix + "CrosshairLength", 4f, 40f, 16f),
            new Def(Prefix + "CrosshairThickness", 1f, 10f, 4f),
            new Def(Prefix + "CrosshairOpacity", 0.2f, 1f, 1f),
            new Def(Prefix + "CrosshairOutline", 0f, 1f, 1f),
            new Def(Prefix + "CrosshairDot", 0f, 1f, 0f),
            new Def(Prefix + "CrosshairDynamic", 0f, 1f, 1f),
            new Def("Zombies55.MusicVolume", 0f, 1f, 0.8f), // GameAudio's own keys and defaults
            new Def("Zombies55.SfxVolume", 0f, 1f, 0.9f),
            new Def(Prefix + "DepthOfField", 0f, 1f, 1f),
            new Def(Prefix + "LensFlare", 0f, 1f, 1f),
        };

        public static readonly string[] StyleNames =
            { "CLASSIC", "CROSS + DOT", "DOT", "CIRCLE", "T-SHAPE", "CIRCLE + DOT" };

        public static readonly string[] ColorNames =
            { "WHITE", "YELLOW", "GREEN", "CYAN", "MAGENTA", "RED", "ORANGE", "TEAL" };

        static readonly Color[] ColorValues =
        {
            Palette.UiLight, Palette.Accent, Palette.CrosshairGreen, Palette.CrosshairCyan,
            Palette.CrosshairMagenta, Palette.Danger, Palette.FireOrange, Palette.CharacterPrimary,
        };

        static float[] _values;

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _values = null;
            Changed = null;
        }

        static void EnsureLoaded()
        {
            if (_values != null) return;
            _values = new float[Defs.Length];
            for (int i = 0; i < Defs.Length; i++)
            {
                if (IsAudio((SettingId)i)) continue;
                _values[i] = Clamp(i, PlayerPrefs.GetFloat(Defs[i].Key, Defs[i].Default));
            }
        }

        static bool IsAudio(SettingId id) => id == SettingId.MusicVolume || id == SettingId.SfxVolume;

        static float Clamp(int i, float v)
        {
            v = Mathf.Clamp(v, Defs[i].Min, Defs[i].Max);
            return i >= (int)SettingId.CrosshairStyle && i <= (int)SettingId.CrosshairColor
                || i == (int)SettingId.InvertY || i == (int)SettingId.CrosshairOutline
                || i == (int)SettingId.CrosshairDot || i == (int)SettingId.CrosshairDynamic
                || i == (int)SettingId.DepthOfField || i == (int)SettingId.LensFlare
                ? Mathf.Round(v) : v;
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
            return _values[(int)id];
        }

        public static void SetFloat(SettingId id, float value)
        {
            EnsureLoaded();
            int i = (int)id;
            value = Clamp(i, value);
            if (id == SettingId.MusicVolume) GameAudio.MusicVolume = value;
            else if (id == SettingId.SfxVolume) GameAudio.SfxVolume = value;
            else
            {
                if (Mathf.Approximately(_values[i], value)) return;
                _values[i] = value;
                PlayerPrefs.SetFloat(Defs[i].Key, value);
            }
            Changed?.Invoke();
        }

        public static bool GetBool(SettingId id) => GetFloat(id) > 0.5f;
        public static void SetBool(SettingId id, bool value) => SetFloat(id, value ? 1f : 0f);
        public static int GetInt(SettingId id) => Mathf.RoundToInt(GetFloat(id));
        public static void SetInt(SettingId id, int value) => SetFloat(id, value);

        // Number of choices for a stepper setting.
        public static int OptionCount(SettingId id) => id == SettingId.CrosshairColor ? ColorNames.Length : StyleNames.Length;

        public static string Format(SettingId id)
        {
            float v = GetFloat(id);
            var ci = CultureInfo.InvariantCulture;
            switch (id)
            {
                case SettingId.CrosshairStyle: return StyleNames[Mathf.Clamp(GetInt(id), 0, StyleNames.Length - 1)];
                case SettingId.CrosshairColor: return ColorNames[Mathf.Clamp(GetInt(id), 0, ColorNames.Length - 1)];
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
                if (id == SettingId.MusicVolume) GameAudio.MusicVolume = Defs[i].Default;
                else if (id == SettingId.SfxVolume) GameAudio.SfxVolume = Defs[i].Default;
                else
                {
                    _values[i] = Defs[i].Default;
                    PlayerPrefs.SetFloat(Defs[i].Key, Defs[i].Default);
                }
            }
            Save();
            Changed?.Invoke();
        }

        public static void Save() => PlayerPrefs.Save();

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

        public static float MusicVolume { get => GetFloat(SettingId.MusicVolume); set => SetFloat(SettingId.MusicVolume, value); }
        public static float SfxVolume { get => GetFloat(SettingId.SfxVolume); set => SetFloat(SettingId.SfxVolume, value); }
    }
}
