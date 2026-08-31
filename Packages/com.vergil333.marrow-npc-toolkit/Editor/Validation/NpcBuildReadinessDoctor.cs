using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;
using Vergil333.MarrowNpcToolkit.Editor.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Movement;
using MarrowAvatar = SLZ.VRMK.Avatar;

namespace Vergil333.MarrowNpcToolkit.Editor.Validation
{
    public enum NpcBuildReadinessSeverity
    {
        Error,
        Warning,
        Info,
    }

    public sealed class NpcBuildReadinessIssue
    {
        public NpcBuildReadinessSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public HumanBodyBones? Role { get; }

        internal int Stage { get; }
        internal int Sequence { get; }

        internal NpcBuildReadinessIssue(
            NpcBuildReadinessSeverity severity,
            string code,
            string message,
            int stage,
            int sequence,
            HumanBodyBones? role = null)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Stage = stage;
            Sequence = sequence;
            Role = role;
        }
    }

    public sealed class NpcBuildReadinessReport
    {
        private readonly List<NpcBuildReadinessIssue> issues =
            new List<NpcBuildReadinessIssue>();
        private int sequence;

        public NpcDefinition Definition { get; internal set; }
        public string PreviewAssetPath { get; internal set; }
        public string CurrentSourceDependencyHash { get; internal set; }
        public string Fingerprint { get; internal set; }
        public int ReviewedRoleCount { get; internal set; }
        public int ExpectedRoleCount { get; internal set; }
        public int RigidbodyCount { get; internal set; }
        public int ColliderCount { get; internal set; }
        public int JointCount { get; internal set; }
        public int RendererCount { get; internal set; }
        public int ExpectedRendererCount { get; internal set; }
        public IReadOnlyList<NpcBuildReadinessIssue> Issues => issues;
        public int ErrorCount => issues.Count(value =>
            value.Severity == NpcBuildReadinessSeverity.Error);
        public int WarningCount => issues.Count(value =>
            value.Severity == NpcBuildReadinessSeverity.Warning);
        public bool HasErrors => ErrorCount > 0;
        public bool ReadyForBuild => !HasErrors;

        internal void Add(
            NpcBuildReadinessSeverity severity,
            string code,
            string message,
            int stage,
            HumanBodyBones? role = null)
        {
            issues.Add(new NpcBuildReadinessIssue(
                severity, code, message, stage, sequence++, role));
        }

        internal void OrderIssues()
        {
            issues.Sort(CompareIssues);
        }

        private static int CompareIssues(
            NpcBuildReadinessIssue left,
            NpcBuildReadinessIssue right)
        {
            int value = left.Stage.CompareTo(right.Stage);
            if (value != 0) return value;
            value = left.Severity.CompareTo(right.Severity);
            if (value != 0) return value;
            value = RoleOrder(left.Role).CompareTo(RoleOrder(right.Role));
            if (value != 0) return value;
            value = string.Compare(left.Code, right.Code, StringComparison.Ordinal);
            if (value != 0) return value;
            value = string.Compare(left.Message, right.Message, StringComparison.Ordinal);
            return value != 0 ? value : left.Sequence.CompareTo(right.Sequence);
        }

        private static int RoleOrder(HumanBodyBones? role)
        {
            if (!role.HasValue) return -1;
            int index = Array.IndexOf(NpcHumanoidGraph.CanonicalRoles, role.Value);
            return index >= 0 ? index : NpcHumanoidGraph.CanonicalRoles.Length;
        }
    }

    /// <summary>
    /// Performs the read-only checks required before a native NPC build is allowed.
    /// The doctor never creates, edits, saves, imports, or marks an asset dirty.
    /// </summary>
    public static class NpcBuildReadinessDoctor
    {
        private const int DefinitionStage = 0;
        private const int AnatomyStage = 1;
        private const int MovementStage = 2;
        private const int PreviewStage = 3;
        private const float DirectionEpsilon = 0.000001f;
        private const float ParallelDotThreshold = 0.999f;
        private const float LimitEpsilon = 0.001f;
        private const float MaximumAvatarDimension = 10f;
        private const float MaximumMovementOffset = 5f;
        private const float MinimumMovementScale = 0.1f;
        private const float MaximumMovementScale = 4f;
        private const string PrimaryColliderName = "PrimaryCollider";
        private static readonly NpcAudioEvent[] AudioEventOrder =
        {
            NpcAudioEvent.Agro,
            NpcAudioEvent.UnAgro,
            NpcAudioEvent.PainSmall,
            NpcAudioEvent.PainBig,
            NpcAudioEvent.Death,
            NpcAudioEvent.JumpCharge,
            NpcAudioEvent.Jump,
            NpcAudioEvent.SmallEffort,
            NpcAudioEvent.MediumEffort,
            NpcAudioEvent.LargeEffort,
            NpcAudioEvent.Attack1,
            NpcAudioEvent.AttackLand1,
            NpcAudioEvent.Attack2,
            NpcAudioEvent.ImpactHead,
            NpcAudioEvent.ImpactSpine,
            NpcAudioEvent.ImpactLimb,
        };

        private static readonly NpcAudioEvent[] RequiredReactionEvents =
        {
            NpcAudioEvent.PainSmall,
            NpcAudioEvent.PainBig,
            NpcAudioEvent.Death,
        };

        /// <summary>
        /// Validates the definition and the generated preview at the definition's
        /// expected output path. This is the one-call API intended for Step 4 UI.
        /// </summary>
        public static NpcBuildReadinessReport Validate(NpcDefinition definition)
        {
            string previewPath = definition == null
                ? string.Empty
                : NpcPhysicsPreviewBuilder.GetOutputPath(definition);
            GameObject preview = string.IsNullOrWhiteSpace(previewPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(previewPath);
            return ValidateInternal(definition, preview, previewPath);
        }

        /// <summary>
        /// Runs the same complete readiness inspection against a supplied preview.
        /// This remains read-only and is useful for tests and future custom preview
        /// locations without changing the definition's build profile.
        /// </summary>
        public static NpcBuildReadinessReport ValidateWithPreview(
            NpcDefinition definition,
            GameObject preview,
            string previewAssetPath = null)
        {
            return ValidateInternal(
                definition,
                preview,
                previewAssetPath ?? AssetDatabase.GetAssetPath(preview));
        }

        /// <summary>
        /// Stable editor identity for every public movement value and both
        /// provider-derived assets. Physics Preview receipts deliberately do not
        /// consume this identity; native build and packaging do.
        /// </summary>
        public static string ComputeMovementFingerprint(
            NpcMovementProfile profile)
        {
            var value = new StringBuilder("npc-movement-v2|");
            AppendMovement(value, profile);
            return Hash128.Compute(value.ToString()).ToString();
        }

        private static NpcBuildReadinessReport ValidateInternal(
            NpcDefinition definition,
            GameObject preview,
            string previewAssetPath)
        {
            var report = new NpcBuildReadinessReport
            {
                Definition = definition,
                PreviewAssetPath = previewAssetPath ?? string.Empty,
                CurrentSourceDependencyHash = string.Empty,
                ExpectedRoleCount = definition != null
                                    && definition.IncludePhysicalJaw
                    ? NpcHumanoidGraph.CanonicalRoles.Length + 1
                    : NpcHumanoidGraph.CanonicalRoles.Length,
            };

            NpcRigMappingReport mapping = ValidateDefinition(definition, report);
            ValidateAudio(definition, report);
            ValidateSecondaryMotion(definition, report);
            ValidateAnatomy(definition, mapping, report);
            ValidateMovement(definition, mapping, report);
            ValidatePreview(definition, preview, report);

            report.OrderIssues();
            report.Fingerprint = ComputeFingerprint(definition, preview, report);
            return report;
        }

        private static NpcRigMappingReport ValidateDefinition(
            NpcDefinition definition,
            NpcBuildReadinessReport report)
        {
            if (definition == null)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "DEFINITION_MISSING",
                    "No NPC Definition is selected.",
                    DefinitionStage);
                return null;
            }

            if (definition.SourceAvatar == null)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SOURCE_AVATAR_MISSING",
                    "The NPC Definition has no source Avatar.",
                    DefinitionStage);
            if (definition.AvatarSourceProfile == null)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SOURCE_PROFILE_MISSING",
                    "The NPC Definition has no accepted Avatar source profile.",
                    DefinitionStage);
            if (definition.AnatomyProfile == null)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "ANATOMY_PROFILE_MISSING",
                    "The NPC Definition has no Anatomy Profile.",
                    DefinitionStage);
            if (definition.MovementProfile == null)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROFILE_MISSING",
                    "The NPC Definition has no Movement Profile. Create one and run the movement auto-fit before Step 4.",
                    DefinitionStage);
            if (definition.BuildProfile == null)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "BUILD_PROFILE_MISSING",
                    "The NPC Definition has no Build Profile.",
                    DefinitionStage);

            if (definition.SourceAvatar == null
                || definition.AvatarSourceProfile == null)
                return null;

            try
            {
                NpcRigMappingReport mapping = NpcRigMappingService.Validate(definition);
                report.CurrentSourceDependencyHash =
                    mapping.CurrentSourceDependencyHash ?? string.Empty;

                foreach (NpcRigIssue issue in mapping.Issues
                             .OrderBy(value => RigSeverity(value.Severity))
                             .ThenBy(value => value.Message, StringComparer.Ordinal))
                {
                    report.Add(
                        RigSeverity(issue.Severity),
                        "SOURCE_RIG_VALIDATION",
                        issue.Message,
                        DefinitionStage);
                }

                if (!mapping.ReadyForBaseline)
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "SOURCE_MAPPING_NOT_READY",
                        "The accepted Avatar mapping is stale or invalid. Refresh the Avatar snapshot before building.",
                        DefinitionStage);
                return mapping;
            }
            catch (Exception exception)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SOURCE_VALIDATION_FAILED",
                    "Source validation could not complete: "
                    + exception.GetType().Name + ": " + exception.Message,
                    DefinitionStage);
                return null;
            }
        }

        private static void ValidateAudio(
            NpcDefinition definition,
            NpcBuildReadinessReport report)
        {
            if (definition == null) return;
            if (!Enum.IsDefined(typeof(NpcAudioMode), definition.AudioMode))
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "AUDIO_MODE_INVALID",
                    "NPC Audio must be explicitly Silent or use an Audio Profile.",
                    DefinitionStage);
                return;
            }
            if (definition.AudioMode == NpcAudioMode.Silent) return;

            NpcAudioProfile profile = definition.AudioProfile;
            if (profile == null)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "AUDIO_PROFILE_MISSING",
                    "NPC Audio is set to Profile, but the NPC Definition has no Audio Profile.",
                    DefinitionStage);
                return;
            }

            if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(profile)))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "AUDIO_PROFILE_NOT_PERSISTENT",
                    "The Audio Profile must be a saved Project asset.",
                    DefinitionStage);

            foreach (NpcAudioEvent required in RequiredReactionEvents)
                if (profile.GetClips(required).Count == 0)
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "AUDIO_REQUIRED_GROUP_EMPTY",
                        AudioEventName(required)
                        + " needs at least one clip when NPC Audio uses a profile.",
                        DefinitionStage);

            foreach (NpcAudioEvent audioEvent in AudioEventOrder)
                ValidateAudioClips(
                    profile.GetClips(audioEvent),
                    AudioEventName(audioEvent),
                    report);
            ValidateAudioClips(profile.WalkConcrete, "Walk Concrete", report);
            ValidateAudioClips(profile.RunConcrete, "Run Concrete", report);
            ValidateAudioClip(profile.DotLoop1, "DOT Loop", report);
            ValidateAudioClip(
                profile.AgroMovementLoop, "Agro Movement Loop", report);
            ValidateAudioClip(profile.MovementLoop, "Movement Loop", report);

            bool hasWalk = profile.WalkConcrete.Count > 0;
            bool hasRun = profile.RunConcrete.Count > 0;
            if (hasWalk != hasRun)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "AUDIO_FOOTSTEP_PAIR_INCOMPLETE",
                    "Footsteps need both walking and running clips, or neither group.",
                    DefinitionStage);
            if (!IsFinitePositive(profile.PitchMultiplier))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "AUDIO_PITCH_INVALID",
                    "Audio pitch multiplier must be finite and greater than zero.",
                    DefinitionStage);
            if (!IsFinite(profile.FootstepVolumeMultiplier)
                || profile.FootstepVolumeMultiplier < 0f)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "AUDIO_FOOTSTEP_VOLUME_INVALID",
                    "Footstep volume multiplier must be finite and zero or greater.",
                    DefinitionStage);

        }

        private static void ValidateSecondaryMotion(
            NpcDefinition definition,
            NpcBuildReadinessReport report)
        {
            if (definition == null || !definition.IncludeSecondaryMotion)
                return;

            GameObject sourceAvatar = definition.SourceAvatar;
            MarrowAvatar[] avatars = sourceAvatar == null
                ? Array.Empty<MarrowAvatar>()
                : sourceAvatar.GetComponentsInChildren<MarrowAvatar>(true);
            if (avatars.Length != 1)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SECONDARY_MOTION_MARROW_AVATAR_MISSING",
                    "Secondary Motion needs exactly one source Marrow Avatar with its two Breast Soft Body bones assigned; found "
                    + avatars.Length
                    + ". This module does not generate abdomen or butt soft-body physics.",
                    DefinitionStage);
                return;
            }

            MarrowAvatar avatar = avatars[0];
            MarrowAvatar.SoftBulge breast = avatar.bulgeBreast;
            bool hasRight = breast != null && breast.primaryRt != null;
            bool hasLeft = breast != null && breast.secondaryLf != null;
            if (!hasRight || !hasLeft)
            {
                string missing = !hasRight && !hasLeft
                    ? "right and left"
                    : !hasRight ? "right" : "left";
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SECONDARY_MOTION_BREAST_BONES_MISSING",
                    "Secondary Motion is enabled, but the " + missing
                    + " Breast Soft Body bone assignment is missing on the source Marrow Avatar. Assign both breast bones in the official Avatar editor, or turn Secondary Motion off. Abdomen and butt assignments are not used by this module.",
                    DefinitionStage);
                return;
            }

            Transform right = breast.primaryRt;
            Transform left = breast.secondaryLf;
            if (right == left)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SECONDARY_MOTION_BREAST_BONES_DUPLICATE",
                    "The right and left Breast Soft Body assignments must reference two distinct source bones.",
                    DefinitionStage);
                return;
            }

            Transform sourceRoot = sourceAvatar.transform;
            Transform[] assigned = { right, left };
            if (assigned.Any(value => value == null
                                      || !value.IsChildOf(sourceRoot)))
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SECONDARY_MOTION_BREAST_BONE_OUTSIDE_SOURCE",
                    "Both Breast Soft Body assignments must resolve below the accepted source Avatar so they survive cloning under AnimationRoot.",
                    DefinitionStage);
                return;
            }

            Transform acceptedAnimationRoot = ResolveSourceTransform(
                sourceRoot,
                definition.AvatarSourceProfile?.AnimatorPath);
            if (acceptedAnimationRoot == null
                || assigned.Any(value =>
                    !value.IsChildOf(acceptedAnimationRoot)))
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SECONDARY_MOTION_BREAST_BONE_OUTSIDE_ANIMATION_ROOT",
                    "Both Breast Soft Body assignments must resolve below the accepted Animator/AnimationRoot captured by the Avatar snapshot.",
                    DefinitionStage);
                return;
            }

            SkinnedMeshRenderer[] renderers = sourceRoot
                .GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Transform[] skinnedBones = renderers
                .Where(value => value != null && value.bones != null)
                .SelectMany(value => value.bones)
                .Where(value => value != null)
                .Distinct()
                .ToArray();
            Transform[] unskinned = assigned
                .Where(value => !skinnedBones.Contains(value))
                .ToArray();
            if (unskinned.Length > 0)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SECONDARY_MOTION_BREAST_BONE_NOT_SKINNED",
                    "Each Breast Soft Body assignment must be used in a source SkinnedMeshRenderer bone array. Not used: "
                    + string.Join(", ", unskinned.Select(value => value.name))
                    + ".",
                    DefinitionStage);
                return;
            }

            Transform[] unmappable = assigned
                .Where(value => !TryResolveSecondaryMotionOwner(
                    definition,
                    sourceRoot,
                    acceptedAnimationRoot,
                    value,
                    out _))
                .ToArray();
            if (unmappable.Length > 0)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "SECONDARY_MOTION_BREAST_BONE_OWNER_UNMAPPABLE",
                    "Each Breast Soft Body assignment must be the accepted canonical body bone, descend from one, or contain the accepted Hips root. No physical owner can be resolved for: "
                    + string.Join(", ", unmappable.Select(value => value.name))
                    + ".",
                    DefinitionStage);
                return;
            }

            report.Add(
                NpcBuildReadinessSeverity.Info,
                "SECONDARY_MOTION_BREAST_BONES_READY",
                "Secondary Motion will use the two distinct, renderer-skinned Breast Soft Body bones already assigned on the source Marrow Avatar. Both resolve to canonical physical owners. Abdomen and butt soft-body assignments are not generated by this module.",
                DefinitionStage);
        }

        private static bool TryResolveSecondaryMotionOwner(
            NpcDefinition definition,
            Transform sourceRoot,
            Transform acceptedAnimationRoot,
            Transform sourceBone,
            out HumanBodyBones ownerRole)
        {
            ownerRole = HumanBodyBones.LastBone;
            if (definition?.AvatarSourceProfile == null || sourceRoot == null
                || acceptedAnimationRoot == null || sourceBone == null
                || !sourceBone.IsChildOf(acceptedAnimationRoot))
                return false;

            var roleForTarget = new Dictionary<Transform, HumanBodyBones>();
            foreach (NpcHumanoidBoneBinding binding in
                     definition.AvatarSourceProfile.HumanoidBones)
            {
                if (!NpcHumanoidGraph.CanonicalRoles.Contains(binding.Role))
                    continue;
                Transform target = ResolveSourceTransform(
                    sourceRoot, binding.TransformPath);
                if (target != null && !roleForTarget.ContainsKey(target))
                    roleForTarget.Add(target, binding.Role);
            }

            if (definition.IncludePhysicalJaw
                && !string.IsNullOrWhiteSpace(
                    definition.AvatarSourceProfile.JawPath))
            {
                Transform jaw = ResolveSourceTransform(
                    sourceRoot, definition.AvatarSourceProfile.JawPath);
                if (jaw != null && !roleForTarget.ContainsKey(jaw))
                    roleForTarget.Add(jaw, HumanBodyBones.Jaw);
            }

            if (roleForTarget.TryGetValue(sourceBone, out ownerRole))
                return true;
            for (Transform cursor = sourceBone.parent;
                 cursor != null && cursor.IsChildOf(acceptedAnimationRoot);
                 cursor = cursor.parent)
            {
                if (roleForTarget.TryGetValue(cursor, out ownerRole))
                    return true;
            }

            Transform hips = roleForTarget
                .Where(value => value.Value == HumanBodyBones.Hips)
                .Select(value => value.Key)
                .FirstOrDefault();
            if (hips != null && (sourceBone == hips || hips.IsChildOf(sourceBone)))
            {
                ownerRole = HumanBodyBones.Hips;
                return true;
            }
            return false;
        }

        private static Transform ResolveSourceTransform(
            Transform sourceRoot,
            string path)
        {
            if (sourceRoot == null) return null;
            return string.IsNullOrWhiteSpace(path)
                ? sourceRoot
                : sourceRoot.Find(path);
        }

        private static void ValidateAudioClips(
            IReadOnlyList<AudioClip> clips,
            string group,
            NpcBuildReadinessReport report)
        {
            for (int index = 0; index < clips.Count; index++)
            {
                AudioClip clip = clips[index];
                if (clip == null)
                {
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "AUDIO_CLIP_MISSING",
                        group + " clip " + index + " is missing.",
                        DefinitionStage);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(clip)))
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "AUDIO_CLIP_NOT_PERSISTENT",
                        group + " clip " + index
                        + " must be a saved Project audio asset.",
                        DefinitionStage);
            }
        }

        private static void ValidateAudioClip(
            AudioClip clip,
            string label,
            NpcBuildReadinessReport report)
        {
            if (clip == null) return;
            if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(clip)))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "AUDIO_CLIP_NOT_PERSISTENT",
                    label + " must be a saved Project audio asset.",
                    DefinitionStage);
        }

        private static string AudioEventName(NpcAudioEvent audioEvent)
        {
            switch (audioEvent)
            {
                case NpcAudioEvent.UnAgro: return "Un-Agro";
                case NpcAudioEvent.PainSmall: return "Small Pain";
                case NpcAudioEvent.PainBig: return "Big Pain";
                case NpcAudioEvent.JumpCharge: return "Jump Charge";
                case NpcAudioEvent.SmallEffort: return "Small Effort";
                case NpcAudioEvent.MediumEffort: return "Medium Effort";
                case NpcAudioEvent.LargeEffort: return "Large Effort";
                case NpcAudioEvent.AttackLand1: return "Attack Land 1";
                case NpcAudioEvent.ImpactHead: return "Head Impact";
                case NpcAudioEvent.ImpactSpine: return "Spine Impact";
                case NpcAudioEvent.ImpactLimb: return "Limb Impact";
                default: return audioEvent.ToString();
            }
        }

        private static void ValidateAnatomy(
            NpcDefinition definition,
            NpcRigMappingReport mapping,
            NpcBuildReadinessReport report)
        {
            NpcAnatomyProfile anatomy = definition?.AnatomyProfile;
            if (anatomy == null) return;

            IReadOnlyList<NpcBodyRoleProfile> roles = anatomy.BodyRoles;
            if (!anatomy.HasCanonicalRoleSet())
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "ANATOMY_ROLE_SET_INVALID",
                    "The Anatomy Profile must contain exactly the 16 unique canonical NPC roles.",
                    AnatomyStage);

            var roleGroups = (roles ?? new NpcBodyRoleProfile[0])
                .Where(value => value != null)
                .GroupBy(value => value.Role)
                .ToDictionary(group => group.Key, group => group.ToArray());

            foreach (HumanBodyBones role in NpcHumanoidGraph.CanonicalRoles)
            {
                if (!roleGroups.TryGetValue(role, out NpcBodyRoleProfile[] matches)
                    || matches.Length == 0)
                {
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "ANATOMY_ROLE_MISSING",
                        role + " is missing from the Anatomy Profile.",
                        AnatomyStage,
                        role);
                    continue;
                }
                if (matches.Length > 1)
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "ANATOMY_ROLE_DUPLICATE",
                        role + " appears " + matches.Length
                        + " times in the Anatomy Profile.",
                        AnatomyStage,
                        role);

                ValidateRole(matches[0], report);
            }

            foreach (HumanBodyBones unexpected in roleGroups.Keys
                         .Where(role => !NpcHumanoidGraph.IsCanonical(role))
                         .OrderBy(role => (int)role))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "ANATOMY_ROLE_UNEXPECTED",
                    unexpected + " is not one of the 16 canonical body roles.",
                    AnatomyStage,
                    unexpected);

            NpcBodyRoleProfile jaw = anatomy.OptionalJaw;
            if (definition != null && definition.IncludePhysicalJaw)
            {
                if (definition.AvatarSourceProfile == null
                    || string.IsNullOrWhiteSpace(
                        definition.AvatarSourceProfile.JawPath))
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "PHYSICAL_JAW_MAPPING_MISSING",
                        "Physical Jaw is requested, but the accepted Avatar snapshot has no mapped Humanoid Jaw. Map Jaw or turn off Physical Jaw in Define NPC.",
                        AnatomyStage,
                        HumanBodyBones.Jaw);

                if (jaw == null)
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "PHYSICAL_JAW_PROFILE_MISSING",
                        "Physical Jaw is requested, but the Anatomy Profile has no optional Jaw role.",
                        AnatomyStage,
                        HumanBodyBones.Jaw);
                else if (!jaw.Enabled)
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "PHYSICAL_JAW_DISABLED",
                        "Physical Jaw is requested, but Jaw is disabled in Physics Alignment. Enable Jaw or turn off Physical Jaw in Define NPC.",
                        AnatomyStage,
                        HumanBodyBones.Jaw);
                else if (jaw.AlignmentState == NpcAlignmentState.Unseeded)
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "PHYSICAL_JAW_UNFITTED",
                        "Physical Jaw is requested, but Jaw has no fitted lower-face box. Run Create / Refresh Auto-Fit Baseline, then review Jaw.",
                        AnatomyStage,
                        HumanBodyBones.Jaw);
                else
                {
                    if (jaw.ColliderShape != NpcColliderShape.Box)
                        report.Add(
                            NpcBuildReadinessSeverity.Error,
                            "PHYSICAL_JAW_SHAPE_INVALID",
                            "Physical Jaw must use one Box fitted to the lower-face vertices weighted to Jaw. Reset Jaw to Auto-Fit.",
                            AnatomyStage,
                            HumanBodyBones.Jaw);
                    ValidateRole(jaw, report);
                }
            }

            report.ReviewedRoleCount = NpcHumanoidGraph.CanonicalRoles.Count(role =>
                roleGroups.TryGetValue(role, out NpcBodyRoleProfile[] matches)
                && matches.Length == 1
                && matches[0].AlignmentState == NpcAlignmentState.Reviewed);
            if (definition != null && definition.IncludePhysicalJaw
                && jaw != null
                && jaw.AlignmentState == NpcAlignmentState.Reviewed)
                report.ReviewedRoleCount++;
            if (report.ReviewedRoleCount < report.ExpectedRoleCount)
                report.Add(
                    NpcBuildReadinessSeverity.Warning,
                    "ALIGNMENT_REVIEW_INCOMPLETE",
                    report.ReviewedRoleCount + "/" + report.ExpectedRoleCount
                    + " body shapes are marked reviewed. This does not block a draft build, but review every shape before release.",
                    AnatomyStage);

            string expectedSourceHash = mapping?.CurrentSourceDependencyHash;
            if (string.IsNullOrWhiteSpace(expectedSourceHash))
                expectedSourceHash = definition?.SourceDependencyHash;
            if (string.IsNullOrWhiteSpace(expectedSourceHash))
                expectedSourceHash = definition?.AvatarSourceProfile
                    ?.SourceDependencyHash;

            if (!anatomy.HasFittedBaseline)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "ANATOMY_BASELINE_MISSING",
                    "No complete fitted anatomy baseline is available. Return to Step 3A and click 'Create / Refresh Auto-Fit Baseline'.",
                    AnatomyStage);
                return;
            }

            if (!string.IsNullOrWhiteSpace(expectedSourceHash)
                && !string.Equals(
                    anatomy.BaselineSourceDependencyHash,
                    expectedSourceHash,
                    StringComparison.Ordinal))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "ANATOMY_BASELINE_SOURCE_CHANGED",
                    "The source Avatar changed after this anatomy was fitted. Return to Step 3A and click 'Create / Refresh Auto-Fit Baseline', then review the shapes again.",
                    AnatomyStage);

            if (!string.Equals(
                    anatomy.BaselineToolkitVersion,
                    NpcToolkitVersion.Current,
                    StringComparison.Ordinal))
            {
                string fittedVersion = string.IsNullOrWhiteSpace(
                    anatomy.BaselineToolkitVersion)
                    ? "an older toolkit"
                    : "toolkit " + anatomy.BaselineToolkitVersion;
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "ANATOMY_BASELINE_TOOLKIT_OUTDATED",
                    "This anatomy was fitted by " + fittedVersion
                    + ", while toolkit " + NpcToolkitVersion.Current
                    + " is installed. Return to Step 3A and click 'Create / Refresh Auto-Fit Baseline'. Reviewed manual roles will be preserved.",
                    AnatomyStage);
            }
        }

        private static void ValidateMovement(
            NpcDefinition definition,
            NpcRigMappingReport mapping,
            NpcBuildReadinessReport report)
        {
            NpcMovementProfile profile = definition?.MovementProfile;
            if (profile == null) return;

            if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(profile)))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROFILE_NOT_PERSISTENT",
                    "The Movement Profile must be a saved Project asset before Step 4.",
                    MovementStage);

            if (!Enum.IsDefined(
                    typeof(NpcAlignmentState), profile.AlignmentState))
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_ALIGNMENT_STATE_INVALID",
                    "The Movement Profile has an invalid alignment state. Reset and refit movement.",
                    MovementStage);
                return;
            }

            if (!profile.HasFittedMeasurements)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROFILE_UNFITTED",
                    "The Movement Profile has not been fitted to the accepted Avatar. Run movement auto-fit before Step 4.",
                    MovementStage);
                return;
            }

            string expectedSourceHash = mapping?.CurrentSourceDependencyHash;
            if (string.IsNullOrWhiteSpace(expectedSourceHash))
                expectedSourceHash = definition?.SourceDependencyHash;
            if (string.IsNullOrWhiteSpace(expectedSourceHash))
                expectedSourceHash = definition?.AvatarSourceProfile
                    ?.SourceDependencyHash;

            if (!string.IsNullOrWhiteSpace(expectedSourceHash)
                && !string.Equals(
                    profile.AutoFitSourceDependencyHash,
                    expectedSourceHash,
                    StringComparison.Ordinal))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROFILE_SOURCE_CHANGED",
                    "The source Avatar changed after movement was fitted. Refresh the movement auto-fit before Step 4.",
                    MovementStage);

            string expectedAuthoringFingerprint =
                NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(definition);
            if (!string.Equals(
                    profile.AutoFitAuthoringFingerprint,
                    expectedAuthoringFingerprint,
                    StringComparison.Ordinal))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROFILE_ALIGNMENT_CHANGED",
                    "The Avatar or Physics Alignment changed after movement was fitted. Refresh Step 3D before Step 4.",
                    MovementStage);

            if (!string.Equals(
                    profile.AutoFitToolkitVersion,
                    NpcToolkitVersion.Current,
                    StringComparison.Ordinal))
            {
                string fittedVersion = string.IsNullOrWhiteSpace(
                    profile.AutoFitToolkitVersion)
                    ? "an older toolkit"
                    : "toolkit " + profile.AutoFitToolkitVersion;
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROFILE_TOOLKIT_OUTDATED",
                    "This movement fit was created by " + fittedVersion
                    + ", while toolkit " + NpcToolkitVersion.Current
                    + " is installed. Refresh the movement auto-fit before Step 4.",
                    MovementStage);
            }

            ValidateMovementRange(
                report, "Eye height", profile.EyeHeight,
                DirectionEpsilon, MaximumAvatarDimension);
            ValidateMovementRange(
                report, "Body height", profile.BodyHeight,
                DirectionEpsilon, MaximumAvatarDimension);
            ValidateMovementRange(
                report, "Navigation height", profile.NavHeight,
                DirectionEpsilon, MaximumAvatarDimension);
            ValidateMovementRange(
                report, "Left leg length", profile.LeftLegLength,
                DirectionEpsilon, MaximumAvatarDimension);
            ValidateMovementRange(
                report, "Right leg length", profile.RightLegLength,
                DirectionEpsilon, MaximumAvatarDimension);
            ValidateMovementRange(
                report, "Mean leg length", profile.MeanLegLength,
                DirectionEpsilon, MaximumAvatarDimension);
            ValidateMovementRange(
                report, "Hip width", profile.HipWidth,
                DirectionEpsilon, MaximumAvatarDimension);
            ValidateMovementRange(
                report, "Stance width", profile.StanceWidth,
                DirectionEpsilon, MaximumAvatarDimension);
            ValidateMovementRange(
                report, "Sole height", profile.SoleHeight,
                -MaximumMovementOffset, MaximumMovementOffset);
            ValidateMovementRange(
                report, "Navigation radius", profile.NavRadius,
                DirectionEpsilon, MaximumAvatarDimension);
            ValidateMovementRange(
                report, "Navigation base offset", profile.NavBaseOffset,
                -MaximumMovementOffset, MaximumMovementOffset);

            ValidateMovementDirection(
                report, "Left foot forward", profile.LeftFootForwardLocal);
            ValidateMovementDirection(
                report, "Right foot forward", profile.RightFootForwardLocal);

            if (IsFinite(profile.LeftLegLength)
                && IsFinite(profile.RightLegLength)
                && IsFinite(profile.MeanLegLength)
                && Mathf.Abs(profile.MeanLegLength
                             - (profile.LeftLegLength
                                + profile.RightLegLength) * 0.5f) > 0.0001f)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_MEAN_LEG_INCONSISTENT",
                    "Mean leg length does not match the fitted left and right leg lengths. Refresh the movement auto-fit.",
                    MovementStage);

            ValidateMovementRange(
                report, "Pelvis height offset", profile.PelvisHeightOffset,
                -MaximumMovementOffset, MaximumMovementOffset);
            ValidateMovementRange(
                report, "Stance width scale", profile.StanceWidthScale,
                MinimumMovementScale, MaximumMovementScale);
            ValidateMovementRange(
                report, "Left foot yaw correction",
                profile.LeftFootYawCorrectionDegrees, -180f, 180f);
            ValidateMovementRange(
                report, "Right foot yaw correction",
                profile.RightFootYawCorrectionDegrees, -180f, 180f);
            ValidateMovementRange(
                report, "Stride scale", profile.StrideScale,
                MinimumMovementScale, MaximumMovementScale);
            ValidateMovementRange(
                report, "Step height scale", profile.StepHeightScale,
                MinimumMovementScale, MaximumMovementScale);
            ValidateMovementRange(
                report, "Step rate scale", profile.StepRateScale,
                MinimumMovementScale, MaximumMovementScale);
            ValidateMovementRange(
                report, "Walk speed", profile.WalkSpeed,
                DirectionEpsilon, 20f);
            ValidateMovementRange(
                report, "Acceleration", profile.Acceleration,
                DirectionEpsilon, 100f);
            ValidateMovementRange(
                report, "Angular speed", profile.AngularSpeed,
                DirectionEpsilon, 1080f);
            ValidateMovementRange(
                report, "Stopping distance", profile.StoppingDistance,
                0f, 20f);
            ValidateMovementRange(
                report, "Starting hostility", profile.StartingHostility,
                0f, 1f);
            ValidateMovementRange(
                report, "Hostility after a typical hit",
                profile.HostilityAfterTypicalHit, 0f, 1f);
            if (profile.HostilityAfterTypicalHit
                < profile.StartingHostility)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_HOSTILITY_RESPONSE_INVALID",
                    "Hostility after a typical hit cannot be lower than the starting hostility.",
                    MovementStage);

            if (string.IsNullOrWhiteSpace(profile.ProviderRecipeFingerprint))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROVIDER_RECIPE_MISSING",
                    "Native movement preparation has not completed. Run Step 3D before Step 4.",
                    MovementStage);

            bool hasStandingPose = profile.ProviderStandingPose != null;
            bool hasMovementConfig = profile.ProviderMovementConfig != null;
            if (hasStandingPose != hasMovementConfig)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROVIDER_ASSETS_INCOMPLETE",
                    "Native movement preparation must create both the standing pose and movement settings. Run Step 3D again.",
                    MovementStage);
            else if (!hasStandingPose)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROVIDER_ASSETS_MISSING",
                    "Native movement preparation has not created its standing pose and movement settings. Run Step 3D before Step 4.",
                    MovementStage);

            ValidateProviderMovementAsset(
                report, profile.ProviderStandingPose, "standing pose");
            ValidateProviderMovementAsset(
                report, profile.ProviderMovementConfig, "movement config");

            if (hasStandingPose
                && hasMovementConfig
                && !string.IsNullOrWhiteSpace(
                    profile.ProviderRecipeFingerprint))
            {
                NpcMovementRecipeValidationReport validation =
                    NpcMovementRecipeValidator.Validate(definition, profile);
                if (!validation.IsCurrent)
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "MOVEMENT_PROVIDER_RECIPE_STALE",
                        string.IsNullOrWhiteSpace(validation.Detail)
                            ? "The native movement setup is out of date. Refresh Step 3D before Step 4."
                            : validation.Detail,
                        MovementStage);
            }
        }

        private static void ValidateMovementRange(
            NpcBuildReadinessReport report,
            string label,
            float value,
            float minimum,
            float maximum)
        {
            if (!IsFinite(value))
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_VALUE_NOT_FINITE",
                    label + " must be finite.",
                    MovementStage);
                return;
            }
            if (value < minimum || value > maximum)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_VALUE_OUT_OF_RANGE",
                    label + " must be between "
                    + Float(minimum) + " and " + Float(maximum) + ".",
                    MovementStage);
        }

        private static void ValidateMovementDirection(
            NpcBuildReadinessReport report,
            string label,
            Vector3 value)
        {
            if (!IsFiniteDirection(value)
                || Mathf.Abs(value.sqrMagnitude - 1f) > 0.01f)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_FOOT_FORWARD_INVALID",
                    label + " must be a finite normalized direction.",
                    MovementStage);
        }

        private static void ValidateProviderMovementAsset(
            NpcBuildReadinessReport report,
            UnityEngine.Object value,
            string label)
        {
            if (value != null
                && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(value)))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MOVEMENT_PROVIDER_ASSET_NOT_PERSISTENT",
                    "The provider " + label + " must be a saved Project asset.",
                    MovementStage);
        }

        private static void ValidateRole(
            NpcBodyRoleProfile role,
            NpcBuildReadinessReport report)
        {
            HumanBodyBones bone = role.Role;
            if (!role.Enabled)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "ANATOMY_ROLE_DISABLED",
                    bone + " is disabled, but all 16 canonical bodies are required.",
                    AnatomyStage,
                    bone);
            if (role.AlignmentState == NpcAlignmentState.Unseeded)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "ANATOMY_ROLE_UNSEEDED",
                    bone + " has no fitted alignment.",
                    AnatomyStage,
                    bone);
            if (!IsFinitePositive(role.MassKilograms))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "BODY_MASS_INVALID",
                    bone + " mass must be finite and greater than zero.",
                    AnatomyStage,
                    bone);
            if (!IsFinitePositive(role.MuscleSpring))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MUSCLE_SPRING_INVALID",
                    bone + " muscle spring must be finite and greater than zero.",
                    AnatomyStage,
                    bone);
            if (!IsFinitePositive(role.MuscleDamper))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MUSCLE_DAMPER_INVALID",
                    bone + " muscle damper must be finite and greater than zero.",
                    AnatomyStage,
                    bone);
            if (!IsFinitePositive(role.MuscleWeight)
                || role.MuscleWeight > 1f + LimitEpsilon)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "MUSCLE_WEIGHT_INVALID",
                    bone + " muscle weight must be finite and between 0 and 1.",
                    AnatomyStage,
                    bone);
            if (!IsFinitePositive(role.JointDriveMaxForce))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "JOINT_DRIVE_FORCE_INVALID",
                    bone + " joint drive force must be finite and greater than zero.",
                    AnatomyStage,
                    bone);

            if (!IsFinite(role.ColliderCenter))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "COLLIDER_CENTER_INVALID",
                    bone + " collider center contains a non-finite value.",
                    AnatomyStage,
                    bone);
            if (!IsFinite(role.ColliderLocalRotation)
                || QuaternionLengthSquared(role.ColliderLocalRotation)
                < DirectionEpsilon)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "COLLIDER_ROTATION_INVALID",
                    bone + " collider rotation must be finite and non-zero.",
                    AnatomyStage,
                    bone);

            switch (role.ColliderShape)
            {
                case NpcColliderShape.Box:
                    if (!IsFinitePositive(role.ColliderSize))
                        report.Add(
                            NpcBuildReadinessSeverity.Error,
                            "COLLIDER_SIZE_INVALID",
                            bone + " box size must be finite and positive on every axis.",
                            AnatomyStage,
                            bone);
                    break;
                case NpcColliderShape.Sphere:
                    if (!IsFinitePositive(role.CapsuleRadius))
                        report.Add(
                            NpcBuildReadinessSeverity.Error,
                            "COLLIDER_SPHERE_INVALID",
                            bone + " sphere radius must be finite and greater than zero.",
                            AnatomyStage,
                            bone);
                    break;
                case NpcColliderShape.Capsule:
                    if (!IsFinitePositive(role.CapsuleRadius)
                        || !IsFinitePositive(role.CapsuleHeight)
                        || role.CapsuleHeight + LimitEpsilon
                        < role.CapsuleRadius * 2f)
                        report.Add(
                            NpcBuildReadinessSeverity.Error,
                            "COLLIDER_CAPSULE_INVALID",
                            bone + " capsule radius and height must be finite and positive, with height at least twice its radius.",
                            AnatomyStage,
                            bone);
                    if (role.CapsuleDirection < 0 || role.CapsuleDirection > 2)
                        report.Add(
                            NpcBuildReadinessSeverity.Error,
                            "COLLIDER_DIRECTION_INVALID",
                            bone + " capsule direction must be X, Y, or Z.",
                            AnatomyStage,
                            bone);
                    break;
            }

            bool axisValid = IsFiniteDirection(role.JointAxis);
            bool secondaryValid = IsFiniteDirection(role.JointSecondaryAxis);
            if (!axisValid)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "JOINT_AXIS_INVALID",
                    bone + " primary joint axis must be finite and non-zero.",
                    AnatomyStage,
                    bone);
            if (!secondaryValid)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "JOINT_SECONDARY_AXIS_INVALID",
                    bone + " secondary joint axis must be finite and non-zero.",
                    AnatomyStage,
                    bone);
            if (axisValid && secondaryValid)
            {
                float dot = Mathf.Abs(Vector3.Dot(
                    role.JointAxis.normalized,
                    role.JointSecondaryAxis.normalized));
                if (dot >= ParallelDotThreshold)
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "JOINT_AXES_PARALLEL",
                        bone + " primary and secondary joint axes are parallel. They must define two different directions.",
                        AnatomyStage,
                        bone);
            }

            Vector3 low = role.AngularLowLimits;
            Vector3 high = role.AngularHighLimits;
            if (!IsFinite(low) || !IsFinite(high))
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "JOINT_LIMIT_NOT_FINITE",
                    bone + " joint limits contain a non-finite value.",
                    AnatomyStage,
                    bone);
                return;
            }

            if (!LimitsInsideSaneRange(low) || !LimitsInsideSaneRange(high))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "JOINT_LIMIT_RANGE_INVALID",
                    bone + " joint limits must stay between -180 and 180 degrees.",
                    AnatomyStage,
                    bone);
            ValidateLimitOrder(
                bone, "X", role.AngularXMotion, low.x, high.x, report);
            ValidateLimitOrder(
                bone, "Y", role.AngularYMotion, low.y, high.y, report);
            ValidateLimitOrder(
                bone, "Z", role.AngularZMotion, low.z, high.z, report);
        }

        private static void ValidateLimitOrder(
            HumanBodyBones role,
            string axis,
            NpcJointMotion motion,
            float low,
            float high,
            NpcBuildReadinessReport report)
        {
            if (motion != NpcJointMotion.Limited || low <= high) return;
            report.Add(
                NpcBuildReadinessSeverity.Error,
                "JOINT_LIMIT_ORDER_INVALID",
                role + " " + axis + " low limit must not exceed its high limit.",
                AnatomyStage,
                role);
        }

        private static void ValidatePreview(
            NpcDefinition definition,
            GameObject preview,
            NpcBuildReadinessReport report)
        {
            report.ExpectedRendererCount =
                definition?.AvatarSourceProfile?.Renderers?.Count ?? 0;
            if (preview == null)
            {
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "PREVIEW_MISSING",
                    string.IsNullOrWhiteSpace(report.PreviewAssetPath)
                        ? "No generated Physics Preview was supplied."
                        : "The generated Physics Preview is missing at '"
                          + report.PreviewAssetPath + "'.",
                    PreviewStage);
                return;
            }
            if (!string.IsNullOrWhiteSpace(report.PreviewAssetPath)
                && !NpcPhysicsPreviewBuilder.ReceiptMatches(
                    definition,
                    report.PreviewAssetPath,
                    out string receiptDetail))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "PREVIEW_RECEIPT_STALE",
                    receiptDetail,
                    PreviewStage);

            Transform animationRoot = SingleDirectChild(
                preview.transform, "AnimationRoot", report);
            Transform physicsRoot = SingleDirectChild(
                preview.transform, "Physics", report);
            if (animationRoot == null || physicsRoot == null) return;

            SkinnedMeshRenderer[] animationRenderers = animationRoot
                .GetComponentsInChildren<SkinnedMeshRenderer>(true);
            report.RendererCount = animationRenderers.Length;
            if (report.RendererCount != report.ExpectedRendererCount)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "PREVIEW_RENDERER_COUNT_INVALID",
                    "AnimationRoot has " + report.RendererCount
                    + " skinned renderer(s); the accepted Avatar snapshot expects "
                    + report.ExpectedRendererCount + ".",
                    PreviewStage);

            SkinnedMeshRenderer[] sourceRenderers = definition?.SourceAvatar == null
                ? new SkinnedMeshRenderer[0]
                : definition.SourceAvatar
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (sourceRenderers.Length > 0
                && !RendererMeshesMatch(sourceRenderers, animationRenderers))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "PREVIEW_RENDERERS_NOT_PRESERVED",
                    "AnimationRoot does not preserve the source Avatar's skinned renderer meshes.",
                    PreviewStage);

            Rigidbody[] bodies = physicsRoot.GetComponentsInChildren<Rigidbody>(true);
            Collider[] colliders = physicsRoot.GetComponentsInChildren<Collider>(true);
            ConfigurableJoint[] joints = physicsRoot
                .GetComponentsInChildren<ConfigurableJoint>(true);
            report.RigidbodyCount = bodies.Length;
            report.ColliderCount = colliders.Length;
            report.JointCount = joints.Length;

            ValidateExactCount(
                report.RigidbodyCount,
                report.ExpectedRoleCount,
                "rigidbodies",
                "PREVIEW_RIGIDBODY_COUNT_INVALID",
                report);
            ValidateExactCount(
                report.ColliderCount,
                report.ExpectedRoleCount,
                "colliders",
                "PREVIEW_COLLIDER_COUNT_INVALID",
                report);
            ValidateExactCount(
                report.JointCount,
                report.ExpectedRoleCount,
                "ConfigurableJoints",
                "PREVIEW_JOINT_COUNT_INVALID",
                report);

            if (preview.GetComponentsInChildren<Rigidbody>(true).Length
                != report.RigidbodyCount
                || preview.GetComponentsInChildren<Collider>(true).Length
                != report.ColliderCount
                || preview.GetComponentsInChildren<ConfigurableJoint>(true).Length
                != report.JointCount)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "PREVIEW_PHYSICS_OUTSIDE_ROOT",
                    "Physics components must live only beneath the Physics sibling.",
                    PreviewStage);

            if (physicsRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "PREVIEW_RENDERER_UNDER_PHYSICS",
                    "Visible Avatar renderers must remain under AnimationRoot, not Physics.",
                    PreviewStage);

            var bodyGroups = bodies
                .GroupBy(value => value.gameObject.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(),
                    StringComparer.Ordinal);
            foreach (IGrouping<string, Rigidbody> duplicate in bodies
                         .GroupBy(value => value.gameObject.name, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "PREVIEW_BODY_ROLE_DUPLICATE",
                    "Physics contains " + duplicate.Count() + " rigidbodies named '"
                    + duplicate.Key + "'.",
                    PreviewStage);

            HumanBodyBones[] expectedRoles = ExpectedRoles(definition);
            var roleBodies = new Dictionary<HumanBodyBones, Rigidbody>();
            foreach (HumanBodyBones role in expectedRoles)
            {
                if (!bodyGroups.TryGetValue(role.ToString(), out Rigidbody[] matches)
                    || matches.Length != 1)
                {
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "PREVIEW_BODY_ROLE_MISSING",
                        "Physics must contain one unique " + role + " rigidbody.",
                        PreviewStage,
                        role);
                    continue;
                }
                roleBodies[role] = matches[0];
            }

            foreach (string unexpected in bodyGroups.Keys
                         .Where(name => !expectedRoles.Any(
                             role => string.Equals(
                                 role.ToString(), name, StringComparison.Ordinal)))
                         .OrderBy(value => value, StringComparer.Ordinal))
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "PREVIEW_BODY_ROLE_UNEXPECTED",
                    "Physics contains an unexpected rigidbody named '"
                    + unexpected + "'.",
                    PreviewStage);

            int unownedColliderCount = colliders.Count(value =>
                FindOwningBody(value, physicsRoot) == null);
            if (unownedColliderCount > 0)
                report.Add(
                    NpcBuildReadinessSeverity.Error,
                    "PREVIEW_COLLIDER_UNOWNED",
                    "Physics contains " + unownedColliderCount
                    + " collider(s) outside a Rigidbody body hierarchy.",
                    PreviewStage);

            foreach (HumanBodyBones role in expectedRoles)
            {
                if (!roleBodies.TryGetValue(role, out Rigidbody body)) continue;

                Collider[] ownedColliders = colliders.Where(value =>
                    FindOwningBody(value, physicsRoot) == body).ToArray();
                Transform[] colliderHolders = DirectChildrenNamed(
                    body.transform, PrimaryColliderName);
                Collider[] markedColliders = colliderHolders.Length == 1
                    ? colliderHolders[0].GetComponents<Collider>()
                    : new Collider[0];
                bool colliderContractValid = colliderHolders.Length == 1
                                             && markedColliders.Length == 1
                                             && ownedColliders.Length == 1
                                             && ownedColliders[0]
                                             == markedColliders[0];
                if (!colliderContractValid)
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "PREVIEW_BODY_COLLIDER_INVALID",
                        role + " must own exactly one collider on one direct '"
                        + PrimaryColliderName + "' child. Found "
                        + ownedColliders.Length + " owned collider(s), "
                        + colliderHolders.Length + " marker child(ren), and "
                        + markedColliders.Length + " collider(s) on the marker.",
                        PreviewStage,
                        role);

                ConfigurableJoint[] ownedJoints = body
                    .GetComponents<ConfigurableJoint>();
                if (ownedJoints.Length != 1)
                {
                    report.Add(
                        NpcBuildReadinessSeverity.Error,
                        "PREVIEW_BODY_JOINT_INVALID",
                        role + " owns " + ownedJoints.Length
                        + " ConfigurableJoints; exactly one is required.",
                        PreviewStage,
                        role);
                    continue;
                }

                ConfigurableJoint joint = ownedJoints[0];
                if (TryGetParent(
                        role, out HumanBodyBones parentRole))
                {
                    if (!roleBodies.TryGetValue(parentRole, out Rigidbody parentBody))
                        continue;
                    if (joint.connectedBody != parentBody)
                        report.Add(
                            NpcBuildReadinessSeverity.Error,
                            "PREVIEW_JOINT_CONNECTION_INVALID",
                            role + " joint must connect to " + parentRole + ".",
                            PreviewStage,
                            role);
                    if (body.transform.parent != parentBody.transform)
                        report.Add(
                            NpcBuildReadinessSeverity.Error,
                            "PREVIEW_BODY_HIERARCHY_INVALID",
                            role + " body must be parented beneath " + parentRole + ".",
                            PreviewStage,
                            role);
                }
                else
                {
                    if (joint.connectedBody != null)
                        report.Add(
                            NpcBuildReadinessSeverity.Error,
                            "PREVIEW_ROOT_JOINT_INVALID",
                            role + " is the graph root and must not have a connected body.",
                            PreviewStage,
                            role);
                    if (body.transform.parent != physicsRoot)
                        report.Add(
                            NpcBuildReadinessSeverity.Error,
                            "PREVIEW_BODY_HIERARCHY_INVALID",
                            role + " body must be a direct child of Physics.",
                            PreviewStage,
                            role);
                }
            }
        }

        private static Transform SingleDirectChild(
            Transform root,
            string name,
            NpcBuildReadinessReport report)
        {
            Transform[] matches = DirectChildrenNamed(root, name);
            if (matches.Length == 1) return matches[0];
            report.Add(
                NpcBuildReadinessSeverity.Error,
                "PREVIEW_ROOT_SIBLING_INVALID",
                matches.Length == 0
                    ? "The preview root is missing the direct '" + name + "' sibling."
                    : "The preview root contains " + matches.Length
                      + " direct children named '" + name + "'.",
                PreviewStage);
            return null;
        }

        private static Transform[] DirectChildrenNamed(
            Transform root,
            string name)
        {
            return Enumerable.Range(0, root.childCount)
                .Select(root.GetChild)
                .Where(value => string.Equals(
                    value.name, name, StringComparison.Ordinal))
                .ToArray();
        }

        // Component.GetComponentInParent<T>() skips inactive prefab-asset
        // hierarchies in this Unity version. Walk the serialized transform chain
        // explicitly so a persistent prefab reports the same owner as an
        // instantiated preview.
        private static Rigidbody FindOwningBody(
            Collider collider,
            Transform physicsRoot)
        {
            Transform current = collider == null ? null : collider.transform;
            while (current != null && current != physicsRoot)
            {
                Rigidbody body = current.GetComponent<Rigidbody>();
                if (body != null) return body;
                current = current.parent;
            }
            return null;
        }

        private static void ValidateExactCount(
            int actual,
            int expected,
            string label,
            string code,
            NpcBuildReadinessReport report)
        {
            if (actual == expected) return;
            report.Add(
                NpcBuildReadinessSeverity.Error,
                code,
                "Physics has " + actual + " " + label + "; expected "
                + expected + ".",
                    PreviewStage);
        }

        private static HumanBodyBones[] ExpectedRoles(NpcDefinition definition)
        {
            return definition != null && definition.IncludePhysicalJaw
                ? NpcHumanoidGraph.CanonicalRoles
                    .Concat(new[] { HumanBodyBones.Jaw })
                    .ToArray()
                : NpcHumanoidGraph.CanonicalRoles;
        }

        private static bool TryGetParent(
            HumanBodyBones role,
            out HumanBodyBones parent)
        {
            if (role == HumanBodyBones.Jaw)
            {
                parent = HumanBodyBones.Head;
                return true;
            }
            return NpcHumanoidGraph.TryGetParent(role, out parent);
        }

        private static bool RendererMeshesMatch(
            IEnumerable<SkinnedMeshRenderer> source,
            IEnumerable<SkinnedMeshRenderer> preview)
        {
            string[] sourceIds = source.Select(value => StableMeshId(value.sharedMesh))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] previewIds = preview.Select(value => StableMeshId(value.sharedMesh))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return sourceIds.SequenceEqual(previewIds, StringComparer.Ordinal);
        }

        private static string StableMeshId(Mesh mesh)
        {
            if (mesh == null) return "<null>";
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    mesh, out string guid, out long localId))
                return guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
            return mesh.name + ":" + mesh.vertexCount.ToString(CultureInfo.InvariantCulture);
        }

        private static NpcBuildReadinessSeverity RigSeverity(
            NpcRigIssueSeverity severity)
        {
            switch (severity)
            {
                case NpcRigIssueSeverity.Error:
                    return NpcBuildReadinessSeverity.Error;
                case NpcRigIssueSeverity.Warning:
                    return NpcBuildReadinessSeverity.Warning;
                default:
                    return NpcBuildReadinessSeverity.Info;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinitePositive(Vector3 value)
        {
            return IsFinitePositive(value.x)
                   && IsFinitePositive(value.y)
                   && IsFinitePositive(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y)
                                     && IsFinite(value.z) && IsFinite(value.w);
        }

        private static float QuaternionLengthSquared(Quaternion value)
        {
            return value.x * value.x + value.y * value.y
                                     + value.z * value.z + value.w * value.w;
        }

        private static bool IsFiniteDirection(Vector3 value)
        {
            return IsFinite(value) && value.sqrMagnitude >= DirectionEpsilon;
        }

        private static bool LimitsInsideSaneRange(Vector3 value)
        {
            return Mathf.Abs(value.x) <= 180f + LimitEpsilon
                   && Mathf.Abs(value.y) <= 180f + LimitEpsilon
                   && Mathf.Abs(value.z) <= 180f + LimitEpsilon;
        }

        private static string ComputeFingerprint(
            NpcDefinition definition,
            GameObject preview,
            NpcBuildReadinessReport report)
        {
            var value = new StringBuilder("npc-readiness-v5|");
            Append(value, definition == null ? "<null>" : definition.name);
            if (definition != null)
            {
                Append(value, definition.SourceAssetGuid);
                Append(value, definition.SourceDependencyHash);
                Append(value, report.CurrentSourceDependencyHash);
                Append(value, definition.CreatedWithToolkitVersion);
                if (definition.BuildProfile != null)
                {
                    Append(value, definition.BuildProfile.GeneratedAssetFolder);
                    Append(value, definition.BuildProfile.CompatibilityProfileId);
                }
                Append(value, ((int)definition.AudioMode)
                    .ToString(CultureInfo.InvariantCulture));
                Append(value, definition.IncludeSecondaryMotion
                    ? "secondary-motion-enabled-v1"
                    : "secondary-motion-disabled-v1");
                AppendAudio(value, definition.AudioProfile);
                AppendAnatomy(value, definition.AnatomyProfile);
                AppendMovement(value, definition.MovementProfile);
                if (definition.IncludePhysicalJaw)
                {
                    Append(value, "physical-jaw-v1");
                    Append(value, definition.AnatomyProfile?.OptionalJaw == null
                        ? "<no-jaw-profile>"
                        : RoleFingerprint(definition.AnatomyProfile.OptionalJaw));
                    if (definition.AnatomyProfile != null)
                    {
                        NpcBodyRoleProfile jaw =
                            definition.AnatomyProfile.OptionalJaw;
                        if (jaw != null)
                        {
                            Append(value, Float(jaw.JointDriveMaxForce));
                            Append(value, Float(jaw.MuscleSpring));
                            Append(value, Float(jaw.MuscleDamper));
                            Append(value, Float(jaw.MuscleWeight));
                        }
                        Append(value, Vector(
                            definition.AnatomyProfile.JawClosedReferenceLocal));
                        Append(value, QuaternionValue(
                            definition.AnatomyProfile.JawClosedLocalRotation));
                    }
                }
            }

            Append(value, report.PreviewAssetPath);
            // Do not include Unity's imported dependency hash for this generated
            // prefab. Re-saving byte-identical preview content can produce a new
            // artifact hash after a platform refresh. ReceiptMatches already
            // verifies the deterministic prefab-file hash, and AppendPreview
            // records the validated runtime structure below.
            AppendPreview(value, preview);
            foreach (NpcBuildReadinessIssue issue in report.Issues)
            {
                Append(value, ((int)issue.Severity)
                    .ToString(CultureInfo.InvariantCulture));
                Append(value, issue.Code);
                Append(value, issue.Role.HasValue
                    ? ((int)issue.Role.Value).ToString(CultureInfo.InvariantCulture)
                    : string.Empty);
                Append(value, issue.Message);
            }
            return Hash128.Compute(value.ToString()).ToString();
        }

        private static void AppendAnatomy(
            StringBuilder value,
            NpcAnatomyProfile anatomy)
        {
            if (anatomy == null)
            {
                Append(value, "<no-anatomy>");
                return;
            }
            Append(value, anatomy.BaselineSourceDependencyHash);
            Append(value, anatomy.BaselineToolkitVersion);
            IEnumerable<NpcBodyRoleProfile> ordered =
                (anatomy.BodyRoles ?? new NpcBodyRoleProfile[0])
                .Where(role => role != null)
                .OrderBy(role => (int)role.Role)
                .ThenBy(RoleFingerprint, StringComparer.Ordinal);
            foreach (NpcBodyRoleProfile role in ordered)
                Append(value, RoleFingerprint(role));
        }

        private static void AppendMovement(
            StringBuilder value,
            NpcMovementProfile profile)
        {
            if (profile == null)
            {
                Append(value, "<no-movement-profile>");
                return;
            }

            Append(value, StableObjectId(profile));
            Append(value, StableObjectDependencyHash(profile));
            Append(value, ((int)profile.AlignmentState)
                .ToString(CultureInfo.InvariantCulture));
            Append(value, profile.AutoFitSourceDependencyHash);
            Append(value, profile.AutoFitAuthoringFingerprint);
            Append(value, profile.AutoFitToolkitVersion);
            Append(value, Float(profile.EyeHeight));
            Append(value, Float(profile.BodyHeight));
            Append(value, Float(profile.NavHeight));
            Append(value, Float(profile.LeftLegLength));
            Append(value, Float(profile.RightLegLength));
            Append(value, Float(profile.MeanLegLength));
            Append(value, Float(profile.HipWidth));
            Append(value, Float(profile.StanceWidth));
            Append(value, Float(profile.SoleHeight));
            Append(value, Float(profile.NavRadius));
            Append(value, Float(profile.NavBaseOffset));
            Append(value, Vector(profile.LeftFootForwardLocal));
            Append(value, Vector(profile.RightFootForwardLocal));
            Append(value, Float(profile.PelvisHeightOffset));
            Append(value, Float(profile.StanceWidthScale));
            Append(value, Float(profile.LeftFootYawCorrectionDegrees));
            Append(value, Float(profile.RightFootYawCorrectionDegrees));
            Append(value, Float(profile.StrideScale));
            Append(value, Float(profile.StepHeightScale));
            Append(value, Float(profile.StepRateScale));
            Append(value, Float(profile.WalkSpeed));
            Append(value, Float(profile.Acceleration));
            Append(value, Float(profile.AngularSpeed));
            Append(value, Float(profile.StoppingDistance));
            Append(value, Float(profile.StartingHostility));
            Append(value, Float(profile.HostilityAfterTypicalHit));
            Append(value, Float(profile.RetaliationVengefulness));
            Append(value, StableObjectId(profile.ProviderStandingPose));
            Append(value, StableObjectDependencyHash(
                profile.ProviderStandingPose));
            Append(value, StableObjectId(profile.ProviderMovementConfig));
            Append(value, StableObjectDependencyHash(
                profile.ProviderMovementConfig));
            Append(value, profile.ProviderRecipeFingerprint);
        }

        private static void AppendAudio(
            StringBuilder value,
            NpcAudioProfile profile)
        {
            if (profile == null)
            {
                Append(value, "<no-audio-profile>");
                return;
            }

            string profilePath = AssetDatabase.GetAssetPath(profile);
            Append(value, profilePath);
            Append(value, string.IsNullOrWhiteSpace(profilePath)
                ? string.Empty
                : AssetDatabase.GetAssetDependencyHash(profilePath).ToString());
            Append(value, Float(profile.PitchMultiplier));
            Append(value, Float(profile.FootstepVolumeMultiplier));
            Append(value, profile.Language);
            Append(value, profile.Source);
            Append(value, profile.Credit);
            Append(value, profile.LicenseOrPermission);
            Append(value, profile.Notes);
            foreach (NpcAudioEvent audioEvent in AudioEventOrder)
            {
                Append(value, ((int)audioEvent).ToString(CultureInfo.InvariantCulture));
                AppendAudioClips(value, profile.GetClips(audioEvent));
            }
            AppendAudioClips(value, profile.WalkConcrete);
            AppendAudioClips(value, profile.RunConcrete);
            Append(value, StableAudioId(profile.DotLoop1));
            Append(value, StableAudioId(profile.AgroMovementLoop));
            Append(value, StableAudioId(profile.MovementLoop));
        }

        private static void AppendAudioClips(
            StringBuilder value,
            IReadOnlyList<AudioClip> clips)
        {
            Append(value, clips.Count.ToString(CultureInfo.InvariantCulture));
            for (int index = 0; index < clips.Count; index++)
                Append(value, StableAudioId(clips[index]));
        }

        private static string StableAudioId(AudioClip clip)
        {
            if (clip == null) return "<null>";
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    clip, out string guid, out long localId))
            {
                string path = AssetDatabase.GetAssetPath(clip);
                string dependency = string.IsNullOrWhiteSpace(path)
                    ? string.Empty
                    : AssetDatabase.GetAssetDependencyHash(path).ToString();
                return guid + ":" + localId.ToString(CultureInfo.InvariantCulture)
                       + ":" + dependency;
            }
            return "<transient>:" + clip.name + ":"
                   + clip.samples.ToString(CultureInfo.InvariantCulture) + ":"
                   + clip.channels.ToString(CultureInfo.InvariantCulture) + ":"
                   + clip.frequency.ToString(CultureInfo.InvariantCulture);
        }

        private static string StableObjectId(UnityEngine.Object value)
        {
            if (value == null) return "<null>";
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    value, out string guid, out long localId))
                return guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
            return "<transient>:" + value.GetType().FullName + ":" + value.name;
        }

        private static string StableObjectDependencyHash(UnityEngine.Object value)
        {
            if (value == null) return string.Empty;
            string path = AssetDatabase.GetAssetPath(value);
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : AssetDatabase.GetAssetDependencyHash(path).ToString();
        }

        private static string RoleFingerprint(NpcBodyRoleProfile role)
        {
            var value = new StringBuilder();
            Append(value, ((int)role.Role).ToString(CultureInfo.InvariantCulture));
            Append(value, role.Enabled ? "1" : "0");
            Append(value, ((int)role.AlignmentState)
                .ToString(CultureInfo.InvariantCulture));
            Append(value, ((int)role.ColliderShape)
                .ToString(CultureInfo.InvariantCulture));
            Append(value, Float(role.MassKilograms));
            Append(value, Vector(role.ColliderCenter));
            Append(value, QuaternionValue(role.ColliderLocalRotation));
            Append(value, Vector(role.ColliderSize));
            Append(value, Float(role.CapsuleRadius));
            Append(value, Float(role.CapsuleHeight));
            Append(value, role.CapsuleDirection.ToString(CultureInfo.InvariantCulture));
            Append(value, Vector(role.JointAxis));
            Append(value, Vector(role.JointSecondaryAxis));
            Append(value, ((int)role.AngularXMotion)
                .ToString(CultureInfo.InvariantCulture));
            Append(value, ((int)role.AngularYMotion)
                .ToString(CultureInfo.InvariantCulture));
            Append(value, ((int)role.AngularZMotion)
                .ToString(CultureInfo.InvariantCulture));
            Append(value, Vector(role.AngularLowLimits));
            Append(value, Vector(role.AngularHighLimits));
            return value.ToString();
        }

        private static void AppendPreview(StringBuilder value, GameObject preview)
        {
            if (preview == null)
            {
                Append(value, "<no-preview>");
                return;
            }
            Append(value, preview.name);
            foreach (Transform child in Enumerable.Range(0, preview.transform.childCount)
                         .Select(preview.transform.GetChild)
                         .OrderBy(item => item.name, StringComparer.Ordinal))
                Append(value, child.name);

            foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true)
                         .OrderBy(item => AnimationUtility.CalculateTransformPath(
                             item.transform, preview.transform), StringComparer.Ordinal))
            {
                Append(value, AnimationUtility.CalculateTransformPath(
                    body.transform, preview.transform));
                Append(value, Float(body.mass));
                ConfigurableJoint[] joints = body.GetComponents<ConfigurableJoint>();
                Append(value, joints.Length.ToString(CultureInfo.InvariantCulture));
                foreach (ConfigurableJoint joint in joints)
                {
                    Append(value, joint.connectedBody == null
                        ? "<world>"
                        : AnimationUtility.CalculateTransformPath(
                            joint.connectedBody.transform, preview.transform));
                    Append(value, Vector(joint.axis));
                    Append(value, Vector(joint.secondaryAxis));
                }
            }
            foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true)
                         .OrderBy(item => AnimationUtility.CalculateTransformPath(
                             item.transform, preview.transform), StringComparer.Ordinal))
            {
                Append(value, AnimationUtility.CalculateTransformPath(
                    collider.transform, preview.transform));
                Append(value, collider.GetType().FullName);
                if (collider is BoxCollider box)
                    Append(value, Vector(box.size));
                else if (collider is CapsuleCollider capsule)
                {
                    Append(value, Float(capsule.radius));
                    Append(value, Float(capsule.height));
                    Append(value, capsule.direction.ToString(CultureInfo.InvariantCulture));
                }
                else if (collider is SphereCollider sphere)
                    Append(value, Float(sphere.radius));
            }
            foreach (SkinnedMeshRenderer renderer in preview
                         .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                         .OrderBy(item => AnimationUtility.CalculateTransformPath(
                             item.transform, preview.transform), StringComparer.Ordinal))
            {
                Append(value, AnimationUtility.CalculateTransformPath(
                    renderer.transform, preview.transform));
                Append(value, StableMeshId(renderer.sharedMesh));
            }
        }

        private static string Float(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Vector(Vector3 value)
        {
            return Float(value.x) + "," + Float(value.y) + "," + Float(value.z);
        }

        private static string QuaternionValue(Quaternion value)
        {
            return Float(value.x) + "," + Float(value.y) + ","
                   + Float(value.z) + "," + Float(value.w);
        }

        private static void Append(StringBuilder target, string value)
        {
            string safe = value ?? string.Empty;
            target.Append(safe.Length.ToString(CultureInfo.InvariantCulture));
            target.Append(':');
            target.Append(safe);
            target.Append('|');
        }
    }
}
