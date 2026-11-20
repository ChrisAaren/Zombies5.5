using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Zombies55
{
    public enum DisplayModeKind { Borderless = 0, Windowed = 1, Exclusive = 2 }

    // Which display the game runs on, in which mode and at which resolution. Self-contained and reusable:
    //   * Startup(): runs before the first scene loads. First run: borderless at native resolution on the primary display.
    //     Later runs: restore the saved monitor, mode and resolution (a saved monitor that is gone falls back to the primary).
    //     Whatever was saved, a window that is bigger than its display, does not fit the display's work area (windowed),
    //     or straddles two displays is replaced by borderless fullscreen at that display's native resolution.
    //   * SetMonitor / SetMode / SetResolution / SetRefresh: the settings screen. A monitor change moves the window with
    //     Screen.MoveMainWindowTo (asynchronous, awaited) and then re-applies the mode at the new display's native resolution.
    //   * Persistence in PlayerPrefs ("Zombies55.Display.*"): monitor by name, resolution and index, mode, resolution per mode.
    // Unity persists its own window state in the registry too (Screenmanager keys); this class always wins at startup.
    public static class DisplayManager
    {
        // Direct3D 12 has no true exclusive fullscreen. Tested on this project's D3D12 player (high-refresh primary monitor, SetResolution with
        // ExclusiveFullScreen and 240 / 144 Hz): Screen.fullScreenMode stays FullScreenWindow and the monitor's desktop mode stays
        // at its native refresh rate, so the option and the refresh-rate row would do nothing. The code paths are kept behind this flag
        // (for a D3D11 build) and are off.
        public const bool ExclusiveAvailable = false;

        public static event Action Changed;

        const string P = "Zombies55.Display.";
        const float WindowedFraction = 0.75f;

        struct Choice
        {
            public int Monitor;
            public DisplayModeKind Mode;
            public int FsW, FsH;      // fullscreen resolution (0 = native)
            public int WinW, WinH;    // windowed client size (0 = default fraction)
            public int Hz;            // exclusive refresh rate (0 = display's current)
        }

        static readonly List<DisplayInfo> s_Displays = new List<DisplayInfo>();
        static readonly List<DisplayInfo> s_Tmp = new List<DisplayInfo>();
        static Choice s_Cur;
        static bool s_Ready;
        static bool s_Busy;
        static bool s_Pending;
        static DisplayHost s_Host;
        static readonly StringBuilder s_Trace = new StringBuilder();

        public static bool Busy => s_Busy;
        public static int MonitorCount => s_Displays.Count;
        public static string LastTrace => s_Trace.ToString();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Displays.Clear(); s_Ready = false; s_Busy = false; s_Pending = false; s_Host = null; Changed = null; s_Trace.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
#if UNITY_EDITOR
            return; // the Editor's Game view is not a display
#else
            if (Application.isBatchMode) return;
            foreach (var stale in Resources.FindObjectsOfTypeAll<DisplayHost>())
                if (stale != null && stale.gameObject.scene.IsValid()) UnityEngine.Object.Destroy(stale.gameObject);
            var go = new GameObject("DisplayManager") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_Host = go.AddComponent<DisplayHost>();
            Startup();
#endif
        }

        static void Log(string msg)
        {
            s_Trace.AppendLine(msg);
            Debug.Log("[Display] " + msg);
        }

        // ------------------------------------------------------------------ display list

        // Primary first, then the rest in the order Unity reports them.
        public static void RefreshDisplays()
        {
            s_Tmp.Clear();
            Screen.GetDisplayLayout(s_Tmp);
            s_Displays.Clear();
            if (s_Tmp.Count == 0) { s_Displays.Add(Screen.mainWindowDisplayInfo); return; }
            int primary = PrimaryIndex(s_Tmp);
            s_Displays.Add(s_Tmp[primary]);
            for (int i = 0; i < s_Tmp.Count; i++) if (i != primary) s_Displays.Add(s_Tmp[i]);
        }

        // Unity's DisplayInfo.workArea is relative to its own display, so it cannot tell which display is the primary.
        // On Windows the primary monitor is matched by native size and refresh; otherwise (or when ambiguous) the first entry
        // of the layout is taken, which is the primary on every platform seen so far.
        static int PrimaryIndex(List<DisplayInfo> list)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            foreach (var m in DisplayNative.Monitors())
            {
                if (!m.Primary) continue;
                int best = -1; double bestDiff = double.MaxValue;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].width != m.W || list[i].height != m.H) continue;
                    double diff = Math.Abs(list[i].refreshRate.value - m.Hz);
                    if (diff < bestDiff) { bestDiff = diff; best = i; }
                }
                if (best >= 0) return best;
            }
