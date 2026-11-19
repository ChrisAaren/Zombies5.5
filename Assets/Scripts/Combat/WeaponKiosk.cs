using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [SerializeField] float useRange = 2f;
        [SerializeField] float spinDegreesPerSecond = 40f;
        [SerializeField] float bobHeight = 0.03f;

        WeaponDefinition _def;
        float _hold;
        bool _consumed;
        bool _near;
        bool _labelOwned;
        Vector3 _displayRest;

        public void Configure(string id, Transform displayPoint, TextMeshPro label)
        {
            weaponId = id;
            display = displayPoint;
            priceLabel = label;
        }

        void Start()
        {
            _def = WeaponCatalog.Get(weaponId);
            if (display != null) _displayRest = display.localPosition;
            RefreshLabel(false);
        }

        void OnDisable()
        {
            if (_near) { _near = false; PlayerAtKiosk = false; BuyHoldProgress = 0f; }
            HudPrompt.Clear(this);
        }

        void Update()
        {
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
            int cost = owned ? _def.AmmoCost : _def.Cost;
            bool wallet = PlayerWallet.Instance != null;
            bool affordable = wallet && PlayerWallet.Instance.CanAfford(cost);

            if (full) HudPrompt.Show(this, _def.DisplayName + " ammo is full", true);
            else if (owned) HudPrompt.Show(this, "Hold E for " + _def.DisplayName + " ammo [" + cost + "]", affordable);
            else HudPrompt.Show(this, "Hold E to buy " + _def.DisplayName + " [" + cost + "]", affordable);

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool held = !full && ((kb != null && kb.eKey.isPressed) || (pad != null && pad.buttonWest.isPressed));
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
            priceLabel.text = owned ? _def.DisplayName.ToUpperInvariant() + "\nAMMO " + _def.AmmoCost
                                    : _def.DisplayName.ToUpperInvariant() + "\n" + _def.Cost;
        }
    }
}
