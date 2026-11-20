using System.Collections.Generic;
using UnityEngine;

namespace Zombies55
{
    // Something a blast can knock down: enemies and the player.
    public interface IBlastRagdollVictim
    {
        // Where the blast distance is measured from (the hips or chest).
        Vector3 BlastCenter { get; }
        void OnBlast(in BlastInfo info);
    }

    // The pool of ragdoll proxies and the one listener that turns a blast into knockdowns.
    // Enemy ragdolls count against EffectsDetail.MaxRagdolls; the player's does not.
    public class RagdollPool : MonoBehaviour
    {
        // Inside this fraction of a blast's radius a survivor is knocked down.
        public const float KnockdownFraction = 0.85f;

        public static RagdollPool Instance { get; private set; }

        [System.Serializable]
        public class Group
        {
            public int archetype;
            public RagdollProxy[] proxies;
        }

        [SerializeField] Group[] groups;

        public const int MaxStartsPerFrame = 10;
        int _budgetFrame = -1, _startsThisFrame;

        readonly List<IBlastRagdollVictim> _victims = new List<IBlastRagdollVictim>(64);
        readonly List<IBlastRagdollVictim> _hit = new List<IBlastRagdollVictim>(64);

        // Counters for tests and the benchmark.
        public int ActiveCounted { get; private set; }
        public int ActiveTotal { get; private set; }
        public int PeakActive { get; private set; }
        public int Granted { get; private set; }
        public int Refused { get; private set; }
        public float LastAcquireMs { get; private set; }
        public float MaxAcquireMs { get; private set; }

        public static void Register(IBlastRagdollVictim v)
        {
            if (Instance != null) { if (!Instance._victims.Contains(v)) Instance._victims.Add(v); }
            else if (!s_Pending.Contains(v)) s_Pending.Add(v);
        }

        public static void Unregister(IBlastRagdollVictim v)
        {
            if (Instance != null) Instance._victims.Remove(v);
            s_Pending.Remove(v);
        }

        static readonly List<IBlastRagdollVictim> s_Pending = new List<IBlastRagdollVictim>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; s_Pending.Clear(); }

        void Awake()
        {
            Instance = this;
            // The layer matrix is also written to the project's physics settings by the scene builder; set again so a stale
            // setting can never put a ragdoll on a collision course with the player, the crowd or the debris.
            Physics.IgnoreLayerCollision(GameLayers.Ragdoll, GameLayers.Player, true);
            Physics.IgnoreLayerCollision(GameLayers.Ragdoll, GameLayers.Enemy, true);
            Physics.IgnoreLayerCollision(GameLayers.Ragdoll, GameLayers.Debris, true);
            Physics.IgnoreLayerCollision(GameLayers.Ragdoll, GameLayers.Ragdoll, true);
            foreach (var v in s_Pending) if (!_victims.Contains(v)) _victims.Add(v);
            s_Pending.Clear();
        }

        // Every proxy is brought up once, far below the city, so the first knockdown of a run does not pay for first-time setup.
        System.Collections.IEnumerator Start()
        {
            yield return null;
            var list = new List<RagdollProxy>();
            foreach (var g in groups) foreach (var p in g.proxies) if (p != null && !p.InUse) list.Add(p);
            foreach (var p in list) { p.ResetState(); p.transform.position = new Vector3(0f, -2000f, 0f); p.gameObject.SetActive(true); }
            yield return null;
            yield return null;
            foreach (var p in list) if (!p.InUse) p.gameObject.SetActive(false);
        }

        void OnEnable() { Instance = this; Blast.Detonated += OnDetonated; }
        void OnDisable() { Blast.Detonated -= OnDetonated; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public int Capacity(int archetype)
        {
            foreach (var g in groups) if (g.archetype == archetype) return g.proxies.Length;
            return 0;
        }

        // A free proxy, or null when none is left or (for counted users) the detail level's limit is reached.
        public RagdollProxy Acquire(int archetype, bool counted)
        {
            if (counted && ActiveCounted >= EffectsDetail.MaxRagdolls) { Refused++; return null; }
            // Starting a ragdoll costs about 0.25 ms; a blast over a crowd cannot start them all in one frame.
            if (Time.frameCount != _budgetFrame) { _budgetFrame = Time.frameCount; _startsThisFrame = 0; }
            if (_startsThisFrame >= MaxStartsPerFrame) { Refused++; return null; }
            foreach (var g in groups)
            {
                if (g.archetype != archetype) continue;
                foreach (var p in g.proxies)
                {
                    if (p == null || p.InUse) continue;
                    p.InUse = true;
                    _startsThisFrame++;
                    p.Counted = counted;
                    p.ResetState();
                    ActiveTotal++;
                    if (counted) ActiveCounted++;
                    PeakActive = Mathf.Max(PeakActive, ActiveTotal);
                    Granted++;
                    return p;
                }
            }
            Refused++;
            return null;
        }

        public void Release(RagdollProxy p)
        {
            if (p == null || !p.InUse) return;
            p.InUse = false;
            ActiveTotal = Mathf.Max(0, ActiveTotal - 1);
            if (p.Counted) ActiveCounted = Mathf.Max(0, ActiveCounted - 1);
            p.gameObject.SetActive(false);
        }

        internal void RecordAcquireCost(float ms)
        {
            LastAcquireMs = ms;
            MaxAcquireMs = Mathf.Max(MaxAcquireMs, ms);
        }

        public void ResetStats() { PeakActive = ActiveTotal; Granted = 0; Refused = 0; MaxAcquireMs = 0f; LastAcquireMs = 0f; }

        const int WallMask = 1 << GameLayers.Default;

        void OnDetonated(BlastInfo info)
        {
            float reach = info.Radius * KnockdownFraction;
            float r2 = reach * reach;
            // Collect first: a victim's reaction can change the list.
            _hit.Clear();
            for (int i = 0; i < _victims.Count; i++)
            {
                var v = _victims[i];
                Vector3 c = v.BlastCenter;
                if ((c - info.Position).sqrMagnitude > r2) continue;
                // Solid walls between the blast and the body stop the push (the same walls that shield from the damage).
                if (Physics.Linecast(info.Position + Vector3.up * 0.3f, c, WallMask, QueryTriggerInteraction.Ignore)) continue;
                _hit.Add(v);
            }
            for (int i = 0; i < _hit.Count; i++) _hit[i].OnBlast(info);
            _hit.Clear();
        }
    }
}
