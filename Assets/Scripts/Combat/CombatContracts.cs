using UnityEngine;

namespace Zombies55
{
    // Shared combat types. Every system that deals or takes damage speaks these.

    public enum HitZone { Body, Head }

    public enum DamageKind { Bullet, Explosion, Melee }

    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        public Vector3 Direction;     // direction the damage travels (shot direction, blast outward)
        public HitZone Zone;
        public DamageKind Kind;
        public GameObject Source;     // who caused it: the player, an explosive prop, an enemy
        public bool FromPlayer;       // true for the player's bullets and explosions the player set off
        public bool Scripted;         // pickup kills (Shockwave): credited to the player, but no per-kill points and no drop roll
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(DamageInfo info);
    }

    // Physics layers (set up in ProjectSettings/TagManager by the foundation step).
    public static class GameLayers
    {
        public const int Default = 0;
        public const int IgnoreRaycast = 2;
        public const int Enemy = 8;       // enemy hitboxes and bodies
        public const int Explosive = 9;   // barrels and explosive cars
        public const int Player = 10;     // the player's CharacterController
        public const int Debris = 12;     // pooled blast debris; collides with the world only
        public const int Ragdoll = 13;    // pooled ragdoll bodies; collide with the world, cars and barriers only; never hit by bullets or blasts
        public const int Barrier = 11;    // district barrier colliders; not shootable, not baked into the NavMesh

        // What the player's bullets can hit: world, enemies, explosives. Never the player.
        public const int ShotMask = (1 << Default) | (1 << Enemy) | (1 << Explosive);
        // What explosions look for.
        public const int BlastMask = (1 << Enemy) | (1 << Explosive) | (1 << Player);
    }
}
