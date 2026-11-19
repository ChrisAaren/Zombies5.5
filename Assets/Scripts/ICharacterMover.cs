using System;
using UnityEngine;

namespace Zombies55
{
    // What CharacterMotion needs to animate a rig: implemented by the player and by pedestrians.
    public interface ICharacterMover
    {
        Vector3 Velocity { get; }
        float MaxSpeed { get; }
        bool IsGrounded { get; }
        event Action Jumped;
        event Action<float> Landed;
    }
}
