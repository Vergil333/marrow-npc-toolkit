using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using MarrowAvatar = SLZ.VRMK.Avatar;

namespace Vergil333.MarrowNpcToolkit.Editor.AvatarIntake
{
    public static class MarrowAvatarSnapshotService
    {
        public static void Capture(GameObject avatarPrefab, NpcAvatarSourceProfile destination)
        {
            if (avatarPrefab == null) throw new ArgumentNullException(nameof(avatarPrefab));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            AvatarIntakeReport report = AvatarIntakeValidator.Validate(avatarPrefab);
            if (!report.ReadyForNpcDefinition)
                throw new InvalidOperationException(
                    "The Avatar must pass intake validation before its source profile can be captured.");

            MarrowAvatar avatar = report.MarrowAvatar;
            Transform root = avatarPrefab.transform;
            string assetPath = AssetDatabase.GetAssetPath(avatarPrefab);

            destination.SetSource(
                avatarPrefab,
                AssetDatabase.AssetPathToGUID(assetPath),
                AssetDatabase.GetAssetDependencyHash(assetPath).ToString(),
                NpcToolkitVersion.SupportedMarrowSdkVersion,
                Path(root, report.Animator.transform));

            NpcHumanoidBoneBinding[] boneBindings = report.HumanoidBonePaths
                .OrderBy(value => (int)value.Key)
                .Select(value => new NpcHumanoidBoneBinding(value.Key, value.Value))
                .ToArray();

            var body = new HashSet<SkinnedMeshRenderer>(avatar.bodyMeshes
                                                       ?? Array.Empty<SkinnedMeshRenderer>());
            var head = new HashSet<SkinnedMeshRenderer>(avatar.headMeshes
                                                       ?? Array.Empty<SkinnedMeshRenderer>());
            var hair = new HashSet<SkinnedMeshRenderer>(avatar.hairMeshes
                                                       ?? Array.Empty<SkinnedMeshRenderer>());
            NpcAvatarRendererBinding[] rendererBindings = avatarPrefab
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Select(renderer => new NpcAvatarRendererBinding(
                    hair.Contains(renderer) ? NpcAvatarRendererCategory.Hair
                    : head.Contains(renderer) ? NpcAvatarRendererCategory.Head
                    : body.Contains(renderer) ? NpcAvatarRendererCategory.Body
                    : NpcAvatarRendererCategory.Unassigned,
                    Path(root, renderer.transform)))
                .ToArray();

            var optionalBindings = new List<NpcOptionalAvatarBinding>();
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.Neck2, avatar.neck2);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.LeftScapula, avatar.scapulaLf);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.RightScapula, avatar.scapulaRt);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.LeftCarpal, avatar.carpalLf);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.RightCarpal, avatar.carpalRt);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.LeftUpperArmTwist, avatar.twistUpperArmLf);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.RightUpperArmTwist, avatar.twistUpperArmRt);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.LeftForearmTwist, avatar.twistForearmLf);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.RightForearmTwist, avatar.twistForearmRt);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.LeftUpperThighTwist, avatar.twistUpperThighLf);
            AddOptional(optionalBindings, root, NpcOptionalAvatarRole.RightUpperThighTwist, avatar.twistUpperThighRt);

            destination.SetBindings(
                boneBindings,
                rendererBindings,
                optionalBindings,
                Path(root, avatar.wristLf),
                Path(root, avatar.wristRt),
                Path(root, avatar.eyeCenterOverride),
                report.GetHumanoidBonePath(HumanBodyBones.Jaw));

            destination.SetBodyFit(avatar.eyeOffset, new NpcAvatarBodyFit
            {
                HeadTop = avatar.HeadTop,
                ChinY = avatar.ChinY,
                UnderbustY = avatar.UnderbustY,
                WaistY = avatar.WaistY,
                HighHipsY = avatar.HighHipsY,
                CrotchBottom = avatar.CrotchBottom,
                ForeheadWidth = avatar.ForeheadEllipseX,
                JawWidth = avatar.JawEllipseX,
                NeckWidth = avatar.NeckEllipseX,
                ChestWidth = avatar.ChestEllipseX,
                WaistWidth = avatar.WaistEllipseX,
                HighHipsWidth = avatar.HighHipsEllipseX,
                HipsWidth = avatar.HipsEllipseX,
                ForeheadDepth = new Vector2(avatar.ForeheadEllipseZ, avatar.ForeheadEllipseNegZ),
                JawDepth = new Vector2(avatar.JawEllipseZ, avatar.JawEllipseNegZ),
                NeckDepth = new Vector2(avatar.NeckEllipseZ, avatar.NeckEllipseNegZ),
                SternumDepth = new Vector2(avatar.SternumEllipseZ, avatar.SternumEllipseNegZ),
                ChestDepth = new Vector2(avatar.ChestEllipseZ, avatar.ChestEllipseNegZ),
                WaistDepth = new Vector2(avatar.WaistEllipseZ, avatar.WaistEllipseNegZ),
                HighHipsDepth = new Vector2(avatar.HighHipsEllipseZ, avatar.HighHipsEllipseNegZ),
                HipsDepth = new Vector2(avatar.HipsEllipseZ, avatar.HipsEllipseNegZ),
            });

            destination.SetLimbFit(
                Ellipse(avatar.thighUpperEllipse),
                Ellipse(avatar.kneeEllipse),
                Ellipse(avatar.calfEllipse),
                Ellipse(avatar.ankleEllipse),
                Ellipse(avatar.upperarmEllipse),
                Ellipse(avatar.elbowEllipse),
                Ellipse(avatar.forearmEllipse),
                Ellipse(avatar.wristEllipse));

            EditorUtility.SetDirty(destination);
        }

        private static NpcAvatarLimbEllipse Ellipse(MarrowAvatar.SoftEllipse value)
        {
            return new NpcAvatarLimbEllipse(
                value.XRadius, value.XBias, value.ZRadius, value.ZBias);
        }

        private static void AddOptional(
            ICollection<NpcOptionalAvatarBinding> destination,
            Transform root,
            NpcOptionalAvatarRole role,
            Transform transform)
        {
            if (transform != null)
                destination.Add(new NpcOptionalAvatarBinding(role, Path(root, transform)));
        }

        private static string Path(Transform root, Transform transform)
        {
            if (transform == null) return string.Empty;
            return AnimationUtility.CalculateTransformPath(transform, root);
        }
    }
}
