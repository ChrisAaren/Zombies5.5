using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Zombies55
{
    // The settings screen: Controls / Keybinds / Crosshair / Audio / Graphics / Benchmark tabs, reset and back. GameShell opens it, closes it
    // (it owns the Esc and B input so pause handling cannot fight it) and restores the previous screen.
    public class SettingsMenu : MonoBehaviour
    {
        public const int ControlsTab = 0;
        public const int KeybindsTab = 1;
        public const int CrosshairTab = 2;
        public const int AudioTab = 3;
        public const int GraphicsTab = 4;
        public const int BenchmarkTab = 5;

        [SerializeField] Button[] tabButtons;      // indexed by the *Tab constants
        [SerializeField] GameObject[] tabPanels;
        [SerializeField] Selectable[] firstSelectable; // focus target when a tab opens
        [SerializeField] Button resetButton;
        [SerializeField] Button backButton;
        [SerializeField] RectTransform card;
        [SerializeField] TMP_Text descriptionText;   // one line under the list: what the focused setting does
        [SerializeField] BenchmarkPanel benchmarkPanel;
        [SerializeField] Button graphicsBenchmarkButton; // RUN BENCHMARK at the end of the Graphics list: jumps to the Benchmark tab
        [SerializeField] ScrollRect graphicsScroll;

        public static SettingsMenu Current { get; private set; }

        public event Action BackRequested;
        public bool IsOpen => gameObject.activeSelf;

        int _tab;
        bool _wired;

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                int index = i;
                tabButtons[i].onClick.AddListener(() => { GameAudio.Instance?.PlayClick(); SelectTab(index, false); });
            }
            resetButton.onClick.AddListener(() =>
            {
                GameAudio.Instance?.PlayClick();
                // Each tab resets only its own settings: key bindings are separate from GameSettings.
                if (_tab == KeybindsTab) GameInput.ResetToDefaults();
                else GameSettings.Reset();
            });
            backButton.onClick.AddListener(() => { GameAudio.Instance?.PlayClick(); BackRequested?.Invoke(); });
            if (benchmarkPanel != null) benchmarkPanel.ViewChanged += RefreshFooter;
            if (graphicsBenchmarkButton != null)
                graphicsBenchmarkButton.onClick.AddListener(() => { GameAudio.Instance?.PlayClick(); SelectTab(BenchmarkTab, true); });
        }

        public void Open() => Open(0);

        public void Open(int tab)
        {
            Wire();
            Current = this;
            gameObject.SetActive(true);
            SelectTab(tab, true);
            StartCoroutine(Ease.Tween(0.2f, t => card.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, t), Ease.OutBack));
        }

        // After a benchmark run: straight to its results.
        public void OpenBenchmarkResults(BenchmarkResult result)
        {
            Open(BenchmarkTab);
            if (result != null) benchmarkPanel.ShowResult(result);
        }

        public void Close()
        {
            gameObject.SetActive(false);
            GameSettings.Save();
            if (Current == this) Current = null;
        }

        public void ShowDescription(string text)
        {
            if (descriptionText != null) descriptionText.text = text ?? string.Empty;
        }

        // The results screen has its own BACK; reset makes no sense on the Benchmark tab. Esc / B still go back.
        void RefreshFooter()
        {
            bool bench = _tab == BenchmarkTab;
            resetButton.gameObject.SetActive(!bench);
            backButton.gameObject.SetActive(!(bench && benchmarkPanel != null && benchmarkPanel.ShowingResults));
            // With RESET hidden, BACK takes the middle.
            var pos = ((RectTransform)backButton.transform).anchoredPosition;
            pos.x = bench ? 0f : 250f;
            ((RectTransform)backButton.transform).anchoredPosition = pos;
        }

        void SelectTab(int index, bool focus)
        {
            _tab = (index % tabPanels.Length + tabPanels.Length) % tabPanels.Length;
            ShowDescription(string.Empty);
            for (int i = 0; i < tabPanels.Length; i++)
            {
                tabPanels[i].SetActive(i == _tab);
                var colors = tabButtons[i].colors;
                colors.normalColor = i == _tab ? Palette.Accent : Palette.CharacterPrimary;
                tabButtons[i].colors = colors;
            }
            RefreshFooter();
            if (_tab == GraphicsTab && graphicsScroll != null) graphicsScroll.verticalNormalizedPosition = 1f;
            if (focus && EventSystem.current != null)
            {
                GameObject target = firstSelectable[_tab] != null ? firstSelectable[_tab].gameObject : null;
                if (_tab == BenchmarkTab && benchmarkPanel != null && benchmarkPanel.FirstSelectable != null) target = benchmarkPanel.FirstSelectable;
                if (target != null) EventSystem.current.SetSelectedGameObject(target);
            }
        }

        void Update()
        {
            var pad = Gamepad.current;
            if (pad == null || GameInput.MenuInputBlocked) return;
            if (pad.rightShoulder.wasPressedThisFrame) SelectTab(_tab + 1, true);
            else if (pad.leftShoulder.wasPressedThisFrame) SelectTab(_tab - 1, true);
        }
    }
}
