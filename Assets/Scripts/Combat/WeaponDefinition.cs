using System;
using UnityEngine;

namespace Zombies55
{
    // Semi: one shot per trigger pull. Auto: held. Pump: semi with a pump cycle and a tube reload.
    // Burst: one pull fires BurstCount rounds at RoundsPerMinute, then BurstPause before the next pull is accepted.
    // Bolt: semi with a long bolt cycle between shots that blocks firing (and a bolt sound at the end of it).
    public enum FireMode { Semi, Auto, Pump, Burst, Bolt }

    // Tuning for one gun. The catalog below is the game's full arsenal.
    [Serializable]
    public class WeaponDefinition
    {
        public string Id;
        public string DisplayName;
        public int Tier;                 // 0..4: the district tier whose kiosks sell it (0 = Fountain Square)
        public bool Handgun;             // held and carried like the P-9 (one-handed grip, no shouldering)
        public int Cost;                 // points at a kiosk; 0 = starting weapon
        public FireMode Mode;
        public float Damage;             // per pellet
        public int Pellets = 1;
        public float RoundsPerMinute;    // Burst: the rate inside a burst
        public int BurstCount = 3;
        public float BurstPause = 0.28f; // Burst: seconds after the last round of a burst before the next pull works
        public int MagazineSize;
        public int MaxReserve;
        public float ReloadSeconds;
        public float HipSpread;          // degrees (cone half-angle) when not aiming
        public float AimSpread;          // degrees when aiming down sights
        public float Range = 120f;
        public float HeadshotMultiplier = 2f;
        public float RecoilPitch;        // degrees of camera kick per shot
        public float RecoilYaw;          // random +/- degrees per shot
        public float Shake;              // camera impulse per shot (per burst for Burst); 0 = none
        public int Pierce = 1;           // enemies one bullet passes through (damage drops PierceFalloff per enemy)
        public float PierceFalloff = 0.85f;
        public bool Projectile;          // fires a 40 mm grenade instead of a hitscan bullet
        public float ProjectileSpeed = 55f;
        public float ArmDistance = 3f;   // closer than this the shell is a dud
        public float BlastRadius = 5f;
        public float BlastDamage;        // at the centre, before falloff
        public string ModelName;         // FBX in Assets/Art/Models/Weapons/

        public int AmmoCost => Cost > 0 ? Cost / 2 : 250;
        public int BuyCost => Cost > 0 ? Cost : 500;      // the starter costs something when it is bought back at the kiosk
        public float SecondsPerShot => 60f / RoundsPerMinute;
        public Color TierColor => Palette.Tier(Tier);

        // Cycle length of one trigger pull, seconds: the time between the first rounds of two bursts for Burst.
        public float CycleSeconds => Mode == FireMode.Burst ? (BurstCount - 1) * SecondsPerShot + BurstPause : SecondsPerShot;
        public float RoundsPerCycle => Mode == FireMode.Burst ? BurstCount : 1f;
        // Damage of one round against one enemy, body shot, before range falloff (a shell counts its blast centre).
        public float DamagePerRound => Projectile ? BlastDamage : Damage * Pellets;
        // Single-target body-shot damage per second while the magazine lasts.
        public float Dps => DamagePerRound * RoundsPerCycle / CycleSeconds;
    }

    public static class WeaponCatalog
    {
        public const string StarterId = "p9";

        public static readonly string[] TierNames = { "T0", "T1", "T2", "T3", "T4" };

