using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Zombies55
{
    // The one-line controls summary on the title screen, rebuilt from the current Primary bindings.
    [RequireComponent(typeof(TMP_Text))]
    public class ControlsHintText : MonoBehaviour
    {
        const string Empty = "—";
        TMP_Text _text;
        bool _subscribed;

        void OnEnable()
        {
            if (_text == null) _text = GetComponent<TMP_Text>();
            if (!_subscribed) { GameInput.BindingsChanged += Rebuild; _subscribed = true; }
            Rebuild();
        }

        void OnDisable() => Unsubscribe();
        void OnDestroy() => Unsubscribe();

        void Unsubscribe()
        {
            if (!_subscribed) return;
            GameInput.BindingsChanged -= Rebuild;
            _subscribed = false;
        }

        static string Key(GameAction a)
        {
            string s = GameInput.GetDisplayName(a, BindingSlot.Primary);
            return s == Empty ? null : s;
        }

        void Rebuild()
        {
            if (_text == null) return;
            var parts = new List<string>();

            var move = new List<string>();
            bool single = true;
            foreach (var a in new[] { GameAction.MoveForward, GameAction.MoveLeft, GameAction.MoveBack, GameAction.MoveRight })
            {
                string k = Key(a);
                if (k == null) continue;
                move.Add(k);
                if (k.Length != 1) single = false;
            }
            if (move.Count > 0) parts.Add((single ? string.Concat(move) : string.Join("/", move)) + " MOVE");

            Add(parts, GameAction.Sprint, "SPRINT");
            Add(parts, GameAction.Jump, "JUMP");
            Add(parts, GameAction.Fire, "FIRE");
            Add(parts, GameAction.Aim, "AIM");
            Add(parts, GameAction.Reload, "RELOAD");

            string w1 = Key(GameAction.Weapon1), w2 = Key(GameAction.Weapon2);
            if (w1 != null && w2 != null) parts.Add(w1 + "/" + w2 + " SWAP");
            else if (w1 != null || w2 != null) parts.Add((w1 ?? w2) + " SWAP");

            Add(parts, GameAction.Interact, "BUY");
            Add(parts, GameAction.Pause, "PAUSE");
            _text.text = string.Join(" · ", parts);
        }

        static void Add(List<string> parts, GameAction a, string verb)
        {
            string k = Key(a);
            if (k != null) parts.Add(k + " " + verb);
        }
    }
}
