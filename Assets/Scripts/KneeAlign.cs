using UnityEngine;

namespace Zombies55
{
    // Points both knees along the foot after the animation and the foot IK have run. The humanoid retarget carries each clip's
    // hip rotation onto a body with another hip width and leg length, and the foot IK then pins the ankles to goals scaled from
    // the clip skeleton, so the knee, which the IK takes from the pose, ends up beside the line from hip to ankle: legs that
    // arch out to the side. The ankle stays where the animation put it; only the knee's side is chosen again: the knee goes
    // over the foot, the direction of the toes, the way a real knee tracks over the second toe.
    public static class KneeAlign
    {
        public static void Apply(Animator anim, Transform frame)
        {
            Leg(anim, frame, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            Leg(anim, frame, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
        }

        static void Leg(Animator anim, Transform frame, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot, HumanBodyBones toes)
        {
            Transform up = anim.GetBoneTransform(upper), lo = anim.GetBoneTransform(lower), ft = anim.GetBoneTransform(foot), to = anim.GetBoneTransform(toes);
            if (up == null || lo == null || ft == null) return;
            Vector3 heading = to != null ? Vector3.ProjectOnPlane(to.position - ft.position, frame.up) : Vector3.zero;
            if (heading.sqrMagnitude < 1e-4f) heading = frame.forward;
            Vector3 ankle = ft.position;
            Quaternion footRot = ft.rotation;
            PlayerAnimation.SolveTwoBone(up, lo, ft, ankle, lo.position + heading.normalized);
            ft.rotation = footRot;
        }
    }
}
