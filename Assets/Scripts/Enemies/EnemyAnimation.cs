using UnityEngine;

namespace Zombies55
{
    // Drives an enemy's Animator: speed blend (idle / shamble / jog / sprint), attack and hit triggers, death,
    // a random cycle offset so a crowd does not step in lockstep, and a head turn toward the player when near.
    // Lives on the same object as the Animator so OnAnimatorIK is called here.
    [RequireComponent(typeof(Animator))]
    public class EnemyAnimation : MonoBehaviour
    {
        [SerializeField] float lookRange = 7f;
        [SerializeField] float lookEyeHeight = 1.55f;
        [SerializeField] float hitCooldown = 0.9f;

        Animator _anim;
        ICharacterMover _mover;
        Enemy _enemy;
        float _lookWeight;
        float _nextHit;
        bool _dead;
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int CycleOffset = Animator.StringToHash("CycleOffset");
        static readonly int StrideScale = Animator.StringToHash("StrideScale");
        static readonly int Attack = Animator.StringToHash("Attack");
        static readonly int Hit = Animator.StringToHash("Hit");
        static readonly int Dead = Animator.StringToHash("Dead");

        void Awake()
        {
            _anim = GetComponent<Animator>();
            _mover = GetComponentInParent<ICharacterMover>();
            _enemy = GetComponentInParent<Enemy>();
        }

        // Pooled enemies come back through here: restart the state machine and pick a new stride phase.
        void OnEnable()
        {
            if (_anim == null) return;
            _dead = false;
            _suspended = false;
            _lookWeight = 0f;
            _nextHit = 0f;
            _anim.Rebind();
            _anim.SetFloat(CycleOffset, Random.value);
            // A scaled body takes proportionally longer strides: play its stride slower so the feet still match the ground speed.
            _anim.SetFloat(StrideScale, 1f / Mathf.Max(0.5f, transform.lossyScale.y));
            _anim.SetBool(Dead, false);
            _anim.Update(0f);
        }

        void Update()
        {
            if (_mover == null || _dead || _suspended) return;
            Vector3 v = _mover.Velocity;
            v.y = 0f;
            _anim.SetFloat(Speed, v.magnitude, 0.1f, Time.deltaTime);
        }

        // Called at the start of the attack windup; the clip's contact frame (0.45 s) matches the damage time.
        bool _suspended;

        // Knocked down or ragdolled: the rig owns the bones, so no speed blend, knee alignment or head turn.
        public void SetSuspended(bool value)
        {
            _suspended = value;
            if (!value && _anim != null && _anim.enabled) { _lookWeight = 0f; _anim.SetFloat(Speed, 0f); }
        }

        public void PlayAttack() => _anim.SetTrigger(Attack);

        public void PlayHit()
        {
            if (_dead || Time.time < _nextHit) return;
            _nextHit = Time.time + hitCooldown;
            _anim.SetTrigger(Hit);
        }

        public void PlayDeath()
        {
            _dead = true;
            _anim.ResetTrigger(Attack);
            _anim.ResetTrigger(Hit);
            _anim.SetBool(Dead, true);
        }

        SkinnedMeshRenderer _body;

        // After the animation and the foot IK: knees over the feet (only while the body is on screen; culled bodies are not posed).
        void LateUpdate()
        {
            if (_dead || _suspended || _anim == null) return;
            if (_body == null) _body = GetComponentInChildren<SkinnedMeshRenderer>();
            if (_body != null && !_body.isVisible) return;
            KneeAlign.Apply(_anim, transform);
        }

        void OnAnimatorIK(int layerIndex)
        {
            var player = PlayerHealth.Instance;
            bool want = !_dead && !_suspended && player != null && player.IsAlive
                && (player.transform.position - transform.position).sqrMagnitude < lookRange * lookRange;
            _lookWeight = Mathf.MoveTowards(_lookWeight, want ? 1f : 0f, Time.deltaTime * 3f);
            if (_lookWeight <= 0.001f) { _anim.SetLookAtWeight(0f); return; }
            _anim.SetLookAtWeight(_lookWeight, 0.15f, 0.85f, 0f, 0.6f);
            _anim.SetLookAtPosition(player.transform.position + Vector3.up * lookEyeHeight);
        }
    }
}
