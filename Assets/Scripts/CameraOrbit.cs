using UnityEngine;
using UnityEngine.InputSystem;

namespace Zombies55
{
    // The Cinemachine camera follows this pivot (Third Person Follow inherits its rotation).
    // The pivot trails the player's position, takes yaw/pitch from mouse or right stick while
    // playing, and drifts in a slow orbit on the title and end screens.
    public class CameraOrbit : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float height = 1.5f;
        [SerializeField] float followSharpness = 14f;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.12f;  // degrees per pixel
        [SerializeField] float stickSpeed = 160f;          // degrees per second
        [SerializeField] float minPitch = -5f;
        [SerializeField] float maxPitch = 55f;
        [SerializeField] float startPitch = 18f;

        [Header("Idle orbit (title / end screens)")]
        [SerializeField] float idleOrbitSpeed = 5f;
        [SerializeField] float idlePitch = 24f;

        float _yaw;
        float _pitch;
        int _ignoreMouseFrames;

        void Start()
        {
            _yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;
            _pitch = startPitch;
            Snap();
        }

        void LateUpdate()
        {
            var state = GameLoop.Instance != null ? GameLoop.Instance.State : RunState.Running;

            if (state == RunState.Running)
            {
                Vector2 look = Vector2.zero;
                var mouse = Mouse.current;
                // Locking the cursor produces a one-off delta spike; skip the first frames after it.
                if (mouse != null && _ignoreMouseFrames == 0) look += mouse.delta.ReadValue() * mouseSensitivity;
                var pad = Gamepad.current;
                if (pad != null) look += pad.rightStick.ReadValue() * (stickSpeed * Time.unscaledDeltaTime);
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);
            }
            else if (state != RunState.Paused)
            {
                _yaw += idleOrbitSpeed * Time.unscaledDeltaTime;
                _pitch = Mathf.Lerp(_pitch, idlePitch, 1f - Mathf.Exp(-1.5f * Time.unscaledDeltaTime));
            }

            bool lockCursor = state == RunState.Running;
            if (lockCursor && Cursor.lockState != CursorLockMode.Locked) _ignoreMouseFrames = 3;
            else if (_ignoreMouseFrames > 0) _ignoreMouseFrames--;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;

            if (target != null)
            {
                Vector3 goal = target.position + Vector3.up * height;
                float t = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
                transform.position = Vector3.Lerp(transform.position, goal, t);
            }
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void Snap()
        {
            if (target != null) transform.position = target.position + Vector3.up * height;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
