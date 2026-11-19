using UnityEngine;

namespace Zombies55
{
    // Procedural animation for the primitive-built courier: gait bob and roll, limb swing,
    // lean into acceleration and turns, jump stretch / landing squash, lagging backpack,
    // head look, and an idle breathe so the character never freezes.
    public class CharacterMotion : MonoBehaviour
    {
        [Header("Rig (pivots)")]
        [SerializeField] Transform visualRoot;   // at the feet: squash, stretch, lean
        [SerializeField] Transform body;         // hips: bob and roll
        [SerializeField] Transform legL, legR;
        [SerializeField] Transform armL, armR;
        [SerializeField] Transform head;
        [SerializeField] Transform backpack;

        [Header("Gait")]
        [SerializeField] float stepFrequency = 2.6f;  // Hz at full speed
        [SerializeField] float bobAmplitude = 0.07f;
        [SerializeField] float rollDegrees = 3f;
        [SerializeField] float legSwing = 38f;
        [SerializeField] float armSwing = 32f;

        [Header("Lean")]
        [SerializeField] float accelLean = 9f;
        [SerializeField] float turnBank = 6f;

        [Header("Squash & stretch")]
        [SerializeField] float jumpStretch = 1.12f;
        [SerializeField] float landSquash = 0.82f;
        [SerializeField] float squashRecover = 10f;

        [Header("Look (only when LookTarget is set; the player never sets it)")]
        [SerializeField] float lookRange = 4f;
        [SerializeField] float lookMaxYaw = 60f;
        [SerializeField] float lookDamping = 6f;

        [Header("Idle")]
        [SerializeField] float breatheAmplitude = 0.012f;
        [SerializeField] float breatheFrequency = 0.55f;

        ICharacterMover _mover;
        float _lookYaw;

        // Optional: the head turns toward this transform when it is within lookRange.
        public Transform LookTarget { get; set; }

        Vector3 _bodyRest, _visualScaleRest, _backpackRestEuler;
        Quaternion _armLRest, _armRRest, _legLRest, _legRRest, _headRest;
        Vector3 _lastVelocity;
        float _lastYaw;
        float _phase;
        float _gaitWeight;
        float _pitch, _bank;
        Vector3 _scaleOffset; // added to the rest scale, springs back to zero
        float _backpackLag;

        void Awake()
        {
            _mover = GetComponentInParent<ICharacterMover>();
            _bodyRest = body.localPosition;
            _visualScaleRest = visualRoot.localScale;
            _armLRest = armL.localRotation;
            _armRRest = armR.localRotation;
            _legLRest = legL.localRotation;
            _legRRest = legR.localRotation;
            _headRest = head.localRotation;
            if (backpack != null) _backpackRestEuler = backpack.localEulerAngles;
            _lastYaw = transform.eulerAngles.y;
        }

        void OnEnable()
        {
            if (_mover == null) return;
            _mover.Jumped += OnJumped;
            _mover.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (_mover == null) return;
            _mover.Jumped -= OnJumped;
            _mover.Landed -= OnLanded;
        }

        void OnJumped()
        {
            _scaleOffset = new Vector3(-(jumpStretch - 1f) * 0.5f, jumpStretch - 1f, -(jumpStretch - 1f) * 0.5f);
        }

