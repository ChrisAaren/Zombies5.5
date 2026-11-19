using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Zombies55
{
    // A labelled slider bound to one float setting. The slider is the selectable, so it works with a gamepad.
    public class SettingsSliderRow : MonoBehaviour
    {
        [SerializeField] SettingId id;
        [SerializeField] Slider slider;
        [SerializeField] TMP_Text valueText;

        bool _wired;

        void Awake() => Wire();

        void OnEnable()
        {
            Wire();
            GameSettings.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => GameSettings.Changed -= Refresh;

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            slider.minValue = GameSettings.Min(id);
            slider.maxValue = GameSettings.Max(id);
            slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(v => GameSettings.SetFloat(id, v));
        }

        void Refresh()
        {
            slider.SetValueWithoutNotify(GameSettings.GetFloat(id));
            valueText.text = GameSettings.Format(id);
        }
    }

    // Previous / next chooser for an enum-like setting. Left and right move the choice; submit cycles forward.
    public class SettingsStepperRow : Selectable, ISubmitHandler
    {
        [SerializeField] SettingId id;
        [SerializeField] TMP_Text valueText;
        [SerializeField] Image swatch;   // optional, shows the crosshair color
        [SerializeField] Button prevButton;
        [SerializeField] Button nextButton;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!Application.isPlaying) return;
            GameSettings.Changed += Refresh;
            prevButton.onClick.AddListener(Prev);
            nextButton.onClick.AddListener(Next);
            Refresh();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!Application.isPlaying) return;
            GameSettings.Changed -= Refresh;
            prevButton.onClick.RemoveListener(Prev);
            nextButton.onClick.RemoveListener(Next);
        }

        void Prev() => Step(-1);
        void Next() => Step(1);

        void Step(int dir)
        {
            int count = GameSettings.OptionCount(id);
            GameSettings.SetInt(id, ((GameSettings.GetInt(id) + dir) % count + count) % count);
            GameAudio.Instance?.PlayClick();
        }

        void Refresh()
        {
            valueText.text = GameSettings.Format(id);
            if (swatch != null) swatch.color = GameSettings.CrosshairColor;
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left) { Step(-1); eventData.Use(); }
            else if (eventData.moveDir == MoveDirection.Right) { Step(1); eventData.Use(); }
            else base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData) => Step(1);
    }

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
