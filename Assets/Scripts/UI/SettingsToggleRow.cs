using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Zombies55
{
    // On/off row. Click or submit toggles; left and right set off and on.
    public class SettingsToggleRow : Selectable, ISubmitHandler, IPointerClickHandler
    {
        [SerializeField] SettingId id;
        [SerializeField] TMP_Text valueText;
        [SerializeField] Image pill;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!Application.isPlaying) return;
            GameSettings.Changed += Refresh;
            Refresh();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!Application.isPlaying) return;
            GameSettings.Changed -= Refresh;
        }

        void Set(bool on)
        {
            if (GameSettings.GetBool(id) == on) return;
            GameSettings.SetBool(id, on);
            GameAudio.Instance?.PlayClick();
        }

        void Refresh()
        {
            bool on = GameSettings.GetBool(id);
            valueText.text = on ? "ON" : "OFF";
            valueText.color = on ? Palette.UiDark : Palette.UiLight;
            pill.color = on ? Palette.Accent : Palette.WithAlpha(Palette.Shadow, 0.9f);
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left) { Set(false); eventData.Use(); }
            else if (eventData.moveDir == MoveDirection.Right) { Set(true); eventData.Use(); }
            else base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData) => Set(!GameSettings.GetBool(id));
        public void OnPointerClick(PointerEventData eventData) => Set(!GameSettings.GetBool(id));
    }
}
