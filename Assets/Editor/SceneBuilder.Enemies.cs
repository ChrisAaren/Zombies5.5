using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Zombies55.EditorTools
{
    // Enemy setup for the scene: enemy template, round manager, spawn points, NavMesh bake.
    // Runs last among the world builders so every collider exists before the bake.
    public static partial class SceneBuilder
    {
        const string NavMeshDir = "Assets/Scenes/Zombies55";
        const string NavMeshDataPath = NavMeshDir + "/NavMesh.asset";

        static void BuildEnemies()
        {
            ConfigureAgentType();

            var enemiesRoot = Root("Enemies");

            // Player health lives on the player root.
            _playerRoot.gameObject.AddComponent<PlayerHealth>();

            // Round manager, audio and shared effects.
            var roundsGo = new GameObject("Rounds");
            roundsGo.transform.SetParent(_game, false);
            var rounds = roundsGo.AddComponent<RoundManager>();
            roundsGo.AddComponent<RoundAudio>();
            var effects = roundsGo.AddComponent<EnemyEffects>();

            var dust = MakeSystem("EnemyDust", roundsGo.transform, 1f, false, false, 64);
            {
                var main = dust.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
                main.gravityModifier = -0.05f;
                main.startColor = Palette.WithAlpha(Palette.ConcreteTint, 0.8f);
                var shape = dust.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.3f;
            }
            SetRef(effects, "dustPuff", dust);

            var template = BuildEnemyTemplate(enemiesRoot);
            SetRef(rounds, "enemyTemplate", template);

            // The district barriers carry carving obstacles. Even with ignoreNavMeshObstacle set, a bake or a path
            // query made while they are enabled cuts a hole along every barrier line, so no cell connects to the
            // player. Keep them off for the bake and the connectivity check; they carve at runtime on top of the data.
            var obstacles = Object.FindObjectsByType<NavMeshObstacle>(FindObjectsInactive.Include);
            var wasEnabled = new bool[obstacles.Length];
            for (int i = 0; i < obstacles.Length; i++) { wasEnabled[i] = obstacles[i].enabled; obstacles[i].enabled = false; }

            // NavMesh over everything that has a physics collider on the walkable layers.
            BuildNavMesh();

            // Spawn points, kept only when they sit on the baked NavMesh and connect to the player.
            var points = ValidSpawnPoints();
            for (int i = 0; i < obstacles.Length; i++) obstacles[i].enabled = wasEnabled[i];
            var so = new SerializedObject(rounds);
            var prop = so.FindProperty("spawnPoints");
            prop.arraySize = points.Count;
            for (int i = 0; i < points.Count; i++) prop.GetArrayElementAtIndex(i).vector3Value = points[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("Enemies: " + points.Count + " spawn points on the NavMesh");
        }

        static Enemy BuildEnemyTemplate(Transform parent)
        {
            var go = new GameObject("EnemyTemplate");
            go.transform.SetParent(parent, false);

            var agent = go.AddComponent<NavMeshAgent>();
            agent.agentTypeID = 0;
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.speed = Enemy.ShambleSpeed;
            agent.acceleration = 14f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 1.2f;
            agent.autoBraking = true;
            agent.updateRotation = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = 50;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            go.AddComponent<Enemy>();

            var character = BuildHollow(go.transform);
            character.AddComponent<EnemyAnimation>();

            // Hitboxes ride the animated bones, so a headshot lands on the head wherever the clip puts it. Shapes are
            // sized in the rest pose (upright, template at the origin), then parented to their bone.
            Transform Bone(HumanBodyBones b) { return HBone(character, b); }
            var skin = SkinRestBounds(character);
            var headBone = Bone(HumanBodyBones.Head);
            var skull = SkinBoundsAbove(character, headBone.position.y);
            float headR = Mathf.Max(skull.extents.x, skull.extents.z) * 1.05f;
            // The face (eyes, nose) sits at the front of the skull bounds, so the sphere is nudged forward to keep it inside.
            AddBoneHitbox(headBone, "Hit_Head", HitZone.Head, new Vector3(skull.center.x, skull.max.y - headR, skull.center.z + 0.02f), c =>
            {
                var sp = c.AddComponent<SphereCollider>();
                sp.radius = headR;
            });
            var chestBone = UpperTorso(character);
            var spineBone = Bone(HumanBodyBones.Spine);
            var neckBone = Bone(HumanBodyBones.Neck);
            float torsoBottom = spineBone.position.y + 0.05f;
            float torsoTop = neckBone.position.y - 0.01f;
            AddBoneHitbox(chestBone, "Hit_Torso", HitZone.Body, new Vector3(0f, (torsoBottom + torsoTop) * 0.5f, skin.center.z), c =>
            {
                var bx = c.AddComponent<BoxCollider>();
                bx.size = new Vector3(0.36f, torsoTop - torsoBottom, 0.26f);
            });
            var hipsBone = Bone(HumanBodyBones.Hips);
            AddBoneHitbox(hipsBone, "Hit_Hips", HitZone.Body, new Vector3(0f, (hipsBone.position.y + spineBone.position.y) * 0.5f, skin.center.z), c =>
            {
                var bx = c.AddComponent<BoxCollider>();
                bx.size = new Vector3(0.34f, 0.22f, 0.24f);
            });
            AddLimbHitbox(Bone(HumanBodyBones.LeftUpperLeg), Bone(HumanBodyBones.LeftLowerLeg), "Hit_LeftUpperLeg", 0.095f);
            AddLimbHitbox(Bone(HumanBodyBones.LeftLowerLeg), Bone(HumanBodyBones.LeftFoot), "Hit_LeftLowerLeg", 0.075f);
            AddLimbHitbox(Bone(HumanBodyBones.RightUpperLeg), Bone(HumanBodyBones.RightLowerLeg), "Hit_RightUpperLeg", 0.095f);
            AddLimbHitbox(Bone(HumanBodyBones.RightLowerLeg), Bone(HumanBodyBones.RightFoot), "Hit_RightLowerLeg", 0.075f);

            SetLayerRecursive(go, GameLayers.Enemy);
            go.SetActive(false);
            return go.GetComponent<Enemy>();
        }

        // Collider object at a rest-pose world position (template at the origin), parented to the bone.
        static GameObject AddBoneHitbox(Transform bone, string name, HitZone zone, Vector3 worldPos, System.Action<GameObject> addCollider)
        {
            var c = new GameObject(name);
            c.transform.SetPositionAndRotation(worldPos, Quaternion.identity);
            c.transform.SetParent(bone, true);
            addCollider(c);
            c.AddComponent<Hitbox>().SetZone(zone);
            return c;
        }

        // Capsule between two bones in the rest pose (the legs hang vertically), following the upper bone.
        static void AddLimbHitbox(Transform from, Transform to, string name, float radius)
        {
            Vector3 a = from.position, b = to.position;
            AddBoneHitbox(from, name, HitZone.Body, (a + b) * 0.5f, c =>
            {
                var k = c.AddComponent<CapsuleCollider>();
                k.direction = 1;
                k.radius = radius;
                k.height = Vector3.Distance(a, b) + radius * 1.6f;
            });
        }

        // Humanoid agent type: radius 0.35, height 1.8, step 0.4, slope 45 (stored in ProjectSettings/NavMeshAreas.asset).
        static void ConfigureAgentType()
        {
            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
                if (assets == null || assets.Length == 0) throw new System.Exception("NavMeshAreas.asset not found");
                var so = new SerializedObject(assets[0]);
                var list = so.FindProperty("m_Settings");
                for (int i = 0; i < list.arraySize; i++)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("agentTypeID").intValue != 0) continue;
                    e.FindPropertyRelative("agentRadius").floatValue = 0.35f;
                    e.FindPropertyRelative("agentHeight").floatValue = 1.8f;
                    e.FindPropertyRelative("agentClimb").floatValue = 0.4f;
                    e.FindPropertyRelative("agentSlope").floatValue = 45f;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(assets[0]);
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Could not set the NavMesh agent type (radius 0.35, height 1.8, step 0.4, slope 45); set it in Window > AI > Navigation. " + ex.Message);
            }

            var s = NavMesh.GetSettingsByID(0);
            if (Mathf.Abs(s.agentRadius - 0.35f) > 0.01f)
                Debug.LogWarning("NavMesh agent radius is " + s.agentRadius + ", expected 0.35. Set the Humanoid agent type in the Navigation window and rebuild.");
        }

        static void BuildNavMesh()
        {
            EnsureFolder(NavMeshDir);
            AssetDatabase.DeleteAsset(NavMeshDataPath);

            var go = new GameObject("NavMeshSurface");
            go.transform.SetParent(_env, false);
            var surface = go.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            int excluded = (1 << GameLayers.IgnoreRaycast) | (1 << GameLayers.Enemy) | (1 << GameLayers.Explosive) | (1 << GameLayers.Player) | (1 << GameLayers.Barrier);
            surface.layerMask = ~excluded;
            surface.defaultArea = 0;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;

            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                Debug.LogWarning("NavMesh bake produced no data.");
                return;
            }
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshDataPath);
            surface.AddData();
            EditorUtility.SetDirty(surface);
        }

        // Spawn points per district cell: at least 6 each (up to 14), all on the baked NavMesh, connected to the
        // player, and 3 m or more inside their cell so none sits on a barrier line.
        const int SpawnPointsMin = 6;
        const int SpawnPointsMax = 14;

        static List<Vector3> ValidSpawnPoints()
        {
            var origin = _playerRoot.position;
            if (NavMesh.SamplePosition(origin, out var originHit, 3f, NavMesh.AllAreas)) origin = originHit.position;

            var path = new NavMeshPath();
            var valid = new List<Vector3>();
            float cs = ZoneLayout.CellSize;
            for (int ix = -ZoneLayout.Half; ix <= ZoneLayout.Half; ix++)
                for (int iz = -ZoneLayout.Half; iz <= ZoneLayout.Half; iz++)
                {
                    var center = ZoneLayout.CellCenter(ix, iz);
                    List<Vector3> chosen = null;
                    // Strict first, then relax margin and spacing until the cell has enough points.
                    foreach (var pass in new[] { (margin: 3f, gap: 4f), (margin: 2.5f, gap: 2.5f), (margin: 2f, gap: 1.5f) })
                    {
                        chosen = CellSpawnPoints(center, cs * 0.5f - pass.margin, pass.gap, origin, path);
                        if (chosen.Count >= SpawnPointsMin) break;
                    }
                    if (chosen.Count < SpawnPointsMin)
                        Debug.LogWarning("Enemies: cell (" + ix + "," + iz + ") has only " + chosen.Count + " spawn points");
                    valid.AddRange(chosen);
                }
            return valid;
        }

        static List<Vector3> CellSpawnPoints(Vector2 center, float reach, float gap, Vector3 origin, NavMeshPath path)
        {
            var found = new List<Vector3>();
            for (float dx = -reach; dx <= reach + 0.01f; dx += 4f)
                for (float dz = -reach; dz <= reach + 0.01f; dz += 4f)
                {
                    var c = new Vector3(center.x + dx, 0.05f, center.y + dz);
                    if (!NavMesh.SamplePosition(c, out var hit, 2f, NavMesh.AllAreas)) continue;
                    var p = hit.position;
                    if (Mathf.Abs(p.x - center.x) > reach || Mathf.Abs(p.z - center.y) > reach) continue;
                    if (!NavMesh.CalculatePath(origin, p, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                    found.Add(p);
                }

            // Farthest-point sampling keeps the picks spread over the cell.
            var picked = new List<Vector3>();
            while (found.Count > 0 && picked.Count < SpawnPointsMax)
            {
                int best = 0;
                float bestD = -1f;
                for (int i = 0; i < found.Count; i++)
                {
                    float d = float.MaxValue;
                    foreach (var q in picked) d = Mathf.Min(d, (found[i] - q).magnitude);
                    if (picked.Count == 0) d = (found[i] - new Vector3(center.x, 0f, center.y)).magnitude;
                    if (d > bestD) { bestD = d; best = i; }
                }
                if (picked.Count > 0 && bestD < gap) break;
                picked.Add(found[best]);
                found.RemoveAt(best);
            }
            return picked;
        }
    }
}
