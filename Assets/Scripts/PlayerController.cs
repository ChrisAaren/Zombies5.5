using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Zombies55
{
    // Camera-relative run and jump on a CharacterController.
    // Keyboard: WASD / arrows to move, Shift to sprint, Space to jump.
    // Gamepad: left stick to move, left stick press to sprint (right trigger fires), South button to jump.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour, ICharacterMover
    {
        [Header("Movement")]
        [SerializeField] float walkSpeed = 5f;
        [SerializeField] float sprintSpeed = 8.5f;
        [Tooltip("m/s^2 while speeding up (0 to run speed in about 0.28 s).")]
        [SerializeField] float acceleration = 18f;
        [Tooltip("m/s^2 while slowing down or reversing (run speed to 0 in about 0.19 s).")]
        [SerializeField] float deceleration = 26f;
        [Tooltip("Max turn rate in degrees per second while facing the camera (aiming / firing).")]
        [SerializeField] float turnSpeed = 900f;
        [Tooltip("SmoothDampAngle time (s) for turning towards the move direction.")]
        [SerializeField] float turnSmoothTime = 0.07f;
        [Tooltip("SmoothDampAngle time (s) for facing the camera while aiming: snappy, so the gun follows the mouse.")]
        [SerializeField] float aimTurnSmoothTime = 0.03f;

        [Header("Combat")]
        [SerializeField] float aimSpeedScale = 0.40f;
        [SerializeField] float combatStanceSeconds = 0.5f;

        [Header("Jump")]
        [SerializeField] float jumpHeight = 1.5f;
        [SerializeField] float gravity = -28f;
        [SerializeField] float coyoteTime = 0.12f;
        [SerializeField] float jumpBuffer = 0.12f;

        [Header("Ground following")]
        [Tooltip("The character model. Its height is smoothed over a short time, so curbs and small steps glide instead of popping.")]
        [SerializeField] Transform visual;
        [SerializeField] float visualSmoothTime = 0.12f;
        [Tooltip("Walking off a ledge no higher than this (m) keeps the player on the ground instead of falling. 0 = the controller's step offset.")]
        [SerializeField] float snapDistance = 0f;
        [Tooltip("Landing is only reported after at least this long in the air (s); stepping off a curb is not a landing.")]
        [SerializeField] float minAirTimeForLanding = 0.1f;

        [Tooltip("Camera yaw source; movement is relative to where the camera looks.")]
        [SerializeField] Transform cameraYaw;

        CharacterController _controller;
        Vector3 _planarVelocity;
        float _verticalVelocity;
        float _lastGroundedTime = -1f;
        float _lastJumpPressedTime = -1f;
        bool _wasGrounded = true;
        float _yawVelocity;
        float _airTime;
        Vector3 _visualRest;
        float _visualOffset, _visualOffsetVel;

        public Vector3 Velocity => _planarVelocity + Vector3.up * _verticalVelocity;
        public float PlanarSpeed => _planarVelocity.magnitude;
        // Speed the body really moves at, from the last Move (zero against a wall), in contrast to the intended Velocity.
        public Vector3 GroundVelocity { get; private set; }
        // Change of the intended planar velocity (m/s^2) and the turn rate (deg/s, positive = clockwise from above) this frame.
        public Vector3 PlanarAcceleration { get; private set; }
        public float YawRate { get; private set; }
        public float AimSpeed => walkSpeed * aimSpeedScale;
        public float MaxSpeed => sprintSpeed;
        public bool IsGrounded { get; private set; } = true;
        public bool IsSprinting { get; private set; }

        public event Action Jumped;
        public event Action<float> Landed; // impact speed

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (visual != null) _visualRest = visual.localPosition;
        }

        const int SnapMask = ~((1 << GameLayers.Player) | (1 << GameLayers.Enemy) | (1 << GameLayers.IgnoreRaycast) | (1 << GameLayers.Ragdoll));

        // After a Move that lost the ground (walking off a curb or over a seam), look for ground within the step offset
        // below the feet and drop onto it. Returns the distance dropped, or -1 when there is no walkable ground.
        float FindGroundBelow()
        {
            float reach = snapDistance > 0f ? snapDistance : _controller.stepOffset;
            float r = _controller.radius;
            const float lift = 0.05f;
            Vector3 origin = transform.position + _controller.center - Vector3.up * (_controller.height * 0.5f - r) + Vector3.up * lift;
            if (!Physics.SphereCast(origin, r * 0.9f, Vector3.down, out var hit, reach + lift + 0.02f, SnapMask, QueryTriggerInteraction.Ignore))
                return -1f;
            if (hit.normal.y < Mathf.Cos(_controller.slopeLimit * Mathf.Deg2Rad)) return -1f;
            return Mathf.Max(0f, hit.distance - lift);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // Knocked down by a blast: the ragdoll owns the body (the controller is switched off, so no Move).
            if (PlayerRagdoll.IsDown) return;

            bool canAct = GameLoop.Instance == null || GameLoop.Instance.State == RunState.Running;
            Vector2 move = canAct ? ReadMove() : Vector2.zero;
            bool sprint = canAct && ReadSprint();
            if (canAct && ReadJumpPressed()) _lastJumpPressedTime = Time.time;

            // Aiming or recently firing: face the camera yaw and strafe. Aiming cancels sprint.
            var weapons = PlayerWeapons.Instance;
            bool aiming = canAct && weapons != null && weapons.IsAiming;
            bool combat = aiming || (canAct && weapons != null && Time.time - weapons.LastFireTime < combatStanceSeconds);
            if (aiming) sprint = false;

            // Camera-relative wish direction.
            float yaw = cameraYaw != null ? cameraYaw.eulerAngles.y : transform.eulerAngles.y;
            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y);
            wish = Vector3.ClampMagnitude(wish, 1f);

            IsSprinting = sprint && wish.sqrMagnitude > 0.01f;
            float targetSpeed = sprint ? sprintSpeed : walkSpeed;
            if (aiming) targetSpeed *= aimSpeedScale;
            // Inertia: gentle build-up, quicker braking (also used when the wish direction reverses).
            Vector3 wishVelocity = wish * targetSpeed;
            float rate = wishVelocity.sqrMagnitude > _planarVelocity.sqrMagnitude + 1e-4f ? acceleration : deceleration;
            Vector3 before = _planarVelocity;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, wishVelocity, rate * dt);
            PlanarAcceleration = (_planarVelocity - before) / dt;

            float currentYaw = transform.eulerAngles.y;
            float yawBefore = currentYaw;
            if (combat)
            {
                float newYaw = Mathf.SmoothDampAngle(currentYaw, yaw, ref _yawVelocity, aimTurnSmoothTime, turnSpeed, dt);
                transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
            }
            else if (wish.sqrMagnitude > 0.01f)
            {
                float goal = Quaternion.LookRotation(wish, Vector3.up).eulerAngles.y;
                float newYaw = Mathf.SmoothDampAngle(currentYaw, goal, ref _yawVelocity, turnSmoothTime, Mathf.Infinity, dt);
                transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
            }
            else _yawVelocity = 0f;
            YawRate = Mathf.DeltaAngle(yawBefore, transform.eulerAngles.y) / dt;

            // Jump with coyote time and input buffering.
            if (IsGrounded) _lastGroundedTime = Time.time;
            bool jumpBuffered = Time.time - _lastJumpPressedTime <= jumpBuffer;
            bool coyote = Time.time - _lastGroundedTime <= coyoteTime;
            if (jumpBuffered && coyote && _verticalVelocity <= 0.1f)
            {
                _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
                _lastJumpPressedTime = -1f;
                _lastGroundedTime = -1f;
                Jumped?.Invoke();
            }

            if (IsGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            float fallSpeed = -_verticalVelocity;
            _verticalVelocity += gravity * dt;

            float yBefore = transform.position.y;
            bool jumpedThisFrame = _verticalVelocity > 0.1f;
            var flags = _controller.Move((_planarVelocity + Vector3.up * _verticalVelocity) * dt);
            var actual = _controller.velocity;
            GroundVelocity = new Vector3(actual.x, 0f, actual.z);
            IsGrounded = (flags & CollisionFlags.Below) != 0;

            // Ground snapping: a one-frame loss of ground (curb edge, seam, tiny slope) must not become a fall and a landing.
            if (!IsGrounded && _wasGrounded && !jumpedThisFrame)
            {
                float drop = FindGroundBelow();
                if (drop >= 0f)
                {
                    if (drop > 0.0005f) _controller.Move(Vector3.down * drop);
                    IsGrounded = true;
                    _verticalVelocity = -2f;
                }
            }
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;

            if (IsGrounded) { if (!_wasGrounded && _airTime >= minAirTimeForLanding) Landed?.Invoke(Mathf.Max(0f, fallSpeed)); _airTime = 0f; }
            else _airTime += dt;
            _wasGrounded = IsGrounded;

            SmoothVisual(yBefore, jumpedThisFrame, dt);
        }

        // After a knockdown: no leftover run speed or fall speed.
        public void ResetMotion()
        {
            _planarVelocity = Vector3.zero;
            _verticalVelocity = -2f;
            _yawVelocity = 0f;
            IsGrounded = true;
            _wasGrounded = true;
            _airTime = 0f;
            GroundVelocity = Vector3.zero;
            PlanarAcceleration = Vector3.zero;
            if (visual != null) { visual.localPosition = _visualRest; _visualOffset = 0f; _visualOffsetVel = 0f; }
        }

        // Keep the capsule exact but let the model glide over steps: the height jump of a grounded frame is absorbed
        // into an offset that eases back to zero over visualSmoothTime.
        void SmoothVisual(float yBefore, bool jumped, float dt)
        {
            if (visual == null) return;
            float dy = transform.position.y - yBefore;
            if (IsGrounded && !jumped && Mathf.Abs(dy) > 0.004f && Mathf.Abs(dy) < 0.5f) _visualOffset -= dy;
            _visualOffset = Mathf.SmoothDamp(_visualOffset, 0f, ref _visualOffsetVel, visualSmoothTime, Mathf.Infinity, dt);
            if (Mathf.Abs(_visualOffset) < 0.0002f) { _visualOffset = 0f; _visualOffsetVel = 0f; }
            visual.localPosition = _visualRest + Vector3.up * _visualOffset;
        }

        // Set by BenchmarkRunner: drives the player along a fixed path instead of reading input.
        public static bool ScriptedInput;
        public static Vector2 ScriptedMove;
        public static bool ScriptedSprint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ScriptedInput = false; ScriptedMove = default; ScriptedSprint = false; }

        static Vector2 ReadMove()
        {
            if (ScriptedInput) return ScriptedMove;
            Vector2 v = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
            }
            var pad = Gamepad.current;
            if (pad != null) v += pad.leftStick.ReadValue();
            return Vector2.ClampMagnitude(v, 1f);
        }

        static bool ReadSprint()
        {
            if (ScriptedInput) return ScriptedSprint;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed))
                || (pad != null && pad.leftStickButton.isPressed);
        }

        static bool ReadJumpPressed()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return (kb != null && kb.spaceKey.wasPressedThisFrame)
                || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        }
    }
}
