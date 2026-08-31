using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Movement;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcMovementAuthoringTests
    {
        private string temporaryFolder;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(temporaryFolder))
                AssetDatabase.DeleteAsset(temporaryFolder);
            temporaryFolder = null;
        }

        [Test]
        public void ResultContractsRequireDeterministicFingerprints()
        {
            Assert.That(
                NpcMovementAuthoringResult.Succeeded(string.Empty).Success,
                Is.False);
            Assert.That(
                NpcMovementAuthoringValidationResult.Current(string.Empty)
                    .IsCurrent,
                Is.False);
            Assert.That(
                NpcMovementAuthoringValidationResult.Current("recipe-v1")
                    .IsCurrent,
                Is.True);
        }

        [Test]
        public void RegistryRequiresOneAvailableExactContractProvider()
        {
            NpcBuildProfile build = CreateBuildProfile();
            try
            {
                var registry = new NpcMovementAuthoringProviderRegistry();
                registry.Register(new FirstProvider(
                    build.CompatibilityProfileId));

                NpcMovementAuthoringProviderSelection unique =
                    registry.Resolve(build);

                Assert.That(unique.Status, Is.EqualTo(
                    NpcMovementAuthoringProviderSelectionStatus.Available));
                Assert.That(unique.CanPrepare, Is.True);
                Assert.That(unique.Provider.ProviderId,
                    Is.EqualTo("movement.first"));

                registry.Register(new SecondProvider(
                    build.CompatibilityProfileId));
                NpcMovementAuthoringProviderSelection ambiguous =
                    registry.Resolve(build);

                Assert.That(ambiguous.Status, Is.EqualTo(
                    NpcMovementAuthoringProviderSelectionStatus.AmbiguousProvider));
                Assert.That(ambiguous.CanPrepare, Is.False);
                Assert.That(ambiguous.CandidateProviderIds,
                    Is.EquivalentTo(new[]
                    {
                        "movement.first",
                        "movement.second",
                    }));
            }
            finally
            {
                Object.DestroyImmediate(build);
            }
        }

        [Test]
        public void ExistingDefinitionCanCreateAndLinkMissingMovementProfile()
        {
            temporaryFolder = "Assets/MarrowNpcMovementTest_"
                              + Guid.NewGuid().ToString("N");
            string folderName = temporaryFolder.Substring(
                temporaryFolder.LastIndexOf('/') + 1);
            AssetDatabase.CreateFolder("Assets", folderName);
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            definition.name = "Existing Definition";
            string definitionPath = temporaryFolder + "/ExistingDefinition.asset";
            AssetDatabase.CreateAsset(definition, definitionPath);

            NpcMovementProfile movement =
                NpcMovementProfileFactory.CreateForDefinition(definition);

            Assert.That(movement, Is.Not.Null);
            Assert.That(definition.MovementProfile, Is.SameAs(movement));
            Assert.That(movement.AlignmentState,
                Is.EqualTo(NpcAlignmentState.Unseeded));
            Assert.That(AssetDatabase.GetAssetPath(movement),
                Does.StartWith(temporaryFolder + "/"));
            Assert.That(EditorUtility.IsPersistent(movement), Is.True);
        }

        [Test]
        public void LegLengthUsesBothHumanoidSegments()
        {
            var root = new GameObject("MovementMeasurement");
            var upper = new GameObject("Upper").transform;
            var lower = new GameObject("Lower").transform;
            var foot = new GameObject("Foot").transform;
            upper.SetParent(root.transform);
            lower.SetParent(root.transform);
            foot.SetParent(root.transform);
            upper.position = new Vector3(0f, 1f, 0f);
            lower.position = new Vector3(0f, 0.55f, 0f);
            foot.position = new Vector3(0f, 0.1f, 0.18f);
            try
            {
                float expected = Vector3.Distance(
                                     upper.position, lower.position)
                                 + Vector3.Distance(
                                     lower.position, foot.position);
                Assert.That(
                    NpcMovementProfileFitter.CalculateLegLength(
                        upper, lower, foot),
                    Is.EqualTo(expected).Within(0.000001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NavigationRadiusUsesHumanoidClearanceNotInnerTorsoCore()
        {
            Assert.That(
                NpcMovementProfileFitter.NavigationRadius(1.76f, 0.09f),
                Is.EqualTo(0.41f).Within(0.0001f));
            Assert.That(
                NpcMovementProfileFitter.NavigationRadius(1.76f, 0.5f),
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ToeLessFootUsesAcceptedSourceForwardInsteadOfBoneAxis()
        {
            var root = new GameObject("SourceRoot").transform;
            var foot = new GameObject("Foot").transform;
            foot.SetParent(root, false);
            root.rotation = Quaternion.Euler(0f, 37f, 0f);
            foot.localRotation = Quaternion.Euler(0f, 90f, 90f);
            try
            {
                Vector3 direction =
                    NpcMovementProfileFitter.CalculateFootForwardLocal(
                        root,
                        foot,
                        null,
                        root.up,
                        root.forward);

                Assert.That(
                    Vector3.Angle(direction, Vector3.forward),
                    Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [Test]
        public void MovementMeasurementInstanceUsesAnIsolatedPreviewScene()
        {
            temporaryFolder = "Assets/MarrowNpcMovementTest_"
                              + Guid.NewGuid().ToString("N");
            string folderName = temporaryFolder.Substring(
                temporaryFolder.LastIndexOf('/') + 1);
            AssetDatabase.CreateFolder("Assets", folderName);
            var sourceRoot = new GameObject("Movement Source");
            GameObject sourceAsset = PrefabUtility.SaveAsPrefabAsset(
                sourceRoot, temporaryFolder + "/MovementSource.prefab");
            Object.DestroyImmediate(sourceRoot);
            Scene activeScene = SceneManager.GetActiveScene();
            Scene previewScene = default;
            GameObject instance = null;
            try
            {
                instance = NpcMovementProfileFitter.InstantiateMeasurementSource(
                    sourceAsset, out previewScene);

                Assert.That(previewScene.IsValid(), Is.True);
                Assert.That(previewScene, Is.Not.EqualTo(activeScene));
                Assert.That(instance.scene, Is.EqualTo(previewScene));
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(activeScene));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (previewScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedMovementFitRestoresProfileAndOriginalDirtyState(
            bool originallyDirty)
        {
            var profile = ScriptableObject.CreateInstance<NpcMovementProfile>();
            try
            {
                profile.ResetToDefaults();
                profile.WalkSpeed = 3.25f;
                profile.StrideScale = 0.82f;
                if (originallyDirty)
                    EditorUtility.SetDirty(profile);
                else
                    EditorUtility.ClearDirty(profile);
                string before = EditorJsonUtility.ToJson(profile, false);

                profile.ResetToDefaults();
                profile.WalkSpeed = 9f;
                profile.StrideScale = 1.4f;
                EditorUtility.SetDirty(profile);

                NpcMovementProfileFitter.RestoreMovementProfileSnapshot(
                    profile, before, originallyDirty);

                Assert.That(
                    EditorJsonUtility.ToJson(profile, false),
                    Is.EqualTo(before));
                Assert.That(
                    EditorUtility.IsDirty(profile),
                    Is.EqualTo(originallyDirty));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RecipeValidationRejectsChangedProviderInputs()
        {
            NpcBuildProfile build = CreateBuildProfile();
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            var movement = ScriptableObject.CreateInstance<NpcMovementProfile>();
            var pose = ScriptableObject.CreateInstance<NpcBuildProfile>();
            var config = ScriptableObject.CreateInstance<NpcBuildProfile>();
            try
            {
                definition.Initialize(
                    null,
                    NpcAvatarSourceKind.HumanoidPrefab,
                    null,
                    null,
                    build,
                    string.Empty,
                    string.Empty,
                    null,
                    movement);
                movement.SetProviderRecipe(pose, config, "stored-recipe");
                var registry = new NpcMovementAuthoringProviderRegistry();
                registry.Register(new DriftedProvider(
                    build.CompatibilityProfileId));

                NpcMovementRecipeValidationReport report =
                    NpcMovementRecipeValidator.Validate(
                        definition, movement, registry);

                Assert.That(report.IsCurrent, Is.False);
                Assert.That(report.Detail, Does.Contain("changed"));
            }
            finally
            {
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(pose);
                Object.DestroyImmediate(movement);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(build);
            }
        }

        [Test]
        public void RecipeValidationRejectsAndRestoresProviderMutation()
        {
            NpcBuildProfile build = CreateBuildProfile();
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            var movement = ScriptableObject.CreateInstance<NpcMovementProfile>();
            var pose = ScriptableObject.CreateInstance<NpcBuildProfile>();
            var config = ScriptableObject.CreateInstance<NpcBuildProfile>();
            try
            {
                definition.Initialize(
                    null,
                    NpcAvatarSourceKind.HumanoidPrefab,
                    null,
                    null,
                    build,
                    string.Empty,
                    string.Empty,
                    null,
                    movement);
                movement.SetProviderRecipe(pose, config, "stored-recipe");
                float originalWalkSpeed = movement.WalkSpeed;
                bool dirtyBefore = EditorUtility.IsDirty(movement);
                var registry = new NpcMovementAuthoringProviderRegistry();
                registry.Register(new MutatingProvider(
                    build.CompatibilityProfileId));

                NpcMovementRecipeValidationReport report =
                    NpcMovementRecipeValidator.Validate(
                        definition, movement, registry);

                Assert.That(report.IsCurrent, Is.False);
                Assert.That(report.Detail, Does.Contain("read-only"));
                Assert.That(movement.WalkSpeed, Is.EqualTo(originalWalkSpeed));
                Assert.That(EditorUtility.IsDirty(movement),
                    Is.EqualTo(dirtyBefore));
            }
            finally
            {
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(pose);
                Object.DestroyImmediate(movement);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(build);
            }
        }

        [Test]
        public void RecipeValidationRestoresPersistentlySavedProviderMutation()
        {
            temporaryFolder = "Assets/MarrowNpcMovementTest_"
                              + Guid.NewGuid().ToString("N");
            string folderName = temporaryFolder.Substring(
                temporaryFolder.LastIndexOf('/') + 1);
            AssetDatabase.CreateFolder("Assets", folderName);
            string definitionPath = temporaryFolder + "/Definition.asset";
            string movementPath = temporaryFolder + "/Movement.asset";
            var build = CreateBuildProfile();
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            var movement = ScriptableObject.CreateInstance<NpcMovementProfile>();
            var pose = ScriptableObject.CreateInstance<NpcBuildProfile>();
            var config = ScriptableObject.CreateInstance<NpcBuildProfile>();
            AssetDatabase.CreateAsset(build, temporaryFolder + "/Build.asset");
            AssetDatabase.CreateAsset(pose, temporaryFolder + "/Pose.asset");
            AssetDatabase.CreateAsset(config, temporaryFolder + "/Config.asset");
            AssetDatabase.CreateAsset(movement, movementPath);
            AssetDatabase.CreateAsset(definition, definitionPath);
            movement.ResetToDefaults();
            movement.WalkSpeed = 2.25f;
            movement.SetProviderRecipe(pose, config, "stored-recipe");
            definition.Initialize(
                null,
                NpcAvatarSourceKind.HumanoidPrefab,
                null,
                null,
                build,
                string.Empty,
                string.Empty,
                null,
                movement);
            EditorUtility.SetDirty(movement);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            string guidBefore = AssetDatabase.AssetPathToGUID(movementPath);
            byte[] bytesBefore = File.ReadAllBytes(AbsoluteAssetPath(movementPath));
            byte[] metaBefore = File.ReadAllBytes(
                AbsoluteAssetPath(movementPath) + ".meta");
            var registry = new NpcMovementAuthoringProviderRegistry();
            registry.Register(new PersistentlyMutatingProvider(
                build.CompatibilityProfileId));

            NpcMovementRecipeValidationReport report =
                NpcMovementRecipeValidator.Validate(
                    definition, movement, registry);

            Assert.That(report.IsCurrent, Is.False);
            Assert.That(report.Detail, Does.Contain("read-only"));
            Assert.That(movement.WalkSpeed, Is.EqualTo(2.25f));
            Assert.That(EditorUtility.IsDirty(movement), Is.False);
            Assert.That(File.ReadAllBytes(AbsoluteAssetPath(movementPath)),
                Is.EqualTo(bytesBefore));
            Assert.That(File.ReadAllBytes(AbsoluteAssetPath(movementPath) + ".meta"),
                Is.EqualTo(metaBefore));
            Assert.That(AssetDatabase.AssetPathToGUID(movementPath),
                Is.EqualTo(guidBefore));
            AssetDatabase.ImportAsset(
                movementPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            NpcMovementProfile reloaded =
                AssetDatabase.LoadAssetAtPath<NpcMovementProfile>(movementPath);
            Assert.That(reloaded.WalkSpeed, Is.EqualTo(2.25f));
            Assert.That(reloaded.ProviderRecipeFingerprint,
                Is.EqualTo("stored-recipe"));
        }

        private static string AbsoluteAssetPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                assetPath));
        }

        private static NpcBuildProfile CreateBuildProfile()
        {
            var profile = ScriptableObject.CreateInstance<NpcBuildProfile>();
            profile.Initialize("Tester", "Example", "Assets/Example");
            return profile;
        }

        private abstract class ProviderBase : INpcMovementAuthoringProvider
        {
            public abstract string ProviderId { get; }
            public string DisplayName => ProviderId;
            public string CompatibilityProfileId { get; }

            protected ProviderBase(string compatibilityProfileId)
            {
                CompatibilityProfileId = compatibilityProfileId;
            }

            public NpcCompatibilityProbeResult Probe()
            {
                return NpcCompatibilityProbeResult.Available(
                    NpcCompatibilityCapabilities.All);
            }

            public NpcMovementAuthoringResult Prepare(
                NpcDefinition definition,
                NpcMovementProfile profile)
            {
                return NpcMovementAuthoringResult.Failed("Not used by this test.");
            }

            public virtual NpcMovementAuthoringValidationResult Validate(
                NpcDefinition definition,
                NpcMovementProfile profile)
            {
                return NpcMovementAuthoringValidationResult.Stale(
                    "Not used by this test.");
            }
        }

        private sealed class FirstProvider : ProviderBase
        {
            public override string ProviderId => "movement.first";

            public FirstProvider(string compatibilityProfileId)
                : base(compatibilityProfileId)
            {
            }
        }

        private sealed class SecondProvider : ProviderBase
        {
            public override string ProviderId => "movement.second";

            public SecondProvider(string compatibilityProfileId)
                : base(compatibilityProfileId)
            {
            }
        }

        private sealed class DriftedProvider : ProviderBase
        {
            public override string ProviderId => "movement.drifted";

            public DriftedProvider(string compatibilityProfileId)
                : base(compatibilityProfileId)
            {
            }

            public override NpcMovementAuthoringValidationResult Validate(
                NpcDefinition definition,
                NpcMovementProfile profile)
            {
                return NpcMovementAuthoringValidationResult.Current(
                    "changed-recipe");
            }
        }

        private sealed class MutatingProvider : ProviderBase
        {
            public override string ProviderId => "movement.mutating";

            public MutatingProvider(string compatibilityProfileId)
                : base(compatibilityProfileId)
            {
            }

            public override NpcMovementAuthoringValidationResult Validate(
                NpcDefinition definition,
                NpcMovementProfile profile)
            {
                profile.WalkSpeed = 9f;
                EditorUtility.SetDirty(profile);
                return NpcMovementAuthoringValidationResult.Current(
                    profile.ProviderRecipeFingerprint);
            }
        }

        private sealed class PersistentlyMutatingProvider : ProviderBase
        {
            public override string ProviderId => "movement.persistently-mutating";

            public PersistentlyMutatingProvider(string compatibilityProfileId)
                : base(compatibilityProfileId)
            {
            }

            public override NpcMovementAuthoringValidationResult Validate(
                NpcDefinition definition,
                NpcMovementProfile profile)
            {
                profile.WalkSpeed = 9f;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                return NpcMovementAuthoringValidationResult.Current(
                    profile.ProviderRecipeFingerprint);
            }
        }
    }
}
