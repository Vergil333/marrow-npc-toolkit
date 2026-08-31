using System;
using System.Collections.Generic;
using System.Linq;
using SLZ.Marrow.Warehouse;
using UnityEditor;
using UnityEngine;
using MarrowAvatar = SLZ.VRMK.Avatar;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor.AvatarIntake
{
    public enum AvatarIntakeSeverity
    {
        Info,
        Warning,
        Error,
    }

    public sealed class AvatarIntakeIssue
    {
        public AvatarIntakeSeverity Severity { get; }
        public string Message { get; }

        public AvatarIntakeIssue(AvatarIntakeSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    public sealed class AvatarIntakeReport
    {
        public Object Input { get; internal set; }
        public GameObject Source { get; internal set; }
        public AvatarCrate AvatarCrate { get; internal set; }
        public string AssetPath { get; internal set; }
        public Animator Animator { get; internal set; }
        public MarrowAvatar MarrowAvatar { get; internal set; }
        public ModelImporter ModelImporter { get; internal set; }
        public int RendererCount { get; internal set; }
        public IReadOnlyList<AvatarIntakeIssue> Issues => issues;
        public IReadOnlyDictionary<HumanBodyBones, string> HumanoidBonePaths =>
            humanoidBonePaths;

        private readonly List<AvatarIntakeIssue> issues = new List<AvatarIntakeIssue>();
        private readonly Dictionary<HumanBodyBones, string> humanoidBonePaths =
            new Dictionary<HumanBodyBones, string>();

        public bool HasErrors => issues.Any(value => value.Severity == AvatarIntakeSeverity.Error);
        public bool IsHumanoid => Animator != null
                                  && Animator.avatar != null
                                  && Animator.avatar.isValid
                                  && Animator.avatar.isHuman;
        public bool IsMarrowAvatar => MarrowAvatar != null;
        public bool IsAvatarCrate => AvatarCrate != null;
        public bool CanConfigureAsHumanoid => ModelImporter != null && !IsHumanoid;
        public bool CanCreateMarrowAvatar => !HasErrors && IsHumanoid && !IsMarrowAvatar;
        public bool ReadyForNpcDefinition => !HasErrors && IsHumanoid && IsMarrowAvatar;

        internal void Add(AvatarIntakeSeverity severity, string message)
        {
            issues.Add(new AvatarIntakeIssue(severity, message));
        }

        internal void SetHumanoidBonePath(HumanBodyBones role, string path)
        {
            humanoidBonePaths[role] = path;
        }

        public bool HasHumanoidBone(HumanBodyBones role)
        {
            return humanoidBonePaths.ContainsKey(role);
        }

        public string GetHumanoidBonePath(HumanBodyBones role)
        {
            return humanoidBonePaths.TryGetValue(role, out string path)
                ? path
                : string.Empty;
        }
    }

    public static class AvatarIntakeValidator
    {
        private static readonly HashSet<HumanBodyBones> OptionalAvatarBones =
            new HashSet<HumanBodyBones>
            {
                HumanBodyBones.LeftEye,
                HumanBodyBones.RightEye,
                HumanBodyBones.Jaw,
                HumanBodyBones.UpperChest,
                HumanBodyBones.LeftMiddleProximal,
                HumanBodyBones.LeftMiddleIntermediate,
                HumanBodyBones.LeftMiddleDistal,
                HumanBodyBones.RightMiddleProximal,
                HumanBodyBones.RightMiddleIntermediate,
                HumanBodyBones.RightMiddleDistal,
                HumanBodyBones.LeftLittleProximal,
                HumanBodyBones.LeftLittleIntermediate,
                HumanBodyBones.LeftLittleDistal,
                HumanBodyBones.RightLittleProximal,
                HumanBodyBones.RightLittleIntermediate,
                HumanBodyBones.RightLittleDistal,
            };

        public static AvatarIntakeReport Validate(Object input)
        {
            var report = new AvatarIntakeReport { Input = input };
            if (input == null)
            {
                report.Add(AvatarIntakeSeverity.Info,
                    "Select an AvatarCrate, an existing Marrow Avatar prefab, or a model asset.");
                return report;
            }

            GameObject source;
            if (input is AvatarCrate avatarCrate)
            {
                report.AvatarCrate = avatarCrate;
                source = avatarCrate.MainGameObject?.EditorAsset;
                if (source == null)
                {
                    report.Add(AvatarIntakeSeverity.Error,
                        "The selected AvatarCrate does not resolve to a prefab in this Unity project.");
                    return report;
                }
            }
            else
            {
                source = input as GameObject;
                if (source == null)
                {
                    report.Add(AvatarIntakeSeverity.Error,
                        "The selected asset is not an AvatarCrate, prefab, or model.");
                    return report;
                }
            }

            report.Source = source;

            string path = AssetDatabase.GetAssetPath(source);
            report.AssetPath = path;
            if (string.IsNullOrWhiteSpace(path))
            {
                report.Add(AvatarIntakeSeverity.Error,
                    "The source must be a Project asset, not a Scene instance.");
                return report;
            }

            report.ModelImporter = AssetImporter.GetAtPath(path) as ModelImporter;
            Animator[] animators = source.GetComponentsInChildren<Animator>(true);
            report.Animator = source.GetComponent<Animator>()
                              ?? animators.FirstOrDefault();
            report.MarrowAvatar = source.GetComponent<MarrowAvatar>();
            report.RendererCount = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;

            if (animators.Length > 1)
            {
                report.Add(AvatarIntakeSeverity.Warning,
                    $"The source contains {animators.Length} Animators. NPC generation will require one authoritative Humanoid Animator.");
            }

            if (report.Animator == null)
            {
                report.Add(AvatarIntakeSeverity.Error,
                    "No Animator was found on the source hierarchy.");
            }
            else if (report.Animator.avatar == null)
            {
                if (report.ModelImporter == null)
                    report.Add(AvatarIntakeSeverity.Error,
                        "The Animator has no Unity Avatar. Configure the source model as Humanoid first.");
                else
                    report.Add(AvatarIntakeSeverity.Info,
                        "The model is not Humanoid yet. The toolkit can configure its importer.");
            }
            else if (!report.Animator.avatar.isValid || !report.Animator.avatar.isHuman)
            {
                if (report.ModelImporter == null)
                    report.Add(AvatarIntakeSeverity.Error,
                        "The Animator Avatar is not a valid Unity Humanoid.");
                else
                    report.Add(AvatarIntakeSeverity.Info,
                        "The model needs a valid Unity Humanoid mapping before NPC authoring.");
            }

            if (report.IsHumanoid)
                CaptureHumanoidBonePaths(report);

            if (report.Animator != null && report.Animator.avatar != null
                && report.Animator.avatar.isHuman)
            {
                var missing = RequiredBones()
                    .Where(role => !report.HasHumanoidBone(role))
                    .Select(role => role.ToString())
                    .ToArray();
                if (missing.Length > 0)
                {
                    report.Add(AvatarIntakeSeverity.Error,
                        "Missing required Marrow Avatar bones: " + string.Join(", ", missing));
                }
            }

            if (report.RendererCount == 0)
            {
                report.Add(AvatarIntakeSeverity.Error,
                    "No SkinnedMeshRenderer was found. The NPC needs skinned character geometry.");
            }

            Vector3 scale = source.transform.localScale;
            bool nonUnit = Vector3.Distance(scale, Vector3.one) > 0.0001f;
            if (nonUnit)
                report.Add(AvatarIntakeSeverity.Error,
                    $"The source root scale is {scale}. Reset or bake the prefab root to (1, 1, 1) before NPC authoring; physics collider sizes and movement clearances require a unit-scale source root.");

            float rootRotation = Quaternion.Angle(
                source.transform.localRotation, Quaternion.identity);
            if (rootRotation > 0.01f)
                report.Add(AvatarIntakeSeverity.Error,
                    $"The source prefab root is rotated by {rootRotation:0.###} degrees. Reset or bake the root rotation so the NPC, NavMesh movement, and Humanoid forward direction share one frame.");

            ValidateMarrowAvatar(report);

            if (!report.HasErrors && report.IsHumanoid && report.MarrowAvatar == null)
            {
                report.Add(AvatarIntakeSeverity.Info,
                    "Valid Humanoid detected. Create a Marrow Avatar prefab to reuse the official Avatar setup and Scene handles.");
            }
            else if (report.ReadyForNpcDefinition)
            {
                report.Add(AvatarIntakeSeverity.Info,
                    $"Avatar intake is ready: {report.RendererCount} skinned renderer(s), complete required Humanoid mapping.");
            }

            return report;
        }

        public static AvatarIntakeReport Validate(GameObject source)
        {
            return Validate((Object)source);
        }

        private static IEnumerable<HumanBodyBones> RequiredBones()
        {
            foreach (HumanBodyBones role in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (role == HumanBodyBones.LastBone || OptionalAvatarBones.Contains(role))
                    continue;
                yield return role;
            }
        }

        private static void CaptureHumanoidBonePaths(AvatarIntakeReport report)
        {
            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(report.Source);
                instance.hideFlags = HideFlags.HideAndDontSave;
                Animator animator = instance.GetComponent<Animator>()
                                    ?? instance.GetComponentInChildren<Animator>(true);
                if (animator == null) return;

                foreach (HumanBodyBones role in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (role == HumanBodyBones.LastBone) continue;
                    Transform bone = animator.GetBoneTransform(role);
                    if (bone != null)
                    {
                        report.SetHumanoidBonePath(
                            role,
                            AnimationUtility.CalculateTransformPath(
                                bone, instance.transform));
                    }
                }
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        private static void ValidateMarrowAvatar(AvatarIntakeReport report)
        {
            MarrowAvatar avatar = report.MarrowAvatar;
            if (avatar == null) return;

            if (avatar.animator == null)
                report.Add(AvatarIntakeSeverity.Error,
                    "The Marrow Avatar component has no Animator reference.");
            else if (report.Animator != null && avatar.animator != report.Animator)
                report.Add(AvatarIntakeSeverity.Warning,
                    "The Marrow Avatar references a different Animator than the intake scan selected.");

            if (avatar.wristLf == null || avatar.wristRt == null)
                report.Add(AvatarIntakeSeverity.Error,
                    "Marrow Avatar wrist references are incomplete. Hand bones are valid fallbacks.");

            if (avatar.bodyMeshes == null || avatar.bodyMeshes.Length == 0
                || avatar.bodyMeshes.Any(value => value == null))
                report.Add(AvatarIntakeSeverity.Error,
                    "Marrow Avatar Body Meshes must contain the character's skinned body renderers without null entries.");

            var configuredRenderers = new HashSet<SkinnedMeshRenderer>();
            if (avatar.bodyMeshes != null) configuredRenderers.UnionWith(avatar.bodyMeshes);
            if (avatar.headMeshes != null) configuredRenderers.UnionWith(avatar.headMeshes);
            if (avatar.hairMeshes != null) configuredRenderers.UnionWith(avatar.hairMeshes);
            int unassignedRendererCount = report.Source
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Count(value => !configuredRenderers.Contains(value));
            if (unassignedRendererCount > 0)
                report.Add(AvatarIntakeSeverity.Warning,
                    $"{unassignedRendererCount} skinned renderer(s) are not assigned to Body, Head, or Hair Meshes. Review them in the official Avatar inspector.");

            bool hasEyes = report.HasHumanoidBone(HumanBodyBones.LeftEye)
                           && report.HasHumanoidBone(HumanBodyBones.RightEye);
            if (!hasEyes && avatar.eyeCenterOverride == null)
                report.Add(AvatarIntakeSeverity.Warning,
                    "No mapped eye bones or Eye Center Override. Add and position an override in the official Avatar inspector.");
        }
    }
}
