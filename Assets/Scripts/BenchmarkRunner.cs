using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Zombies55
{
    // Benchmark mode: started with the -benchmark command-line argument (optional -benchmarkSeconds N, -benchmarkPreset low|medium|high|ultra,
    // -benchmarkExplosions, -benchmarkStats) or from the settings menu (BeginFromMenu: loading message, run, results screen, scene reload).
    // Worst case survival run: every district owned, 48 enemies alive and chasing, explosive props in the scene.
    // The player and camera follow a fixed street loop (no input), VSync is off and the frame rate is uncapped.
    // Warm up 5 s, measure N s (default 60), write benchmark.json next to the exe (or persistentDataPath), quit.
    public class BenchmarkRunner : MonoBehaviour
    {
        const float WarmupSeconds = 5f;

        [Serializable]
        class Report
        {
            public string build, graphicsApi, gpu, cpu, fullscreenMode, qualityLevel;
            public int resolutionWidth, resolutionHeight, refreshRateHz;
            public float seconds;
            public int frames;
            public float avgFps, low1PctFps, low01PctFps;
            public string lowDefinition = "mean fps of the slowest 1% / 0.1% of frames";
            public float frameMsAvg, frameMsP50, frameMsP95, frameMsP99, frameMsMax;
            public bool frameTimingAvailable;
            public float cpuMainThreadMsAvg, cpuRenderThreadMsAvg, gpuMsAvg;
            public float cpuMainThreadMsP95, cpuRenderThreadMsP95, gpuMsP95;
            public float enemiesAliveAvg;
            public int enemiesAliveMin;
            public int zonesOwned, gcCollectionsDuringMeasure;
            public float gcAllocKBPerFrame;
            public int framesOver8_3ms, framesOver16_6ms;
            public string[] slowestFrames, trail, perSecond, perSecondStats;
            public string[] profilerMarkersTop;
            public string experiments;
            public bool explosions;          // -benchmarkExplosions: a car or barrel blown up near the player's path every 5 s
            public int explosionsFired;
            public int ragdollsStarted, ragdollsRefused, ragdollPeakActive, playerKnockdowns;   // enemy and player ragdolls during the measured window
            public float ragdollStartMsMax;
            public string note;
            public string preset;
            public string[] settings;
            public bool frameTimingEnabledInPlayer;
            public string bottleneck, bottleneckHow;
            public float[] graph;
            public float probeFpsFull, probeFpsReduced;
        }

        static readonly Vector2[] Loop =
        {
            new Vector2(57, 57), new Vector2(57, -57), new Vector2(19, -57), new Vector2(19, 57),
            new Vector2(-19, 57), new Vector2(-19, -57), new Vector2(-57, -57), new Vector2(-57, 57),
        };

        // True from the menu's RUN until the scene reload that follows the results. GameShell ignores pause input meanwhile.
        public static bool Active { get; private set; }
        // The run that just finished; GameShell opens the results screen with it after the reload, then clears it.
        public static BenchmarkResult Pending;
        // One line for the benchmark tab ("BENCHMARK CANCELLED").
        public static string Note;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = false; Pending = null; Note = null; }

        float _seconds = 60f;
        BenchmarkOptions _opt;
        bool _fromMenu, _cancel;
        GameObject _overlay;
        TMPro.TMP_Text _overlayText;
        UnityEngine.UI.Image _overlayDim;
        bool _stand;
        Vector3 _stuckRef;
        float _stuckTimer;
        int _wp;
        float _yaw;
        bool _explosions, _carNext = true;
        float _nextBoom;
        int _boomsFired;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            bool on = false;
            float secs = 60f;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-benchmark", StringComparison.OrdinalIgnoreCase)) on = true;
                else if (string.Equals(args[i], "-benchmarkSeconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                         && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 1f) secs = s;
            }
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-benchmarkPreset", StringComparison.OrdinalIgnoreCase))
                {
                    int p = Array.FindIndex(GameSettings.PresetNames, n => string.Equals(n, args[i + 1], StringComparison.OrdinalIgnoreCase));
                    if (p >= 0 && p < 4) { GameSettings.ApplyPreset(p); GraphicsApplier.ApplyNow(); }
                }
            if (on) Begin(secs);
        }

        // The settings menu's RUN BENCHMARK: reloads the scene for a fresh game, runs with the current settings, then shows the results.
        public static void BeginFromMenu(BenchmarkOptions options)
        {
            if (Active) return;
            Active = true;
            Pending = null; Note = null;
            var go = new GameObject("BenchmarkRunner") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            var r = go.AddComponent<BenchmarkRunner>();
            r._opt = options; r._seconds = options.Seconds; r._fromMenu = true;
        }

        // Also callable from the Editor (script eval) to test the benchmark in Play mode.
        public static void Begin(float seconds)
        {
            var go = new GameObject("BenchmarkRunner") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<BenchmarkRunner>()._seconds = seconds;
        }

        // ---------------------------------------------------------------- overlay (the only UI while a menu run is going)

        void ShowOverlay(string text, bool dim)
        {
            if (_overlay == null)
            {
                _overlay = new GameObject("BenchmarkOverlay", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
                _overlay.transform.SetParent(transform, false);
                var canvas = _overlay.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 5000;
                var sc = _overlay.GetComponent<UnityEngine.UI.CanvasScaler>();
                sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sc.referenceResolution = new Vector2(1920f, 1080f);
                sc.matchWidthOrHeight = 0.5f;
                var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                dimGo.transform.SetParent(_overlay.transform, false);
                _overlayDim = dimGo.GetComponent<UnityEngine.UI.Image>();
                _overlayDim.raycastTarget = false;
                var rt = (RectTransform)dimGo.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                var bar = new GameObject("Text", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                bar.transform.SetParent(_overlay.transform, false);
                _overlayText = bar.GetComponent<TMPro.TextMeshProUGUI>();
                _overlayText.raycastTarget = false;
                _overlayText.alignment = TMPro.TextAlignmentOptions.Center;
                _overlayText.fontSize = 30f;
                _overlayText.characterSpacing = 6f;
                _overlayText.color = Palette.UiLight;
                var brt = (RectTransform)bar.transform;
                brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f);
                brt.pivot = new Vector2(0.5f, 0f);
                brt.sizeDelta = new Vector2(0f, 60f); brt.anchoredPosition = new Vector2(0f, 26f);
            }
            _overlay.SetActive(true);
            _overlayDim.color = Palette.WithAlpha(Palette.UiDark, dim ? 0.92f : 0f);
            var rtt = (RectTransform)_overlayText.transform;
            if (dim) { rtt.anchorMin = new Vector2(0f, 0.5f); rtt.anchorMax = new Vector2(1f, 0.5f); rtt.pivot = new Vector2(0.5f, 0.5f); rtt.anchoredPosition = Vector2.zero; _overlayText.fontSize = 56f; }
            else { rtt.anchorMin = new Vector2(0f, 0f); rtt.anchorMax = new Vector2(1f, 0f); rtt.pivot = new Vector2(0.5f, 0f); rtt.anchoredPosition = new Vector2(0f, 26f); _overlayText.fontSize = 28f; }
            _overlayText.text = text;
        }

        void SetOverlayText(string text) { if (_overlayText != null) _overlayText.text = text; }

        static bool CancelPressed()
        {
            var k = Keyboard.current; var pad = Gamepad.current;
            return (k != null && k.escapeKey.wasPressedThisFrame) || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        System.Collections.IEnumerator Start()
        {
            _explosions = _fromMenu ? _opt.Explosions : HasArg("-benchmarkExplosions");
            _stand = HasArg("-benchmarkStand");   // player stands still, the camera orbits: the crowd closes in and stays in view
            bool cliQuit = !_fromMenu;
            bool stats = HasArg("-benchmarkStats");   // per-second visible/moving enemy and render counters: they cost frame time, so only on request
            if (_fromMenu)
            {
                ShowOverlay("LOADING BENCHMARK", true);
                yield return null;
                yield return null;
                var old = GameLoop.Instance;
                if (old != null) old.ReturnToTitle();   // fresh scene: enemies, zones, round and points all start clean
                else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                while (GameLoop.Instance == null || ReferenceEquals(GameLoop.Instance, old)) yield return null;
                yield return null;
            }
            GraphicsApplier.ApplyNow();
            // Frame timing in a release player: attaching a ProfilerRecorder turns FrameTimingManager on (documented for 6000.6).
            var ftRecMain = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            var ftRecGpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
            bool uncapped = _fromMenu ? _opt.Uncapped : true;
            FramePacing.ForceUncapped = uncapped;
            FramePacing.HideCounter = true;
            FramePacing.Apply();
            UnityEngine.Random.InitState(12345);
            PlayerHealth.GodMode = true;
            RoundManager.BenchmarkMode = true;
            PlayerController.ScriptedInput = true;
            CameraOrbit.ScriptedLook = true;
            CameraOrbit.ScriptedPitch = 12f;

            while (GameLoop.Instance == null || ZoneManager.Instance == null || PlayerHealth.Instance == null || RoundManager.Instance == null) yield return null;
            yield return null;
            ZoneManager.Instance.UnlockAll();
            var player = PlayerHealth.Instance.transform;
            Vector3 p = player.position;
            _wp = 0;
            float best = float.MaxValue;
            for (int i = 0; i < Loop.Length; i++) { float d = (Loop[i] - new Vector2(p.x, p.z)).sqrMagnitude; if (d < best) { best = d; _wp = i; } }
            _yaw = player.eulerAngles.y;
            CameraOrbit.ScriptedYaw = _yaw;
            GameLoop.Instance.StartRun();
            ApplyExperiments();

            float t0 = Time.unscaledTime;
            float nextLabel = 0f;
            if (_fromMenu) ShowOverlay("", false);
            while (Time.unscaledTime - t0 < WarmupSeconds && !_cancel)
            {
                Drive();
                if (_fromMenu && Time.unscaledTime >= nextLabel) { nextLabel = Time.unscaledTime + 0.5f; SetOverlayText("BENCHMARK  WARM-UP  " + (Time.unscaledTime - t0).ToString("0") + " / " + WarmupSeconds.ToString("0") + " s   -   ESC TO CANCEL"); }
                if (CancelPressed()) _cancel = true;
                yield return null;
            }
            RagdollPool.Instance?.ResetStats();

            var ft = new List<float>(1 << 16);
            var main = new List<float>(1 << 16);
            var rend = new List<float>(1 << 16);
            var gpu = new List<float>(1 << 16);
            var timings = new FrameTiming[1];
            bool timingOn = FrameTimingManager.IsFeatureEnabled();
            bool ftEnabledInPlayer = timingOn;
            double unfocused = 0; bool lostFocus = false;
            double aliveSum = 0; int aliveMin = int.MaxValue;
            int gc0 = GC.CollectionCount(0);
            long heapPrev = GC.GetTotalMemory(false);
            long allocBytes = 0;
            var times = new List<float>(1 << 16);
            var where = new List<Vector2>(1 << 16);
            var trail = new List<string>();
            var statNames = new[] { "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Shadow Casters Count", "Visible Skinned Meshes Count" };
            var statRecs = new ProfilerRecorder[statNames.Length];
            if (stats && Debug.isDebugBuild) for (int i = 0; i < statNames.Length; i++) statRecs[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, statNames[i]);
            var statSum = new double[statNames.Length];
            int visEnemies = 0, movingEnemies = 0; double visSum = 0, movSum = 0; int statFrames = 0;
            Enemy[] allEnemies = stats ? FindObjectsByType<Enemy>(FindObjectsInactive.Exclude) : null;
            var perSecondStats = new List<string>();
            int lastStatSec = 0, statFramesSec = 0; double visSumSec = 0, movSumSec = 0; var statSumSec = new double[statNames.Length];
            float nextTrail = 0f;
            // -benchmarkMarkers (development players): average the sum-per-frame of the profiler markers, to see where the main thread goes.
            List<ProfilerRecorder> recs = null; List<string> recNames = null; double[] recSum = null; int recFrames = 0;
            if (HasArg("-benchmarkMarkers") && Debug.isDebugBuild)
            {
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                recs = new List<ProfilerRecorder>(); recNames = new List<string>();
                foreach (var h in handles)
                {
                    var d = ProfilerRecorderHandle.GetDescription(h);
                    if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                    var cat = d.Category.ToString();
                    if (cat != "Internal" && cat != "Scripts" && cat != "Animation" && cat != "Physics" && cat != "Ai" && cat != "Render" && cat != "Particles" && cat != "Gui" && cat != "Lighting" && cat != "Audio") continue;
                    recs.Add(ProfilerRecorder.StartNew(d.Category, d.Name, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame));
                    recNames.Add(cat + "/" + d.Name);
                }
                recSum = new double[recs.Count];
            }
            double elapsed = 0;
            while (elapsed < _seconds && !_cancel)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (!Application.isFocused) { unfocused += dt; if (unfocused > 2.0) lostFocus = true; }
                if (_fromMenu)
                {
                    if (CancelPressed()) _cancel = true;
                    if (Time.unscaledTime >= nextLabel) { nextLabel = Time.unscaledTime + 0.5f; SetOverlayText("BENCHMARK  " + elapsed.ToString("0") + " / " + _seconds.ToString("0") + " s   -   ESC TO CANCEL"); }
                }
                elapsed += dt;
                ft.Add(dt * 1000f);
                times.Add((float)elapsed);
                {
                    var pp = PlayerHealth.Instance.transform.position; where.Add(new Vector2(pp.x, pp.z));
                    if (elapsed >= nextTrail) { nextTrail += 5f; trail.Add("t=" + elapsed.ToString("0") + " (" + pp.x.ToString("0") + "," + pp.y.ToString("0.0") + "," + pp.z.ToString("0") + ") wp=" + _wp + " yaw=" + _yaw.ToString("0")); }
                }
                long heap = GC.GetTotalMemory(false);
                if (heap > heapPrev) allocBytes += heap - heapPrev; // lower bound on allocation; collections hide some
                heapPrev = heap;
                if (recs != null)
                {
                    recFrames++;
                    for (int i = 0; i < recs.Count; i++) if (recs[i].Valid) recSum[i] += recs[i].LastValue;
                }
                statFrames++;
                for (int i = 0; i < statRecs.Length; i++) if (statRecs[i].Valid) statSum[i] += statRecs[i].LastValue;
                if (stats && statFrames % 10 == 1)
                {
                    allEnemies = FindObjectsByType<Enemy>(FindObjectsInactive.Exclude);
                    visEnemies = 0; movingEnemies = 0;
                    foreach (var en in allEnemies)
                    {
                        var smr = en.GetComponentInChildren<SkinnedMeshRenderer>();
                        if (smr != null && smr.isVisible) visEnemies++;
                        if (en.Velocity.sqrMagnitude > 0.25f) movingEnemies++;
                    }
                }
                visSum += visEnemies; movSum += movingEnemies;
                if (stats && (int)elapsed != lastStatSec)
                {
                    if (statFramesSec > 0)
                    {
                        string line = lastStatSec + ": vis=" + (visSumSec / statFramesSec).ToString("0") + " moving=" + (movSumSec / statFramesSec).ToString("0");
                        for (int i = 0; i < statNames.Length; i++) line += " " + statNames[i].Replace(" Count", "").Replace(" ", "") + "=" + (statSumSec[i] / statFramesSec).ToString("0");
                        perSecondStats.Add(line);
                    }
                    lastStatSec = (int)elapsed; statFramesSec = 0; visSumSec = 0; movSumSec = 0; System.Array.Clear(statSumSec, 0, statSumSec.Length);
                }
                statFramesSec++; visSumSec += visEnemies; movSumSec += movingEnemies;
                for (int i = 0; i < statRecs.Length; i++) if (statRecs[i].Valid) statSumSec[i] += statRecs[i].LastValue;
                Drive();
                TickExplosions((float)elapsed);
                int alive = RoundManager.Instance.AliveCount;
                aliveSum += alive; if (alive < aliveMin) aliveMin = alive;
                if (timingOn)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                    {
                        main.Add((float)timings[0].cpuMainThreadFrameTime);
                        rend.Add((float)timings[0].cpuRenderThreadFrameTime);
                        gpu.Add((float)timings[0].gpuFrameTime);
                    }
                }
            }
            int gcs = GC.CollectionCount(0) - gc0;
            if (!ftEnabledInPlayer) ftEnabledInPlayer = FrameTimingManager.IsFeatureEnabled();
            if (ftRecMain.Valid) ftRecMain.Dispose();
            if (ftRecGpu.Valid) ftRecGpu.Dispose();
            if (_cancel) { Finish(null, null, cliQuit); yield break; }
            // CPU or GPU? FrameTimingManager gives no data in a release player (even with a ProfilerRecorder attached), so probe:
            // run the same route again at the full render scale and at 60% of it. Barely faster = CPU-bound, much faster = GPU-bound.
            float probeA = 0f, probeB = 0f;
            if (_fromMenu || HasArg("-benchmarkProbe"))
            {
                if (_fromMenu) SetOverlayText("BENCHMARK  CHECKING WHAT LIMITS THE FRAME RATE");
                // Full, reduced, full, reduced: the crowd keeps closing in while the route runs, so the order is balanced to cancel that drift.
                for (int pass = 0; pass < 4; pass++)
                {
                    GraphicsApplier.ProbeScale = pass % 2 == 0 ? 1f : 0.6f; GraphicsApplier.ApplyNow();
                    yield return Probe(0.8f);   // settle: render targets are reallocated
                    yield return Probe(3.2f);
                    if (pass % 2 == 0) probeA += _probeFps * 0.5f; else probeB += _probeFps * 0.5f;
                }
                GraphicsApplier.ProbeScale = 1f; GraphicsApplier.ApplyNow();
            }

            var r = new Report
            {
                build = Application.isEditor ? "editor" : (Debug.isDebugBuild ? "development player" : "release player"),
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                gpu = SystemInfo.graphicsDeviceName,
                cpu = SystemInfo.processorType,
                fullscreenMode = Screen.fullScreenMode.ToString(),
                qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()],
                resolutionWidth = Screen.width, resolutionHeight = Screen.height,
                refreshRateHz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value),
                seconds = (float)elapsed, frames = ft.Count,
                enemiesAliveAvg = (float)(aliveSum / Mathf.Max(1, ft.Count)), enemiesAliveMin = aliveMin == int.MaxValue ? 0 : aliveMin,
                zonesOwned = ZoneManager.Instance.OwnedCount,
                gcCollectionsDuringMeasure = gcs,
                gcAllocKBPerFrame = allocBytes / 1024f / Mathf.Max(1, ft.Count),
                frameTimingAvailable = timingOn && gpu.Count > 0,
            };
            Fill(r, ft);
            r.frameTimingEnabledInPlayer = ftEnabledInPlayer;
            r.preset = GameSettings.PresetNames[Mathf.Clamp(GameSettings.QualityPreset, 0, 4)];
            r.settings = GraphicsApplier.Snapshot();
            r.explosions = _explosions; r.explosionsFired = _boomsFired;
            var rp = RagdollPool.Instance;
            if (rp != null) { r.ragdollsStarted = rp.Granted; r.ragdollsRefused = rp.Refused; r.ragdollPeakActive = rp.PeakActive; r.ragdollStartMsMax = rp.MaxAcquireMs; }
            if (PlayerRagdoll.Instance != null) r.playerKnockdowns = PlayerRagdoll.Instance.Knockdowns;
            foreach (var a in Environment.GetCommandLineArgs()) if (a.StartsWith("-bn=", StringComparison.OrdinalIgnoreCase)) r.experiments = a;
            r.trail = trail.ToArray();
            r.perSecondStats = perSecondStats.ToArray();
            foreach (var sr in statRecs) if (sr.Valid) sr.Dispose();
            {
                var ps = new List<string>();
                float sumMs = 0, maxMs = 0; int n = 0; int sec = 0;
                for (int i = 0; i < ft.Count; i++)
                {
                    sumMs += ft[i]; maxMs = Mathf.Max(maxMs, ft[i]); n++;
                    if (i == ft.Count - 1 || (int)times[i + 1] > sec)
                    {
                        float mainMs = 0; int mc = 0;
                        for (int k = i - n + 1; k <= i && k < main.Count; k++) { mainMs += main[k]; mc++; }
                        ps.Add(sec + ": " + (sumMs / n).ToString("0.0") + "ms avg, " + maxMs.ToString("0") + " max, main " + (mc > 0 ? mainMs / mc : 0).ToString("0.0") + " (" + n + " frames)");
                        sumMs = 0; maxMs = 0; n = 0; sec = (int)times[Mathf.Min(i + 1, ft.Count - 1)];
                    }
                }
                r.perSecond = ps.ToArray();
            }
            foreach (var v in ft) { if (v > 8.333f) r.framesOver8_3ms++; if (v > 16.667f) r.framesOver16_6ms++; }
            var idx = new List<int>(ft.Count);
            for (int i = 0; i < ft.Count; i++) idx.Add(i);
            idx.Sort((a, b) => ft[b].CompareTo(ft[a]));
            int top = Mathf.Min(24, idx.Count);
            r.slowestFrames = new string[top];
            for (int i = 0; i < top; i++) r.slowestFrames[i] = "t=" + times[idx[i]].ToString("0.00") + "s " + ft[idx[i]].ToString("0.0") + "ms at (" + where[idx[i]].x.ToString("0") + "," + where[idx[i]].y.ToString("0") + ")";
            if (r.frameTimingAvailable)
            {
                r.cpuMainThreadMsAvg = Avg(main); r.cpuRenderThreadMsAvg = Avg(rend); r.gpuMsAvg = Avg(gpu);
                r.cpuMainThreadMsP95 = Pct(main, 95); r.cpuRenderThreadMsP95 = Pct(rend, 95); r.gpuMsP95 = Pct(gpu, 95);
            }
            else r.note = "FrameTimingManager reported no data on this build (feature disabled or unsupported).";
            var result = BuildResult(r, ft, times, lostFocus, uncapped, probeA, probeB);
            r.probeFpsFull = probeA; r.probeFpsReduced = probeB;
            if (recs != null)
            {
                var order = Enumerable.Range(0, recs.Count).OrderByDescending(i => recSum[i]).Take(70).ToArray();
                r.profilerMarkersTop = order.Select(i => recNames[i] + "  avg=" + (recSum[i] / Mathf.Max(1, recFrames) / 1e6).ToString("0.000") + "ms").ToArray();
                foreach (var rec in recs) rec.Dispose();
            }
            r.bottleneck = result.bottleneck; r.bottleneckHow = result.bottleneckHow; r.graph = result.graph;
            Write(r);
            Finish(result, r, cliQuit);
        }

        float _probeFps;

        System.Collections.IEnumerator Probe(float seconds)
        {
            float t = 0f; int n = 0;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; n++; Drive(); }
            _probeFps = n / Mathf.Max(0.001f, t);
        }

        // Puts every flag the run set back, then either quits (command line) or reloads the scene and hands the result to the shell.
        void Finish(BenchmarkResult result, Report report, bool cliQuit)
        {
            PlayerHealth.GodMode = false; RoundManager.BenchmarkMode = false;
            PlayerController.ScriptedInput = false; CameraOrbit.ScriptedLook = false;
            FramePacing.ForceUncapped = false; FramePacing.HideCounter = false;
            FramePacing.Apply();
            if (cliQuit)
            {
                Application.Quit();
#if UNITY_EDITOR
                if (report != null) Debug.Log("[Benchmark] done: " + JsonUtility.ToJson(report));
                Destroy(gameObject);
#endif
                return;
            }
            if (result != null) { Pending = result; BenchmarkHistory.Add(result); }
            else Note = "BENCHMARK CANCELLED";
            if (_overlay != null) ShowOverlay("SAVING RESULTS", true);
            StartCoroutine(ReloadAndDie());
        }

        System.Collections.IEnumerator ReloadAndDie()
        {
            yield return null;
            var old = GameLoop.Instance;
            if (old != null) old.ReturnToTitle(); else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            while (GameLoop.Instance == null || ReferenceEquals(GameLoop.Instance, old)) yield return null;
            yield return null;
            yield return null;
            Active = false;
            Destroy(gameObject);
        }

        BenchmarkResult BuildResult(Report r, List<float> ft, List<float> times, bool lostFocus, bool uncapped, float probeA, float probeB)
        {
            var res = new BenchmarkResult
            {
                id = Guid.NewGuid().ToString("N"),
                utc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                gameVersion = Application.version, build = r.build,
                seconds = r.seconds, frames = r.frames,
                avgFps = r.avgFps, low1PctFps = r.low1PctFps, low01PctFps = r.low01PctFps,
                minFps = 1000f / Mathf.Max(0.001f, r.frameMsMax), maxFps = 1000f / Mathf.Max(0.001f, Min(ft)),
                frameMsAvg = r.frameMsAvg, frameMsP50 = r.frameMsP50, frameMsP95 = r.frameMsP95, frameMsP99 = r.frameMsP99, frameMsMax = r.frameMsMax,
                framesOver8_3 = r.framesOver8_3ms, framesOver16_6 = r.framesOver16_6ms,
                preset = r.preset, settings = r.settings,
                gpu = r.gpu, cpu = r.cpu, os = SystemInfo.operatingSystem, api = r.graphicsApi,
                display = Screen.mainWindowDisplayInfo.name,
                resolution = r.resolutionWidth + "x" + r.resolutionHeight,
                mode = r.fullscreenMode, refresh = r.refreshRateHz + " Hz",
                vramMb = SystemInfo.graphicsMemorySize, ramMb = SystemInfo.systemMemorySize, cores = SystemInfo.processorCount,
                uncapped = uncapped, explosions = _explosions,
                frameTimingAvailable = r.frameTimingAvailable,
                cpuMainMs = r.cpuMainThreadMsAvg, cpuRenderMs = r.cpuRenderThreadMsAvg, gpuMs = r.gpuMsAvg,
            };
            float scale = GameSettings.RenderScalePercent[Mathf.Clamp(GameSettings.GetInt(SettingId.RenderScale), 0, 5)] / 100f;
            res.renderResolution = Mathf.RoundToInt(r.resolutionWidth * scale) + "x" + Mathf.RoundToInt(r.resolutionHeight * scale);
            res.capNote = uncapped ? "frame cap and VSync ignored" : "cap " + GameSettings.Format(SettingId.FrameRate) + ", VSync " + GameSettings.Format(SettingId.VSync);
            float med = r.frameMsP50;
            foreach (var v in ft) { if (v > 33.3f) res.framesOver33++; if (v > 3f * med && v > 25f) res.hitches++; }
            if (lostFocus) { res.valid = false; res.invalidReason = "the window lost focus during the run"; }
            // 240 slices over the run, each showing its slowest frame so spikes are not averaged away.
            const int Buckets = 240;
            res.graph = new float[Buckets];
            float total = times.Count > 0 ? times[times.Count - 1] : 1f;
            for (int i = 0; i < ft.Count; i++)
            {
                int b = Mathf.Clamp((int)(times[i] / Mathf.Max(0.001f, total) * Buckets), 0, Buckets - 1);
                if (ft[i] > res.graph[b]) res.graph[b] = ft[i];
            }
            // CPU or GPU: from frame timing when the player reports it.
            if (r.frameTimingAvailable && r.frameMsAvg > 0f)
            {
                float f = r.frameMsAvg;
                float gpuShare = r.gpuMsAvg / f, mainShare = r.cpuMainThreadMsAvg / f;
                if (gpuShare >= 0.9f) res.bottleneck = "GPU";
                else if (mainShare >= 0.9f && gpuShare < 0.7f) res.bottleneck = "CPU";
                else if (r.cpuRenderThreadMsAvg / f >= 0.9f) res.bottleneck = "CPU";
                else if (gpuShare >= mainShare + 0.15f) res.bottleneck = "GPU";
                else if (mainShare >= gpuShare + 0.15f) res.bottleneck = "CPU";
                else res.bottleneck = "BALANCED";
                res.bottleneckHow = "frame timing: GPU " + Mathf.RoundToInt(gpuShare * 100f) + "%, CPU main thread " + Mathf.RoundToInt(mainShare * 100f) + "% of the frame";
            }
            else if (probeA > 0f && probeB > 0f)
            {
                float gain = (probeB - probeA) / probeA;
                res.bottleneck = gain < 0.05f ? "CPU" : gain > 0.25f ? "GPU" : "BALANCED";
                res.bottleneckHow = "render scale probe: " + F0(probeA) + " fps at full scale, " + F0(probeB) + " fps at 60% (" + (gain >= 0 ? "+" : "") + F0(gain * 100f) + "%)";
            }
            res.verdict = BenchmarkVerdict.Make(res, FramePacing.RefreshRate);
            return res;
        }

        static string F0(float v) => Mathf.RoundToInt(v).ToString(System.Globalization.CultureInfo.InvariantCulture);

        static float Min(List<float> l) { float m = float.MaxValue; foreach (var v in l) if (v < m) m = v; return m == float.MaxValue ? 0f : m; }

        // -benchmarkExplosions: every 5 s the nearest live car (then barrel, alternating) ahead of the player blows up, so the worst case of
        // blast FX, debris and chain reactions is part of the measurement. Props are reset when none is left to blow.
        void TickExplosions(float t)
        {
            if (!_explosions || t < _nextBoom || PlayerHealth.Instance == null) return;
            _nextBoom = t + 5f;
            var player = PlayerHealth.Instance.transform.position;
            Vector3 aim = player + Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward * 14f;
            var want = _carNext ? ExplosiveKind.Car : ExplosiveKind.Barrel;
            for (int pass = 0; pass < 2; pass++)
            {
                ExplosiveProp best = null;
                float bestD = float.MaxValue;
                var all = ExplosiveProp.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (!p.IsAlive || p.Kind != want) continue;
                    var pp = p.transform.position;
                    float fromPlayer = Vector2.Distance(new Vector2(pp.x, pp.z), new Vector2(player.x, player.z));
                    if (fromPlayer < 7f || fromPlayer > 30f) continue;
                    float d = (new Vector2(pp.x, pp.z) - new Vector2(aim.x, aim.z)).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = p; }
                }
                if (best != null) { best.DetonateNow(false); _boomsFired++; _carNext = !_carNext; return; }
                ExplosiveProp.ResetAll();
                ExplosionFX.Instance?.ResetAll();
            }
            _carNext = !_carNext;
        }

        void Drive()
        {
            var player = PlayerHealth.Instance != null ? PlayerHealth.Instance.transform : null;
            if (player == null) return;
            Vector3 pos = player.position;
            if (_stand)
            {
                _yaw += 20f * Time.unscaledDeltaTime;
                CameraOrbit.ScriptedYaw = _yaw;
                PlayerController.ScriptedMove = Vector2.zero;
                PlayerController.ScriptedSprint = false;
                return;
            }
            // Blocked by the crowd for 2 s: warp to the next waypoint so the route stays repeatable.
            _stuckTimer += Time.unscaledDeltaTime;
            if (_stuckTimer >= 2f)
            {
                if ((pos - _stuckRef).sqrMagnitude < 4f)
                {
                    _wp = (_wp + 1) % Loop.Length;
                    var cc = player.GetComponent<CharacterController>();
                    cc.enabled = false;
                    player.position = new Vector3(Loop[_wp].x, pos.y, Loop[_wp].y);
                    cc.enabled = true;
                    pos = player.position;
                }
                _stuckRef = pos;
                _stuckTimer = 0f;
            }
            Vector2 to = Loop[_wp] - new Vector2(pos.x, pos.z);
            if (to.magnitude < 5f) { _wp = (_wp + 1) % Loop.Length; to = Loop[_wp] - new Vector2(pos.x, pos.z); }
            float goal = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, goal, 150f * Time.unscaledDeltaTime);
            CameraOrbit.ScriptedYaw = _yaw;
            PlayerController.ScriptedMove = new Vector2(0f, 1f);
            PlayerController.ScriptedSprint = true;
        }

        // -bn=flag1,flag2: switch one cost off at a time to see what it is worth (diagnosis only; nothing here ships on).
        void ApplyExperiments()
        {
            string csv = null;
            foreach (var a in Environment.GetCommandLineArgs()) if (a.StartsWith("-bn=", StringComparison.OrdinalIgnoreCase)) csv = a.Substring(4).ToLowerInvariant();
            if (string.IsNullOrEmpty(csv)) return;
            var flags = new HashSet<string>(csv.Split(','));
            var urp = (GraphicsApplier.Asset != null ? GraphicsApplier.Asset : GraphicsSettings.currentRenderPipeline) as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            var camData = Camera.main != null ? Camera.main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null;
            if (flags.Contains("noshadow") && urp != null) urp.shadowDistance = 0.1f;
            if (flags.Contains("shadow60") && urp != null) { urp.shadowDistance = 60f; urp.shadowCascadeCount = 3; }
            if (flags.Contains("nopost") && camData != null) camData.renderPostProcessing = false;
            if (flags.Contains("notaa") && camData != null) camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
            if (flags.Contains("noknee")) KneeAlign.Disabled = true;
            var enemies = FindObjectsByType<Enemy>(FindObjectsInactive.Include);
            foreach (var e in enemies)
            {
                if (flags.Contains("noanim")) foreach (var an in e.GetComponentsInChildren<Animator>(true)) an.enabled = false;
                if (flags.Contains("noenemyshadow")) foreach (var r in e.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (flags.Contains("nohitboxes")) foreach (var c in e.GetComponentsInChildren<Collider>(true)) if (c.GetComponent<Hitbox>() != null) c.enabled = false;
                if (flags.Contains("noavoid")) { var ag = e.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (ag != null) ag.obstacleAvoidanceType = UnityEngine.AI.ObstacleAvoidanceType.NoObstacleAvoidance; }
                if (flags.Contains("noenemy")) e.enabled = false;
                if (flags.Contains("norender")) foreach (var r in e.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;
                if (flags.Contains("nomv")) foreach (var r in e.GetComponentsInChildren<Renderer>(true)) { r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion; var sm = r as SkinnedMeshRenderer; if (sm != null) sm.skinnedMotionVectors = false; }
                if (flags.Contains("lowskin")) foreach (var sm in e.GetComponentsInChildren<SkinnedMeshRenderer>(true)) sm.quality = SkinQuality.Bone2;
            }
            if (flags.Contains("nossao"))
            {
                var f = urp != null ? urp.GetType().GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;
                if (f != null) foreach (var rd in (UnityEngine.Object[])f.GetValue(urp)) foreach (var ft in ((UnityEngine.Rendering.Universal.ScriptableRendererData)rd).rendererFeatures) if (ft != null && ft.name.Contains("AmbientOcclusion")) ft.SetActive(false);
            }
            Debug.Log("[Benchmark] experiments: " + csv);
        }

        static bool HasArg(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs()) if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static void Fill(Report r, List<float> ft)
        {
            var s = new List<float>(ft);
            s.Sort();
            float sum = 0;
            foreach (var v in s) sum += v;
            r.frameMsAvg = sum / s.Count;
            r.avgFps = 1000f * s.Count / sum;
            r.frameMsP50 = Pct(s, 50, true); r.frameMsP95 = Pct(s, 95, true); r.frameMsP99 = Pct(s, 99, true);
            r.frameMsMax = s[s.Count - 1];
            r.low1PctFps = 1000f / WorstMean(s, 0.01f);
            r.low01PctFps = 1000f / WorstMean(s, 0.001f);
        }

        static float WorstMean(List<float> sorted, float frac)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(sorted.Count * frac));
            float sum = 0;
            for (int i = sorted.Count - n; i < sorted.Count; i++) sum += sorted[i];
            return sum / n;
        }

        static float Avg(List<float> l)
        {
            if (l.Count == 0) return 0;
            float s = 0;
            foreach (var v in l) s += v;
            return s / l.Count;
        }

        static float Pct(List<float> l, float p, bool sorted = false)
        {
            if (l.Count == 0) return 0;
            var s = sorted ? l : new List<float>(l);
            if (!sorted) s.Sort();
            return s[Mathf.Clamp(Mathf.CeilToInt(p / 100f * s.Count) - 1, 0, s.Count - 1)];
        }

        static void Write(Report r)
        {
            string json = JsonUtility.ToJson(r, true);
            string dir;
            try
            {
                dir = Directory.GetParent(Application.dataPath).FullName;
                File.WriteAllText(Path.Combine(dir, "benchmark.json"), json);
            }
            catch (Exception)
            {
                dir = Application.persistentDataPath;
                try { File.WriteAllText(Path.Combine(dir, "benchmark.json"), json); }
                catch (Exception e) { Debug.LogError("[Benchmark] cannot write report: " + e.Message); }
            }
            Debug.Log("[Benchmark] wrote " + Path.Combine(dir, "benchmark.json"));
        }
    }
}
