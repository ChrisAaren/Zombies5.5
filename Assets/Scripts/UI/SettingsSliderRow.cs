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
}
