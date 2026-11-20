using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Zombies55
{
    // Keeps the focused row inside a scrolling settings list (gamepad and keyboard navigation do not move the scroll rect by themselves).
    [RequireComponent(typeof(ScrollRect))]
    public class SettingsScrollFollow : MonoBehaviour
    {
        const float Pad = 14f;
        ScrollRect _scroll;
        GameObject _last;
        readonly Vector3[] _corners = new Vector3[4];

        void Awake() => _scroll = GetComponent<ScrollRect>();

        void OnEnable() => _last = null;

        void LateUpdate()
        {
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == _last) return;
            _last = sel;
            if (sel == null || !sel.transform.IsChildOf(_scroll.content)) return;

            var viewport = _scroll.viewport;
            var content = _scroll.content;
            var rt = (RectTransform)sel.transform;
            // The row is the selectable itself, or a child of it (the slider inside a slider row): scroll to the row-sized parent when there is one.
            if (rt.parent != content && rt.parent is RectTransform parent && parent.parent == content) rt = parent;
            rt.GetWorldCorners(_corners);
            float top = viewport.InverseTransformPoint(_corners[1]).y;
            float bottom = viewport.InverseTransformPoint(_corners[0]).y;
            var view = viewport.rect;
            Vector2 pos = content.anchoredPosition;
            if (bottom < view.yMin + Pad) pos.y += view.yMin + Pad - bottom;
            else if (top > view.yMax - Pad) pos.y -= top - (view.yMax - Pad);
            float max = Mathf.Max(0f, content.rect.height - view.height);
            pos.y = Mathf.Clamp(pos.y, 0f, max);
            content.anchoredPosition = pos;
        }
    }
}
