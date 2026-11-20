using UnityEngine;

namespace Zombies55
{
    public enum PickupType { AmmoCache = 0, Shockwave = 1, Deadeye = 2, Payday = 3 }

    // Every number that shapes item drops, in one place.
    public static class PickupTuning
    {
        // ---- drop rules (only kills credited to the player roll)
        public const float BaseDropChance = 0.03f;       // per kill
        public const int PityKills = 25;                 // a drop is guaranteed once this many kills passed without one
        public const int MaxDropsPerRound = 4;
        // Weights indexed by PickupType: AmmoCache, Shockwave, Deadeye, Payday. A type never drops twice in a row.
        public static readonly float[] Weights = { 40f, 16f, 22f, 22f };

        // ---- lifetime and pickup
        public const float DropLifetime = 25f;
        public const float BlinkSeconds = 6f;            // the last N seconds blink, speeding up
        public const float BlinkSlowHz = 3f;
        public const float BlinkFastHz = 11f;
        public const float PickupRadius = 1.2f;          // metres, horizontal
        public const float PickupHeight = 2.5f;          // max vertical distance from the ground spot
        public const int PoolSize = 8;                   // 4 per round, and they outlive the round break

        // ---- effects
        public const float DeadeyeSeconds = 30f;
        public const float PaydaySeconds = 30f;
        public const int PaydayMultiplier = 2;
        public const int ShockwaveBonus = 400;           // flat, instead of per-kill points (doubled under Payday)
        public const float EffectWarnSeconds = 5f;       // chips blink in the last N seconds

        // ---- presentation
        public const float Hover = 0.9f;                 // metres above the ground
        public const float ModelScale = 1.8f;
        public const float SpinDegreesPerSecond = 90f;
        public const float BobAmplitude = 0.07f;
        public const float EmissionMin = 2f;
        public const float EmissionMax = 4f;
        public const float PulseHz = 0.7f;
        public const float PopSeconds = 0.35f;
        public const float BeamHeight = 14f;
        public const float BeamWidth = 0.7f;
        public const float BeamIntensity = 1.25f;
        public const float GroundGlowIntensity = 0.8f;
        public const float GroundGlowSize = 3.4f;
        public const float ShockwaveRadius = 48f;
        public const float ShockwaveRingSeconds = 1.6f;

        public static Color ColorOf(PickupType t)
        {
            switch (t)
            {
                case PickupType.AmmoCache: return Palette.PickupAmber;
                case PickupType.Shockwave: return Palette.PickupCyan;
                case PickupType.Deadeye: return Palette.PickupRed;
                default: return Palette.PickupLime;
            }
        }

        public static string NameOf(PickupType t)
        {
            switch (t)
            {
                case PickupType.AmmoCache: return "AMMO CACHE";
                case PickupType.Shockwave: return "SHOCKWAVE";
                case PickupType.Deadeye: return "DEADEYE";
                default: return "PAYDAY";
            }
        }

        public static string SubOf(PickupType t)
        {
            switch (t)
            {
                case PickupType.AmmoCache: return "AMMO REFILLED";
                case PickupType.Shockwave: return "ALL ENEMIES ELIMINATED  +" + ShockwaveBonus;
                case PickupType.Deadeye: return "ONE SHOT KILLS  " + (int)DeadeyeSeconds + "S";
                default: return "DOUBLE POINTS  " + (int)PaydaySeconds + "S";
            }
        }
    }

    // The drop rules as plain state, so the game and the simulation test run exactly the same code.
    public class DropRules
    {
        System.Random _rng;                              // null: UnityEngine.Random
        public int KillsSinceDrop;
        public int DropsThisRound;
        public PickupType Last = (PickupType)(-1);

        public DropRules(System.Random rng = null) { _rng = rng; }

        float Next() => _rng != null ? (float)_rng.NextDouble() : Random.value;

        public void NewRound() => DropsThisRound = 0;

        // One player-credited kill. True when it drops something, with the type.
        public bool OnKill(out PickupType type)
        {
            type = default;
            KillsSinceDrop++;
            if (DropsThisRound >= PickupTuning.MaxDropsPerRound) return false;
            if (KillsSinceDrop < PickupTuning.PityKills && Next() >= PickupTuning.BaseDropChance) return false;

            float total = 0f;
            for (int i = 0; i < PickupTuning.Weights.Length; i++) if (i != (int)Last) total += PickupTuning.Weights[i];
            float r = Next() * total;
            int pick = -1;
            for (int i = 0; i < PickupTuning.Weights.Length; i++)
            {
                if (i == (int)Last) continue;
                pick = i;
                r -= PickupTuning.Weights[i];
                if (r < 0f) break;
            }
            type = (PickupType)pick;
            Last = type;
            KillsSinceDrop = 0;
            DropsThisRound++;
            return true;
        }
    }
}
