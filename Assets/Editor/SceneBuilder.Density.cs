using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Zombies55.EditorTools
{
    // Round 15 street density: sidewalk vault grates, steam vents and stacks, A-frame and blade signs, cardboard, extra bins and
    // bags, hydrant and gutter puddles, cables strung across the side streets. Own RNG; every spot keeps 2.4 m from spawn
    // candidates and kiosks and must be free of colliders, so nothing blocks a walkway.
    public static partial class SceneBuilder
    {
        static System.Random _dn;
        static readonly List<Vector2> _densityPts = new List<Vector2>();
        static float Dnf(float a, float b) { return a + (float)_dn.NextDouble() * (b - a); }
        static bool Dnp(float p) { return _dn.NextDouble() < p; }

        static readonly string[] ABoardTexts =
        {
            "FRESH\nCOFFEE\n$2", "OPEN\nUNTIL\n9 PM", "SOUP &\nSANDWICH", "NOODLES\nTO GO", "DAILY\nSPECIAL", "SHOE\nREPAIR",
            "WARM\nBREAD", "SLICE\n$1.50", "TAILOR\nWHILE\nYOU WAIT", "PRODUCE\nFRESH\nTODAY",
        };

        static readonly string[] BladeTexts =
        {
            "PHARMACY", "BARBER", "LAUNDRY", "HARDWARE", "BAKERY", "DELI", "PAWN", "CAFE", "TAILOR", "BOOKS",
        };

        static int _cntGrate, _cntSteam, _cntAFrame, _cntBlade, _cntBox, _cntBin, _cntBag, _cntPuddle, _cntWire, _cntNews;

        // ------------------------------------------------------------------ small mesh builder

        class Mb
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int>[] T = { new List<int>(), new List<int>() };

            // Axis-aligned box (no underside), UV in metres of world XZ so the steel texture tiles uniformly.
            public void Box(int sub, Vector3 c, Vector3 s)
            {
                var h = s * 0.5f;
                void Quad(Vector3 n, Vector3 a, Vector3 b, Vector3 cc, Vector3 d)
                {
                    int i = V.Count;
                    V.Add(c + a); V.Add(c + b); V.Add(c + cc); V.Add(c + d);
                    for (int k = 0; k < 4; k++) N.Add(n);
                    UV.Add(new Vector2((c + a).x, (c + a).z)); UV.Add(new Vector2((c + b).x, (c + b).z));
                    UV.Add(new Vector2((c + cc).x, (c + cc).z)); UV.Add(new Vector2((c + d).x, (c + d).z));
                    T[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }
                Quad(Vector3.up, new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z));
                Quad(Vector3.forward, new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, -h.y, h.z));
                Quad(Vector3.back, new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, -h.y, -h.z));
                Quad(Vector3.right, new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, -h.y, -h.z));
                Quad(Vector3.left, new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, -h.y, h.z));
            }

            public Mesh Save(string name)
            {
                EnsureFolder(GeneratedDir);
                string path = GeneratedDir + "/" + name + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
                mesh.Clear();
                mesh.subMeshCount = 2;
                mesh.SetVertices(V);
                mesh.SetNormals(N);
                mesh.SetUVs(0, UV);
                mesh.SetTriangles(T[0], 0);
                mesh.SetTriangles(T[1], 1);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                EditorUtility.SetDirty(mesh);
                return mesh;
            }
        }

        // Steel vault light: dark pit under a frame, longitudinal bars and cross-ties, 1.8 x 1.1 m, centred on the origin, lying at y = 0.
        static Mesh VaultGrateMesh(string name, float w, float d, float barPitch)
        {
            var m = new Mb();
            m.Box(0, new Vector3(0f, 0.004f, 0f), new Vector3(w - 0.1f, 0.006f, d - 0.1f));            // pit (sub 0)
            float rail = 0.075f, hh = 0.04f;
            m.Box(1, new Vector3(0f, hh * 0.5f, (d - rail) * 0.5f), new Vector3(w, hh, rail));
            m.Box(1, new Vector3(0f, hh * 0.5f, -(d - rail) * 0.5f), new Vector3(w, hh, rail));
            m.Box(1, new Vector3((w - rail) * 0.5f, hh * 0.5f, 0f), new Vector3(rail, hh, d - 2f * rail));
            m.Box(1, new Vector3(-(w - rail) * 0.5f, hh * 0.5f, 0f), new Vector3(rail, hh, d - 2f * rail));
            for (float x = -w * 0.5f + rail + barPitch * 0.5f; x < w * 0.5f - rail; x += barPitch)
                m.Box(1, new Vector3(x, hh * 0.45f, 0f), new Vector3(0.022f, hh * 0.9f, d - 2f * rail));
            for (int k = -1; k <= 1; k++)
                m.Box(1, new Vector3(0f, hh * 0.3f, k * (d - 2f * rail) * 0.32f), new Vector3(w - 2f * rail, 0.018f, 0.03f));
            return m.Save(name);
        }

        static Mesh _vaultMesh, _ventMesh;

        // ------------------------------------------------------------------ materials and textures

        static Texture2D EnsureSoftTex(string name, int w, int h, System.Func<float, float, float> alpha)
        {
            EnsureFolder(TextureDir);
            string path = TextureDir + "/" + name + "_Albedo.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                        float a = Mathf.Clamp01(alpha(u, v));
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material SteamMat()
        {
            if (_mats.TryGetValue("SteamPuff", out var c)) return c;
            var tex = EnsureSoftTex("SteamPuff", 64, 64, (u, v) =>
            {
                float r = Mathf.Sqrt(u * u + v * v);
                float n = Mathf.PerlinNoise(u * 2.2f + 5f, v * 2.2f + 9f);
                return Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f) * (0.65f + 0.7f * n);
            });
            var m = LoadOrCreateMat("SteamPuff", Shader.Find("Sprites/Default"));
            m.SetTexture("_MainTex", tex);
            m.color = Color.white;
            EditorUtility.SetDirty(m);
            _mats["SteamPuff"] = m;
            return m;
        }

        static Material PuddleMat()
        {
            if (_mats.TryGetValue("PuddleWet", out var c)) return c;
            EnsureSoftTex("PuddleSoft", 128, 128, (u, v) =>
            {
                float n = Mathf.PerlinNoise(u * 1.7f + 3f, v * 1.7f + 11f);
                float r = Mathf.Sqrt(u * u * 1.0f + v * v * 1.6f) + (n - 0.5f) * 0.45f;
                return Mathf.SmoothStep(1f, 0.55f, r);
            });
            var m = DecalMat("PuddleWet", "PuddleSoft", new Color(0.05f, 0.055f, 0.06f, 0.88f));
            m.SetFloat("_Smoothness", 0.95f);
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetFloat("_EnvironmentReflections", 1f);
            m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            return m;
        }

        // ------------------------------------------------------------------ placement helpers

        static bool DensitySpotFree(Vector3 p, float sep, Vector3 half, float yaw)
        {
            var v = new Vector2(p.x, p.z);
            if (NearSpawnCandidate(v, 2.5f) || NearKiosk(v, 2.5f)) return false;
            foreach (var o in _densityPts) if ((o - v).sqrMagnitude < sep * sep) return false;
            return BoxFree(p + Vector3.up * (half.y + 0.02f), half, yaw);
        }

        // A building wall on the street side of a block: point, normal (pointing out into the street).
        static bool StreetWall(Vector2 block, Vector2 n, float along, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = default;
            var t = new Vector2(-n.y, n.x);
            var s2 = block + n * 12.4f + t * along;
            var o = new Vector3(s2.x, SidewalkTop + 0.9f, s2.y);
            var dirIn = new Vector3(-n.x, 0f, -n.y);
            if (!Physics.Raycast(o, dirIn, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.name != "Building" || Mathf.Abs(hit.normal.y) > 0.1f) return false;
            normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            point = new Vector3(hit.point.x, SidewalkTop, hit.point.z);
            return true;
        }

        static void ForEachBlock(System.Action<Vector2> body)
        {
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {
                    if (Mathf.Approximately(cx, 0f) && Mathf.Approximately(cz, 0f)) continue;
                    body(new Vector2(cx, cz));
                }
        }

        static float YawOf(Vector3 n) { return Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg; }

        // ------------------------------------------------------------------ entry

        static void BuildStreetDensity()
        {
            _dn = new System.Random(1515);
            _densityPts.Clear();
            _cntGrate = _cntSteam = _cntAFrame = _cntBlade = _cntBox = _cntBin = _cntBag = _cntPuddle = _cntWire = _cntNews = 0;
            Physics.SyncTransforms();
            var root = Sub("Density");

            BuildVaultGrates(root);
            BuildSteam(root);
            BuildSidewalkSigns(root);
            BuildCardboardAndBins(root);
            BuildPuddles(root);
            BuildCrossStreetCables(root);
            // Small props: no shadow-map cost (their contact shadows come from SSAO).
            foreach (var g in new[] { "Signs", "Steam", "CrossCables", "VaultGrates" })
                NoShadows(root.Find(g).gameObject);
            Debug.Log("Density: " + _cntGrate + " vault grates, " + _cntSteam + " steam vents, " + _cntAFrame + " A-frames, " + _cntBlade + " blade signs, "
                + _cntBox + " cardboard piles, " + _cntBin + " bins, " + _cntBag + " bag piles, " + _cntNews + " news boxes, " + _cntPuddle + " puddles, " + _cntWire + " cable spans");
        }

        // ------------------------------------------------------------------ vault grates

        static void BuildVaultGrates(Transform root)
        {
            var grp = Node("VaultGrates", root, Vector3.zero);
            if (_vaultMesh == null) _vaultMesh = VaultGrateMesh("VaultGrate", 2.2f, 1.25f, 0.075f);
            var pit = Mat("VaultPit", new Color(0.02f, 0.02f, 0.022f));
            var steel = Meters("VaultSteel", new Color(0.3f, 0.3f, 0.31f), "MetalPainted", 1.5f, 0.55f, 0f);
            ForEachBlock(block =>
            {
                foreach (var n in SideNormals)
                {
                    int want = Dnp(0.75f) ? 1 : 0;
                    if (Dnp(0.25f)) want++;
                    for (int attempt = 0; attempt < 14 && want > 0; attempt++)
                    {
                        if (!StreetWall(block, n, Dnf(-8.5f, 8.5f), out var wp, out var wn)) continue;
                        var pos = wp + wn * (0.65f + 0.5f);                              // flush against the shop, beside the wall
                        pos.y = SidewalkTop;
                        float yaw = YawOf(wn);
                        if (!DensitySpotFree(pos, 3.2f, new Vector3(1.1f, 0.05f, 0.65f), yaw)) continue;
                        var go = new GameObject("VaultGrate");
                        go.transform.SetParent(grp, false);
                        go.transform.position = pos + Vector3.up * 0.003f;
                        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        go.AddComponent<MeshFilter>().sharedMesh = _vaultMesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = new[] { pit, steel };
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        _densityPts.Add(new Vector2(pos.x, pos.z));
                        want--; _cntGrate++;
                    }
                }
            });
        }

        // ------------------------------------------------------------------ steam

        static void BuildSteam(Transform root)
        {
            var grp = Node("Steam", root, Vector3.zero);
            if (_ventMesh == null) _ventMesh = VaultGrateMesh("SteamVent", 0.8f, 0.5f, 0.06f);
            var pit = Mat("VaultPit", new Color(0.02f, 0.02f, 0.022f));
            var steel = Meters("VaultSteel", new Color(0.3f, 0.3f, 0.31f), "MetalPainted", 1.5f, 0.55f, 0f);
            var orange = Meters("Paint", new Color(0.78f, 0.34f, 0.08f), "MetalPainted", 1.5f, 1f);
            var white = Meters("Paint", new Color(0.82f, 0.8f, 0.76f), "MetalPainted", 1.5f, 1f);
            int vents = 0, stacks = 0;
            ForEachBlock(block =>
            {
                if (!Dnp(0.42f)) return;
                var n = SideNormals[_dn.Next(4)];
                var t = new Vector2(-n.y, n.x);
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    var q = block + n * 12.2f + t * Dnf(-8f, 8f);
                    var pos = new Vector3(q.x, SidewalkTop, q.y);
                    float yaw = n.x != 0f ? 90f : 0f;
                    if (!DensitySpotFree(pos, 4f, new Vector3(0.5f, 0.35f, 0.4f), yaw)) continue;
                    bool stack = Dnp(0.3f) && stacks < 7;
                    var go = new GameObject(stack ? "SteamStack" : "SteamVent");
                    go.transform.SetParent(grp, false);
                    go.transform.position = pos;
                    if (stack)
                    {
                        var body = Prim(PrimitiveType.Cylinder, "Body", go.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.34f, 0.55f, 0.34f), orange, false);
                        Prim(PrimitiveType.Cylinder, "Band", go.transform, new Vector3(0f, 0.62f, 0f), new Vector3(0.352f, 0.07f, 0.352f), white, false);
                        Prim(PrimitiveType.Cylinder, "Band2", go.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.352f, 0.05f, 0.352f), white, false);
                        Prim(PrimitiveType.Cylinder, "Cap", go.transform, new Vector3(0f, 1.12f, 0f), new Vector3(0.4f, 0.025f, 0.4f), Mat("VaultPit", new Color(0.02f, 0.02f, 0.022f)), false);
                        var box = go.AddComponent<BoxCollider>();
                        box.center = new Vector3(0f, 0.57f, 0f); box.size = new Vector3(0.38f, 1.14f, 0.38f);
                        stacks++;
                    }
                    else
                    {
                        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        go.AddComponent<MeshFilter>().sharedMesh = _ventMesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = new[] { pit, steel };
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        vents++;
                    }
                    MakeSteamPuff(go.transform, stack ? 1.15f : 0.06f, stack ? 1f : 0.75f);
                    _densityPts.Add(new Vector2(pos.x, pos.z));
                    _cntSteam++;
                    break;
                }
            });
        }

        // Sparse, soft, rising steam: a few big low-alpha puffs that grow and drift, palette-warm white.
        static void MakeSteamPuff(Transform parent, float height, float strength)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 6f; main.loop = true; main.playOnAwake = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.2f, 4.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startColor = new Color(0.93f, 0.92f, 0.88f, 0.6f * strength);
            main.maxParticles = 18;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.01f;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 3.2f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.12f;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 4.2f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.86f, 0.87f, 0.9f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.35f; noise.scrollSpeed = 0.2f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.2f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SteamMat();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = 2f;
            ps.Play();
        }

        // ------------------------------------------------------------------ A-frame and blade signs

        static TextMeshPro MakeLabel(Transform parent, string name, string text, Vector3 pos, float yaw, Vector2 size, float fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = size;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return tmp;
        }

        static void BuildSidewalkSigns(Transform root)
        {
            var grp = Node("Signs", root, Vector3.zero);
            var board = Meters("Chalk", new Color(0.075f, 0.085f, 0.08f), null, 1f, 0.08f);
            var frame = Meters("ChalkFrame", new Color(0.4f, 0.28f, 0.17f), "Wood", 1f, 0.2f);
            var panelCols = new[] { new Color(0.075f, 0.085f, 0.08f), new Color(0.55f, 0.1f, 0.08f), new Color(0.1f, 0.2f, 0.3f), new Color(0.78f, 0.74f, 0.64f) };
            int ti = 0, bi = 0;
            ForEachBlock(block =>
            {
                // A-frames: one or two per block, a metre out from the wall.
                int want = 1 + (Dnp(0.5f) ? 1 : 0);
                for (int attempt = 0; attempt < 14 && want > 0; attempt++)
                {
                    var n = SideNormals[_dn.Next(4)];
                    if (!StreetWall(block, n, Dnf(-8f, 8f), out var wp, out var wn)) continue;
                    var pos = wp + wn * 1.15f; pos.y = SidewalkTop;
                    float yaw = YawOf(wn) + Dnf(-18f, 18f);
                    if (!DensitySpotFree(pos, 3f, new Vector3(0.42f, 0.5f, 0.45f), yaw)) continue;
                    bool dark = Dnp(0.6f);
                    var bm = dark ? board : Meters("ChalkAlt" + (ti % 3), panelCols[1 + ti % 3], null, 1f, 0.08f);
                    Color txt = dark || ti % 3 != 2 ? new Color(0.94f, 0.92f, 0.84f) : new Color(0.1f, 0.1f, 0.1f);
                    var go = new GameObject("AFrameSign");
                    go.transform.SetParent(grp, false);
                    go.transform.position = pos;
                    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    string text = ABoardTexts[ti++ % ABoardTexts.Length];
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var hinge = Node("Hinge", go.transform, new Vector3(0f, 0.98f, 0f));
                        hinge.localRotation = Quaternion.Euler(side < 0f ? 11f : -11f, 0f, 0f);
                        Prim(PrimitiveType.Cube, "Panel", hinge, new Vector3(0f, -0.49f, 0f), new Vector3(0.62f, 0.98f, 0.035f), bm, false);
                        Prim(PrimitiveType.Cube, "Frame", hinge, new Vector3(0f, -0.49f, -side * 0.004f), new Vector3(0.67f, 1.03f, 0.03f), frame, false);
                        var lab = MakeLabel(hinge, "Text", text, new Vector3(0f, -0.49f, side * 0.022f), side < 0f ? 0f : 180f, new Vector2(0.55f, 0.85f), 2.1f, txt);
                        lab.enableAutoSizing = true; lab.fontSizeMin = 1f; lab.fontSizeMax = 2.4f;
                    }
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.5f, 0f); box.size = new Vector3(0.66f, 1f, 0.5f);
                    _densityPts.Add(new Vector2(pos.x, pos.z));
                    want--; _cntAFrame++;
                }

                // Blade signs hanging from the wall over the pavement, above the awning line.
                int wantB = Dnp(0.65f) ? 1 : 0;
                for (int attempt = 0; attempt < 12 && wantB > 0; attempt++)
                {
                    var n = SideNormals[_dn.Next(4)];
                    if (!StreetWall(block, n, Dnf(-9f, 9f), out var wp, out var wn)) continue;
                    var pos = wp + Vector3.up * 3.55f;
                    float yaw = YawOf(wn);
                    var q = new Vector2(wp.x, wp.z) + new Vector2(wn.x, wn.z) * 0.55f;
                    if (NearSpawnCandidate(q, 2.5f) || NearKiosk(q, 2.5f)) continue;
                    var tang = new Vector3(wn.z, 0f, -wn.x);
                    if (!BoxFree(pos + wn * 0.55f + Vector3.up * 0.1f, new Vector3(0.12f, 0.5f, 0.55f), yaw)) continue;
                    var go = new GameObject("BladeSign");
                    go.transform.SetParent(grp, false);
                    go.transform.position = pos;
                    go.transform.rotation = Quaternion.LookRotation(wn, Vector3.up);
                    var metal = Meters("Metal", Palette.MetalDark, "MetalPainted", 1.5f, 1f);
                    var panelMat = Meters("BladePanel" + (bi % 4), panelCols[bi % 4] * 1.0f, "MetalPainted", 1.5f, 1f);
                    Prim(PrimitiveType.Cube, "Arm", go.transform, new Vector3(0f, 0.55f, 0.42f), new Vector3(0.03f, 0.03f, 0.84f), metal, false);
                    Prim(PrimitiveType.Cube, "Brace", go.transform, new Vector3(0f, 0.31f, 0.2f), new Vector3(0.02f, 0.02f, 0.5f), metal, false, new Vector3(-35f, 0f, 0f));
                    Prim(PrimitiveType.Cube, "Panel", go.transform, new Vector3(0f, 0.1f, 0.65f), new Vector3(0.05f, 0.7f, 0.9f), panelMat, false);
                    Prim(PrimitiveType.Cube, "Hang1", go.transform, new Vector3(0f, 0.48f, 0.35f), new Vector3(0.012f, 0.18f, 0.012f), metal, false);
                    Prim(PrimitiveType.Cube, "Hang2", go.transform, new Vector3(0f, 0.48f, 0.95f), new Vector3(0.012f, 0.18f, 0.012f), metal, false);
                    string text = BladeTexts[bi++ % BladeTexts.Length];
                    Color txt = bi % 4 == 0 ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.95f, 0.92f, 0.82f);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        // The panel's long axis runs along the arm (local z); text rotated to read along it.
                        var go2 = new GameObject("Face");
                        go2.transform.SetParent(go.transform, false);
                        go2.transform.localPosition = new Vector3(side * 0.03f, 0.1f, 0.65f);
                        go2.transform.localRotation = Quaternion.Euler(0f, side < 0f ? 90f : -90f, 0f);
                        var lab = MakeLabel(go2.transform, "Text", text, Vector3.zero, 0f, new Vector2(0.85f, 0.6f), 2f, txt);
                        lab.enableAutoSizing = true; lab.fontSizeMin = 0.8f; lab.fontSizeMax = 2.2f;
                    }
                    wantB--; _cntBlade++;
                }
            });
        }

        // ------------------------------------------------------------------ cardboard, bins, bags

        static void BuildCardboardAndBins(Transform root)
        {
            var grp = Node("Litter", root, Vector3.zero);
            var card = Meters("Cardboard", new Color(0.5f, 0.38f, 0.25f), null, 1f, 0.08f);
            var cardDark = Meters("CardboardDark", new Color(0.4f, 0.3f, 0.2f), null, 1f, 0.08f);
            var bagLook = new Look();
            ForEachBlock(block =>
            {
                // Cardboard piles against shop walls, flat bundles and open boxes.
                int want = 1 + (Dnp(0.6f) ? 1 : 0);
                for (int attempt = 0; attempt < 14 && want > 0; attempt++)
                {
                    var n = SideNormals[_dn.Next(4)];
                    if (!StreetWall(block, n, Dnf(-9f, 9f), out var wp, out var wn)) continue;
                    var pos = wp + wn * 0.5f; pos.y = SidewalkTop;
                    float yaw = YawOf(wn);
                    if (!DensitySpotFree(pos, 2.2f, new Vector3(0.55f, 0.4f, 0.4f), yaw)) continue;
                    var go = new GameObject("CardboardPile");
                    go.transform.SetParent(grp, false);
                    go.transform.position = pos;
                    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    int boxes = 2 + _dn.Next(3);
                    float x = -0.35f;
                    for (int b = 0; b < boxes; b++)
                    {
                        float w = Dnf(0.35f, 0.55f), h = Dnf(0.22f, 0.42f), d = Dnf(0.3f, 0.45f);
                        Prim(PrimitiveType.Cube, "Box", go.transform, new Vector3(x + w * 0.5f, h * 0.5f, Dnf(-0.05f, 0.05f)), new Vector3(w, h, d), b % 2 == 0 ? card : cardDark, false, new Vector3(0f, Dnf(-14f, 14f), 0f));
                        if (b > 0 && Dnp(0.55f))
                            Prim(PrimitiveType.Cube, "BoxTop", go.transform, new Vector3(x + w * 0.5f, h + 0.11f, 0f), new Vector3(w * 0.85f, 0.22f, d * 0.85f), b % 2 == 0 ? cardDark : card, false, new Vector3(0f, Dnf(-25f, 25f), 0f));
                        x += w + 0.04f;
                    }
                    if (Dnp(0.5f)) Prim(PrimitiveType.Cube, "Flat", go.transform, new Vector3(0.2f, 0.04f, 0.38f), new Vector3(0.9f, 0.06f, 0.5f), cardDark, false, new Vector3(0f, Dnf(-20f, 20f), 0f));
                    NoShadows(go);
                    var col = go.AddComponent<BoxCollider>();
                    col.center = new Vector3(0.1f, 0.25f, 0f); col.size = new Vector3(1.4f, 0.5f, 0.55f);
                    _densityPts.Add(new Vector2(pos.x, pos.z));
                    want--; _cntBox++;
                }

                // Trash bins and bag piles along the curb side and walls.
                int bins = 1 + (Dnp(0.5f) ? 1 : 0);
                for (int attempt = 0; attempt < 16 && bins > 0; attempt++)
                {
                    var n = SideNormals[_dn.Next(4)];
                    var t = new Vector2(-n.y, n.x);
                    var q = block + n * Dnf(11.9f, 12.6f) + t * Dnf(-9f, 9f);
                    var pos = new Vector3(q.x, SidewalkTop, q.y);
                    float yaw = n.x != 0f ? 90f : 0f;
                    if (!DensitySpotFree(pos, 2.6f, new Vector3(0.4f, 0.55f, 0.4f), yaw)) continue;
                    var bin = Spawn("TrashCan", grp, pos, yaw + Dnf(-15f, 15f));
                    ThinBox(bin, 0.6f, 1f);
                    NoShadows(bin);
                    Physics.SyncTransforms();
                    _densityPts.Add(new Vector2(pos.x, pos.z));
                    if (Dnp(0.7f))
                    {
                        var ang = Dnf(0f, 360f) * Mathf.Deg2Rad;
                        var bp = pos + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Dnf(1.0f, 1.4f);
                        if (BoxFree(bp + Vector3.up * 0.4f, new Vector3(0.5f, 0.3f, 0.5f), 0f) && !NearSpawnCandidate(new Vector2(bp.x, bp.z), 2.5f))
                        { NoShadows(Spawn("TrashBags", grp, bp, Dnf(0f, 360f))); _cntBag++; }
                    }
                    bins--; _cntBin++;
                }

                // An extra news box or two on the kerb-side of the walkway.
                if (Dnp(0.5f))
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        var n = SideNormals[_dn.Next(4)];
                        if (!StreetWall(block, n, Dnf(-9f, 9f), out var wp, out var wn)) continue;
                        var pos = wp + wn * 0.3f; pos.y = SidewalkTop;
                        float yaw = YawOf(wn);
                        if (!DensitySpotFree(pos, 2.4f, new Vector3(0.3f, 0.55f, 0.3f), yaw)) continue;
                        if (!BoxFree(pos + wn * 1.0f + Vector3.up * 1.3f, new Vector3(0.7f, 0.9f, 0.8f), yaw)) continue;
                        var nb = Spawn("NewsBox", grp, pos, yaw, new Look { Paint = Dnp(0.5f) ? Palette.NewsBoxBlue : Palette.NewsBoxRed });
                        FitBox(nb, 0.1f);
                        Physics.SyncTransforms();
                        _densityPts.Add(new Vector2(pos.x, pos.z));
                        _cntNews++;
                        break;
                    }
            });
        }

        // ------------------------------------------------------------------ puddles

        static void BuildPuddles(Transform root)
        {
            var grp = Node("Puddles", root, Vector3.zero);
            var mat = PuddleMat();
            var quad = GritQuad(false, 0f, 1f);
            void Puddle(Vector3 p, float w, float d, float yaw)
            {
                PlaceQuad(quad, mat, grp, new Vector3(p.x, p.y + 0.006f, p.z), Quaternion.Euler(0f, yaw, 0f), new Vector3(w, 1f, d), "Puddle");
                _cntPuddle++;
            }
            foreach (var h in _hydrants)
            {
                Puddle(new Vector3(h.x, SidewalkTop, h.y) + new Vector3(Dnf(-0.15f, 0.15f), 0f, Dnf(-0.15f, 0.15f)), Dnf(1.6f, 2.3f), Dnf(1.3f, 1.9f), Dnf(0f, 360f));
                Puddle(new Vector3(h.x + Dnf(-1.2f, 1.2f), SidewalkTop, h.y + Dnf(-1.2f, 1.2f)), Dnf(0.6f, 0.9f), Dnf(0.5f, 0.8f), Dnf(0f, 360f));
            }
            // Gutter pools and a few mid-sidewalk ones where the paving dips.
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                    foreach (var n in SideNormals)
                    {
                        if (!Dnp(0.45f)) continue;
                        var t = new Vector2(-n.y, n.x);
                        bool gutter = Dnp(0.65f);
                        var q = new Vector2(cx, cz) + n * (gutter ? 13.3f : 11.4f) + t * Dnf(-9f, 9f);
                        var p = new Vector3(q.x, gutter ? 0.012f : SidewalkTop, q.y);
                        if (Mathf.Approximately(cx, 0f) && Mathf.Approximately(cz, 0f)) continue;
                        Puddle(p, gutter ? Dnf(2.4f, 4.2f) : Dnf(1.2f, 2f), gutter ? Dnf(0.6f, 0.9f) : Dnf(0.9f, 1.4f), n.x != 0f ? 90f + Dnf(-8f, 8f) : Dnf(-8f, 8f));
                    }
        }

        // ------------------------------------------------------------------ cables across the side streets

        static void SaggedWire(Transform parent, Vector3 a, Vector3 b, float sag, int segs)
        {
            Vector3 prev = a;
            for (int i = 1; i <= segs; i++)
            {
                float t = i / (float)segs;
                var p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t);
                PlaceWire(parent, prev, p);
                prev = p;
            }
        }

        static void BuildCrossStreetCables(Transform root)
        {
            var grp = Node("CrossCables", root, Vector3.zero);
            var fixture = Meters("Metal", Palette.MetalDark, "MetalPainted", 1.5f, 1f);
            // Spans over the mid-block of each road segment, wall to wall: roads along Z (axis 0) and along X (axis 1).
            for (int axis = 0; axis < 2; axis++)
                foreach (float road in RoadCenters)
                    for (int seg = 0; seg + 1 < RoadCenters.Length; seg++)
                    {
                        int spans = 1 + _dn.Next(2) + (Dnp(0.3f) ? 1 : 0);
                        for (int k = 0; k < spans; k++)
                        {
                            float s = Dnf(RoadCenters[seg] + 11f, RoadCenters[seg + 1] - 11f);
                            float h = Dnf(6.5f, 9.5f);
                            Vector3 mid = axis == 0 ? new Vector3(road, h, s) : new Vector3(s, h, road);
                            var across = axis == 0 ? Vector3.right : Vector3.forward;
                            if (!Physics.Raycast(mid, across, out var hitA, 14f, ~0, QueryTriggerInteraction.Ignore) || hitA.collider.name != "Building") continue;
                            if (!Physics.Raycast(mid, -across, out var hitB, 14f, ~0, QueryTriggerInteraction.Ignore) || hitB.collider.name != "Building") continue;
                            var a = hitA.point - across * 0.04f;
                            var b = hitB.point + across * 0.04f;
                            float sag = Dnf(0.4f, 0.9f);
                            int strands = 1 + _dn.Next(3);
                            for (int w = 0; w < strands; w++)
                            {
                                var off = (axis == 0 ? Vector3.forward : Vector3.right) * (w * 0.09f) + Vector3.up * (w * 0.05f);
                                SaggedWire(grp, a + off, b + off, sag, 6);
                            }
                            // Insulator brackets at the wall ends.
                            foreach (var p in new[] { a, b })
                                Prim(PrimitiveType.Cube, "Bracket", grp, p, new Vector3(0.1f, 0.1f, 0.1f), fixture, false);
                            _cntWire++;
                        }
                    }
        }
    }
}