        public static readonly WeaponDefinition[] All =
        {
            // ---- Tier 0: Fountain Square
            new WeaponDefinition { Id = "p9", DisplayName = "P-9 Sidearm", Tier = 0, Handgun = true, Cost = 0, Mode = FireMode.Semi,
                Damage = 34, RoundsPerMinute = 420, MagazineSize = 12, MaxReserve = 84, ReloadSeconds = 1.4f,
                HipSpread = 1.6f, AimSpread = 0.4f, RecoilPitch = 1.2f, RecoilYaw = 0.4f, ModelName = "Gun_Pistol" },
            new WeaponDefinition { Id = "mule", DisplayName = "Mule .44", Tier = 0, Handgun = true, Cost = 500, Mode = FireMode.Semi,
                Damage = 100, RoundsPerMinute = 150, MagazineSize = 6, MaxReserve = 42, ReloadSeconds = 2.6f,
                HipSpread = 2.4f, AimSpread = 0.3f, Range = 150f, HeadshotMultiplier = 2.5f, RecoilPitch = 3.4f, RecoilYaw = 0.9f,
                Shake = 0.35f, ModelName = "Gun_Revolver" },
            new WeaponDefinition { Id = "wasp", DisplayName = "Wasp SMG", Tier = 0, Cost = 750, Mode = FireMode.Auto,
                Damage = 24, RoundsPerMinute = 850, MagazineSize = 32, MaxReserve = 192, ReloadSeconds = 2.0f,
                HipSpread = 3.0f, AimSpread = 1.2f, RecoilPitch = 0.6f, RecoilYaw = 0.5f, ModelName = "Gun_SMG" },

            // ---- Tier 1: inner sides
            new WeaponDefinition { Id = "breaker", DisplayName = "Breaker 12", Tier = 1, Cost = 1000, Mode = FireMode.Pump,
                Damage = 44, Pellets = 8, RoundsPerMinute = 75, MagazineSize = 6, MaxReserve = 42, ReloadSeconds = 2.8f,
                HipSpread = 6.5f, AimSpread = 4.5f, Range = 35f, HeadshotMultiplier = 1.5f, RecoilPitch = 4f, RecoilYaw = 1f,
                Shake = 0.3f, ModelName = "Gun_Shotgun" },
            new WeaponDefinition { Id = "rook", DisplayName = "Rook Carbine", Tier = 1, Cost = 1250, Mode = FireMode.Burst,
                Damage = 60, RoundsPerMinute = 1000, BurstCount = 3, BurstPause = 0.28f, MagazineSize = 30, MaxReserve = 210,
                ReloadSeconds = 2.1f, HipSpread = 2.6f, AimSpread = 0.45f, Range = 140f, RecoilPitch = 0.8f, RecoilYaw = 0.4f,
                Shake = 0.2f, ModelName = "Gun_Carbine" },

            // ---- Tier 2: inner corners
            new WeaponDefinition { Id = "kestrel", DisplayName = "Kestrel AR", Tier = 2, Cost = 1500, Mode = FireMode.Auto,
                Damage = 55, RoundsPerMinute = 650, MagazineSize = 30, MaxReserve = 240, ReloadSeconds = 2.3f,
                HipSpread = 2.4f, AimSpread = 0.5f, RecoilPitch = 0.9f, RecoilYaw = 0.45f, ModelName = "Gun_Rifle" },
            new WeaponDefinition { Id = "bulldog", DisplayName = "Bulldog Auto-12", Tier = 2, Cost = 1750, Mode = FireMode.Auto,
                Damage = 22, Pellets = 8, RoundsPerMinute = 240, MagazineSize = 12, MaxReserve = 72, ReloadSeconds = 3.0f,
                HipSpread = 5.5f, AimSpread = 3.8f, Range = 30f, HeadshotMultiplier = 1.5f, RecoilPitch = 3.2f, RecoilYaw = 1f,
                Shake = 0.25f, ModelName = "Gun_AutoShotgun" },

            // ---- Tier 3: outer west / east columns
            new WeaponDefinition { Id = "longshot", DisplayName = "Longshot DMR", Tier = 3, Cost = 2250, Mode = FireMode.Semi,
                Damage = 240, RoundsPerMinute = 200, MagazineSize = 10, MaxReserve = 70, ReloadSeconds = 2.6f,
                HipSpread = 3.5f, AimSpread = 0.1f, Range = 200f, HeadshotMultiplier = 3f, RecoilPitch = 3f, RecoilYaw = 0.6f,
                Shake = 0.3f, ModelName = "Gun_DMR" },
            new WeaponDefinition { Id = "atlas", DisplayName = "Atlas LMG", Tier = 3, Cost = 2500, Mode = FireMode.Auto,
                Damage = 80, RoundsPerMinute = 600, MagazineSize = 75, MaxReserve = 300, ReloadSeconds = 2.9f,
                HipSpread = 3.2f, AimSpread = 1.0f, RecoilPitch = 1.0f, RecoilYaw = 0.7f, ModelName = "Gun_LMG" },

            // ---- Tier 4: outer north / south rows
            new WeaponDefinition { Id = "warden", DisplayName = "Warden .50", Tier = 4, Cost = 3000, Mode = FireMode.Bolt,
                Damage = 1200, RoundsPerMinute = 45, MagazineSize = 5, MaxReserve = 35, ReloadSeconds = 3.2f,
                HipSpread = 4f, AimSpread = 0.02f, Range = 300f, HeadshotMultiplier = 3f, RecoilPitch = 5.5f, RecoilYaw = 0.8f,
                Shake = 0.9f, Pierce = 4, PierceFalloff = 0.85f, ModelName = "Gun_Sniper50" },
            new WeaponDefinition { Id = "hammerhead", DisplayName = "Hammerhead GL", Tier = 4, Cost = 3500, Mode = FireMode.Semi,
                Projectile = true, ProjectileSpeed = 55f, ArmDistance = 3f, BlastRadius = 5f, BlastDamage = 500,
                RoundsPerMinute = 120, MagazineSize = 6, MaxReserve = 30, ReloadSeconds = 3.5f,
                HipSpread = 1.0f, AimSpread = 0.2f, Range = 120f, RecoilPitch = 4.5f, RecoilYaw = 0.7f,
                Shake = 0.6f, ModelName = "Gun_GrenadeLauncher" },
        };

        public static WeaponDefinition Get(string id)
        {
            foreach (var w in All) if (w.Id == id) return w;
            return null;
        }
    }
}
