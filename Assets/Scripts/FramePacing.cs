using System.Globalization;
using UnityEngine;

namespace Zombies55
{
    // Applies the FRAME RATE / VSYNC settings and draws the optional FPS counter.
    // Lives on a hidden persistent object created at startup, so it works in every scene and before any menu exists.
    //
    // Unity on desktop ignores Application.targetFrameRate while vSyncCount > 0, so the rule is:
    //   Cap UNLIMITED (0): no frame cap; vSyncCount 1 with VSync on, 0 with it off.
    //   VSync off: vSyncCount 0 and targetFrameRate = cap.
    //   VSync on, cap >= R - 1: vSyncCount 1 (one frame per refresh).
    //   VSync on, R / cap within 3% of an integer n in 2..4: vSyncCount n (every n-th refresh, still tear-free).
    //   Otherwise (for example 90 on a 144 Hz display): vSyncCount 0 with targetFrameRate = cap. THIS CASE CAN TEAR,
    //   because the frame cap is not locked to the display refresh. There is no tear-free way to hit that rate.
    // R is Screen.currentResolution.refreshRateRatio.value. The rule is re-evaluated when R, the fullscreen mode or the settings change.
    public class FramePacing : MonoBehaviour
    {
        // Set by BenchmarkRunner: ignore the settings and run uncapped with VSync off.
        public static bool ForceUncapped;
        // Set while a benchmark runs: the FPS counter must not draw (or cost) anything inside the measured window.
        public static bool HideCounter;

        // The refresh rate of the display the window is on right now. Screen.currentResolution can lag behind a window
        // that was moved to another monitor, so the window's display is the first choice.
        public static double RefreshRate
        {
            get
            {
#if !UNITY_EDITOR
                double d = Screen.mainWindowDisplayInfo.refreshRate.value;
                if (d > 1.0) return d;
#endif
                return Screen.currentResolution.refreshRateRatio.value;
            }
        }
        public static int AppliedVSyncCount { get; private set; }
        public static int AppliedTargetFrameRate { get; private set; }
        public static bool CanTear { get; private set; }

        static FramePacing s_Instance;

        double _lastRefresh;
        FullScreenMode _lastMode;
        int _lastW, _lastH;
        string _lastDisplay = "";
        float _nextDisplayCheck;   // display queries are native calls that allocate the display name: poll twice a second, not every frame

        // FPS counter state
        float _accum;
        int _frames;
        float _smoothMs = 8f;
        string _text = "";
        GUIStyle _style, _shadow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (s_Instance != null) return;
            var go = new GameObject("FramePacing") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<FramePacing>();
            s_Instance.useGUILayout = false;   // the counter uses GUI.Label only: skip the per-frame IMGUI layout pass
        }

        // Pure rule, exposed for tests and the benchmark report.
        public static void Resolve(double refresh, int cap, bool vsync, out int vSyncCount, out int targetFps, out bool canTear)
        {
            canTear = false;
            if (cap <= 0) { vSyncCount = vsync ? 1 : 0; targetFps = -1; return; } // UNLIMITED: no cap (VSync on still locks to every refresh)
            if (!vsync) { vSyncCount = 0; targetFps = cap; return; }
            if (refresh <= 0.0 || cap >= refresh - 1.0) { vSyncCount = 1; targetFps = -1; return; }
            double ratio = refresh / cap;
            int n = (int)System.Math.Round(ratio);
            if (n >= 2 && n <= 4 && System.Math.Abs(ratio - n) <= 0.03 * n)
            {
                vSyncCount = n; targetFps = -1; return;
            }
            vSyncCount = 0; targetFps = cap; canTear = true; // documented tearing fallback
        }

        void OnEnable()
        {
            GameSettings.Changed += Apply;
            Apply();
        }

        void OnDisable() => GameSettings.Changed -= Apply;

        public static void Apply()
        {
            if (ForceUncapped)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                AppliedVSyncCount = 0; AppliedTargetFrameRate = -1; CanTear = false;
                return;
            }
            Resolve(RefreshRate, GameSettings.FrameRateCap, GameSettings.VSync, out int v, out int fps, out bool tear);
            QualitySettings.vSyncCount = v;
            Application.targetFrameRate = fps;
            AppliedVSyncCount = v; AppliedTargetFrameRate = fps; CanTear = tear;
        }

        void Update()
        {
            // Re-evaluate when the display mode or refresh rate changes (window moved to another monitor, mode switch).
            if (Time.unscaledTime >= _nextDisplayCheck)
            {
                _nextDisplayCheck = Time.unscaledTime + 0.5f;
                double r = RefreshRate;
                var mode = Screen.fullScreenMode;
                var res = Screen.currentResolution;
                string disp = Screen.mainWindowDisplayInfo.name;
                if (r != _lastRefresh || mode != _lastMode || res.width != _lastW || res.height != _lastH || disp != _lastDisplay)
                {
                    _lastRefresh = r; _lastMode = mode; _lastW = res.width; _lastH = res.height; _lastDisplay = disp;
                    Apply();
                }
            }

            float dt = Time.unscaledDeltaTime;
            _accum += dt; _frames++;
            _smoothMs = Mathf.Lerp(_smoothMs, dt * 1000f, 0.05f);
            if (_accum >= 0.25f)
            {
                if (GameSettings.FpsCounter)
                {
                    float fps = _frames / _accum;
                    _text = Mathf.RoundToInt(fps).ToString(CultureInfo.InvariantCulture) + " FPS  " +
                            _smoothMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
                }
                _accum = 0f; _frames = 0;
            }
        }

        void OnGUI()
        {
            if (HideCounter || !GameSettings.FpsCounter || _text.Length == 0 || Event.current.type != EventType.Repaint) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
                _style.normal.textColor = Palette.WithAlpha(Palette.UiLight, 0.8f);
                _shadow = new GUIStyle(_style);
                _shadow.normal.textColor = Palette.WithAlpha(Palette.UiDark, 0.8f);
            }
            var r = new Rect((Screen.width - 210f) * 0.5f, 6f, 210f, 24f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), _text, _shadow);
            GUI.Label(r, _text, _style);
        }
    }
}
