using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    // Drives the player's Animator (locomotion blend, jump / fall / land, upper-body weapon layer, death), holds the gun
    // in the right hand, puts the left hand on the foregrip and pitches the spine so the muzzle follows the crosshair.
    // Lives on the same object as the Animator (the Char_Human instance) so OnAnimatorIK is called here.
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(100)]
    public class PlayerAnimation : MonoBehaviour
    {
        const int UpperLayer = 1;

        [Tooltip("Parent of the gun models: a child of the RightHand bone at the grip.")]
        [SerializeField] Transform gunMount;
        [SerializeField] float moveDamp = 0.08f;
        [SerializeField] float combatStanceSeconds = 0.5f;
        [Tooltip("Upper-layer weight for the pistol when not aiming (relaxed hold).")]
        [SerializeField, Range(0f, 1f)] float pistolRelaxedWeight = 0f;
        [SerializeField] float maxAimCorrection = 75f;
        [SerializeField] float kickMeters = 0.03f;
        [Header("Grip fingers")]
        [Tooltip("The clips' finger poses rotate about the bone's side axis (the fingers fan out instead of curling), so the" +
                 " four long fingers of a hand holding a gun are curled here, about the bone X axis.")]
        [SerializeField] float curlProximal = 50f;
        [SerializeField] float curlIntermediate = 70f;
        [SerializeField] float curlDistal = 45f;
        [SerializeField] float curlSign = 1f;
        [SerializeField, Range(0f, 1f)] float leftHandCurl = 0.6f;

        Animator _anim;
        PlayerController _player;
        PlayerHealth _health;
        PlayerWeapons _weapons;
        Transform _root, _spine, _chest;
        readonly Dictionary<string, GameObject> _guns = new Dictionary<string, GameObject>();
        GameObject _gun;
        Transform _muzzle, _foregrip;
        Vector3 _mountRest;
        struct FingerBone { public Transform Bone; public Quaternion Rest; public float Curl; }
        FingerBone[] _rightFingers, _leftFingers;
        float _gripBlend;

        float _airTime, _jumpedAt = -99f;
        float _upperWeight, _aimBlend, _ikWeight, _kick;
        bool _dead;
        int _moveX, _moveZ, _locoSpeed, _reloadSpeed, _airborne, _jump, _deadId, _weaponType, _aiming, _reloading;

        public Transform Muzzle => _muzzle;
        public Transform Gun => _gun != null ? _gun.transform : null;

        void Awake()
        {
            _anim = GetComponent<Animator>();
            _player = GetComponentInParent<PlayerController>();
            _root = _player != null ? _player.transform : transform;
            _spine = _anim.GetBoneTransform(HumanBodyBones.Spine);
            _chest = _anim.GetBoneTransform(HumanBodyBones.Chest);
            _moveX = Animator.StringToHash("MoveX");
            _moveZ = Animator.StringToHash("MoveZ");
            _locoSpeed = Animator.StringToHash("LocoSpeed");
            _reloadSpeed = Animator.StringToHash("ReloadSpeed");
            _airborne = Animator.StringToHash("Airborne");
            _jump = Animator.StringToHash("Jump");
            _deadId = Animator.StringToHash("Dead");
            _weaponType = Animator.StringToHash("WeaponType");
            _aiming = Animator.StringToHash("Aiming");
            _reloading = Animator.StringToHash("Reloading");
            _rightFingers = CacheFingers(true);
            _leftFingers = CacheFingers(false);
            CacheGuns();
        }

        FingerBone[] CacheFingers(bool right)
        {
            string side = right ? "Right" : "Left";
            var list = new List<FingerBone>();
            foreach (string finger in new[] { "Index", "Middle", "Ring", "Little" })
            {
                string[] parts = { "Proximal", "Intermediate", "Distal" };
                float[] curls = { curlProximal, curlIntermediate, curlDistal };
                for (int i = 0; i < 3; i++)
                {
                    var bone = _anim.GetBoneTransform((HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), side + finger + parts[i]));
                    if (bone != null) list.Add(new FingerBone { Bone = bone, Rest = bone.localRotation, Curl = curls[i] });
                }
            }
            return list.ToArray();
        }

        void CurlFingers(FingerBone[] fingers, float weight)
        {
            if (weight <= 0.001f) return;
            foreach (var f in fingers)
            {
                var pose = f.Rest * Quaternion.AngleAxis(curlSign * f.Curl, Vector3.right);
                f.Bone.localRotation = Quaternion.Slerp(f.Bone.localRotation, pose, weight);
            }
        }

        void CacheGuns()
        {
            _guns.Clear();
            if (gunMount == null) return;
            _mountRest = gunMount.localPosition;
            foreach (Transform child in gunMount) _guns[child.name] = child.gameObject;
        }

        void OnEnable()
        {
            _weapons = PlayerWeapons.Instance != null ? PlayerWeapons.Instance : GetComponentInParent<PlayerWeapons>();
            _health = PlayerHealth.Instance != null ? PlayerHealth.Instance : GetComponentInParent<PlayerHealth>();
            if (_player != null) _player.Jumped += OnJumped;
            if (_weapons != null) { _weapons.WeaponChanged += RefreshGun; _weapons.Fired += OnFired; }
            if (_health != null) _health.Died += OnDied;
            if (_guns.Count == 0) CacheGuns();
            RefreshGun();
        }

        void OnDisable()
        {
            if (_player != null) _player.Jumped -= OnJumped;
            if (_weapons != null) { _weapons.WeaponChanged -= RefreshGun; _weapons.Fired -= OnFired; }
            if (_health != null) _health.Died -= OnDied;
        }

        void OnJumped()
        {
            _jumpedAt = Time.time;
            _anim.SetTrigger(_jump);
        }

        void OnFired() => _kick = 1f;

        void OnDied()
        {
            _dead = true;
            _anim.SetBool(_deadId, true);
        }

        void RefreshGun()
        {
            var def = _weapons != null ? _weapons.Current : null;
            _gun = null;
            _muzzle = null;
            _foregrip = null;
            foreach (var kv in _guns) if (kv.Value != null) kv.Value.SetActive(false);
            if (def == null || !_guns.TryGetValue(def.ModelName, out _gun) || _gun == null) return;
            _gun.SetActive(true);
            foreach (var t in _gun.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Muzzle") _muzzle = t;
                else if (t.name == "Foregrip") _foregrip = t;
            }
        }

        bool PistolSet => _weapons == null || _weapons.Current == null || _weapons.Current.Id == WeaponCatalog.StarterId;

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _player == null) return;

            // Velocity in the character's own frame, m/s.
            Vector3 v = _player.Velocity;
            v.y = 0f;
            Vector3 local = _root.InverseTransformDirection(v);
            _anim.SetFloat(_moveX, local.x, moveDamp, dt);
            _anim.SetFloat(_moveZ, local.z, moveDamp, dt);

            // Clips are authored at fixed ground speeds; past the edge of the blend, play faster so the feet keep up.
            float speed = new Vector2(local.x, local.z).magnitude;
            float lateral = speed > 0.01f ? Mathf.Abs(local.x) / (Mathf.Abs(local.x) + Mathf.Abs(local.z)) : 0f;
            float forwardMax = local.z >= 0f ? 8f : 1.4f;
            float edge = Mathf.Lerp(forwardMax, 1.6f, lateral);
            _anim.SetFloat(_locoSpeed, Mathf.Clamp(speed / edge, 1f, 2f), 0.1f, dt);

            // Airborne: briefly off the ground (steps, curbs) does not count.
            if (_player.IsGrounded) _airTime = 0f; else _airTime += dt;
            bool air = _airTime > 0.1f || Time.time - _jumpedAt < 0.12f;
            _anim.SetBool(_airborne, air);

            bool pistol = PistolSet;
            bool aiming = _weapons != null && (_weapons.IsAiming || Time.time - _weapons.LastFireTime < combatStanceSeconds);
            bool reloading = _weapons != null && _weapons.IsReloading;
            _anim.SetInteger(_weaponType, pistol ? 0 : 1);
            _anim.SetBool(_aiming, aiming);
            _anim.SetBool(_reloading, reloading);
            if (reloading && _weapons.Current != null)
            {
                float clip = pistol ? 1.4f : 2.3f;
                _anim.SetFloat(_reloadSpeed, clip / Mathf.Max(0.2f, _weapons.Current.ReloadSeconds));
            }

            // Upper layer: full while armed and active, a relaxed pistol hold in between, off when dead.
            float target = _dead ? 0f : (pistol && !aiming && !reloading ? pistolRelaxedWeight : 1f);
            _upperWeight = Mathf.MoveTowards(_upperWeight, target, dt * 5f);
            _anim.SetLayerWeight(UpperLayer, _upperWeight);

            bool aimActive = aiming && !_dead && !reloading && _gun != null;
            _aimBlend = Mathf.MoveTowards(_aimBlend, aimActive ? 1f : 0f, dt * 8f);
            bool ik = !pistol && !reloading && !_dead && _foregrip != null;
            _ikWeight = Mathf.MoveTowards(_ikWeight, ik ? 1f : 0f, dt * 6f);
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 9f);
        }

        // Left hand on the foregrip (long guns); the clips already place it close, the IK closes the last centimetres.
        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != UpperLayer) return;
            float w = _ikWeight * _upperWeight;
            if (_foregrip == null || w <= 0.001f)
            {
                _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                _anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                return;
            }
            _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
            _anim.SetIKRotationWeight(AvatarIKGoal.LeftHand, w);
            _anim.SetIKPosition(AvatarIKGoal.LeftHand, _foregrip.position);
            _anim.SetIKRotation(AvatarIKGoal.LeftHand, _foregrip.rotation);
        }

        // After the animation: tilt spine and chest so the muzzle points at the camera's aim point.
        void LateUpdate()
        {
            if (gunMount != null) gunMount.localPosition = _mountRest + new Vector3(0f, -_kick * kickMeters, 0f);
            _gripBlend = Mathf.MoveTowards(_gripBlend, _gun != null && !_dead ? 1f : 0f, Time.deltaTime * 6f);
            CurlFingers(_rightFingers, _gripBlend);
            CurlFingers(_leftFingers, _ikWeight * leftHandCurl);
            if (_aimBlend <= 0.001f || _muzzle == null || _spine == null || _chest == null) return;

            Vector3 aimPoint = FindAimPoint();
            Rotate(_spine, aimPoint, 0.4f);
            Rotate(_chest, aimPoint, 0.75f);
            Rotate(_chest, aimPoint, 1f);
        }

        void Rotate(Transform bone, Vector3 aimPoint, float share)
        {
            Vector3 want = aimPoint - _muzzle.position;
            if (want.sqrMagnitude < 0.04f) return;
            var delta = Quaternion.FromToRotation(_muzzle.forward, want.normalized);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            angle = Mathf.Clamp(angle, -maxAimCorrection, maxAimCorrection) * share * _aimBlend;
            bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
        }

        Vector3 FindAimPoint()
        {
            var cam = Camera.main;
            if (cam == null) return _root.position + _root.forward * 40f + Vector3.up * 1.4f;
            Vector3 camPos = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            float along = Mathf.Max(0.5f, Vector3.Dot(_root.position + Vector3.up * 1.4f - camPos, fwd));
            Vector3 origin = camPos + fwd * along;
            if (Physics.Raycast(origin, fwd, out RaycastHit hit, 150f, GameLayers.ShotMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return origin + fwd * 60f;
        }
    }
}
