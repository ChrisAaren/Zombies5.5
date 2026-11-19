using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // Combat setup for the scene: player weapons and aim, weapon kiosks, explosive barrels and cars.
    public static partial class SceneBuilder
    {
        const float KioskLine = 10.75f;     // kiosk center, from the block center (building walls end at 10.0)
        const int BarrelTarget = 30;        // for a 3x3 grid; grows with the block count

        static readonly List<string> _combatMissing = new List<string>();
        static Transform _combatRoot;

        static void BuildCombat()
        {
            _combatMissing.Clear();
            _combatRoot = Root("Combat");
            Physics.SyncTransforms();

            BuildCombatPlayer();

            var loop = Object.FindAnyObjectByType<GameLoop>();
            if (loop != null && loop.GetComponent<PlayerWallet>() == null) loop.gameObject.AddComponent<PlayerWallet>();

            BuildExplosiveCars();
            BuildKiosks();
            BuildBarrels();

            if (_combatMissing.Count > 0)
                Debug.LogWarning("Combat: models missing, primitive stand-ins used for: " + string.Join(", ", _combatMissing)
                    + ". Rebuild the scene once the FBX files exist.");
        }

        // ------------------------------------------------------------------ models and materials

        static GameObject TryModel(string name)
        {
            try { return Model(name); }
            catch (InvalidOperationException)
            {
                if (!_combatMissing.Contains(name)) _combatMissing.Add(name);
                return null;
            }
        }

        // Slot materials for the survival-mode slots; every other slot goes through the shared kit.
        static Material CombatSlotMat(string slot, Look look)
        {
            int dot = slot.IndexOf('.');
            if (dot > 0) slot = slot.Substring(0, dot);
            switch (slot)
            {
                case "GunMetal": return Meters("GunMetal", Palette.GunMetal, null, 1f, 0.55f, 0.7f);
                case "GunPolymer": return Meters("GunPolymer", Palette.GunPolymer, null, 1f, 0.3f);
                case "GunGrip": return Meters("GunGrip", Palette.GunGrip, null, 1f, 0.15f);
                case "Sight": return Surf("S_Sight", Palette.SightGlow, null, Vector2.one, 0.6f, 0f, 2f);
                case "Hazard": return Meters("Hazard", Palette.HazardYellow, null, 1f, 0.35f);
                case "KioskScreen": return Surf("S_KioskScreen", Palette.Accent, null, Vector2.one, 0.7f, 0f, 1.6f);
                case "Hood": return Meters("Hood", Palette.HoodGrey, null, 1f, 0.2f);
            }
            return SlotMat(slot, look);
        }

        static void ApplyCombatLook(GameObject go, Look look)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = CombatSlotMat(src[i] != null ? src[i].name : "", look);
                r.sharedMaterials = dst;
            }
        }

        static GameObject SpawnCombat(GameObject model, Transform parent, Vector3 localPos, float yaw, Look look, string name)
        {
            var go = (GameObject)Object.Instantiate(model);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one;
            ApplyCombatLook(go, look);
            return go;
        }

        // Bounds of the renderers (optionally filtered) in root's local space.
        static bool CombatBounds(GameObject root, Func<Renderer, bool> filter, out Bounds bounds)
        {
            var inv = root.transform.worldToLocalMatrix;
            bounds = default;
            bool first = true;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<Renderer>();
                if (mf.sharedMesh == null || r == null || (filter != null && !filter(r))) continue;
                var mb = mf.sharedMesh.bounds;
                var m = inv * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            return !first;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // ------------------------------------------------------------------ player

        // Gun grips, in the right hand's palm frame (see PlayerAnimation): X is the curl axis (the gun's up is -X, the thumb
        // side), Y runs along the fingers (the barrel), Z is the palm normal (the gun's left, towards the palm). The grip
        // positions are the gun origin's offset from the palm centre, fitted against the captures so the grip handle sits in
        // the curled fingers; yaw and pitch trim the barrel against the fingers' direction in the AimPistol / AimRifle poses.
        static readonly Vector3 PistolGripPos = new Vector3(-0.065f, 0.03f, 0.015f);
        static readonly Vector3 PistolGripTrim = new Vector3(-2f, 18f, 0f);    // pitch, yaw, roll about the gun's own axes
        static readonly Vector3 LongGripPos = new Vector3(-0.065f, 0.03f, 0.015f);
        static readonly Vector3 LongGripTrim = new Vector3(0f, 104.6f, 0f);
        // Left-hand palm centre on the pistol, in gun space.
        static readonly Vector3 PistolSupportPos = new Vector3(-0.045f, -0.07f, 0.02f);

        // Per long gun, in gun space (X right, Y up, Z forward, origin at the grip): where the support palm sits (under the
        // handguard or pump, palm up, fingers across to the gun's right) and the point of the butt that goes into the
        // shoulder pocket. Heights are the underside of the handguard minus the soft palm.
        static readonly Dictionary<string, (Vector3 foregrip, Vector3 stock)> LongGunFit = new Dictionary<string, (Vector3, Vector3)>
        {
            { "Gun_SMG",     (new Vector3(0f, 0.002f, 0.200f), new Vector3(0f, 0.028f, -0.205f)) },
            { "Gun_Shotgun", (new Vector3(0f, -0.022f, 0.310f), new Vector3(0f, 0.0f, -0.328f)) },
            { "Gun_Rifle",   (new Vector3(0f, 0.002f, 0.270f), new Vector3(0f, -0.01f, -0.243f)) },
            { "Gun_DMR",     (new Vector3(0f, 0.006f, 0.240f), new Vector3(0f, -0.02f, -0.305f)) },
            { "Gun_LMG",     (new Vector3(0f, 0.006f, 0.250f), new Vector3(0f, -0.02f, -0.297f)) },
        };

        static void BuildCombatPlayer()
        {
            var root = _playerRoot.gameObject;
            SetLayerRecursive(root, GameLayers.Player);
            root.AddComponent<PlayerWeapons>();
            root.AddComponent<CombatAudio>();
            var fx = root.AddComponent<CombatFX>();
            SetRef(fx, "particleMaterial", mParticle);

            var anim = _playerCharacter.GetComponent<PlayerAnimation>();
            var animator = _playerCharacter.GetComponent<Animator>();
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);

            // Mount: at the palm centre, oriented as the palm frame (the rest orientation of the middle proximal bone).
            var mount = Node("GunMount", hand, Vector3.zero);
            mount.SetPositionAndRotation(hand.position + (middle.position - hand.position).normalized * PlayerAnimation.PalmCentre, middle.rotation);

            foreach (var def in WeaponCatalog.All)
            {
                var model = TryModel(def.ModelName);
                GameObject gun = model != null
                    ? SpawnCombat(model, mount, Vector3.zero, 0f, DefaultLook, def.ModelName)
                    : StandInGun(def.ModelName, mount);
                foreach (var c in gun.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                bool pistol = def.Id == WeaponCatalog.StarterId;
                var trim = pistol ? PistolGripTrim : LongGripTrim;
                // Barrel along the fingers (palm Y), gun up against the curl axis (palm -X).
                gun.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.left)
                    * Quaternion.Euler(trim.x, trim.y, trim.z);
                gun.transform.localPosition = pistol ? PistolGripPos : LongGripPos;

                // Foregrip: the left palm centre, as a palm frame (Y along the fingers, Z out of the palm).
                Transform grip;
                if (pistol)
                {
                    // Support hand on the left of the grip, fingers forward, palm towards the gun.
                    grip = Node("Foregrip", gun.transform, PistolSupportPos);
                    grip.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.forward);
                }
                else
                {
                    // Under the handguard, fingers across to the right, palm up, curling round the barrel.
                    var fit = LongGunFit[def.ModelName];
                    grip = Node("Foregrip", gun.transform, fit.foregrip);
                    grip.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.right);
                    Node("Stock", gun.transform, fit.stock);
                }
                SetLayerRecursive(gun, GameLayers.Player);
                gun.SetActive(pistol);
            }

            SetRef(anim, "gunMount", mount);
        }

        static float StandInLength(string modelName)
        {
            switch (modelName)
            {
                case "Gun_Pistol": return 0.2f;
                case "Gun_SMG": return 0.5f;
                case "Gun_Shotgun": return 0.95f;
                case "Gun_Rifle": return 0.85f;
                case "Gun_DMR": return 1.05f;
                default: return 1.0f;
            }
        }

        static GameObject StandInGun(string modelName, Transform parent)
        {
            float len = StandInLength(modelName);
            var gun = Node(modelName, parent, Vector3.zero).gameObject;
            var metal = Mat("GunMetal", Palette.GunMetal);
            var body = Box("Body", gun.transform, new Vector3(0f, 0f, len * 0.5f - 0.05f), new Vector3(0.05f, 0.07f, len), metal, false);
            body.name = "Body";
            Box("Grip", gun.transform, new Vector3(0f, -0.07f, -0.01f), new Vector3(0.04f, 0.12f, 0.05f), Mat("GunPolymer", Palette.GunPolymer), false);
            Node("Muzzle", gun.transform, new Vector3(0f, 0f, len - 0.05f));
            return gun;
        }

        // ------------------------------------------------------------------ cars

        static void BuildExplosiveCars()
        {
            var cars = _props.Find("Cars");
            if (cars == null) return;
            // Out of Props so the static-batching pass leaves them alone (wrecks swap materials).
            cars.SetParent(_combatRoot, true);
            cars.name = "ExplosiveCars";
            var wreck = Mat("WreckChar", Palette.WreckChar);
            foreach (Transform car in cars)
            {
                var col = car.GetComponent<BoxCollider>();
                if (col == null) col = FitBox(car.gameObject, 0.2f);
                _carColliders.Add(col);
                SetLayerRecursive(car.gameObject, GameLayers.Explosive);
                var prop = car.gameObject.AddComponent<ExplosiveProp>();
                prop.Configure(ExplosiveKind.Car, wreck);
                var ob = car.gameObject.AddComponent<NavMeshObstacle>();
                ob.shape = NavMeshObstacleShape.Box;
                ob.center = col.center;
                ob.size = col.size + new Vector3(0.3f, 0f, 0.3f);
                ob.carving = true;
            }
        }

        // ------------------------------------------------------------------ block grid helpers

        static List<Vector2> CombatBlocks(bool includePlaza)
        {
            var list = new List<Vector2>();
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                {
                    bool plaza = Mathf.Abs(cx) < 0.01f && Mathf.Abs(cz) < 0.01f;
                    if (plaza && !includePlaza) continue;
                    list.Add(new Vector2(cx, cz));
                }
            return list;
        }

        static readonly Vector2[] SideNormals = { new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f) };

        // ------------------------------------------------------------------ kiosks

        static void BuildKiosks()
        {
            // One kiosk per gun, placed by district tier. Each entry lists candidate cells (ix, iz) in order of
            // preference; the first cell with a free sidewalk spot wins. Spots sit 10.75 m from the block center,
            // so they never touch the barrier lines on the cell edges (19 m out).
            var kiosks = Node("Kiosks", _combatRoot, Vector3.zero);
            var plan = new (string id, Vector2Int[] cells)[]
            {
                ("wasp", new[] { new Vector2Int(0, 0) }),                                                        // Fountain Square
                ("breaker", new[] { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) }),   // inner side
                ("kestrel", new[] { new Vector2Int(1, 1), new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(-1, 1) }), // inner corner
                ("longshot", new[] { new Vector2Int(-2, 0), new Vector2Int(2, 0), new Vector2Int(-2, 1), new Vector2Int(2, -1) }), // West or East
                ("atlas", new[] { new Vector2Int(0, 2), new Vector2Int(0, -2), new Vector2Int(1, 2), new Vector2Int(-1, -2) }),    // North or South
            };

            for (int k = 0; k < plan.Length; k++)
            {
                var def = WeaponCatalog.Get(plan[k].id);
                bool placed = false;
                foreach (var cell in plan[k].cells)
                {
                    Vector2 block = ZoneLayout.CellCenter(cell.x, cell.y);

                    // Sides ordered by how much they face the map center; first free spot wins.
                    var order = new List<int> { 0, 1, 2, 3 };
                    Vector2 toCenter = block.sqrMagnitude < 0.01f ? Vector2.up : -block.normalized;
                    order.Sort((a, b) => Vector2.Dot(SideNormals[b], toCenter).CompareTo(Vector2.Dot(SideNormals[a], toCenter)));
                    foreach (float alongSign in new[] { k % 2 == 0 ? 1f : -1f, k % 2 == 0 ? -1f : 1f })
                    {
                        foreach (int side in order)
                        {
                            Vector2 n = SideNormals[side];
                            Vector2 tangent = new Vector2(-n.y, n.x);
                            Vector2 p2 = block + n * KioskLine + tangent * (8.8f * alongSign);
                            var pos = new Vector3(p2.x, SidewalkTop, p2.y);
                            float yaw = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg;
                            if (!CombatSpotFree(pos, yaw, new Vector3(0.6f, 1.0f, 0.3f), 1.15f)) continue;
                            MakeKiosk(def, kiosks, pos, yaw);
                            placed = true;
                            break;
                        }
                        if (placed) break;
                    }
                    if (placed) break;
                }
                if (!placed) Debug.LogWarning("Combat: no free sidewalk spot for the " + def.DisplayName + " kiosk in its districts");
            }

            // Every kiosk keeps 2.5 m of free curb from any parked car (the V3 cars are wider than the old ones).
            int removed = 0;
            foreach (Transform k in kiosks)
                for (int i = _carColliders.Count - 1; i >= 0; i--)
                {
                    var c = _carColliders[i];
                    if (c == null) continue;
                    var q = new Vector3(k.position.x, c.bounds.center.y, k.position.z);
                    if (c.bounds.SqrDistance(q) >= 2.5f * 2.5f) continue;
                    Object.DestroyImmediate(c.gameObject);
                    _carColliders.RemoveAt(i);
                    removed++;
                }
            if (removed > 0) Debug.Log("Kiosks: cleared " + removed + " parked cars inside 2.5 m");
        }

        static bool CombatSpotFree(Vector3 basePos, float yaw, Vector3 halfExtents, float centerHeight)
        {
            var center = basePos + Vector3.up * centerHeight;
            return !Physics.CheckBox(center, halfExtents, Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
        }

        static void MakeKiosk(WeaponDefinition def, Transform parent, Vector3 pos, float yaw)
        {
            var look = new Look { Paint = Palette.DoorNavy, Frame = Palette.FrameDark };
            var model = TryModel("WeaponKiosk");
            GameObject root;
            if (model != null)
            {
                root = SpawnCombat(model, parent, pos, yaw, look, "Kiosk_" + def.Id);
            }
            else
            {
                root = Node("Kiosk_" + def.Id, parent, pos).gameObject;
                root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                Box("Body", root.transform, new Vector3(0f, 1.15f, -0.05f), new Vector3(1.3f, 2.3f, 0.5f), Mat("KioskBody", Palette.DoorNavy), false);
                Box("Glass", root.transform, new Vector3(0f, 1.1f, 0.22f), new Vector3(1.1f, 1.2f, 0.06f), Mat("KioskGlass", Palette.GlassTint), false);
                Box("Screen", root.transform, new Vector3(0f, 2.0f, 0.21f), new Vector3(1.1f, 0.3f, 0.04f), Mat("KioskScreenStandIn", Palette.Accent, 1.5f), false);
            }

            // The cabinet glass is see-through so the gun on sale shows.
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] != null && ms[i].name.StartsWith("S_Glass")) ms[i] = KioskGlassMat();
                r.sharedMaterials = ms;
            }

            var display = FindDeep(root.transform, "Display");
            if (display == null) display = Node("Display", root.transform, new Vector3(0f, 1.1f, 0.05f));

            // The gun on sale, centered on the Display point so it spins in place.
            var gunModel = TryModel(def.ModelName);
            GameObject gun = gunModel != null
                ? SpawnCombat(gunModel, display, Vector3.zero, 0f, DefaultLook, "DisplayGun")
                : StandInGun("DisplayGun", display);
            foreach (var c in gun.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            if (CombatBounds(gun, null, out var gb))
            {
                float longest = Mathf.Max(gb.size.x, gb.size.y, gb.size.z);
                float scale = Mathf.Min(1f, 0.7f / Mathf.Max(0.01f, longest));
                gun.transform.localScale = Vector3.one * scale;
                gun.transform.localPosition = -gb.center * scale;
            }

            // Price label on the screen panel (or a guessed spot), readable from the front (+Z).
            Vector3 labelPos = new Vector3(0f, 2.03f, 0.3f);
            Vector2 labelSize = new Vector2(1.1f, 0.34f);
            if (CombatBounds(root, r => r.sharedMaterial != null && r.sharedMaterial.name.Contains("KioskScreen"), out var sb)
                && sb.size.x > 0.2f)
            {
                labelPos = new Vector3(sb.center.x, sb.center.y, sb.max.z + 0.012f);
                labelSize = new Vector2(sb.size.x * 0.92f, Mathf.Max(0.15f, sb.size.y * 0.92f));
            }
            var labelGo = new GameObject("PriceLabel");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = labelPos;
            labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var label = labelGo.AddComponent<TextMeshPro>();
            label.font = TMP_Settings.defaultFontAsset;
            label.fontSharedMaterial = ShellBuilder.GetOrCreateOutlineMaterial();
            label.enableAutoSizing = true;
            label.fontSizeMin = 0.5f;
            label.fontSizeMax = 3f;
            label.fontStyle = FontStyles.Bold;
            label.color = Palette.Shadow; // dark on the bright emissive panel
            label.alignment = TextAlignmentOptions.Center;
            label.text = def.DisplayName.ToUpperInvariant() + "\n" + def.Cost;
            label.rectTransform.sizeDelta = new Vector2(Mathf.Min(labelSize.x, 0.95f), Mathf.Min(labelSize.y, 0.19f));

            // A small warm light keeps the gun readable in shade.
            var lightGo = new GameObject("DisplayLight");
            lightGo.transform.SetParent(display, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.25f, 0.45f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.92f, 0.78f);
            light.intensity = 1.2f;
            light.range = 2.2f;
            light.shadows = LightShadows.None;

            FitBox(root);
            var kiosk = root.AddComponent<WeaponKiosk>();
            kiosk.Configure(def.Id, display, label);
        }

        static Material KioskGlassMat()
        {
            var m = LoadOrCreateMat("KioskGlassClear", _litShader);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = 3000;
            var c = new Color(0.6f, 0.78f, 0.85f, 0.14f);
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Smoothness", 0.95f);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ barrels

        static void BuildBarrels()
        {
            var blocks = CombatBlocks(false);
            if (blocks.Count == 0) return;
            int target = BarrelTarget + Mathf.Max(0, CombatBlocks(true).Count - 9) * 2;

            var barrels = Node("Barrels", _combatRoot, Vector3.zero);
            var wreckless = Mat("BarrelRed", Palette.BarrelRed);
            var placed = new List<Vector3>();
            int cluster = 0;
            int guard = 0;
            while (placed.Count < target && guard++ < 400)
            {
                var block = blocks[cluster % blocks.Count];
                int anchorType = (cluster / blocks.Count + cluster) % 3;   // 0 corner, 1 alley mouth, 2 street edge
                cluster++;

                Vector2 a;
                switch (anchorType)
                {
                    case 0:
                        a = block + new Vector2(_rng.Next(2) == 0 ? -11.3f : 11.3f, _rng.Next(2) == 0 ? -11.3f : 11.3f);
                        break;
                    case 1:
                        a = _rng.Next(2) == 0
                            ? block + new Vector2(Rf(-0.4f, 0.4f), _rng.Next(2) == 0 ? -9.3f : 9.3f)
                            : block + new Vector2(_rng.Next(2) == 0 ? -9.3f : 9.3f, Rf(-0.4f, 0.4f));
                        break;
                    default:
                    {
                        Vector2 n = SideNormals[_rng.Next(4)];
                        a = block + n * 11.4f + new Vector2(-n.y, n.x) * Rf(-5f, 5f);
                        break;
                    }
                }

                int want = 2 + _rng.Next(3);
                int made = 0;
                for (int i = 0; i < want * 3 && made < want && placed.Count < target; i++)
                {
                    Vector2 p2 = a + new Vector2(Rf(-0.9f, 0.9f), Rf(-0.9f, 0.9f));
                    var pos = new Vector3(p2.x, SidewalkTop, p2.y);
                    if (!BarrelSpotFree(pos, placed)) continue;
                    MakeBarrel(barrels, pos, Rf(0f, 360f), wreckless);
                    placed.Add(pos);
                    made++;
                }
            }
        }

        static bool BarrelSpotFree(Vector3 pos, List<Vector3> others)
        {
            if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(_playerRoot.position.x, _playerRoot.position.z)) < 3f) return false;
            foreach (var o in others) if ((new Vector2(o.x - pos.x, o.z - pos.z)).magnitude < 0.75f) return false;
            // Clear of walls, trees, furniture, kiosks and cars (the floor slab sits below the sphere).
            return !Physics.CheckCapsule(pos + Vector3.up * 0.55f, pos + Vector3.up * 0.9f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        static void MakeBarrel(Transform parent, Vector3 pos, float yaw, Material standInMat)
        {
            var model = TryModel("ExplosiveBarrel");
            GameObject go;
            if (model != null)
            {
                go = SpawnCombat(model, parent, pos, yaw, new Look { Paint = Palette.BarrelRed }, "Barrel");
            }
            else
            {
                go = Node("Barrel", parent, pos).gameObject;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                Prim(PrimitiveType.Cylinder, "Drum", go.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.45f, 0.6f), standInMat, false);
                Prim(PrimitiveType.Cylinder, "Band", go.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.62f, 0.07f, 0.62f),
                    Mat("HazardYellow", Palette.HazardYellow), false);
            }
            FitBox(go);
            SetLayerRecursive(go, GameLayers.Explosive);
            go.AddComponent<ExplosiveProp>().Configure(ExplosiveKind.Barrel, null);
            var ob = go.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Capsule;
            ob.center = new Vector3(0f, 0.45f, 0f);
            ob.radius = 0.33f;
            ob.height = 0.9f;
            ob.carving = true;
        }
    }
}
