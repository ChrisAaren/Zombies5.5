using System;
using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    public enum BlastKind { Barrel, Car, Grenade }

    public struct BlastResult
    {
        public int Hits;      // damageables that took damage
        public int Kills;     // of those, how many died from it
    }

    // What a detonation tells its listeners (explosion FX, ragdolls, noise, decals).
    public readonly struct BlastInfo
    {
        public readonly Vector3 Position;   // blast centre, world space
        public readonly float Radius;       // metres; nothing is hurt beyond it
        public readonly float Damage;       // damage at the centre, before falloff
        public readonly float Force;        // velocity change in m/s a free body at the centre gets (it falls off like VelocityAt says)
        public readonly BlastKind Kind;
        public readonly bool FromPlayer;    // the player caused it (kills pay out to the player)
        public readonly GameObject Source;  // the exploding prop or the shooter; may be null

        public BlastInfo(Vector3 position, float radius, float damage, float force, BlastKind kind, bool fromPlayer, GameObject source)
        {
            Position = position; Radius = radius; Damage = damage; Force = force; Kind = kind; FromPlayer = fromPlayer; Source = source;
        }

        // 1 at the centre, 0 at the edge: full strength inside a quarter of the radius, then a smooth fall.
        public float Falloff(Vector3 point)
        {
            float t = Mathf.Clamp01((point - Position).magnitude / Mathf.Max(0.01f, Radius));
            return t < 0.25f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.75f);
        }

        // The velocity change a ragdoll limb or any free body at point should take: away from the centre, lifted a little.
        public Vector3 VelocityAt(Vector3 point)
        {
            Vector3 dir = point - Position;
            dir.y += 0.35f * Mathf.Max(0.5f, dir.magnitude);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.up;
            return dir.normalized * (Force * Falloff(point));
        }
    }

    // The one place explosions happen: damage with falloff and wall shielding, the visual and the audio.
    // Barrels, cars and the grenade launcher all call Detonate. Kills of a blast marked fromPlayer are credited to the
    // player (DamageInfo.FromPlayer), so they pay out and roll item drops like bullet kills.
    public static class Blast
    {
        // Share of the blast the player takes from their own explosions and anything else they set off.
        public const float PlayerDamageScale = 0.6f;
        // Inside this fraction of the radius the damage is full; it falls to 10% at the edge.
        const float FullDamageFraction = 0.25f;
        const float EdgeDamage = 0.1f;
        // Solid walls between the blast and the target take most of it.
        const float WallShield = 0.35f;

        // Raised after every detonation (for the effects that hook explosions: ragdolls, decals, noise).
        // Position, radius, damage, force, kind, fromPlayer and source all travel in the BlastInfo. The explosion FX, the debris pool and
        // the parked-car rock listen here; the ragdoll system hooks in the same way.
        public static event Action<BlastInfo> Detonated;

        // Default Force (m/s of velocity change at the centre) per kind.
        public static float DefaultForce(BlastKind kind) => kind == BlastKind.Car ? 15f : kind == BlastKind.Barrel ? 11f : 9f;

        static readonly Collider[] s_Buffer = new Collider[160];
        static readonly Dictionary<IDamageable, Collider> s_Found = new Dictionary<IDamageable, Collider>();
        static readonly List<KeyValuePair<IDamageable, Collider>> s_Work = new List<KeyValuePair<IDamageable, Collider>>();

        // position: blast centre. radius: metres. damage: at the centre. source: the object that blew up (an explosive prop) or
        // the shooter (the player, for a shell); it is only written to DamageInfo.Source. ignore: a damageable the blast skips
        // (the prop that is exploding).
        public static BlastResult Detonate(Vector3 position, float radius, float damage, GameObject source, bool fromPlayer,
            BlastKind kind, IDamageable ignore = null, float force = 0f)
        {
            var info = new BlastInfo(position, radius, damage, force > 0f ? force : DefaultForce(kind), kind, fromPlayer, source);
            CombatAudio.Instance?.PlayExplosion(position, kind, radius);
            var result = Damage(position, radius, damage, source, fromPlayer, ignore);
            Detonated?.Invoke(info);
            return result;
        }

        static BlastResult Damage(Vector3 center, float radius, float damage, GameObject source, bool fromPlayer, IDamageable ignore)
        {
            var result = new BlastResult();
            int n = Physics.OverlapSphereNonAlloc(center, radius, s_Buffer, GameLayers.BlastMask, QueryTriggerInteraction.Ignore);
            s_Found.Clear();
            for (int i = 0; i < n; i++)
            {
                var c = s_Buffer[i];
                var d = c.GetComponentInParent<IDamageable>();
                if (d == null || ReferenceEquals(d, ignore)) continue;
                if (!s_Found.TryGetValue(d, out var best)
                    || (c.bounds.ClosestPoint(center) - center).sqrMagnitude < (best.bounds.ClosestPoint(center) - center).sqrMagnitude)
                    s_Found[d] = c;
            }

            s_Work.Clear();
            foreach (var kv in s_Found) s_Work.Add(kv);
            s_Found.Clear();

            foreach (var kv in s_Work)
            {
                var d = kv.Key;
                var c = kv.Value;
                if (c == null || !d.IsAlive) continue;
                Vector3 p = c.bounds.ClosestPoint(center);
                float t = Mathf.Clamp01((p - center).magnitude / radius);
                float falloff = t < FullDamageFraction ? 1f : Mathf.Lerp(1f, EdgeDamage, (t - FullDamageFraction) / (1f - FullDamageFraction));
                float amount = damage * falloff;

                Vector3 target = c.bounds.center;
                if (Physics.Linecast(center + Vector3.up * 0.3f, target, 1 << GameLayers.Default, QueryTriggerInteraction.Ignore))
                    amount *= WallShield;
                if (d is PlayerHealth) amount *= PlayerDamageScale;

                Vector3 dir = target - center;
                dir.y += 0.3f;
                d.TakeDamage(new DamageInfo
                {
                    Amount = amount,
                    Point = p,
                    Direction = dir.normalized,
                    Zone = HitZone.Body,
                    Kind = DamageKind.Explosion,
                    Source = source,
                    FromPlayer = fromPlayer,
                });
                result.Hits++;
                if (!d.IsAlive) result.Kills++;
            }
            s_Work.Clear();
            return result;
        }
    }
}
