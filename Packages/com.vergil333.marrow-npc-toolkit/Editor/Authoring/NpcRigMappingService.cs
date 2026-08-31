using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.AvatarIntake;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor.Authoring
{
    public enum NpcRigIssueSeverity
    {
        Info,
        Warning,
        Error,
    }

    public sealed class NpcRigIssue
    {
        public NpcRigIssueSeverity Severity { get; }
        public string Message { get; }

        public NpcRigIssue(NpcRigIssueSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    public sealed class NpcRigMappingEntry
    {
        public HumanBodyBones Role { get; }
        public string SnapshotPath { get; }
        public string CurrentPath { get; }
        public bool Resolves => !string.IsNullOrWhiteSpace(CurrentPath);
        public bool MatchesSnapshot => Resolves
                                       && string.Equals(
                                           SnapshotPath,
                                           CurrentPath,
                                           StringComparison.Ordinal);

        public NpcRigMappingEntry(
            HumanBodyBones role,
            string snapshotPath,
            string currentPath)
        {
            Role = role;
            SnapshotPath = snapshotPath ?? string.Empty;
            CurrentPath = currentPath ?? string.Empty;
        }
    }

    public sealed class NpcRigMappingReport
    {
        private readonly List<NpcRigIssue> issues = new List<NpcRigIssue>();
        private readonly List<NpcRigMappingEntry> entries =
            new List<NpcRigMappingEntry>();

        public IReadOnlyList<NpcRigIssue> Issues => issues;
        public IReadOnlyList<NpcRigMappingEntry> Entries => entries;
        public string CurrentSourceDependencyHash { get; internal set; }
        public bool SourceChanged { get; internal set; }
        public int ResolvedCanonicalCount => entries.Count(value => value.Resolves);
        public int MatchingCanonicalCount => entries.Count(value => value.MatchesSnapshot);
        public bool HasErrors => issues.Any(value => value.Severity == NpcRigIssueSeverity.Error);
        public bool ReadyForBaseline => !HasErrors && !SourceChanged
                                        && MatchingCanonicalCount
                                        == NpcHumanoidGraph.CanonicalRoles.Length;

        internal void Add(NpcRigIssueSeverity severity, string message)
        {
            issues.Add(new NpcRigIssue(severity, message));
        }

        internal void Add(NpcRigMappingEntry entry)
        {
            entries.Add(entry);
        }
    }

    public static class NpcRigMappingService
    {
        public static NpcRigMappingReport Validate(NpcDefinition definition)
        {
            var report = new NpcRigMappingReport();
            if (definition == null)
            {
                report.Add(NpcRigIssueSeverity.Error, "No NPC Definition is selected.");
                return report;
            }
            if (definition.SourceAvatar == null || definition.AvatarSourceProfile == null)
            {
                report.Add(NpcRigIssueSeverity.Error,
                    "The NPC Definition is missing its Avatar source profile.");
                return report;
            }

            string sourcePath = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            report.CurrentSourceDependencyHash = string.IsNullOrWhiteSpace(sourcePath)
                ? string.Empty
                : AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
            report.SourceChanged = !string.Equals(
                report.CurrentSourceDependencyHash,
                definition.AvatarSourceProfile.SourceDependencyHash,
                StringComparison.Ordinal);
            if (report.SourceChanged)
                report.Add(NpcRigIssueSeverity.Warning,
                    "The Avatar asset changed after its snapshot was captured. Refresh the snapshot before accepting new alignment.");

            AvatarIntakeReport intake = AvatarIntakeValidator.Validate(definition.SourceAvatar);
            foreach (AvatarIntakeIssue issue in intake.Issues)
            {
                if (issue.Severity == AvatarIntakeSeverity.Error)
                    report.Add(NpcRigIssueSeverity.Error, issue.Message);
            }

            var snapshotGroups = definition.AvatarSourceProfile.HumanoidBones
                .GroupBy(value => value.Role)
                .ToArray();
            foreach (IGrouping<HumanBodyBones, NpcHumanoidBoneBinding> duplicate
                     in snapshotGroups.Where(group => group.Count() > 1))
                report.Add(NpcRigIssueSeverity.Error,
                    $"The Avatar snapshot contains {duplicate.Count()} bindings for {duplicate.Key}.");
            var snapshotPaths = snapshotGroups
                .ToDictionary(group => group.Key, group => group.First().TransformPath);
            foreach (HumanBodyBones role in NpcHumanoidGraph.CanonicalRoles)
            {
                snapshotPaths.TryGetValue(role, out string snapshotPath);
                string currentPath = intake.GetHumanoidBonePath(role);
                var entry = new NpcRigMappingEntry(role, snapshotPath, currentPath);
                report.Add(entry);
                if (string.IsNullOrWhiteSpace(snapshotPath))
                    report.Add(NpcRigIssueSeverity.Error,
                        $"The Avatar snapshot has no {role} binding.");
                else if (!entry.Resolves)
                    report.Add(NpcRigIssueSeverity.Error,
                        $"The current Avatar no longer resolves {role}.");
                else if (!entry.MatchesSnapshot)
                    report.Add(NpcRigIssueSeverity.Error,
                        $"{role} moved from '{snapshotPath}' to '{currentPath}'. Refresh the Avatar snapshot and review the change.");
            }

            foreach (IGrouping<string, NpcRigMappingEntry> duplicate in report.Entries
                         .Where(value => !string.IsNullOrWhiteSpace(value.CurrentPath))
                         .GroupBy(value => value.CurrentPath, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
                report.Add(NpcRigIssueSeverity.Error,
                    $"Multiple canonical roles resolve to '{duplicate.Key}': "
                    + string.Join(", ", duplicate.Select(value => value.Role)) + ".");

            ValidateResolvedHierarchy(definition, report);

            int unassigned = definition.AvatarSourceProfile.Renderers.Count(value =>
                value.Category == NpcAvatarRendererCategory.Unassigned);
            if (unassigned > 0)
                report.Add(NpcRigIssueSeverity.Warning,
                    $"{unassigned} renderer binding(s) are unassigned. Classify them in the Marrow Avatar before generation.");

            if (report.ReadyForBaseline)
                report.Add(NpcRigIssueSeverity.Info,
                    "All 16 canonical NPC roles resolve to the accepted Avatar snapshot.");
            return report;
        }

        private static void ValidateResolvedHierarchy(
            NpcDefinition definition,
            NpcRigMappingReport report)
        {
            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(definition.SourceAvatar);
                instance.hideFlags = HideFlags.HideAndDontSave;
                Transform root = instance.transform;
                Transform animatorTransform = Resolve(root, definition.AvatarSourceProfile.AnimatorPath);
                Animator animator = animatorTransform == null
                    ? instance.GetComponent<Animator>()
                      ?? instance.GetComponentInChildren<Animator>(true)
                    : animatorTransform.GetComponent<Animator>();
                if (animator == null)
                {
                    report.Add(NpcRigIssueSeverity.Error,
                        "The snapshot Animator path no longer resolves an Animator component.");
                    return;
                }

                var resolved = new Dictionary<HumanBodyBones, Transform>();
                foreach (NpcRigMappingEntry entry in report.Entries)
                {
                    Transform pathBone = Resolve(root, entry.CurrentPath);
                    Transform humanoidBone = animator.GetBoneTransform(entry.Role);
                    if (pathBone == null)
                    {
                        report.Add(NpcRigIssueSeverity.Error,
                            $"{entry.Role} path '{entry.CurrentPath}' does not resolve on a clean prefab instance.");
                        continue;
                    }
                    if (pathBone != humanoidBone)
                    {
                        report.Add(NpcRigIssueSeverity.Error,
                            $"{entry.Role} path does not match Animator.GetBoneTransform on a clean prefab instance.");
                        continue;
                    }
                    resolved[entry.Role] = pathBone;
                }

                foreach (HumanBodyBones role in NpcHumanoidGraph.CanonicalRoles)
                {
                    if (!NpcHumanoidGraph.TryGetParent(role, out HumanBodyBones parent)
                        || !resolved.TryGetValue(role, out Transform bone)
                        || !resolved.TryGetValue(parent, out Transform parentBone))
                        continue;
                    if (!bone.IsChildOf(parentBone))
                        report.Add(NpcRigIssueSeverity.Error,
                            $"{role} is not below its semantic parent {parent} in the current Humanoid hierarchy.");
                }

                foreach (NpcAvatarRendererBinding binding in definition.AvatarSourceProfile.Renderers)
                {
                    Transform rendererTransform = Resolve(root, binding.TransformPath);
                    if (rendererTransform == null
                        || rendererTransform.GetComponent<SkinnedMeshRenderer>() == null)
                        report.Add(NpcRigIssueSeverity.Error,
                            $"Renderer path '{binding.TransformPath}' no longer resolves a SkinnedMeshRenderer.");
                }

                if (!string.IsNullOrWhiteSpace(
                        definition.AvatarSourceProfile.EyeCenterOverridePath)
                    && Resolve(root, definition.AvatarSourceProfile.EyeCenterOverridePath) == null)
                    report.Add(NpcRigIssueSeverity.Error,
                        "The Eye Center Override snapshot path no longer resolves.");

                if (definition.IncludePhysicalJaw)
                {
                    if (string.IsNullOrWhiteSpace(definition.AvatarSourceProfile.JawPath))
                        report.Add(NpcRigIssueSeverity.Warning,
                            "Physical jaw is enabled, but this Avatar has no mapped jaw. The jaw module will remain unavailable.");
                    else if (Resolve(root, definition.AvatarSourceProfile.JawPath) == null)
                        report.Add(NpcRigIssueSeverity.Error,
                            "The mapped jaw snapshot path no longer resolves.");
                }
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        private static Transform Resolve(Transform root, string path)
        {
            if (root == null) return null;
            return string.IsNullOrWhiteSpace(path) ? root : root.Find(path);
        }
    }
}
