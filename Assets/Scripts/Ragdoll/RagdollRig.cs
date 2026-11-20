using System;
using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    // What starts a ragdoll: the velocity the character already has, and optionally a blast that throws it.
    public struct RagdollLaunch
    {
        public Vector3 BaseVelocity;     // the character's own motion (agent or controller velocity), m/s
        public bool HasBlast;
        public BlastInfo Blast;
        public float BlastScale;         // 1 = the blast's own velocity
        public float MaxBlastSpeed;      // cap on the blast's velocity at the hips (0 = none)
        public float Spin;               // rad/s of random tumble on hips and chest
    }

    // Turns any Humanoid character into a ragdoll on demand and back into an animated one with a get-up clip.
    //
    // The rig itself carries no rigidbodies: that would put 12 physics bodies per character (48 enemies) onto every animated bone
    // for nothing. A pooled RagdollProxy (inactive until needed) does the physics; this component reads the character's pose into it
    // on Begin, copies the body rotations back onto the bones each LateUpdate, and on the way up blends from the ragdoll pose into
    // the get-up clip. Idle cost: none (no Update, no physics objects). The owner (Enemy, PlayerRagdoll) decides when to fall and
    // when to stand.
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(Animator))]
    public class RagdollRig : MonoBehaviour
    {
        public enum Phase { Animated, Ragdoll, GetUp }

        [Tooltip("Which proxy skeleton fits this character (RagdollPool group).")]
        [SerializeField] int archetype;
        [Tooltip("Counts against EffectsDetail.MaxRagdolls (enemies). The player's rig does not.")]
        [SerializeField] bool countsAgainstLimit = true;
        [Tooltip("The character's root follows the hips while ragdolled (the player: camera, enemies and triggers follow it).")]
        [SerializeField] bool followRoot;
        [Tooltip("The object that holds the character and moves it (enemy or player root). Empty = the animator's parent.")]
        [SerializeField] Transform root;

        [Header("Settling")]
        [SerializeField] float settleSpeed = 0.4f;
        [SerializeField] float settleAngularSpeed = 2.5f;
        [SerializeField] float settleSeconds = 0.3f;
        [SerializeField] float maxDownSeconds = 2.5f;

        [Header("Get up")]
        [SerializeField] float blendSeconds = 0.4f;
        [Tooltip("The clips start with the hips this far (m) ahead of the standing root, behind its facing.")]
        [SerializeField] float getUpHipsOffset = 0.42f;
        [SerializeField] float locomotionFade = 0.25f;

        public Phase CurrentPhase { get; private set; }
        public bool IsRagdoll => CurrentPhase == Phase.Ragdoll;
        public bool IsBusy => CurrentPhase != Phase.Animated;
        public float TimeDown { get; private set; }
        public bool Settled { get; private set; }
        public float GetUpProgress => _getUpLength > 0f ? Mathf.Clamp01(_getUpTime / _getUpLength) : 1f;
        public float GetUpSeconds => _getUpLength;
        public bool LastGetUpFaceUp { get; private set; }
        public RagdollProxy Proxy => _proxy;
        public Transform Hips => _bones != null ? _bones[(int)RagBone.Hips] : null;
        public Animator Animator => _anim;
        public Transform Root => root != null ? root : transform.parent != null ? transform.parent : transform;
        public float SinkOffset { get; set; }

        // Raised when the get-up clip is over and the character is an animated one again.
        public event Action Finished;

        // Measurements for tests: the largest bone displacement (m) in one frame at the switch to the ragdoll and to the get-up,
        // and over the first few frames after each.
        public static bool TrackJumps;
        public static float JumpAtRagdoll, JumpAfterRagdoll, JumpAtGetUp, JumpAfterGetUp;

        Animator _anim;
        Transform[] _bones;
        Transform[] _skin;
        Quaternion[] _snapRot;
        Vector3 _snapHipsPos;
        Quaternion _snapHipsRot;
        Vector3[] _prevPos;
        int _trackFrames;
        bool _trackIsGetUp;
        RagdollProxy _proxy;
        float _settleTimer;
        float _getUpTime, _getUpLength;
        float[] _layerWeights;
        bool _sampled;

        static readonly int GetUpBackHash = Animator.StringToHash("GetUpBack");
        static readonly int GetUpFrontHash = Animator.StringToHash("GetUpFront");
        static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
        static float s_BackLength = 2.57f, s_FrontLength = 2.17f;
        static bool s_LengthsRead;

        // Idle rigs are switched off (no Update, no LateUpdate): the component is only enabled while a character is down or getting up.
        void Awake() { EnsureInit(); enabled = false; }

        void EnsureInit()
        {
            if (_sampled) return;
            _sampled = true;
            _anim = GetComponent<Animator>();
            _bones = new Transform[RagBones.Count];
            for (int i = 0; i < _bones.Length; i++) _bones[i] = RagBones.Resolve(_anim, (RagBone)i);
            var set = new HashSet<Transform>();
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in smr.bones) if (b != null) set.Add(b);
            foreach (var b in _bones) if (b != null) set.Add(b);
            _skin = new Transform[set.Count];
            set.CopyTo(_skin);
            _snapRot = new Quaternion[_skin.Length];
            _prevPos = new Vector3[_skin.Length];
        }

        void OnDisable() { if (!gameObject.activeInHierarchy) Abort(); }

        // Drops everything (a pooled enemy that is switched off): the proxy goes back, the animator comes back on.
        public void Abort()
        {
            if (_proxy != null) { RagdollPool.Instance?.Release(_proxy); _proxy = null; }
            if (CurrentPhase != Phase.Animated)
            {
                CurrentPhase = Phase.Animated;
                if (_anim != null) _anim.enabled = true;
                RestoreLayers();
            }
            SinkOffset = 0f;
            Settled = false;
            TimeDown = 0f;
            enabled = false;
        }

        static void ReadLengths(Animator a)
        {
            if (s_LengthsRead || a.runtimeAnimatorController == null) return;
            s_LengthsRead = true;
            foreach (var c in a.runtimeAnimatorController.animationClips)
            {
                if (c.name == "Anim_GetUpBack" || c.name == "GetUpBack") s_BackLength = c.length;
                else if (c.name == "Anim_GetUpFront" || c.name == "GetUpFront") s_FrontLength = c.length;
            }
        }

        // ------------------------------------------------------------------ down

        // Starts the ragdoll. False when no proxy is free or the ragdoll limit is reached (the caller falls back to an animation).
        public bool Begin(in RagdollLaunch launch)
        {
            EnsureInit();
            var pool = RagdollPool.Instance;
            if (pool == null || _bones[(int)RagBone.Hips] == null) return false;
            if (CurrentPhase == Phase.Ragdoll) { Impulse(launch); return true; }

            var proxy = pool.Acquire(archetype, countsAgainstLimit);
            if (proxy == null) return false;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (CurrentPhase == Phase.GetUp) { CurrentPhase = Phase.Animated; RestoreLayers(); }

            // The pose now, and the pose one short step later: the difference is the limbs' own velocity.
            const float step = 1f / 30f;
            int n = RagBones.Count;
            var posA = new Vector3[n];
            var rotA = new Quaternion[n];
            var posB = new Vector3[n];
            var rotB = new Quaternion[n];
            ReadBodies(posA, rotA);
            bool stepped = false;
            // The limbs' own animation velocity costs an extra animator evaluation (~0.1 ms): the player gets it, a crowd of enemies
            // thrown by a blast takes the body's velocity (the blast dwarfs the swing of a stride).
            if (!countsAgainstLimit && _anim.isActiveAndEnabled && _anim.runtimeAnimatorController != null && _anim.enabled)
            {
                SnapshotSkin();
                _anim.Update(step);
                ReadBodies(posB, rotB);
                RestoreSkin();
                stepped = true;
            }

            _proxy = proxy;
            float s = (transform.lossyScale.x + transform.lossyScale.y + transform.lossyScale.z) / 3f;
            var pt = proxy.transform;
            pt.localScale = Vector3.one * s;
            proxy.gameObject.SetActive(true);

            Vector3 blastHips = Vector3.zero;
            float blastK = launch.BlastScale > 0f ? launch.BlastScale : 1f;
            if (launch.HasBlast)
            {
                blastHips = launch.Blast.VelocityAt(posA[(int)RagBone.Hips]);
                if (launch.MaxBlastSpeed > 0f && blastHips.magnitude * blastK > launch.MaxBlastSpeed)
                    blastK = launch.MaxBlastSpeed / blastHips.magnitude;
            }
            float spin = launch.Spin;
            for (int i = 0; i < n; i++)
            {
                var rb = proxy.Bodies[i];
                rb.transform.SetPositionAndRotation(posA[i], rotA[i]);
                Vector3 v = launch.BaseVelocity;
                Vector3 w = Vector3.zero;
                if (stepped)
                {
                    v += (posB[i] - posA[i]) / step;
                    var dq = rotB[i] * Quaternion.Inverse(rotA[i]);
                    dq.ToAngleAxis(out float ang, out Vector3 axis);
                    if (ang > 180f) ang -= 360f;
                    if (!float.IsNaN(axis.x) && !float.IsInfinity(axis.x)) w = axis * (ang * Mathf.Deg2Rad / step);
                }
                if (launch.HasBlast) v += launch.Blast.VelocityAt(posA[i]) * (blastK * RagBones.ThrowWeight((RagBone)i));
                if (spin > 0f)
                {
                    float share = i <= (int)RagBone.Chest ? 1f : 0.5f;
                    w += UnityEngine.Random.onUnitSphere * (spin * share);
                }
                rb.linearVelocity = v;
                rb.angularVelocity = w;
                rb.WakeUp();
            }

            // The bodies were moved through their transforms: push that to the physics scene now, or the interpolated pose of the first
            // frames is still the proxy's parked one.
            Physics.SyncTransforms();
            if (TrackJumps) StartTrack(false);
            enabled = true;
            _anim.enabled = false;
            SuppressLayers();
            CurrentPhase = Phase.Ragdoll;
            TimeDown = 0f;
            _settleTimer = 0f;
            Settled = false;
            SinkOffset = 0f;
            sw.Stop();
            pool.RecordAcquireCost((float)sw.Elapsed.TotalMilliseconds);
            return true;
        }

        // A further blast while down: more velocity on the bodies, and the settle timer starts again.
        public void Impulse(in RagdollLaunch launch)
        {
            if (_proxy == null || !launch.HasBlast) return;
            float k = launch.BlastScale > 0f ? launch.BlastScale : 1f;
            Vector3 hips = launch.Blast.VelocityAt(_proxy.Body(RagBone.Hips).position);
            if (launch.MaxBlastSpeed > 0f && hips.magnitude * k > launch.MaxBlastSpeed) k = launch.MaxBlastSpeed / Mathf.Max(0.01f, hips.magnitude);
            for (int i = 0; i < RagBones.Count; i++)
            {
                var rb = _proxy.Bodies[i];
                rb.linearVelocity += launch.Blast.VelocityAt(rb.position) * (k * RagBones.ThrowWeight((RagBone)i));
                if (launch.Spin > 0f) rb.angularVelocity += UnityEngine.Random.onUnitSphere * (launch.Spin * (i <= (int)RagBone.Chest ? 1f : 0.5f));
                rb.WakeUp();
            }
            _settleTimer = 0f;
            Settled = false;
            TimeDown = Mathf.Min(TimeDown, maxDownSeconds - 1f);
        }

        // The corpse stops simulating (it is about to sink away).
        public void Freeze() { if (_proxy != null) _proxy.Freeze(); }

        public Vector3 HipsVelocity => _proxy != null ? _proxy.Body(RagBone.Hips).linearVelocity : Vector3.zero;

        void Update()
        {
            if (CurrentPhase == Phase.Ragdoll && _proxy != null) UpdateSettle(Time.deltaTime);
            else if (CurrentPhase == Phase.GetUp) _getUpTime += Time.deltaTime;
        }

        void UpdateSettle(float dt)
        {
            TimeDown += dt;
            if (_proxy.Frozen) { Settled = true; return; }
            // Torso and head must be nearly still; the limbs may still be swinging a little (they hang from joints and wobble last).
            bool slow = true;
            for (int i = 0; i < RagBones.Count && slow; i++)
            {
                var rb = _proxy.Bodies[i];
                if (rb.IsSleeping()) continue;
                float k = i <= (int)RagBone.Head ? 1f : 2.5f;
                if (rb.linearVelocity.sqrMagnitude > settleSpeed * settleSpeed * k * k
                    || rb.angularVelocity.sqrMagnitude > settleAngularSpeed * settleAngularSpeed * k * k * 2f) slow = false;
            }
            _settleTimer = slow ? _settleTimer + dt : 0f;
            Settled = _settleTimer >= settleSeconds || TimeDown >= maxDownSeconds;
        }

        // ------------------------------------------------------------------ pose

        void ReadBodies(Vector3[] pos, Quaternion[] rot)
        {
            for (int i = 0; i < pos.Length; i++) { pos[i] = _bones[i].position; rot[i] = _bones[i].rotation; }
        }

        Vector3 _stashHipsPos;
        Quaternion _stashHipsRot;
        Quaternion[] _stashRot;

        void SnapshotSkin()
        {
            if (_stashRot == null || _stashRot.Length != _skin.Length) _stashRot = new Quaternion[_skin.Length];
            for (int i = 0; i < _skin.Length; i++) _stashRot[i] = _skin[i].localRotation;
            var h = _bones[(int)RagBone.Hips];
            _stashHipsPos = h.position;
            _stashHipsRot = h.rotation;
        }

        void RestoreSkin()
        {
            for (int i = 0; i < _skin.Length; i++) _skin[i].localRotation = _stashRot[i];
            var h = _bones[(int)RagBone.Hips];
            h.SetPositionAndRotation(_stashHipsPos, _stashHipsRot);
        }

        void SuppressLayers()
        {
            int layers = _anim.layerCount;
            if (_layerWeights == null || _layerWeights.Length != layers) _layerWeights = new float[layers];
            for (int i = 0; i < layers; i++)
            {
                _layerWeights[i] = _anim.GetLayerWeight(i);
                if (i > 0) _anim.SetLayerWeight(i, 0f);
            }
        }

        void RestoreLayers()
        {
            if (_anim == null || _layerWeights == null) return;
            for (int i = 1; i < _layerWeights.Length && i < _anim.layerCount; i++) _anim.SetLayerWeight(i, _layerWeights[i]);
        }

        void LateUpdate()
        {
            if (CurrentPhase == Phase.Ragdoll)
            {
                if (_proxy == null) { Abort(); return; }
                Drive();
            }
            else if (CurrentPhase == Phase.GetUp)
            {
                Blend();
                if (_getUpTime >= _getUpLength) FinishGetUp();
            }
            else return;
            if (_trackFrames > 0) Track();
        }

        void Drive()
        {
            // The root moves first: the bones are its children, so moving it afterwards would drag the pose just written.
            if (followRoot) FollowHips();
            for (int i = 0; i < RagBones.Count; i++)
            {
                var b = _proxy.BodyTransform((RagBone)i);
                var bone = _bones[i];
                if (i == 0) bone.SetPositionAndRotation(b.position + Vector3.down * SinkOffset, b.rotation);
                else bone.rotation = b.rotation;
            }
        }

        void FollowHips()
        {
            var r = Root;
            Vector3 h = _proxy.BodyTransform(RagBone.Hips).position;
            float ground = h.y - 0.15f;
            if (Physics.Raycast(h + Vector3.up * 0.3f, Vector3.down, out var hit, 6f, GroundMask, QueryTriggerInteraction.Ignore)) ground = hit.point.y;
            r.position = new Vector3(h.x, Mathf.Max(ground, h.y - 0.12f), h.z);
        }

        public const int GroundMask = (1 << GameLayers.Default) | (1 << GameLayers.Explosive) | (1 << GameLayers.Barrier);

        // ------------------------------------------------------------------ up

        // Where the character should stand and which clip fits: the hips' ground point, the facing the clip expects, and
        // whether it lies on its back (belly up).
        public void ComputeGetUp(out Vector3 hipsGround, out Quaternion facing, out bool onBack)
        {
            Vector3 hips = _proxy != null ? _proxy.Body(RagBone.Hips).position : _bones[(int)RagBone.Hips].position;
            float gy = hips.y - 0.12f;
            if (Physics.Raycast(hips + Vector3.up * 0.5f, Vector3.down, out var hit, 6f, GroundMask, QueryTriggerInteraction.Ignore)) gy = hit.point.y;
            hipsGround = new Vector3(hips.x, gy, hips.z);

            Vector3 front;
            if (_proxy != null)
                front = _proxy.BodyTransform(RagBone.Chest).rotation * _proxy.ChestFront + _proxy.BodyTransform(RagBone.Hips).rotation * _proxy.HipsFront;
            else front = Root.forward;
            onBack = front.y > 0f;

            Vector3 head = _proxy != null ? _proxy.Body(RagBone.Head).position : _bones[(int)RagBone.Head].position;
            Vector3 toHead = head - hips;
            toHead.y = 0f;
            if (toHead.sqrMagnitude < 0.0036f)
            {
                // Curled up or sitting: use the belly direction instead.
                Vector3 f = front; f.y = 0f;
                toHead = f.sqrMagnitude > 0.001f ? (onBack ? -f : f) : Root.forward;
            }
            // Back clip: the head lies behind the standing spot, so forward runs head to feet. Front clip: forward is hips to head.
            Vector3 dir = onBack ? -toHead : toHead;
            facing = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        public Vector3 RootForGetUp(Vector3 hipsGround, Quaternion facing)
        {
            float s = Mathf.Max(0.5f, transform.lossyScale.y);
            return hipsGround + facing * new Vector3(0f, 0f, getUpHipsOffset * s);
        }

        // Swaps the ragdoll for the get-up clip: the root goes to rootPosition/rootRotation, the proxy is released, and the bones
        // blend from the ragdoll pose into the clip.
        public void BeginGetUp(Vector3 rootPosition, Quaternion rootRotation, bool onBack)
        {
            if (CurrentPhase != Phase.Ragdoll) return;
            ReadLengths(_anim);
            // Pose to blend from: every skinned bone's local rotation, the hips in world space.
            for (int i = 0; i < _skin.Length; i++) _snapRot[i] = _skin[i].localRotation;
            var hips = _bones[(int)RagBone.Hips];
            _snapHipsPos = hips.position;
            _snapHipsRot = hips.rotation;
            if (TrackJumps) StartTrack(true);

            if (_proxy != null) { RagdollPool.Instance?.Release(_proxy); _proxy = null; }
            Root.SetPositionAndRotation(rootPosition, rootRotation);
            SinkOffset = 0f;
            _anim.enabled = true;
            _anim.Play(onBack ? GetUpBackHash : GetUpFrontHash, 0, 0f);
            _getUpLength = onBack ? s_BackLength : s_FrontLength;
            _getUpTime = 0f;
            LastGetUpFaceUp = onBack;
            CurrentPhase = Phase.GetUp;
        }

        void Blend()
        {
            float t = Mathf.Clamp01(_getUpTime / Mathf.Max(0.01f, blendSeconds));
            t = t * t * (3f - 2f * t);
            if (t >= 1f) return;
            var hips = _bones[(int)RagBone.Hips];
            for (int i = 0; i < _skin.Length; i++)
            {
                var b = _skin[i];
                if (b == hips) continue;
                b.localRotation = Quaternion.Slerp(_snapRot[i], b.localRotation, t);
            }
            hips.SetPositionAndRotation(Vector3.Lerp(_snapHipsPos, hips.position, t), Quaternion.Slerp(_snapHipsRot, hips.rotation, t));
        }

        void FinishGetUp()
        {
            CurrentPhase = Phase.Animated;
            RestoreLayers();
            _anim.CrossFadeInFixedTime(LocomotionHash, locomotionFade, 0);
            enabled = false;
            Finished?.Invoke();
        }

        // ------------------------------------------------------------------ jump measurement

        void StartTrack(bool getUp)
        {
            _trackIsGetUp = getUp;
            _trackFrames = 6;
            for (int i = 0; i < _skin.Length; i++) _prevPos[i] = _skin[i].position;
            _trackFirst = true;
        }

        bool _trackFirst;

        void Track()
        {
            float max = 0f;
            var hips = _bones[(int)RagBone.Hips];
            for (int i = 0; i < _skin.Length; i++)
            {
                // Only bones that move with the body (the hips and what hangs off it): the skeleton's root node follows the root object.
                if (_skin[i] == hips || _skin[i].IsChildOf(hips)) max = Mathf.Max(max, (_skin[i].position - _prevPos[i]).magnitude);
                _prevPos[i] = _skin[i].position;
            }
            if (_trackFirst)
            {
                if (_trackIsGetUp) JumpAtGetUp = Mathf.Max(JumpAtGetUp, max); else JumpAtRagdoll = Mathf.Max(JumpAtRagdoll, max);
                _trackFirst = false;
            }
            else if (_trackIsGetUp) JumpAfterGetUp = Mathf.Max(JumpAfterGetUp, max);
            else JumpAfterRagdoll = Mathf.Max(JumpAfterRagdoll, max);
            _trackFrames--;
        }

        // Editor-time setup (SceneBuilder).
        public void Configure(int archetypeId, bool counted, bool followRootTransform, Transform rootTransform)
        {
            archetype = archetypeId;
            countsAgainstLimit = counted;
            followRoot = followRootTransform;
            root = rootTransform;
        }
    }
}
