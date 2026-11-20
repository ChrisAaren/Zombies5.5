using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Zombies55
{
    // The UI state layer: title, HUD, pause menu and the win/lose moment.
    // It owns no run state. It reads GameLoop and calls its methods.
    // All tweens use unscaled time because pausing sets Time.timeScale to 0.
    public class GameShell : MonoBehaviour
    {
        [Header("Title")]
        [SerializeField] GameObject titleRoot;
        [SerializeField] TMP_Text promptText;
        [SerializeField] UnityEngine.UI.Button titleSettingsButton;

        [Header("HUD")]
        [SerializeField] GameObject hudRoot;
        [SerializeField] SurvivalHud survivalHud;

        [Header("Pause")]
        [SerializeField] GameObject pauseRoot;
        [SerializeField] UnityEngine.UI.Button resumeButton;
        [SerializeField] UnityEngine.UI.Button pauseRestartButton;
        [SerializeField] UnityEngine.UI.Button pauseTitleButton;
        [SerializeField] UnityEngine.UI.Button pauseSettingsButton;

        [Header("Settings")]
        [SerializeField] SettingsMenu settingsMenu;

        [Header("Ended")]
        [SerializeField] GameObject endRoot;
        [SerializeField] TMP_Text bannerText;
        [SerializeField] RectTransform bannerRect;
        [SerializeField] TMP_Text statsText;
        [SerializeField] TMP_Text extraStatsText;
        [SerializeField] UnityEngine.UI.Button endRestartButton;
        [SerializeField] UnityEngine.UI.Button endTitleButton;

        [Header("Overlays")]
        [SerializeField] UnityEngine.UI.Image fadeOverlay;
        [SerializeField] UnityEngine.UI.Image flashOverlay;

        const float InputGrace = 0.3f;
        const float FadeTotal = 0.3f;
        const float PromptPulseSeconds = 1.6f;

        GameLoop _loop;
        int _startFrame;
        float _startTime;
        bool _leaving;
        float _fadeAlpha;

        bool _settingsOpen;
        RunState _settingsFrom;
        float _ignoreStartUntil;

        Coroutine _transition, _flash, _banner;

        void Start()
        {
            _startFrame = Time.frameCount;
            _startTime = Time.unscaledTime;

            _loop = GameLoop.Instance;
            if (_loop == null) return;

            _loop.StateChanged += OnStateChanged;
            if (survivalHud != null) survivalHud.FlashRequested += OnFlashRequested;

            resumeButton.onClick.AddListener(() => Click(_loop.Resume));
            pauseRestartButton.onClick.AddListener(() => Click(Restart));
            pauseTitleButton.onClick.AddListener(() => Click(ToTitle));
            endRestartButton.onClick.AddListener(() => Click(Restart));
            endTitleButton.onClick.AddListener(() => Click(ToTitle));

            titleSettingsButton.onClick.AddListener(() => Click(OpenSettings));
            pauseSettingsButton.onClick.AddListener(() => Click(OpenSettings));
            settingsMenu.BackRequested += CloseSettings;

            SetFade(0f);
            flashOverlay.color = Palette.WithAlpha(flashOverlay.color, 0f);
            SetExtraStats(string.Empty);

            ApplyState(_loop.State);

            // Back from a benchmark run (the scene was reloaded for a clean game): open its results, or say it was cancelled.
            if (_loop.State == RunState.Ready && (BenchmarkRunner.Pending != null || !string.IsNullOrEmpty(BenchmarkRunner.Note)))
            {
                var result = BenchmarkRunner.Pending;
                BenchmarkRunner.Pending = null;
                _settingsOpen = true;
                _settingsFrom = RunState.Ready;
                titleRoot.SetActive(false);
                settingsMenu.OpenBenchmarkResults(result);
            }
        }

        void OnDestroy()
        {
            if (_loop == null) return;
            _loop.StateChanged -= OnStateChanged;
            if (survivalHud != null) survivalHud.FlashRequested -= OnFlashRequested;
            if (settingsMenu != null) settingsMenu.BackRequested -= CloseSettings;
        }

        // A later step (for example a leaderboard) can show an extra line under the stats.
        public void SetExtraStats(string text)
        {
            bool has = !string.IsNullOrEmpty(text);
            extraStatsText.text = has ? text : string.Empty;
            extraStatsText.gameObject.SetActive(has);
        }

        // ---------------------------------------------------------------- input and per-frame

        void Update()
        {
            if (_loop == null || _leaving) return;

            if (_settingsOpen)
            {
                // The settings screen swallows input; Esc, B and Start go back.
                if (BackPressed()) CloseSettings();
                return;
            }

            switch (_loop.State)
            {
                case RunState.Ready:
                    PulsePrompt();
                    if (SettingsKeyPressed())
                    {
                        OpenSettings();
                        break;
                    }
                    if (Time.unscaledTime >= _ignoreStartUntil && Time.unscaledTime - _startTime >= InputGrace && AnyStartInput())
                        _loop.StartRun();
                    break;

                case RunState.Running:
                    if (PausePressed() && !BenchmarkRunner.Active) _loop.Pause();   // Esc cancels a benchmark instead
                    break;

                case RunState.Paused:
                    if (PausePressed()) _loop.Resume();
                    break;

                case RunState.Lost:
                    if (RestartKeyPressed()) Restart();
                    break;
            }
        }

        static bool AnyStartInput()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            // The S key and clicks on UI (the settings button) open settings instead of starting the run.
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            return (keyboard != null && keyboard.anyKey.wasPressedThisFrame && !keyboard.sKey.wasPressedThisFrame)
                || (mouse != null && mouse.leftButton.wasPressedThisFrame && !overUi)
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        static bool SettingsKeyPressed()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            return (keyboard != null && keyboard.sKey.wasPressedThisFrame)
                || (pad != null && pad.buttonNorth.wasPressedThisFrame);
        }

        static bool BackPressed()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            return (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        static bool PausePressed()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            return (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame))
                || (pad != null && pad.startButton.wasPressedThisFrame);
        }

        static bool RestartKeyPressed()
        {
            var keyboard = Keyboard.current;
            return keyboard != null
                && (keyboard.rKey.wasPressedThisFrame
                    || keyboard.enterKey.wasPressedThisFrame
                    || keyboard.numpadEnterKey.wasPressedThisFrame);
        }

        void PulsePrompt()
        {
            float wave = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / PromptPulseSeconds) + 1f) * 0.5f;
            promptText.color = Palette.WithAlpha(Palette.UiLight, Mathf.Lerp(0.5f, 1f, wave));
        }

        // ---------------------------------------------------------------- state changes

        void OnStateChanged(RunState state)
        {
            // Same frame as Start: a restart goes straight into play, so skip the title flash.
            if (Time.frameCount == _startFrame)
            {
                ApplyState(state);
                return;
            }

            if (_transition != null) StopCoroutine(_transition);

            if (state == RunState.Lost)
            {
                // The flash covers the swap, so the panels change instantly here.
                SetFade(0f);
                ApplyState(state);
                if (_flash != null) StopCoroutine(_flash);
                _flash = StartCoroutine(FlashRoutine(Palette.Danger, 0.6f));
            }
            else
            {
                _transition = StartCoroutine(TransitionRoutine(state));
            }
        }

        IEnumerator TransitionRoutine(RunState state)
        {
            float half = FadeTotal * 0.5f;

            float from = _fadeAlpha;
            yield return Ease.Tween(half, t => SetFade(Mathf.Lerp(from, 1f, t)), Ease.OutCubic);

            ApplyState(state);

            yield return Ease.Tween(half, t => SetFade(Mathf.Lerp(1f, 0f, t)), Ease.OutCubic);
            _transition = null;
        }

        // Shows the right panels for a state and prepares them.
        void ApplyState(RunState state)
        {
            _settingsOpen = false;
            settingsMenu.Close();
            titleRoot.SetActive(state == RunState.Ready);
            hudRoot.SetActive(state == RunState.Running || state == RunState.Paused);
            pauseRoot.SetActive(state == RunState.Paused);
            endRoot.SetActive(state == RunState.Lost);

            switch (state)
            {
                case RunState.Paused:
                    Select(resumeButton);
                    break;

                case RunState.Lost:
                    ShowEnded();
                    break;
            }
        }

        void ShowEnded()
        {
            var rm = RoundManager.Instance;
            var wallet = PlayerWallet.Instance;
            string v = "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Accent) + ">";
            statsText.text =
                $"ROUNDS SURVIVED  {v}{(rm != null ? rm.RoundsSurvived : 0)}</color>\n" +
                $"KILLS  {v}{(rm != null ? rm.TotalKills : 0)}</color>\n" +
                $"HEADSHOTS  {v}{(rm != null ? rm.TotalHeadshots : 0)}</color>\n" +
                $"POINTS EARNED  {v}{(wallet != null ? wallet.TotalEarned : 0):N0}</color>";

            bannerText.text = "YOU DIDN'T MAKE IT";
            bannerText.color = Palette.Danger;
            bannerRect.localScale = Vector3.zero;
            if (_banner != null) StopCoroutine(_banner);
            _banner = StartCoroutine(BannerRoutine());

            Select(endRestartButton);
        }

        void OnFlashRequested(Color color, float alpha)
        {
            if (_flash != null) StopCoroutine(_flash);
            _flash = StartCoroutine(FlashRoutine(color, alpha));
        }

        IEnumerator FlashRoutine(Color color, float peak)
        {
            yield return Ease.Tween(peak > 0.5f ? 0.1f : 0.25f,
                t => flashOverlay.color = Palette.WithAlpha(color, Mathf.Lerp(peak, 0f, t)),
                Ease.OutCubic);
            _flash = null;
        }

        IEnumerator BannerRoutine()
        {
            yield return new WaitForSecondsRealtime(0.05f);
            yield return Ease.Tween(0.35f, t => bannerRect.localScale = Vector3.one * t, Ease.OutBack);
            _banner = null;
        }

        // ---------------------------------------------------------------- helpers

        void SetFade(float alpha)
        {
            _fadeAlpha = alpha;
            fadeOverlay.color = Palette.WithAlpha(Palette.UiDark, alpha);
            fadeOverlay.raycastTarget = alpha > 0.001f; // block clicks while fading
        }

        static void Select(UnityEngine.UI.Button button)
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        static void Click(Action action)
        {
            GameAudio.Instance?.PlayClick();
            action();
        }

        // ---------------------------------------------------------------- settings

        // Opens the settings screen over the title or pause menu and remembers where to return to.
        void OpenSettings()
        {
            if (_settingsOpen || _loop == null) return;
            if (_loop.State != RunState.Ready && _loop.State != RunState.Paused) return;
            _settingsOpen = true;
            _settingsFrom = _loop.State;
            titleRoot.SetActive(false);
            pauseRoot.SetActive(false);
            settingsMenu.Open();
        }

        void CloseSettings()
        {
            if (!_settingsOpen) return;
            ApplyState(_loop.State); // restores the screen we came from and saves settings

            // Keep the key or click that closed the menu from starting a run or reopening it.
            _ignoreStartUntil = Time.unscaledTime + 0.25f;
            if (_settingsFrom == RunState.Paused && _loop.State == RunState.Paused) Select(pauseSettingsButton);
        }

        void Restart()
        {
            if (_leaving) return;
            _leaving = true;
            _loop.Restart();
        }

        void ToTitle()
        {
            if (_leaving) return;
            _leaving = true;
            _loop.ReturnToTitle();
        }
    }
}
