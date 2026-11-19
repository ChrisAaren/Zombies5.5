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

    // CONTRACT STUB: the public API below is fixed. The zones agent implements the behavior
    // (district table, ownership, gates, enemy spawn filtering) without changing these signatures.
    public class ZoneManager : MonoBehaviour
    {
        public static ZoneManager Instance { get; private set; }

        public int ZoneCount => 0;
        public int OwnedCount { get; private set; }
        public int CurrentZone { get; private set; } = -1;   // the district the player stands in, -1 if none

#pragma warning disable CS0067 // raised by the implementation
        public event Action<int> ZoneUnlocked;               // zone id
        public event Action<int> CurrentZoneChanged;         // zone id the player entered
#pragma warning restore CS0067

        void Awake() => Instance = this;
        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public ZoneInfo GetZone(int id) => null;
        public bool IsOwned(int id) => false;
        public int ZoneAt(Vector3 worldPosition) => -1;
        public bool TryBuy(int id) => false;                 // spends points via PlayerWallet; false if unaffordable or invalid
    }
}
