using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Zombies55.EditorTools
{
    // Small-scale street noise: procedural posters and flyers (fictional events and bands), abstract tags, stickers on poles,
    // weed and grass tufts along curbs, wall bases, slab joints and tree pits, and cigarette-butt and paper litter in doorways.
    // Everything is a non-colliding, shadowless decal or card, merged into one mesh per block. Own RNG: the base layout keeps its seed.
    public static partial class SceneBuilder
    {
        static System.Random _noise;
        static float Nf(float a, float b) { return a + (float)_noise.NextDouble() * (b - a); }

        // ------------------------------------------------------------------ procedural textures

        // 3 x 5 pixel font: rows of three bits, top to bottom.
        static readonly Dictionary<char, string> Font3x5 = new Dictionary<char, string>
        {
            {'A',"010101111101101"},{'B',"110101110101110"},{'C',"011100100100011"},{'D',"110101101101110"},{'E',"111100110100111"},
            {'F',"111100110100100"},{'G',"011100101101011"},{'H',"101101111101101"},{'I',"111010010010111"},{'J',"001001001101010"},
            {'K',"101101110101101"},{'L',"100100100100111"},{'M',"101111111101101"},{'N',"110101101101101"},{'O',"010101101101010"},
            {'P',"110101110100100"},{'Q',"010101101111011"},{'R',"110101110101101"},{'S',"011100010001110"},{'T',"111010010010010"},
            {'U',"101101101101111"},{'V',"101101101101010"},{'W',"101101111111101"},{'X',"101101010101101"},{'Y',"101101010010010"},
            {'Z',"111001010100111"},
            {'0',"111101101101111"},{'1',"010110010010111"},{'2',"110001010100111"},{'3',"110001010001110"},{'4',"101101111001001"},
            {'5',"111100110001110"},{'6',"011100111101111"},{'7',"111001010010010"},{'8',"111101111101111"},{'9',"111101111001110"},
            {'.',"000000000000010"},{'-',"000000111000000"},{':',"000010000010000"},{'/',"001001010100100"},{'&',"010101010101011"},
            {'!',"010010010000010"},{' ',"000000000000000"},
        };

        class PixCanvas
        {
            public readonly int W, H;
            public readonly Color[] P;
            public PixCanvas(int w, int h, Color bleed)
            {
                W = w; H = h; P = new Color[w * h];
                var c = bleed; c.a = 0f;
                for (int i = 0; i < P.Length; i++) P[i] = c;
            }
            public void Over(int x, int y, Color s, float cov = 1f)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return;
                float a = s.a * cov;
                if (a <= 0f) return;
                var d = P[y * W + x];
                float oa = a + d.a * (1f - a);
                float k = d.a * (1f - a);
                P[y * W + x] = new Color((s.r * a + d.r * k) / oa, (s.g * a + d.g * k) / oa, (s.b * a + d.b * k) / oa, oa);
            }
            public void Rect(int x0, int y0, int x1, int y1, Color c)
            {
                for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) Over(x, y, c);
            }
            public void Disk(float cx, float cy, float r, Color c)
            {
                int x0 = Mathf.FloorToInt(cx - r - 1), x1 = Mathf.CeilToInt(cx + r + 1), y0 = Mathf.FloorToInt(cy - r - 1), y1 = Mathf.CeilToInt(cy + r + 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        float cov = Mathf.Clamp01(r - d + 0.5f);
                        if (cov > 0f) Over(x, y, c, cov);
                    }
            }
            // y grows upward (texture space).
            public void Text(int x, int y, string s, int sc, Color c)
            {
                foreach (char ch in s)
                {
                    if (Font3x5.TryGetValue(ch, out var g))
                        for (int r = 0; r < 5; r++)
                            for (int cc = 0; cc < 3; cc++)
                                if (g[r * 3 + cc] == '1') Rect(x + cc * sc, y + (4 - r) * sc, x + (cc + 1) * sc, y + (5 - r) * sc, c);
                    x += 4 * sc;
                }
            }
            public static int TextW(string s, int sc) { return s.Length * 4 * sc - sc; }
        }

        static Vector2 CatmullRom(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (-a + 3f * b - 3f * c + d) * t3);
        }

        static void Stroke(PixCanvas cv, List<Vector2> pts, float r, Color col)
        {
            for (int i = 1; i + 2 < pts.Count; i++)
                for (int s = 0; s < 24; s++)
                {
                    var p = CatmullRom(pts[i - 1], pts[i], pts[i + 1], pts[i + 2], s / 24f);
                    cv.Disk(p.x, p.y, r, col);
                }
        }

        static void Poster(PixCanvas cv, int ox, int oy, string t1, string t2, string l1, string l2, string l3, Color paper, Color band, Color ink, int imageStyle)
        {
            int x0 = ox + 30, x1 = ox + 226, y0 = oy + 6, y1 = oy + 250;
            // An older flyer pasted underneath: it peeks out at the left and bottom edges and shows through the tears.
            {
                var older = Color.Lerp(paper, new Color(0.72f, 0.66f, 0.55f), 0.55f);
                int ux0 = x0 - 16, ux1 = x1 - 34, uy0 = y0, uy1 = y1 - 70;
                for (int y = uy0; y < uy1; y++)
                    for (int x = ux0; x < ux1; x++)
                    {
                        float e = Mathf.Min(Mathf.Min(x - ux0, ux1 - x), Mathf.Min(y - uy0, uy1 - y));
                        float w = (Mathf.PerlinNoise(x * 0.09f + oy, y * 0.09f + ox) - 0.5f) * 9f;
                        if (e + w < 1f) continue;
                        float stripe = (y - uy0) % 46 < 9 ? 0.78f : 1f;
                        cv.Over(x, y, new Color(older.r * stripe, older.g * stripe, older.b * stripe, 1f));
                    }
            }
            // paper with a torn, ragged edge, bitten corners and a peeled-off corner chunk
            int peel = 34 + (ox / 256 + oy / 128) % 3 * 12;
            bool peelLeft = ((ox / 256) & 1) == 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    float edge = Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(y - y0, y1 - y));
                    float wob = (Mathf.PerlinNoise(x * 0.11f + ox, y * 0.11f + oy) - 0.5f) * 8f + (Mathf.PerlinNoise(x * 0.4f + oy, y * 0.4f) - 0.5f) * 3.5f;
                    if (edge + wob < 1.5f) continue;
                    if (edge < 30f && Mathf.PerlinNoise(x * 0.06f + 3f + oy, y * 0.06f + ox) > 0.76f) continue;
                    int cx = peelLeft ? x - x0 : x1 - x, cy = y1 - y;
                    if (cx + cy < peel + (int)(wob * 2f)) continue;
                    float fade = 0.9f + 0.1f * Mathf.PerlinNoise(x * 0.05f, y * 0.05f + ox);
                    cv.Over(x, y, new Color(paper.r * fade, paper.g * fade, paper.b * fade, 1f));
                }
            cv.Rect(x0 + 4, y1 - 66, x1 - 4, y1 - 6, band);
            bool two = !string.IsNullOrEmpty(t2);
            int sc = Mathf.Max(2, Mathf.Min(two ? 3 : 5, (x1 - x0 - 24) / (Mathf.Max(t1.Length, 1) * 4 - 1)));
            var titleCol = Color.Lerp(Color.white, paper, 0.15f);
            cv.Text(x0 + 12, y1 - 10 - 5 * sc - (two ? 0 : 14), t1, sc, titleCol);
            if (two) cv.Text(x0 + 12, y1 - 10 - 5 * sc - 5 * 2 - 6, t2, 2, titleCol);
            // picture block
            int by0 = y1 - 164, by1 = y1 - 76;
            switch (imageStyle)
            {
                case 0:   // sun over bars
                    cv.Rect(x0 + 10, by0, x1 - 10, by1, Color.Lerp(band, paper, 0.55f));
                    cv.Disk((x0 + x1) * 0.5f, by0 + 52, 28, Color.Lerp(ink, paper, 0.2f));
                    for (int i = 0; i < 4; i++) cv.Rect(x0 + 10, by0 + i * 9, x1 - 10, by0 + i * 9 + 4, ink);
                    break;
                case 1:   // halftone dots
                    cv.Rect(x0 + 10, by0, x1 - 10, by1, Color.Lerp(paper, ink, 0.12f));
                    for (int yy = by0 + 6; yy < by1 - 3; yy += 8)
                        for (int xx = x0 + 14; xx < x1 - 12; xx += 8)
                        {
                            float rr = 1f + 3.2f * Mathf.PerlinNoise(xx * 0.03f + ox, yy * 0.03f);
                            cv.Disk(xx, yy, rr, ink);
                        }
                    break;
                case 2:   // diagonal stripes
                    cv.Rect(x0 + 10, by0, x1 - 10, by1, Color.Lerp(paper, band, 0.25f));
                    for (int xx = x0 - 90; xx < x1; xx += 22)
                        for (int yy = by0; yy < by1; yy++)
                        {
                            int sx = xx + (yy - by0);
                            if (sx >= x0 + 10 && sx < x1 - 10) cv.Rect(sx, yy, Mathf.Min(sx + 9, x1 - 10), yy + 1, ink);
                        }
                    break;
                default:  // photo-ish dark block with a bright circle
                    cv.Rect(x0 + 10, by0, x1 - 10, by1, Color.Lerp(ink, paper, 0.15f));
                    cv.Disk((x0 + x1) * 0.5f, by0 + 46, 30, Color.Lerp(paper, band, 0.35f));
                    cv.Rect(x0 + 10, by0, x1 - 10, by0 + 22, Color.Lerp(ink, Color.black, 0.5f));
                    break;
            }
            cv.Text(x0 + 12, y1 - 190, l1, 2, ink);
            cv.Text(x0 + 12, y1 - 204, l2, 2, ink);
            cv.Text(x0 + 12, y1 - 218, l3, 2, Color.Lerp(ink, paper, 0.35f));
            Weather(cv, ox, oy, x0 - 16, y0, x1, y1);
        }

        // Sun fade, water streaks, creases, scuffs and pin-holes: a flyer that has hung on a wall for a season.
        static void Weather(PixCanvas cv, int ox, int oy, int x0, int y0, int x1, int y1)
        {
            int foldY = y0 + 100 + (ox / 256) * 9 % 40, foldX = (x0 + x1) / 2 + (oy / 256) * 7 % 30 - 15;
            // a few irregular pin-holes per flyer (corners are likelier), never a regular pattern
            var holes = new List<Vector3>();
            var hr = new System.Random(ox * 7919 + oy * 104729 + x0 * 31 + y0);
            int nHoles = hr.Next(2, 5);
            for (int k = 0; k < nHoles; k++)
            {
                bool corner = hr.NextDouble() < 0.6;
                float hx = corner ? (hr.NextDouble() < 0.5 ? x0 + 3 + hr.Next(0, 10) : x1 - 3 - hr.Next(0, 10)) : hr.Next(x0, x1);
                float hy = corner ? (hr.NextDouble() < 0.5 ? y0 + 3 + hr.Next(0, 10) : y1 - 3 - hr.Next(0, 10)) : hr.Next(y0, y1);
                holes.Add(new Vector3(hx, hy, 1.2f + (float)hr.NextDouble() * 1.6f));
            }
            for (int y = y0; y < y1; y++)
                for (int x = Mathf.Max(x0, ox); x < Mathf.Min(x1, ox + 256); x++)
                {
                    var c = cv.P[y * cv.W + x];
                    if (c.a <= 0.01f) continue;
                    float up = Mathf.InverseLerp(y0, y1, y);
                    float n = Mathf.PerlinNoise(x * 0.03f + ox * 0.1f, y * 0.03f + oy * 0.1f);
                    float fadeAmt = Mathf.Clamp01(0.28f + 0.42f * n + 0.25f * up);
                    float lum = c.r * 0.3f + c.g * 0.55f + c.b * 0.15f;
                    var bleached = Color.Lerp(new Color(lum, lum, lum), new Color(0.78f, 0.74f, 0.66f), 0.45f);
                    var rgb = Color.Lerp(new Color(c.r, c.g, c.b), bleached, fadeAmt);
                    // vertical rain / damp streaks
                    float streak = Mathf.PerlinNoise(x * 0.22f + oy, y * 0.012f + ox);
                    if (streak > 0.64f) rgb *= Mathf.Lerp(1f, 0.72f, Mathf.Clamp01((streak - 0.64f) * 5f));
                    if (streak > 0.64f) rgb = Color.Lerp(rgb, rgb * new Color(0.95f, 0.88f, 0.75f), 0.5f);
                    // folds
                    if (Mathf.Abs(y - foldY) <= 1) rgb *= y == foldY ? 0.72f : 1.12f;
                    if (Mathf.Abs(x - foldX) <= 1) rgb *= x == foldX ? 0.8f : 1.08f;
                    // scuffs: paper fibre showing through ink
                    float scuff = Mathf.PerlinNoise(x * 0.35f + 9f, y * 0.35f + oy);
                    if (scuff > 0.74f) rgb = Color.Lerp(rgb, new Color(0.8f, 0.77f, 0.7f), 0.55f);
                    float a = c.a;
                    // pin-holes and rips
                    foreach (var h in holes)
                    {
                        float dx = x - h.x, dy = y - h.y;
                        if (dx * dx + dy * dy < h.z * h.z) a *= 0f;
                    }
                    cv.P[y * cv.W + x] = new Color(Mathf.Clamp01(rgb.r), Mathf.Clamp01(rgb.g), Mathf.Clamp01(rgb.b), a);
                }
        }

        static void WriteTextureAsset(string name, PixCanvas cv, bool clamp)
        {
            var tex = new Texture2D(cv.W, cv.H, TextureFormat.RGBA32, false);
            tex.SetPixels(cv.P);
            tex.Apply();
            string path = TextureDir + "/" + name + "_Albedo.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.alphaIsTransparency = true;
            ti.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.filterMode = FilterMode.Trilinear;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
            _tex.Remove(name + "_Albedo");
        }

        // 4 x 4 cells of 256 px: 0-7 posters, 8-11 tags, 12-15 stickers.
        static void BuildNoiseTextures()
        {
            var rnd = new System.Random(2718);
            var cv = new PixCanvas(1024, 1024, new Color(0.7f, 0.68f, 0.62f));
            var posters = new[]
            {
                new { a = "SALT FLATS", b = "LIVE", c = "FRI 14 NOV", d = "DOORS 9 PM", e = "THE LOW ROOM", paper = new Color(0.86f, 0.82f, 0.68f), band = new Color(0.62f, 0.14f, 0.1f), ink = new Color(0.12f, 0.1f, 0.1f), img = 0 },
                new { a = "VELVET MOTH", b = "TOUR", c = "SAT 22 NOV", d = "ALL AGES", e = "TICKETS 12", paper = new Color(0.9f, 0.88f, 0.82f), band = new Color(0.1f, 0.12f, 0.3f), ink = new Color(0.1f, 0.1f, 0.14f), img = 1 },
                new { a = "NIGHT MARKET", b = "8 PM", c = "EVERY FRIDAY", d = "FOOD + MUSIC", e = "UNDER THE BRIDGE", paper = new Color(0.9f, 0.78f, 0.45f), band = new Color(0.1f, 0.3f, 0.22f), ink = new Color(0.14f, 0.1f, 0.06f), img = 2 },
                new { a = "LOST CAT", b = "REWARD", c = "ANSWERS TO MOSS", d = "GREY TABBY", e = "CALL 555 0143", paper = new Color(0.94f, 0.94f, 0.92f), band = new Color(0.15f, 0.15f, 0.15f), ink = new Color(0.08f, 0.08f, 0.08f), img = 3 },
                new { a = "OPEN MIC", b = "TUESDAYS", c = "SIGN UP 7 PM", d = "NO COVER", e = "BRASS LANTERN", paper = new Color(0.82f, 0.88f, 0.9f), band = new Color(0.78f, 0.34f, 0.08f), ink = new Color(0.1f, 0.12f, 0.14f), img = 1 },
                new { a = "ROOF PARTY", b = "", c = "SUN 2 PM", d = "BRING A DISH", e = "NO NOISE AFTER 10", paper = new Color(0.93f, 0.8f, 0.82f), band = new Color(0.45f, 0.1f, 0.3f), ink = new Color(0.14f, 0.08f, 0.1f), img = 0 },
                new { a = "SUMMER SLAM", b = "22 JUL", c = "DJ KORVUS", d = "GLASS HARBOR", e = "18 PLUS", paper = new Color(0.78f, 0.9f, 0.78f), band = new Color(0.08f, 0.08f, 0.1f), ink = new Color(0.08f, 0.12f, 0.08f), img = 3 },
                new { a = "ROOM FOR RENT", b = "", c = "CLEAN QUIET", d = "CALL 555 0187", e = "ASK FOR DEE", paper = new Color(0.95f, 0.93f, 0.7f), band = new Color(0.2f, 0.2f, 0.22f), ink = new Color(0.1f, 0.1f, 0.1f), img = 2 },
            };
            for (int i = 0; i < 8; i++)
            {
                var p = posters[i];
                Poster(cv, (i % 4) * 256, 768 - (i / 4) * 256, p.a, p.b, p.c, p.d, p.e, p.paper, p.band, p.ink, p.img);
            }
            // tags and throw-ups: bright paint with a dark outline and drips
            var paints = new[] { new Color(0.9f, 0.85f, 0.2f), new Color(0.85f, 0.2f, 0.18f), new Color(0.15f, 0.7f, 0.85f), new Color(0.92f, 0.92f, 0.9f), new Color(0.2f, 0.75f, 0.35f) };
            for (int i = 0; i < 4; i++)
            {
                int ox = i * 256, oy = 256;          // tag row: texture y 256..511 (poster rows sit at 768 and 512, stickers at 0)
                var col = paints[rnd.Next(paints.Length)];
                var outline = new Color(0.07f, 0.07f, 0.08f);
                var pts = new List<Vector2>();
                if (i == 0)           // looping tag
                {
                    int n = 9;
                    for (int k = 0; k < n; k++)
                    {
                        float a = k * 1.1f;
                        pts.Add(new Vector2(ox + 30 + k * 24f + Mathf.Cos(a * 2f) * 18f, oy + 128 + Mathf.Sin(a * 3f) * 70f));
                    }
                }
                else if (i == 1)      // arrows and crossing bars
                {
                    for (int k = 0; k < 7; k++) pts.Add(new Vector2(ox + 20 + (k % 2 == 0 ? 20 : 220) + (float)rnd.NextDouble() * 10f, oy + 30 + k * 32f));
                }
                else if (i == 2)      // fat bubble blobs
                {
                    for (int k = 0; k < 4; k++)
                    {
                        float cx = ox + 50 + k * 52f, cy = oy + 128 + Mathf.Sin(k * 1.9f) * 28f;
                        cv.Disk(cx, cy, 34f, outline);
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        float cx = ox + 50 + k * 52f, cy = oy + 128 + Mathf.Sin(k * 1.9f) * 28f;
                        cv.Disk(cx, cy, 27f, col);
                        cv.Disk(cx - 9f, cy + 9f, 7f, Color.Lerp(col, Color.white, 0.6f));
                    }
                    pts = null;
                }
                else                  // spray circle, crosshair and dots
                {
                    for (int k = 0; k < 14; k++)
                    {
                        float a = k / 13f * 6.28f * 1.15f;
                        pts.Add(new Vector2(ox + 128 + Mathf.Cos(a) * 80f, oy + 128 + Mathf.Sin(a) * 80f));
                    }
                }
                if (pts != null)
                {
                    Stroke(cv, pts, 11f, outline);
                    Stroke(cv, pts, 7f, col);
                    if (i == 3)
                    {
                        var cross = new List<Vector2> { new Vector2(ox + 128, oy + 30), new Vector2(ox + 128, oy + 70), new Vector2(ox + 128, oy + 190), new Vector2(ox + 128, oy + 226) };
                        Stroke(cv, cross, 9f, outline); Stroke(cv, cross, 5f, col);
                    }
                }
                for (int d = 0; d < 5; d++)       // drips
                {
                    int dx = ox + 40 + rnd.Next(170), len = 18 + rnd.Next(52);
                    int dy = oy + 120 + rnd.Next(30) - 40;
                    for (int s = 0; s < len; s++) cv.Disk(dx, dy - s, 1.8f - s * 0.012f, col);
                    cv.Disk(dx, dy - len, 2.8f, col);
                }
            }
            // stickers: one round or square sticker per cell, with a white rim
            var stickerCols = new[] { new Color(0.8f, 0.18f, 0.15f), new Color(0.15f, 0.35f, 0.7f), new Color(0.9f, 0.75f, 0.15f), new Color(0.12f, 0.12f, 0.13f) };
            for (int i = 0; i < 4; i++)
            {
                int ox = i * 256, oy = 0;
                var c = stickerCols[i];
                if (i % 2 == 0)
                {
                    cv.Disk(ox + 128, oy + 128, 92, new Color(0.92f, 0.92f, 0.9f));
                    cv.Disk(ox + 128, oy + 128, 80, c);
                    cv.Text(ox + 128 - PixCanvas.TextW(i == 0 ? "OK" : "FREE", 8) / 2, oy + 108, i == 0 ? "OK" : "FREE", 8, new Color(0.93f, 0.9f, 0.8f));
                }
                else
                {
                    cv.Rect(ox + 36, oy + 70, ox + 220, oy + 186, new Color(0.92f, 0.92f, 0.9f));
                    cv.Rect(ox + 46, oy + 80, ox + 210, oy + 176, c);
                    string s = i == 1 ? "NO ADS" : "LOCAL";
                    cv.Text(ox + 128 - PixCanvas.TextW(s, 6) / 2, oy + 108, s, 6, new Color(0.93f, 0.9f, 0.8f));
                }
            }
            WriteTextureAsset("StreetNoise", cv, true);

            // grass and weed tufts: two cells side by side (fresh green, dry straw)
            var tv = new PixCanvas(512, 256, new Color(0.3f, 0.36f, 0.14f));
            for (int cell = 0; cell < 2; cell++)
            {
                int ox = cell * 256;
                int blades = 30;
                for (int b = 0; b < blades; b++)
                {
                    float ang = ((float)rnd.NextDouble() - 0.5f) * 1.5f;
                    float len = 70f + (float)rnd.NextDouble() * 150f;
                    float bend = ((float)rnd.NextDouble() - 0.5f) * 1.1f;
                    float bx = ox + 128 + ((float)rnd.NextDouble() - 0.5f) * 40f;
                    Color baseC = cell == 0 ? new Color(0.22f, 0.3f, 0.1f) : new Color(0.5f, 0.42f, 0.2f);
                    Color tipC = cell == 0 ? new Color(0.5f, 0.55f, 0.22f) : new Color(0.72f, 0.64f, 0.36f);
                    int steps = 40;
                    for (int s = 0; s < steps; s++)
                    {
                        float t = s / (float)(steps - 1);
                        float a = ang + bend * t * t;
                        float x = bx + Mathf.Sin(a) * len * t;
                        float y = 4f + Mathf.Cos(a) * len * t;
                        float r = Mathf.Lerp(4.2f, 0.7f, t);
                        var c = Color.Lerp(baseC, tipC, t); c.a = 1f;
                        float jitter = 0.85f + 0.3f * (float)rnd.NextDouble();
                        c = new Color(c.r * jitter, c.g * jitter, c.b * jitter, 1f);
                        tv.Disk(x, y, r, c);
                    }
                }
                if (cell == 0)     // a few small broadleaf weeds
                    for (int l = 0; l < 5; l++)
                    {
                        float a = -0.9f + l * 0.45f;
                        float cx = ox + 128 + Mathf.Sin(a) * 34f, cy = 40f + Mathf.Cos(a) * 30f;
                        for (int s = 0; s < 14; s++) tv.Disk(cx + Mathf.Sin(a) * s * 1.2f, cy + s * 1.6f, 7f - s * 0.38f, new Color(0.2f, 0.4f, 0.14f, 1f));
                    }
            }
            WriteTextureAsset("StreetTuft", tv, true);
        }

        // ------------------------------------------------------------------ mesh accumulation

        class NAcc
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> I = new List<int>();
        }

        static Vector2Int BlockKey(Vector3 p) { return new Vector2Int(Mathf.RoundToInt(p.x / 38f), Mathf.RoundToInt(p.z / 38f)); }

        static NAcc Chunk(Dictionary<Vector2Int, NAcc> d, Vector3 p)
        {
            var k = BlockKey(p);
            if (!d.TryGetValue(k, out var a)) d[k] = a = new NAcc();
            return a;
        }

        // Quad with explicit axes; cell selects a 4 x 4 atlas cell (or an arbitrary uv rect).
        static void AddQuad(NAcc a, Vector3 c, Vector3 right, Vector3 up, float w, float h, Vector3 normal, Vector4 uvRect)
        {
            int b = a.V.Count;
            a.V.Add(c - right * (w * 0.5f) - up * (h * 0.5f));
            a.V.Add(c - right * (w * 0.5f) + up * (h * 0.5f));
            a.V.Add(c + right * (w * 0.5f) + up * (h * 0.5f));
            a.V.Add(c + right * (w * 0.5f) - up * (h * 0.5f));
            for (int i = 0; i < 4; i++) a.N.Add(normal);
            a.UV.Add(new Vector2(uvRect.x, uvRect.y)); a.UV.Add(new Vector2(uvRect.x, uvRect.w));
            a.UV.Add(new Vector2(uvRect.z, uvRect.w)); a.UV.Add(new Vector2(uvRect.z, uvRect.y));
            a.I.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }

        static Vector4 AtlasCell(int cell)
        {
            const float e = 0.002f;
            float u0 = (cell % 4) * 0.25f + e, v0 = (3 - cell / 4) * 0.25f + e;
            return new Vector4(u0, v0, u0 + 0.25f - 2f * e, v0 + 0.25f - 2f * e);
        }

        static void WallDecal(NAcc a, Vector3 hitPoint, Vector3 nrm, float w, float h, int cell, float roll)
        {
            var right = Vector3.Cross(Vector3.up, nrm).normalized;
            var up = Vector3.up;
            var q = Quaternion.AngleAxis(roll, nrm);
            AddQuad(a, hitPoint + nrm * 0.03f, q * right, q * up, w, h, nrm, AtlasCell(cell));
        }

        static void SaveNoiseMeshes(Dictionary<Vector2Int, NAcc> d, string prefix, Material mat, Transform parent)
        {
            EnsureFolder(GeneratedDir);
            foreach (var kv in d)
            {
                var a = kv.Value;
                if (a.V.Count == 0) continue;
                string name = prefix + "_" + kv.Key.x + "_" + kv.Key.y;
                string path = GeneratedDir + "/" + name + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
                mesh.Clear();
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(a.V);
                mesh.SetNormals(a.N);
                mesh.SetUVs(0, a.UV);
                mesh.SetTriangles(a.I, 0);
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
        }

        // ------------------------------------------------------------------ placement

        static bool WallAt(Vector3 from, Vector3 dirIn, float reach, out RaycastHit hit)
        {
            hit = default;
            if (!Physics.Raycast(from, dirIn, out hit, reach, ~0, QueryTriggerInteraction.Ignore)) return false;
            return hit.collider.name == "Building" && Mathf.Abs(hit.normal.y) < 0.1f;
        }

        // Street-facing wall point and outward normal in front of a facade piece (its +Z may point either way).
        static bool PieceWall(Transform tr, out RaycastHit wh)
        {
            var fwd = tr.forward; fwd.y = 0f; fwd.Normalize();
            if (!WallAt(tr.position + Vector3.up * 1.2f + fwd * 0.4f, -fwd, 2f, out _)) fwd = -fwd;
            return WallAt(tr.position + Vector3.up * 1.2f + fwd * 1.5f, -fwd, 4f, out wh);
        }

        static void BuildStreetNoise()
        {
            _noise = new System.Random(7711);
            BuildNoiseTextures();
            Physics.SyncTransforms();
            var root = Sub("StreetNoise");
            var wall = new Dictionary<Vector2Int, NAcc>();
            var tufts = new Dictionary<Vector2Int, NAcc>();
            var litter = new Dictionary<Vector2Int, NAcc>();
            int nPoster = 0, nTag = 0, nSticker = 0, nTuft = 0, nLitter = 0;

            // 1. Shutters and blank walls: for every street-facing sidewalk strip sample wall hits along the block face.
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {
                    if (Mathf.Approximately(cx, 0f) && Mathf.Approximately(cz, 0f)) continue;
                    foreach (var n in SideNormals)
                    {
                        var t = new Vector2(-n.y, n.x);
                        var dirIn = new Vector3(-n.x, 0f, -n.y);
                        int posters = 0;
                        for (int attempt = 0; attempt < 60 && posters < 7; attempt++)
                        {
                            var s2 = new Vector2(cx, cz) + n * 12.6f + t * Nf(-11.5f, 11.5f);
                            var o = new Vector3(s2.x, Nf(1.0f, 2.6f), s2.y);
                            if (!WallAt(o, dirIn, 4f, out var hit)) continue;
                            var nrm = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                            var a = Chunk(wall, hit.point);
                            float ph = Nf(0.42f, 0.8f);
                            WallDecal(a, hit.point, nrm, ph * 0.98f, ph, _noise.Next(8), Nf(-4f, 4f));
                            if (_noise.NextDouble() < 0.7)   // a second flyer pasted overlapping
                            {
                                var right = Vector3.Cross(Vector3.up, nrm);
                                WallDecal(a, hit.point + right * Nf(-0.35f, 0.35f) + Vector3.up * Nf(-0.15f, 0.2f), nrm, ph * 0.9f, ph * 0.92f, _noise.Next(8), Nf(-6f, 6f));
                                nPoster++;
                            }
                            posters++; nPoster++;
                        }
                    }
                }

            // 1b. Tags on closed shutters and blank ground-floor walls only (never over glass).
            foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            {
                bool shutter = tr.name == "V2_Facade_ShutterShop_Down", blank = tr.name == "V2_Side_Blank_Ground";
                if (!shutter && !blank) continue;
                if (_noise.NextDouble() > (shutter ? 0.5 : 0.4)) continue;
                if (!PieceWall(tr, out var wh)) continue;
                var nrm = new Vector3(wh.normal.x, 0f, wh.normal.z).normalized;
                var right = Vector3.Cross(Vector3.up, nrm);
                float sz = Nf(1.1f, 1.9f);
                var pt = new Vector3(tr.position.x, 0f, tr.position.z);
                pt = new Vector3(wh.point.x, 0f, wh.point.z) + right * Vector3.Dot(pt - new Vector3(wh.point.x, 0f, wh.point.z), right) + Vector3.up * Nf(0.9f, 1.6f);
                WallDecal(Chunk(wall, pt), pt, nrm, sz, sz, 8 + _noise.Next(4), Nf(-8f, 8f));
                nTag++;
            }

            // 2. Stickers at eye height on lamp and utility poles.
            foreach (var group in new[] { "Lamps", "UtilityPoles" })
            {
                var g = _props.Find(group);
                if (g == null) continue;
                foreach (Transform pole in g)
                {
                    int count = _noise.Next(1, 4);
                    for (int i = 0; i < count; i++)
                    {
                        float ang = Nf(0f, 360f);
                        var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                        float h = Nf(1.2f, 2.0f);
                        float sz = Nf(0.12f, 0.2f);
                        var pos = pole.position + Vector3.up * h + dir * 0.17f;
                        var right = Vector3.Cross(Vector3.up, dir);
                        AddQuad(Chunk(wall, pos), pos, right, Vector3.up, sz, sz, dir, AtlasCell(12 + _noise.Next(4)));
                        nSticker++;
                    }
                }
            }

            // 3. Tufts along curb edges, wall bases, slab joints and tree pits.
            Vector3 SnapJoint(Vector3 p, bool alongX)
            {
                const float slab = 1.5f;
                if (alongX) p.z = Mathf.Round(p.z / slab) * slab; else p.x = Mathf.Round(p.x / slab) * slab;
                return p;
            }
            bool TuftOk(Vector3 p) { return !NearSpawnCandidate(new Vector2(p.x, p.z), 2.2f) && !NearKiosk(new Vector2(p.x, p.z), 1.6f); }
            void Tuft(Vector3 p, float size)
            {
                var a = Chunk(tufts, p);
                float yaw = Nf(0f, 180f);
                int cell = _noise.NextDouble() < 0.65 ? 0 : 1;
                var uv = new Vector4(cell * 0.5f + 0.002f, 0.002f, cell * 0.5f + 0.498f, 0.998f);
                var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                var fwd = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.right;
                float w = size, h = size * Nf(0.7f, 1.1f);
                AddQuad(a, p + Vector3.up * (h * 0.5f - 0.01f), right, Vector3.up, w, h, Vector3.up, uv);
                AddQuad(a, p + Vector3.up * (h * 0.5f - 0.01f), fwd, Vector3.up, w, h, Vector3.up, uv);
                nTuft++;
            }
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {
                    if (Mathf.Approximately(cx, 0f) && Mathf.Approximately(cz, 0f)) continue;
                    foreach (var n in SideNormals)
                    {
                        var t = new Vector2(-n.y, n.x);
                        var dirIn = new Vector3(-n.x, 0f, -n.y);
                        for (int k = 0; k < 26; k++)
                        {
                            float along = Nf(-12.4f, 12.4f);
                            int kind = _noise.Next(10);
                            Vector3 p;
                            if (kind < 3)          // curb edge
                            {
                                var q = new Vector2(cx, cz) + n * Nf(12.72f, 12.92f) + t * along;
                                p = new Vector3(q.x, SidewalkTop, q.y);
                            }
                            else if (kind < 6)     // wall base
                            {
                                var q = new Vector2(cx, cz) + n * 12.6f + t * along;
                                if (!WallAt(new Vector3(q.x, SidewalkTop + 0.3f, q.y), dirIn, 4f, out var hit)) continue;
                                p = new Vector3(hit.point.x, SidewalkTop, hit.point.z) + new Vector3(hit.normal.x, 0f, hit.normal.z).normalized * Nf(0.06f, 0.2f);
                            }
                            else                   // slab joint across the walkway
                            {
                                var q = new Vector2(cx, cz) + n * Nf(10.9f, 12.6f) + t * along;
                                p = SnapJoint(new Vector3(q.x, SidewalkTop, q.y), n.x == 0f);
                            }
                            if (!TuftOk(p)) continue;
                            Tuft(p, kind < 6 ? Nf(0.28f, 0.55f) : Nf(0.15f, 0.3f));
                        }
                    }
                }
            var trees = _props.Find("Trees");
            if (trees != null)
                foreach (Transform tr in trees)
                {
                    if (tr.name != "TreePit") continue;
                    for (int k = 0; k < 5; k++)
                    {
                        float a = Nf(0f, 6.28f), r = Nf(0.55f, 0.85f);
                        Tuft(tr.position + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), Nf(0.25f, 0.5f));
                    }
                }

            // 4. Litter in doorways: butts, paper, wrappers and cups, laid flat in front of shop and service doors.
            var dirt = new Dictionary<Vector2Int, MeshAcc>();
            int[] cells = { 15, 15, 15, 8, 9, 11, 10, 7 };
            foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            {
                if (tr.name != "V2_Facade_ShopDoorway" && tr.name != "V2_Facade_ServiceDoor") continue;
                var fwd = tr.forward;
                fwd.y = 0f; fwd.Normalize();
                if (WallAt(tr.position + Vector3.up * 1.2f + fwd * 0.4f, -fwd, 2f, out var h0)) { }          // forward points at the street already
                else fwd = -fwd;                                                                          // flipped: the wall is on the other side
                var basePos = new Vector3(tr.position.x, SidewalkTop + 0.014f, tr.position.z);
                if (!WallAt(tr.position + Vector3.up * 1.2f + fwd * 1.5f, -fwd, 4f, out var wh)) continue;   // must be a street-facing building wall
                var wallPt = new Vector3(wh.point.x, SidewalkTop + 0.014f, wh.point.z);
                var wn = new Vector3(wh.normal.x, 0f, wh.normal.z).normalized;
                if (NearSpawnCandidate(new Vector2(wallPt.x, wallPt.z), 1.8f) || NearKiosk(new Vector2(wallPt.x, wallPt.z), 1.6f)) continue;
                var tang = new Vector3(wn.z, 0f, -wn.x);
                int count = _noise.Next(3, 7);
                var key = BlockKey(wallPt);
                if (!dirt.TryGetValue(key, out var acc)) dirt[key] = acc = new MeshAcc();
                for (int i = 0; i < count; i++)
                {
                    int cell = cells[_noise.Next(cells.Length)];
                    float sz = cell == 15 ? Nf(0.35f, 0.6f) : Nf(0.35f, 0.7f);
                    var p = wallPt + wn * Nf(0.2f, 1.5f) + tang * Nf(-1.2f, 1.2f);
                    AddMarkTile(acc, cell, p, Nf(0f, 360f), sz, sz);
                    nLitter++;
                }
            }

            var wallMat = DecalMat("StreetNoiseDecal", "StreetNoise", new Color(0.92f, 0.9f, 0.86f, 1f));
            wallMat.SetFloat("_Smoothness", 0.04f);
            SaveNoiseMeshes(wall, "NoiseDecals", wallMat, root);

            var tuftMat = DecalMat("StreetTuftCard", "StreetTuft", new Color(0.95f, 0.95f, 0.9f, 1f));
            tuftMat.SetFloat("_Cull", 0f);
            tuftMat.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(tuftMat);
            SaveNoiseMeshes(tufts, "NoiseTufts", tuftMat, root);

            var litterMat = DecalMat("StreetDirtV2", "StreetDirt", new Color(0.52f, 0.48f, 0.44f, 1f));
            foreach (var kv in dirt)
            {
                var mesh = SaveMarkMesh("DoorLitter_" + kv.Key.x + "_" + kv.Key.y, kv.Value);
                var go = new GameObject("DoorLitter_" + kv.Key.x + "_" + kv.Key.y);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = litterMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            Debug.Log("Noise: " + nPoster + " posters, " + nTag + " tags, " + nSticker + " stickers, " + nTuft + " tufts, " + nLitter + " litter tiles");
        }
    }
}
