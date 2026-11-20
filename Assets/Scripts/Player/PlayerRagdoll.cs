using UnityEngine;

namespace Zombies55
{
    // The player knocked down by a blast: ragdoll while down (no input, no shooting, camera follows the hips), then a get-up clip
    // chosen from which way the chest faces, with the root placed on free ground. Lives on the player root.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerRagdoll : MonoBehaviour, IBlastRagdollVictim
    {
        public static PlayerRagdoll Instance { get; private set; }
        // True from the knockdown until control returns. Input, aiming and shooting check it.
        public static bool IsDown { get; private set; }

        [SerializeField] RagdollRig rig;
        [SerializeField] PlayerAnimation animation_;
        [Header("Tuning")]
        [Tooltip("Cap (m/s) on the blast's velocity at the hips, so a car blast does not launch the player across the block.")]
        [SerializeField] float maxThrowSpeed = 10f;
        [Tooltip("Seconds after standing during which another blast only pushes, never knocks down.")]
        [SerializeField] float protectionSeconds = 1f;
        [Tooltip("Control returns at this fraction of the get-up clip.")]
        [SerializeField] float controlAt = 0.93f;
        [SerializeField] float spin = 4f;

        // Test hooks.
        public static bool ForceFaceUp, ForceFaceDown;
        public bool IsGettingUp { get; private set; }
        public int Knockdowns { get; private set; }
        public Vector3 LastRootPlaced { get; private set; }
        public bool LastPlacementClear { get; private set; }
        public float LastNudge { get; private set; }
        public float ProtectedUntil => _protectedUntil;

        CharacterController _cc;
        PlayerController _controller;
        float _protectedUntil;
        bool _dead;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { IsDown = false; Instance = null; ForceFaceUp = ForceFaceDown = false; }

        void Awake()
        {
            Instance = this;
            _cc = GetComponent<CharacterController>();
            _controller = GetComponent<PlayerController>();
            if (rig == null) rig = GetComponentInChildren<RagdollRig>(true);
            if (animation_ == null) animation_ = GetComponentInChildren<PlayerAnimation>(true);
        }

        void OnEnable() { Instance = this; RagdollPool.Register(this); }
        void OnDisable() { RagdollPool.Unregister(this); }
        void OnDestroy() { if (Instance == this) Instance = null; IsDown = false; }

        public Vector3 BlastCenter => rig != null && rig.IsBusy && rig.Hips != null ? rig.Hips.position : transform.position + Vector3.up * 1f;

        public void OnBlast(in BlastInfo info)
        {
            if (rig == null) return;
            var health = PlayerHealth.Instance;
            _dead = health != null && !health.IsAlive;
            if (rig.IsRagdoll)
            {
                rig.Impulse(MakeLaunch(info));
                return;
            }
            // Protection after standing: only against a new knockdown; a death by the blast still ragdolls.
            if (!_dead && Time.time < _protectedUntil) return;
            Knock(info);
        }

        RagdollLaunch MakeLaunch(in BlastInfo info)
        {
            Vector3 v = _controller != null ? _controller.Velocity : Vector3.zero;
            return new RagdollLaunch
            {
                BaseVelocity = v,
                HasBlast = true,
                Blast = info,
                BlastScale = 1f,
                MaxBlastSpeed = maxThrowSpeed,
                Spin = spin,
            };
        }

        void Knock(in BlastInfo info)
        {
            var launch = MakeLaunch(info);
            // The controller's velocity belongs to the capsule; the hips' blast part is capped separately.
            if (!rig.Begin(launch)) return;
            Knockdowns++;
            IsDown = true;
            IsGettingUp = false;
            _cc.enabled = false;
            if (animation_ != null) animation_.SetSuspended(true);
        }

        void Update()
        {
            if (!IsDown || rig == null) return;
            if (_dead || (PlayerHealth.Instance != null && !PlayerHealth.Instance.IsAlive))
            {
                _dead = true;   // stays ragdolled through the game-over flow
                return;
            }

            if (rig.IsRagdoll)
            {
                if (rig.Settled) StartGetUp();
            }
            else if (rig.CurrentPhase == RagdollRig.Phase.GetUp)
            {
                if (rig.GetUpProgress >= controlAt) Release();
            }
            else Release();   // the rig finished or was aborted
        }

        void StartGetUp()
        {
            rig.ComputeGetUp(out Vector3 hipsGround, out Quaternion facing, out bool onBack);
            if (ForceFaceUp) onBack = true;
            else if (ForceFaceDown) onBack = false;
            // The clip needs the other facing for the other lying direction.
            if (ForceFaceUp || ForceFaceDown)
            {
                Vector3 toHead = (rig.Proxy != null ? rig.Proxy.Body(RagBone.Head).position - rig.Proxy.Body(RagBone.Hips).position : Vector3.forward);
                toHead.y = 0f;
                if (toHead.sqrMagnitude < 0.0001f) toHead = Vector3.forward;
                facing = Quaternion.LookRotation(onBack ? -toHead : toHead, Vector3.up);
            }
            Vector3 ideal = rig.RootForGetUp(hipsGround, facing);
            Vector3 spot = FindStandingSpot(ideal, hipsGround);
            LastRootPlaced = spot;
            LastNudge = (spot - ideal).magnitude;
            _cc.enabled = false;
            rig.BeginGetUp(spot, facing, onBack);
            _cc.enabled = true;
            IsGettingUp = true;
            LastPlacementClear = CapsuleClear(spot);
        }

        void Release()
        {
            IsDown = false;
            IsGettingUp = false;
            if (!_cc.enabled) _cc.enabled = true;
            if (_controller != null) _controller.ResetMotion();
            if (animation_ != null) animation_.SetSuspended(false);
            _protectedUntil = Time.time + protectionSeconds;
        }

        // ------------------------------------------------------------------ placement

        const int BlockMask = (1 << GameLayers.Default) | (1 << GameLayers.Explosive) | (1 << GameLayers.Barrier);

        bool CapsuleClear(Vector3 rootPos)
        {
            float r = _cc.radius;
            Vector3 c = rootPos + _cc.center;
            float half = Mathf.Max(0f, _cc.height * 0.5f - r);
            // A skin of clearance so touching the ground or a wall counts as clear.
            return !Physics.CheckCapsule(c + Vector3.up * (half - 0.001f + 0.06f), c - Vector3.up * (half - 0.06f), r - 0.03f, BlockMask, QueryTriggerInteraction.Ignore);
        }

        bool GroundAt(Vector3 p, out Vector3 grounded)
        {
            grounded = p;
            // From above the candidate (never from inside a ceiling): the nearest ground within a step up or a drop down.
            if (!Physics.Raycast(p + Vector3.up * 0.8f, Vector3.down, out var hit, 2.4f, BlockMask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.normal.y < 0.7f) return false;
            grounded = new Vector3(p.x, hit.point.y, p.z);
            return true;
        }

        Vector3 FindStandingSpot(Vector3 ideal, Vector3 hipsGround)
        {
            if (GroundAt(ideal, out var g) && CapsuleClear(g) && LineClear(hipsGround, g)) return g;
            Vector3 best = ideal;
            float bestD = float.MaxValue;
            bool found = false;
            for (int ring = 1; ring <= 8 && !found; ring++)
            {
                float rad = ring * 0.25f;
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI * 2f / 16f;
                    Vector3 p = ideal + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rad;
                    if (!GroundAt(p, out var gp) || !CapsuleClear(gp) || !LineClear(hipsGround, gp)) continue;
                    float d = (gp - ideal).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = gp; found = true; }
                }
            }
            if (found) return best;
            return GroundAt(ideal, out var last) ? last : ideal;
        }

        // No wall between where the body lies and where it will stand.
        static bool LineClear(Vector3 from, Vector3 to)
        {
            return !Physics.Linecast(from + Vector3.up * 0.5f, to + Vector3.up * 0.9f, BlockMask, QueryTriggerInteraction.Ignore);
        }
    }
}
