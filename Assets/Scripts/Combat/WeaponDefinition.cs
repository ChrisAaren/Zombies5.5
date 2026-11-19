using System;

namespace Zombies55
{
    public enum FireMode { Semi, Auto, Pump }

    // Tuning for one gun. The catalog below is the game's full arsenal.
    [Serializable]
    public class WeaponDefinition
    {
        public string Id;
        public string DisplayName;
        public int Cost;                 // points at a kiosk; 0 = starting weapon
        public FireMode Mode;
        public float Damage;             // per pellet
        public int Pellets = 1;
        public float RoundsPerMinute;
        public int MagazineSize;
        public int MaxReserve;
        public float ReloadSeconds;
        public float HipSpread;          // degrees (cone half-angle) when not aiming
        public float AimSpread;          // degrees when aiming down sights
        public float Range = 120f;
        public float HeadshotMultiplier = 2f;
        public float RecoilPitch;        // degrees of camera kick per shot
        public float RecoilYaw;          // random +/- degrees per shot
        public string ModelName;         // FBX in Assets/Art/Models/Weapons/

        public int AmmoCost => Cost > 0 ? Cost / 2 : 250;
        public float SecondsPerShot => 60f / RoundsPerMinute;
    }

    public static class WeaponCatalog
    {
        public const string StarterId = "p9";

        public static readonly WeaponDefinition[] All =
        {
            new WeaponDefinition { Id = "p9", DisplayName = "P-9 Sidearm", Cost = 0, Mode = FireMode.Semi,
                Damage = 34, RoundsPerMinute = 420, MagazineSize = 12, MaxReserve = 84, ReloadSeconds = 1.4f,
                HipSpread = 1.6f, AimSpread = 0.4f, RecoilPitch = 1.2f, RecoilYaw = 0.4f, ModelName = "Gun_Pistol" },
            new WeaponDefinition { Id = "wasp", DisplayName = "Wasp SMG", Cost = 1000, Mode = FireMode.Auto,
                Damage = 24, RoundsPerMinute = 850, MagazineSize = 32, MaxReserve = 192, ReloadSeconds = 2.0f,
                HipSpread = 3.0f, AimSpread = 1.2f, RecoilPitch = 0.6f, RecoilYaw = 0.5f, ModelName = "Gun_SMG" },
            new WeaponDefinition { Id = "breaker", DisplayName = "Breaker 12", Cost = 1250, Mode = FireMode.Pump,
                Damage = 20, Pellets = 8, RoundsPerMinute = 70, MagazineSize = 6, MaxReserve = 42, ReloadSeconds = 2.8f,
                HipSpread = 6.5f, AimSpread = 4.5f, Range = 35f, HeadshotMultiplier = 1.5f, RecoilPitch = 4f, RecoilYaw = 1f,
                ModelName = "Gun_Shotgun" },
            new WeaponDefinition { Id = "kestrel", DisplayName = "Kestrel AR", Cost = 1800, Mode = FireMode.Auto,
                Damage = 38, RoundsPerMinute = 650, MagazineSize = 30, MaxReserve = 240, ReloadSeconds = 2.3f,
                HipSpread = 2.4f, AimSpread = 0.5f, RecoilPitch = 0.9f, RecoilYaw = 0.45f, ModelName = "Gun_Rifle" },
            new WeaponDefinition { Id = "longshot", DisplayName = "Longshot DMR", Cost = 2000, Mode = FireMode.Semi,
                Damage = 130, RoundsPerMinute = 200, MagazineSize = 10, MaxReserve = 70, ReloadSeconds = 2.6f,
                HipSpread = 3.5f, AimSpread = 0.1f, Range = 200f, HeadshotMultiplier = 3f, RecoilPitch = 3f, RecoilYaw = 0.6f,
                ModelName = "Gun_DMR" },
            new WeaponDefinition { Id = "atlas", DisplayName = "Atlas LMG", Cost = 3000, Mode = FireMode.Auto,
                Damage = 44, RoundsPerMinute = 600, MagazineSize = 75, MaxReserve = 300, ReloadSeconds = 2.9f,
                HipSpread = 3.2f, AimSpread = 1.0f, RecoilPitch = 1.0f, RecoilYaw = 0.7f, ModelName = "Gun_LMG" },
        };

        public static WeaponDefinition Get(string id)
        {
            foreach (var w in All) if (w.Id == id) return w;
            return null;
        }
    }
}
