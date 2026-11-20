using System;
using UnityEngine;

namespace Zombies55
{
    // The one place the explosion effects scale. Level 0 = Low, 1 = Medium, 2 = High (the shipped default), 3 = Ultra.
    // A graphics-settings screen only has to set EffectsDetail.Level; every explosion reads the numbers below when it fires.
    public static class EffectsDetail
    {
        public const int Min = 0, Max = 3, Default = 2;

        static int s_Level = Default;

        // Raised after the level changes (live pools that cap by level re-read on their next use).
        public static event Action Changed;

        public static int Level
        {
            get => s_Level;
            set
            {
                int v = Mathf.Clamp(value, Min, Max);
                if (v == s_Level) return;
                s_Level = v;
                Changed?.Invoke();
            }
        }

        static readonly float[] ParticleTable = { 0.35f, 0.65f, 1f, 1.4f };
        static readonly int[] DebrisTable = { 16, 36, 60, 84 };
        static readonly float[] SmokeLifeTable = { 0.4f, 0.7f, 1f, 1.25f };
        static readonly int[] LightTable = { 1, 1, 2, 3 };
        static readonly int[] BurnTable = { 1, 2, 4, 6 };
        static readonly int[] ScorchTable = { 6, 12, 24, 40 };

        // Multiplies every emitted particle count (fireballs, smoke puffs, sparks, embers, bits).
        public static float ParticleScale => ParticleTable[s_Level];
        // Cap on simultaneously active physics debris parts.
        public static int MaxDebris => DebrisTable[s_Level];
        // Multiplies smoke and wreck-fire lifetimes (6-10 s columns at High).
        public static float SmokeLifetimeScale => SmokeLifeTable[s_Level];
        // Concurrent explosion point lights (flash spike plus orange glow); none of them cast shadows.
        public static int MaxLights => LightTable[s_Level];
        // Wrecks that can burn with the fire-loop flipbook at once.
        public static int MaxBurningWrecks => BurnTable[s_Level];
        // Ground scorch marks kept until the round resets.
        public static int MaxScorchMarks => ScorchTable[s_Level];
        // Brief exposure and bloom punch on a close blast.
        public static bool ExposurePunch => s_Level >= 1;
        // Particles that bounce off the world (debris bits, embers).
        public static bool ParticleCollision => s_Level >= 1;
    }
}
