using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;
using Vergil333.MarrowNpcToolkit.Editor.AvatarIntake;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Validation;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor.Diagnostics
{
    public static class NpcToolkitCommandLine
    {
        [Serializable]
        private sealed class AvatarSmokeResult
        {
            public bool ok;
            public string inputAsset;
            public string resolvedAvatar;
            public string provider;
            public string providerVersion;
            public bool usedAvatarCrate;
            public int humanoidBoneCount;
            public int rendererCount;
            public int optionalBoneCount;
            public string[] issues;
        }

        [Serializable]
        private sealed class BaselineBodyResult
        {
            public string role;
            public string state;
            public string shape;
            public Vector3 center;
            public Vector3 size;
            public float radius;
            public float height;
            public float mass;
        }

        [Serializable]
        private sealed class BaselineSmokeResult
        {
            public bool ok;
            public string inputAsset;
            public string resolvedAvatar;
            public bool includePhysicalJaw;
            public bool jawMapped;
            public int expectedRoleCount;
            public bool usedAvatarCrate;
            public int humanoidBoneCount;
            public int rendererCount;
            public int fittedRoleCount;
            public float eyeHeightMeters;
            public string routeFingerprint;
            public string baselineFingerprint;
            public string repeatedFingerprint;
            public bool deterministic;
            public bool sourceUnmodified;
            public string sourceHashBefore;
            public string sourceHashAfter;
            public BaselineBodyResult[] bodies;
            public string[] issues;
        }

        [Serializable]
        private sealed class PhysicsPreviewSmokeResult
        {
            public bool ok;
            public string inputAsset;
            public string resolvedAvatar;
            public bool includePhysicalJaw;
            public bool jawMapped;
            public int expectedRoleCount;
            public string previewAsset;
            public string firstFingerprint;
            public string repeatedFingerprint;
            public bool deterministic;
            public bool hasSiblingRoots;
            public bool physicsContainsOnlyUnityComponents;
            public int rigidbodyCount;
            public int jointCount;
            public int colliderCount;
            public int rendererCount;
            public bool sourceUnmodified;
            public bool cleanupOk;
            public string[] issues;
        }

        [Serializable]
        private sealed class ReadinessIssueSmokeResult
        {
            public string severity;
            public string code;
            public string role;
            public string message;
        }

        [Serializable]
        private sealed class ReadinessSmokeResult
        {
            public bool ok;
            public string definitionAsset;
            public bool physicsReady;
            public bool nativeProviderAvailable;
            public bool requestedCapabilitiesReady;
            public bool readyForNativeHandoff;
            public int reviewedRoleCount;
            public int expectedRoleCount;
            public int rigidbodyCount;
            public int colliderCount;
            public int jointCount;
            public int rendererCount;
            public string providerStatus;
            public string providerId;
            public string providerDetail;
            public string requestedCapabilities;
            public string availableCapabilities;
            public string firstFingerprint;
            public string repeatedFingerprint;
            public bool deterministic;
            public bool compatibilityDeterministic;
            public bool assetsUnmodified;
            public string assetFingerprintBefore;
            public string assetFingerprintAfter;
            public ReadinessIssueSmokeResult[] issues;
        }

        public static void SmokeTestAvatar()
        {
            string sourcePath = ReadArgument("-npcToolkitSource");
            string resultPath = ReadArgument("-npcToolkitResult");
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Missing -npcToolkitSource <Project asset path>.");
            if (string.IsNullOrWhiteSpace(resultPath))
                throw new ArgumentException("Missing -npcToolkitResult <absolute JSON path>.");

            Object input = AssetDatabase.LoadMainAssetAtPath(sourcePath);
            AvatarIntakeReport report = AvatarIntakeValidator.Validate(input);
            NpcSdkEnvironment environment = NpcSdkEnvironmentProbe.Probe();
            var result = new AvatarSmokeResult
            {
                ok = report.ReadyForNpcDefinition,
                inputAsset = sourcePath,
                resolvedAvatar = report.Source == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(report.Source),
                provider = environment.DisplayName,
                providerVersion = environment.PackageVersion,
                usedAvatarCrate = report.IsAvatarCrate,
                rendererCount = report.RendererCount,
                issues = report.Issues.Select(value =>
                    value.Severity + ": " + value.Message).ToArray(),
            };

            if (result.ok)
            {
                var snapshot = ScriptableObject.CreateInstance<NpcAvatarSourceProfile>();
                try
                {
                    MarrowAvatarSnapshotService.Capture(report.Source, snapshot);
                    result.humanoidBoneCount = snapshot.HumanoidBones.Count;
                    result.rendererCount = snapshot.Renderers.Count;
                    result.optionalBoneCount = snapshot.OptionalBones.Count;
                }
                finally
                {
                    Object.DestroyImmediate(snapshot);
                }
            }

            string directory = Path.GetDirectoryName(resultPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
            Debug.Log($"Marrow NPC Toolkit Avatar smoke test: {(result.ok ? "PASS" : "FAIL")}\n{resultPath}");
            if (!result.ok)
                throw new InvalidOperationException(
                    "Avatar smoke test failed. See the JSON result for structured intake issues.");
        }

        public static void SmokeTestBaseline()
        {
            string sourcePath = ReadArgument("-npcToolkitSource");
            string resultPath = ReadArgument("-npcToolkitResult");
            bool includePhysicalJaw = ReadBooleanArgument(
                "-npcToolkitIncludeJaw", false);
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Missing -npcToolkitSource <Project asset path>.");
            if (string.IsNullOrWhiteSpace(resultPath))
                throw new ArgumentException("Missing -npcToolkitResult <absolute JSON path>.");

            var output = new BaselineSmokeResult
            {
                inputAsset = sourcePath,
                includePhysicalJaw = includePhysicalJaw,
                expectedRoleCount = includePhysicalJaw ? 17 : 16,
                issues = Array.Empty<string>(),
                bodies = Array.Empty<BaselineBodyResult>(),
            };
            NpcAvatarSourceProfile snapshot = null;
            NpcAnatomyProfile anatomy = null;
            NpcDefinition definition = null;
            NpcAnatomyProfile repeatAnatomy = null;
            NpcDefinition repeatDefinition = null;
            try
            {
                Object input = AssetDatabase.LoadMainAssetAtPath(sourcePath);
                AvatarIntakeReport intake = AvatarIntakeValidator.Validate(input);
                output.usedAvatarCrate = intake.IsAvatarCrate;
                output.resolvedAvatar = intake.Source == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(intake.Source);
                if (!intake.ReadyForNpcDefinition)
                {
                    output.issues = intake.Issues.Select(value =>
                        value.Severity + ": " + value.Message).ToArray();
                    return;
                }

                output.sourceHashBefore = AssetDatabase
                    .GetAssetDependencyHash(output.resolvedAvatar).ToString();
                snapshot = ScriptableObject.CreateInstance<NpcAvatarSourceProfile>();
                MarrowAvatarSnapshotService.Capture(intake.Source, snapshot);
                output.humanoidBoneCount = snapshot.HumanoidBones.Count;
                output.rendererCount = snapshot.Renderers.Count;
                output.jawMapped = !string.IsNullOrWhiteSpace(snapshot.JawPath);
                output.routeFingerprint = RouteFingerprint(snapshot);

                anatomy = ScriptableObject.CreateInstance<NpcAnatomyProfile>();
                anatomy.ResetToHumanoidDefaults();
                definition = CreateTransientDefinition(
                    intake.Source,
                    snapshot,
                    anatomy,
                    includePhysicalJaw: includePhysicalJaw);
                NpcBaselineFitReport fit = NpcBaselineFitter.Fit(
                    definition, overwriteReviewed: true, registerUndo: false);

                repeatAnatomy = ScriptableObject.CreateInstance<NpcAnatomyProfile>();
                repeatAnatomy.ResetToHumanoidDefaults();
                repeatDefinition = CreateTransientDefinition(
                    intake.Source,
                    snapshot,
                    repeatAnatomy,
                    includePhysicalJaw: includePhysicalJaw);
                NpcBaselineFitReport repeat = NpcBaselineFitter.Fit(
                    repeatDefinition, overwriteReviewed: true, registerUndo: false);

                output.fittedRoleCount = fit.FittedRoleCount;
                output.eyeHeightMeters = fit.EyeHeightMeters;
                output.baselineFingerprint = fit.Fingerprint;
                output.repeatedFingerprint = repeat.Fingerprint;
                output.deterministic = fit.Success
                                       && repeat.Success
                                       && string.Equals(
                                           fit.Fingerprint,
                                           repeat.Fingerprint,
                                           StringComparison.Ordinal);
                HumanBodyBones[] smokeRoles = includePhysicalJaw
                    ? NpcHumanoidGraph.CanonicalRoles
                        .Concat(new[] { HumanBodyBones.Jaw })
                        .ToArray()
                    : NpcHumanoidGraph.CanonicalRoles;
                output.bodies = smokeRoles.Select(role =>
                {
                    NpcBodyRoleProfile value = role == HumanBodyBones.Jaw
                        ? anatomy.OptionalJaw
                        : anatomy.FindRole(role);
                    return new BaselineBodyResult
                    {
                        role = role.ToString(),
                        state = value?.AlignmentState.ToString() ?? "Missing",
                        shape = value?.ColliderShape.ToString() ?? "Missing",
                        center = value?.ColliderCenter ?? Vector3.zero,
                        size = value?.ColliderSize ?? Vector3.zero,
                        radius = value?.CapsuleRadius ?? 0f,
                        height = value?.CapsuleHeight ?? 0f,
                        mass = value?.MassKilograms ?? 0f,
                    };
                }).ToArray();
                output.issues = fit.Issues.Concat(repeat.Issues).ToArray();
                output.ok = fit.Success
                            && repeat.Success
                            && output.fittedRoleCount == output.expectedRoleCount
                            && output.deterministic;
            }
            catch (Exception exception)
            {
                output.ok = false;
                output.issues = output.issues.Concat(new[] { exception.ToString() }).ToArray();
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(output.resolvedAvatar))
                {
                    output.sourceHashAfter = AssetDatabase
                        .GetAssetDependencyHash(output.resolvedAvatar).ToString();
                    output.sourceUnmodified = string.Equals(
                        output.sourceHashBefore,
                        output.sourceHashAfter,
                        StringComparison.Ordinal);
                    output.ok &= output.sourceUnmodified;
                }
                if (repeatDefinition != null) Object.DestroyImmediate(repeatDefinition);
                if (repeatAnatomy != null) Object.DestroyImmediate(repeatAnatomy);
                if (definition != null) Object.DestroyImmediate(definition);
                if (anatomy != null) Object.DestroyImmediate(anatomy);
                if (snapshot != null) Object.DestroyImmediate(snapshot);

                string directory = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(resultPath, JsonUtility.ToJson(output, true));
                Debug.Log($"Marrow NPC Toolkit baseline smoke test: {(output.ok ? "PASS" : "FAIL")}\n{resultPath}");
            }

            if (!output.ok)
                throw new InvalidOperationException(
                    "Baseline smoke test failed. Parse the JSON 'ok' field and issues for the result.");
        }

        public static void SmokeTestPhysicsPreview()
        {
            string sourcePath = ReadArgument("-npcToolkitSource");
            string resultPath = ReadArgument("-npcToolkitResult");
            string previewPath = ReadArgument("-npcToolkitPreviewAsset");
            bool includePhysicalJaw = ReadBooleanArgument(
                "-npcToolkitIncludeJaw", false);
            RunPhysicsPreviewSmoke(
                sourcePath,
                resultPath,
                previewPath,
                includePhysicalJaw);
        }

        public static void RunPhysicsPreviewSmoke(
            string sourcePath,
            string resultPath,
            string previewPath,
            bool includePhysicalJaw)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(resultPath)
                || string.IsNullOrWhiteSpace(previewPath))
                throw new ArgumentException(
                    "Physics preview smoke requires source, absolute result, and preview asset arguments.");
            previewPath = previewPath.Replace('\\', '/');
            string previewFolder = Path.GetDirectoryName(previewPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(previewFolder)
                || !previewFolder.StartsWith(
                    "Assets/__MarrowNpcToolkitVerification_", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Verification previews are restricted to a unique Assets/__MarrowNpcToolkitVerification_* folder.");

            var output = new PhysicsPreviewSmokeResult
            {
                inputAsset = sourcePath,
                includePhysicalJaw = includePhysicalJaw,
                expectedRoleCount = includePhysicalJaw ? 17 : 16,
                previewAsset = previewPath,
                issues = Array.Empty<string>(),
            };
            bool folderExisted = AssetDatabase.IsValidFolder(previewFolder);
            NpcAvatarSourceProfile snapshot = null;
            NpcAnatomyProfile anatomy = null;
            NpcBuildProfile build = null;
            NpcDefinition definition = null;
            string sourceHashBefore = string.Empty;
            try
            {
                if (folderExisted || AssetDatabase.LoadAssetAtPath<Object>(previewPath) != null)
                    throw new InvalidOperationException(
                        "The unique verification output already exists; refusing to overwrite it.");

                Object input = AssetDatabase.LoadMainAssetAtPath(sourcePath);
                AvatarIntakeReport intake = AvatarIntakeValidator.Validate(input);
                if (!intake.ReadyForNpcDefinition)
                    throw new InvalidOperationException(string.Join("\n", intake.Issues.Select(
                        value => value.Severity + ": " + value.Message)));
                output.resolvedAvatar = AssetDatabase.GetAssetPath(intake.Source);
                sourceHashBefore = AssetDatabase.GetAssetDependencyHash(
                    output.resolvedAvatar).ToString();

                snapshot = ScriptableObject.CreateInstance<NpcAvatarSourceProfile>();
                MarrowAvatarSnapshotService.Capture(intake.Source, snapshot);
                output.jawMapped = !string.IsNullOrWhiteSpace(snapshot.JawPath);
                anatomy = ScriptableObject.CreateInstance<NpcAnatomyProfile>();
                anatomy.ResetToHumanoidDefaults();
                build = ScriptableObject.CreateInstance<NpcBuildProfile>();
                build.Initialize("Verification", intake.Source.name, previewFolder);
                definition = CreateTransientDefinition(
                    intake.Source,
                    snapshot,
                    anatomy,
                    build,
                    includePhysicalJaw);

                NpcBaselineFitReport fit = NpcBaselineFitter.Fit(
                    definition, overwriteReviewed: true, registerUndo: false);
                if (!fit.Success)
                    throw new InvalidOperationException(string.Join("\n", fit.Issues));

                NpcPhysicsPreviewReport first = NpcPhysicsPreviewBuilder.Build(
                    definition, previewPath);
                NpcPhysicsPreviewReport repeated = NpcPhysicsPreviewBuilder.Build(
                    definition, previewPath);
                output.firstFingerprint = first.Fingerprint;
                output.repeatedFingerprint = repeated.Fingerprint;
                output.deterministic = first.Success
                                       && repeated.Success
                                       && string.Equals(
                                           first.Fingerprint,
                                           repeated.Fingerprint,
                                           StringComparison.Ordinal);
                output.rigidbodyCount = repeated.RigidbodyCount;
                output.jointCount = repeated.JointCount;
                output.colliderCount = repeated.ColliderCount;
                output.rendererCount = repeated.RendererCount;
                output.issues = first.Issues.Concat(repeated.Issues).ToArray();

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(previewPath);
                Transform animationRoot = prefab?.transform.Find("AnimationRoot");
                Transform physicsRoot = prefab?.transform.Find("Physics");
                output.hasSiblingRoots = animationRoot != null
                                         && physicsRoot != null
                                         && animationRoot.parent == prefab.transform
                                         && physicsRoot.parent == prefab.transform
                                         && !animationRoot.IsChildOf(physicsRoot)
                                         && !physicsRoot.IsChildOf(animationRoot);
                output.physicsContainsOnlyUnityComponents = physicsRoot != null
                    && physicsRoot.GetComponentsInChildren<Component>(true).All(component =>
                        component is Transform
                        || component is Rigidbody
                        || component is Collider
                        || component is ConfigurableJoint);
                output.ok = output.deterministic
                            && output.hasSiblingRoots
                            && output.physicsContainsOnlyUnityComponents
                            && output.rigidbodyCount == output.expectedRoleCount
                            && output.jointCount == output.expectedRoleCount
                            && output.colliderCount == output.expectedRoleCount
                            && output.rendererCount == snapshot.Renderers.Count;
            }
            catch (Exception exception)
            {
                output.ok = false;
                output.issues = output.issues.Concat(new[] { exception.ToString() }).ToArray();
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(output.resolvedAvatar))
                    output.sourceUnmodified = string.Equals(
                        sourceHashBefore,
                        AssetDatabase.GetAssetDependencyHash(
                            output.resolvedAvatar).ToString(),
                        StringComparison.Ordinal);
                bool previewRemoved = folderExisted
                                      || AssetDatabase.LoadAssetAtPath<Object>(previewPath) == null
                                      || AssetDatabase.DeleteAsset(previewPath);
                bool folderRemoved = folderExisted
                                     || !AssetDatabase.IsValidFolder(previewFolder)
                                     || AssetDatabase.DeleteAsset(previewFolder);
                output.cleanupOk = previewRemoved && folderRemoved;
                output.ok &= output.sourceUnmodified && output.cleanupOk;

                if (definition != null) Object.DestroyImmediate(definition);
                if (build != null) Object.DestroyImmediate(build);
                if (anatomy != null) Object.DestroyImmediate(anatomy);
                if (snapshot != null) Object.DestroyImmediate(snapshot);
                string directory = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(resultPath, JsonUtility.ToJson(output, true));
                Debug.Log($"Marrow NPC Toolkit physics preview smoke test: {(output.ok ? "PASS" : "FAIL")}\n{resultPath}");
            }

            if (!output.ok)
                throw new InvalidOperationException(
                    "Physics preview smoke test failed. Parse the structured JSON result.");
        }

        public static void SmokeTestReadiness()
        {
            string definitionPath = ReadArgument("-npcToolkitDefinition");
            string resultPath = ReadArgument("-npcToolkitResult");
            if (string.IsNullOrWhiteSpace(definitionPath))
                throw new ArgumentException(
                    "Missing -npcToolkitDefinition <Project asset path>.");
            if (string.IsNullOrWhiteSpace(resultPath))
                throw new ArgumentException(
                    "Missing -npcToolkitResult <absolute JSON path>.");

            var output = new ReadinessSmokeResult
            {
                definitionAsset = definitionPath,
                issues = Array.Empty<ReadinessIssueSmokeResult>(),
            };
            try
            {
                NpcDefinition definition = AssetDatabase.LoadAssetAtPath<NpcDefinition>(
                    definitionPath);
                if (definition == null)
                    throw new InvalidOperationException(
                        "The supplied asset is not an NPC Definition.");

                output.assetFingerprintBefore = ReadinessAssetFingerprint(definition);
                NpcBuildReadinessReport first = NpcBuildReadinessDoctor.Validate(definition);
                NpcBuildReadinessReport repeated = NpcBuildReadinessDoctor.Validate(definition);
                NpcCompatibilityReport firstCompatibility =
                    NpcCompatibilityProbeRegistry.Default.Evaluate(definition.BuildProfile);
                NpcCompatibilityReport repeatedCompatibility =
                    NpcCompatibilityProbeRegistry.Default.Evaluate(definition.BuildProfile);
                NpcCompatibilityCapabilities required = RequiredCapabilities(definition);

                output.physicsReady = repeated.ReadyForBuild;
                output.nativeProviderAvailable =
                    repeatedCompatibility.NativeNpcProviderAvailable;
                output.requestedCapabilitiesReady =
                    repeatedCompatibility.Supports(required);
                output.readyForNativeHandoff = output.physicsReady
                                               && output.nativeProviderAvailable
                                               && output.requestedCapabilitiesReady;
                output.reviewedRoleCount = repeated.ReviewedRoleCount;
                output.expectedRoleCount = repeated.ExpectedRoleCount;
                output.rigidbodyCount = repeated.RigidbodyCount;
                output.colliderCount = repeated.ColliderCount;
                output.jointCount = repeated.JointCount;
                output.rendererCount = repeated.RendererCount;
                output.providerStatus = repeatedCompatibility.NativeProviderStatus.ToString();
                output.providerId = repeatedCompatibility.ProviderId;
                output.providerDetail = repeatedCompatibility.Detail;
                output.requestedCapabilities = required.ToString();
                output.availableCapabilities = repeatedCompatibility.Capabilities.ToString();
                output.firstFingerprint = first.Fingerprint;
                output.repeatedFingerprint = repeated.Fingerprint;
                output.deterministic = string.Equals(
                    first.Fingerprint,
                    repeated.Fingerprint,
                    StringComparison.Ordinal);
                output.compatibilityDeterministic =
                    firstCompatibility.NativeProviderStatus
                    == repeatedCompatibility.NativeProviderStatus
                    && firstCompatibility.Capabilities
                    == repeatedCompatibility.Capabilities
                    && string.Equals(
                        firstCompatibility.ProviderId,
                        repeatedCompatibility.ProviderId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        firstCompatibility.Detail,
                        repeatedCompatibility.Detail,
                        StringComparison.Ordinal);
                output.issues = repeated.Issues.Select(issue =>
                    new ReadinessIssueSmokeResult
                    {
                        severity = issue.Severity.ToString(),
                        code = issue.Code,
                        role = issue.Role?.ToString() ?? string.Empty,
                        message = issue.Message,
                    }).ToArray();
                output.assetFingerprintAfter = ReadinessAssetFingerprint(definition);
                output.assetsUnmodified = string.Equals(
                    output.assetFingerprintBefore,
                    output.assetFingerprintAfter,
                    StringComparison.Ordinal);
                output.ok = output.deterministic
                            && output.compatibilityDeterministic
                            && output.assetsUnmodified;
            }
            catch (Exception exception)
            {
                output.ok = false;
                output.issues = output.issues.Concat(new[]
                {
                    new ReadinessIssueSmokeResult
                    {
                        severity = "Error",
                        code = "READINESS_SMOKE_FAILED",
                        message = exception.ToString(),
                    },
                }).ToArray();
            }
            finally
            {
                string directory = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(resultPath, JsonUtility.ToJson(output, true));
                Debug.Log($"Marrow NPC Toolkit readiness smoke test: "
                          + $"{(output.ok ? "PASS" : "FAIL")}\n{resultPath}");
            }

            if (!output.ok)
                throw new InvalidOperationException(
                    "Readiness smoke test failed. Parse the structured JSON result.");
        }

        private static NpcDefinition CreateTransientDefinition(
            GameObject avatar,
            NpcAvatarSourceProfile snapshot,
            NpcAnatomyProfile anatomy,
            NpcBuildProfile build = null,
            bool includePhysicalJaw = false)
        {
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            string path = AssetDatabase.GetAssetPath(avatar);
            definition.Initialize(
                avatar,
                NpcAvatarSourceKind.MarrowAvatarPrefab,
                snapshot,
                anatomy,
                build,
                AssetDatabase.AssetPathToGUID(path),
                AssetDatabase.GetAssetDependencyHash(path).ToString());
            definition.IncludePhysicalJaw = includePhysicalJaw;
            return definition;
        }

        private static string RouteFingerprint(NpcAvatarSourceProfile snapshot)
        {
            string bones = string.Join(";", snapshot.HumanoidBones
                .OrderBy(value => (int)value.Role)
                .Select(value => value.Role + "=" + value.TransformPath));
            string renderers = string.Join(";", snapshot.Renderers
                .OrderBy(value => value.TransformPath, StringComparer.Ordinal)
                .Select(value => value.Category + "=" + value.TransformPath));
            string optional = string.Join(";", snapshot.OptionalBones
                .OrderBy(value => (int)value.Role)
                .Select(value => value.Role + "=" + value.TransformPath));
            return Hash128.Compute(
                bones + "|" + renderers + "|" + optional
                + "|eyes=" + snapshot.EyeCenterOverridePath
                + "|jaw=" + snapshot.JawPath).ToString();
        }

        private static NpcCompatibilityCapabilities RequiredCapabilities(
            NpcDefinition definition)
        {
            return NpcCompatibilityRequirements.ForDefinition(definition);
        }

        private static string ReadinessAssetFingerprint(NpcDefinition definition)
        {
            Object[] assets =
            {
                definition,
                definition?.SourceAvatar,
                definition?.AvatarSourceProfile,
                definition?.AnatomyProfile,
                definition?.BuildProfile,
                definition?.AudioProfile,
                definition == null
                    ? null
                    : AssetDatabase.LoadAssetAtPath<GameObject>(
                        NpcPhysicsPreviewBuilder.GetOutputPath(definition)),
            };
            string receipt = string.Join("|", assets
                .Where(asset => asset != null)
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => path + "="
                    + AssetDatabase.GetAssetDependencyHash(path)));
            return Hash128.Compute(receipt).ToString();
        }

        private static string ReadArgument(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < args.Length; index++)
            {
                if (string.Equals(args[index], key, StringComparison.Ordinal))
                    return args[index + 1];
            }
            return string.Empty;
        }

        private static bool ReadBooleanArgument(string key, bool defaultValue)
        {
            string value = ReadArgument(key);
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            if (bool.TryParse(value, out bool result)) return result;
            throw new ArgumentException(
                key + " must be followed by true or false, but was '" + value + "'.");
        }
    }
}
