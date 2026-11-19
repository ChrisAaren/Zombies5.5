using System;
using UnityEngine;

namespace Zombies55
{
    // CONTRACT STUB: the public API below is fixed. The combat agent implements the behavior
    // (two slots, firing, reload, aim, buying via Give/RefillAmmo).
    public class PlayerWeapons : MonoBehaviour
    {
        public const int SlotCount = 2;

        public static PlayerWeapons Instance { get; private set; }

        public WeaponDefinition Current => GetSlot(ActiveSlot);
        public int ActiveSlot { get; private set; }
        public int Magazine { get; private set; }            // rounds in the current gun
        public int Reserve { get; private set; }             // spare rounds for the current gun
        public bool IsReloading { get; private set; }
        public float ReloadProgress { get; private set; }    // 0..1 while reloading
        public bool IsAiming { get; private set; }
        public float CurrentSpreadDegrees { get; private set; }

#pragma warning disable CS0067 // raised by the implementation
        public event Action WeaponChanged;                   // slot switch, buy, replace
        public event Action AmmoChanged;
        public event Action Fired;
        public event Action<bool, bool> HitConfirmed;        // (killed, headshot)
#pragma warning restore CS0067

        void Awake() => Instance = this;
        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public WeaponDefinition GetSlot(int slot) => null;
        public bool Owns(string weaponId) => false;
        public void Give(WeaponDefinition weapon) { }        // fills its ammo; replaces the active slot when both are full
        public void RefillAmmo(string weaponId) { }
    }
}
