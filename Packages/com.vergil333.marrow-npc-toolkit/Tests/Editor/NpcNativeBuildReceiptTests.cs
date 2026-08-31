using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Build;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Validation;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcNativeBuildReceiptTests
    {
        private string testFolder;

        [SetUp]
        public void SetUp()
        {
            testFolder = "Assets/__NpcNativeBuildReceiptTests_"
                         + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", testFolder.Substring(
                "Assets/".Length));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(testFolder)
                && AssetDatabase.IsValidFolder(testFolder))
                AssetDatabase.DeleteAsset(testFolder);
        }

        [Test]
        public void ReceiptPathIsAStableSidecarBesidePrefab()
        {
            Assert.That(
                NpcNativeBuildReceiptUtility.GetReceiptPath(
                    "Assets/Generated/EveNpc.prefab"),
                Is.EqualTo(
                    "Assets/Generated/EveNpc.NativeBuildReceipt.asset"));
            Assert.That(
                NpcNativeBuildReceiptUtility.GetReceiptPath(
                    "Packages/example/EveNpc.prefab"),
                Is.Empty);
            Assert.That(
                NpcNativeBuildReceiptUtility.GetReceiptPath(
                    "Assets/Generated/EveNpc.asset"),
                Is.Empty);
        }

        [Test]
        public void CommitCreatesCompleteReceiptAndValidationIsReadOnly()
        {
            NpcDefinition definition = CreateDefinition();
            string stagedPath = CreatePrefab("Pass1.prefab", "First");
            string outputPath = testFolder + "/GeneratedNpc.prefab";

            CommitResult commit = Commit(
                stagedPath,
                outputPath,
                definition,
                "definition-fingerprint",
                "input-fingerprint",
                "test.provider",
                NpcCompatibilityCapabilities.CoreAnatomy |
                NpcCompatibilityCapabilities.AI,
                "provider-fingerprint",
                "output-fingerprint");

            Assert.That(commit.Success, Is.True, commit.Detail);
            NpcNativeBuildReceipt receipt =
                NpcNativeBuildReceiptUtility.LoadForPrefab(outputPath);
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt.name,
                Is.EqualTo(Path.GetFileNameWithoutExtension(
                    NpcNativeBuildReceiptUtility.GetReceiptPath(outputPath))));
            Assert.That(receipt.Definition, Is.SameAs(definition));
            Assert.That(receipt.DefinitionAssetGuid,
                Is.EqualTo(AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(definition))));
            Assert.That(receipt.DefinitionFingerprint,
                Is.EqualTo("definition-fingerprint"));
            Assert.That(receipt.InputFingerprint,
                Is.EqualTo("input-fingerprint"));
            Assert.That(receipt.ProviderId, Is.EqualTo("test.provider"));
            Assert.That(receipt.RequestedCapabilities,
                Is.EqualTo(NpcCompatibilityCapabilities.CoreAnatomy |
                           NpcCompatibilityCapabilities.AI));
            Assert.That(receipt.NativePrefabAssetGuid,
                Is.EqualTo(AssetDatabase.AssetPathToGUID(outputPath)));
            Assert.That(receipt.NativePrefabDependencyHash,
                Is.EqualTo(AssetDatabase.GetAssetDependencyHash(outputPath)
                    .ToString()));
            Assert.That(receipt.ProviderFingerprint,
                Is.EqualTo("provider-fingerprint"));
            Assert.That(receipt.OutputFingerprint,
                Is.EqualTo("output-fingerprint"));
            Assert.That(receipt.CompatibilityProfileId,
                Is.EqualTo(definition.BuildProfile.CompatibilityProfileId));
            Assert.That(receipt.ToolkitVersion,
                Is.EqualTo(NpcToolkitVersion.Current));

            string before = EditorJsonUtility.ToJson(receipt, false);
            bool dirtyBefore = EditorUtility.IsDirty(receipt);
            NpcNativeBuildReceiptValidationReport validation =
                NpcNativeBuildReceiptUtility.Validate(
                    receipt,
                    definition,
                    "definition-fingerprint",
                    "input-fingerprint",
                    "test.provider",
                    NpcCompatibilityCapabilities.CoreAnatomy |
                    NpcCompatibilityCapabilities.AI);

            Assert.That(validation.IsValid, Is.True,
                string.Join(", ", validation.Issues));
            Assert.That(EditorJsonUtility.ToJson(receipt, false), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(receipt), Is.EqualTo(dirtyBefore));
        }

        [Test]
        public void FailedReceiptUpdateRestoresPrefabAndReceiptBytesAndGuids()
        {
            NpcDefinition definition = CreateDefinition();
            string outputPath = testFolder + "/GeneratedNpc.prefab";
            CommitResult first = Commit(
                CreatePrefab("Pass1.prefab", "First"),
                outputPath,
                definition,
                "definition-v1",
                "input-v1",
                "test.provider",
                NpcCompatibilityCapabilities.CoreAnatomy,
                "provider-v1",
                "output-v1");
            Assert.That(first.Success, Is.True, first.Detail);

            string receiptPath =
                NpcNativeBuildReceiptUtility.GetReceiptPath(outputPath);
            string prefabGuidBefore = AssetDatabase.AssetPathToGUID(outputPath);
            string prefabHashBefore = AssetDatabase.GetAssetDependencyHash(outputPath)
                .ToString();
            string receiptGuidBefore = AssetDatabase.AssetPathToGUID(receiptPath);
            NpcNativeBuildReceipt receiptBefore =
                AssetDatabase.LoadAssetAtPath<NpcNativeBuildReceipt>(receiptPath);
            string receiptJsonBefore = EditorJsonUtility.ToJson(receiptBefore, false);

            // An empty provider fingerprint makes the newly written sidecar
            // fail its read-only verification after the prefab was replaced.
            // The combined transaction must restore both previous assets.
            CommitResult failed = Commit(
                CreatePrefab("Pass2.prefab", "Second"),
                outputPath,
                definition,
                "definition-v2",
                "input-v2",
                "test.provider",
                NpcCompatibilityCapabilities.CoreAnatomy,
                string.Empty,
                "output-v2");

            Assert.That(failed.Success, Is.False);
            Assert.That(failed.PreviousOutputPreserved, Is.True, failed.Detail);
            Assert.That(AssetDatabase.AssetPathToGUID(outputPath),
                Is.EqualTo(prefabGuidBefore));
            Assert.That(AssetDatabase.GetAssetDependencyHash(outputPath).ToString(),
                Is.EqualTo(prefabHashBefore));
            Assert.That(AssetDatabase.AssetPathToGUID(receiptPath),
                Is.EqualTo(receiptGuidBefore));
            NpcNativeBuildReceipt restored =
                AssetDatabase.LoadAssetAtPath<NpcNativeBuildReceipt>(receiptPath);
            Assert.That(EditorJsonUtility.ToJson(restored, false),
                Is.EqualTo(receiptJsonBefore));
            Assert.That(
                NpcNativeBuildReceiptUtility.Validate(restored).IsValid,
                Is.True);
        }

        [Test]
        public void FailedFirstReceiptValidationLeavesNoCommittedOutput()
        {
            NpcDefinition definition = CreateDefinition();
            string outputPath = testFolder + "/GeneratedNpc.prefab";
            string receiptPath =
                NpcNativeBuildReceiptUtility.GetReceiptPath(outputPath);

            CommitResult failed = Commit(
                CreatePrefab("Pass.prefab", "Uncommitted"),
                outputPath,
                definition,
                "definition",
                "input",
                "test.provider",
                NpcCompatibilityCapabilities.CoreAnatomy,
                string.Empty,
                "output");

            Assert.That(failed.Success, Is.False);
            Assert.That(failed.PreviousOutputPreserved, Is.True, failed.Detail);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(outputPath), Is.Null);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(receiptPath), Is.Null);
        }

        [Test]
        public void ValidatorReportsPrefabDependencyDriftWithoutRepairingReceipt()
        {
            NpcDefinition definition = CreateDefinition();
            string outputPath = testFolder + "/GeneratedNpc.prefab";
            CommitResult commit = Commit(
                CreatePrefab("Pass.prefab", "Before"),
                outputPath,
                definition,
                "definition",
                "input",
                "test.provider",
                NpcCompatibilityCapabilities.CoreAnatomy,
                "provider",
                "output");
            Assert.That(commit.Success, Is.True, commit.Detail);
            NpcNativeBuildReceipt receipt =
                NpcNativeBuildReceiptUtility.LoadForPrefab(outputPath);
            string receiptBefore = EditorJsonUtility.ToJson(receipt, false);

            GameObject contents = PrefabUtility.LoadPrefabContents(outputPath);
            try
            {
                var changed = new GameObject("ChangedAfterBuild");
                changed.transform.SetParent(contents.transform, false);
                PrefabUtility.SaveAsPrefabAsset(contents, outputPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.ImportAsset(
                outputPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            NpcNativeBuildReceiptValidationReport validation =
                NpcNativeBuildReceiptUtility.Validate(receipt);

            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues, Has.Some.Matches<
                NpcNativeBuildReceiptIssue>(value =>
                    value.Code == "NATIVE_RECEIPT_PREFAB_DEPENDENCY_CHANGED"));
            Assert.That(EditorJsonUtility.ToJson(receipt, false),
                Is.EqualTo(receiptBefore));
        }

        [Test]
        public void InspectCurrentValidationRejectsCapabilityChoiceDrift()
        {
            NpcDefinition definition = CreateDefinition();
            definition.IncludeHandGrips = false;
            definition.IncludeGaze = false;
            definition.IncludePhysicalJaw = false;
            definition.IncludeNpcAudio = false;
            definition.IncludeSecondaryMotion = false;

            string outputPath = testFolder + "/GeneratedNpc.prefab";
            NpcBuildReadinessReport readiness =
                NpcBuildReadinessDoctor.Validate(definition);
            NpcCompatibilityCapabilities capabilities =
                NpcCompatibilityRequirements.ForDefinition(definition);
            string inputFingerprint =
                NpcNativeBuildCoordinator.ComputeNativeInputFingerprint(
                    definition,
                    readiness.Fingerprint,
                    "test.provider",
                    capabilities);
            CommitResult commit = Commit(
                CreatePrefab("Pass.prefab", "BeforeCapabilityChange"),
                outputPath,
                definition,
                readiness.Fingerprint,
                inputFingerprint,
                "test.provider",
                capabilities,
                "provider",
                "output");
            Assert.That(commit.Success, Is.True, commit.Detail);

            NpcNativeBuildReceipt receipt =
                NpcNativeBuildReceiptUtility.LoadForPrefab(outputPath);
            string receiptBefore = EditorJsonUtility.ToJson(receipt, false);
            NpcNativeBuildReceiptInspection initial =
                NpcNativeBuildReceiptUtility.InspectCurrent(
                    definition,
                    outputPath);
            Assert.That(initial.Validation.IsValid, Is.True,
                string.Join(", ", initial.Validation.Issues));
            Assert.That(initial.RequestedCapabilities,
                Is.EqualTo(capabilities));

            definition.IncludeHandGrips = true;
            AssertCapabilityDrift(
                definition,
                outputPath,
                capabilities | NpcCompatibilityCapabilities.Grips);
            definition.IncludeHandGrips = false;

            definition.IncludeGaze = true;
            AssertCapabilityDrift(
                definition,
                outputPath,
                capabilities | NpcCompatibilityCapabilities.Gaze);
            definition.IncludeGaze = false;

            definition.IncludeSecondaryMotion = true;
            AssertCapabilityDrift(
                definition,
                outputPath,
                capabilities | NpcCompatibilityCapabilities.SecondaryMotion);

            Assert.That(EditorJsonUtility.ToJson(receipt, false),
                Is.EqualTo(receiptBefore));
        }

        private static void AssertCapabilityDrift(
            NpcDefinition definition,
            string outputPath,
            NpcCompatibilityCapabilities expectedCapabilities)
        {
            NpcNativeBuildReceiptInspection inspection =
                NpcNativeBuildReceiptUtility.InspectCurrent(
                    definition,
                    outputPath);

            Assert.That(inspection.RequestedCapabilities,
                Is.EqualTo(expectedCapabilities));
            Assert.That(inspection.Validation.IsValid, Is.False);
            Assert.That(inspection.Validation.Issues, Has.Some.Matches<
                NpcNativeBuildReceiptIssue>(value =>
                    value.Code == "NATIVE_RECEIPT_CAPABILITIES_CHANGED"));
            Assert.That(inspection.Validation.Issues, Has.Some.Matches<
                NpcNativeBuildReceiptIssue>(value =>
                    value.Code == "NATIVE_RECEIPT_INPUT_FINGERPRINT_CHANGED"));
        }

        private NpcDefinition CreateDefinition()
        {
            var build = ScriptableObject.CreateInstance<NpcBuildProfile>();
            build.Initialize("Tester", "Receipt", testFolder);
            AssetDatabase.CreateAsset(build, testFolder + "/BuildProfile.asset");
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            definition.Initialize(
                null,
                NpcAvatarSourceKind.MarrowAvatarPrefab,
                null,
                null,
                build,
                "source-guid",
                "source-hash");
            AssetDatabase.CreateAsset(
                definition,
                testFolder + "/Definition.asset");
            return definition;
        }

        private string CreatePrefab(string fileName, string childName)
        {
            string path = testFolder + "/" + fileName;
            var root = new GameObject("NativeNpc");
            try
            {
                var child = new GameObject(childName);
                child.transform.SetParent(root.transform, false);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(root, path), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            return path;
        }

        private static CommitResult Commit(
            string stagingPath,
            string outputPath,
            NpcDefinition definition,
            string definitionFingerprint,
            string inputFingerprint,
            string providerId,
            NpcCompatibilityCapabilities capabilities,
            string providerFingerprint,
            string outputFingerprint)
        {
            MethodInfo method = typeof(NpcNativeBuildCoordinator).GetMethod(
                "CommitPrefabAndReceipt",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object value = method.Invoke(null, new object[]
            {
                stagingPath,
                outputPath,
                definition,
                definitionFingerprint,
                inputFingerprint,
                providerId,
                capabilities,
                providerFingerprint,
                outputFingerprint,
            });
            Assert.That(value, Is.Not.Null);
            Type type = value.GetType();
            return new CommitResult(
                (bool)type.GetProperty("Success").GetValue(value),
                (string)type.GetProperty("Detail").GetValue(value),
                (bool)type.GetProperty("PreviousOutputPreserved").GetValue(value));
        }

        private sealed class CommitResult
        {
            public bool Success { get; }
            public string Detail { get; }
            public bool PreviousOutputPreserved { get; }

            public CommitResult(
                bool success,
                string detail,
                bool previousOutputPreserved)
            {
                Success = success;
                Detail = detail;
                PreviousOutputPreserved = previousOutputPreserved;
            }
        }
    }
}
