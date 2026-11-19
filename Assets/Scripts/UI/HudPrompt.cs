using System;
using UnityEngine;

namespace Zombies55
{
    // One context prompt line on the HUD ("Hold E to buy Wasp SMG [1000]").
    // Whoever showed it owns it; only the owner can clear it.
    public static class HudPrompt
    {
        static object s_Owner;

        public static string Text { get; private set; } = string.Empty;
        public static bool Affordable { get; private set; } = true;

        public static event Action Changed;

        public static void Show(object owner, string text, bool affordable = true)
        {
            s_Owner = owner;
            if (Text == text && Affordable == affordable) return;
            Text = text;
            Affordable = affordable;
            Changed?.Invoke();
        }

        public static void Clear(object owner)
        {
            if (s_Owner != owner) return;
            s_Owner = null;
            Text = string.Empty;
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Owner = null;
            Text = string.Empty;
            Affordable = true;
            Changed = null;
        }
    }
}