#endif
            return 0;
        }

        static bool SameDisplay(DisplayInfo a, DisplayInfo b) =>
            a.name == b.name && a.width == b.width && a.height == b.height;

        static int IndexOf(DisplayInfo d)
        {
            for (int i = 0; i < s_Displays.Count; i++) if (SameDisplay(s_Displays[i], d)) return i;
            return -1;
        }

        static int Hz(DisplayInfo d) => Mathf.RoundToInt((float)d.refreshRate.value);

        static string Describe(DisplayInfo d, int index) =>
            (index + 1) + ": " + d.width + "x" + d.height + " " + Hz(d) + " Hz" + (index == 0 ? " (Primary)" : "");

        // Modes (width, height, whole Hz) a display supports. DisplayInfo.resolutions is not implemented on Windows, so the
        // monitor's mode list comes from Win32 (Unity lists the primary first, then the others in Windows' enumeration order;
        // the pairing is verified by native size). Elsewhere only the display's current mode and Screen.resolutions are known.
        static List<Vector3Int> Modes(int displayIndex)
        {
            var d = s_Displays[displayIndex];
            var list = new List<Vector3Int>();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var mons = DisplayNative.Monitors();
            int p = mons.FindIndex(m => m.Primary);
            if (p > 0) { var pm = mons[p]; mons.RemoveAt(p); mons.Insert(0, pm); }
            int pick = -1;
            if (mons.Count == s_Displays.Count && mons[displayIndex].W == d.width && mons[displayIndex].H == d.height) pick = displayIndex;
            else for (int i = 0; i < mons.Count; i++) if (mons[i].W == d.width && mons[i].H == d.height) { pick = i; break; }
            if (pick >= 0) list = DisplayNative.Modes(mons[pick].Device);
#else
            if (SameDisplay(Screen.mainWindowDisplayInfo, d))
                foreach (var r in Screen.resolutions) list.Add(new Vector3Int(r.width, r.height, Mathf.RoundToInt((float)r.refreshRateRatio.value)));
#endif
            if (list.Count == 0) list.Add(new Vector3Int(d.width, d.height, Hz(d)));
            return list;
        }

        // ------------------------------------------------------------------ persistence

        static bool HasSaved => PlayerPrefs.GetInt(P + "Configured", 0) == 1;

        static void Save()
        {
            var d = s_Displays[Mathf.Clamp(s_Cur.Monitor, 0, s_Displays.Count - 1)];
            PlayerPrefs.SetInt(P + "Configured", 1);
            PlayerPrefs.SetString(P + "MonitorName", d.name);
            PlayerPrefs.SetInt(P + "MonitorIndex", s_Cur.Monitor);
            PlayerPrefs.SetInt(P + "MonitorW", d.width);
            PlayerPrefs.SetInt(P + "MonitorH", d.height);
            PlayerPrefs.SetInt(P + "Mode", (int)s_Cur.Mode);
            PlayerPrefs.SetInt(P + "FsW", s_Cur.FsW); PlayerPrefs.SetInt(P + "FsH", s_Cur.FsH);
            PlayerPrefs.SetInt(P + "WinW", s_Cur.WinW); PlayerPrefs.SetInt(P + "WinH", s_Cur.WinH);
            PlayerPrefs.SetInt(P + "Hz", s_Cur.Hz);
            PlayerPrefs.Save();
        }

        // Saved monitor by name + resolution (+ index to tell identical monitors apart); -1 when it is not connected.
        static int FindSavedMonitor()
        {
            string name = PlayerPrefs.GetString(P + "MonitorName", "");
            int w = PlayerPrefs.GetInt(P + "MonitorW", 0), h = PlayerPrefs.GetInt(P + "MonitorH", 0);
            int idx = PlayerPrefs.GetInt(P + "MonitorIndex", 0);
            var matches = new List<int>();
            for (int i = 0; i < s_Displays.Count; i++)
                if (s_Displays[i].name == name && s_Displays[i].width == w && s_Displays[i].height == h) matches.Add(i);
            if (matches.Count == 0) return -1;
            return matches.Contains(idx) ? idx : matches[0];
        }

        static Choice LoadChoice(out bool monitorMissing)
        {
            monitorMissing = false;
            var c = new Choice
            {
                Monitor = 0, Mode = DisplayModeKind.Borderless,
                FsW = PlayerPrefs.GetInt(P + "FsW", 0), FsH = PlayerPrefs.GetInt(P + "FsH", 0),
                WinW = PlayerPrefs.GetInt(P + "WinW", 0), WinH = PlayerPrefs.GetInt(P + "WinH", 0),
                Hz = PlayerPrefs.GetInt(P + "Hz", 0),
            };
            if (!HasSaved) return new Choice { Monitor = 0, Mode = DisplayModeKind.Borderless };
            int m = FindSavedMonitor();
            if (m < 0) { monitorMissing = true; c.FsW = c.FsH = c.WinW = c.WinH = c.Hz = 0; m = 0; }
            c.Monitor = m;
            int mode = PlayerPrefs.GetInt(P + "Mode", 0);
            c.Mode = mode == 1 ? DisplayModeKind.Windowed : mode == 2 && ExclusiveAvailable ? DisplayModeKind.Exclusive : DisplayModeKind.Borderless;
            return c;
        }

        // ------------------------------------------------------------------ geometry

        static RectInt OuterRect()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (DisplayNative.TryGetWindowRect(out var r)) return r;
