using UnityEngine;
using UnityEngine.UI;

namespace Zombies55
{
    // Draws the crosshair from GameSettings (style, color, size, gap, length, thickness, opacity, outline, dot).
    // The HUD drives SetSpread every frame; the settings preview sets previewPulse to demo the dynamic spread.
    // The parts are created at runtime, so the scene builder only needs this component on an empty rect.
    public class CrosshairView : MonoBehaviour
    {
        [SerializeField] bool previewPulse;

        // Parts: 0 up, 1 down, 2 left, 3 right (ticks), then dot and ring.
        Image[] _ticks;
        Image _dot, _ring;
        Outline[] _outlines;
        RectTransform _parts;
        Texture2D _ringTex;
        Sprite _ringSprite;
        int _ringKeyDiameter = -1, _ringKeyThickness = -1;
        float _ringBaseRadius = 1f;
        float _spread;
        bool _built;

        void OnEnable()
        {
            GameSettings.Changed += Apply;
            Apply();
        }

        void OnDisable() => GameSettings.Changed -= Apply;

        void OnDestroy()
        {
            if (_ringSprite != null) Destroy(_ringSprite);
            if (_ringTex != null) Destroy(_ringTex);
        }

        void Update()
        {
            if (!previewPulse) return;
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f);
            SetSpread(GameSettings.CrosshairDynamic ? wave * 9f : 0f);
        }

        // Extra gap in canvas units on top of the configured gap (negative values tighten it).
        public void SetSpread(float extra)
        {
            _spread = extra;
            Layout();
        }

        void Build()
        {
            if (_built) return;
            _built = true;

            _parts = new GameObject("Parts", typeof(RectTransform)).GetComponent<RectTransform>();
            _parts.SetParent(transform, false);
            _parts.SetAsFirstSibling(); // the hit marker stays on top
            _parts.anchorMin = _parts.anchorMax = new Vector2(0.5f, 0.5f);
            _parts.sizeDelta = Vector2.zero;

            _ticks = new Image[4];
            _outlines = new Outline[6];
            for (int i = 0; i < 4; i++) _ticks[i] = NewPart("Tick" + i, i);
            _dot = NewPart("Dot", 4);
            _ring = NewPart("Ring", 5);
        }

        Image NewPart(string name, int outlineIndex)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_parts, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            _outlines[outlineIndex] = outline;
            return img;
        }

        // Restyles everything from the current settings.
        public void Apply()
        {
            Build();

            var style = GameSettings.CrosshairStyle;
            bool ticks = style == CrosshairStyle.Classic || style == CrosshairStyle.CrossDot || style == CrosshairStyle.TShape;
            bool ring = style == CrosshairStyle.Circle || style == CrosshairStyle.CircleDot;
            bool dot = GameSettings.CrosshairDot || style == CrosshairStyle.CrossDot
                || style == CrosshairStyle.Dot || style == CrosshairStyle.CircleDot;

            float opacity = GameSettings.CrosshairOpacity;
            Color color = Palette.WithAlpha(GameSettings.CrosshairColor, opacity);
            Color outlineColor = Palette.WithAlpha(Palette.UiDark, 0.85f * opacity);
            bool outline = GameSettings.CrosshairOutline;

            for (int i = 0; i < 4; i++)
            {
                _ticks[i].gameObject.SetActive(ticks && !(style == CrosshairStyle.TShape && i == 0));
                _ticks[i].color = color;
            }
            _dot.gameObject.SetActive(dot);
            _dot.color = color;
            _ring.gameObject.SetActive(ring);
            _ring.color = color;

            foreach (var o in _outlines)
            {
                o.enabled = outline;
                o.effectColor = outlineColor;
            }

            if (ring) RebuildRing();
            Layout();
        }

        void Layout()
        {
            if (!_built) return;

            float size = GameSettings.CrosshairSize;
            float gap = Mathf.Max(0f, GameSettings.CrosshairGap * size + _spread);
            float length = GameSettings.CrosshairLength * size;
            float thick = Mathf.Max(1f, GameSettings.CrosshairThickness * size);
            var style = GameSettings.CrosshairStyle;

            float d = gap + length * 0.5f;
            SetRect(_ticks[0], new Vector2(0f, d), new Vector2(thick, length));
            SetRect(_ticks[1], new Vector2(0f, -d), new Vector2(thick, length));
            SetRect(_ticks[2], new Vector2(-d, 0f), new Vector2(length, thick));
            SetRect(_ticks[3], new Vector2(d, 0f), new Vector2(length, thick));

            float dotSize = style == CrosshairStyle.Dot ? thick * 1.6f + 2f : thick + 1f;
            SetRect(_dot, Vector2.zero, new Vector2(dotSize, dotSize));

            if (_ring.gameObject.activeSelf && _ringSprite != null)
            {
                float radius = gap + 6f * size;
                float s = radius / _ringBaseRadius;
                _ring.rectTransform.anchoredPosition = Vector2.zero;
                _ring.rectTransform.sizeDelta = new Vector2(_ringTex.width, _ringTex.height);
                _ring.rectTransform.localScale = new Vector3(s, s, 1f);
            }
        }

        static void SetRect(Image img, Vector2 pos, Vector2 size)
        {
            img.rectTransform.anchoredPosition = pos;
            img.rectTransform.sizeDelta = size;
        }

        // The ring is a small generated texture; it is regenerated only when its radius or thickness changes.
        void RebuildRing()
        {
            float size = GameSettings.CrosshairSize;
            float radius = Mathf.Max(3f, GameSettings.CrosshairGap * size + 6f * size);
            float thick = Mathf.Max(1f, GameSettings.CrosshairThickness * size);
            int keyD = Mathf.RoundToInt(radius * 2f);
            int keyT = Mathf.RoundToInt(thick * 4f);
            if (_ringSprite != null && keyD == _ringKeyDiameter && keyT == _ringKeyThickness) return;

            _ringKeyDiameter = keyD;
            _ringKeyThickness = keyT;
            _ringBaseRadius = keyD * 0.5f;

            float r = _ringBaseRadius;
            float t = keyT * 0.25f;
            int px = Mathf.CeilToInt((r + t * 0.5f) * 2f) + 4;
            var pixels = new Color32[px * px];
            float c = px * 0.5f;
            for (int y = 0; y < px; y++)
            {
                for (int x = 0; x < px; x++)
                {
                    float dist = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(0.5f - (Mathf.Abs(dist - r) - t * 0.5f));
                    pixels[y * px + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            if (_ringSprite != null) Destroy(_ringSprite);
            if (_ringTex != null) Destroy(_ringTex);
            _ringTex = new Texture2D(px, px, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _ringTex.SetPixels32(pixels);
            _ringTex.Apply();
            _ringSprite = Sprite.Create(_ringTex, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 1f);
            _ringSprite.hideFlags = HideFlags.HideAndDontSave;
            _ring.sprite = _ringSprite;
        }
    }
}
