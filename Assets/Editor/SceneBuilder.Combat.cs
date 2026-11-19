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

        static void BuildCombatPlayer()
        {
            var root = _playerRoot.gameObject;
            SetLayerRecursive(root, GameLayers.Player);
            root.AddComponent<PlayerWeapons>();
            root.AddComponent<CombatAudio>();
            var fx = root.AddComponent<CombatFX>();
            SetRef(fx, "particleMaterial", mParticle);
            var pose = root.AddComponent<AimPose>();

            var armL = _bodyPivot.Find("ArmL");
            var armR = _bodyPivot.Find("ArmR");

            // Hand holder at the end of the right arm; +Z of the holder is along the arm.
            float handY = -0.52f;
            if (CombatBounds(armR.gameObject, null, out var ab) && ab.size.sqrMagnitude > 0.01f) handY = ab.min.y + 0.06f;
            var holder = Node("HandHolder", armR, new Vector3(0f, handY, 0f));
            holder.localRotation = Quaternion.Euler(90f, 0f, 0f);

            foreach (var def in WeaponCatalog.All)
            {
                var model = TryModel(def.ModelName);
                GameObject gun = model != null
                    ? SpawnCombat(model, holder, Vector3.zero, 0f, DefaultLook, def.ModelName)
                    : StandInGun(def.ModelName, holder);
                foreach (var c in gun.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                gun.SetActive(def.Id == WeaponCatalog.StarterId);
            }

            SetRef(pose, "armL", armL);
            SetRef(pose, "armR", armR);
            SetRef(pose, "holder", holder);
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
            var blocks = CombatBlocks(false);
            if (blocks.Count == 0) return;

            // Farthest-point sampling spreads five kiosks over the map, seeded at the block nearest the plaza.
            var chosen = new List<Vector2>();
            Vector2 seed = blocks[0];
            foreach (var b in blocks) if (b.magnitude < seed.magnitude) seed = b;
            chosen.Add(seed);
            while (chosen.Count < 5)
            {
                Vector2 best = blocks[0];
                float bestD = -1f;
                foreach (var b in blocks)
                {
                    float d = float.MaxValue;
                    foreach (var c in chosen) d = Mathf.Min(d, (b - c).sqrMagnitude);
                    if (d > bestD) { bestD = d; best = b; }
                }
                if (bestD <= 0f && chosen.Count >= blocks.Count) { chosen.Add(blocks[chosen.Count % blocks.Count]); continue; }
                chosen.Add(best);
            }
            chosen.Sort((a, b) => a.magnitude.CompareTo(b.magnitude));

            var kiosks = Node("Kiosks", _combatRoot, Vector3.zero);
            string[] ids = { "wasp", "breaker", "kestrel", "longshot", "atlas" };
            for (int k = 0; k < ids.Length; k++)
            {
                var block = chosen[k];
                var def = WeaponCatalog.Get(ids[k]);

                // Sides ordered by how much they face the map center; first free spot wins.
                var order = new List<int> { 0, 1, 2, 3 };
                Vector2 toCenter = -block.normalized;
                order.Sort((a, b) => Vector2.Dot(SideNormals[b], toCenter).CompareTo(Vector2.Dot(SideNormals[a], toCenter)));
                bool placed = false;
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
                if (!placed) Debug.LogWarning("Combat: no free sidewalk spot for the " + def.DisplayName + " kiosk near block " + block);
            }
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
            Vector3 labelPos = new Vector3(0f, 2.0f, 0.3f);
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
            label.color = Palette.Accent;
            label.alignment = TextAlignmentOptions.Center;
            label.text = def.DisplayName.ToUpperInvariant() + "\n" + def.Cost;
            label.rectTransform.sizeDelta = labelSize;

            FitBox(root);
            var kiosk = root.AddComponent<WeaponKiosk>();
            kiosk.Configure(def.Id, display, label);
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