#endif
            var p = Screen.mainWindowPosition;
            bool win = Screen.fullScreenMode == FullScreenMode.Windowed;
            return new RectInt(p.x, p.y, Screen.width + (win ? 16 : 0), Screen.height + (win ? 39 : 0));
        }

        // Frame thickness (width, height) around the client area of a windowed window.
        static Vector2Int FrameSize()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (DisplayNative.TryGetFrame(out var f)) return f;
#endif
            return new Vector2Int(16, 39);
        }

        static bool Contains(RectInt area, RectInt r, int slack) =>
            r.xMin >= area.xMin - slack && r.yMin >= area.yMin - slack && r.xMax <= area.xMax + slack && r.yMax <= area.yMax + slack;

        // Largest windowed client size that fits the work area together with the window frame.
        static Vector2Int FitClient(DisplayInfo d, int w, int h)
        {
            var f = FrameSize();
            int maxW = Mathf.Max(320, d.workArea.width - f.x), maxH = Mathf.Max(240, d.workArea.height - f.y);
            return new Vector2Int(Mathf.Min(w, maxW), Mathf.Min(h, maxH));
        }

        static Vector2Int DefaultWindowed(DisplayInfo d)
        {
            int w = Mathf.RoundToInt(d.width * WindowedFraction / 2f) * 2, h = Mathf.RoundToInt(d.height * WindowedFraction / 2f) * 2;
            return FitClient(d, w, h);
        }

        // Why the current window must be corrected, or null when it is fine.
        // Windows: the real window rectangle is compared with the real monitor rectangles (desktop coordinates).
        // Unity's own DisplayInfo gives the display's size and a work area that is relative to that display.
        static string Violation()
        {
            var cur = Screen.mainWindowDisplayInfo;
            bool windowed = Screen.fullScreenMode == FullScreenMode.Windowed;
            if (Screen.width > cur.width || Screen.height > cur.height)
                return "resolution " + Screen.width + "x" + Screen.height + " exceeds display " + cur.width + "x" + cur.height;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var outer = OuterRect();
            var mons = DisplayNative.Monitors();
            int best = -1, touched = 0; long bestArea = 0;
            for (int i = 0; i < mons.Count; i++)
            {
                var b = mons[i].Bounds;
                int ox = Mathf.Min(outer.xMax, b.xMax) - Mathf.Max(outer.xMin, b.xMin);
                int oy = Mathf.Min(outer.yMax, b.yMax) - Mathf.Max(outer.yMin, b.yMin);
                if (ox > 8 && oy > 8) { touched++; long a = (long)ox * oy; if (a > bestArea) { bestArea = a; best = i; } }
            }
            if (best < 0) return "window " + Str(outer) + " is not on any display";
            if (touched > 1) return "window " + Str(outer) + " straddles " + touched + " displays";
            var m = mons[best];
            if (Screen.width > m.W || Screen.height > m.H) return "resolution " + Screen.width + "x" + Screen.height + " exceeds monitor " + m.W + "x" + m.H;
            if (windowed && !Contains(m.Work, outer, 0)) return "windowed window " + Str(outer) + " does not fit work area " + Str(m.Work);
#endif
            return null;
        }

        static string Str(RectInt r) => "(" + r.x + "," + r.y + " " + r.width + "x" + r.height + ")";

        // ------------------------------------------------------------------ startup

        static void Startup()
        {
            RefreshDisplays();
            var cur = Screen.mainWindowDisplayInfo;
            var outer = OuterRect();
            Log("startup: window " + Str(outer) + " client " + Screen.width + "x" + Screen.height + " mode " + Screen.fullScreenMode +
                " on '" + cur.name + "' " + cur.width + "x" + cur.height + " work(rel) " + Str(cur.workArea) + ", mainWindowPosition " + Screen.mainWindowPosition + ", displays " + s_Displays.Count);
            for (int i = 0; i < s_Displays.Count; i++) Log("  display " + (i + 1) + ": " + s_Displays[i].name + " " + s_Displays[i].width + "x" + s_Displays[i].height +
                " " + s_Displays[i].refreshRate.value.ToString("0.00", CultureInfo.InvariantCulture) + " Hz work(rel) " + Str(s_Displays[i].workArea) + (i == 0 ? " PRIMARY" : ""));

            bool saved = HasSaved;
            s_Cur = LoadChoice(out bool missing);
            if (!saved) Log("first run: borderless at native resolution on the primary display");
            else if (missing) Log("saved monitor is not connected: falling back to the primary display");

            string why = Violation();
            if (why != null)
            {
                Log("correcting window: " + why + " -> borderless at native resolution");
                s_Cur.Mode = DisplayModeKind.Borderless; s_Cur.FsW = s_Cur.FsH = 0;
            }
            bool already = IndexOf(cur) == s_Cur.Monitor && why == null && Matches(s_Cur);
            if (already) { Log("window already matches the saved state"); s_Ready = true; Save(); return; }
            s_Host.StartCoroutine(Apply(s_Cur, true));
        }

        static bool Matches(Choice c)
        {
            if (Screen.fullScreenMode != ToUnity(c.Mode)) return false;
            var d = s_Displays[c.Monitor];
            if (c.Mode == DisplayModeKind.Windowed)
            {
                var want = c.WinW > 0 ? FitClient(d, c.WinW, c.WinH) : DefaultWindowed(d);
                return Screen.width == want.x && Screen.height == want.y;
            }
            int w = c.FsW > 0 ? c.FsW : d.width, h = c.FsH > 0 ? c.FsH : d.height;
            return Screen.width == w && Screen.height == h;
        }

        static FullScreenMode ToUnity(DisplayModeKind m) =>
            m == DisplayModeKind.Windowed ? FullScreenMode.Windowed : m == DisplayModeKind.Exclusive ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;

        // ------------------------------------------------------------------ applying

        static IEnumerator Apply(Choice c, bool startup)
        {
            s_Busy = true;
            c.Monitor = Mathf.Clamp(c.Monitor, 0, s_Displays.Count - 1);
            var d = s_Displays[c.Monitor];
            var cur = Screen.mainWindowDisplayInfo;
            bool windowed = c.Mode == DisplayModeKind.Windowed;

            Vector2Int client;
            if (windowed) client = c.WinW > 0 ? FitClient(d, c.WinW, c.WinH) : DefaultWindowed(d);
            else client = new Vector2Int(c.FsW > 0 ? Mathf.Min(c.FsW, d.width) : d.width, c.FsH > 0 ? Mathf.Min(c.FsH, d.height) : d.height);

            if (!SameDisplay(cur, d))
            {
                // A fullscreen window that is moved to another display has to be the size of that display or smaller;
                // go windowed and small first so the move can never leave a window spilling over the neighbours.
                if (Screen.fullScreenMode != FullScreenMode.Windowed)
                {
                    Screen.SetResolution(Mathf.Min(1280, d.width / 2), Mathf.Min(720, d.height / 2), FullScreenMode.Windowed);
                    for (int i = 0; i < 3; i++) yield return null;
                }
                var f = FrameSize();
                var pos = windowed
                    ? new Vector2Int(Mathf.Max(0, (d.workArea.width - client.x - f.x) / 2), Mathf.Max(0, (d.workArea.height - client.y - f.y) / 2))
                    : Vector2Int.zero;
                Log("moving window to display " + (c.Monitor + 1) + " at " + pos);
                var op = Screen.MoveMainWindowTo(d, pos);
                if (op != null) { float t0 = Time.realtimeSinceStartup; while (!op.isDone && Time.realtimeSinceStartup - t0 < 5f) yield return null; }
                for (int i = 0; i < 2; i++) yield return null;
            }

            var rr = d.refreshRate;
            if (c.Mode == DisplayModeKind.Exclusive && c.Hz > 0)
            {
                rr = new RefreshRate { numerator = (uint)c.Hz, denominator = 1 };
                if (SameDisplay(Screen.mainWindowDisplayInfo, d))
                    foreach (var r in Screen.resolutions)
                        if (r.width == client.x && r.height == client.y && Mathf.RoundToInt((float)r.refreshRateRatio.value) == c.Hz) { rr = r.refreshRateRatio; break; }
            }
            Log("apply: display " + (c.Monitor + 1) + " " + c.Mode + " " + client.x + "x" + client.y + (c.Mode == DisplayModeKind.Exclusive ? " @" + rr.value.ToString("0.00", CultureInfo.InvariantCulture) + " Hz" : ""));
            Screen.SetResolution(client.x, client.y, ToUnity(c.Mode), rr);
            for (int i = 0; i < 4; i++) yield return null;

            if (windowed)
            {
                // Centre on the display (the position given to the move can be off by the frame).
                var f = FrameSize();
                var pos = new Vector2Int(Mathf.Max(0, (d.workArea.width - client.x - f.x) / 2), Mathf.Max(0, (d.workArea.height - client.y - f.y) / 2));
                var op = Screen.MoveMainWindowTo(d, pos);
                if (op != null) { float t0 = Time.realtimeSinceStartup; while (!op.isDone && Time.realtimeSinceStartup - t0 < 5f) yield return null; }
                for (int i = 0; i < 2; i++) yield return null;
            }

            s_Cur = c;
            var after = Screen.mainWindowDisplayInfo;
            Log("result: window " + Str(OuterRect()) + " client " + Screen.width + "x" + Screen.height + " mode " + Screen.fullScreenMode + " on '" + after.name + "' " +
                after.width + "x" + after.height + " " + after.refreshRate.value.ToString("0.00", CultureInfo.InvariantCulture) + " Hz; screen.currentResolution " +
                Screen.currentResolution.width + "x" + Screen.currentResolution.height + " @" + Screen.currentResolution.refreshRateRatio.value.ToString("0.00", CultureInfo.InvariantCulture));
            s_Ready = true;
            Save();
            s_Busy = false;
            Changed?.Invoke();
            GameSettings.NotifyExternalChange();
            if (s_Pending) { s_Pending = false; s_Host.StartCoroutine(Apply(s_Cur, false)); }
        }

        static void Request(Choice c)
        {
            s_Cur = c;
            if (!s_Ready && s_Host == null) return; // Editor or batch mode: nothing to move
            if (s_Busy) { s_Pending = true; return; }
            s_Host.StartCoroutine(Apply(c, false));
        }

        // ------------------------------------------------------------------ settings screen API (indices into the option lists)

        static void EnsureReady()
        {
            if (s_Displays.Count == 0) RefreshDisplays();
            if (!s_Ready && s_Host == null) s_Cur = LoadChoice(out _);
        }

        static DisplayInfo Selected() { EnsureReady(); return s_Displays[Mathf.Clamp(s_Cur.Monitor, 0, s_Displays.Count - 1)]; }

        public static string[] MonitorOptions()
        {
            EnsureReady();
            var r = new string[s_Displays.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Describe(s_Displays[i], i);
            return r;
        }

        public static string[] ModeOptions() =>
            ExclusiveAvailable ? new[] { "FULLSCREEN", "WINDOWED", "EXCLUSIVE FULLSCREEN" } : new[] { "FULLSCREEN", "WINDOWED" };

        public static List<Vector2Int> Resolutions()
        {
            var d = Selected();
            var list = new List<Vector2Int>();
            var seen = new HashSet<long>();
            var f = FrameSize();
            bool windowed = s_Cur.Mode == DisplayModeKind.Windowed;
            foreach (var r in Modes(Mathf.Clamp(s_Cur.Monitor, 0, s_Displays.Count - 1)))
            {
                if (r.x < 640 || r.y < 360 || r.x > d.width || r.y > d.height) continue;
                if (windowed && (r.x + f.x > d.workArea.width || r.y + f.y > d.workArea.height)) continue;
                if (seen.Add(((long)r.x << 20) | (long)r.y)) list.Add(new Vector2Int(r.x, r.y));
            }
            if (!windowed && seen.Add(((long)d.width << 20) | (long)d.height)) list.Add(new Vector2Int(d.width, d.height));
            list.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return list;
        }

        public static List<int> RefreshRates()
        {
            var d = Selected();
            var res = CurrentResolution();
            var list = new List<int>();
            foreach (var r in Modes(Mathf.Clamp(s_Cur.Monitor, 0, s_Displays.Count - 1)))
                if (r.x == res.x && r.y == res.y && !list.Contains(r.z)) list.Add(r.z);
            if (list.Count == 0) list.Add(Hz(d));
            list.Sort((a, b) => b.CompareTo(a));
            return list;
        }

        static Vector2Int CurrentResolution()
        {
            var d = Selected();
            if (s_Cur.Mode == DisplayModeKind.Windowed) return s_Cur.WinW > 0 ? FitClient(d, s_Cur.WinW, s_Cur.WinH) : DefaultWindowed(d);
            return new Vector2Int(s_Cur.FsW > 0 ? Mathf.Min(s_Cur.FsW, d.width) : d.width, s_Cur.FsH > 0 ? Mathf.Min(s_Cur.FsH, d.height) : d.height);
        }

        public static int OptionCount(SettingId id)
        {
            switch (id)
            {
                case SettingId.Monitor: return Mathf.Max(1, MonitorOptions().Length);
                case SettingId.DisplayMode: return ModeOptions().Length;
                case SettingId.Resolution: return Mathf.Max(1, Resolutions().Count);
                case SettingId.RefreshRate: return Mathf.Max(1, RefreshRates().Count);
            }
            return 1;
        }

        public static int GetIndex(SettingId id)
        {
            EnsureReady();
            switch (id)
            {
                case SettingId.Monitor: return Mathf.Clamp(s_Cur.Monitor, 0, s_Displays.Count - 1);
                case SettingId.DisplayMode: return Mathf.Min((int)s_Cur.Mode, ModeOptions().Length - 1);
                case SettingId.Resolution:
                    {
                        var cur = CurrentResolution(); var list = Resolutions();
                        int best = 0;
                        for (int i = 0; i < list.Count; i++) if (list[i].x <= cur.x && list[i].y <= cur.y) { best = i; break; }
                        return best;
                    }
                case SettingId.RefreshRate:
                    {
                        var list = RefreshRates(); int hz = s_Cur.Hz > 0 ? s_Cur.Hz : Hz(Selected());
                        int best = 0, bestDiff = int.MaxValue;
                        for (int i = 0; i < list.Count; i++) { int df = Mathf.Abs(list[i] - hz); if (df < bestDiff) { bestDiff = df; best = i; } }
                        return best;
                    }
            }
            return 0;
        }

        public static string Format(SettingId id)
        {
            EnsureReady();
            int i = GetIndex(id);
            switch (id)
            {
                case SettingId.Monitor: return MonitorOptions()[i];
                case SettingId.DisplayMode: return ModeOptions()[i];
                case SettingId.Resolution:
                    {
                        var list = Resolutions(); if (list.Count == 0) return "-";
                        var r = list[Mathf.Clamp(i, 0, list.Count - 1)]; var d = Selected();
                        return r.x + "x" + r.y + (r.x == d.width && r.y == d.height ? " (NATIVE)" : "");
                    }
                case SettingId.RefreshRate:
                    {
                        if (s_Cur.Mode != DisplayModeKind.Exclusive) return Hz(Selected()) + " Hz";
                        var list = RefreshRates(); return list[Mathf.Clamp(i, 0, list.Count - 1)] + " Hz";
                    }
            }
            return "";
        }

        public static bool IsEnabled(SettingId id)
        {
            EnsureReady();
            switch (id)
            {
                case SettingId.Monitor: return s_Displays.Count > 1;
                case SettingId.Resolution: return true;
                case SettingId.RefreshRate: return s_Cur.Mode == DisplayModeKind.Exclusive && RefreshRates().Count > 1;
            }
            return true;
        }

        public static void SetIndex(SettingId id, int index)
        {
            EnsureReady();
            var c = s_Cur;
            switch (id)
            {
                case SettingId.Monitor:
                    index = Mathf.Clamp(index, 0, s_Displays.Count - 1);
                    if (index == c.Monitor) return;
                    c.Monitor = index; c.FsW = c.FsH = c.WinW = c.WinH = 0; c.Hz = 0; // native of the new display (the mode stays)
                    break;
                case SettingId.DisplayMode:
                    {
                        var m = (DisplayModeKind)Mathf.Clamp(index, 0, ModeOptions().Length - 1);
                        if (m == c.Mode) return;
                        c.Mode = m;
                        if (m != DisplayModeKind.Exclusive) c.Hz = 0;
                        break;
                    }
                case SettingId.Resolution:
                    {
                        s_Cur = c; var list = Resolutions(); if (list.Count == 0) return;
                        var r = list[Mathf.Clamp(index, 0, list.Count - 1)]; var d = Selected();
                        if (c.Mode == DisplayModeKind.Windowed) { c.WinW = r.x; c.WinH = r.y; }
                        else if (r.x == d.width && r.y == d.height) { c.FsW = c.FsH = 0; }
                        else { c.FsW = r.x; c.FsH = r.y; }
                        break;
                    }
                case SettingId.RefreshRate:
                    {
                        if (c.Mode != DisplayModeKind.Exclusive) return;
                        var list = RefreshRates(); c.Hz = list[Mathf.Clamp(index, 0, list.Count - 1)];
                        break;
                    }
                default: return;
            }
            Request(c);
        }

        // Test hook: pretend the saved monitor was unplugged (the next launch must fall back to the primary).
        internal static void TestForgetMonitor()
        {
            PlayerPrefs.SetString(P + "MonitorName", "Unplugged Monitor 9000");
            PlayerPrefs.SetInt(P + "MonitorIndex", 3);
            PlayerPrefs.Save();
        }

        // Test hook entry (see DisplayTestHook): the same call the settings rows make.
        internal static IEnumerator WaitIdle()
        {
            yield return null;
            while (s_Busy) yield return null;
            for (int i = 0; i < 5; i++) yield return null;
        }

        internal static DisplayInfo DisplayAt(int i) { EnsureReady(); return s_Displays[Mathf.Clamp(i, 0, s_Displays.Count - 1)]; }
        internal static IReadOnlyList<DisplayInfo> AllDisplays() { EnsureReady(); return s_Displays; }
        internal static string DescribeWindow() =>
            "window " + Str(OuterRect()) + " client " + Screen.width + "x" + Screen.height + " mode " + Screen.fullScreenMode + " monitor-index " + IndexOf(Screen.mainWindowDisplayInfo) +
            " displayInfo " + Screen.mainWindowDisplayInfo.width + "x" + Screen.mainWindowDisplayInfo.height + "@" + Screen.mainWindowDisplayInfo.refreshRate.value.ToString("0.00", CultureInfo.InvariantCulture) +
            " currentResolution " + Screen.currentResolution.width + "x" + Screen.currentResolution.height + "@" + Screen.currentResolution.refreshRateRatio.value.ToString("0.00", CultureInfo.InvariantCulture) +
            " framePacing vsync=" + FramePacing.AppliedVSyncCount + " target=" + FramePacing.AppliedTargetFrameRate + " R=" + FramePacing.RefreshRate.ToString("0.00", CultureInfo.InvariantCulture);

        // Watches for the window being dragged to another display (title bar, Win+Shift+Arrow) so the lists and the pacing follow.
        sealed class DisplayHost : MonoBehaviour
        {
            string _lastName; int _lastW, _lastH;
            float _next;   // polled twice a second: mainWindowDisplayInfo is a native query that allocates the display name
            void Update()
            {
                if (s_Busy || Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.5f;
                var cur = Screen.mainWindowDisplayInfo;
                if (cur.name != _lastName || cur.width != _lastW || cur.height != _lastH)
                {
                    bool first = _lastName == null;
                    _lastName = cur.name; _lastW = cur.width; _lastH = cur.height;
                    if (first) return;
                    RefreshDisplays();
                    int idx = IndexOf(cur);
                    if (idx >= 0 && idx != s_Cur.Monitor)
                    {
                        // The player dragged the window to another display: remember it, windowed size defaults for that display.
                        s_Cur.Monitor = idx; s_Cur.FsW = s_Cur.FsH = 0;
                        Save();
                    }
                    Changed?.Invoke();
                    GameSettings.NotifyExternalChange();
                }
            }
        }
    }
}
