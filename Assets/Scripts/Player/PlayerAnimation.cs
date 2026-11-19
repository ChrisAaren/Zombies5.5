using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    // Drives the player's Animator (locomotion blend, jump / fall / land, upper-body weapon layer, death), holds the gun
    // in the right hand, puts the left hand on the foregrip and pitches the spine so the muzzle follows the crosshair.
    // Lives on the same object as the Animator (the MPFB_Courier instance).
    //
    // Palm frame: the rest-pose orientation of a hand's MiddleProximal bone. Y runs along the fingers, Z is the palm normal
    // (out of the palm, towards what the hand holds), X is the curl axis (Y x Z). The gun mount is a child of the right hand
    // in that frame, and the foregrip nodes on the guns are palm frames of the left hand.
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(100)]
    public class PlayerAnimation : MonoBehaviour
    {
        const int UpperLayer = 1;

        // Distance from the wrist to the palm centre (the hand is about 0.115 m to the knuckles).
        public const float PalmCentre = 0.063f;
        // Closest point along the camera ray the muzzle is pointed at.
        const float MinAimDistance = 10f;
        // Share of the wrist roll that goes to the forearm; the hand takes the rest.
        const float ForearmRollShare = 0.5f;

        [Tooltip("Parent of the gun models: a child of the RightHand bone, at the palm centre, in the palm frame.")]
        [SerializeField] Transform gunMount;
        [SerializeField] float moveDamp = 0.08f;
        [SerializeField] float combatStanceSeconds = 0.5f;
        [Tooltip("Upper-layer weight for the pistol when not aiming (relaxed hold).")]
        [SerializeField, Range(0f, 1f)] float pistolRelaxedWeight = 0f;
        [SerializeField] float maxAimCorrection = 75f;
        [SerializeField] float kickMeters = 0.03f;
        [Header("Grip fingers")]
        [Tooltip("The clips' finger poses do not close the hand, so the four long fingers of a hand holding a gun are curled" +
                 " here, about the finger bones' X axis (the curl axis, positive towards the palm on both hands).")]
        [SerializeField] float curlProximal = 50f;
        [SerializeField] float curlIntermediate = 70f;
        [SerializeField] float curlDistal = 45f;
        [SerializeField] float curlSign = 1f;
        [SerializeField, Range(0f, 1f)] float leftHandCurl = 0.8f;
        [Tooltip("Left-hand finger curl in degrees (proximal, intermediate, distal) on a handguard, wrapped round it.")]
        [SerializeField] Vector3 supportCurl = new Vector3(62f, 80f, 55f);
        [SerializeField] Vector3 supportThumb = new Vector3(40f, 40f, 40f);
        [SerializeField] Vector3 thumbAxis = new Vector3(0f, 0f, -1f);
        [Header("Shouldering")]
        [Tooltip("Shoulder pocket, from the shoulder joint: metres forward, inward (towards the neck) and down.")]
        [SerializeField] Vector3 pocket = new Vector3(0.10f, 0.06f, 0.0f);
        [Tooltip("Shooting stance: the chest turns this many degrees to the right of the aim (left shoulder forward) so the support" +
                 " arm reaches the handguard; the head turns back onto the target and the left collarbone reaches forward.")]
        [SerializeField] float bladeYaw = 28f;
        [SerializeField] float clavicleReach = 10f;
        [Tooltip("Muzzle pitch below the horizon, degrees, when a long gun is carried at low ready.")]
        [SerializeField] float lowReadyPitch = 40f;

        Animator _anim;
        PlayerController _player;
        PlayerHealth _health;
        PlayerWeapons _weapons;
        Transform _root, _spine, _chest;
        Transform _lUpper, _lLower, _lHand, _rUpper, _rLower, _rHand, _lThumb0, _lThumb1, _lThumb2, _lClavicle, _neck, _head;
        Vector3 _pocketLocal;                   // shoulder pocket in the chest bone's frame (rest pose)
        Quaternion _palmRight, _palmLeft;
        Vector3 _chestUpLocal;                  // the torso's up in the chest bone's frame (rest pose)
        readonly Dictionary<string, GameObject> _guns = new Dictionary<string, GameObject>();
        GameObject _gun;
        Transform _muzzle, _foregrip, _stock;
        Vector3 _mountRest;
        struct FingerBone { public Transform Bone; public Quaternion Rest; public float Curl; }
        FingerBone[] _rightFingers, _leftFingers;
        float _gripBlend;
        Quaternion[] _thumbRest;

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
            _lUpper = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _lLower = _anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _lHand = _anim.GetBoneTransform(HumanBodyBones.LeftHand);
            _rUpper = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lClavicle = _anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
            _neck = _anim.GetBoneTransform(HumanBodyBones.Neck);
            _head = _anim.GetBoneTransform(HumanBodyBones.Head);
            _rLower = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _lThumb0 = _anim.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
            _lThumb1 = _anim.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate);
            _lThumb2 = _anim.GetBoneTransform(HumanBodyBones.LeftThumbDistal);
            _thumbRest = new[] { _lThumb0.localRotation, _lThumb1.localRotation, _lThumb2.localRotation };
            _rHand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            // Awake runs before the Animator's first evaluation, so the bones are still in the T-pose.
            _palmRight = PalmFrame(_rHand, _anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal));
            _palmLeft = PalmFrame(_lHand, _anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal));
            _chestUpLocal = Quaternion.Inverse(_chest.rotation) * transform.up;
            _pocketLocal = _chest.InverseTransformPoint(_rUpper.position + transform.forward * pocket.x - transform.right * pocket.y - transform.up * pocket.z);
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

        public static Quaternion PalmFrame(Transform hand, Transform middleProximal)
        {
            return Quaternion.Inverse(hand.rotation) * middleProximal.rotation;
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
            _stock = null;
            foreach (var kv in _guns) if (kv.Value != null) kv.Value.SetActive(false);
            if (def == null || !_guns.TryGetValue(def.ModelName, out _gun) || _gun == null) return;
            _gun.SetActive(true);
            foreach (var t in _gun.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Muzzle") _muzzle = t;
                else if (t.name == "Foregrip") _foregrip = t;
                else if (t.name == "Stock") _stock = t;
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
            bool ik = !reloading && !_dead && _foregrip != null;
            _ikWeight = Mathf.MoveTowards(_ikWeight, ik ? 1f : 0f, dt * 6f);
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 9f);
        }

        // After the animation, in this order: roll the wrists so the grip stands upright, tilt spine and chest so the muzzle
        // points at the camera's aim point, put the left hand on the foregrip, curl the fingers.
        void LateUpdate()
        {
            if (gunMount != null) gunMount.localPosition = _mountRest + new Vector3(0f, -_kick * kickMeters, 0f);
            _gripBlend = Mathf.MoveTowards(_gripBlend, _gun != null && !_dead ? 1f : 0f, Time.deltaTime * 6f);
            float roll = _gun != null && !_dead ? _upperWeight : 0f;
            bool aim = _aimBlend > 0.001f && _muzzle != null && _spine != null && _chest != null;
            Vector3 aimPoint = aim ? FindAimPoint() : Vector3.zero;
            float ik = _ikWeight * _upperWeight;

            if (roll > 0.001f) RollWrists(roll);
            if (aim)
            {
                // Each pass re-measures the muzzle, so the rotations converge on the aim point.
                for (int i = 0; i < 4; i++)
                {
                    Rotate(_spine, aimPoint, i == 0 ? 0.4f : 0.2f);
                    Rotate(_chest, aimPoint, i == 0 ? 0.75f : 0.5f);
                    if (roll > 0.001f) RollWrists(roll);
                }
                Rotate(_chest, aimPoint, 1f);
            }
            // Long guns: the butt goes into the shoulder pocket and the right arm follows the gun, so the stock never sits in
            // the arm or the chest. The gun turns about the butt until the muzzle is on the aim point.
            if (_stock != null && _gun != null && !_dead)
            {
                float w = _upperWeight;
                Blade(w);
                if (w > 0.001f)
                {
                    // Low ready: muzzle down in front of the chest; blends into the real aim point as the aim comes up.
                    Vector3 lowDir = Quaternion.AngleAxis(lowReadyPitch, _root.right) * _root.forward;
                    Vector3 low = _muzzle.position + lowDir * 10f;
                    ShoulderGun(w, aim ? Vector3.Lerp(low, aimPoint, _aimBlend) : low);
                }
            }
            if (ik > 0.001f) PlaceLeftHand(ik);
            CurlFingers(_rightFingers, _gripBlend);
            if (_foregrip != null && _stock != null) CurlSupportHand(ik);
            else CurlFingers(_leftFingers, Mathf.Max(ik * leftHandCurl, 0f));
        }

        void CurlSupportHand(float weight)
        {
            if (weight <= 0.001f) return;
            for (int i = 0; i < _leftFingers.Length; i++)
            {
                var f = _leftFingers[i];
                float deg = (i % 3 == 0 ? supportCurl.x : i % 3 == 1 ? supportCurl.y : supportCurl.z) * curlSign;
                f.Bone.localRotation = Quaternion.Slerp(f.Bone.localRotation, f.Rest * Quaternion.AngleAxis(deg, Vector3.right), weight);
            }
            Transform[] thumb = { _lThumb0, _lThumb1, _lThumb2 };
            float[] deg3 = { supportThumb.x, supportThumb.y, supportThumb.z };
            for (int i = 0; i < 3; i++)
                if (thumb[i] != null)
                    thumb[i].localRotation = Quaternion.Slerp(thumb[i].localRotation, _thumbRest[i] * Quaternion.AngleAxis(deg3[i], thumbAxis), weight);
        }

        // Chest turned to the right of the aim, head back on it, left collarbone forward (about the torso's up).
        void Blade(float weight)
        {
            Vector3 up = _root.up;
            _chest.rotation = Quaternion.AngleAxis(bladeYaw * weight, up) * _chest.rotation;
            if (_lClavicle != null) _lClavicle.rotation = Quaternion.AngleAxis(clavicleReach * weight, up) * _lClavicle.rotation;
            if (_neck != null) _neck.rotation = Quaternion.AngleAxis(-bladeYaw * 0.45f * weight, up) * _neck.rotation;
            if (_head != null) _head.rotation = Quaternion.AngleAxis(-bladeYaw * 0.45f * weight, up) * _head.rotation;
        }

        void ShoulderGun(float weight, Vector3 aimPoint)
        {
            Transform g = _gun.transform;
            Vector3 pocketWorld = _chest.TransformPoint(_pocketLocal);
            Quaternion rot = g.rotation;
            Vector3 stockLocal = g.InverseTransformPoint(_stock.position);
            Vector3 muzzleLocal = g.InverseTransformPoint(_muzzle.position);
            Vector3 muzzleFwdLocal = g.InverseTransformDirection(_muzzle.forward);
            // Turn the gun about the butt until the muzzle points at the aim point (the muzzle moves as it turns).
            for (int i = 0; i < 4; i++)
            {
                Vector3 muzzleWorld = pocketWorld + rot * (muzzleLocal - stockLocal);
                Vector3 want = aimPoint - muzzleWorld;
                if (want.sqrMagnitude < 0.04f) break;
                rot = Quaternion.FromToRotation(rot * muzzleFwdLocal, want.normalized) * rot;
            }
            Vector3 gunPos = pocketWorld - rot * stockLocal;
            Quaternion rel = Quaternion.Inverse(_rHand.rotation) * g.rotation;
            Vector3 relPos = _rHand.InverseTransformPoint(g.position);
            Quaternion handRot = rot * Quaternion.Inverse(rel);
            Vector3 handPos = gunPos - handRot * relPos;
            var upperRot = _rUpper.localRotation;
            var lowerRot = _rLower.localRotation;
            var handLocal = _rHand.localRotation;
            SolveTwoBone(_rUpper, _rLower, _rHand, handPos, _rLower.position);
            _rHand.rotation = handRot;
            _rUpper.localRotation = Quaternion.Slerp(upperRot, _rUpper.localRotation, weight);
            _rLower.localRotation = Quaternion.Slerp(lowerRot, _rLower.localRotation, weight);
            _rHand.localRotation = Quaternion.Slerp(handLocal, _rHand.localRotation, weight);
        }

        void RollWrists(float weight)
        {
            // "Up" is the torso's, not the world's: when the chest pitches to bring the muzzle onto the target, the grip pitches
            // with it instead of the roll fighting the aim.
            Vector3 up = _chest.rotation * _chestUpLocal;
            Roll(_rLower, _rHand, _palmRight, -1f, weight, up);
            // The left hand is rolled only while it is not about to be placed on the foregrip.
            if (_foregrip == null) Roll(_lLower, _lHand, _palmLeft, 1f, weight, up);
        }

        // Rolls the hand about its finger axis until the thumb side of the fist points up (the torso's up), so the gun stands upright in the
        // palm (the clips hold the hands palm-down); the finger direction, and with it the barrel, does not move. The forearm
        // takes part of the twist so the wrist is not wrung. The thumb side is -X of the palm frame on the right hand, +X on
        // the left.
        void Roll(Transform lower, Transform hand, Quaternion palm, float thumbSign, float weight, Vector3 up)
        {
            Quaternion palmWorld = hand.rotation * palm;
            Vector3 fingers = palmWorld * Vector3.up;
            Vector3 thumb = thumbSign * (palmWorld * Vector3.right);
            Vector3 a = Vector3.ProjectOnPlane(thumb, fingers);
            Vector3 b = Vector3.ProjectOnPlane(up, fingers);
            if (a.sqrMagnitude < 0.04f || b.sqrMagnitude < 0.04f) return;
            float angle = Mathf.Clamp(Vector3.SignedAngle(a, b, fingers), -130f, 130f) * weight;
            Quaternion target = Quaternion.AngleAxis(angle, fingers) * hand.rotation;
            Vector3 axis = hand.position - lower.position;
            if (axis.sqrMagnitude > 1e-6f)
                lower.rotation = Quaternion.AngleAxis(angle * ForearmRollShare, axis.normalized) * lower.rotation;
            hand.rotation = target;
        }

        // Left wrist to the foregrip: the foregrip node is the palm frame of the left hand and its position is the palm
        // centre. Two-bone solve for the arm (the elbow keeps its animated side), then the hand takes the node's orientation.
        void PlaceLeftHand(float weight)
        {
            Quaternion handGoal = _foregrip.rotation * Quaternion.Inverse(_palmLeft);
            Vector3 wristGoal = _foregrip.position - _foregrip.up * PalmCentre;
            var upperRot = _lUpper.localRotation;
            var lowerRot = _lLower.localRotation;
            var handRot = _lHand.localRotation;
            SolveTwoBone(_lUpper, _lLower, _lHand, wristGoal, _lLower.position);
            _lHand.rotation = handGoal;
            _lUpper.localRotation = Quaternion.Slerp(upperRot, _lUpper.localRotation, weight);
            _lLower.localRotation = Quaternion.Slerp(lowerRot, _lLower.localRotation, weight);
            _lHand.localRotation = Quaternion.Slerp(handRot, _lHand.localRotation, weight);
        }

        static void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float l1 = (b - a).magnitude, l2 = (c - b).magnitude;
            Vector3 toTarget = target - a;
            float dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = toTarget.normalized;
            float x = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - x * x));
            Vector3 side = Vector3.ProjectOnPlane(pole - a, dir);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(b - a, dir);
            side.Normalize();
            Vector3 newElbow = a + dir * x + side * h;
            upper.rotation = Quaternion.FromToRotation(b - a, newElbow - a) * upper.rotation;
            Vector3 b2 = lower.position, c2 = end.position;
            lower.rotation = Quaternion.FromToRotation(c2 - b2, a + dir * dist - b2) * lower.rotation;
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
            // Never aim at something right in front of the gun (an enemy in the player's face): the spine would fold.
            if (Physics.Raycast(origin, fwd, out RaycastHit hit, 150f, GameLayers.ShotMask, QueryTriggerInteraction.Ignore))
                return origin + fwd * Mathf.Max(hit.distance, MinAimDistance);
            return origin + fwd * 60f;
        }
    }
}
