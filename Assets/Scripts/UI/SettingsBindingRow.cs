using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Zombies55
{
    // One row of the KEYBINDS tab: an action with three keycap buttons (keyboard / alternate / gamepad).
    // Click, Enter or A on a slot listens for the next key or button. While that runs, and until every key is released
    // afterwards, the EventSystem's input module is switched off so the press that binds the control never also clicks a button.
    public class SettingsBindingRow : MonoBehaviour
    {
        const float PromptPulseSeconds = 1.6f;   // same as GameShell's title prompt
        const float FlashSeconds = 0.6f;

        [SerializeField] GameAction action;
        [SerializeField] Button[] slotButtons;       // BindingSlot order
        [SerializeField] TMP_Text[] slotTexts;
        [SerializeField] Image[] slotImages;
        [SerializeField] string[] slotDescriptions;  // shown while the slot is focused or hovered

        static InputSystemUIInputModule s_module;    // the module this class switched off, if any

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_module = null;

        readonly string[] _shown = new string[3];
        readonly float[] _flashEnd = new float[3];
        int _rebindSlot = -1;
        bool _awaitRelease;
        bool _subscribed;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slot = i;
                slotButtons[i].onClick.AddListener(() => Begin(slot));
            }
            GameInput.BindingsChanged += OnBindingsChanged;
            _subscribed = true;
            System.Array.Clear(_flashEnd, 0, _flashEnd.Length);
            Refresh(false);
        }

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            if (_subscribed) { GameInput.BindingsChanged -= OnBindingsChanged; _subscribed = false; }
            for (int i = 0; i < slotButtons.Length; i++) slotButtons[i].onClick.RemoveAllListeners();
            // Closing settings or changing tab mid-rebind: stop listening and give the UI its input back.
            if (_rebindSlot >= 0) GameInput.CancelRebind();
            _rebindSlot = -1;
            _awaitRelease = false;
            RestoreModule();
        }

        void Begin(int slot)
        {
            if (GameInput.IsRebinding || GameInput.MenuInputBlocked || _rebindSlot >= 0) return;
            var bs = (BindingSlot)slot;
            if (!GameInput.IsRebindable(action, bs)) return;

            _rebindSlot = slot;
            _awaitRelease = false;
            DisableModule();
            SettingsMenu.Current?.ShowDescription(bs == BindingSlot.Gamepad
                ? "Press a gamepad button · START or ESC cancels"
                : "Press a key or mouse button to bind · ESC cancels");
            UpdateTexts();
            GameInput.StartRebind(action, bs, ok => OnRebindDone(slot));
        }

        void OnRebindDone(int slot)
        {
            _rebindSlot = -1;
            _awaitRelease = true;   // Update re-enables the UI once every key is up
            UpdateTexts();
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (_rebindSlot >= 0)
            {
                float wave = (Mathf.Sin(now * Mathf.PI * 2f / PromptPulseSeconds) + 1f) * 0.5f;
                slotTexts[_rebindSlot].alpha = Mathf.Lerp(0.45f, 1f, wave);
            }
            else if (_awaitRelease && !GameInput.MenuInputBlocked)
            {
                _awaitRelease = false;
                RestoreModule();
                int slot = LastSlotFocus();
                var es = EventSystem.current;
                if (es != null) es.SetSelectedGameObject(slotButtons[slot].gameObject);
                SettingsMenu.Current?.ShowDescription(slotDescriptions[slot]);
            }

            for (int i = 0; i < slotImages.Length; i++)
            {
                if (_flashEnd[i] <= 0f) continue;
                float k = Mathf.Clamp01((_flashEnd[i] - now) / FlashSeconds);
                slotImages[i].color = Color.Lerp(Color.white, Palette.Accent, k);
                if (k <= 0f) _flashEnd[i] = 0f;
            }
        }

        int _lastSlot;
        int LastSlotFocus() => _lastSlot;

        void DisableModule()
        {
            var es = EventSystem.current;
            var module = es != null ? es.GetComponent<InputSystemUIInputModule>() : null;
            if (module == null || !module.enabled) return;
            module.enabled = false;
            s_module = module;
        }

        static void RestoreModule()
        {
            if (s_module != null) s_module.enabled = true;
            s_module = null;
        }

        void OnBindingsChanged() => Refresh(true);

        void Refresh(bool flash)
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < slotButtons.Length; i++)
            {
                string text = GameInput.GetDisplayName(action, (BindingSlot)i);
                // The slot the player just set is not flashed; the one a conflict moved a key to is.
                if (flash && _shown[i] != null && _shown[i] != text && i != _rebindSlot) _flashEnd[i] = now + FlashSeconds;
                _shown[i] = text;
            }
            UpdateTexts();
        }

        void UpdateTexts()
        {
            for (int i = 0; i < slotButtons.Length; i++)
            {
                var bs = (BindingSlot)i;
                bool locked = !GameInput.IsRebindable(action, bs);
                slotButtons[i].interactable = !locked;
                var t = slotTexts[i];
                if (i == _rebindSlot)
                {
                    _lastSlot = i;
                    t.text = bs == BindingSlot.Gamepad ? "PRESS A BUTTON" : "PRESS A KEY";
                    t.color = Palette.UiDark;
                }
                else
                {
                    t.text = _shown[i] ?? GameInput.GetDisplayName(action, bs);
                    t.color = locked ? Palette.WithAlpha(Palette.UiLight, 0.5f) : Palette.UiDark;
                }
            }
        }

        // The focused slot is remembered by selection so it can be re-selected after a rebind.
        void LateUpdate()
        {
            var es = EventSystem.current;
            if (es == null || _rebindSlot >= 0) return;
            var sel = es.currentSelectedGameObject;
            if (sel == null) return;
            for (int i = 0; i < slotButtons.Length; i++)
                if (slotButtons[i].gameObject == sel) { _lastSlot = i; return; }
        }
    }
}