        void OnLanded(float impact)
        {
            float k = Mathf.Clamp01(impact / 12f);
            float squash = Mathf.Lerp(1f, landSquash, 0.4f + 0.6f * k) - 1f;
            _scaleOffset = new Vector3(-squash * 0.6f, squash, -squash * 0.6f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _mover == null) return;

            Vector3 vel = _mover.Velocity;
            Vector3 planar = new Vector3(vel.x, 0f, vel.z);
            float speedN = Mathf.Clamp01(planar.magnitude / Mathf.Max(0.01f, _mover.MaxSpeed));
            bool grounded = _mover.IsGrounded;

            // Gait weight blends in with speed and out over ~0.3 s when stopping or airborne.
            float targetWeight = grounded ? Mathf.Clamp01(speedN * 1.6f) : 0f;
            _gaitWeight = Mathf.MoveTowards(_gaitWeight, targetWeight, dt / 0.3f);
            _phase += dt * stepFrequency * Mathf.Lerp(0.6f, 1f, speedN) * Mathf.PI * 2f;

            float s = Mathf.Sin(_phase);
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * bobAmplitude * _gaitWeight;
            float breathe = Mathf.Sin(Time.time * breatheFrequency * Mathf.PI * 2f) * breatheAmplitude * (1f - _gaitWeight);
            body.localPosition = _bodyRest + Vector3.up * (bob + breathe);

            float roll = Mathf.Sin(_phase * 0.5f) * rollDegrees * _gaitWeight;

            // Limbs: opposite pairs phase-shifted by pi. Airborne: tuck legs, raise arms.
            float air = grounded ? 0f : 1f;
            legL.localRotation = _legLRest * Quaternion.Euler(s * legSwing * _gaitWeight - 25f * air, 0f, 0f);
            legR.localRotation = _legRRest * Quaternion.Euler(-s * legSwing * _gaitWeight + 10f * air, 0f, 0f);
            armL.localRotation = _armLRest * Quaternion.Euler(-s * armSwing * _gaitWeight - 30f * air, 0f, -20f * air);
            armR.localRotation = _armRRest * Quaternion.Euler(s * armSwing * _gaitWeight - 30f * air, 0f, 20f * air);

            // Lean into acceleration (local forward) and bank into turns.
            Vector3 accel = (planar - new Vector3(_lastVelocity.x, 0f, _lastVelocity.z)) / dt;
            float forwardAccel = Vector3.Dot(accel, transform.forward);
            float yaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
            _lastYaw = yaw;
            _lastVelocity = vel;

            float targetPitch = Mathf.Clamp(forwardAccel / 40f, -1f, 1f) * accelLean + speedN * 4f;
            float targetBank = Mathf.Clamp(-yawRate / 360f, -1f, 1f) * turnBank * speedN;
            _pitch = Mathf.Lerp(_pitch, targetPitch, 1f - Mathf.Exp(-8f * dt));
            _bank = Mathf.Lerp(_bank, targetBank, 1f - Mathf.Exp(-8f * dt));
            visualRoot.localRotation = Quaternion.Euler(_pitch, 0f, _bank + roll);

            // Squash and stretch spring back to rest.
            _scaleOffset = Vector3.Lerp(_scaleOffset, Vector3.zero, 1f - Mathf.Exp(-squashRecover * dt));
            visualRoot.localScale = Vector3.Scale(_visualScaleRest, Vector3.one + _scaleOffset);

            // Head counter-rotates the bank a little and nods with the gait.
            Quaternion nod = Quaternion.Euler(-s * 2f * _gaitWeight - _pitch * 0.4f, 0f, -(_bank + roll) * 0.5f);
            if (LookTarget != null)
            {
                float targetYaw = 0f;
                Vector3 d = LookTarget.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude < lookRange * lookRange && d.sqrMagnitude > 0.01f)
                {
                    float a = Mathf.DeltaAngle(transform.eulerAngles.y, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                    if (Mathf.Abs(a) < 120f) targetYaw = Mathf.Clamp(a, -lookMaxYaw, lookMaxYaw);
                }
                _lookYaw = Mathf.Lerp(_lookYaw, targetYaw, 1f - Mathf.Exp(-lookDamping * dt));
                head.localRotation = _headRest * Quaternion.Euler(0f, _lookYaw, 0f) * nod;
            }
            else
            {
                head.localRotation = _headRest * nod;
            }

            // Backpack lags behind the body's motion (secondary motion).
            if (backpack != null)
            {
                float lagTarget = -_pitch * 1.2f + bob * 120f + air * -12f;
                _backpackLag = Mathf.Lerp(_backpackLag, lagTarget, 1f - Mathf.Exp(-6f * dt));
                backpack.localEulerAngles = _backpackRestEuler + new Vector3(_backpackLag, 0f, 0f);
            }
        }
    }
}
