using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Zombies55
{
    // Camera-relative run and jump on a CharacterController.
    // Keyboard: WASD / arrows to move, Shift to sprint, Space to jump.
    // Gamepad: left stick to move, left stick press or right trigger to sprint, South button to jump.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour, ICharacterMover
    {
        [Header("Movement")]
        [SerializeField] float walkSpeed = 5f;
        [SerializeField] float sprintSpeed = 8.5f;
        [SerializeField] float acceleration = 40f;
        [SerializeField] float turnSpeed = 900f;

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

        public Vector3 Velocity => _planarVelocity + Vector3.up * _verticalVelocity;
        public float PlanarSpeed => _planarVelocity.magnitude;
        public float MaxSpeed => sprintSpeed;
        public bool IsGrounded { get; private set; } = true;

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

            // Camera-relative wish direction.
            float yaw = cameraYaw != null ? cameraYaw.eulerAngles.y : transform.eulerAngles.y;
            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y);
            wish = Vector3.ClampMagnitude(wish, 1f);

            float targetSpeed = sprint ? sprintSpeed : walkSpeed;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, wish * targetSpeed, acceleration * dt);

            if (wish.sqrMagnitude > 0.01f)
            {
                var face = Quaternion.LookRotation(wish, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, turnSpeed * dt);
            }

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
            IsGrounded = (flags & CollisionFlags.Below) != 0;
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;

            if (IsGrounded && !_wasGrounded) Landed?.Invoke(Mathf.Max(0f, fallSpeed));
            _wasGrounded = IsGrounded;
        }

        static Vector2 ReadMove()
        {
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
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            return (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed))
                || (pad != null && (pad.leftStickButton.isPressed || pad.rightTrigger.isPressed));
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
