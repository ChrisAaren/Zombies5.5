using UnityEngine;

namespace Zombies55
{
    // The 40 mm grenade: flies a ballistic arc (gravity), trails light smoke, explodes on first impact through Blast.Detonate.
    // Closer than the arming distance it is a dud: a thump and a puff, no blast. Pooled by GrenadeLauncher.
    public class GrenadeProjectile : MonoBehaviour
    {
        const float Gravity = 9.81f;
        const float MaxLife = 6f;
        const float Radius = 0.07f;

        Vector3 _vel;
        Vector3 _launchPos;
        float _life;
        float _arm, _blastRadius, _blastDamage;
        bool _fromPlayer;
        GameObject _owner;
        TrailRenderer _trail;
        GrenadeLauncher _pool;

        public bool Active { get; private set; }

        public void Init(GrenadeLauncher pool)
        {
            _pool = pool;
            _trail = GetComponentInChildren<TrailRenderer>(true);
            Active = false;
            gameObject.SetActive(false);
        }

        public void Launch(Vector3 pos, Vector3 velocity, WeaponDefinition def, GameObject owner, bool fromPlayer)
        {
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(velocity);
            _launchPos = pos;
            _vel = velocity;
            _life = 0f;
            _arm = def.ArmDistance;
            _blastRadius = def.BlastRadius;
            _blastDamage = def.BlastDamage;
            _owner = owner;
            _fromPlayer = fromPlayer;
            Active = true;
            gameObject.SetActive(true);
            if (_trail != null)
            {
                if (_trail.sharedMaterial == null && CombatFX.Instance != null) _trail.sharedMaterial = CombatFX.Instance.SoftMaterial;
                _trail.Clear();
            }
        }

        void Update()
        {
            if (!Active) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _life += dt;

            Vector3 start = transform.position;
            _vel += Vector3.down * (Gravity * dt);
            Vector3 step = _vel * dt;
            float len = step.magnitude;

            if (len > 1e-4f && Physics.SphereCast(start, Radius, step / len, out var hit, len, GameLayers.ShotMask,
                    QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;
                Impact(hit.point + hit.normal * 0.05f, hit.normal);
                return;
            }

            transform.position = start + step;
            if (step.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(step);
            if (_life > MaxLife || transform.position.y < -20f) Retire();
        }

        void Impact(Vector3 point, Vector3 normal)
        {
            bool armed = (point - _launchPos).magnitude >= _arm;
            if (armed)
            {
                Blast.Detonate(point, _blastRadius, _blastDamage, _owner, _fromPlayer, BlastKind.Grenade);
            }
            else
            {
                CombatFX.Instance?.Dud(point);
                CombatAudio.Instance?.PlayDud(point);
            }
            Retire();
        }

        void Retire()
        {
            Active = false;
            gameObject.SetActive(false);
            if (_pool != null) _pool.Release(this);
        }
    }
}
