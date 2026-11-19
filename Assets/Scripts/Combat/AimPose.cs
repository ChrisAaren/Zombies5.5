using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    // Two-handed gun hold. Runs after CharacterMotion's LateUpdate and drives only the local rotations of
    // ArmL / ArmR, so a skinned body bound to the same transforms keeps working.
    // Idle and running: a lowered ready pose. Aiming or firing: both arms point the gun at the camera ray's hit.
    [DefaultExecutionOrder(100)]
    public class AimPose : MonoBehaviour
    {
        [Header("Rig")]
        [SerializeField] Transform armL;
        [SerializeField] Transform armR;
        [Tooltip("Under ArmR, at the hand. Gun instances (named after WeaponDefinition.ModelName) are its children.")]
        [SerializeField] Transform holder;

        [Header("Pose")]
        [SerializeField] float readyAngle = 40f;       // degrees below horizontal, forward
        [SerializeField] float dipAngle = 68f;         // while reloading or swapping
        [SerializeField] float armLength = 0.55f;
        [SerializeField] float readyWeight = 0.88f;    // the rest stays CharacterMotion's swing
        [SerializeField] float holdAfterFire = 0.5f;
        [SerializeField] float kickDegrees = 5f;
        [SerializeField] float kickMeters = 0.05f;

        PlayerWeapons _weapons;
        readonly Dictionary<string, GameObject> _guns = new Dictionary<string, GameObject>();
        GameObject _gun;
        Quaternion _restL, _restR;
        Vector3 _holderRest;
        float _raise, _dip, _kick;
        float _gripDistance = 0.1f;

        public Transform Muzzle { get; private set; }
        public Transform Gun => _gun != null ? _gun.transform : null;

        void Awake()
        {
            if (armL != null) _restL = armL.localRotation;
            if (armR != null) _restR = armR.localRotation;
            if (holder != null)
            {
                _holderRest = holder.localPosition;
                foreach (Transform child in holder) _guns[child.name] = child.gameObject;
            }
        }

        void Start()
        {
            _weapons = GetComponent<PlayerWeapons>();
            if (_weapons == null) _weapons = PlayerWeapons.Instance;
            if (_weapons == null) return;
            _weapons.WeaponChanged += Refresh;
            _weapons.Fired += OnFired;
            Refresh();
        }

        void OnDestroy()
        {
            if (_weapons == null) return;
            _weapons.WeaponChanged -= Refresh;
            _weapons.Fired -= OnFired;
        }

        void OnFired() => _kick = 1f;

        void Refresh()
        {
            var def = _weapons != null ? _weapons.Current : null;
            _gun = null;
            Muzzle = null;
            foreach (var kv in _guns) kv.Value.SetActive(false);
            if (def == null || !_guns.TryGetValue(def.ModelName, out _gun)) return;
            _gun.SetActive(true);
            foreach (var t in _gun.GetComponentsInChildren<Transform>(true))
                if (t.name == "Muzzle") { Muzzle = t; break; }
            float z = Muzzle != null ? Muzzle.localPosition.z : 0.3f;
            _gripDistance = Mathf.Clamp(z * 0.45f, 0.06f, 0.5f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (_weapons == null || armL == null || armR == null || _gun == null || dt <= 0f) return;

            bool combat = _weapons.IsAiming || Time.time - _weapons.LastFireTime < holdAfterFire;
            _raise = Mathf.MoveTowards(_raise, combat ? 1f : 0f, dt * (combat ? 10f : 3.5f));
            bool dipping = _weapons.IsReloading || _weapons.IsSwapping;
            _dip = Mathf.MoveTowards(_dip, dipping ? 1f : 0f, dt * 6f);
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 9f);
            float raise = _raise * (1f - _dip * 0.85f);

            Vector3 aimPoint = FindAimPoint();
            Vector3 shoulder = armR.position;
            Vector3 aimDir = (aimPoint - shoulder).normalized;
            aimDir = (aimPoint - (shoulder + aimDir * armLength)).normalized;

            Vector3 flat = transform.forward;
            flat.y = 0f;
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
            float ang = Mathf.Lerp(readyAngle, dipAngle, _dip) * Mathf.Deg2Rad;
            Vector3 readyDir = flat * Mathf.Cos(ang) + Vector3.down * Mathf.Sin(ang);

            Vector3 dirR = Vector3.Slerp(readyDir, aimDir, raise).normalized;
            if (_kick > 0f)
            {
                Vector3 axis = Vector3.Cross(dirR, Vector3.up);
                if (axis.sqrMagnitude > 0.0001f) dirR = Quaternion.AngleAxis(_kick * kickDegrees, axis.normalized) * dirR;
            }

            float weight = Mathf.Lerp(readyWeight, 1f, raise);
            PointArm(armR, _restR, dirR, weight);
            if (holder != null) holder.localPosition = _holderRest + new Vector3(0f, _kick * kickMeters, 0f);

            // Left hand to the foregrip.
            var g = _gun.transform;
            Vector3 grip = g.position + g.forward * _gripDistance - g.up * 0.04f;
            PointArm(armL, _restL, (grip - armL.position).normalized, weight);
        }

        static void PointArm(Transform arm, Quaternion rest, Vector3 worldDir, float weight)
        {
            Vector3 local = Quaternion.Inverse(arm.parent.rotation) * worldDir;
            Quaternion pose = Quaternion.FromToRotation(Vector3.down, local) * rest;
            arm.localRotation = Quaternion.Slerp(arm.localRotation, pose, weight);
        }

        Vector3 FindAimPoint()
        {
            var cam = Camera.main;
            if (cam == null) return transform.position + transform.forward * 40f + Vector3.up * 1.4f;
            Vector3 camPos = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            float along = Mathf.Max(0.5f, Vector3.Dot(transform.position + Vector3.up * 1.4f - camPos, fwd));
            Vector3 origin = camPos + fwd * along;
            if (Physics.Raycast(origin, fwd, out RaycastHit hit, 150f, GameLayers.ShotMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return origin + fwd * 60f;
        }
    }
}
