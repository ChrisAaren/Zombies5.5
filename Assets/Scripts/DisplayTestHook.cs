#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Zombies55
{
    // Test-only command-line hook for the standalone player (no player-facing input):
    //   -displayTest "monitor:2;mode:windowed;res:1280x720;mode:borderless;monitor:1;mode:exclusive;hz:144;exit"
    // Each step goes through the same DisplayManager calls the settings rows use, waits for the window to settle, then logs
    // the Win32 window rectangle against every monitor's bounds to the player log. The player quits after the last step.
    // Steps: monitor:N (1-based) | mode:borderless|windowed|exclusive | res:WxH | hz:N | wait:seconds | lists | forgetmonitor | exit
    static class DisplayTestHook
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            string script = null;
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-displayTest", StringComparison.OrdinalIgnoreCase)) script = args[i + 1];
            if (script == null) return;
            var go = new GameObject("DisplayTestHook") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().Script = script;
        }

        sealed class Runner : MonoBehaviour
        {
            public string Script;

            void Awake() { Invoke(nameof(Bail), 150f); }
            void Bail() { Debug.Log("[DisplayTest] timeout, quitting"); Application.Quit(); }

            IEnumerator Start()
            {
                for (int i = 0; i < 30; i++) yield return null;
                yield return DisplayManager.WaitIdle();
                Report("after startup");
                foreach (var raw in Script.Split(';'))
                {
                    string step = raw.Trim(); if (step.Length == 0) continue;
                    string key = step, val = "";
                    int c = step.IndexOf(':'); if (c > 0) { key = step.Substring(0, c); val = step.Substring(c + 1); }
                    if (key == "wait") { yield return new WaitForSecondsRealtime(float.Parse(val, CultureInfo.InvariantCulture)); continue; }
                    if (key == "exit") { Application.Quit(); yield break; }
                    Execute(key, val);
                    yield return DisplayManager.WaitIdle();
                    for (int i = 0; i < 20; i++) yield return null;
                    Report(step);
                }
                Application.Quit();
            }

            static void Execute(string key, string val)
            {
                try
                {
                    switch (key)
                    {
                        case "monitor": DisplayManager.SetIndex(SettingId.Monitor, int.Parse(val, CultureInfo.InvariantCulture) - 1); break;
                        case "mode": DisplayManager.SetIndex(SettingId.DisplayMode, val == "windowed" ? 1 : val == "exclusive" ? 2 : 0); break;
                        case "res":
                            {
                                var p = val.Split('x'); int w = int.Parse(p[0]), h = int.Parse(p[1]);
                                var list = DisplayManager.Resolutions(); int idx = -1;
                                for (int i = 0; i < list.Count; i++) if (list[i].x == w && list[i].y == h) idx = i;
                                if (idx < 0) Debug.Log("[DisplayTest] resolution " + val + " not in list: " + string.Join(",", list)); else DisplayManager.SetIndex(SettingId.Resolution, idx);
                                break;
                            }
                        case "hz":
                            {
                                var list = DisplayManager.RefreshRates(); int hz = int.Parse(val), idx = -1;
                                for (int i = 0; i < list.Count; i++) if (list[i] == hz) idx = i;
                                if (idx < 0) Debug.Log("[DisplayTest] refresh " + val + " not in list: " + string.Join(",", list)); else DisplayManager.SetIndex(SettingId.RefreshRate, idx);
                                break;
                            }
                        case "forgetmonitor": DisplayManager.TestForgetMonitor(); break;
                        case "lists":
                            Debug.Log("[DisplayTest] resolutions: " + string.Join(",", DisplayManager.Resolutions()) + " | refresh: " + string.Join(",", DisplayManager.RefreshRates()) + " | monitors: " + string.Join(" ; ", DisplayManager.MonitorOptions()));
                            break;
                    }
                }
                catch (Exception e) { Debug.Log("[DisplayTest] step " + key + ":" + val + " failed: " + e.Message); }
            }

            static void Report(string label)
            {
                var sb = new StringBuilder();
                sb.AppendLine("[DisplayTest] === " + label);
                sb.AppendLine("[DisplayTest] unity: " + DisplayManager.DescribeWindow());
                sb.AppendLine("[DisplayTest] win32: " + DisplayNative.RawWindowRect());
                var mons = DisplayNative.Monitors();
                TryGetRect(out var rect);
                int inside = -1;
                var spill = new StringBuilder();
                for (int i = 0; i < mons.Count; i++)
                {
                    var m = mons[i];
                    sb.AppendLine("[DisplayTest] monitor " + m.Device + (m.Primary ? " PRIMARY" : "") + " bounds (" + m.Bounds.x + "," + m.Bounds.y + " " + m.Bounds.width + "x" + m.Bounds.height + ") desktop mode " + m.W + "x" + m.H + "@" + m.Hz + "Hz");
                    bool contained = rect.xMin >= m.Bounds.xMin && rect.yMin >= m.Bounds.yMin && rect.xMax <= m.Bounds.xMax && rect.yMax <= m.Bounds.yMax;
                    int ox = Mathf.Min(rect.xMax, m.Bounds.xMax) - Mathf.Max(rect.xMin, m.Bounds.xMin), oy = Mathf.Min(rect.yMax, m.Bounds.yMax) - Mathf.Max(rect.yMin, m.Bounds.yMin);
                    bool overlap = ox > 0 && oy > 0;
                    if (contained) inside = i;
                    if (overlap && !contained) spill.Append(m.Device + " ");
                }
                sb.AppendLine("[DisplayTest] PLACEMENT rect (" + rect.x + "," + rect.y + " " + rect.width + "x" + rect.height + ") fully inside " + (inside >= 0 ? mons[inside].Device : "NO MONITOR") + "; overlaps (spills onto) " + (spill.Length == 0 ? "none" : spill.ToString()));
                Debug.Log(sb.ToString());
            }

            static void TryGetRect(out RectInt r) { DisplayNative.TryGetWindowRect(out r); }
        }
    }
}
#endif
