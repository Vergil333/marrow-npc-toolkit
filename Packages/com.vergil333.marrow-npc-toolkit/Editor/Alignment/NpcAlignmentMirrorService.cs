using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.Editor.Alignment
{
    /// <summary>
    /// Mirrors collider-only authoring data between paired Humanoid roles.
    /// Pose conversion happens in Avatar-root space because left and right
    /// bones commonly use different local rotations.
    /// </summary>
    internal static class NpcAlignmentMirrorService
    {
        internal static bool TryMirrorCollider(
            Transform avatarSpaceRoot,
            Transform sourceBone,
            Transform targetBone,
            NpcBodyRoleProfile source,
            NpcBodyRoleProfile target,
            out string error)
        {
            error = string.Empty;
            if (avatarSpaceRoot == null || sourceBone == null || targetBone == null
                                   || source == null || target == null)
            {
                error = "The Avatar root, paired bones, and anatomy roles are required.";
                return false;
            }

            if (!NpcHumanoidGraph.TryGetOppositeSide(source.Role, out HumanBodyBones opposite)
                || opposite != target.Role)
            {
                error = "The selected source and target are not an opposite-side Humanoid pair.";
                return false;
            }

            if (!IsWithin(avatarSpaceRoot, sourceBone)
                || !IsWithin(avatarSpaceRoot, targetBone))
            {
                error = "Both paired bones must belong to the same source Avatar root.";
                return false;
            }

            if (source.AlignmentState == NpcAlignmentState.Unseeded
                || !HasValidCollider(source))
            {
                error = "Create the automatic fit or finish this side before mirroring it.";
                return false;
            }

            // Derive the character's center plane from the matching bone origins.
            // This remains correct when the rig is offset from x=0 beneath the
            // prefab root, while its normal still follows Avatar-root X.
            Vector3 sourceBoneRoot = avatarSpaceRoot.InverseTransformPoint(sourceBone.position);
            Vector3 targetBoneRoot = avatarSpaceRoot.InverseTransformPoint(targetBone.position);
            float sagittalPlaneX = (sourceBoneRoot.x + targetBoneRoot.x) * 0.5f;

            Vector3 sourceCenterWorld = sourceBone.TransformPoint(source.ColliderCenter);
            Vector3 mirroredCenterRoot = avatarSpaceRoot.InverseTransformPoint(
                sourceCenterWorld);
            mirroredCenterRoot.x = sagittalPlaneX * 2f - mirroredCenterRoot.x;
            Vector3 mirroredCenterWorld = avatarSpaceRoot.TransformPoint(
                mirroredCenterRoot);
            Vector3 targetLocalCenter = targetBone.InverseTransformPoint(mirroredCenterWorld);

            Quaternion sourceRootRotation = Quaternion.Inverse(avatarSpaceRoot.rotation)
                                              * sourceBone.rotation
                                              * source.ColliderLocalRotation;
            Quaternion mirroredRootRotation = MirrorRotationAcrossRootX(
                sourceRootRotation);
            Quaternion targetLocalRotation = Quaternion.Inverse(targetBone.rotation)
                                             * avatarSpaceRoot.rotation
                                             * mirroredRootRotation;

            if (!IsFinite(targetLocalCenter) || !IsFinite(targetLocalRotation))
            {
                error = "The paired bone transforms produced an invalid mirrored pose.";
                return false;
            }

            // Collider alignment is the only copied contract. Keep target-side
            // enabled/auto-fit flags, mass, joint limits, drive, and muscle
            // tuning intact; some proven left/right joint values intentionally
            // differ.
            target.ColliderShape = source.ColliderShape;
            target.ColliderCenter = targetLocalCenter;
            target.ColliderLocalRotation = Normalize(targetLocalRotation);
            target.ColliderSize = source.ColliderSize;
            target.CapsuleRadius = source.CapsuleRadius;
            target.CapsuleHeight = source.CapsuleHeight;
            target.CapsuleDirection = source.CapsuleDirection;
            target.AlignmentState = NpcAlignmentState.Reviewed;
            return true;
        }

        private static Quaternion MirrorRotationAcrossRootX(Quaternion value)
        {
            value = Normalize(value);
            // Reflection matrix M = diag(-1, 1, 1). M * R * M is a proper
            // rotation and describes the same mirrored box/capsule/sphere;
            // the second M merely reverses a shape-local axis.
            return Normalize(new Quaternion(value.x, -value.y, -value.z, value.w));
        }

        private static bool HasValidCollider(NpcBodyRoleProfile value)
        {
            if (!IsFinite(value.ColliderCenter)
                || !IsFinite(value.ColliderLocalRotation))
                return false;

            switch (value.ColliderShape)
            {
                case NpcColliderShape.Box:
                    return IsFinite(value.ColliderSize)
                           && value.ColliderSize.x > 0f
                           && value.ColliderSize.y > 0f
                           && value.ColliderSize.z > 0f;
                case NpcColliderShape.Sphere:
                    return IsFinite(value.CapsuleRadius)
                           && value.CapsuleRadius > 0f;
                case NpcColliderShape.Capsule:
                    return IsFinite(value.CapsuleRadius)
                           && IsFinite(value.CapsuleHeight)
                           && value.CapsuleRadius > 0f
                           && value.CapsuleHeight >= value.CapsuleRadius * 2f
                           && value.CapsuleDirection >= 0
                           && value.CapsuleDirection <= 2;
                default:
                    return false;
            }
        }

        private static bool IsWithin(Transform root, Transform value)
        {
            return value == root || value.IsChildOf(root);
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float length = Mathf.Sqrt(
                value.x * value.x + value.y * value.y
                + value.z * value.z + value.w * value.w);
            if (length < 0.000001f) return Quaternion.identity;
            float inverse = 1f / length;
            return new Quaternion(
                value.x * inverse,
                value.y * inverse,
                value.z * inverse,
                value.w * inverse);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y)
                                     && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
