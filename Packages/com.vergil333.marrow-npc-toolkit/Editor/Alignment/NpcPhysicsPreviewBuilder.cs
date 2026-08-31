using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Authoring;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor.Alignment
{
    public sealed class NpcPhysicsPreviewReport
    {
        private readonly List<string> issues = new List<string>();

        public bool Success { get; internal set; }
        public string AssetPath { get; internal set; }
        public string Fingerprint { get; internal set; }
        public int RigidbodyCount { get; internal set; }
        public int JointCount { get; internal set; }
        public int ColliderCount { get; internal set; }
        public int RendererCount { get; internal set; }
        public IReadOnlyList<string> Issues => issues;

        internal void Add(string message)
        {
            issues.Add(message);
        }
    }

    public static class NpcPhysicsPreviewBuilder
    {
        public const string PreviewSuffix = "NpcPhysicsPreview.prefab";
        private const string ReceiptPrefix =
            "Vergil333.MarrowNpcToolkit.PhysicsPreview.v1";

        public static NpcPhysicsPreviewReport Build(
            NpcDefinition definition,
            string outputPathOverride = null)
        {
            var report = new NpcPhysicsPreviewReport();
            if (definition == null || definition.SourceAvatar == null
                                   || definition.AvatarSourceProfile == null
                                   || definition.AnatomyProfile == null
                                   || definition.BuildProfile == null)
            {
                report.Add("The NPC Definition is missing an Avatar, anatomy, or build profile.");
                return report;
            }

            NpcRigMappingReport mapping = NpcRigMappingService.Validate(definition);
            if (!mapping.ReadyForBaseline)
            {
                report.Add("The accepted 16-role Avatar mapping is stale or invalid. Refresh and refit before generating a preview.");
                return report;
            }
            if (!definition.AnatomyProfile.BaselineMatches(
                    mapping.CurrentSourceDependencyHash))
            {
                report.Add("The anatomy baseline does not match the current Avatar dependency hash.");
                return report;
            }
            HumanBodyBones[] requestedRoles = RequestedRoles(definition);
            if (definition.IncludePhysicalJaw
                && !ValidateRequestedJaw(definition, report))
                return report;

            string outputPath = string.IsNullOrWhiteSpace(outputPathOverride)
                ? GetOutputPath(definition)
                : outputPathOverride.Replace('\\', '/');
            if (!outputPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !outputPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                report.Add("The physics preview path must be a .prefab under Assets/.");
                return report;
            }
            string sourcePath = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            if (string.Equals(
                    sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                report.Add("The generated preview path cannot be the source Avatar prefab.");
                return report;
            }

            string sourceHashBefore = AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
            Scene previewScene = default;
            GameObject generatedRoot = null;
            try
            {
                EnsureAssetFolder(Path.GetDirectoryName(outputPath)?.Replace('\\', '/'));
                previewScene = EditorSceneManager.NewPreviewScene();
                generatedRoot = new GameObject(
                    definition.SourceAvatar.name + " NPC Physics Preview");
                SceneManager.MoveGameObjectToScene(generatedRoot, previewScene);

                var animationRoot = new GameObject("AnimationRoot");
                animationRoot.transform.SetParent(generatedRoot.transform, false);
                var physicsRoot = new GameObject("Physics");
                physicsRoot.transform.SetParent(generatedRoot.transform, false);

                GameObject avatarInstance = PrefabUtility.InstantiatePrefab(
                    definition.SourceAvatar, previewScene) as GameObject;
                if (avatarInstance == null)
                    throw new InvalidOperationException(
                        "Unity could not instantiate the accepted Avatar prefab in the preview scene.");
                avatarInstance.transform.SetParent(animationRoot.transform, false);

                Animator animator = FindAnimator(
                    avatarInstance.transform,
                    definition.AvatarSourceProfile.AnimatorPath);
                if (animator == null)
                    throw new InvalidOperationException(
                        "The accepted Animator path did not resolve in the preview instance.");

                var roleTransforms = new Dictionary<HumanBodyBones, Transform>();
                var bodies = new Dictionary<HumanBodyBones, Rigidbody>();
                foreach (HumanBodyBones role in requestedRoles)
                {
                    Transform targetBone = animator.GetBoneTransform(role);
                    NpcBodyRoleProfile profile = ProfileForRole(
                        definition.AnatomyProfile, role);
                    if (targetBone == null || profile == null)
                        throw new InvalidOperationException(
                            $"Could not resolve the {role} preview contract.");

                    Transform parent = physicsRoot.transform;
                    if (TryGetParent(role, out HumanBodyBones parentRole))
                        parent = roleTransforms[parentRole];
                    var bodyObject = new GameObject(role.ToString());
                    bodyObject.layer = 12;
                    bodyObject.transform.SetParent(parent, false);
                    bodyObject.transform.position = targetBone.position;
                    bodyObject.transform.rotation = targetBone.rotation;
                    bodyObject.transform.localScale = Vector3.one;

                    Rigidbody body = bodyObject.AddComponent<Rigidbody>();
                    body.mass = profile.MassKilograms;
                    body.solverIterations = 20;
                    body.solverVelocityIterations = 20;
                    body.maxAngularVelocity = 20f;
                    body.isKinematic = true;
                    body.useGravity = false;

                    roleTransforms[role] = bodyObject.transform;
                    bodies[role] = body;
                    AddCollider(bodyObject.transform, profile);
                }

                foreach (HumanBodyBones role in requestedRoles)
                {
                    Transform bodyTransform = roleTransforms[role];
                    NpcBodyRoleProfile profile = ProfileForRole(
                        definition.AnatomyProfile, role);
                    ConfigurableJoint joint = bodyTransform.gameObject
                        .AddComponent<ConfigurableJoint>();
                    joint.autoConfigureConnectedAnchor = false;
                    joint.anchor = Vector3.zero;
                    joint.axis = SafeDirection(profile.JointAxis, Vector3.right);
                    joint.secondaryAxis = SafeDirection(
                        profile.JointSecondaryAxis, Vector3.up);
                    joint.enableCollision = false;
                    joint.rotationDriveMode = RotationDriveMode.Slerp;
                    joint.slerpDrive = new JointDrive
                    {
                        positionSpring = profile.MuscleSpring,
                        positionDamper = profile.MuscleDamper,
                        maximumForce = profile.JointDriveMaxForce,
                    };

                    if (TryGetParent(role, out HumanBodyBones parentRole))
                    {
                        Rigidbody parentBody = bodies[parentRole];
                        joint.connectedBody = parentBody;
                        joint.connectedAnchor = parentBody.transform
                            .InverseTransformPoint(bodyTransform.position);
                        joint.xMotion = ConfigurableJointMotion.Locked;
                        joint.yMotion = ConfigurableJointMotion.Locked;
                        joint.zMotion = ConfigurableJointMotion.Locked;
                    }
                    else
                    {
                        joint.connectedBody = null;
                        joint.connectedAnchor = bodyTransform.position;
                        joint.xMotion = ConfigurableJointMotion.Free;
                        joint.yMotion = ConfigurableJointMotion.Free;
                        joint.zMotion = ConfigurableJointMotion.Free;
                    }

                    joint.angularXMotion = Motion(profile.AngularXMotion);
                    joint.angularYMotion = Motion(profile.AngularYMotion);
                    joint.angularZMotion = Motion(profile.AngularZMotion);
                    joint.lowAngularXLimit = new SoftJointLimit
                    {
                        limit = profile.AngularLowLimits.x,
                    };
                    joint.highAngularXLimit = new SoftJointLimit
                    {
                        limit = profile.AngularHighLimits.x,
                    };
                    joint.angularYLimit = new SoftJointLimit
                    {
                        limit = Mathf.Max(
                            Mathf.Abs(profile.AngularLowLimits.y),
                            Mathf.Abs(profile.AngularHighLimits.y)),
                    };
                    joint.angularZLimit = new SoftJointLimit
                    {
                        limit = Mathf.Max(
                            Mathf.Abs(profile.AngularLowLimits.z),
                            Mathf.Abs(profile.AngularHighLimits.z)),
                    };
                }

                bool saved;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                    generatedRoot, outputPath, out saved);
                if (!saved || prefab == null)
                    throw new InvalidOperationException(
                        "Unity did not save the generated physics preview prefab.");
                AssetDatabase.SaveAssets();
                WriteReceipt(definition, outputPath);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
                if (prefab == null)
                    throw new InvalidOperationException(
                        "Unity could not reload the generated Physics Preview receipt.");

                report.AssetPath = outputPath;
                report.RigidbodyCount = prefab.GetComponentsInChildren<Rigidbody>(true).Length;
                report.JointCount = prefab.GetComponentsInChildren<ConfigurableJoint>(true).Length;
                report.ColliderCount = prefab.GetComponentsInChildren<Collider>(true).Length;
                report.RendererCount = prefab
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                report.Fingerprint = Hash128.Compute(
                    ComputeAuthoringFingerprint(definition) + "|"
                    + report.RigidbodyCount + "|" + report.JointCount + "|"
                    + report.ColliderCount + "|" + report.RendererCount).ToString();

                int expectedRoleCount = requestedRoles.Length;
                report.Success = report.RigidbodyCount == expectedRoleCount
                                 && report.JointCount == expectedRoleCount
                                 && report.ColliderCount == expectedRoleCount
                                 && report.RendererCount
                                 == definition.AvatarSourceProfile.Renderers.Count;
                if (!report.Success)
                    report.Add(
                        $"The saved preview did not preserve the expected {expectedRoleCount}/{expectedRoleCount}/{expectedRoleCount} physics and source-derived renderer counts.");
            }
            catch (Exception exception)
            {
                report.Add(exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                if (generatedRoot != null) Object.DestroyImmediate(generatedRoot);
                if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
                string sourceHashAfter = AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
                if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal))
                {
                    report.Success = false;
                    report.Add("The source Avatar dependency hash changed while generating its preview.");
                }
            }
            return report;
        }

        public static string ComputeAuthoringFingerprint(NpcDefinition definition)
        {
            if (definition == null || definition.SourceAvatar == null
                                   || definition.AvatarSourceProfile == null
                                   || definition.AnatomyProfile == null)
                return string.Empty;
            string sourcePath = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            return Hash128.Compute(
                NpcToolkitVersion.Current + "|"
                + definition.SourceAssetGuid + "|"
                + AssetDatabase.GetAssetDependencyHash(sourcePath) + "|"
                + EditorJsonUtility.ToJson(
                    definition.AvatarSourceProfile, false) + "|"
                + EditorJsonUtility.ToJson(
                    definition.AnatomyProfile, false)
                + (definition.IncludePhysicalJaw
                    ? "|physical-jaw-v1"
                    : string.Empty)).ToString();
        }

        public static bool ReceiptMatches(
            NpcDefinition definition,
            string previewPath,
            out string detail)
        {
            detail = string.Empty;
            AssetImporter importer = string.IsNullOrWhiteSpace(previewPath)
                ? null
                : AssetImporter.GetAtPath(previewPath);
            string[] parts = (importer?.userData ?? string.Empty).Split('|');
            if (parts.Length != 3
                || !string.Equals(parts[0], ReceiptPrefix, StringComparison.Ordinal))
            {
                detail = "This Physics Preview predates the alignment receipt. "
                         + "Return to Step 3C and generate it again.";
                return false;
            }
            string expectedAuthoring = ComputeAuthoringFingerprint(definition);
            if (string.IsNullOrWhiteSpace(expectedAuthoring)
                || !string.Equals(
                    parts[1], expectedAuthoring, StringComparison.Ordinal))
            {
                detail = "The Avatar or Physics Alignment changed after this "
                         + "preview was generated. Return to Step 3C and refresh it.";
                return false;
            }
            string contentHash;
            try
            {
                contentHash = ComputePrefabContentHash(previewPath);
            }
            catch (Exception exception)
            {
                detail = "The Physics Preview file could not be verified: "
                         + exception.Message;
                return false;
            }
            if (!string.Equals(parts[2], contentHash, StringComparison.Ordinal))
            {
                detail = "The generated Physics Preview was edited after Step 3C. "
                         + "Regenerate it so the anatomy handoff is deterministic.";
                return false;
            }
            return true;
        }

        private static void WriteReceipt(
            NpcDefinition definition,
            string previewPath)
        {
            AssetImporter importer = AssetImporter.GetAtPath(previewPath);
            if (importer == null)
                throw new InvalidOperationException(
                    "Unity did not create an importer for the Physics Preview.");
            string authoring = ComputeAuthoringFingerprint(definition);
            string content = ComputePrefabContentHash(previewPath);
            importer.userData = ReceiptPrefix + "|" + authoring + "|" + content;
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(previewPath);
        }

        private static string ComputePrefabContentHash(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new InvalidOperationException(
                    "Could not resolve the Unity project root.");
            string absolutePath = Path.GetFullPath(
                Path.Combine(projectRoot, assetPath));
            byte[] bytes = File.ReadAllBytes(absolutePath);
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
        }

        public static string GetOutputPath(NpcDefinition definition)
        {
            if (definition == null || definition.BuildProfile == null
                                   || definition.SourceAvatar == null)
                return string.Empty;
            string folder = definition.BuildProfile.GeneratedAssetFolder.TrimEnd('/');
            string safeName = SafeAssetName(definition.SourceAvatar.name);
            return folder + "/" + safeName + PreviewSuffix;
        }

        private static void AddCollider(
            Transform body,
            NpcBodyRoleProfile profile)
        {
            var holder = new GameObject("PrimaryCollider");
            holder.layer = 12;
            holder.transform.SetParent(body, false);
            holder.transform.localPosition = profile.ColliderCenter;
            holder.transform.localRotation = profile.ColliderLocalRotation;
            holder.transform.localScale = Vector3.one;
            if (profile.ColliderShape == NpcColliderShape.Box)
            {
                BoxCollider collider = holder.AddComponent<BoxCollider>();
                collider.size = profile.ColliderSize;
            }
            else if (profile.ColliderShape == NpcColliderShape.Sphere)
            {
                SphereCollider collider = holder.AddComponent<SphereCollider>();
                collider.radius = profile.CapsuleRadius;
            }
            else
            {
                CapsuleCollider collider = holder.AddComponent<CapsuleCollider>();
                collider.direction = 1;
                collider.radius = profile.CapsuleRadius;
                collider.height = Mathf.Max(
                    profile.CapsuleHeight, profile.CapsuleRadius * 2f);
            }
        }

        private static HumanBodyBones[] RequestedRoles(NpcDefinition definition)
        {
            return definition != null && definition.IncludePhysicalJaw
                ? NpcHumanoidGraph.CanonicalRoles
                    .Concat(new[] { HumanBodyBones.Jaw })
                    .ToArray()
                : NpcHumanoidGraph.CanonicalRoles;
        }

        private static NpcBodyRoleProfile ProfileForRole(
            NpcAnatomyProfile anatomy,
            HumanBodyBones role)
        {
            return role == HumanBodyBones.Jaw
                ? anatomy?.OptionalJaw
                : anatomy?.FindRole(role);
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

        private static bool ValidateRequestedJaw(
            NpcDefinition definition,
            NpcPhysicsPreviewReport report)
        {
            if (definition?.AvatarSourceProfile == null
                || string.IsNullOrWhiteSpace(definition.AvatarSourceProfile.JawPath))
            {
                report.Add(
                    "Physical Jaw is requested, but the accepted Avatar snapshot has no mapped Jaw. Map a Humanoid Jaw or turn off Physical Jaw in Define NPC.");
                return false;
            }

            NpcBodyRoleProfile jaw = definition.AnatomyProfile?.OptionalJaw;
            if (jaw == null)
            {
                report.Add(
                    "Physical Jaw is requested, but the Anatomy Profile has no optional Jaw role. Recreate or repair the Anatomy Profile.");
                return false;
            }
            if (!jaw.Enabled)
            {
                report.Add(
                    "Physical Jaw is requested, but Jaw is disabled in Physics Alignment. Enable it or turn off Physical Jaw in Define NPC.");
                return false;
            }
            if (jaw.AlignmentState == NpcAlignmentState.Unseeded
                || jaw.ColliderShape != NpcColliderShape.Box
                || !PositiveFinite(jaw.ColliderSize))
            {
                report.Add(
                    "Physical Jaw is requested, but its lower-face box is not fitted. Run Create / Refresh Auto-Fit Baseline, then review Jaw in Physics Alignment.");
                return false;
            }
            return true;
        }

        private static bool PositiveFinite(Vector3 value)
        {
            return value.x > 0f && value.y > 0f && value.z > 0f
                   && !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                   && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                   && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static ConfigurableJointMotion Motion(NpcJointMotion value)
        {
            switch (value)
            {
                case NpcJointMotion.Free: return ConfigurableJointMotion.Free;
                case NpcJointMotion.Limited: return ConfigurableJointMotion.Limited;
                default: return ConfigurableJointMotion.Locked;
            }
        }

        private static Animator FindAnimator(Transform root, string path)
        {
            Transform animatorTransform = string.IsNullOrWhiteSpace(path)
                ? root
                : root.Find(path);
            return animatorTransform?.GetComponent<Animator>()
                   ?? root.GetComponentInChildren<Animator>(true);
        }

        private static Vector3 SafeDirection(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude < 0.000001f ? fallback : value.normalized;
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)
                || !folder.StartsWith("Assets", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The generated preview folder must be under Assets/.");
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = "Assets";
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static string SafeAssetName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid.ToString(), string.Empty);
            return string.IsNullOrWhiteSpace(value) ? "Character" : value.Trim();
        }
    }
}
