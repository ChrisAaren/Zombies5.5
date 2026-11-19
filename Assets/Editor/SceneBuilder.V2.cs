using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // V2 art pass: slot materials for the new kits (brick walls, reflective glass over the window-interior atlas, leaf cards,
    // car paint with dirt, traffic signals) and the V2 facade-kit building generator.
    public static partial class SceneBuilder
    {
        static System.Random _v2;

        // ------------------------------------------------------------------ materials

        static void MakeTransparent(Material m, bool premultiply)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", premultiply ? 2f : 0f);
            m.SetFloat("_SrcBlend", premultiply ? 1f : 5f);
            m.SetFloat("_DstBlend", 10f);
            m.SetFloat("_SrcBlendAlpha", 1f);
            m.SetFloat("_DstBlendAlpha", 10f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (premultiply) m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); else m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static Shader _weatherShader;

        // URP-Lit-compatible material on the WorldWeather shader: world-space facade grime (drips, ground soot, rust bleed)
        // on vertical faces and, optionally, varied paving slabs with joints, stains and gum on up-facing ones.
        static Material SurfW(string name, Color color, string tex, Vector2 tiling, float smooth, float grime,
            float slabs = 0f, float ground = 3f, float rust = 0.3f)
        {
            var m = Surf(name, color, tex, tiling, smooth);
            if (_weatherShader == null) _weatherShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Shaders/WorldWeather/WorldWeather.shader");
            if (_weatherShader == null) throw new InvalidOperationException("Shader 'Zombies55/WorldWeather' not found.");
            m.shader = _weatherShader;
            m.SetTexture("_GrimeMask", Tex("FacadeGrime_Mask"));
            m.SetColor("_GrimeColor", new Color(0.11f, 0.095f, 0.08f, 1f));
            m.SetFloat("_GrimeAmount", grime);
            m.SetFloat("_GrimeScale", 0.2f);
            m.SetFloat("_GroundGrime", ground);
            m.SetFloat("_SlabMode", slabs);
            m.SetFloat("_SlabSize", 1.5f);
            m.SetFloat("_RustAmount", rust);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material WallMat(Look l)
        {
            string t = l.WallTex;
            if (t == "BrickRed" || t == "BrickBrown" || t == "BrickTan")
                return SurfW("S_Wall_" + t + "_" + Hx(l.Wall), l.Wall, t, new Vector2(2f / 1.5f, 2f / 1.6f), 0.3f, 1.35f);
            return SurfW("S_Wall_" + t + "_" + Hx(l.Wall), l.Wall, t, new Vector2(0.5f, 0.5f), 0.3f, 1.0f);
        }

        // The plain shell of a skyline building: the same brick (or plaster) as the face, tiled per metre across the box, so a
        // distant side wall reads as masonry instead of a flat colour.
        static Material RingShellMat(Look l, float w, float d, float h)
        {
            bool brick = l.WallTex == "BrickRed" || l.WallTex == "BrickBrown" || l.WallTex == "BrickTan";
            var tiling = brick ? new Vector2((w + d) * 0.5f * 2f / 1.5f, h * 2f / 1.6f) : new Vector2((w + d) * 0.25f, h * 0.25f);
            string key = "S_RingShell_" + l.WallTex + "_" + Hx(l.Wall) + "_" + Mathf.RoundToInt(w) + "x" + Mathf.RoundToInt(d) + "x" + Mathf.RoundToInt(h);
            return SurfW(key, l.Wall * 0.92f, l.WallTex, tiling, 0.2f, brick ? 1.35f : 1.0f);
        }

        // Window glass: reflective, a touch metallic, see-through enough to show the interior card behind it.
        static Material WindowGlassMat()
        {
            if (_mats.TryGetValue("S_WindowGlass", out var cached)) return cached;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Shaders/WindowGlass.shader");
            if (shader == null) throw new InvalidOperationException("Shader 'Zombies55/WindowGlass' not found.");
            var m = LoadOrCreateMat("S_WindowGlass", shader);
            m.SetColor("_BaseColor", new Color(0.07f, 0.1f, 0.13f, 1f));
            m.SetFloat("_Smoothness", 0.98f);
            m.SetFloat("_FresnelPower", 3.2f);
            m.SetFloat("_ReflMin", 0.13f);
            m.SetFloat("_ReflMax", 0.92f);
            m.SetFloat("_ReflIntensity", 1.3f);
            m.SetFloat("_InteriorDim", 0.4f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            _mats["S_WindowGlass"] = m;
            return m;
        }

        // Car glass: the WindowGlass Fresnel shader tuned for a tinted pane: clear face-on (the dim cabin shows), mirror-like at grazing angles.
        static Material CarGlassMat()
        {
            if (_mats.TryGetValue("S_CarGlass", out var cached)) return cached;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Shaders/WindowGlass.shader");
            var m = LoadOrCreateMat("S_CarGlass", shader);
            m.SetColor("_BaseColor", new Color(0.05f, 0.07f, 0.07f, 1f));
            m.SetFloat("_Smoothness", 0.97f);
            m.SetFloat("_FresnelPower", 3.8f);
            m.SetFloat("_ReflMin", 0.04f);
            m.SetFloat("_ReflMax", 0.2f);
            m.SetFloat("_ReflIntensity", 0.5f);
            m.SetFloat("_InteriorDim", 0.85f);
            m.SetFloat("_Waviness", 0.02f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            _mats["S_CarGlass"] = m;
            return m;
        }

        // Seats, dash and headliner: a dim warm grey fabric, lighter than the black the building interiors use.
        static Material CarInteriorMat()
        {
            var m = Surf("S_CarInterior", new Color(0.36f, 0.32f, 0.27f), null, Vector2.one, 0.12f);
            m.SetColor("_EmissionColor", new Color(0.09f, 0.08f, 0.065f));   // light bounced into the shaded cabin
            m.EnableKeyword("_EMISSION");
            return m;
        }

        // One quad per window selects one cell of the atlas; only the lamp and TV cells carry emission.
        static Material WindowInteriorMat()
        {
            if (_mats.TryGetValue("S_WindowInterior", out var cached)) return cached;
            var m = Surf("S_WindowInterior", new Color(0.95f, 0.95f, 0.95f), "WindowInteriors", Vector2.one, 0.05f);
            m.SetTexture("_EmissionMap", Tex("WindowInteriors_Emission"));
            m.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        static Material SideAdMat()
        {
            if (_mats.TryGetValue("S_SideAd", out var cached)) return cached;
            var m = DecalMat("S_SideAd", "SideAds", new Color(0.95f, 0.95f, 0.95f, 1f));
            m.SetFloat("_Smoothness", 0.1f);
            return m;
        }

        // Two-sided alpha-clipped leaf cards with the imported (crown-radial) normals.
        static Material LeafMat(Color tint)
        {
            string name = "S_Leaf_" + Hx(tint);
            if (_mats.TryGetValue(name, out var cached)) return cached;
            var m = Surf(name, tint, "LeafCards", Vector2.one, 0.12f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material CarPaintMat(Color paint, float dirt)
        {
            string name = "S_CarPaint_" + Hx(paint) + "_" + Mathf.RoundToInt(dirt * 100f);
            if (_mats.TryGetValue(name, out var cached)) return cached;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Shaders/CarPaint/CarPaint.shader");
            if (shader == null) throw new InvalidOperationException("Shader 'Zombies55/CarPaint' not found.");
            var m = LoadOrCreateMat(name, shader);
            m.SetColor("_BaseColor", paint);
            m.color = paint;
            m.SetFloat("_Smoothness", 0.66f);
            m.SetFloat("_Metallic", 0.25f);
            m.SetTexture("_DirtMask", Tex("CarDirt_Mask"));
            m.SetColor("_DirtColor", new Color(0.3f, 0.26f, 0.21f, 1f));
            m.SetFloat("_DirtAmount", dirt);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            _mats[name] = m;
            return m;
        }

        // Signal lamps: one material set per axis so the controller can drive the two phases independently.
        static Material SignalMat(string color, string axis, Color c)
        {
            string name = "S_Signal" + color + "_" + axis;
            if (_mats.TryGetValue(name, out var cached)) return cached;
            bool lit = (axis == "Z" && color == "Green") || (axis == "X" && color == "Red");
            var m = Surf(name, c, null, Vector2.one, 0.8f, 0f, lit ? 3f : 0f);
            return m;
        }

        // ------------------------------------------------------------------ V2 facade kit

        static readonly string[][] V2WindowSets =
        {
            new[] { "V2_Window_Flat_BlindsHalf", "V2_Window_Flat_DarkFurniture", "V2_Window_Flat_EmptyDark" },
            new[] { "V2_Window_Seg_CurtainsRed", "V2_Window_Seg_ACUnit", "V2_Window_Seg_BlindsClosedGuard" },
            new[] { "V2_Window_Key_CurtainsBlue", "V2_Window_Key_LampLit", "V2_Window_Key_GreenPlantGuard" },
            new[] { "V2_Window_Hood_CurtainsCream", "V2_Window_Hood_PlantGuard", "V2_Window_Hood_TVRoom" },
        };

        static Look V2Look()
        {
            var r = _v2;
            double p = r.NextDouble();
            string tex = p < 0.5 ? "BrickRed" : (p < 0.84 ? "BrickBrown" : "BrickTan");
            float v = 0.86f + (float)r.NextDouble() * 0.26f;
            float tr = 1f, tb = 1f;
            if (tex == "BrickTan") { v *= 0.66f; tr = 1f; tb = 0.82f; }   // tan reads as dusty buff, not yellow
            var tint = new Color(v * tr * (1f + ((float)r.NextDouble() - 0.5f) * 0.1f), v, v * tb * (1f + ((float)r.NextDouble() - 0.5f) * 0.14f), 1f);
            var frames = new[] { new Color(0.1f, 0.2f, 0.15f), new Color(0.07f, 0.07f, 0.07f), new Color(0.74f, 0.7f, 0.58f), new Color(0.24f, 0.1f, 0.08f), new Color(0.1f, 0.2f, 0.15f) };
            var doors = new[] { Palette.DoorGreen, Palette.DoorNavy, Palette.DoorRed };
            var awnings = new[] { Palette.AwningRed, Palette.AwningGreen, Palette.AwningNavy, Palette.AwningCream };
            float st = 0.8f + (float)r.NextDouble() * 0.22f;
            return new Look
            {
                Wall = tint,
                WallTex = tex,
                Trim = new Color(0.82f * st, 0.79f * st, 0.72f * st, 1f),
                Frame = frames[r.Next(frames.Length)],
                Door = doors[r.Next(doors.Length)],
                Awning = awnings[r.Next(awnings.Length)] * 0.85f,
            };
        }

        // Mirrors the random draws the previous generator made for this building, so the layout seed downstream
        // (footprints, heights, positions of the following buildings) stays exactly as it was.
        static void BurnLegacyRng(int w, int d, int floors, int faceMask)
        {
            _buildingIndex++;
            for (int i = 0; i < 4; i++) _rng.NextDouble();
            _rng.Next(4);
            _rng.NextDouble();
            _rng.Next(3);
            for (int f = 0; f < 4; f++)
            {
                if ((faceMask & (1 << f)) == 0) continue;
                int bays = ((f < 2) ? w : d) / 3;
                int entrances = bays >= 4 && _rng.NextDouble() < 0.5 ? 2 : 1;
                for (int e = 0; e < entrances; e++) _rng.Next(bays);
                _rng.NextDouble();
                _rng.NextDouble();
                _rng.Next(3);
            }
            int count = 1 + _rng.Next(3);
            var cells = new List<int>();
            for (int i = 0; i < 9; i++) cells.Add(i);
            for (int i = cells.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (cells[i], cells[j]) = (cells[j], cells[i]); }
            int tankCell = -1;
            bool tank = (w >= 9 || d >= 9) && _rng.NextDouble() < 0.3;
            if (tank) { tankCell = cells[0]; cells.RemoveAt(0); _rng.Next(4); }
            foreach (int c in cells)
            {
                if (count <= 0) break;
                if (tankCell >= 0 && Mathf.Abs(c % 3 - tankCell % 3) < 2 && Mathf.Abs(c / 3 - tankCell / 3) < 2) continue;
                _rng.Next(4);
                count--;
            }
        }

        static GameObject Squeezed(GameObject go, float shrink, float shift)
        {
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(s.x * (3f - shrink) / 3f, s.y, s.z);
            go.transform.localPosition += go.transform.localRotation * new Vector3(shift, 0f, 0f);
            return go;
        }

        // Skyline: the v1 pass runs first into a throwaway node so every random stream downstream stays exactly as it was,
        // then the v2 kit builds the face that looks at the city with its own stream.
        static void MakeRingBuilding(Transform parent, Vector3 pos, int w, int d, int floors, int mask)
        {
            var tmp = Node("_skylineBurn", parent, pos);
            MakeKitBuildingLegacy(tmp, Vector3.zero, w, d, floors, mask, true);
            Object.DestroyImmediate(tmp.gameObject);
            var savedRng = _v2;
            int savedSign = _signIndex;
            _v2 = new System.Random(9001 + _ringIndex++);
            MakeKitBuildingV2(parent, pos, w, d, floors + (_v2.Next(3) == 0 ? 2 : 0), mask, true);
            _v2 = savedRng;
            _signIndex = savedSign;
        }

        static int _ringIndex;

        static void MakeKitBuildingV2(Transform parent, Vector3 pos, int w, int d, int floors, int faceMask = 15, bool ring = false)
        {
            var r = _v2;
            var look = V2Look();
            var b = Node("Building", parent, pos);
            float baseY = SidewalkTop;
            float height = 4f + 3.2f * floors;
            bool commercial = r.NextDouble() < 0.4;
            int headStyle = r.Next(4);
            bool bracketed = r.NextDouble() < 0.55;
            bool withParapet = !bracketed || r.NextDouble() < 0.5;
            float corniceTop = bracketed ? 0.86f : 0.56f;
            var yaws = new[] { 0f, 180f, 90f, 270f };
            int adFace = r.Next(4);
            var stoopBoxes = new List<(Vector3 pos, float yaw)>();

            for (int f = 0; f < 4; f++)
            {
                if (ring && (faceMask & (1 << f)) == 0) continue;     // skyline buildings only get the face that looks at the city
                bool zFace = f < 2;
                int bays = (zFace ? w : d) / 3;
                float half = (zFace ? d : w) * 0.5f;
                bool street = ring || (f == 0 ? pos.z > 0f : f == 1 ? pos.z < 0f : f == 2 ? pos.x > 0f : pos.x < 0f);
                float plane = zFace ? Mathf.Abs(pos.z) + half : Mathf.Abs(pos.x) + half;
                float yaw = yaws[f];
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var face = Node("Face" + f, b, Vector3.zero);
                var xs = new float[bays];
                for (int i = 0; i < bays; i++) xs[i] = -bays * 1.5f + 1.5f + 3f * i;
                Vector3 At(float x, float y, float dz = 0f) { return rot * new Vector3(x, 0f, half + dz) + new Vector3(0f, baseY + y, 0f); }
                GameObject Put(string model, float x, float y, float dz = 0f, Look l = null) { return Spawn(model, face, At(x, y, dz), yaw, l ?? look); }

                var ground = new string[bays];
                int escBay = -1;
                var upperCols = new string[bays];

                if (street)
                {
                    bool stoopOk = !ring && !commercial && plane <= 9.6f;
                    bool anyStoop = false;
                    for (int i = 0; i < bays; i++)
                    {
                        double q = r.NextDouble();
                        if (commercial) ground[i] = q < 0.3 ? "V2_Facade_ShutterShop_Half" : q < 0.55 ? "V2_Facade_ShutterShop_Down" : "V2_Facade_ShopDoorway";
                        else if (stoopOk && q < 0.62) { ground[i] = "V2_Facade_Stoop"; anyStoop = true; }
                        else ground[i] = q < 0.7 ? "V2_Facade_ShutterShop_Half" : q < 0.82 ? "V2_Facade_ShopDoorway" : q < 0.92 ? "V2_Facade_ServiceDoor" : "V2_Side_Blank_Ground";
                    }
                    if (stoopOk && !anyStoop) ground[r.Next(bays)] = "V2_Facade_Stoop";
                    bool awnings = commercial ? r.NextDouble() < 0.8 : r.NextDouble() < 0.4;
                    if (floors >= 2 && r.NextDouble() < (commercial ? 0.3 : 0.5)) escBay = r.Next(bays);

                    for (int i = 0; i < bays; i++)
                    {
                        bool shop = ground[i].Contains("Shutter") || ground[i].Contains("ShopDoorway");
                        Put(ground[i], xs[i], 0f);
                        if (ground[i] == "V2_Facade_Stoop")
                            stoopBoxes.Add((b.position + rot * new Vector3(xs[i], baseY + 0.5f, half + 0.8f), yaw));
                        if (shop)
                        {
                            if (awnings && r.NextDouble() < 0.85) NoShadows(Put("V2_Awning", xs[i], 3.2f, 0.02f));
                            if (r.NextDouble() < (commercial ? 0.75 : 0.35))
                            {
                                int k = _signIndex++ % 6;
                                NoShadows(Put("Storefront_Sign_" + (k + 1), xs[i], 3.3f, 0.15f, look.WithSign(SignColors[k])));
                            }
                        }
                    }

                    // Upper floors: one head style per building (a few strays), columns consistent across floors.
                    for (int i = 0; i < bays; i++)
                    {
                        int style = r.NextDouble() < 0.82 ? headStyle : r.Next(4);
                        bool blank = bays >= 3 && i > 0 && i < bays - 1 && i != escBay && r.NextDouble() < 0.1;
                        upperCols[i] = blank ? "blank" : style.ToString();
                    }
                    for (int i = 0; i < bays; i++)
                    {
                        for (int fl = 0; fl < floors; fl++)
                        {
                            float y0 = 4f + 3.2f * fl;
                            string piece;
                            if (upperCols[i] == "blank") piece = "V2_Facade_BeltCourse";
                            else
                            {
                                double q = r.NextDouble();
                                if (q < 0.06) piece = "V2_Facade_BrickedUp";
                                else if (q < 0.12 && fl >= 1 && i != escBay) piece = "V2_Facade_Juliet";
                                else
                                {
                                    var set = V2WindowSets[int.Parse(upperCols[i])];
                                    piece = set[r.Next(set.Length)];
                                }
                            }
                            Put(piece, xs[i], y0);
                            if (piece.Contains("Window_Flat") || piece.Contains("Window_Key"))
                            {
                                if (i != escBay && r.NextDouble() < 0.1)
                                    NoShadows(Spawn("AC_WindowUnit", face, At(xs[i] + (float)(r.NextDouble() - 0.5) * 0.4f, y0 + 0.87f), yaw,
                                        new Look { Paint = Palette.PaintWorn, Wall = look.Wall }));
                            }
                            // Grime streaks under the sills.
                            if (upperCols[i] != "blank" && r.NextDouble() < 0.4)
                                GrimeQuad(face, At(xs[i] + (float)(r.NextDouble() - 0.5) * 0.2f, y0 + 0.4f, 0.03f), rot, 1.6f, 0.8f + (float)r.NextDouble() * 0.2f);
                        }
                    }

                    if (escBay >= 0)
                    {
                        bool flip = r.NextDouble() < 0.5;
                        for (int fl = 0; fl < floors; fl++)
                        {
                            string piece = fl == floors - 1 ? "V2_FireEscape_Top" : (((fl % 2 == 0) != flip) ? "V2_FireEscape_ZigA" : "V2_FireEscape_ZigB");
                            Put(piece, xs[escBay], 4f + 3.2f * fl);
                            GrimeQuad(face, At(xs[escBay], 4f + 3.2f * fl - 0.5f, 0.04f), rot, Gf(2.6f, 3.2f), Gf(2.8f, 3.6f));   // soot and rust staining below each landing
                        }
                        if (r.NextDouble() < 0.6) Put("V2_FireEscape_DropLadder", xs[escBay] + (flip ? 1.2f : -1.2f), 4f);
                    }

                    // Downspouts and conduit on the bay boundaries that carry no stoop.
                    if (bays >= 2 && r.NextDouble() < 0.5)
                    {
                        int k = r.Next(bays - 1);
                        if (ground[k] != "V2_Facade_Stoop" && ground[k + 1] != "V2_Facade_Stoop")
                        {
                            float px = xs[k] + 1.5f;
                            Put("V2_Pipe_Downspout_Foot", px, 0f);
                            for (int fl = 0; fl < floors; fl++) Put("V2_Pipe_Downspout", px, 4f + 3.2f * fl);
                        }
                    }
                    if (bays >= 2 && r.NextDouble() < 0.3)
                    {
                        int k = r.Next(bays - 1);
                        float px = xs[k] + 1.5f;
                        for (int fl = 0; fl < Mathf.Min(floors, 2); fl++) Put("V2_Conduit_3_2", px, 4f + 3.2f * fl);
                    }
                }
                else
                {
                    // Alley / party-wall face: blank brick, painted ads, the odd window.
                    int adBay = (f == adFace && floors >= 2) ? r.Next(bays) : -1;
                    int adFloor = floors >= 2 ? 1 + r.Next(floors - 1) : 0;
                    string[] ads = { "V2_Side_Ad_Sterling", "V2_Side_Ad_Kestrel", "V2_Side_Ad_Meridian", "V2_Side_Ad_Arden" };
                    bool service = r.NextDouble() < 0.5;
                    int serviceBay = r.Next(bays);
                    for (int i = 0; i < bays; i++)
                    {
                        ground[i] = (service && i == serviceBay) ? "V2_Facade_ServiceDoor" : "V2_Side_Blank_Ground";
                        Put(ground[i], xs[i], 0f);
                        for (int fl = 0; fl < floors; fl++)
                        {
                            float y0 = 4f + 3.2f * fl;
                            string piece;
                            if (i == adBay && fl == adFloor) piece = ads[r.Next(ads.Length)];
                            else if (r.NextDouble() < 0.28) piece = V2WindowSets[0][r.Next(3)];
                            else piece = "V2_Side_Blank";
                            Put(piece, xs[i], y0);
                        }
                    }
                    if (bays >= 2 && r.NextDouble() < 0.4)
                    {
                        float px = xs[r.Next(bays - 1)] + 1.5f;
                        Put("V2_Pipe_Downspout_Foot", px, 0f);
                        for (int fl = 0; fl < floors; fl++) Put("V2_Pipe_Downspout", px, 4f + 3.2f * fl);
                    }
                }

                // Quoin corners at both ends of the face (they also hide the seams between two faces).
                float xe = bays * 1.5f;
                Put("V2_Corner_Quoin_Ground_L", xe, 0f);
                Put("V2_Corner_Quoin_Ground_R", -xe, 0f);
                for (int fl = 0; fl < floors; fl++)
                {
                    Put("V2_Corner_Quoin_L", xe, 4f + 3.2f * fl);
                    Put("V2_Corner_Quoin_R", -xe, 4f + 3.2f * fl);
                }

                // Cornice and parapet. Odd faces are inset at both ends so two faces never overlap at a corner.
                float ins = bracketed ? 0.3f : 0.34f;
                bool odd = (f & 1) == 1;
                for (int i = 0; i < bays; i++)
                {
                    var c = Put(bracketed ? "V2_Cornice_Bracketed" : "V2_Cornice_Corbel", xs[i], height);
                    var pr = withParapet ? Put("V2_Parapet_Coped", xs[i], height + corniceTop) : null;
                    if (odd && i == 0) { Squeezed(c, ins, ins * 0.5f); if (pr != null) Squeezed(pr, 0.36f, 0.18f); }
                    else if (odd && i == bays - 1) { Squeezed(c, ins, -ins * 0.5f); if (pr != null) Squeezed(pr, 0.36f, -0.18f); }
                    if (odd && bays == 1) { /* never happens: footprints are at least 2 bays */ }
                    GrimeQuad(face, At(xs[i], height - 0.55f, 0.03f), rot, 3.05f, 1.0f + (float)r.NextDouble() * 0.3f);
                    if (Gp(0.65f)) GrimeQuad(face, At(xs[i] + Gf(-0.4f, 0.4f), height - 1.7f, 0.035f), rot, Gf(2.2f, 3.0f), Gf(2.2f, 3.4f));   // long rain stain under the cornice, readable from the street
                }
                if (bracketed) Put("V2_Cornice_Bracketed_Corner_L", xe, height);
            }

            // Skyline buildings only carry the city-facing face: a plain shell closes the other three sides and the roof underside.
            if (ring)
                Box("Shell", b, new Vector3(0f, baseY + (height + 0.3f) * 0.5f, 0f), new Vector3(w - 0.7f, height + 0.3f, d - 0.7f),
                    RingShellMat(look, w - 0.7f, d - 0.7f, height + 0.3f), false);

            // Roof slab and rooftop furniture.
            Box("Roof", b, new Vector3(0f, baseY + height + 0.15f, 0f), new Vector3(w - 0.5f, 0.3f, d - 0.5f), RoofSlabMat(w - 0.5f, d - 0.5f), false);
            float roofY = baseY + height + 0.3f;
            var cells = new List<int>();
            for (int i = 0; i < 9; i++) cells.Add(i);
            for (int i = cells.Count - 1; i > 0; i--) { int j = r.Next(i + 1); (cells[i], cells[j]) = (cells[j], cells[i]); }
            float cw = (w - 2.6f) / 2f, cd = (d - 2.6f) / 2f;
            Vector3 CellPos(int c) { return new Vector3(((c % 3) - 1) * cw, roofY, ((c / 3) - 1) * cd); }
            int tankCell = -1;
            if ((w >= 9 || d >= 9) && r.NextDouble() < (ring ? 0.7 : 0.35)) { tankCell = cells[0]; cells.RemoveAt(0); Spawn("V2_Roof_WaterTower", b, CellPos(tankCell), r.Next(4) * 90f, look); }
            string[] roofKinds = { "V2_Roof_Chimney_A", "V2_Roof_Chimney_B", "V2_Roof_Bulkhead", "Roof_ACUnit", "Roof_Vent", "V2_Roof_Chimney_A", "Roof_ACUnit" };
            int count = 1 + r.Next(3);
            bool bulk = floors >= 3 && r.NextDouble() < 0.4;
            foreach (int c in cells)
            {
                if (count <= 0) break;
                if (tankCell >= 0 && Mathf.Abs(c % 3 - tankCell % 3) < 2 && Mathf.Abs(c / 3 - tankCell / 3) < 2) continue;
                string kind = (bulk && count == 1) ? "V2_Roof_Bulkhead" : roofKinds[r.Next(roofKinds.Length)];
                if (kind == "V2_Roof_Bulkhead") bulk = false;
                Spawn(kind, b, CellPos(c), r.Next(4) * 90f, look);
                count--;
            }

            var col = b.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, baseY + (height + 0.3f) * 0.5f, 0f);
            col.size = new Vector3(w, height + 0.3f, d);
            if (ring)
                foreach (var mr in b.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.Off;

            // Solid stoops: a low block the player cannot step onto, so the steps are not walked through.
            foreach (var s in stoopBoxes)
            {
                var sg = new GameObject("StoopCollider");
                sg.transform.SetParent(b, false);
                sg.transform.position = s.pos;
                sg.transform.rotation = Quaternion.Euler(0f, s.yaw, 0f);
                var sb = sg.AddComponent<BoxCollider>();
                sb.size = new Vector3(2.9f, 1.0f, 1.55f);
            }
        }
    }
}
