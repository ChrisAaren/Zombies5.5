using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Zombies55
{
    // The settings screen: Controls / Crosshair / Audio tabs, reset and back. GameShell opens it, closes it
    // (it owns the Esc and B input so pause handling cannot fight it) and restores the previous screen.
    public class SettingsMenu : MonoBehaviour
    {
        [SerializeField] Button[] tabButtons;      // Controls, Crosshair, Audio
        [SerializeField] GameObject[] tabPanels;
        [SerializeField] Selectable[] firstSelectable; // focus target when a tab opens
        [SerializeField] Button resetButton;
        [SerializeField] Button backButton;
        [SerializeField] RectTransform card;

        public event Action BackRequested;
        public bool IsOpen => gameObject.activeSelf;

        int _tab;
        bool _wired;

        void Wire()
        {
            if (_wired) return;
            _wired = true;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                int index = i;
                tabButtons[i].onClick.AddListener(() => { GameAudio.Instance?.PlayClick(); SelectTab(index, false); });
            }
            resetButton.onClick.AddListener(() => { GameAudio.Instance?.PlayClick(); GameSettings.Reset(); });
            backButton.onClick.AddListener(() => { GameAudio.Instance?.PlayClick(); BackRequested?.Invoke(); });
        }

        public void Open()
        {
            Wire();
            gameObject.SetActive(true);
            SelectTab(0, true);
            StartCoroutine(Ease.Tween(0.2f, t => card.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, t), Ease.OutBack));
        }

        public void Close()
        {
            gameObject.SetActive(false);
            GameSettings.Save();
        }

        void SelectTab(int index, bool focus)
        {
            _tab = (index % tabPanels.Length + tabPanels.Length) % tabPanels.Length;
            for (int i = 0; i < tabPanels.Length; i++)
            {
                tabPanels[i].SetActive(i == _tab);
                var colors = tabButtons[i].colors;
                colors.normalColor = i == _tab ? Palette.Accent : Palette.CharacterPrimary;
                tabButtons[i].colors = colors;
            }
            if (focus && EventSystem.current != null && firstSelectable[_tab] != null)
                EventSystem.current.SetSelectedGameObject(firstSelectable[_tab].gameObject);
        }

        void Update()
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            if (pad.rightShoulder.wasPressedThisFrame) SelectTab(_tab + 1, true);
            else if (pad.leftShoulder.wasPressedThisFrame) SelectTab(_tab - 1, true);
        }
    }
}
