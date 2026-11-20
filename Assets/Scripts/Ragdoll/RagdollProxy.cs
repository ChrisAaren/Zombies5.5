using UnityEngine;

namespace Zombies55
{
    // The humanoid bones that carry a rigidbody: 12 bodies, like Unity's Ragdoll Wizard with the spine split in two.
    public enum RagBone
    {
        Hips, Spine, Chest, Head,
        LeftUpperArm, LeftLowerArm, RightUpperArm, RightLowerArm,
        LeftUpperLeg, LeftLowerLeg, RightUpperLeg, RightLowerLeg,
    }

    public static class RagBones
    {
        public const int Count = 12;

        // Resolves a ragdoll bone on any Humanoid animator (the chest body rides the UpperChest when the avatar maps it).
        public static Transform Resolve(Animator a, RagBone b)
        {
            switch (b)
            {
                case RagBone.Hips: return a.GetBoneTransform(HumanBodyBones.Hips);
                case RagBone.Spine: return a.GetBoneTransform(HumanBodyBones.Spine);
                case RagBone.Chest:
                {
                    var t = a.GetBoneTransform(HumanBodyBones.UpperChest);
                    if (t == null) t = a.GetBoneTransform(HumanBodyBones.Chest);
                    return t;
                }
                case RagBone.Head: return a.GetBoneTransform(HumanBodyBones.Head);
                case RagBone.LeftUpperArm: return a.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                case RagBone.LeftLowerArm: return a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                case RagBone.RightUpperArm: return a.GetBoneTransform(HumanBodyBones.RightUpperArm);
                case RagBone.RightLowerArm: return a.GetBoneTransform(HumanBodyBones.RightLowerArm);
                case RagBone.LeftUpperLeg: return a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                case RagBone.LeftLowerLeg: return a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                case RagBone.RightUpperLeg: return a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                default: return a.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            }
        }

        // Share of a blast's velocity each body takes: hips and torso take all of it, the limbs a bit less.
        public static float ThrowWeight(RagBone b)
        {
            switch (b)
            {
                case RagBone.Hips: case RagBone.Spine: case RagBone.Chest: return 1f;
                case RagBone.Head: return 0.9f;
                default: return 0.8f;
            }
        }
    }

    // One pooled ragdoll skeleton: 12 rigidbodies with colliders and character joints, built at editor time in the character's idle
    // pose (so the joint limits are relative to a natural standing pose). It lives inactive in the scene and costs nothing there. When a
    // character goes down, RagdollPool hands out one; its bodies take the character's pose and velocities, and RagdollRig copies the
    // body rotations back onto the character's bones every frame. The proxy is not a child of the character, so the character's root
    // can move freely while it is ragdolled.
    public class RagdollProxy : MonoBehaviour
    {
        [SerializeField] int archetype;
        [SerializeField] Rigidbody[] bodies = new Rigidbody[RagBones.Count];
        [Tooltip("The character's forward axis in the chest body's frame (idle pose): which way the belly faces.")]
        [SerializeField] Vector3 chestFront = Vector3.forward;
        [SerializeField] Vector3 hipsFront = Vector3.forward;
        [Tooltip("The bodies' local pose in the idle pose. The joints take their limits from the pose they are created in, so every use starts here.")]
        [SerializeField] Vector3[] restPos;
        [SerializeField] Quaternion[] restRot;

        public int Archetype => archetype;
        public Rigidbody[] Bodies => bodies;
        public Vector3 ChestFront => chestFront;
        public Vector3 HipsFront => hipsFront;
        public bool InUse { get; internal set; }
        public bool Counted { get; internal set; }
        public bool Frozen { get; private set; }

        Transform[] _tf;

        public Transform BodyTransform(RagBone b)
        {
            if (_tf == null)
            {
                _tf = new Transform[bodies.Length];
                for (int i = 0; i < bodies.Length; i++) _tf[i] = bodies[i].transform;
            }
            return _tf[(int)b];
        }

        public Rigidbody Body(RagBone b) => bodies[(int)b];

        // The bodies stop simulating and stay where they are (a corpse that is about to sink away).
        public void Freeze()
        {
            if (Frozen) return;
            Frozen = true;
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].isKinematic = true;
                bodies[i].detectCollisions = false;
            }
        }

        internal void ResetState()
        {
            Frozen = false;
            // Back to the idle pose before the bodies (and their joints) come into being on SetActive.
            if (restPos != null && restPos.Length == bodies.Length)
                for (int i = 0; i < bodies.Length; i++)
                    bodies[i].transform.SetLocalPositionAndRotation(restPos[i], restRot[i]);
            for (int i = 0; i < bodies.Length; i++)
            {
                var rb = bodies[i];
                if (rb == null) continue;
                rb.isKinematic = false;
                rb.detectCollisions = true;
            }
        }

        // Editor-time setup (SceneBuilder).
        public void Configure(int archetypeId, Rigidbody[] bodyList, Vector3 chestFrontLocal, Vector3 hipsFrontLocal)
        {
            archetype = archetypeId;
            bodies = bodyList;
            chestFront = chestFrontLocal;
            hipsFront = hipsFrontLocal;
            restPos = new Vector3[bodyList.Length];
            restRot = new Quaternion[bodyList.Length];
            for (int i = 0; i < bodyList.Length; i++) bodyList[i].transform.GetLocalPositionAndRotation(out restPos[i], out restRot[i]);
        }
    }
}
