using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Zombies55
{
    // What the player chose for a menu run.
    public struct BenchmarkOptions
    {
        public float Seconds;       // measured window (the run also warms up for 5 s first)
        public bool Uncapped;       // ignore the frame cap and VSync during the run
        public bool Explosions;     // blow up a car or barrel near the route every 5 s
    }

    // One finished run. Everything needed to show it again later is stored (JsonUtility, so public fields only).
    [Serializable]
    public class BenchmarkResult
    {
        public const int ScenarioVersion = 1;   // bump when the route, enemy count or warm-up changes: older runs stop being comparable

        public string id, utc, gameVersion, build;
        public int scenario = ScenarioVersion;
        public float seconds;
        public int frames;
        public float avgFps, low1PctFps, low01PctFps, minFps, maxFps;
        public float frameMsAvg, frameMsP50, frameMsP95, frameMsP99, frameMsMax;
        public int hitches, framesOver8_3, framesOver16_6, framesOver33;
        public float[] graph;               // frame time (ms), the slowest frame of each of 240 equal time slices
        public string preset;
        public string[] settings;           // "SHADOW QUALITY=HIGH" style lines
        public string gpu, cpu, os, api, display, resolution, renderResolution, mode, refresh;
        public int vramMb, ramMb, cores;
        public bool uncapped, explosions;
        public string capNote;              // what the cap and VSync were during the run
        public bool frameTimingAvailable;
        public float cpuMainMs, cpuRenderMs, gpuMs;
        public string bottleneck;           // "GPU", "CPU", "BALANCED" or ""
        public string bottleneckHow;        // frame timing or render scale probe
        public bool valid = true;
        public string invalidReason;
        public string verdict;

        public string Label
        {
            get
            {
                if (DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
                    return t.ToLocalTime().ToString("MMM dd HH:mm", CultureInfo.InvariantCulture).ToUpperInvariant();
                return utc;
            }
        }

        public string Summary => Label + "   " + preset + "   " + Mathf.RoundToInt(avgFps) + " AVG   " + Mathf.RoundToInt(low1PctFps) + " 1% LOW   " + resolution;
    }

    [Serializable]
    class BenchmarkHistoryFile { public List<BenchmarkResult> runs = new List<BenchmarkResult>(); }

    // benchmark_history.json in persistentDataPath: the newest 20 runs, newest first. Written through a temp file; a corrupt file is
    // renamed to .bak and the history starts empty.
    public static class BenchmarkHistory
    {
        public const int MaxRuns = 20;
        static List<BenchmarkResult> s_Runs;

        static string PathFile => Path.Combine(Application.persistentDataPath, "benchmark_history.json");
        // Test runs and the Editor write to a separate file so they never touch the player's history.
        static string FilePath => GameSettings.Ephemeral || Application.isEditor ? Path.Combine(Application.persistentDataPath, "benchmark_history_test.json") : PathFile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Runs = null;

        public static IReadOnlyList<BenchmarkResult> Runs
        {
            get
            {
                if (s_Runs == null) Load();
                return s_Runs;
            }
        }

        static void Load()
        {
            s_Runs = new List<BenchmarkResult>();
            string path = FilePath;
            try
            {
                if (!File.Exists(path)) return;
                var f = JsonUtility.FromJson<BenchmarkHistoryFile>(File.ReadAllText(path));
                if (f?.runs != null) s_Runs.AddRange(f.runs);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Benchmark] history unreadable (" + e.Message + "), starting empty");
                try { File.Move(path, path + ".bak"); } catch (Exception) { /* nothing more to do */ }
                s_Runs = new List<BenchmarkResult>();
            }
        }

        public static void Add(BenchmarkResult r)
        {
            if (s_Runs == null) Load();
            s_Runs.Insert(0, r);
            if (s_Runs.Count > MaxRuns) s_Runs.RemoveRange(MaxRuns, s_Runs.Count - MaxRuns);
            Save();
        }

        public static void Delete(string id)
        {
            if (s_Runs == null) Load();
            s_Runs.RemoveAll(x => x.id == id);
            Save();
        }

        static void Save()
        {
            try
            {
                string path = FilePath, tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(new BenchmarkHistoryFile { runs = s_Runs }, false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning("[Benchmark] cannot save history: " + e.Message); }
        }
    }

    public static class BenchmarkVerdict
    {
        // One sentence about the frame rate and stutter, one about the bottleneck (settings from that side only).
        public static string Make(BenchmarkResult r, double refreshHz)
        {
            string head;
            if (!r.valid) head = "Result may be unreliable: " + r.invalidReason + ".";
            else if (r.low01PctFps < 0.35f * r.avgFps || r.hitches > 20)
                head = "Smooth on average, but it stutters: the slowest 0.1% of frames ran at " + Mathf.RoundToInt(r.low01PctFps) + " fps.";
            else if (!r.uncapped && refreshHz > 1 && r.low1PctFps >= refreshHz * 0.9)
                head = "Locked to your monitor's refresh rate.";
            else if (r.avgFps >= 144f && r.low1PctFps >= 90f) head = "Excellent: this handles a 144 Hz display.";
            else if (r.avgFps >= 90f && r.low1PctFps >= 60f) head = "Very smooth, 90+ fps.";
            else if (r.avgFps >= 60f && r.low1PctFps >= 45f) head = "Smooth at 60 fps.";
            else if (r.avgFps >= 45f) head = "Playable, but not smooth everywhere.";
            else head = "Below 45 fps.";

            string bn = "";
            if (r.bottleneck == "GPU") bn = " The graphics card is the limit: lower Render Scale, Shadow Quality, Ambient Occlusion or Anti-aliasing.";
            else if (r.bottleneck == "CPU") bn = " The processor is the limit: lower Effects Quality, Street Detail, Character Detail or Object Detail.";
            else if (r.bottleneck == "BALANCED") bn = " The processor and graphics card are about equally loaded.";
            return head + bn;
        }
    }
}
