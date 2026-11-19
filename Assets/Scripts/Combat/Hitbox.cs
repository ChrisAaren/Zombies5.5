using UnityEngine;

namespace Zombies55
{
    // Put on each collider that bullets should register on. Routes the hit to the IDamageable
    // in its parents and tells it which zone was hit.
    public class Hitbox : MonoBehaviour
    {
        [SerializeField] HitZone zone = HitZone.Body;

        IDamageable _owner;

        public HitZone Zone => zone;

        public IDamageable Owner => _owner ??= GetComponentInParent<IDamageable>();

        public void SetZone(HitZone value) => zone = value;
    }
}
