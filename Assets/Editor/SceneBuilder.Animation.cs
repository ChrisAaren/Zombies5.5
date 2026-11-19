using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Zombies55.EditorTools
{
    // Generates the Animator controllers and the upper-body avatar mask for the humanoid character.
    // Parameter names are shared with PlayerAnimation and EnemyAnimation (see AnimParams there).
    public static partial class SceneBuilder
    {
        const string ControllerDir = AnimDir + "/Controllers";
        const string PlayerControllerPath = ControllerDir + "/PlayerAnimator.controller";
        const string EnemyControllerPath = ControllerDir + "/EnemyAnimator.controller";
        const string UpperMaskPath = ControllerDir + "/UpperBody.mask";

        [MenuItem("Tools/Zombies 5.5/Build Animator Controllers")]
        public static void BuildAnimatorControllers()
        {
            EnsureFolder(ControllerDir);
            var mask = BuildUpperMask();
            BuildPlayerController(mask);
            BuildEnemyController(mask);
            AssetDatabase.SaveAssets();
        }

        static AvatarMask BuildUpperMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask { name = "UpperBody" };
                AssetDatabase.CreateAsset(mask, UpperMaskPath);
            }
            foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                    || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                    || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers
                    || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, on);
            }
            EditorUtility.SetDirty(mask);
            return mask;
        }

        static AnimatorController NewController(string path)
        {
            AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration, float exitTime = -1f)
        {
            var t = from.AddTransition(to);
            t.hasFixedDuration = true;
            t.duration = duration;
            t.hasExitTime = exitTime >= 0f;
            t.exitTime = Mathf.Max(0f, exitTime);
            t.interruptionSource = TransitionInterruptionSource.None;
            return t;
        }

        static AnimatorStateTransition LinkAny(AnimatorStateMachine sm, AnimatorState to, float duration)
        {
            var t = sm.AddAnyStateTransition(to);
            t.hasFixedDuration = true;
            t.duration = duration;
            t.hasExitTime = false;
            t.canTransitionToSelf = false;
            return t;
        }

        static AnimatorState AddClipState(AnimatorStateMachine sm, string name, string clip, Vector3 pos)
        {
            var s = sm.AddState(name, pos);
            s.motion = LoadClip(clip);
            s.writeDefaultValues = true;
            return s;
        }

        // Ground speeds of PlayerController (walkSpeed 5, aim scale 0.55, sprintSpeed 8.5); keep in step with its defaults.
        const float PlayerRunSpeed = 5f;
        const float PlayerAimSpeed = 2.75f;
        const float PlayerSprintSpeed = 8.5f;

        // Adds a locomotion clip to a 2D blend tree at (x, z) m/s, authored at `authoredSpeed` m/s, time-scaled to match.
        static void AddLocoChild(BlendTree tree, string clip, float authoredSpeed, float x, float z)
        {
            float speed = Mathf.Sqrt(x * x + z * z);
            tree.AddChild(LoadClip(clip), new Vector2(x, z));
            var children = tree.children;
            children[children.Length - 1].timeScale = speed / authoredSpeed;
            tree.children = children;
        }

        static void BuildPlayerController(AvatarMask mask)
        {
            var c = NewController(PlayerControllerPath);
            c.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            c.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            c.AddParameter("LocoSpeed", AnimatorControllerParameterType.Float);
            c.AddParameter("ReloadSpeed", AnimatorControllerParameterType.Float);
            c.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
            c.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            c.AddParameter("WeaponType", AnimatorControllerParameterType.Int);   // 0 pistol set, 1 long-gun set
            c.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
            c.AddParameter("Reloading", AnimatorControllerParameterType.Bool);
            var ps = c.parameters;
            foreach (var p in ps) if (p.name == "LocoSpeed" || p.name == "ReloadSpeed") p.defaultFloat = 1f;
            c.parameters = ps;

            // ---- base layer: locomotion blend, jump / fall / land, death
            var sm = c.layers[0].stateMachine;
            var loco = c.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            tree.useAutomaticThresholds = false;
            // Each clip is placed at the ground speeds the player really moves at and played at the rate that makes the
            // clip's stride match that speed (child time scale = speed / authored speed), so a foot on the ground does not
            // slide. The same clip appears at two speeds where the player uses two (jog at the aim speed and at full run).
            tree.AddChild(LoadClip("Idle"), new Vector2(0f, 0f));
            AddLocoChild(tree, "Walk", 1.6f, 0f, 1.6f);
            AddLocoChild(tree, "Jog", 3.6f, 0f, PlayerAimSpeed);
            AddLocoChild(tree, "Jog", 3.6f, 0f, PlayerRunSpeed);
            AddLocoChild(tree, "Sprint", 8f, 0f, PlayerSprintSpeed);
            AddLocoChild(tree, "WalkBack", 1.4f, 0f, -1.4f);
            AddLocoChild(tree, "WalkBack", 1.4f, 0f, -PlayerAimSpeed);
            AddLocoChild(tree, "StrafeLeft", 1.6f, -1.6f, 0f);
            AddLocoChild(tree, "StrafeLeft", 1.6f, -PlayerAimSpeed, 0f);
            AddLocoChild(tree, "StrafeRight", 1.6f, 1.6f, 0f);
            AddLocoChild(tree, "StrafeRight", 1.6f, PlayerAimSpeed, 0f);
            loco.iKOnFeet = true;   // the clips' foot goals pin the feet to the ground through the retarget
            loco.speedParameterActive = true;
            loco.speedParameter = "LocoSpeed";
            sm.defaultState = loco;

            var jump = AddClipState(sm, "JumpStart", "JumpStart", new Vector3(560f, 0f));
            var fall = AddClipState(sm, "Fall", "Fall", new Vector3(560f, 100f));
            var land = AddClipState(sm, "Land", "Land", new Vector3(560f, 200f));
            var death = AddClipState(sm, "Death", "Death", new Vector3(300f, 260f));

            var t = Link(loco, jump, 0.05f); t.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            t = Link(loco, fall, 0.15f); t.AddCondition(AnimatorConditionMode.If, 0f, "Airborne");
            t = Link(jump, fall, 0.12f, 0.8f); t.AddCondition(AnimatorConditionMode.If, 0f, "Airborne");
            t = Link(jump, land, 0.1f, 0.3f); t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Airborne");
            t = Link(fall, land, 0.06f); t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Airborne");
            Link(land, loco, 0.2f, 0.5f);
            t = Link(land, jump, 0.05f); t.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            t = Link(land, fall, 0.1f); t.AddCondition(AnimatorConditionMode.If, 0f, "Airborne");
            t = LinkAny(sm, death, 0.15f); t.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

            // ---- upper-body layer: weapon hold, aim and reload over the legs' locomotion
            c.AddLayer("UpperBody");
            var layers = c.layers;
            var up = layers[1];
            up.avatarMask = mask;
            up.blendingMode = AnimatorLayerBlendingMode.Override;
            up.defaultWeight = 1f;
            c.layers = layers;
            var usm = c.layers[1].stateMachine;

            var pistol = AddClipState(usm, "PistolHold", "AimPistol", new Vector3(300f, 0f));
            var longLow = AddClipState(usm, "LongLow", "HoldRifleLow", new Vector3(300f, 100f));
            var longAim = AddClipState(usm, "LongAim", "AimRifle", new Vector3(300f, 200f));
            var rlPistol = AddClipState(usm, "ReloadPistol", "ReloadPistol", new Vector3(600f, 0f));
            var rlLong = AddClipState(usm, "ReloadLong", "ReloadRifle", new Vector3(600f, 150f));
            rlPistol.speedParameterActive = true; rlPistol.speedParameter = "ReloadSpeed";
            rlLong.speedParameterActive = true; rlLong.speedParameter = "ReloadSpeed";
            usm.defaultState = pistol;

            // Pistol set
            t = Link(pistol, rlPistol, 0.12f); Cond(t, "Reloading", true); CondInt(t, "WeaponType", 0);
            t = Link(rlPistol, pistol, 0.15f); Cond(t, "Reloading", false);
            t = Link(pistol, longLow, 0.2f); CondInt(t, "WeaponType", 1); Cond(t, "Aiming", false);
            t = Link(pistol, longAim, 0.2f); CondInt(t, "WeaponType", 1); Cond(t, "Aiming", true);
            t = Link(rlPistol, longLow, 0.2f); CondInt(t, "WeaponType", 1);
            // Long guns
            t = Link(longLow, longAim, 0.15f); Cond(t, "Aiming", true);
            t = Link(longAim, longLow, 0.2f); Cond(t, "Aiming", false);
            t = Link(longLow, rlLong, 0.15f); Cond(t, "Reloading", true); CondInt(t, "WeaponType", 1);
            t = Link(longAim, rlLong, 0.15f); Cond(t, "Reloading", true); CondInt(t, "WeaponType", 1);
            t = Link(rlLong, longLow, 0.2f); Cond(t, "Reloading", false);
            t = Link(longLow, pistol, 0.2f); CondInt(t, "WeaponType", 0);
            t = Link(longAim, pistol, 0.2f); CondInt(t, "WeaponType", 0);
            t = Link(rlLong, pistol, 0.2f); CondInt(t, "WeaponType", 0);

            EditorUtility.SetDirty(c);
        }

        static void Cond(AnimatorStateTransition t, string param, bool value)
        {
            t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
        }

        static void CondInt(AnimatorStateTransition t, string param, int value)
        {
            t.AddCondition(AnimatorConditionMode.Equals, value, param);
        }

        // Adds an enemy locomotion clip to the 1D blend tree at `speed` m/s, authored at `authoredSpeed`, time-scaled to match.
        static void AddEnemyLocoChild(BlendTree tree, string clip, float authoredSpeed, float speed)
        {
            tree.AddChild(LoadClip(clip), speed);
            var children = tree.children;
            children[children.Length - 1].timeScale = speed / authoredSpeed;
            tree.children = children;
        }

        static void BuildEnemyController(AvatarMask mask)
        {
            var c = NewController(EnemyControllerPath);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("CycleOffset", AnimatorControllerParameterType.Float);
            c.AddParameter("StrideScale", AnimatorControllerParameterType.Float);
            var eps = c.parameters;
            foreach (var p in eps) if (p.name == "StrideScale") p.defaultFloat = 1f;
            c.parameters = eps;
            c.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            var sm = c.layers[0].stateMachine;
            var loco = c.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            // Agent speeds vary +-8% round the nominal 1.6 / 3.2 / 5.0, so each gait covers a speed range with its clip
            // time-scaled to the speed (the same clip at the range's two ends): the stride follows the ground speed.
            tree.AddChild(LoadClip("EnemyIdle"), 0f);
            AddEnemyLocoChild(tree, "Shamble", 1.6f, 1.0f);
            AddEnemyLocoChild(tree, "Shamble", 1.6f, 2.2f);
            AddEnemyLocoChild(tree, "EnemyJog", 3.2f, 2.4f);
            AddEnemyLocoChild(tree, "EnemyJog", 3.2f, 4.0f);
            AddEnemyLocoChild(tree, "EnemySprint", 5f, 4.4f);
            AddEnemyLocoChild(tree, "EnemySprint", 5f, 5.6f);
            loco.iKOnFeet = true;   // the clips' foot goals pin the feet to the ground through the retarget
            loco.speedParameterActive = true;
            loco.speedParameter = "StrideScale";   // 1 / the body's scale: a bigger body takes longer strides, so it steps slower
            loco.cycleOffsetParameterActive = true;
            loco.cycleOffsetParameter = "CycleOffset";
            sm.defaultState = loco;

            var attack = AddClipState(sm, "Attack", "EnemyAttack", new Vector3(560f, 100f));
            var death = AddClipState(sm, "Death", "EnemyDeath", new Vector3(300f, 260f));

            // The attack clip's contact frame (0.45 s) lines up with Enemy's windup time; the state ends with the recovery.
            var t = Link(loco, attack, 0.08f); t.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            Link(attack, loco, 0.15f, 0.8f);
            t = LinkAny(sm, death, 0.1f); t.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

            // Flinch plays on the upper body so the legs keep their stride.
            c.AddLayer("Flinch");
            var layers = c.layers;
            layers[1].avatarMask = mask;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[1].defaultWeight = 1f;
            layers[0].iKPass = true;
            c.layers = layers;
            var fsm = c.layers[1].stateMachine;
            var idle = fsm.AddState("None", new Vector3(300f, 0f));
            idle.writeDefaultValues = true;
            var hit = AddClipState(fsm, "Hit", "EnemyHit", new Vector3(300f, 100f));
            fsm.defaultState = idle;
            t = Link(idle, hit, 0.04f); t.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            Link(hit, idle, 0.08f, 0.85f);

            EditorUtility.SetDirty(c);
        }
    }
}
