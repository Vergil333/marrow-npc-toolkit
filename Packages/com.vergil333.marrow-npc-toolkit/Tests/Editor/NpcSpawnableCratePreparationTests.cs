using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SLZ.Marrow.Warehouse;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Build;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcSpawnableCratePreparationTests
    {
        private string testFolder;
        private NpcDefinition definition;
        private NpcBuildProfile build;
        private NpcNativeBuildReceipt receipt;
        private string nativePath;

        [SetUp]
        public void SetUp()
        {
            testFolder = "Assets/__NpcSpawnableCratePreparationTests_"
                         + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder(
                "Assets", testFolder.Substring("Assets/".Length));

            GameObject source = CreatePrefab(
                testFolder + "/SourceAvatar.prefab", "SourceAvatar");
            build = ScriptableObject.CreateInstance<NpcBuildProfile>();
            build.Initialize("Test Author", "Test NPC", testFolder);
            AssetDatabase.CreateAsset(
                build, testFolder + "/BuildProfile.asset");
            definition = ScriptableObject.CreateInstance<NpcDefinition>();
            string sourcePath = AssetDatabase.GetAssetPath(source);
            definition.Initialize(
                source,
                NpcAvatarSourceKind.MarrowAvatarPrefab,
                null,
                null,
                build,
                AssetDatabase.AssetPathToGUID(sourcePath),
                AssetDatabase.GetAssetDependencyHash(sourcePath).ToString());
            AssetDatabase.CreateAsset(
                definition, testFolder + "/Definition.asset");

            nativePath = NpcNativeBuildCoordinator.GetDefaultOutputPath(
                definition);
            EnsureFolder(Path.GetDirectoryName(nativePath)?.Replace('\\', '/'));
            CreatePrefab(nativePath, "NativeNpc");
            receipt = CreateReceipt(
                definition,
                nativePath,
                "definition-fingerprint",
                "input-fingerprint");
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(testFolder)
                && AssetDatabase.IsValidFolder(testFolder))
                AssetDatabase.DeleteAsset(testFolder);
        }

        [Test]
        public void FirstPreparationCreatesAndBindsOfficialMarrowAssets()
        {
            NpcSpawnableCratePreparationReport report = PrepareVerified();

            Assert.That(report.Success, Is.True, ReportDetail(report));
            Assert.That(report.PalletCreated, Is.True);
            Assert.That(report.CrateCreated, Is.True);
            Assert.That(build.HasSpawnableAssetBindings, Is.True);
            Assert.That(report.PalletAssetGuid,
                Is.EqualTo(build.PalletAssetGuid));
            Assert.That(report.CrateAssetGuid,
                Is.EqualTo(build.SpawnableCrateAssetGuid));

            Pallet pallet = AssetDatabase.LoadAssetAtPath<Pallet>(
                report.PalletAssetPath);
            SpawnableCrate crate =
                AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                    report.CrateAssetPath);
            Assert.That(pallet, Is.Not.Null);
            Assert.That(crate, Is.Not.Null);
            Assert.That(Barcode.IsValid(pallet.Barcode), Is.True);
            Assert.That(Barcode.IsValid(crate.Barcode), Is.True);
            Assert.That(pallet.Title, Is.EqualTo(build.PalletTitle));
            Assert.That(pallet.Author, Is.EqualTo(build.Author));
            Assert.That(pallet.Version, Is.EqualTo(build.Version));
            Assert.That(pallet.Description, Is.EqualTo(build.Description));
            Assert.That(crate.Title, Is.EqualTo(build.CrateTitle));
            Assert.That(crate.Description, Is.EqualTo(build.Description));
            Assert.That(crate.MainAsset.AssetGUID,
                Is.EqualTo(receipt.NativePrefabAssetGuid));
            Assert.That(pallet.Crates.Count(value => value == crate),
                Is.EqualTo(1));
            Assert.That(pallet.Crates.All(value => value.Pallet == pallet),
                Is.True);
        }

        [Test]
        public void UpdateKeepsAssetGuidsAndBarcodesStable()
        {
            NpcSpawnableCratePreparationReport first = PrepareVerified();
            Assert.That(first.Success, Is.True, ReportDetail(first));
            string palletGuid = first.PalletAssetGuid;
            string crateGuid = first.CrateAssetGuid;
            string palletBarcode = first.PalletBarcode;
            string crateBarcode = first.CrateBarcode;

            Pallet pallet = AssetDatabase.LoadAssetAtPath<Pallet>(
                first.PalletAssetPath);
            SpawnableCrate crate =
                AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                    first.CrateAssetPath);
            pallet.Crates.Add(crate);
            EditorUtility.SetDirty(pallet);
            SetBuildMetadata(
                "Updated Author",
                "Updated Pallet",
                "Updated Crate",
                "Updated public description.",
                "2.3.4");

            NpcSpawnableCratePreparationReport updated = PrepareVerified();

            Assert.That(updated.Success, Is.True, ReportDetail(updated));
            Assert.That(updated.PalletCreated, Is.False);
            Assert.That(updated.CrateCreated, Is.False);
            Assert.That(updated.PalletAssetGuid, Is.EqualTo(palletGuid));
            Assert.That(updated.CrateAssetGuid, Is.EqualTo(crateGuid));
            Assert.That(updated.PalletBarcode, Is.EqualTo(palletBarcode));
            Assert.That(updated.CrateBarcode, Is.EqualTo(crateBarcode));
            Pallet savedPallet = AssetDatabase.LoadAssetAtPath<Pallet>(
                updated.PalletAssetPath);
            SpawnableCrate savedCrate =
                AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                    updated.CrateAssetPath);
            Assert.That(savedPallet.Title, Is.EqualTo("Updated Pallet"));
            Assert.That(savedPallet.Author, Is.EqualTo("Updated Author"));
            Assert.That(savedPallet.Version, Is.EqualTo("2.3.4"));
            Assert.That(savedCrate.Title, Is.EqualTo("Updated Crate"));
            Assert.That(savedPallet.Crates.Count(value => value == savedCrate),
                Is.EqualTo(1));
            Assert.That(savedPallet.Crates.All(
                value => value.Pallet == savedPallet), Is.True);
        }

        [Test]
        public void MissingBoundGuidFailsWithoutTitleLookupOrReplacement()
        {
            build.SetSpawnableAssetBindings(
                "11111111111111111111111111111111",
                "22222222222222222222222222222222");
            Save(build);

            NpcSpawnableCratePreparationReport report = PrepareVerified();

            Assert.That(report.Success, Is.False);
            Assert.That(report.Status,
                Is.EqualTo(
                    NpcSpawnableCratePreparationStatus.BoundAssetMissing));
            Assert.That(build.PalletAssetGuid,
                Is.EqualTo("11111111111111111111111111111111"));
            Assert.That(build.SpawnableCrateAssetGuid,
                Is.EqualTo("22222222222222222222222222222222"));
            Assert.That(AssetDatabase.FindAssets(
                "t:Pallet", new[] { testFolder }).Length, Is.EqualTo(0));
            Assert.That(AssetDatabase.FindAssets(
                "t:SpawnableCrate", new[] { testFolder }).Length,
                Is.EqualTo(0));
        }

        [Test]
        public void CrossPalletBindingsAreRejectedWithoutImplicitReparenting()
        {
            NpcSpawnableCratePreparationReport first = PrepareVerified();
            Assert.That(first.Success, Is.True, ReportDetail(first));
            Pallet original = AssetDatabase.LoadAssetAtPath<Pallet>(
                first.PalletAssetPath);
            SpawnableCrate crate =
                AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                    first.CrateAssetPath);
            Pallet unrelated = Pallet.CreatePallet(
                "Unrelated Pallet", "Test Author");
            string unrelatedPath = testFolder + "/Unrelated.pallet.asset";
            AssetDatabase.CreateAsset(unrelated, unrelatedPath);
            AssetDatabase.SaveAssets();

            build.SetSpawnableAssetBindings(
                AssetDatabase.AssetPathToGUID(unrelatedPath),
                first.CrateAssetGuid);
            Save(build);

            NpcSpawnableCratePreparationReport result = PrepareVerified();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status,
                Is.EqualTo(
                    NpcSpawnableCratePreparationStatus.BoundAssetMissing));
            Assert.That(unrelated.Crates.Contains(crate), Is.False);
            Assert.That(original.Crates.Contains(crate), Is.True);
        }

        [Test]
        public void StaleNativeReceiptIsRefusedReadOnly()
        {
            string receiptPath = AssetDatabase.GetAssetPath(receipt);
            byte[] receiptBytes = File.ReadAllBytes(
                AbsoluteAssetPath(receiptPath));
            GameObject contents = PrefabUtility.LoadPrefabContents(nativePath);
            try
            {
                var changed = new GameObject("ChangedAfterReceipt");
                changed.transform.SetParent(contents.transform, false);
                PrefabUtility.SaveAsPrefabAsset(contents, nativePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            Import(nativePath);

            NpcSpawnableCratePreparationReport report =
                NpcSpawnableCratePreparationCoordinator.Prepare(
                    new NpcSpawnableCratePreparationRequest(definition));

            Assert.That(report.Success, Is.False);
            Assert.That(report.Status,
                Is.EqualTo(
                    NpcSpawnableCratePreparationStatus.NativeReceiptStale));
            Assert.That(File.ReadAllBytes(AbsoluteAssetPath(receiptPath)),
                Is.EqualTo(receiptBytes));
            Assert.That(build.HasSpawnableAssetBindings, Is.False);
        }

        [Test]
        public void PostSaveValidationFailureRestoresEveryExistingAssetByte()
        {
            NpcSpawnableCratePreparationReport first = PrepareVerified();
            Assert.That(first.Success, Is.True, ReportDetail(first));
            Pallet pallet = AssetDatabase.LoadAssetAtPath<Pallet>(
                first.PalletAssetPath);
            SpawnableCrate crate =
                AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                    first.CrateAssetPath);

            pallet.Title = "Pallet Before Failed Update";
            crate.Title = "Crate Before Failed Update";
            pallet.Crates.Add(null);
            Save(crate);
            Save(pallet);
            SetBuildMetadata(
                "Desired Author",
                "Desired Pallet",
                "Desired Crate",
                "Desired description.",
                "9.0.0");

            string buildPath = AssetDatabase.GetAssetPath(build);
            byte[] buildBefore = File.ReadAllBytes(AbsoluteAssetPath(buildPath));
            byte[] palletBefore = File.ReadAllBytes(
                AbsoluteAssetPath(first.PalletAssetPath));
            byte[] crateBefore = File.ReadAllBytes(
                AbsoluteAssetPath(first.CrateAssetPath));
            string palletGuid = first.PalletAssetGuid;
            string crateGuid = first.CrateAssetGuid;

            NpcSpawnableCratePreparationReport failed = PrepareVerified();

            Assert.That(failed.Success, Is.False);
            Assert.That(failed.Status,
                Is.EqualTo(
                    NpcSpawnableCratePreparationStatus.ValidationFailed),
                ReportDetail(failed));
            Assert.That(failed.PreviousAssetsPreserved, Is.True,
                ReportDetail(failed));
            Assert.That(File.ReadAllBytes(AbsoluteAssetPath(buildPath)),
                Is.EqualTo(buildBefore));
            Assert.That(File.ReadAllBytes(
                    AbsoluteAssetPath(first.PalletAssetPath)),
                Is.EqualTo(palletBefore));
            Assert.That(File.ReadAllBytes(
                    AbsoluteAssetPath(first.CrateAssetPath)),
                Is.EqualTo(crateBefore));
            Assert.That(AssetDatabase.AssetPathToGUID(first.PalletAssetPath),
                Is.EqualTo(palletGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(first.CrateAssetPath),
                Is.EqualTo(crateGuid));
            Pallet restoredPallet = AssetDatabase.LoadAssetAtPath<Pallet>(
                first.PalletAssetPath);
            SpawnableCrate restoredCrate =
                AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                    first.CrateAssetPath);
            Assert.That(restoredPallet.Title,
                Is.EqualTo("Pallet Before Failed Update"));
            Assert.That(restoredCrate.Title,
                Is.EqualTo("Crate Before Failed Update"));
        }

        private NpcSpawnableCratePreparationReport PrepareVerified()
        {
            return NpcSpawnableCratePreparationCoordinator.PrepareVerified(
                definition, receipt, nativePath);
        }

        private NpcNativeBuildReceipt CreateReceipt(
            NpcDefinition target,
            string prefabPath,
            string definitionFingerprint,
            string inputFingerprint)
        {
            var value = ScriptableObject.CreateInstance<NpcNativeBuildReceipt>();
            value.Initialize(new NpcNativeBuildReceiptData(
                target,
                definitionFingerprint,
                inputFingerprint,
                "test.provider",
                NpcCompatibilityCapabilities.CoreAnatomy,
                prefabPath,
                AssetDatabase.AssetPathToGUID(prefabPath),
                AssetDatabase.GetAssetDependencyHash(prefabPath).ToString(),
                "provider-fingerprint",
                "output-fingerprint",
                DateTime.UtcNow));
            AssetDatabase.CreateAsset(
                value,
                NpcNativeBuildReceiptUtility.GetReceiptPath(prefabPath));
            return value;
        }

        private void SetBuildMetadata(
            string author,
            string palletTitle,
            string crateTitle,
            string description,
            string version)
        {
            SetField(build, "author", author);
            SetField(build, "palletTitle", palletTitle);
            SetField(build, "crateTitle", crateTitle);
            SetField(build, "description", description);
            SetField(build, "version", version);
            Save(build);
        }

        private static void SetField(Object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static void Save(Object target)
        {
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            Import(AssetDatabase.GetAssetPath(target));
        }

        private static GameObject CreatePrefab(string path, string rootName)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            var root = new GameObject(rootName);
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(root, path),
                    Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Import(path);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static void Import(string path)
        {
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private static string AbsoluteAssetPath(string assetPath)
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName;
            return Path.GetFullPath(Path.Combine(root ?? string.Empty, assetPath));
        }

        private static string ReportDetail(
            NpcSpawnableCratePreparationReport report)
        {
            return report == null
                ? "No report."
                : string.Join(" | ", report.Messages.Select(
                    value => value.Code + ": " + value.Message));
        }
    }
}
