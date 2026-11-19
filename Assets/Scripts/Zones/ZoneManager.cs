using System;
using UnityEngine;

namespace Zombies55
{
    [Serializable]
    public class ZoneInfo
    {
        public int Id;
        public string Name;          // e.g. "Fountain Square"
        public int Cost;             // points to unlock; 0 for the starting district
        public bool StartsOwned;
    }

    // District ownership, barriers, gates and "which district is the player in" tracking.
    // Ownership lives in this component, so a scene reload (Restart) resets it.
    public class ZoneManager : MonoBehaviour
    {
        public static ZoneManager Instance { get; private set; }

        [SerializeField] ParticleSystem dustPuff;
        [SerializeField] ParticleSystem smokePuff;

        ZoneInfo[] _zones;
        bool[] _owned;
        ZoneBarrier[] _barriers = new ZoneBarrier[0];
        ZoneGate[] _gates = new ZoneGate[0];
        ZoneAudio _audio;

        public int ZoneCount => ZoneLayout.Count;
        public int OwnedCount { get; private set; }
        public int CurrentZone { get; private set; } = -1;   // the district the player stands in, -1 if none

        public event Action<int> ZoneUnlocked;               // zone id
        public event Action<int> CurrentZoneChanged;         // zone id the player entered

        void Awake() { Instance = this; Init(); }
        void OnEnable() { Instance = this; Init(); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Init()
        {
            if (_zones != null) return;
            _zones = new ZoneInfo[ZoneLayout.Count];
            _owned = new bool[ZoneLayout.Count];
            for (int i = 0; i < _zones.Length; i++)
            {
                _zones[i] = new ZoneInfo { Id = i, Name = ZoneLayout.Names[i], Cost = ZoneLayout.Costs[i], StartsOwned = i == 0 };
                _owned[i] = _zones[i].StartsOwned;
                if (_owned[i]) OwnedCount++;
            }
            _audio = GetComponent<ZoneAudio>();
            if (_audio == null) _audio = gameObject.AddComponent<ZoneAudio>();
        }

        void Start()
        {
            _barriers = FindObjectsByType<ZoneBarrier>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _gates = FindObjectsByType<ZoneGate>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Refresh(false);
        }

        void Update()
        {
            var p = PlayerHealth.Instance;
            if (p == null) return;
            int z = ZoneLayout.DistrictAt(p.transform.position);
            if (z < 0 || z == CurrentZone) return;
            CurrentZone = z;
            CurrentZoneChanged?.Invoke(z);
        }

        public ZoneInfo GetZone(int id) => id >= 0 && id < ZoneCount && _zones != null ? _zones[id] : null;
        public bool IsOwned(int id) => id >= 0 && id < ZoneCount && _owned != null && _owned[id];
        public int ZoneAt(Vector3 worldPosition) => ZoneLayout.DistrictAt(worldPosition);

        // True when worldPosition lies in an owned district (used for spawn filtering).
        public bool IsOwnedAt(Vector3 worldPosition) => IsOwned(ZoneAt(worldPosition));

        // Spends points via PlayerWallet; false if unaffordable, invalid or already owned.
        public bool TryBuy(int id)
        {
            if (id < 0 || id >= ZoneCount || _owned[id]) return false;
            var wallet = PlayerWallet.Instance;
            var z = _zones[id];
            if (wallet == null || !wallet.TrySpend(z.Cost, z.Name)) return false;
            _owned[id] = true;
            OwnedCount++;
            Refresh(true);
            ZoneUnlocked?.Invoke(id);
            return true;
        }

        void Refresh(bool animate)
        {
            foreach (var b in _barriers)
                if (b != null) b.SetOpen(IsOwned(b.ZoneA) && IsOwned(b.ZoneB), animate, this);
            foreach (var g in _gates)
                if (g != null) g.gameObject.SetActive(IsOwned(g.OwnerZone) && !IsOwned(g.TargetZone));
        }

        // Dust along a barrier segment and the clank.
        public void PlayBarrierOpen(Vector3 center, Vector3 along, float length)
        {
            _audio?.PlayClank(center);
            if (dustPuff == null) return;
            int n = Mathf.Max(4, Mathf.RoundToInt(length / 3f));
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                float t = (i + UnityEngine.Random.value) / n - 0.5f;
                ep.position = center + along * (t * length) + Vector3.up * 0.2f;
                ep.velocity = UnityEngine.Random.insideUnitSphere * 1.2f + Vector3.up * 0.8f;
                dustPuff.Emit(ep, 3);
            }
        }

        // Short dark smoke where an enemy appears in view.
        public void PlaySpawnSmoke(Vector3 position)
        {
            if (smokePuff == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = position + Vector3.up * 0.2f,
                velocity = Vector3.up * 0.8f,
            };
            smokePuff.Emit(ep, 9);
        }
    }
}
