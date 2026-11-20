using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Zombies55
{
    // The BENCHMARK tab of the settings menu. Home: your settings (what differs from the preset), the run button and options, and a
    // table of past runs. Results: two big headline numbers with a rating, the plain-language verdict, the frame-time graph, and a
    // DETAILS view for everything else. A previous run can be compared: the change shows next to the headline numbers.
    public class BenchmarkPanel : MonoBehaviour
    {
        // Raised when the home or results view is shown: the menu footer follows.
        public event System.Action ViewChanged;

        [Header("Home")]
        [SerializeField] GameObject home;
        [SerializeField] TMP_Text presetText;
        [SerializeField] TMP_Text displayText;
        [SerializeField] TMP_Text changedText;
        [SerializeField] TMP_Text noteText;
        [SerializeField] Button runButton;
        [SerializeField] RectTransform historyContent;
        [SerializeField] Button historyTemplate;
        [SerializeField] TMP_Text historyEmptyText;

        [Header("Results")]
        [SerializeField] GameObject results;
        [SerializeField] TMP_Text avgNumber;
        [SerializeField] TMP_Text avgInfo;
        [SerializeField] TMP_Text avgDelta;
        [SerializeField] TMP_Text lowNumber;
        [SerializeField] TMP_Text lowInfo;
        [SerializeField] TMP_Text lowDelta;
        [SerializeField] GameObject summaryGroup;
        [SerializeField] GameObject detailsGroup;
        [SerializeField] TMP_Text verdictText;
        [SerializeField] TMP_Text captionText;
        [SerializeField] GraphicsGraph graph;
        [SerializeField] RectTransform graphLabel60;
        [SerializeField] RectTransform graphLabel120;
        [SerializeField] TMP_Text timesText;
        [SerializeField] TMP_Text systemText;
        [SerializeField] TMP_Text settingsTextA;
        [SerializeField] TMP_Text settingsTextB;
        [SerializeField] Button compareButton;
        [SerializeField] TMP_Text compareButtonLabel;
        [SerializeField] Button againButton;
        [SerializeField] Button detailsButton;
        [SerializeField] TMP_Text detailsButtonLabel;
        [SerializeField] Button deleteButton;
        [SerializeField] Button backButton;

        const float Seconds = 60f;   // the measured window of a menu run (the user-facing benchmark length)
        const int MaxChanged = 5;

        readonly List<GameObject> _rows = new List<GameObject>();
        BenchmarkResult _shown;
        int _compareIndex = -1;   // index into the history, -1 = no comparison
        bool _wired;
        bool _details;
        string _note;

        public GameObject FirstSelectable => runButton != null ? runButton.gameObject : null;
        public bool ShowingResults => results != null && results.activeSelf;

        // Test hooks (the clipping check): show a result without touching the history, and flip the details view.
        public bool DetailsShown => _details;
        public void SetDetails(bool on) { _details = on; if (_shown != null) ApplyDetails(); }

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            runButton.onClick.AddListener(Run);
            againButton.onClick.AddListener(Run);
            backButton.onClick.AddListener(() => { Click(); ShowHome(); });
            compareButton.onClick.AddListener(CycleCompare);
            detailsButton.onClick.AddListener(() => { Click(); _details = !_details; ApplyDetails(); });
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

        static string Acc => ColorUtility.ToHtmlStringRGB(Palette.Accent);
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

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

        // "MotionBlur" -> "Motion Blur"
        static string Title(string id) => System.Text.RegularExpressions.Regex.Replace(id, "([a-z])([A-Z])", "$1 $2");

        void RefreshHome()
        {
            if (!home.activeSelf) return;
            int preset = GameSettings.QualityPreset;
            string presetName = GameSettings.PresetNames[Mathf.Clamp(preset, 0, GameSettings.PresetCustom)];
            presetText.text = presetName;
            displayText.text = Screen.width + "x" + Screen.height + "  ·  " + (Screen.fullScreenMode == FullScreenMode.Windowed ? "WINDOWED" : "BORDERLESS");

            var ids = new List<SettingId>();
            string header;
            if (preset < GameSettings.PresetCustom)
            {
                foreach (var id in GameSettings.PresetIds)
                    if (GameSettings.GetInt(id) != GameSettings.PresetValue(preset, id)) ids.Add(id);
                header = ids.Count == 0 ? "Matches the " + presetName + " preset" : "Changed from " + presetName;
            }
            else
            {
                ids.AddRange(new[] { SettingId.RenderScale, SettingId.ShadowQuality, SettingId.AmbientOcclusion, SettingId.AntiAliasing, SettingId.EffectsQuality });
                header = "Key settings";
            }
            var sb = new StringBuilder();
            sb.Append("<color=#").Append(Acc).Append(">").Append(header).Append("</color>");
            for (int i = 0; i < ids.Count && i < MaxChanged; i++)
                sb.Append('\n').Append(Title(ids[i].ToString())).Append("  <color=#").Append(Acc).Append(">").Append(GameSettings.Format(ids[i])).Append("</color>");
            if (ids.Count > MaxChanged) sb.Append("\n+").Append(ids.Count - MaxChanged).Append(" more");
            changedText.text = sb.ToString();

            if (!string.IsNullOrEmpty(BenchmarkRunner.Note)) { _note = BenchmarkRunner.Note; BenchmarkRunner.Note = null; }
            noteText.text = string.IsNullOrEmpty(_note) ? "About 70 seconds  ·  48 enemies  ·  Esc cancels" : _note;
            runButton.interactable = !BenchmarkRunner.Active;

            foreach (var r in _rows) if (r != null) { r.SetActive(false); Destroy(r); }
            _rows.Clear();
            var runs = BenchmarkHistory.Runs;
            historyEmptyText.gameObject.SetActive(runs.Count == 0);
            for (int i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                var row = Instantiate(historyTemplate, historyContent);
                row.gameObject.SetActive(true);
                var cells = row.GetComponentsInChildren<TMP_Text>();
                cells[0].text = run.Label;
                cells[1].text = run.preset;
                cells[2].text = F0(run.avgFps);
                cells[3].text = F0(run.low1PctFps);
                row.onClick.AddListener(() => { Click(); ShowResult(run); });
                _rows.Add(row.gameObject);
            }
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
            _details = false;
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

        // Test hook: compare against a given run (or none) without needing it in the history.
        public void SetCompare(int historyIndex)
        {
            _compareIndex = historyIndex;
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

        void ApplyDetails()
        {
            summaryGroup.SetActive(!_details);
            detailsGroup.SetActive(_details);
            detailsButtonLabel.text = _details ? "SUMMARY" : "DETAILS";
        }

        static string F0(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
        static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        // GREAT / GOOD / PLAYABLE / POOR for a frame rate.
        static void Rate(float fps, out string word, out Color color)
        {
            if (fps >= 120f) { word = "GREAT"; color = Palette.CrosshairGreen; }
            else if (fps >= 60f) { word = "GOOD"; color = Palette.Accent; }
            else if (fps >= 30f) { word = "PLAYABLE"; color = Palette.FireOrange; }
            else { word = "POOR"; color = Palette.Danger; }
        }

        static void Headline(TMP_Text number, TMP_Text info, TMP_Text delta, string label, string caption, float fps, float was, bool hasWas, string wasLabel)
        {
            Rate(fps, out string word, out Color col);
            string c = Hex(col);
            number.text = "<color=#" + c + ">" + F0(fps) + "</color>";
            info.text = label + "   <color=#" + c + ">" + word + "</color>\n<alpha=#B0>" + caption;
            if (!hasWas) { delta.text = string.Empty; return; }
            int d = Mathf.RoundToInt(fps - was);
            int pct = was > 0.0001f ? Mathf.RoundToInt((fps - was) / was * 100f) : 0;
            Color dc = pct == 0 && d == 0 ? Palette.UiLight : d >= 0 ? Palette.CrosshairGreen : Palette.Danger;
            delta.text = "<color=#" + Hex(dc) + ">" + (d >= 0 ? "+" : "-") + Mathf.Abs(d) + " fps (" + (pct >= 0 ? "+" : "-") + Mathf.Abs(pct) + "%)</color>   <alpha=#B0>vs " + wasLabel;
        }

        void Refresh()
        {
            var r = _shown;
            if (r == null) return;
            var other = Other;

            Headline(avgNumber, avgInfo, avgDelta, "AVG FPS", "average over the run", r.avgFps, other != null ? other.avgFps : 0f, other != null, other != null ? other.Label : "");
            Headline(lowNumber, lowInfo, lowDelta, "1% LOW", "slowest 1% of frames", r.low1PctFps, other != null ? other.low1PctFps : 0f, other != null, other != null ? other.Label : "");

            verdictText.text = r.verdict;

            graph.Set(r.graph, other != null ? other.graph : null, new[] { 1000f / 60f, 1000f / 120f },
                new[] { Palette.WithAlpha(Palette.Danger, 0.7f), Palette.WithAlpha(Palette.CharacterPrimary, 0.8f) });
            PlaceGraphLabel(graphLabel60, 1000f / 60f);
            PlaceGraphLabel(graphLabel120, 1000f / 120f);
            string caption = "Frame time during the run: lower is smoother, spikes are stutters.";
            if (other != null)
            {
                caption += "  Dashed line: " + other.Label + ".";
                if (other.scenario != r.scenario) caption += "  <color=#" + Hex(Palette.Danger) + ">Different test route: not comparable.</color>";
                else if (other.resolution != r.resolution) caption += "  <color=#" + Hex(Palette.Danger) + ">Different resolution.</color>";
            }
            captionText.text = caption;
            compareButtonLabel.text = other == null ? "COMPARE: OFF" : "COMPARE: " + other.Label;

            BuildDetails(r);
            ApplyDetails();
        }

        void PlaceGraphLabel(RectTransform label, float ms)
        {
            bool visible = ms < graph.YMax;
            label.gameObject.SetActive(visible);
            if (!visible) return;
            float h = graph.rectTransform.rect.height;
            var pos = label.anchoredPosition;
            pos.y = graph.rectTransform.offsetMin.y + ms / graph.YMax * h;
            label.anchoredPosition = pos;
        }

        // ---------------------------------------------------------------- details

        static string Head(string t) => "<color=#" + Acc + "><size=20>" + t + "</size></color>\n";
        static string Line(string label, string value, string pos = "<pos=290>") => label + pos + "<color=#" + Acc + ">" + value + "</color>\n";
        static string Dim(string t) => "<alpha=#B0>" + t + "<alpha=#FF>\n";

        // "render scale probe: 272 fps at full scale, 212 fps at 60% (-22%)" -> "Render-scale test: 272 fps at 100%, 212 fps at 60%"
        static string ProbeText(string how)
        {
            var m = System.Text.RegularExpressions.Regex.Match(how, @"(\d+) fps at full scale, (\d+) fps at (\d+)%");
            return m.Success ? "Render-scale test: " + m.Groups[1].Value + " fps at 100%, " + m.Groups[2].Value + " fps at " + m.Groups[3].Value + "%" : how;
        }

        void BuildDetails(BenchmarkResult r)
        {
            var t = new StringBuilder();
            t.Append(Head("FRAME TIME"));
            t.Append(Line("Median frame", F1(r.frameMsP50) + " ms"));
            t.Append(Line("Average frame", F1(r.frameMsAvg) + " ms"));
            t.Append(Line("95% of frames under", F1(r.frameMsP95) + " ms"));
            t.Append(Line("99% of frames under", F1(r.frameMsP99) + " ms"));
            t.Append(Line("Slowest frame", F1(r.frameMsMax) + " ms"));
            t.Append(Line("Lowest 0.1% of frames", F0(r.low01PctFps) + " fps"));
            t.Append(Line("Lowest / highest", F0(r.minFps) + " / " + F0(r.maxFps)));
            t.Append("<size=40%> </size>\n");
            t.Append(Head("SLOW FRAMES"));
            t.Append(Line("Over 8.3 ms (under 120 fps)", r.framesOver8_3.ToString()));
            t.Append(Line("Over 16.7 ms (under 60 fps)", r.framesOver16_6.ToString()));
            t.Append(Line("Over 33 ms (under 30 fps)", r.framesOver33.ToString()));
            t.Append(Line("Hitches", r.hitches.ToString()));
            timesText.text = t.ToString();

            var sy = new StringBuilder();
            sy.Append(Head("SYSTEM"));
            const string P = "<pos=120>";
            sy.Append(Line("Graphics card", r.gpu, P));
            sy.Append(Line("Video memory", (r.vramMb / 1024f).ToString("0.#", CultureInfo.InvariantCulture) + " GB", P));
            sy.Append(Line("Graphics API", r.api, P));
            sy.Append(Line("Processor", r.cpu.Replace("(R)", "").Replace("(TM)", "").Replace(" Processor", "").Replace("  ", " ").Trim(), P));
            sy.Append(Line("Threads", r.cores.ToString(), P));
            sy.Append(Line("Memory", (r.ramMb / 1024f).ToString("0.#", CultureInfo.InvariantCulture) + " GB", P));
            sy.Append(Line("Display", r.display, P));
            sy.Append(Line("Resolution", r.resolution + (r.renderResolution != r.resolution ? " (rendered " + r.renderResolution + ")" : ""), P));
            sy.Append(Line("Mode", r.mode + ", " + r.refresh, P));
            sy.Append("<size=40%> </size>\n");
            sy.Append(Head("RUN"));
            string cap = string.IsNullOrEmpty(r.capNote) ? "" : char.ToUpperInvariant(r.capNote[0]) + r.capNote.Substring(1);
            sy.Append(cap).Append('\n');
            sy.Append(r.explosions ? "Explosions on\n" : "Explosions off\n");
            if (!string.IsNullOrEmpty(r.bottleneck))
            {
                sy.Append("Limit: <color=#").Append(Acc).Append(">").Append(r.bottleneck).Append("</color>\n");
                if (!string.IsNullOrEmpty(r.bottleneckHow)) sy.Append(Dim(ProbeText(r.bottleneckHow)));
            }
            systemText.text = sy.ToString();

            var a = new StringBuilder(); var b = new StringBuilder();
            a.Append(Head("SETTINGS USED")); b.Append("<size=20> </size>\n");
            if (r.settings != null)
                for (int i = 0; i < r.settings.Length; i++)
                {
                    string kv = r.settings[i];
                    int eq = kv.IndexOf('=');
                    string line = eq < 0 ? kv + "\n" : Line(Title(kv.Substring(0, eq)), kv.Substring(eq + 1), "<pos=185>");
                    (i < 10 ? a : b).Append(line);
                }
            settingsTextA.text = a.ToString();
            settingsTextB.text = b.ToString();
        }
    }
}
