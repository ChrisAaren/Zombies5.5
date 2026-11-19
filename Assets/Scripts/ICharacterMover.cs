using System;
using UnityEngine;

namespace Zombies55
{
    // What the animation components (PlayerAnimation, EnemyAnimation) read from a mover: implemented by the player and the enemies.
    public interface ICharacterMover
    {
        Vector3 Velocity { get; }
        float MaxSpeed { get; }
        bool IsGrounded { get; }
        event Action Jumped;
        event Action<float> Landed;
    }
}
