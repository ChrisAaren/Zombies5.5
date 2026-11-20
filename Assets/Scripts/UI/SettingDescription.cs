using UnityEngine;
using UnityEngine.EventSystems;

namespace Zombies55
{
    // Sits on a settings row: while the row is focused (gamepad, keyboard) or hovered (mouse) the menu shows its one-line description.
    public class SettingDescription : MonoBehaviour, ISelectHandler, IPointerEnterHandler
    {
        [SerializeField] string text;

        public void OnSelect(BaseEventData eventData) => SettingsMenu.Current?.ShowDescription(text);
        public void OnPointerEnter(PointerEventData eventData) => SettingsMenu.Current?.ShowDescription(text);
    }
}
