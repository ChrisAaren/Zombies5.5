using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Zombies55
{
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
            if (!GameSettings.IsEnabled(id)) return;
            int count = GameSettings.OptionCount(id);
            GameSettings.SetInt(id, ((GameSettings.GetInt(id) + dir) % count + count) % count);
            GameAudio.Instance?.PlayClick();
        }

        void Refresh()
        {
            valueText.text = GameSettings.Format(id);
            bool on = GameSettings.IsEnabled(id);
            interactable = on;
            prevButton.interactable = on;
            nextButton.interactable = on;
            valueText.alpha = on ? 1f : 0.4f;
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
}
