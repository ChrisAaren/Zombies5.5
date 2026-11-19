using UnityEngine;

namespace Zombies55
{
    // Shared visual effects for enemies. One reused particle system, emitted on demand.
    public class EnemyEffects : MonoBehaviour
    {
        public static EnemyEffects Instance { get; private set; }

        [SerializeField] ParticleSystem dustPuff;

        void Awake() => Instance = this;
        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        // A small ground dust cloud where an enemy goes down.
        public void DustPuff(Vector3 position)
        {
            if (dustPuff == null) return;
            dustPuff.transform.position = position + Vector3.up * 0.1f;
            dustPuff.Emit(10);
        }
    }
}
