using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Zombies55
{
    public enum GameAction { MoveForward, MoveBack, MoveLeft, MoveRight, Sprint, Jump, Fire, Aim, Reload, Interact, Weapon1, Weapon2, NextWeapon, Pause }

    // Primary/Alternate hold keyboard or mouse controls, Gamepad holds a gamepad control.
    public enum BindingSlot { Primary, Alternate, Gamepad }

    // Every gameplay input, as rebindable actions. The map is built in code (no asset), so defaults live in one table here.
    // Each action is a Button with exactly three bindings; the binding index is the BindingSlot.
    public static class GameInput
    {
        const string PrefsKey = "Zombies55.Bindings";
        const int ActionCount = 14;
        const int SlotCount = 3;

        static InputActionMap _map;
        static InputAction[] _actions;
        static InputActionRebindingExtensions.RebindingOperation _op;
        static bool _lastPad;
        static bool _blockAfterRebind;

        public static event Action BindingsChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Domain reload may be off: drop the map, any running rebind and all subscribers.
            if (_op != null) { _op.Dispose(); _op = null; }
            if (_actions != null)
                foreach (var a in _actions) if (a != null) a.performed -= OnPerformed;
            if (_map != null) { _map.Disable(); _map.Dispose(); }
            _map = null;
            _actions = null;
            _lastPad = false;
            _blockAfterRebind = false;
            BindingsChanged = null;
        }

        // ------------------------------------------------------------------ reading

        public static Vector2 Move
        {
            get
            {
                EnsureInit();
                Vector2 v = Vector2.zero;
                if (IsPressed(GameAction.MoveForward)) v.y += 1f;
                if (IsPressed(GameAction.MoveBack)) v.y -= 1f;
                if (IsPressed(GameAction.MoveRight)) v.x += 1f;
                if (IsPressed(GameAction.MoveLeft)) v.x -= 1f;
                var pad = Gamepad.current;
                if (pad != null) v += pad.leftStick.ReadValue();
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        public static bool IsPressed(GameAction a) { EnsureInit(); return _actions[(int)a].IsPressed(); }

        public static bool WasPressedThisFrame(GameAction a) { EnsureInit(); return _actions[(int)a].WasPressedThisFrame(); }

        // ------------------------------------------------------------------ text

        public static string Label(GameAction a)
        {
            switch (a)
            {
                case GameAction.MoveForward: return "MOVE FORWARD";
                case GameAction.MoveBack: return "MOVE BACK";
                case GameAction.MoveLeft: return "MOVE LEFT";
                case GameAction.MoveRight: return "MOVE RIGHT";
                case GameAction.Sprint: return "SPRINT";
                case GameAction.Jump: return "JUMP";
                case GameAction.Fire: return "FIRE";
                case GameAction.Aim: return "AIM";
                case GameAction.Reload: return "RELOAD";
                case GameAction.Interact: return "BUY / OPEN GATE";
                case GameAction.Weapon1: return "WEAPON 1";
                case GameAction.Weapon2: return "WEAPON 2";
                case GameAction.NextWeapon: return "NEXT WEAPON";
                case GameAction.Pause: return "PAUSE";
            }
            return a.ToString().ToUpperInvariant();
        }

        public static string Description(GameAction a)
        {
            switch (a)
            {
                case GameAction.MoveForward: return "Run away from the camera. The gamepad left stick always moves too.";
                case GameAction.MoveBack: return "Run towards the camera. The gamepad left stick always moves too.";
                case GameAction.MoveLeft: return "Run to the left. The gamepad left stick always moves too.";
                case GameAction.MoveRight: return "Run to the right. The gamepad left stick always moves too.";
                case GameAction.Sprint: return "Hold to run faster. Aiming cancels it.";
                case GameAction.Jump: return "Jump over low obstacles.";
                case GameAction.Fire: return "Shoot the current weapon. Hold for automatic fire.";
                case GameAction.Aim: return "Hold to aim down the sights: slower, steadier shots.";
                case GameAction.Reload: return "Reload the current weapon.";
                case GameAction.Interact: return "Hold at a weapon terminal or barrier to buy.";
                case GameAction.Weapon1: return "Switch to the first weapon slot.";
                case GameAction.Weapon2: return "Switch to the second weapon slot.";
                case GameAction.NextWeapon: return "Cycle to the other weapon slot.";
                case GameAction.Pause: return "Pause or resume the game. Esc and Start always work as back.";
            }
            return "";
        }

        const string Empty = "—";

        // Short uppercase name of what a slot is bound to; an em dash when the slot is empty.
        public static string GetDisplayName(GameAction a, BindingSlot slot)
        {
            if (IsMove(a) && slot == BindingSlot.Gamepad) return "L-STICK";
            EnsureInit();
            string path = Effective(a, slot);
            return string.IsNullOrEmpty(path) ? Empty : DisplayFromPath(path);
        }

        public static bool IsRebindable(GameAction a, BindingSlot slot) => !IsLocked(a, slot);

        // For HUD prompts: names the control of the device the player last used.
        public static string PromptKey(GameAction a)
        {
            if (_lastPad)
            {
                string g = GetDisplayName(a, BindingSlot.Gamepad);
                if (g != Empty) return g;
            }
            string p = GetDisplayName(a, BindingSlot.Primary);
            if (p != Empty) return p;
            string alt = GetDisplayName(a, BindingSlot.Alternate);
            if (alt != Empty) return alt;
            return GetDisplayName(a, BindingSlot.Gamepad);
        }

        // ------------------------------------------------------------------ rebinding

        public static bool IsRebinding => _op != null;

        // True while listening, and after a rebind ended until every key / button is released, so the press that
        // finished or cancelled the rebind never also drives a menu (Esc closing settings, a shoulder switching tab).
        public static bool MenuInputBlocked
        {
            get
            {
                if (_op != null) return true;
                if (!_blockAfterRebind) return false;
                if (AnyInputHeld()) return true;
                _blockAfterRebind = false;
                return false;
            }
        }

        static bool AnyInputHeld()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed) return true;
            var m = Mouse.current;
            if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed
                || m.forwardButton.isPressed || m.backButton.isPressed)) return true;
            var pad = Gamepad.current;
            if (pad != null)
                foreach (var c in pad.allControls)
                    if (c is ButtonControl b && b.isPressed) return true;
            return false;
        }

        public static void CancelRebind()
        {
            if (_op != null) _op.Cancel();   // OnCancel finishes the rebind
        }

        public static void StartRebind(GameAction a, BindingSlot slot, Action<bool> onComplete)
        {
            EnsureInit();
            if (!IsRebindable(a, slot)) { onComplete?.Invoke(false); return; }
            CancelRebind();

            var action = _actions[(int)a];
            int index = (int)slot;
            bool gamepad = slot == BindingSlot.Gamepad;
            string chosen = null;
            action.Disable();   // a rebind needs the action disabled

            var op = action.PerformInteractiveRebinding(index)
                .WithTargetBinding(index)
                .OnApplyBinding((o, path) => chosen = path)   // SetBinding applies it, with conflict handling and saving
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(o => Finish(a, slot, chosen, onComplete))
                .OnCancel(o => Finish(a, slot, null, onComplete));

            // Only the right kind of control may land in a slot, never mouse movement or a raw stick axis.
            if (gamepad)
            {
                op.WithControlsHavingToMatchPath("<Gamepad>");
                op.WithControlsHavingToMatchPath("<Keyboard>/escape");   // lets Esc cancel as well (see OnPotentialMatch)
                foreach (var stick in new[] { "leftStick", "rightStick" })
                {
                    op.WithControlsExcluding("<Gamepad>/" + stick);
                    foreach (var axis in new[] { "x", "y", "up", "down", "left", "right" })
                        op.WithControlsExcluding("<Gamepad>/" + stick + "/" + axis);
                }
                op.WithControlsExcluding("<Gamepad>/dpad");
                op.WithCancelingThrough("<Gamepad>/start");
                op.OnPotentialMatch(o =>
                {
                    foreach (var c in o.candidates)
                        if (c is KeyControl k && k.keyCode == Key.Escape) { o.Cancel(); return; }
                });
            }
            else
            {
                op.WithControlsHavingToMatchPath("<Keyboard>");
                op.WithControlsHavingToMatchPath("<Mouse>");
                op.WithCancelingThrough("<Keyboard>/escape");
                foreach (var p in new[]
                {
                    "<Pointer>/position", "<Pointer>/delta", "<Pointer>/press", "<Pointer>/pressure", "<Pointer>/radius",
                    "<Pointer>/twist", "<Pointer>/tilt", "<Pointer>/pointerId", "<Pointer>/displayIndex",
                    "<Mouse>/scroll", "<Mouse>/scroll/x", "<Mouse>/scroll/y", "<Mouse>/clickCount",
                    "<Keyboard>/anyKey", "<Keyboard>/IMESelected", "<Touchscreen>/*",
                })
                    op.WithControlsExcluding(p);
            }

            _op = op;
            op.Start();
        }

        static void Finish(GameAction a, BindingSlot slot, string path, Action<bool> onComplete)
        {
            if (_op != null) { _op.Dispose(); _op = null; }
            bool bound = false;
            if (!string.IsNullOrEmpty(path))
            {
                SetBinding(a, slot, path);
                bound = string.Equals(Effective(a, slot), path, StringComparison.OrdinalIgnoreCase);
            }
            if (_map != null) _actions[(int)a].Enable();
            _blockAfterRebind = true;
            onComplete?.Invoke(bound);
        }

        // ------------------------------------------------------------------ bindings

        // Applies a path ("" clears the slot). If another slot of the same kind already uses it, that slot gets the old
        // path (a swap). Reload and Interact may share a control (the game picks by context); locked slots never change.
        public static void SetBinding(GameAction a, BindingSlot slot, string path)
        {
            EnsureInit();
            if (IsLocked(a, slot)) return;
            path = path ?? "";
            string old = Effective(a, slot);
            if (string.Equals(path, old, StringComparison.OrdinalIgnoreCase)) return;

            var swaps = new List<(GameAction, BindingSlot)>();
            if (path.Length > 0)
            {
                for (int i = 0; i < ActionCount; i++)
                    for (int s = 0; s < SlotCount; s++)
                    {
                        var oa = (GameAction)i;
                        var os = (BindingSlot)s;
                        if (oa == a && os == slot) continue;
                        if ((os == BindingSlot.Gamepad) != (slot == BindingSlot.Gamepad)) continue;
                        if (!string.Equals(Effective(oa, os), path, StringComparison.OrdinalIgnoreCase)) continue;
                        if (IsReloadInteractPair(a, oa)) continue;
                        if (IsLocked(oa, os)) return;   // a locked slot owns that control
                        swaps.Add((oa, os));
                    }
            }

            foreach (var (oa, os) in swaps) ApplyOverride(oa, os, old);
            ApplyOverride(a, slot, path);
            Save();
            BindingsChanged?.Invoke();
        }

        public static void ResetToDefaults()
        {
            EnsureInit();
            _map.RemoveAllBindingOverrides();
            Save();
            BindingsChanged?.Invoke();
        }

        static bool IsReloadInteractPair(GameAction a, GameAction b)
            => (a == GameAction.Reload && b == GameAction.Interact) || (a == GameAction.Interact && b == GameAction.Reload);

        static bool IsMove(GameAction a) => a <= GameAction.MoveRight;

        static bool IsLocked(GameAction a, BindingSlot slot)
            => (IsMove(a) && slot == BindingSlot.Gamepad)
            || (a == GameAction.Pause && (slot == BindingSlot.Primary || slot == BindingSlot.Gamepad));

        static string Effective(GameAction a, BindingSlot slot)
        {
            var b = _actions[(int)a].bindings[(int)slot];
            return b.overridePath ?? b.path ?? "";
        }

        static void ApplyOverride(GameAction a, BindingSlot slot, string path)
        {
            var action = _actions[(int)a];
            int index = (int)slot;
            // Back at the default: drop the override so saved JSON only lists real changes.
            if (string.Equals(path, action.bindings[index].path ?? "", StringComparison.OrdinalIgnoreCase))
                action.RemoveBindingOverride(index);
            else
                action.ApplyBindingOverride(index, path);
        }

        static void Save()
        {
            if (GameSettings.Ephemeral) return;
            PlayerPrefs.SetString(PrefsKey, _map.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ setup

        static string[] Defaults(GameAction a)
        {
            switch (a)
            {
                case GameAction.MoveForward: return new[] { "<Keyboard>/w", "<Keyboard>/upArrow", "" };
                case GameAction.MoveBack: return new[] { "<Keyboard>/s", "<Keyboard>/downArrow", "" };
                case GameAction.MoveLeft: return new[] { "<Keyboard>/a", "<Keyboard>/leftArrow", "" };
                case GameAction.MoveRight: return new[] { "<Keyboard>/d", "<Keyboard>/rightArrow", "" };
                case GameAction.Sprint: return new[] { "<Keyboard>/leftShift", "<Keyboard>/rightShift", "<Gamepad>/leftStickPress" };
                case GameAction.Jump: return new[] { "<Keyboard>/space", "", "<Gamepad>/buttonSouth" };
                case GameAction.Fire: return new[] { "<Mouse>/leftButton", "", "<Gamepad>/rightTrigger" };
                case GameAction.Aim: return new[] { "<Mouse>/rightButton", "", "<Gamepad>/leftTrigger" };
                case GameAction.Reload: return new[] { "<Keyboard>/r", "", "<Gamepad>/buttonWest" };
                case GameAction.Interact: return new[] { "<Keyboard>/e", "", "<Gamepad>/buttonWest" };
                case GameAction.Weapon1: return new[] { "<Keyboard>/1", "", "" };
                case GameAction.Weapon2: return new[] { "<Keyboard>/2", "", "" };
                case GameAction.NextWeapon: return new[] { "<Mouse>/scroll/up", "<Mouse>/scroll/down", "<Gamepad>/buttonNorth" };
                default: return new[] { "<Keyboard>/escape", "<Keyboard>/p", "<Gamepad>/start" };   // Pause
            }
        }

        static void EnsureInit()
        {
            if (_map != null) return;
            _map = new InputActionMap("Gameplay");
            _actions = new InputAction[ActionCount];
            for (int i = 0; i < ActionCount; i++)
            {
                var a = (GameAction)i;
                var action = _map.AddAction(a.ToString(), InputActionType.Button);
                var paths = Defaults(a);
                for (int s = 0; s < SlotCount; s++)
                {
                    // Fixed ids: saved overrides are matched to bindings by id, which must survive a restart.
                    var id = new Guid(0x5A4F4D42, (short)i, (short)s, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
                    action.AddBinding(new InputBinding { path = paths[s], id = id });
                }
                action.performed += OnPerformed;
                _actions[i] = action;
            }

            if (!GameSettings.Ephemeral)
            {
                string json = PlayerPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    try { _map.LoadBindingOverridesFromJson(json); }
                    catch (Exception e) { Debug.LogWarning("GameInput: ignoring unreadable saved bindings (" + e.Message + ")"); _map.RemoveAllBindingOverrides(); }
                    // Locked slots never carry an override, even from an edited save.
                    for (int i = 0; i < ActionCount; i++)
                        for (int s = 0; s < SlotCount; s++)
                            if (IsLocked((GameAction)i, (BindingSlot)s)) _actions[i].RemoveBindingOverride(s);
                }
            }
            _map.Enable();
        }

        static void OnPerformed(InputAction.CallbackContext ctx) => _lastPad = ctx.control.device is Gamepad;

        // ------------------------------------------------------------------ names

        static readonly Dictionary<string, string> ControlNames = new Dictionary<string, string>
        {
            { "leftShift", "L-SHIFT" }, { "rightShift", "R-SHIFT" }, { "leftCtrl", "L-CTRL" }, { "rightCtrl", "R-CTRL" },
            { "leftAlt", "L-ALT" }, { "rightAlt", "R-ALT" }, { "space", "SPACE" }, { "escape", "ESC" },
            { "upArrow", "UP" }, { "downArrow", "DOWN" }, { "leftArrow", "LEFT" }, { "rightArrow", "RIGHT" },
            { "enter", "ENTER" }, { "numpadEnter", "NUM ENTER" }, { "tab", "TAB" }, { "backspace", "BACKSPACE" },
            { "capsLock", "CAPS" }, { "leftButton", "LMB" }, { "rightButton", "RMB" }, { "middleButton", "MMB" },
            { "backButton", "MOUSE 4" }, { "forwardButton", "MOUSE 5" }, { "scroll/up", "WHEEL UP" }, { "scroll/down", "WHEEL DOWN" },
            { "buttonSouth", "A" }, { "buttonEast", "B" }, { "buttonWest", "X" }, { "buttonNorth", "Y" },
            { "leftShoulder", "LB" }, { "rightShoulder", "RB" }, { "leftTrigger", "LT" }, { "rightTrigger", "RT" },
            { "leftStickPress", "LS CLICK" }, { "rightStickPress", "RS CLICK" }, { "start", "START" }, { "select", "SELECT" },
            { "dpad/up", "D-PAD UP" }, { "dpad/down", "D-PAD DOWN" }, { "dpad/left", "D-PAD LEFT" }, { "dpad/right", "D-PAD RIGHT" },
        };

        static string DisplayFromPath(string path)
        {
            int i = path.IndexOf('/');
            string rel = i >= 0 ? path.Substring(i + 1) : path;
            if (ControlNames.TryGetValue(rel, out var name)) return name;
            if (rel.StartsWith("digit", StringComparison.Ordinal) && rel.Length == 6) return rel.Substring(5);
            if (rel.Length == 1) return rel.ToUpperInvariant();
            if (rel.StartsWith("numpad", StringComparison.Ordinal)) return "NUM " + rel.Substring(6).ToUpperInvariant();
            return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice).ToUpperInvariant();
        }
    }
}
