using UnityEngine;
using UnityEngine.UI;

namespace Zombies55
{
    // Frame-time line plot for the benchmark results: one or two series (this run solid, a compared run dashed) of ms per time slice,
    // with horizontal guide lines (for example 16.7 ms = 60 fps). Built as a mesh so it needs no texture.
    public class GraphicsGraph : MaskableGraphic
    {
        float[] _a, _b, _guides;
        float _yMax = 20f;
        Color _colorA = Palette.Accent, _colorB = Palette.WithAlpha(Palette.UiLight, 0.75f);
        Color[] _guideColors;

        public void Set(float[] a, float[] b, float[] guidesMs, Color[] guideColors)
        {
            _a = a; _b = b; _guides = guidesMs; _guideColors = guideColors;
            float max = 0f;
            if (a != null) foreach (var v in a) if (v > max) max = v;
            if (b != null) foreach (var v in b) if (v > max) max = v;
            // Cap so one hitch does not flatten the rest of the run: spikes run off the top.
            _yMax = Mathf.Clamp(max * 1.1f, 12f, 50f);
            SetVerticesDirty();
        }

        public float YMax => _yMax;

        static void Quad(VertexHelper vh, Vector2 p0, Vector2 p1, float thickness, Color c)
        {
            Vector2 d = (p1 - p0);
            if (d.sqrMagnitude < 1e-6f) d = Vector2.right;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (thickness * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(p0 - n, c, Vector2.zero); vh.AddVert(p0 + n, c, Vector2.zero);
            vh.AddVert(p1 + n, c, Vector2.zero); vh.AddVert(p1 - n, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        void Series(VertexHelper vh, float[] s, Rect r, Color c, float thickness, bool dashed)
        {
            if (s == null || s.Length < 2) return;
            float stepX = r.width / (s.Length - 1);
            Vector2 prev = Point(r, 0, s[0], stepX);
            for (int i = 1; i < s.Length; i++)
            {
                Vector2 cur = Point(r, i, s[i], stepX);
                if (!dashed || (i / 3) % 2 == 0) Quad(vh, prev, cur, thickness, c);
                prev = cur;
            }
        }

        Vector2 Point(Rect r, int i, float ms, float stepX) =>
            new Vector2(r.xMin + i * stepX, r.yMin + Mathf.Clamp01(ms / _yMax) * r.height);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            if (_guides != null)
                for (int g = 0; g < _guides.Length; g++)
                {
                    if (_guides[g] <= 0f || _guides[g] >= _yMax) continue;
                    float y = r.yMin + _guides[g] / _yMax * r.height;
                    var col = _guideColors != null && g < _guideColors.Length ? _guideColors[g] : Palette.WithAlpha(Palette.UiLight, 0.3f);
                    Quad(vh, new Vector2(r.xMin, y), new Vector2(r.xMax, y), 1.5f, col);
                }
            Series(vh, _b, r, _colorB, 2.5f, true);
            Series(vh, _a, r, _colorA, 3f, false);
        }
    }
}
