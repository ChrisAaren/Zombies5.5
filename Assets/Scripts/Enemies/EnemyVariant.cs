using System;
using UnityEngine;

namespace Zombies55
{
    // Gives every pooled Hollow its own look each time it spawns: skin tone, jacket and trousers, hood up or down (hair shows).
    // Materials and renderer slots are assigned by the scene builder.
    public class EnemyVariant : MonoBehaviour
    {
        [Serializable]
        public struct Look
        {
            public Material skin, top, pants, hair;
            public bool hood;
            public float weight;
        }

        [SerializeField] Look[] looks;
        [SerializeField] SkinnedMeshRenderer skinRenderer, clothesRenderer, hairRenderer;
        [SerializeField] int skinSlot, topSlot, pantsSlot, hairSlot;
        [SerializeField] GameObject hood;

        void OnEnable()
        {
            if (looks == null || looks.Length == 0) return;
            float total = 0f;
            foreach (var l in looks) total += l.weight;
            float q = UnityEngine.Random.value * total;
            int pick = 0;
            for (int i = 0; i < looks.Length; i++) { q -= looks[i].weight; if (q <= 0f) { pick = i; break; } }
            var look = looks[pick];
            Assign(skinRenderer, skinSlot, look.skin);
            Assign(clothesRenderer, topSlot, look.top);
            Assign(clothesRenderer, pantsSlot, look.pants);
            if (hood != null) hood.SetActive(look.hood);
            if (hairRenderer != null)
            {
                hairRenderer.enabled = !look.hood;
                if (!look.hood) Assign(hairRenderer, hairSlot, look.hair);
            }
        }

        static void Assign(Renderer r, int slot, Material m)
        {
            if (r == null || m == null) return;
            var mats = r.sharedMaterials;
            if (slot < 0 || slot >= mats.Length || mats[slot] == m) return;
            mats[slot] = m;
            r.sharedMaterials = mats;
        }
    }
}
