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

            // NavMesh over everything that has a physics collider on the walkable layers.
            BuildNavMesh();

            // Spawn points, kept only when they sit on the baked NavMesh and connect to the player.
            var points = ValidSpawnPoints();
            var so = new SerializedObject(rounds);
            var prop = so.FindProperty("spawnPoints");
            prop.arraySize = points.Count;
            for (int i = 0; i < points.Count; i++) prop.GetArrayElementAtIndex(i).vector3Value = points[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("Enemies: " + points.Count + " spawn points on the NavMesh");
        }

        static Enemy BuildEnemyTemplate(Transform parent)
        {
            var hoodie = Mat("EnemyHoodie", Palette.EnemyHoodie);
            var pants = Mat("EnemyPants", Palette.EnemyPants);
            var skin = Mat("EnemySkin", Palette.EnemySkin);
            var eye = Mat("EnemyEye", Palette.EnemyEyeRed, 3f);

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
            var motion = go.AddComponent<CharacterMotion>();
            go.AddComponent<EnemyAttackPose>();

            // The rig reads the static eye material, so swap it for the glowing one while building.
            var prevEye = mEye;
            mEye = eye;
            BuildCharacterRig(go.transform, motion, hoodie, pants, skin, mShadow, HeadGear.Hood, hoodie, null, 1f, 1f);
            mEye = prevEye;

            // Lurching gait: slower, shorter strides, arms mostly limp.
            SetFloat(motion, "stepFrequency", 1.5f);
            SetFloat(motion, "legSwing", 30f);
            SetFloat(motion, "armSwing", 14f);
            SetFloat(motion, "accelLean", 14f);

            // Body: head y 1.56-1.79, torso and upper arms y 0.87-1.55 (x +-0.33), legs y 0-0.99 (x +-0.2).
            AddHitbox(go.transform, "Hit_Head", HitZone.Head, c =>
            {
                var s = c.AddComponent<SphereCollider>();
                s.center = new Vector3(0f, 1.67f, 0.01f);
                s.radius = 0.15f;
            });
            AddHitbox(go.transform, "Hit_Torso", HitZone.Body, c =>
            {
                var b = c.AddComponent<BoxCollider>();
                b.center = new Vector3(0f, 1.2f, 0f);
                b.size = new Vector3(0.64f, 0.68f, 0.32f);
            });
            AddHitbox(go.transform, "Hit_Legs", HitZone.Body, c =>
            {
                var k = c.AddComponent<CapsuleCollider>();
                k.center = new Vector3(0f, 0.5f, 0f);
                k.radius = 0.2f;
                k.height = 1.0f;
            });

            SetLayerRecursive(go, GameLayers.Enemy);
            go.SetActive(false);
            return go.GetComponent<Enemy>();
        }

        static void AddHitbox(Transform root, string name, HitZone zone, System.Action<GameObject> addCollider)
        {
            var c = new GameObject(name);
            c.transform.SetParent(root, false);
            addCollider(c);
            c.AddComponent<Hitbox>().SetZone(zone);
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
            int excluded = (1 << GameLayers.IgnoreRaycast) | (1 << GameLayers.Enemy) | (1 << GameLayers.Explosive) | (1 << GameLayers.Player);
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

        // About 40-50 candidates derived from the road and block grids, so a bigger city adds points by itself.
        static List<Vector3> ValidSpawnPoints()
        {
            var candidates = new List<Vector3>();

            // Intersections (the outermost ones are the ring road corners).
            foreach (float x in RoadCenters)
                foreach (float z in RoadCenters)
                    candidates.Add(new Vector3(x, 0.05f, z));

            // Mid-block street points: halfway between neighbouring intersections along every road.
            foreach (float r in RoadCenters)
            {
                for (int i = 0; i + 1 < RoadCenters.Length; i++)
                {
                    float mid = (RoadCenters[i] + RoadCenters[i + 1]) * 0.5f;
                    candidates.Add(new Vector3(r, 0.05f, mid));
                    candidates.Add(new Vector3(mid, 0.05f, r));
                }
            }

            // Alleys: the sidewalk lane at the south edge of every block.
            foreach (float cx in BlockCenters)
                foreach (float cz in BlockCenters)
                    candidates.Add(new Vector3(cx, SidewalkTop, cz - OuterLane));

            var origin = _playerRoot.position;
            if (NavMesh.SamplePosition(origin, out var originHit, 3f, NavMesh.AllAreas)) origin = originHit.position;

            var path = new NavMeshPath();
            var valid = new List<Vector3>();
            foreach (var c in candidates)
            {
                if (!NavMesh.SamplePosition(c, out var hit, 2.5f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(origin, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                valid.Add(hit.position);
            }
            return valid;
        }
    }
}
