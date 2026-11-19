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

        [Header("HUD")]
        [SerializeField] GameObject hudRoot;
        [SerializeField] TMP_Text counterText;
        [SerializeField] RectTransform counterRect;
        [SerializeField] UnityEngine.UI.Image progressFill;
        [SerializeField] TMP_Text timerText;
        [SerializeField] RectTransform timerRect;

        [Header("Pause")]
        [SerializeField] GameObject pauseRoot;
        [SerializeField] UnityEngine.UI.Button resumeButton;
        [SerializeField] UnityEngine.UI.Button pauseRestartButton;
        [SerializeField] UnityEngine.UI.Button pauseTitleButton;
        [SerializeField] UnityEngine.UI.Slider musicSlider;
        [SerializeField] UnityEngine.UI.Slider sfxSlider;

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
        int _lastTimerSecond = -1;
        float _fadeAlpha;

        Coroutine _transition, _flash, _banner, _pop, _fill, _timerPulse;

        void Start()
        {
            _startFrame = Time.frameCount;
            _startTime = Time.unscaledTime;

            _loop = GameLoop.Instance;
            if (_loop == null) return;

            _loop.StateChanged += OnStateChanged;
            _loop.PickupCollected += OnPickupCollected;

            resumeButton.onClick.AddListener(() => Click(_loop.Resume));
            pauseRestartButton.onClick.AddListener(() => Click(Restart));
            pauseTitleButton.onClick.AddListener(() => Click(ToTitle));
            endRestartButton.onClick.AddListener(() => Click(Restart));
            endTitleButton.onClick.AddListener(() => Click(ToTitle));

            musicSlider.SetValueWithoutNotify(GameAudio.MusicVolume);
            sfxSlider.SetValueWithoutNotify(GameAudio.SfxVolume);
            musicSlider.onValueChanged.AddListener(v => GameAudio.MusicVolume = v);
            sfxSlider.onValueChanged.AddListener(v => GameAudio.SfxVolume = v);

            SetFade(0f);
            flashOverlay.color = Palette.WithAlpha(flashOverlay.color, 0f);
            SetExtraStats(string.Empty);

            ApplyState(_loop.State);
        }

        void OnDestroy()
        {
            if (_loop == null) return;
            _loop.StateChanged -= OnStateChanged;
            _loop.PickupCollected -= OnPickupCollected;
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

            switch (_loop.State)
            {
                case RunState.Ready:
                    PulsePrompt();
                    if (Time.unscaledTime - _startTime >= InputGrace && AnyStartInput())
                        _loop.StartRun();
                    break;

                case RunState.Running:
                    UpdateTimer();
                    if (PausePressed()) _loop.Pause();
                    break;

                case RunState.Paused:
                    if (PausePressed()) _loop.Resume();
                    break;

                case RunState.Won:
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
            return (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                || (mouse != null && mouse.leftButton.wasPressedThisFrame)
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
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

            if (state == RunState.Won || state == RunState.Lost)
            {
                // The flash covers the swap, so the panels change instantly here.
                SetFade(0f);
                ApplyState(state);
                if (_flash != null) StopCoroutine(_flash);
                _flash = StartCoroutine(FlashRoutine(state == RunState.Won ? Palette.Accent : Palette.Danger));
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
            titleRoot.SetActive(state == RunState.Ready);
            hudRoot.SetActive(state == RunState.Running || state == RunState.Paused);
            pauseRoot.SetActive(state == RunState.Paused);
            endRoot.SetActive(state == RunState.Won || state == RunState.Lost);

            switch (state)
            {
                case RunState.Running:
                case RunState.Paused:
                    RefreshHud();
                    if (state == RunState.Paused) Select(resumeButton);
                    break;

                case RunState.Won:
                case RunState.Lost:
                    ShowEnded(state == RunState.Won);
                    break;
            }
        }

        void RefreshHud()
        {
            counterText.text = CounterString();
            progressFill.fillAmount = Progress();
            counterRect.localScale = Vector3.one;
            timerRect.localScale = Vector3.one;
            _lastTimerSecond = -1;
            UpdateTimer();
        }

        void ShowEnded(bool won)
        {
            bannerText.text = won ? "BLOCK CLEARED!" : "TIME'S UP";
            bannerText.color = won ? Palette.Accent : Palette.Danger;
            statsText.text = $"TOKENS {_loop.Collected} / {_loop.TargetCount}  ·  TIME {FormatTime(Mathf.FloorToInt(_loop.ElapsedSeconds))}";

            bannerRect.localScale = Vector3.zero;
            if (_banner != null) StopCoroutine(_banner);
            _banner = StartCoroutine(BannerRoutine());

            Select(endRestartButton);
        }

        IEnumerator FlashRoutine(Color color)
        {
            yield return Ease.Tween(0.1f,
                t => flashOverlay.color = Palette.WithAlpha(color, Mathf.Lerp(0.6f, 0f, t)),
                Ease.OutCubic);
            _flash = null;
        }

        IEnumerator BannerRoutine()
        {
            yield return new WaitForSecondsRealtime(0.05f);
            yield return Ease.Tween(0.35f, t => bannerRect.localScale = Vector3.one * t, Ease.OutBack);
            _banner = null;
        }

        // ---------------------------------------------------------------- HUD

        void OnPickupCollected(Pickup pickup)
        {
            counterText.text = CounterString();

            if (_pop != null) StopCoroutine(_pop);
            _pop = StartCoroutine(PopRoutine(counterRect, 1.25f, 0.25f));

            if (_fill != null) StopCoroutine(_fill);
            _fill = StartCoroutine(FillRoutine(Progress()));
        }

        // Scale 1 -> peak -> 1 over the duration, using OutBack on each half.
        IEnumerator PopRoutine(RectTransform target, float peak, float duration)
        {
            float half = duration * 0.5f;
            yield return Ease.Tween(half, t => target.localScale = Vector3.one * Mathf.Lerp(1f, peak, t), Ease.OutBack);
            yield return Ease.Tween(half, t => target.localScale = Vector3.one * Mathf.Lerp(peak, 1f, t), Ease.OutBack);
            target.localScale = Vector3.one;
        }

        IEnumerator FillRoutine(float target)
        {
            float from = progressFill.fillAmount;
            yield return Ease.Tween(0.3f, t => progressFill.fillAmount = Mathf.Lerp(from, target, t), Ease.OutCubic);
            _fill = null;
        }

        void UpdateTimer()
        {
            float left = _loop.TimeLeft;
            int second = Mathf.CeilToInt(left);
            bool danger = left < 10f;

            timerText.color = danger ? Palette.Danger : Palette.UiLight;

            if (second == _lastTimerSecond) return;
            _lastTimerSecond = second;
            timerText.text = FormatTime(second);

            // Under 10 seconds the timer pulses once for every new second.
            if (danger && second > 0)
            {
                if (_timerPulse != null) StopCoroutine(_timerPulse);
                _timerPulse = StartCoroutine(PopRoutine(timerRect, 1.3f, 0.4f));
            }
        }

        string CounterString() => $"{_loop.Collected} / {_loop.TargetCount}";

        float Progress() => _loop.TargetCount > 0 ? (float)_loop.Collected / _loop.TargetCount : 0f;

        static string FormatTime(int totalSeconds)
        {
            totalSeconds = Mathf.Max(0, totalSeconds);
            return $"{totalSeconds / 60}:{totalSeconds % 60:00}";
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
