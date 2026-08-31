using System.Collections.Generic;
using UnityEngine;

namespace Vergil333.MarrowNpcToolkit.Authoring
{
    public static class NpcHumanoidGraph
    {
        public static readonly HumanBodyBones[] CanonicalRoles =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
        };

        // BehaviourPowerLegs and PuppetMaster do not consume the display/authoring
        // order above. Keep the proven native muscle order explicit so a future
        // compatibility provider never derives a runtime array from a role set.
        public static readonly HumanBodyBones[] NativeMuscleOrder =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
        };

        private static readonly Dictionary<HumanBodyBones, HumanBodyBones?> Parents =
            new Dictionary<HumanBodyBones, HumanBodyBones?>
            {
                [HumanBodyBones.Hips] = null,
                [HumanBodyBones.Spine] = HumanBodyBones.Hips,
                [HumanBodyBones.Chest] = HumanBodyBones.Spine,
                [HumanBodyBones.Head] = HumanBodyBones.Chest,
                [HumanBodyBones.LeftUpperArm] = HumanBodyBones.Chest,
                [HumanBodyBones.RightUpperArm] = HumanBodyBones.Chest,
                [HumanBodyBones.LeftLowerArm] = HumanBodyBones.LeftUpperArm,
                [HumanBodyBones.RightLowerArm] = HumanBodyBones.RightUpperArm,
                [HumanBodyBones.LeftHand] = HumanBodyBones.LeftLowerArm,
                [HumanBodyBones.RightHand] = HumanBodyBones.RightLowerArm,
                [HumanBodyBones.LeftUpperLeg] = HumanBodyBones.Hips,
                [HumanBodyBones.RightUpperLeg] = HumanBodyBones.Hips,
                [HumanBodyBones.LeftLowerLeg] = HumanBodyBones.LeftUpperLeg,
                [HumanBodyBones.RightLowerLeg] = HumanBodyBones.RightUpperLeg,
                [HumanBodyBones.LeftFoot] = HumanBodyBones.LeftLowerLeg,
                [HumanBodyBones.RightFoot] = HumanBodyBones.RightLowerLeg,
            };

        private static readonly Dictionary<HumanBodyBones, HumanBodyBones> PrimaryChildren =
            new Dictionary<HumanBodyBones, HumanBodyBones>
            {
                [HumanBodyBones.Hips] = HumanBodyBones.Spine,
                [HumanBodyBones.Spine] = HumanBodyBones.Chest,
                [HumanBodyBones.Chest] = HumanBodyBones.Head,
                [HumanBodyBones.LeftUpperArm] = HumanBodyBones.LeftLowerArm,
                [HumanBodyBones.RightUpperArm] = HumanBodyBones.RightLowerArm,
                [HumanBodyBones.LeftLowerArm] = HumanBodyBones.LeftHand,
                [HumanBodyBones.RightLowerArm] = HumanBodyBones.RightHand,
                [HumanBodyBones.LeftUpperLeg] = HumanBodyBones.LeftLowerLeg,
                [HumanBodyBones.RightUpperLeg] = HumanBodyBones.RightLowerLeg,
                [HumanBodyBones.LeftLowerLeg] = HumanBodyBones.LeftFoot,
                [HumanBodyBones.RightLowerLeg] = HumanBodyBones.RightFoot,
            };

        private static readonly Dictionary<HumanBodyBones, HumanBodyBones> OppositeSideRoles =
            new Dictionary<HumanBodyBones, HumanBodyBones>
            {
                [HumanBodyBones.LeftUpperArm] = HumanBodyBones.RightUpperArm,
                [HumanBodyBones.RightUpperArm] = HumanBodyBones.LeftUpperArm,
                [HumanBodyBones.LeftLowerArm] = HumanBodyBones.RightLowerArm,
                [HumanBodyBones.RightLowerArm] = HumanBodyBones.LeftLowerArm,
                [HumanBodyBones.LeftHand] = HumanBodyBones.RightHand,
                [HumanBodyBones.RightHand] = HumanBodyBones.LeftHand,
                [HumanBodyBones.LeftUpperLeg] = HumanBodyBones.RightUpperLeg,
                [HumanBodyBones.RightUpperLeg] = HumanBodyBones.LeftUpperLeg,
                [HumanBodyBones.LeftLowerLeg] = HumanBodyBones.RightLowerLeg,
                [HumanBodyBones.RightLowerLeg] = HumanBodyBones.LeftLowerLeg,
                [HumanBodyBones.LeftFoot] = HumanBodyBones.RightFoot,
                [HumanBodyBones.RightFoot] = HumanBodyBones.LeftFoot,
            };

        public static bool TryGetParent(HumanBodyBones role, out HumanBodyBones parent)
        {
            if (Parents.TryGetValue(role, out HumanBodyBones? value) && value.HasValue)
            {
                parent = value.Value;
                return true;
            }
            parent = HumanBodyBones.LastBone;
            return false;
        }

        public static bool TryGetPrimaryChild(HumanBodyBones role, out HumanBodyBones child)
        {
            return PrimaryChildren.TryGetValue(role, out child);
        }

        public static bool TryGetOppositeSide(
            HumanBodyBones role,
            out HumanBodyBones oppositeRole)
        {
            return OppositeSideRoles.TryGetValue(role, out oppositeRole);
        }

        public static bool IsCanonical(HumanBodyBones role)
        {
            return Parents.ContainsKey(role);
        }
    }
}
