using TMPro;
using UnityEngine;

namespace Zombies55
{
    // Street arms kiosk. Stand close and face it: hold E (gamepad X) to buy the gun, or its ammo once owned.
    public class WeaponKiosk : MonoBehaviour
    {
        const float BuyHoldSeconds = 0.4f;

        // For the HUD and PlayerWeapons.
        public static bool PlayerAtKiosk { get; private set; }
        public static float BuyHoldProgress { get; private set; }   // 0..1 while holding at a kiosk

        [SerializeField] string weaponId;
        [SerializeField] Transform display;
        [SerializeField] TextMeshPro priceLabel;
        [SerializeField] Renderer screen;          // the renderer that carries the screen panel (it has other materials too)
        [SerializeField] int screenSlot;           // the screen's material slot on that renderer
        [SerializeField] Material screenLit, screenDim;   // tier accent, and the same dimmed while the district is locked
        [SerializeField] float useRange = 2f;
        [SerializeField] float spinDegreesPerSecond = 40f;
        [SerializeField] float bobHeight = 0.03f;

        WeaponDefinition _def;
        float _hold;
        bool _consumed;
        bool _near;
        bool _labelOwned;
        Vector3 _displayRest;
        int _district = -1;
        bool _unlocked = true, _lockApplied;
        float _nextLockCheck;
        Color _labelColor;

        public string WeaponId => weaponId;

        public void Configure(string id, Transform displayPoint, TextMeshPro label, Renderer screenRenderer = null, int slot = 0,
            Material lit = null, Material dim = null)
        {
            weaponId = id;
            display = displayPoint;
            priceLabel = label;
            screen = screenRenderer;
            screenSlot = slot;
            screenLit = lit;
            screenDim = dim;
        }

        void Start()
        {
            _def = WeaponCatalog.Get(weaponId);
            if (display != null) _displayRest = display.localPosition;
            if (priceLabel != null) _labelColor = priceLabel.color;
            _district = ZoneLayout.DistrictAt(transform.position);
            RefreshLabel(false);
            ApplyLock(true);
        }

        // Screen and label read dimmed while the district is still locked behind its barrier.
        void ApplyLock(bool force)
        {
            var zones = ZoneManager.Instance;
            bool unlocked = zones == null || _district < 0 || zones.IsOwned(_district);
            if (!force && unlocked == _unlocked && _lockApplied) return;
            _unlocked = unlocked;
            _lockApplied = true;
            if (priceLabel != null)
            {
                var c = _labelColor;
                priceLabel.color = unlocked ? c : new Color(c.r, c.g, c.b, 0.55f);
            }
            if (screen != null && screenLit != null && screenDim != null)
            {
                var mats = screen.sharedMaterials;
                if (screenSlot >= 0 && screenSlot < mats.Length)
                {
                    mats[screenSlot] = unlocked ? screenLit : screenDim;
                    screen.sharedMaterials = mats;
                }
            }
        }

        void OnDisable()
        {
            if (_near) { _near = false; PlayerAtKiosk = false; BuyHoldProgress = 0f; }
            HudPrompt.Clear(this);
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextLockCheck)
            {
                _nextLockCheck = Time.unscaledTime + 0.25f;
                ApplyLock(false);
            }
            if (display != null)
            {
                display.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
                display.localPosition = _displayRest + Vector3.up * (Mathf.Sin(Time.time * 2f) * bobHeight);
            }

            var weapons = PlayerWeapons.Instance;
            var cam = Camera.main;
            bool running = GameLoop.Instance == null || GameLoop.Instance.State == RunState.Running;
            if (_def == null || weapons == null || cam == null || !running)
            {
                Leave();
                return;
            }

            Vector3 to = transform.position - weapons.transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            Vector3 look = cam.transform.forward;
            look.y = 0f;
            bool facing = dist < 0.3f || Vector3.Dot(look.normalized, to / Mathf.Max(0.01f, dist)) > 0.35f;
            if (dist > useRange || !facing)
            {
                Leave();
                return;
            }

            _near = true;
            PlayerAtKiosk = true;

            bool owned = weapons.Owns(_def.Id);
            if (owned != _labelOwned) RefreshLabel(owned);
            bool full = owned && weapons.IsAmmoFull(_def.Id);
            int cost = owned ? _def.AmmoCost : _def.BuyCost;
            bool wallet = PlayerWallet.Instance != null;
            bool affordable = wallet && PlayerWallet.Instance.CanAfford(cost);

            if (full) HudPrompt.Show(this, _def.DisplayName + " ammo is full", true);
            else if (owned) HudPrompt.Show(this, "Hold " + GameInput.PromptKey(GameAction.Interact) + " for " + _def.DisplayName + " ammo [" + cost + "]", affordable);
            else HudPrompt.Show(this, "Hold " + GameInput.PromptKey(GameAction.Interact) + " to buy " + _def.DisplayName + " [" + cost + "]", affordable);

            bool held = !full && GameInput.IsPressed(GameAction.Interact);
            if (!held)
            {
                _hold = 0f;
                _consumed = false;
                BuyHoldProgress = 0f;
                return;
            }
            if (_consumed) return;

            _hold += Time.deltaTime;
            BuyHoldProgress = Mathf.Clamp01(_hold / BuyHoldSeconds);
            if (_hold < BuyHoldSeconds) return;

            _consumed = true;
            BuyHoldProgress = 0f;
            if (wallet && PlayerWallet.Instance.TrySpend(cost, _def.DisplayName))
            {
                if (owned) weapons.RefillAmmo(_def.Id); else weapons.Give(_def);
                CombatAudio.Instance?.PlayBuy();
            }
            else
            {
                CombatAudio.Instance?.PlayDeny();
            }
        }

        void Leave()
        {
            if (!_near) return;
            _near = false;
            _hold = 0f;
            _consumed = false;
            PlayerAtKiosk = false;
            BuyHoldProgress = 0f;
            HudPrompt.Clear(this);
        }

        void RefreshLabel(bool owned)
        {
            _labelOwned = owned;
            if (priceLabel == null || _def == null) return;
            string tier = WeaponCatalog.TierNames[Mathf.Clamp(_def.Tier, 0, 4)];
            priceLabel.text = owned ? _def.DisplayName.ToUpperInvariant() + "\n" + tier + "  AMMO " + _def.AmmoCost
                                    : _def.DisplayName.ToUpperInvariant() + "\n" + tier + "  " + _def.BuyCost;
        }
    }
}
