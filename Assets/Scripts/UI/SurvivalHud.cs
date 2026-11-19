using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Zombies55
{
    // The in-run HUD for the survival mode. It sits on the Hud root, owns no game state and only
    // reads the round, health, weapon and wallet singletons (any of which may appear late).
    // All tweens use unscaled time so the HUD keeps animating while the pause menu is open.
    public class SurvivalHud : MonoBehaviour
    {
        [Header("Round counter")]
        [SerializeField] TMP_Text roundNumberText;
        [SerializeField] RectTransform roundNumberRect;
        [SerializeField] TMP_Text enemiesText;

        [Header("Health")]
        [SerializeField] Image[] healthPips;
        [SerializeField] Image vignette;

        [Header("Points")]
        [SerializeField] TMP_Text pointsText;
        [SerializeField] RectTransform pointsRect;
        [SerializeField] TMP_Text floatTemplate;
        [SerializeField] RectTransform floatRoot;

        [Header("Weapon panel")]
        [SerializeField] TMP_Text weaponNameText;
        [SerializeField] TMP_Text magText;
        [SerializeField] TMP_Text reserveText;
        [SerializeField] Image[] slotChips;
        [SerializeField] TMP_Text[] slotLabels;
        [SerializeField] GameObject reloadBar;
        [SerializeField] Image reloadFill;

        [Header("Crosshair")]
        [SerializeField] GameObject crosshairRoot;
        [SerializeField] RectTransform[] crosshairTicks; // up, down, left, right
        [SerializeField] CanvasGroup hitMarkerGroup;
        [SerializeField] RectTransform hitMarkerRect;
        [SerializeField] Image[] hitMarkerImages;

        [Header("Prompt")]
        [SerializeField] TMP_Text promptText;

        [Header("Round banner")]
        [SerializeField] RectTransform bannerRect;
        [SerializeField] CanvasGroup bannerGroup;
        [SerializeField] TMP_Text bannerText;
        [SerializeField] TMP_Text bannerSubText;

        // Asks the shell to flash the full-screen overlay: (color, peak alpha).
        public event Action<Color, float> FlashRequested;

        const float CrosshairBaseGap = 7f;
        const float CrosshairTickLength = 16f;
        const float LowHealth = 0.4f;
        const int MaxFloats = 8;

        RoundManager _rm;
        PlayerHealth _hp;
        PlayerWeapons _pw;
        PlayerWallet _wallet;
        Canvas _rootCanvas;
        Camera _cam;

        // round / banner
        int _shownRound, _clearedRound, _numberShown = -1;
        bool _showBreak;
        int _breakSecond = -1;
        int _enemiesShown = -1;
        bool _breakShown;
        Vector2 _bannerHome;
        Coroutine _bannerCo, _numberPop, _pointsPop, _subPop, _hitCo;

        // health
        float _damageFlash;
        int _pipsOn = -1;

        // points
        float _pointsShown;
        int _pointsTarget, _pointsDisplayed = int.MinValue;
        float _tintAmount;
        Color _tintColor = Color.white;
        readonly List<GameObject> _floats = new List<GameObject>();

        // weapon / crosshair
        bool _weaponDirty = true;
        float _gap = CrosshairBaseGap;
        bool _crosshairShown, _promptShown;

        void Awake()
        {
            _bannerHome = bannerRect.anchoredPosition;
            floatTemplate.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            _rootCanvas = GetComponentInParent<Canvas>() != null ? GetComponentInParent<Canvas>().rootCanvas : null;
            ResetVisuals();
            Bind();
            HudPrompt.Changed += OnPromptChanged;
            OnPromptChanged();
        }

        void OnDisable()
        {
            Unbind();
            HudPrompt.Changed -= OnPromptChanged;
            foreach (var f in _floats) if (f != null) Destroy(f);
            _floats.Clear();
            _bannerCo = _numberPop = _pointsPop = _subPop = _hitCo = null; // coroutines die with the object
        }

        void ResetVisuals()
        {
            bannerRect.gameObject.SetActive(false);
            bannerGroup.alpha = 1f;
            hitMarkerGroup.alpha = 0f;
            roundNumberRect.localScale = Vector3.one;
            pointsRect.localScale = Vector3.one;
            reloadBar.SetActive(false);
            _damageFlash = 0f;
            _tintAmount = 0f;
            _pipsOn = -1;
            _numberShown = -1;
            _enemiesShown = -1;
            _pointsDisplayed = int.MinValue;
            _showBreak = false;
            _weaponDirty = true;
            _crosshairShown = !crosshairRoot.activeSelf; // force a first apply
            _promptShown = !promptText.gameObject.activeSelf;
            var c = vignette.color; c.a = 0f; vignette.color = c;
        }

        // ---------------------------------------------------------------- binding

        void Bind()
        {
            if (!ReferenceEquals(_rm, RoundManager.Instance))
            {
                if (_rm != null) { _rm.RoundStarted -= OnRoundStarted; _rm.RoundCompleted -= OnRoundCompleted; }
                _rm = RoundManager.Instance;
                if (_rm != null) { _rm.RoundStarted += OnRoundStarted; _rm.RoundCompleted += OnRoundCompleted; }
            }

            if (!ReferenceEquals(_hp, PlayerHealth.Instance))
            {
                if (_hp != null) _hp.Damaged -= OnDamaged;
                _hp = PlayerHealth.Instance;
                if (_hp != null) _hp.Damaged += OnDamaged;
            }

            if (!ReferenceEquals(_pw, PlayerWeapons.Instance))
            {
                if (_pw != null)
                {
                    _pw.WeaponChanged -= MarkWeaponDirty;
                    _pw.AmmoChanged -= MarkWeaponDirty;
                    _pw.HitConfirmed -= OnHitConfirmed;
                }
                _pw = PlayerWeapons.Instance;
                if (_pw != null)
                {
                    _pw.WeaponChanged += MarkWeaponDirty;
                    _pw.AmmoChanged += MarkWeaponDirty;
                    _pw.HitConfirmed += OnHitConfirmed;
                }
                _weaponDirty = true;
            }

            if (!ReferenceEquals(_wallet, PlayerWallet.Instance))
            {
                if (_wallet != null) _wallet.Changed -= OnWalletChanged;
                _wallet = PlayerWallet.Instance;
                if (_wallet != null)
                {
                    _wallet.Changed += OnWalletChanged;
                    _pointsTarget = _wallet.Points;
                    _pointsShown = _pointsTarget;
                }
            }
        }

        void Unbind()
        {
            if (_rm != null) { _rm.RoundStarted -= OnRoundStarted; _rm.RoundCompleted -= OnRoundCompleted; }
            if (_hp != null) _hp.Damaged -= OnDamaged;
            if (_pw != null)
            {
                _pw.WeaponChanged -= MarkWeaponDirty;
                _pw.AmmoChanged -= MarkWeaponDirty;
                _pw.HitConfirmed -= OnHitConfirmed;
            }
            if (_wallet != null) _wallet.Changed -= OnWalletChanged;
            _rm = null; _hp = null; _pw = null; _wallet = null;
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            Bind();

            float dt = Time.unscaledDeltaTime;
            var loop = GameLoop.Instance;
            bool running = loop != null && loop.State == RunState.Running;

            UpdateRound();
            UpdateHealth(dt);
            UpdatePoints(dt);
            UpdateWeapon();
            UpdateCrosshair(dt, running);
            UpdatePrompt(running);
        }

        void UpdateRound()
        {
            if (_rm == null)
            {
                SetEnemies(0, false);
                return;
            }

            // Fallback for a round that began before this HUD was enabled.
            if (!_rm.InBreak && _rm.Round > 0 && _shownRound != _rm.Round) OnRoundStarted(_rm.Round);

            if (_bannerCo == null && _numberShown != _rm.Round) SetRoundNumber(_rm.Round, false);

            SetEnemies(_rm.RemainingThisRound, _rm.InBreak);

            if (_showBreak)
            {
                if (!_rm.InBreak) { _showBreak = false; return; }
                int s = Mathf.Max(0, Mathf.CeilToInt(_rm.BreakTimeLeft));
                if (s != _breakSecond)
                {
                    _breakSecond = s;
                    bannerSubText.text = "NEXT ROUND IN " + s;
                    bannerSubText.color = s <= 3 ? Palette.Accent : Palette.UiLight;
                    Pop(ref _subPop, (RectTransform)bannerSubText.transform, 1.15f, 0.2f);
                }
            }
        }

        void SetEnemies(int remaining, bool inBreak)
        {
            int key = inBreak ? -1 : remaining;
            if (key == _enemiesShown && inBreak == _breakShown) return;
            _enemiesShown = key;
            _breakShown = inBreak;
            enemiesText.text = inBreak ? "REGROUP" : remaining == 1 ? "1 ENEMY LEFT" : remaining + " ENEMIES LEFT";
        }

        void SetRoundNumber(int n, bool pop)
        {
            _numberShown = n;
            roundNumberText.text = n.ToString(CultureInfo.InvariantCulture);
            if (pop) Pop(ref _numberPop, roundNumberRect, 1.35f, 0.3f);
        }

        void UpdateHealth(float dt)
        {
            float n = _hp != null ? Mathf.Clamp01(_hp.Normalized) : 1f;
            bool alive = _hp == null || _hp.IsAlive;
            int on = alive ? Mathf.Clamp(Mathf.CeilToInt(n * healthPips.Length - 0.001f), 0, healthPips.Length) : 0;

            if (_pipsOn > on && on >= 0 && on < healthPips.Length)
                StartCoroutine(PopRoutine((RectTransform)healthPips[on].transform, 1.4f, 0.3f));
            _pipsOn = on;

            float t = Time.unscaledTime;
            bool low = alive && on <= 1;
            float wave = 0.5f + 0.5f * Mathf.Sin(t * 6f);
            for (int i = 0; i < healthPips.Length; i++)
            {
                Color c;
                if (i >= on) c = Palette.WithAlpha(Palette.UiLight, 0.15f);
                else if (low) c = Color.Lerp(Palette.Danger, Palette.UiLight, wave * 0.35f);
                else c = Palette.UiLight;
                healthPips[i].color = c;
            }

            _damageFlash = Mathf.Max(0f, _damageFlash - dt * 2.2f);
            float lowPulse = 0f;
            if (alive && n <= LowHealth)
            {
                float k = n / LowHealth;
                float beat = 0.5f + 0.5f * Mathf.Sin(t * Mathf.Lerp(7f, 3.5f, k));
                lowPulse = Mathf.Lerp(0.55f, 0.2f, k) * (0.55f + 0.45f * beat);
            }
            vignette.color = Palette.WithAlpha(Palette.Danger, Mathf.Max(_damageFlash * 0.85f, lowPulse));
        }

        void UpdatePoints(float dt)
        {
            if (_pointsDisplayed == int.MinValue && _wallet != null)
            {
                _pointsTarget = _wallet.Points;
                _pointsShown = _pointsTarget;
            }

            _pointsShown = Mathf.Lerp(_pointsShown, _pointsTarget, 1f - Mathf.Exp(-12f * dt));
            if (Mathf.Abs(_pointsShown - _pointsTarget) < 0.5f) _pointsShown = _pointsTarget;
            int shown = Mathf.RoundToInt(_pointsShown);
            if (shown != _pointsDisplayed)
            {
                _pointsDisplayed = shown;
                pointsText.text = shown.ToString("N0", CultureInfo.InvariantCulture);
            }

            _tintAmount = Mathf.Max(0f, _tintAmount - dt * 2.5f);
            pointsText.color = Color.Lerp(Palette.UiLight, _tintColor, _tintAmount);
        }

        void UpdateWeapon()
        {
            if (_pw == null) return;

            if (_weaponDirty)
            {
                _weaponDirty = false;
                var cur = _pw.Current;
                weaponNameText.text = cur != null ? cur.DisplayName.ToUpperInvariant() : "UNARMED";

                int mag = _pw.Magazine;
                bool low = cur != null && mag <= Mathf.Max(1, Mathf.CeilToInt(cur.MagazineSize * 0.25f));
                magText.text = mag.ToString(CultureInfo.InvariantCulture);
                magText.color = low ? Palette.Danger : Palette.UiLight;
                reserveText.text = "/ " + _pw.Reserve;

                for (int i = 0; i < slotChips.Length; i++)
                {
                    var def = _pw.GetSlot(i);
                    bool active = i == _pw.ActiveSlot;
                    slotLabels[i].text = (i + 1) + "  " + (def != null ? def.DisplayName.ToUpperInvariant() : "EMPTY");
                    slotChips[i].color = active ? Palette.Accent : Palette.WithAlpha(Palette.UiDark, 0.8f);
                    slotLabels[i].color = active ? Palette.UiDark
                        : Palette.WithAlpha(Palette.UiLight, def != null ? 1f : 0.45f);
                }
            }

            bool reloading = _pw.IsReloading;
            if (reloadBar.activeSelf != reloading) reloadBar.SetActive(reloading);
            if (reloading) reloadFill.fillAmount = Mathf.Clamp01(_pw.ReloadProgress);
        }

        void UpdateCrosshair(float dt, bool running)
        {
            bool show = running && (_hp == null || _hp.IsAlive);
            if (show != _crosshairShown)
            {
                _crosshairShown = show;
                crosshairRoot.SetActive(show);
            }
            if (!show) return;

            float target = CrosshairBaseGap;
            if (_pw != null)
            {
                target += SpreadToCanvasUnits(_pw.CurrentSpreadDegrees);
                if (_pw.IsAiming) target = 3f + (target - CrosshairBaseGap) * 0.6f;
            }

            _gap = Mathf.Lerp(_gap, target, 1f - Mathf.Exp(-18f * dt));
            float d = _gap + CrosshairTickLength * 0.5f;
            if (crosshairTicks.Length >= 4)
            {
                crosshairTicks[0].anchoredPosition = new Vector2(0f, d);
                crosshairTicks[1].anchoredPosition = new Vector2(0f, -d);
                crosshairTicks[2].anchoredPosition = new Vector2(-d, 0f);
                crosshairTicks[3].anchoredPosition = new Vector2(d, 0f);
            }
        }

        // Cone half-angle in degrees to canvas units, using the camera's vertical FOV and the screen height.
        float SpreadToCanvasUnits(float degrees)
        {
            if (degrees <= 0f) return 0f;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return degrees * 8f;

            float halfFov = _cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float px = Mathf.Tan(degrees * Mathf.Deg2Rad) / Mathf.Tan(halfFov) * Screen.height * 0.5f;
            float scale = _rootCanvas != null && _rootCanvas.scaleFactor > 0f ? _rootCanvas.scaleFactor : 1f;
            return px / scale;
        }

        void UpdatePrompt(bool running)
        {
            bool show = running && !string.IsNullOrEmpty(HudPrompt.Text);
            if (show == _promptShown) return;
            _promptShown = show;
            promptText.gameObject.SetActive(show);
        }

        void OnPromptChanged()
        {
            promptText.text = HudPrompt.Text;
            promptText.color = HudPrompt.Affordable ? Palette.UiLight : Palette.Danger;
        }

        // ---------------------------------------------------------------- events

        void MarkWeaponDirty() => _weaponDirty = true;

        void OnDamaged(DamageInfo info) => _damageFlash = 1f;

        void OnWalletChanged(int total, int delta, string reason)
        {
            _pointsTarget = total;
            if (delta == 0) return;

            _tintColor = delta > 0 ? Palette.Accent : Palette.Danger;
            _tintAmount = 1f;
            Pop(ref _pointsPop, pointsRect, 1.12f, 0.2f);
            SpawnFloat(delta);
        }

        void OnHitConfirmed(bool killed, bool headshot)
        {
            Color c = killed ? (headshot ? Palette.Accent : Palette.Danger) : Palette.UiLight;
            foreach (var img in hitMarkerImages) img.color = c;
            if (_hitCo != null) StopCoroutine(_hitCo);
            if (isActiveAndEnabled) _hitCo = StartCoroutine(HitMarkerRoutine(killed ? 0.35f : 0.2f, killed ? 1.7f : 1.35f));
        }

        void OnRoundStarted(int round)
        {
            if (_shownRound == round) return;
            _shownRound = round;
            _showBreak = false;
            RunBanner(RoundBannerRoutine(round));
        }

        void OnRoundCompleted(int round)
        {
            if (_clearedRound == round) return;
            _clearedRound = round;
            _showBreak = true;
            _breakSecond = -1;
            RunBanner(ClearedRoutine(round));
        }

        // ---------------------------------------------------------------- banners

        void RunBanner(IEnumerator routine)
        {
            if (!isActiveAndEnabled) return;
            if (_bannerCo != null) StopCoroutine(_bannerCo);
            _bannerCo = StartCoroutine(routine);
        }

        IEnumerator RoundBannerRoutine(int round)
        {
            bannerRect.gameObject.SetActive(true);
            bannerRect.anchoredPosition = _bannerHome;
            bannerRect.localScale = Vector3.zero;
            bannerGroup.alpha = 1f;
            bannerText.text = "ROUND " + round;
            bannerText.color = Palette.Accent;
            bannerSubText.text = string.Empty;

            FlashRequested?.Invoke(Palette.Accent, 0.3f);
            yield return Ease.Tween(0.4f, t => bannerRect.localScale = Vector3.one * t, Ease.OutBack);
            yield return new WaitForSecondsRealtime(0.9f);

            // Shrink into the corner counter.
            Vector3 from = bannerRect.position;
            Vector3 to = roundNumberRect.TransformPoint(roundNumberRect.rect.center);
            yield return Ease.Tween(0.45f, t =>
            {
                bannerRect.position = Vector3.Lerp(from, to, t);
                bannerRect.localScale = Vector3.one * Mathf.Lerp(1f, 0.25f, t);
                bannerGroup.alpha = 1f - t * t;
            }, Ease.OutCubic);

            bannerRect.gameObject.SetActive(false);
            bannerRect.anchoredPosition = _bannerHome;
            SetRoundNumber(round, true);
            _bannerCo = null;
        }

        IEnumerator ClearedRoutine(int round)
        {
            bannerRect.gameObject.SetActive(true);
            bannerRect.anchoredPosition = _bannerHome;
            bannerRect.localScale = Vector3.zero;
            bannerGroup.alpha = 1f;
            bannerText.text = "ROUND " + round + " CLEARED";
            bannerText.color = Palette.Accent;
            bannerSubText.text = string.Empty; // Update fills the countdown

            FlashRequested?.Invoke(Palette.UiLight, 0.2f);
            yield return Ease.Tween(0.4f, t => bannerRect.localScale = Vector3.one * t, Ease.OutBack);
            _bannerCo = null; // the banner stays up for the break; Update drives the countdown
        }

        // ---------------------------------------------------------------- effects

        void SpawnFloat(int delta)
        {
            if (!isActiveAndEnabled) return;

            while (_floats.Count >= MaxFloats)
            {
                if (_floats[0] != null) Destroy(_floats[0]);
                _floats.RemoveAt(0);
            }

            var text = Instantiate(floatTemplate, floatRoot);
            text.gameObject.SetActive(true);
            text.text = delta > 0 ? "+" + delta : "-" + (-delta); // ASCII minus: the default font atlas has no U+2212
            text.color = delta > 0 ? Palette.Accent : Palette.Danger;
            _floats.Add(text.gameObject);
            StartCoroutine(FloatRoutine(text));
        }

        IEnumerator FloatRoutine(TMP_Text text)
        {
            var rt = text.rectTransform;
            Vector2 start = rt.anchoredPosition;
            Color c = text.color;
            yield return Ease.Tween(0.9f, t =>
            {
                if (text == null) return;
                rt.anchoredPosition = start + new Vector2(0f, 55f * t);
                text.color = Palette.WithAlpha(c, 1f - t * t);
            }, Ease.OutCubic);

            if (text != null)
            {
                _floats.Remove(text.gameObject);
                Destroy(text.gameObject);
            }
        }

        IEnumerator HitMarkerRoutine(float duration, float startScale)
        {
            yield return Ease.Tween(duration, t =>
            {
                hitMarkerGroup.alpha = 1f - t;
                hitMarkerRect.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, t);
            }, Ease.OutCubic);
            hitMarkerGroup.alpha = 0f;
            _hitCo = null;
        }

        void Pop(ref Coroutine slot, RectTransform target, float peak, float duration)
        {
            if (!isActiveAndEnabled) return;
            if (slot != null) StopCoroutine(slot);
            slot = StartCoroutine(PopRoutine(target, peak, duration));
        }

        // Scale 1 -> peak -> 1 using OutBack on each half.
        static IEnumerator PopRoutine(RectTransform target, float peak, float duration)
        {
            float half = duration * 0.5f;
            yield return Ease.Tween(half, t => { if (target != null) target.localScale = Vector3.one * Mathf.Lerp(1f, peak, t); }, Ease.OutBack);
            yield return Ease.Tween(half, t => { if (target != null) target.localScale = Vector3.one * Mathf.Lerp(peak, 1f, t); }, Ease.OutBack);
            if (target != null) target.localScale = Vector3.one;
        }
    }
}
