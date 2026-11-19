using Unity.Cinemachine;
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
        // Sensitivity multipliers, invert Y and the aim scale come from GameSettings.
        [SerializeField] float mouseSensitivity = 0.12f;  // degrees per pixel at GameSettings.MouseSensitivity 1
        [SerializeField] float stickSpeed = 160f;          // degrees per second at GameSettings.ControllerSensitivity 1
        [SerializeField] float minPitch = -5f;
        [SerializeField] float maxPitch = 55f;
        [SerializeField] float startPitch = 18f;

        [Header("Idle orbit (title / end screens)")]
        [SerializeField] float idleOrbitSpeed = 5f;
        [SerializeField] float idlePitch = 24f;

        [Header("Aim (SetAiming)")]
        [SerializeField] CinemachineCamera vcam;          // found at Start when empty
        [SerializeField] float aimFov = 38f;
        [SerializeField] float aimShoulderX = 0.85f;
        [SerializeField] float aimDistance = 3.6f;
        [SerializeField] float aimBlendSharpness = 10f;

        [Header("Recoil")]
        [SerializeField] float recoilRecoverTime = 0.16f;
        [SerializeField] float recoilMax = 12f;

        float _yaw;
        float _pitch;
        int _ignoreMouseFrames;
        CinemachineThirdPersonFollow _follow;
        float _baseFov, _baseDistance;
        Vector3 _baseShoulder;
        float _aim, _aimTarget;
        float _recoilPitch, _recoilYaw, _recoilVelPitch, _recoilVelYaw;

        public bool IsAiming => _aimTarget > 0.5f;
        public float AimBlend => _aim;

        // Zoom and tighten the shoulder framing; look sensitivity drops while aimed.
        public void SetAiming(bool aiming) => _aimTarget = aiming ? 1f : 0f;

        // Camera kick that recovers. Pitch is degrees upward, yaw degrees to the right.
        public void AddRecoil(float pitch, float yaw)
        {
            _recoilPitch = Mathf.Clamp(_recoilPitch - pitch, -recoilMax, recoilMax);
            _recoilYaw = Mathf.Clamp(_recoilYaw + yaw, -recoilMax, recoilMax);
        }

        void FindCamera()
        {
            if (vcam == null) vcam = FindAnyObjectByType<CinemachineCamera>();
            if (vcam == null) return;
            _follow = vcam.GetComponent<CinemachineThirdPersonFollow>();
            _baseFov = vcam.Lens.FieldOfView;
            if (_follow != null)
            {
                _baseShoulder = _follow.ShoulderOffset;
                _baseDistance = _follow.CameraDistance;
            }
        }

        void ApplyAim(float dt)
        {
            _aim += (_aimTarget - _aim) * (1f - Mathf.Exp(-aimBlendSharpness * dt));
            if (vcam == null) return;
            var lens = vcam.Lens;
            lens.FieldOfView = Mathf.Lerp(_baseFov, aimFov, _aim);
            vcam.Lens = lens;
            if (_follow != null)
            {
                var shoulder = _baseShoulder;
                shoulder.x = Mathf.Lerp(_baseShoulder.x, aimShoulderX, _aim);
                _follow.ShoulderOffset = shoulder;
                _follow.CameraDistance = Mathf.Lerp(_baseDistance, aimDistance, _aim);
            }
        }

        void Start()
        {
            _yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;
            _pitch = startPitch;
            FindCamera();
            Snap();
        }

        void LateUpdate()
        {
            var state = GameLoop.Instance != null ? GameLoop.Instance.State : RunState.Running;
            float dt = Time.unscaledDeltaTime;
            if (state != RunState.Running) _aimTarget = 0f;
            ApplyAim(dt);
            _recoilPitch = Mathf.SmoothDamp(_recoilPitch, 0f, ref _recoilVelPitch, recoilRecoverTime, float.PositiveInfinity, dt);
            _recoilYaw = Mathf.SmoothDamp(_recoilYaw, 0f, ref _recoilVelYaw, recoilRecoverTime, float.PositiveInfinity, dt);
            float sens = Mathf.Lerp(1f, GameSettings.AimSensitivity, _aim);

            if (state == RunState.Running)
            {
                Vector2 look = Vector2.zero;
                var mouse = Mouse.current;
                // Locking the cursor produces a one-off delta spike; skip the first frames after it.
                if (mouse != null && _ignoreMouseFrames == 0) look += mouse.delta.ReadValue() * (mouseSensitivity * GameSettings.MouseSensitivity * sens);
                var pad = Gamepad.current;
                if (pad != null) look += pad.rightStick.ReadValue() * (stickSpeed * GameSettings.ControllerSensitivity * sens * Time.unscaledDeltaTime);
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - (GameSettings.InvertY ? -look.y : look.y), minPitch, maxPitch);
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
            transform.rotation = Quaternion.Euler(_pitch + _recoilPitch, _yaw + _recoilYaw, 0f);
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
