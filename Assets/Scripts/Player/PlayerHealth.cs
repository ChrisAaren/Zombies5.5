using System;
using UnityEngine;

namespace Zombies55
{
    // CONTRACT STUB: the public API below is fixed. The enemies agent implements the behavior
    // (hits to down, regeneration, death -> GameLoop.Lose()).
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        public static PlayerHealth Instance { get; private set; }

        [SerializeField] float maxHealth = 100f;

        public float MaxHealth => maxHealth;
        public float Health { get; private set; } = 100f;
        public float Normalized => Health / maxHealth;
        public bool IsAlive => Health > 0f;

#pragma warning disable CS0067 // raised by the implementation
        public event Action<DamageInfo> Damaged;
        public event Action<float> HealthChanged;   // normalized 0..1
        public event Action Died;
#pragma warning restore CS0067

        void Awake() => Instance = this;
        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void TakeDamage(DamageInfo info) { }
    }
}
