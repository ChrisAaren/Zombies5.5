using System;
using UnityEngine;

namespace Zombies55
{
    // CONTRACT STUB: the public API below is fixed. The enemies agent implements the behavior
    // (round sizes, spawning, break timer) without changing these signatures.
    public class RoundManager : MonoBehaviour
    {
        public const int MaxAlive = 48;

        public static RoundManager Instance { get; private set; }

        public int Round { get; private set; }               // 0 before the first round starts
        public int AliveCount { get; private set; }          // enemies currently in the world
        public int RemainingThisRound { get; private set; }  // alive + not yet spawned
        public bool InBreak { get; private set; }            // between rounds
        public float BreakTimeLeft { get; private set; }     // seconds until the next round
        public int TotalKills { get; private set; }
        public int TotalHeadshots { get; private set; }
        public int RoundsSurvived => Mathf.Max(0, InBreak ? Round : Round - 1);

#pragma warning disable CS0067 // raised by the implementation
        public event Action<int> RoundStarted;            // round number; explosives reset on this
        public event Action<int> RoundCompleted;          // round number
        public event Action<Enemy, DamageInfo> EnemyKilled;
#pragma warning restore CS0067

        void Awake() => Instance = this;
        void OnEnable() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
