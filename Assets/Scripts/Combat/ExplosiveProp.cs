using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Zombies55
{
    public enum ExplosiveKind { Barrel, Car }

    // Barrels blow almost at once; parked cars smoke, burn for a fuse, then blow and stay as a charred wreck.
    // Credit: any player damage marks the prop as player-caused, and its blast carries FromPlayer onward.
    [RequireComponent(typeof(Collider))]
    public class ExplosiveProp : MonoBehaviour, IDamageable
    {
        [SerializeField] ExplosiveKind kind = ExplosiveKind.Barrel;
        [SerializeField] float maxHealth = 60f;
        [SerializeField] float blastRadius = 6f;
        [SerializeField] float blastDamage = 140f;
        [SerializeField] float carFuseSeconds = 1.5f;
        [SerializeField] Material wreckMaterial;

        float _hp;
        bool _exploded;
        bool _playerCaused;
        float _fuseEnd = -1f;
        float _resetAt = -1f;   // a round started mid-fuse: let the blast happen, then reset
        Renderer[] _renderers;
        Material[][] _origMats;
        Collider[] _colliders;
        NavMeshObstacle _obstacle;
        ParticleSystem _smoke, _fire;
        RoundManager _rounds;

        public bool IsAlive => !_exploded && _hp > 0f;
        public ExplosiveKind Kind => kind;
        public bool PlayerCaused => _playerCaused;

        // Editor-time setup with per-kind defaults.
        public void Configure(ExplosiveKind k, Material wreck)
        {
            kind = k;
            wreckMaterial = wreck;
            if (k == ExplosiveKind.Barrel)
            {
                maxHealth = 60f; blastRadius = 6f; blastDamage = 140f;
            }
            else
            {
                maxHealth = 350f; blastRadius = 8f; blastDamage = 190f;
            }
        }

        void Awake()
        {
            _hp = maxHealth;
            var rs = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer)) rs.Add(r);
            _renderers = rs.ToArray();
            _origMats = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++) _origMats[i] = _renderers[i].sharedMaterials;
            _colliders = GetComponentsInChildren<Collider>(true);
            _obstacle = GetComponent<NavMeshObstacle>();
        }

        void OnDestroy()
        {
            if (_rounds != null) _rounds.RoundStarted -= OnRoundStarted;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;
            _hp -= info.Amount;
            if (info.FromPlayer) _playerCaused = true;

            if (kind == ExplosiveKind.Car) UpdateSmoke();

            if (_hp <= 0f)
            {
                _hp = 0f;
                if (kind == ExplosiveKind.Barrel)
                {
                    _fuseEnd = Time.time + Random.Range(0.06f, 0.18f);
                }
                else
                {
                    _fuseEnd = Time.time + carFuseSeconds;
                    StartFire();
                }
            }
        }

        void Update()
        {
            if (_resetAt > 0f && Time.time >= _resetAt) ResetProp();

            if (_rounds == null && RoundManager.Instance != null)
            {
                _rounds = RoundManager.Instance;
                _rounds.RoundStarted += OnRoundStarted;
            }

            if (_fuseEnd > 0f)
            {
                if (_fire != null)
                {
                    var em = _fire.emission;
                    em.rateOverTime = 22f + Mathf.Sin(Time.time * 30f) * 6f;
                }
                if (Time.time >= _fuseEnd)
                {
                    Explode();
                    if (_resetAt == 0f) _resetAt = Time.time + 2.5f;
                }
            }
        }

        // ------------------------------------------------------------------ smoke and fire

        void UpdateSmoke()
        {
            float damaged = 1f - _hp / maxHealth;
            if (damaged < 0.3f) return;
            if (_smoke == null)
            {
                if (CombatFX.Instance == null) return;
                _smoke = CombatFX.Instance.CreateSmoke(transform, SmokeLocalPoint());
                _smoke.Play();
            }
            var em = _smoke.emission;
            em.rateOverTime = Mathf.Lerp(4f, 22f, Mathf.InverseLerp(0.3f, 1f, damaged));
        }

        void StartFire()
        {
            UpdateSmoke();
            if (_fire == null && CombatFX.Instance != null)
            {
                _fire = CombatFX.Instance.CreateFire(transform, SmokeLocalPoint());
                _fire.Play();
            }
        }

        Vector3 SmokeLocalPoint()
        {
            // Roughly the engine bay of a car along its length, or the barrel top.
            var col = _colliders.Length > 0 ? _colliders[0] as BoxCollider : null;
            if (col != null) return col.center + Vector3.up * (col.size.y * 0.5f);
            return Vector3.up * 0.9f;
        }

        // ------------------------------------------------------------------ explosion

        void Explode()
        {
            _exploded = true;
            _fuseEnd = -1f;
            Vector3 center = WorldCenter();
            bool big = kind == ExplosiveKind.Car;

            Blast.Detonate(center, blastRadius, blastDamage, gameObject, _playerCaused,
                big ? BlastKind.Car : BlastKind.Barrel, this);

            if (_fire != null) _fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (kind == ExplosiveKind.Barrel)
            {
                foreach (var r in _renderers) r.enabled = false;
                foreach (var c in _colliders) c.enabled = false;
                if (_obstacle != null) _obstacle.enabled = false;
                if (_smoke != null) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            else
            {
                if (wreckMaterial != null)
                {
                    foreach (var r in _renderers)
                    {
                        var mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = wreckMaterial;
                        r.sharedMaterials = mats;
                    }
                }
                if (_smoke == null && CombatFX.Instance != null)
                {
                    _smoke = CombatFX.Instance.CreateSmoke(transform, SmokeLocalPoint());
                    _smoke.Play();
                }
                if (_smoke != null)
                {
                    var em = _smoke.emission;
                    em.rateOverTime = 9f;   // lingering smoke over the wreck
                }
            }
        }

        Vector3 WorldCenter()
        {
            if (_colliders.Length > 0 && _colliders[0] != null)
            {
                var b = _colliders[0].bounds;
                return new Vector3(b.center.x, b.min.y + (kind == ExplosiveKind.Car ? 0.7f : 0.45f), b.center.z);
            }
            return transform.position + Vector3.up * 0.5f;
        }

        // ------------------------------------------------------------------ reset

        void OnRoundStarted(int round)
        {
            if (_fuseEnd > 0f) { _resetAt = 0f; return; }   // mid-fuse: reset 2.5 s after the pending explosion instead of silently now
            ResetProp();
        }

        public void ResetProp()
        {
            _hp = maxHealth;
            _exploded = false;
            _playerCaused = false;
            _fuseEnd = -1f;
            _resetAt = -1f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].enabled = true;
                _renderers[i].sharedMaterials = _origMats[i];
            }
            foreach (var c in _colliders) c.enabled = true;
            if (_obstacle != null) _obstacle.enabled = true;
            if (_smoke != null) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_fire != null) _fire.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
