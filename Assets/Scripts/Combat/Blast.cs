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
        public static event Action<Vector3, float, float, BlastKind, bool> Detonated;   // position, radius, damage, kind, fromPlayer

        static readonly Collider[] s_Buffer = new Collider[160];
        static readonly Dictionary<IDamageable, Collider> s_Found = new Dictionary<IDamageable, Collider>();
        static readonly List<KeyValuePair<IDamageable, Collider>> s_Work = new List<KeyValuePair<IDamageable, Collider>>();

        // position: blast centre. radius: metres. damage: at the centre. source: the object that blew up (an explosive prop) or
        // the shooter (the player, for a shell); it is only written to DamageInfo.Source. ignore: a damageable the blast skips
        // (the prop that is exploding).
        public static BlastResult Detonate(Vector3 position, float radius, float damage, GameObject source, bool fromPlayer,
            BlastKind kind, IDamageable ignore = null)
        {
            CombatFX.Instance?.Explosion(position, radius);
            CombatAudio.Instance?.PlayExplosion(position, kind == BlastKind.Car);
            var result = Damage(position, radius, damage, source, fromPlayer, ignore);
            Detonated?.Invoke(position, radius, damage, kind, fromPlayer);
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
