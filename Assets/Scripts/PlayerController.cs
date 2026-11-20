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

        [Tooltip("Camera yaw source; movement is relative to where the camera looks.")]
        [SerializeField] Transform cameraYaw;

        CharacterController _controller;
        Vector3 _planarVelocity;
        float _verticalVelocity;
        float _lastGroundedTime = -1f;
        float _lastJumpPressedTime = -1f;
        bool _wasGrounded = true;
        float _yawVelocity;

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
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

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

            var flags = _controller.Move((_planarVelocity + Vector3.up * _verticalVelocity) * dt);
            var actual = _controller.velocity;
            GroundVelocity = new Vector3(actual.x, 0f, actual.z);
            IsGrounded = (flags & CollisionFlags.Below) != 0;
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;

            if (IsGrounded && !_wasGrounded) Landed?.Invoke(Mathf.Max(0f, fallSpeed));
            _wasGrounded = IsGrounded;
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
