using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Zombies55.EditorTools
{
    // Ragdoll skeletons: the pooled proxies (12 rigidbodies, colliders and character joints per skeleton) built from the Humanoid
    // bones of the enemy and the player model in their idle pose, the Ragdoll layer, and the rig components on the characters.
    public static partial class SceneBuilder
    {
        const string RagdollMaterialPath = "Assets/Settings/RagdollSkin.asset";
        public const int RagdollArchetypeHollow = 0;
        public const int RagdollArchetypeCourier = 1;
        const int RagdollEnemyPoolSize = 16;   // EffectsDetail's highest ragdoll limit

        // Layer 13 "Ragdoll": no collisions with the player's controller, the enemies, the debris or other ragdolls.
        static void EnsureRagdollLayer()
        {
            var tm = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset");
            if (tm == null) return;
            var so = new SerializedObject(tm);
            var layers = so.FindProperty("layers");
            var l = layers.GetArrayElementAtIndex(GameLayers.Ragdoll);
            if (l.stringValue == "Ragdoll") return;
            if (!string.IsNullOrEmpty(l.stringValue)) throw new InvalidOperationException("Layer " + GameLayers.Ragdoll + " is taken by '" + l.stringValue + "'.");
            l.stringValue = "Ragdoll";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void IgnoreRagdollCollisions()
        {
            Physics.IgnoreLayerCollision(GameLayers.Ragdoll, GameLayers.Player, true);
            Physics.IgnoreLayerCollision(GameLayers.Ragdoll, GameLayers.Enemy, true);
            Physics.IgnoreLayerCollision(GameLayers.Ragdoll, GameLayers.Debris, true);
            Physics.IgnoreLayerCollision(GameLayers.Ragdoll, GameLayers.Ragdoll, true);
        }

        static void AddRagdollRig(GameObject character, int archetype, bool counted, bool followRoot, Transform root)
        {
            var rig = character.AddComponent<RagdollRig>();
            rig.Configure(archetype, counted, followRoot, root);
        }

        static void BuildRagdolls()
        {
            var root = new GameObject("Ragdolls");
            root.transform.SetParent(_game, false);
            var pool = root.AddComponent<RagdollPool>();
            var hollow = BuildProxyGroup(root.transform, RagdollArchetypeHollow, "Hollow", "MPFB_Hollow", "EnemyIdle", RagdollEnemyPoolSize);
            var courier = BuildProxyGroup(root.transform, RagdollArchetypeCourier, "Courier", "MPFB_Courier", "Idle", 1);

            var so = new SerializedObject(pool);
            var groups = so.FindProperty("groups");
            groups.arraySize = 2;
            WriteGroup(groups.GetArrayElementAtIndex(0), RagdollArchetypeHollow, hollow);
            WriteGroup(groups.GetArrayElementAtIndex(1), RagdollArchetypeCourier, courier);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WriteGroup(SerializedProperty g, int archetype, RagdollProxy[] proxies)
        {
            g.FindPropertyRelative("archetype").intValue = archetype;
            var arr = g.FindPropertyRelative("proxies");
            arr.arraySize = proxies.Length;
            for (int i = 0; i < proxies.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = proxies[i];
        }

        static RagdollProxy[] BuildProxyGroup(Transform parent, int archetype, string label, string model, string idleClip, int count)
        {
            var list = new RagdollProxy[count];
            var first = BuildProxy(model, idleClip, archetype);
            first.name = "Ragdoll_" + label + "_01";
            first.transform.SetParent(parent, false);
            first.gameObject.SetActive(false);
            list[0] = first;
            for (int i = 1; i < count; i++)
            {
                var copy = Object.Instantiate(first, parent);
                copy.name = "Ragdoll_" + label + "_" + (i + 1).ToString("00");
                copy.gameObject.SetActive(false);
                list[i] = copy;
            }
            return list;
        }

        static PhysicsMaterial RagdollMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(RagdollMaterialPath);
            if (m == null)
            {
                m = new PhysicsMaterial("RagdollSkin");
                AssetDatabase.CreateAsset(m, RagdollMaterialPath);
            }
            m.dynamicFriction = 0.7f;
            m.staticFriction = 0.8f;
            m.bounciness = 0f;
            m.frictionCombine = PhysicsMaterialCombine.Average;
            m.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(m);
            return m;
        }

        // The model in its first idle frame (standing, arms down): the joint limits are relative to this pose.
        static void PoseIdle(GameObject temp, Animator animator, AnimationClip clip)
        {
            var graph = PlayableGraph.Create("RagdollPose");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "pose", animator);
            var cp = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(cp);
            cp.SetTime(0.0);
            graph.Evaluate(0f);
            graph.Destroy();
        }

        static RagdollProxy BuildProxy(string model, string idleClip, int archetype)
        {
            var temp = (GameObject)Object.Instantiate(Model(model));
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = Vector3.one;
            var animator = temp.GetComponent<Animator>();
            if (animator == null) animator = temp.AddComponent<Animator>();
            var avatar = AvatarAt(AssetDatabase.GetAssetPath(Model(model)));
            if (avatar == null || !avatar.isHuman) throw new InvalidOperationException(model + " has no humanoid avatar.");
            animator.avatar = avatar;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            PoseIdle(temp, animator, LoadClip(idleClip));

            Transform B(RagBone b) { return RagBones.Resolve(animator, b); }
            Vector3 P(RagBone b) { return B(b).position; }
            Transform neckT = animator.GetBoneTransform(HumanBodyBones.Neck);
            Vector3 neck = neckT != null ? neckT.position : P(RagBone.Head);
            Vector3 lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
            Vector3 rHand = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            Vector3 lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            Vector3 rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
            float shoulderW = Vector3.Distance(P(RagBone.LeftUpperArm), P(RagBone.RightUpperArm));
            float hipsW = Vector3.Distance(P(RagBone.LeftUpperLeg), P(RagBone.RightUpperLeg));

            var go = new GameObject("Ragdoll");
            go.layer = GameLayers.Ragdoll;
            var proxy = go.AddComponent<RagdollProxy>();
            var mat = RagdollMaterial();
            var bodies = new Rigidbody[RagBones.Count];

            Rigidbody Body(RagBone b, float mass)
            {
                var g = new GameObject(b.ToString());
                g.transform.SetParent(go.transform, false);
                g.transform.SetPositionAndRotation(B(b).position, B(b).rotation);
                g.layer = GameLayers.Ragdoll;
                var rb = g.AddComponent<Rigidbody>();
                rb.mass = mass;
                rb.useGravity = true;
                rb.linearDamping = b >= RagBone.LeftUpperArm ? 0.2f : 0.08f;
                rb.angularDamping = b >= RagBone.LeftUpperArm ? 2.2f : 1.2f;   // limbs wobble last: more damping so the ragdoll comes to rest
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;   // cheap, and keeps limbs out of the ground at speed
                rb.solverIterations = 14;
                rb.solverVelocityIterations = 6;
                rb.maxAngularVelocity = 20f;
                rb.maxDepenetrationVelocity = 3f;
                rb.sleepThreshold = 0.15f;
                bodies[(int)b] = rb;
                return rb;
            }

            void Capsule(Rigidbody rb, Vector3 a, Vector3 b, float radius)
            {
                var t = rb.transform;
                var c = rb.gameObject.AddComponent<CapsuleCollider>();
                Vector3 dirW = b - a;
                float len = dirW.magnitude;
                Vector3 dl = t.InverseTransformDirection(dirW.normalized);
                int axis = 0;
                if (Mathf.Abs(dl.y) > Mathf.Abs(dl[axis])) axis = 1;
                if (Mathf.Abs(dl.z) > Mathf.Abs(dl[axis])) axis = 2;
                c.direction = axis;
                c.center = t.InverseTransformPoint((a + b) * 0.5f);
                c.radius = radius;
                c.height = Mathf.Max(len + radius, radius * 2.05f);
                c.sharedMaterial = mat;
            }

            void Box(Rigidbody rb, Vector3 center, Vector3 worldSize)
            {
                var t = rb.transform;
                var c = rb.gameObject.AddComponent<BoxCollider>();
                c.center = t.InverseTransformPoint(center);
                var size = Vector3.zero;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 ax = t.TransformDirection(i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
                    int k = 0;
                    if (Mathf.Abs(ax.y) > Mathf.Abs(ax[k])) k = 1;
                    if (Mathf.Abs(ax.z) > Mathf.Abs(ax[k])) k = 2;
                    size[i] = worldSize[k];
                }
                c.size = size;
                c.sharedMaterial = mat;
            }

            CharacterJoint Joint(Rigidbody child, Rigidbody parent, Vector3 twist, Vector3 swing, float lowTwist, float highTwist, float swing1, float swing2)
            {
                var j = child.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parent;
                j.autoConfigureConnectedAnchor = true;
                j.anchor = Vector3.zero;
                twist = twist.normalized;
                swing = Vector3.ProjectOnPlane(swing, twist).normalized;
                j.axis = child.transform.InverseTransformDirection(twist);
                j.swingAxis = child.transform.InverseTransformDirection(swing);
                j.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
                j.highTwistLimit = new SoftJointLimit { limit = highTwist };
                j.swing1Limit = new SoftJointLimit { limit = swing1 };
                j.swing2Limit = new SoftJointLimit { limit = swing2 };
                j.enableProjection = true;
                j.projectionDistance = 0.05f;
                j.projectionAngle = 5f;
                j.enablePreprocessing = false;
                j.enableCollision = false;
                return j;
            }

            // ---- torso and head
            Vector3 hipsP = P(RagBone.Hips), spineP = P(RagBone.Spine), chestP = P(RagBone.Chest), headP = P(RagBone.Head);
            var hips = Body(RagBone.Hips, 11f);
            float hipsH = Mathf.Max(0.16f, Vector3.Distance(hipsP, spineP) + 0.08f);
            Box(hips, hipsP + (spineP - hipsP) * 0.35f, new Vector3(Mathf.Max(0.28f, hipsW * 1.25f), hipsH, 0.2f));

            var spine = Body(RagBone.Spine, 6f);
            Box(spine, (spineP + chestP) * 0.5f, new Vector3(shoulderW * 0.82f, Vector3.Distance(spineP, chestP) + 0.02f, 0.19f));
            Joint(spine, hips, Vector3.up, Vector3.right, -20f, 20f, 30f, 20f);

            var chest = Body(RagBone.Chest, 14f);
            float chestH = Vector3.Distance(chestP, neck) + 0.04f;
            Box(chest, chestP + (neck - chestP) * 0.5f, new Vector3(shoulderW * 0.95f, chestH, 0.22f));
            Joint(chest, spine, Vector3.up, Vector3.right, -20f, 20f, 25f, 18f);

            var head = Body(RagBone.Head, 5f);
            var sphere = head.gameObject.AddComponent<SphereCollider>();
            sphere.radius = 0.1f;
            sphere.center = head.transform.InverseTransformPoint(headP + Vector3.up * 0.09f);
            sphere.sharedMaterial = mat;
            Joint(head, chest, Vector3.up, Vector3.right, -35f, 35f, 40f, 30f);

            // ---- arms and legs
            void Arm(RagBone upper, RagBone lower, Vector3 hand)
            {
                Vector3 ua = P(upper), la = P(lower);
                var u = Body(upper, 2.5f);
                Capsule(u, ua, la, 0.05f);
                Joint(u, chest, la - ua, Vector3.right, -70f, 70f, 90f, 95f);
                var l = Body(lower, 2.2f);
                Capsule(l, la, la + (hand - la) * 1.3f, 0.04f);
                // Elbow: a hinge whose axis runs across the arm; flexion brings the hand forward (positive twist about the axis).
                Vector3 d = (hand - la).normalized;
                Joint(l, u, Vector3.Cross(d, Vector3.forward), d, -8f, 150f, 8f, 8f);
            }
            Arm(RagBone.LeftUpperArm, RagBone.LeftLowerArm, lHand);
            Arm(RagBone.RightUpperArm, RagBone.RightLowerArm, rHand);

            void Leg(RagBone upper, RagBone lower, Vector3 foot)
            {
                Vector3 ul = P(upper), ll = P(lower);
                var u = Body(upper, 9f);
                Capsule(u, ul, ll, 0.08f);
                Joint(u, hips, ll - ul, Vector3.right, -35f, 35f, 75f, 45f);
                var l = Body(lower, 5f);
                Capsule(l, ll, foot, 0.06f);
                // Knee: a hinge across the leg; flexion swings the foot backwards.
                Vector3 d = (foot - ll).normalized;
                Joint(l, u, Vector3.Cross(Vector3.forward, d), d, -8f, 140f, 8f, 8f);
            }
            Leg(RagBone.LeftUpperLeg, RagBone.LeftLowerLeg, lFoot);
            Leg(RagBone.RightUpperLeg, RagBone.RightLowerLeg, rFoot);

            proxy.Configure(archetype, bodies,
                bodies[(int)RagBone.Chest].transform.InverseTransformDirection(Vector3.forward),
                bodies[(int)RagBone.Hips].transform.InverseTransformDirection(Vector3.forward));

            Object.DestroyImmediate(temp);
            return proxy;
        }
    }
}
