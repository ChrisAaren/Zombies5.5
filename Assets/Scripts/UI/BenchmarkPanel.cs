using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Zombies55
{
    // The BENCHMARK tab of the settings menu: run options, the history list and the results screen (numbers, frame-time graph,
    // settings used, system info, verdict and a side-by-side comparison with the previous run or any chosen run).
    public class BenchmarkPanel : MonoBehaviour
    {
        // Raised when the home or results view is shown: the menu footer follows.
        public event System.Action ViewChanged;

        [Header("Home")]
        [SerializeField] GameObject home;
        [SerializeField] TMP_Text summaryText;
        [SerializeField] TMP_Text noteText;
        [SerializeField] Button runButton;
        [SerializeField] Button viewLastButton;
        [SerializeField] RectTransform historyContent;
        [SerializeField] Button historyTemplate;
        [SerializeField] TMP_Text historyEmptyText;

        [Header("Results")]
        [SerializeField] GameObject results;
        [SerializeField] TMP_Text bigText;
        [SerializeField] TMP_Text systemText;
        [SerializeField] TMP_Text statsText;
        [SerializeField] TMP_Text settingsText;
        [SerializeField] TMP_Text compareText;
        [SerializeField] TMP_Text compareDiffText;
        [SerializeField] TMP_Text verdictText;
        [SerializeField] TMP_Text legendText;
        [SerializeField] GraphicsGraph graph;
        [SerializeField] Button compareButton;
        [SerializeField] TMP_Text compareButtonLabel;
        [SerializeField] Button againButton;
        [SerializeField] Button deleteButton;
        [SerializeField] Button backButton;

        const float Seconds = 60f;   // the measured window of a menu run (the user-facing benchmark length)

        readonly List<GameObject> _rows = new List<GameObject>();
        BenchmarkResult _shown;
        int _compareIndex = -1;   // index into the history, -1 = no comparison
        bool _wired;
        string _note;

        public GameObject FirstSelectable => runButton != null ? runButton.gameObject : null;
        public bool ShowingResults => results != null && results.activeSelf;

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            runButton.onClick.AddListener(Run);
            viewLastButton.onClick.AddListener(() => { Click(); var h = BenchmarkHistory.Runs; if (h.Count > 0) ShowResult(h[0]); });
            againButton.onClick.AddListener(Run);
            backButton.onClick.AddListener(() => { Click(); ShowHome(); });
            compareButton.onClick.AddListener(CycleCompare);
            deleteButton.onClick.AddListener(DeleteShown);
        }

        static void Click() => GameAudio.Instance?.PlayClick();

        void OnEnable()
        {
            Wire();
            GameSettings.Changed += RefreshHome;
            if (!ShowingResults) ShowHome();
        }

        void OnDisable() => GameSettings.Changed -= RefreshHome;

        // ---------------------------------------------------------------- home

        public void ShowHome()
        {
            home.SetActive(true);
            results.SetActive(false);
            ViewChanged?.Invoke();
            RefreshHome();
            Select(runButton);
        }

        static void Select(Selectable s)
        {
            if (EventSystem.current != null && s != null && s.gameObject.activeInHierarchy) EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        void RefreshHome()
        {
            if (!home.activeSelf) return;
            var sb = new StringBuilder();
            string hex = ColorUtility.ToHtmlStringRGB(Palette.Accent);
            sb.Append("<color=#").Append(hex).Append(">THESE SETTINGS WILL BE MEASURED</color>\n");
            sb.Append("RESOLUTION  ").Append(Screen.width).Append("x").Append(Screen.height).Append("   ").Append(Screen.fullScreenMode == FullScreenMode.Windowed ? "WINDOWED" : "BORDERLESS").Append('\n');
            foreach (var line in GraphicsApplier.Snapshot()) sb.Append(Pretty(line)).Append('\n');
            summaryText.text = sb.ToString();

            if (!string.IsNullOrEmpty(BenchmarkRunner.Note)) { _note = BenchmarkRunner.Note; BenchmarkRunner.Note = null; }
            string note = _note;
            noteText.text = string.IsNullOrEmpty(note)
                ? "A FIXED ROUTE THROUGH THE CITY WITH ALL DISTRICTS OPEN AND 48 ENEMIES. ABOUT 70 SECONDS. ESC CANCELS."
                : note;
            runButton.interactable = !BenchmarkRunner.Active;

            foreach (var r in _rows) if (r != null) Destroy(r);
            _rows.Clear();
            var runs = BenchmarkHistory.Runs;
            historyEmptyText.gameObject.SetActive(runs.Count == 0);
            viewLastButton.interactable = runs.Count > 0;
            for (int i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                var row = Instantiate(historyTemplate, historyContent);
                row.gameObject.SetActive(true);
                var label = row.GetComponentInChildren<TMP_Text>();
                label.text = run.Label + "  " + run.preset + "  " + Mathf.RoundToInt(run.avgFps) + " / " + Mathf.RoundToInt(run.low1PctFps);
                row.onClick.AddListener(() => { Click(); ShowResult(run); });
                _rows.Add(row.gameObject);
            }
        }

        static string Pretty(string kv)
        {
            int eq = kv.IndexOf('=');
            if (eq < 0) return kv;
            string name = System.Text.RegularExpressions.Regex.Replace(kv.Substring(0, eq), "([a-z])([A-Z])", "$1 $2").ToUpperInvariant();
            return name + "  <color=#" + ColorUtility.ToHtmlStringRGB(Palette.Accent) + ">" + kv.Substring(eq + 1) + "</color>";
        }

        void Run()
        {
            if (BenchmarkRunner.Active) return;
            _note = null;
            Click();
            GameSettings.Save();
            BenchmarkRunner.BeginFromMenu(new BenchmarkOptions
            {
                Seconds = Seconds,
                Uncapped = GameSettings.GetBool(SettingId.BenchUncapped),
                Explosions = GameSettings.GetBool(SettingId.BenchExplosions),
            });
        }

        // ---------------------------------------------------------------- results

        public void ShowResult(BenchmarkResult r)
        {
            if (r == null) { ShowHome(); return; }
            _shown = r;
            home.SetActive(false);
            results.SetActive(true);
            ViewChanged?.Invoke();
            // Default comparison: the run before this one.
            var runs = BenchmarkHistory.Runs;
            int at = IndexOf(r);
            _compareIndex = at >= 0 && at + 1 < runs.Count ? at + 1 : -1;
            Refresh();
            Select(againButton);
        }

        static int IndexOf(BenchmarkResult r)
        {
            var runs = BenchmarkHistory.Runs;
            for (int i = 0; i < runs.Count; i++) if (runs[i].id == r.id) return i;
            return -1;
        }

        void CycleCompare()
        {
            Click();
            var runs = BenchmarkHistory.Runs;
            int self = IndexOf(_shown);
            int next = _compareIndex;
            for (int guard = 0; guard <= runs.Count + 1; guard++)
            {
                next++;
                if (next >= runs.Count) { next = -1; break; }
                if (next != self) break;
            }
            _compareIndex = next;
            Refresh();
        }

        void DeleteShown()
        {
            Click();
            if (_shown != null) BenchmarkHistory.Delete(_shown.id);
            _shown = null;
            ShowHome();
        }

        BenchmarkResult Other
        {
            get
            {
                var runs = BenchmarkHistory.Runs;
                return _compareIndex >= 0 && _compareIndex < runs.Count ? runs[_compareIndex] : null;
            }
        }

        static string F0(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
        static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        void Refresh()
        {
            var r = _shown;
            if (r == null) return;
            string acc = ColorUtility.ToHtmlStringRGB(Palette.Accent);
            bigText.text =
                "<size=22>AVERAGE FPS</size>\n<size=84><color=#" + acc + ">" + F0(r.avgFps) + "</color></size>\n" +
                "<size=26>1% LOW  <color=#" + acc + ">" + F0(r.low1PctFps) + "</color>\n0.1% LOW  <color=#" + acc + ">" + F0(r.low01PctFps) + "</color>\n" +
                "MIN  <color=#" + acc + ">" + F0(r.minFps) + "</color>    MAX  <color=#" + acc + ">" + F0(r.maxFps) + "</color></size>";

            systemText.text =
                "<color=#" + acc + ">SYSTEM</color>\n" +
                "GPU  " + r.gpu + " (" + (r.vramMb / 1024f).ToString("0.#", CultureInfo.InvariantCulture) + " GB, " + r.api + ")\n" +
                "CPU  " + r.cpu + " (" + r.cores + " threads)\n" +
                "RAM  " + (r.ramMb / 1024f).ToString("0.#", CultureInfo.InvariantCulture) + " GB\n" +
                "DISPLAY  " + r.display + "\n" +
                "RESOLUTION  " + r.resolution + " (RENDERED " + r.renderResolution + ")\n" +
                "MODE  " + r.mode + ", " + r.refresh + "\n" +
                "RUN  " + r.capNote + (r.explosions ? ", explosions" : "") +
                (!string.IsNullOrEmpty(r.bottleneckHow) ? "\nLIMIT  " + r.bottleneck + " (" + r.bottleneckHow + ")" : "");

            statsText.text =
                "FRAME TIME  AVG " + F1(r.frameMsAvg) + "   P50 " + F1(r.frameMsP50) + "   P95 " + F1(r.frameMsP95) + "   P99 " + F1(r.frameMsP99) + "   MAX " + F1(r.frameMsMax) + " MS\n" +
                "FRAMES OVER 8.3 MS " + r.framesOver8_3 + "   16.7 MS " + r.framesOver16_6 + "   33 MS " + r.framesOver33 + "   HITCHES " + r.hitches +
                "";

            var sb = new StringBuilder();
            sb.Append("<color=#").Append(acc).Append(">SETTINGS USED</color>\n");
            if (r.settings != null) foreach (var s in r.settings) sb.Append(Pretty(s)).Append('\n');
            settingsText.text = sb.ToString();

            verdictText.text = r.verdict;
            var other = Other;
            graph.Set(r.graph, other != null ? other.graph : null, new[] { 1000f / 60f, 1000f / 120f },
                new[] { Palette.WithAlpha(Palette.Danger, 0.7f), Palette.WithAlpha(Palette.CharacterPrimary, 0.8f) });
            legendText.text = "FRAME TIME (MS), MAX 0-" + F0(graph.YMax) + "    <color=#" + ColorUtility.ToHtmlStringRGB(Palette.Danger) + ">60 FPS</color>    <color=#" + ColorUtility.ToHtmlStringRGB(Palette.CharacterPrimary) + ">120 FPS</color>    SOLID: THIS RUN" + (other != null ? "    DASHED: COMPARED" : "");
            compareButtonLabel.text = other == null ? "COMPARE: OFF" : "VS " + other.Label + " " + other.preset;
            if (other == null)
            {
                compareText.text = "<color=#" + acc + ">COMPARE</color>\nCHOOSE A PREVIOUS RUN WITH THE COMPARE BUTTON\nTO SEE WHAT A SETTINGS CHANGE DID.";
                compareDiffText.text = string.Empty;
            }
            else
            {
                compareText.text = Compare(other, r, out string diff);
                compareDiffText.text = diff;
            }
        }

        static string Delta(float was, float now, bool higherIsBetter, string fmt)
        {
            float pct = was > 0.0001f ? (now - was) / was * 100f : 0f;
            bool good = higherIsBetter ? pct >= 0f : pct <= 0f;
            string col = Mathf.Abs(pct) < 1f ? ColorUtility.ToHtmlStringRGB(Palette.UiLight) : good ? ColorUtility.ToHtmlStringRGB(Palette.CrosshairGreen) : ColorUtility.ToHtmlStringRGB(Palette.Danger);
            return "<color=#" + col + ">" + (pct >= 0 ? "+" : "") + pct.ToString("0.0", CultureInfo.InvariantCulture) + "%</color>";
        }

        static string Compare(BenchmarkResult was, BenchmarkResult now, out string diffText)
        {
            string acc = ColorUtility.ToHtmlStringRGB(Palette.Accent);
            var sb = new StringBuilder();
            sb.Append("<color=#").Append(acc).Append(">COMPARED RUN  ->  THIS RUN</color>");
            if (was.scenario != now.scenario) sb.Append("   <color=#").Append(ColorUtility.ToHtmlStringRGB(Palette.Danger)).Append(">DIFFERENT TEST ROUTE: NOT COMPARABLE</color>");
            else if (was.resolution != now.resolution) sb.Append("   <color=#").Append(ColorUtility.ToHtmlStringRGB(Palette.Danger)).Append(">DIFFERENT RESOLUTION</color>");
            sb.Append('\n');
            void Row(string name, float a, float b, bool hib, bool dec = false)
            {
                string fa = dec ? F1(a) : F0(a), fb = dec ? F1(b) : F0(b);
                sb.Append(name).Append("<pos=135>").Append(fa).Append("<pos=205>").Append(fb).Append("<pos=270>").Append(Delta(a, b, hib, "")).Append('\n');
            }
            Row("AVG FPS", was.avgFps, now.avgFps, true);
            Row("1% LOW", was.low1PctFps, now.low1PctFps, true);
            Row("0.1% LOW", was.low01PctFps, now.low01PctFps, true);
            Row("MIN FPS", was.minFps, now.minFps, true);
            Row("MAX FPS", was.maxFps, now.maxFps, true);
            var d = new StringBuilder();
            d.Append("<color=#").Append(acc).Append(">SETTINGS THAT DIFFER</color>\n");
            int diffs = 0;
            if (was.settings != null && now.settings != null)
            {
                var map = new Dictionary<string, string>();
                foreach (var s in was.settings) { int e = s.IndexOf('='); if (e > 0) map[s.Substring(0, e)] = s.Substring(e + 1); }
                foreach (var s in now.settings)
                {
                    int e = s.IndexOf('='); if (e <= 0) continue;
                    string k = s.Substring(0, e), v = s.Substring(e + 1);
                    if (map.TryGetValue(k, out var old) && old != v && k != "QualityPreset")
                    {
                        if (diffs++ < 9) d.Append(System.Text.RegularExpressions.Regex.Replace(k, "([a-z])([A-Z])", "$1 $2").ToUpperInvariant()).Append(":  ").Append(old).Append("  ->  ").Append(v).Append('\n');
                    }
                }
            }
            if (diffs == 0) d.Append("NONE (SAME SETTINGS)\n");
            else if (diffs > 9) d.Append("+ ").Append(diffs - 9).Append(" MORE\n");
            diffText = d.ToString();
            return sb.ToString();
        }
    }
}
