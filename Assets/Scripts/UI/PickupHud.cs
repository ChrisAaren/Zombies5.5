using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Zombies55
{
    // The item-drop part of the HUD: the big pickup announcement, the active-effect chips (Deadeye, Payday) with countdown bars at
    // the bottom center, the x2 badge beside the points, and the full-screen pulse for pickups and the Shockwave.
    // Driven from Update with plain state, so a pickup allocates nothing. Runs on unscaled time like the rest of the HUD.
    public class PickupHud : MonoBehaviour
    {
        [Header("Announcement")]
        [SerializeField] RectTransform announceRect;
        [SerializeField] CanvasGroup announceGroup;
        [SerializeField] TMP_Text announceText;
        [SerializeField] TMP_Text announceSub;

        [Header("Effect chips (0 = Deadeye, 1 = Payday)")]
        [SerializeField] RectTransform[] chipRects;
        [SerializeField] CanvasGroup[] chipGroups;
        [SerializeField] Image[] chipFills;
        [SerializeField] Image[] chipAccents;
        [SerializeField] TMP_Text[] chipTimes;

        [Header("Payday badge")]
        [SerializeField] RectTransform badgeRect;
        [SerializeField] CanvasGroup badgeGroup;

        [Header("Full-screen pulse")]
        [SerializeField] Image overlay;

        const float AnnounceIn = 0.35f, AnnounceHold = 1.25f, AnnounceOut = 0.4f;
        const float ChipSpacing = 330f;

        static readonly PickupType[] ChipTypes = { PickupType.Deadeye, PickupType.Payday };
        static readonly string[] Seconds = BuildSeconds();

        PickupDirector _dir;
        float _announceT = -1f;
        float _flashT = -1f;
        Color _flashColor;
        float _flashPeak;
        bool _shock;
        bool[] _chipOn;
        int[] _chipShownSecond;
        float[] _chipPop;
        bool _badgeOn;

        static string[] BuildSeconds()
        {
            var a = new string[100];
            for (int i = 0; i < a.Length; i++) a[i] = i + "S";
            return a;
        }

        void Awake()
        {
            _chipOn = new bool[chipRects.Length];
            _chipShownSecond = new int[chipRects.Length];
            _chipPop = new float[chipRects.Length];
        }

        void OnEnable()
        {
            ResetVisuals();
            Bind();
        }

        void OnDisable() => Unbind();

        void ResetVisuals()
        {
            _announceT = -1f;
            _flashT = -1f;
            announceRect.gameObject.SetActive(false);
            overlay.color = Palette.WithAlpha(Color.white, 0f);
            for (int i = 0; i < chipRects.Length; i++)
            {
                _chipOn[i] = false;
                _chipShownSecond[i] = -1;
                chipRects[i].gameObject.SetActive(false);
            }
            _badgeOn = false;
            badgeRect.gameObject.SetActive(false);
        }

        void Bind()
        {
            if (ReferenceEquals(_dir, PickupDirector.Instance)) return;
            Unbind();
            _dir = PickupDirector.Instance;
            if (_dir != null)
            {
                _dir.Collected += OnCollected;
                _dir.ShockwaveFired += OnShockwave;
            }
        }

        void Unbind()
        {
            if (_dir == null) return;
            _dir.Collected -= OnCollected;
            _dir.ShockwaveFired -= OnShockwave;
            _dir = null;
        }

        void OnCollected(PickupType type)
        {
            Color c = PickupTuning.ColorOf(type);
            announceText.text = PickupTuning.NameOf(type);
            announceText.color = c;
            announceSub.text = PickupTuning.SubOf(type);
            announceRect.gameObject.SetActive(true);
            _announceT = 0f;
            if (type == PickupType.Shockwave) return;       // the shockwave has its own, stronger pulse
            _flashColor = c;
            _flashPeak = 0.28f;
            _flashT = 0f;
            _shock = false;
        }

        void OnShockwave(int killed)
        {
            _flashColor = Palette.PickupCyan;
            _flashPeak = 0.5f;
            _flashT = 0f;
            _shock = true;
        }

        void Update()
        {
            Bind();
            float dt = Time.unscaledDeltaTime;
            UpdateAnnounce(dt);
            UpdateFlash(dt);
            UpdateChips(dt);
        }

        void UpdateAnnounce(float dt)
        {
            if (_announceT < 0f) return;
            _announceT += dt;
            float t = _announceT;
            float scale, alpha;
            if (t < AnnounceIn)
            {
                float k = t / AnnounceIn;
                scale = Mathf.LerpUnclamped(0.35f, 1f, Ease.OutBack(k));
                alpha = Mathf.Clamp01(k * 3f);
            }
            else if (t < AnnounceIn + AnnounceHold)
            {
                scale = 1f;
                alpha = 1f;
            }
            else if (t < AnnounceIn + AnnounceHold + AnnounceOut)
            {
                float k = (t - AnnounceIn - AnnounceHold) / AnnounceOut;
                scale = Mathf.Lerp(1f, 1.18f, Ease.OutCubic(k));
                alpha = 1f - k * k;
            }
            else
            {
                _announceT = -1f;
                announceRect.gameObject.SetActive(false);
                return;
            }
            announceRect.localScale = Vector3.one * scale;
            announceGroup.alpha = alpha;
        }

        void UpdateFlash(float dt)
        {
            if (_flashT < 0f) return;
            _flashT += dt;
            float t = _flashT;
            float a;
            if (_shock)
            {
                // hard hit, a second surge as the ring clears the screen, then gone
                a = _flashPeak * Mathf.Exp(-t * 8f) + 0.2f * Mathf.Exp(-Mathf.Pow((t - 0.3f) * 9f, 2f));
                if (t > 1.2f) { _flashT = -1f; overlay.color = Palette.WithAlpha(_flashColor, 0f); return; }
            }
            else
            {
                a = _flashPeak * Mathf.Exp(-t * 9f);
                if (t > 0.6f) { _flashT = -1f; overlay.color = Palette.WithAlpha(_flashColor, 0f); return; }
            }
            overlay.color = Palette.WithAlpha(_flashColor, a);
        }

        void UpdateChips(float dt)
        {
            if (_dir == null) return;
            int count = 0;
            for (int i = 0; i < ChipTypes.Length; i++)
                if (Left(ChipTypes[i]) > 0f) count++;

            int slot = 0;
            for (int i = 0; i < ChipTypes.Length; i++)
            {
                float left = Left(ChipTypes[i]);
                bool on = left > 0f;
                if (on != _chipOn[i])
                {
                    _chipOn[i] = on;
                    chipRects[i].gameObject.SetActive(on);
                    if (on)
                    {
                        _chipPop[i] = 0f;
                        var p = chipRects[i].anchoredPosition;
                        p.x = (slot - (count - 1) * 0.5f) * ChipSpacing;
                        chipRects[i].anchoredPosition = p;
                    }
                }
                if (!on) continue;

                // slide into the centered layout when the other chip comes or goes
                var pos = chipRects[i].anchoredPosition;
                float targetX = (slot - (count - 1) * 0.5f) * ChipSpacing;
                pos.x = Mathf.Lerp(pos.x, targetX, 1f - Mathf.Exp(-14f * dt));
                chipRects[i].anchoredPosition = pos;
                slot++;

                _chipPop[i] = Mathf.Min(1f, _chipPop[i] + dt / 0.3f);
                chipRects[i].localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, Ease.OutBack(_chipPop[i]));

                float total = ChipTypes[i] == PickupType.Deadeye ? PickupTuning.DeadeyeSeconds : PickupTuning.PaydaySeconds;
                chipFills[i].fillAmount = Mathf.Clamp01(left / total);
                int sec = Mathf.Clamp(Mathf.CeilToInt(left), 0, Seconds.Length - 1);
                if (sec != _chipShownSecond[i])
                {
                    _chipShownSecond[i] = sec;
                    chipTimes[i].text = Seconds[sec];
                }

                // blink in the last seconds
                float alpha = 1f;
                if (left <= PickupTuning.EffectWarnSeconds)
                    alpha = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(9f, 18f, 1f - left / PickupTuning.EffectWarnSeconds)));
                chipGroups[i].alpha = alpha;
            }

            // the badge beside the points follows Payday
            float payday = _dir.PaydayLeft;
            bool badge = payday > 0f;
            if (badge != _badgeOn)
            {
                _badgeOn = badge;
                badgeRect.gameObject.SetActive(badge);
            }
            if (badge)
            {
                float pulse = 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 5f);
                badgeRect.localScale = Vector3.one * pulse;
                badgeGroup.alpha = payday <= PickupTuning.EffectWarnSeconds
                    ? 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f)) : 1f;
            }
        }

        float Left(PickupType t) => t == PickupType.Deadeye ? _dir.DeadeyeLeft : _dir.PaydayLeft;
    }
}
