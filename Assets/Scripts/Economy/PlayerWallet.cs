using System;
using UnityEngine;

namespace Zombies55
{
    // The player's points. Enemies pay into it; weapon kiosks spend from it.
    public class PlayerWallet : MonoBehaviour
    {
        public static PlayerWallet Instance { get; private set; }

        [SerializeField] int startingPoints = 500;

        public int Points { get; private set; }
        public int TotalEarned { get; private set; }

        // Every award is multiplied by this (the Payday pickup sets 2). Spending is never affected.
        public int Multiplier { get; set; } = 1;

        // (new total, delta, reason)
        public event Action<int, int, string> Changed;

        void Awake()
        {
            Instance = this;
            Points = startingPoints;
        }

        // Re-register after a script reload during Play mode, which clears statics.
        void OnEnable() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Add(int amount, string reason)
        {
            if (amount <= 0) return;
            amount *= Mathf.Max(1, Multiplier);
            Points += amount;
            TotalEarned += amount;
            Changed?.Invoke(Points, amount, reason);
        }

        public bool CanAfford(int amount) => Points >= amount;

        public bool TrySpend(int amount, string reason)
        {
            if (amount < 0 || Points < amount) return false;
            Points -= amount;
            Changed?.Invoke(Points, -amount, reason);
            return true;
        }
    }
}
