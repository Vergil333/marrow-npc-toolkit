using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Build;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcPalletPackCoordinatorTests
    {
        private string temporaryDirectory;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(temporaryDirectory)
                && Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, true);
            temporaryDirectory = null;
        }

        [Test]
        public void PlatformMappingIsExplicit()
        {
            Assert.That(NpcPalletPackCoordinator.RequiredBuildTarget(
                    NpcTargetPlatform.Quest),
                Is.EqualTo(BuildTarget.Android));
            Assert.That(NpcPalletPackCoordinator.RequiredBuildTarget(
                    NpcTargetPlatform.Windows),
                Is.EqualTo(BuildTarget.StandaloneWindows64));
            Assert.That(NpcPalletPackCoordinator.RequiredBuildTargetGroup(
                    NpcTargetPlatform.Quest),
                Is.EqualTo(BuildTargetGroup.Android));
            Assert.That(NpcPalletPackCoordinator.RequiredBuildTargetGroup(
                    NpcTargetPlatform.Windows),
                Is.EqualTo(BuildTargetGroup.Standalone));
        }

        [TestCase(
            RuntimePlatform.OSXEditor,
            BuildTarget.StandaloneWindows64,
            ScriptingImplementation.IL2CPP,
            true)]
        [TestCase(
            RuntimePlatform.OSXEditor,
            BuildTarget.Android,
            ScriptingImplementation.IL2CPP,
            false)]
        [TestCase(
            RuntimePlatform.WindowsEditor,
            BuildTarget.StandaloneWindows64,
            ScriptingImplementation.IL2CPP,
            false)]
        [TestCase(
            RuntimePlatform.OSXEditor,
            BuildTarget.StandaloneWindows64,
            ScriptingImplementation.Mono2x,
            false)]
        public void MacWindowsCrossPackProfileIsNarrowlySelected(
            RuntimePlatform editorPlatform,
            BuildTarget target,
            ScriptingImplementation backend,
            bool expected)
        {
            Assert.That(
                NpcPalletPackCoordinator.RequiresMacWindowsMonoCrossPack(
                    editorPlatform,
                    target,
                    backend),
                Is.EqualTo(expected));
        }

        [TestCase(
            RuntimePlatform.OSXEditor,
            BuildTarget.StandaloneWindows64,
            ScriptingImplementation.IL2CPP,
            BuildTargetGroup.Standalone,
            true)]
        [TestCase(
            RuntimePlatform.OSXEditor,
            BuildTarget.StandaloneWindows64,
            ScriptingImplementation.IL2CPP,
            BuildTargetGroup.Android,
            false)]
        [TestCase(
            RuntimePlatform.OSXEditor,
            BuildTarget.StandaloneWindows64,
            ScriptingImplementation.Mono2x,
            BuildTargetGroup.Standalone,
            false)]
        [TestCase(
            RuntimePlatform.WindowsEditor,
            BuildTarget.StandaloneWindows64,
            ScriptingImplementation.IL2CPP,
            BuildTargetGroup.Standalone,
            false)]
        public void MacWindowsCrossPackRequiresAStartOutsideStandalone(
            RuntimePlatform editorPlatform,
            BuildTarget target,
            ScriptingImplementation backend,
            BuildTargetGroup originalGroup,
            bool expected)
        {
            Assert.That(
                NpcPalletPackCoordinator
                    .RequiresNonStandaloneStartForMacWindowsPack(
                        editorPlatform,
                        target,
                        backend,
                        originalGroup),
                Is.EqualTo(expected));
        }

        [TestCase(
            BuildTargetGroup.Android,
            BuildTarget.Android,
            BuildTargetGroup.Android,
            BuildTarget.Android,
            false)]
        [TestCase(
            BuildTargetGroup.Android,
            BuildTarget.Android,
            BuildTargetGroup.Standalone,
            BuildTarget.StandaloneWindows64,
            true)]
        [TestCase(
            BuildTargetGroup.Standalone,
            BuildTarget.StandaloneOSX,
            BuildTargetGroup.Standalone,
            BuildTarget.StandaloneWindows64,
            true)]
        public void BuildTargetSwitchDecisionChecksGroupAndTarget(
            BuildTargetGroup activeGroup,
            BuildTarget activeTarget,
            BuildTargetGroup requiredGroup,
            BuildTarget requiredTarget,
            bool expected)
        {
            Assert.That(
                NpcPalletPackCoordinator.RequiresBuildTargetSwitch(
                    activeGroup,
                    activeTarget,
                    requiredGroup,
                    requiredTarget),
                Is.EqualTo(expected));
        }

        [TestCase(BuildTargetGroup.Android, true)]
        [TestCase(BuildTargetGroup.Standalone, false)]
        public void StandaloneBackendRestoreRequiresAnInactiveGroup(
            BuildTargetGroup activeGroup,
            bool expected)
        {
            Assert.That(
                NpcPalletPackCoordinator.IsStandaloneBackendInactive(
                    activeGroup),
                Is.EqualTo(expected));
        }

        [TestCase(
            ScriptingImplementation.IL2CPP,
            ScriptingImplementation.IL2CPP,
            true)]
        [TestCase(
            ScriptingImplementation.Mono2x,
            ScriptingImplementation.IL2CPP,
            false)]
        public void ScriptingBackendRestoreRequiresTheOriginalValue(
            ScriptingImplementation actual,
            ScriptingImplementation expectedBackend,
            bool expected)
        {
            Assert.That(
                NpcPalletPackCoordinator.IsScriptingBackendRestored(
                    actual,
                    expectedBackend),
                Is.EqualTo(expected));
        }

        [Test]
        public void CompleteOutputRequiresCatalogPalletMonoScriptsAndEverySpawnable()
        {
            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "marrow-npc-pack-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            Touch("catalog_test.json");
            Touch("catalog_test.hash");
            Touch("Author.Pallet.pallet.json");
            Touch("Author.Pallet_monoscripts.bundle");
            Touch("npc_spawnables_assets_spawnable/first.bundle");
            Touch("npc_spawnables_assets_spawnable/second.bundle");
            Touch("npc_spawnables_assets_previewmesh/first.bundle");

            NpcPalletOutputInventory result =
                NpcPalletPackCoordinator.InspectOutput(
                    temporaryDirectory, 2);

            Assert.That(result.IsComplete, Is.True);
            Assert.That(result.CatalogJsonCount, Is.EqualTo(1));
            Assert.That(result.CatalogHashCount, Is.EqualTo(1));
            Assert.That(result.PalletJsonCount, Is.EqualTo(1));
            Assert.That(result.MonoScriptsBundleCount, Is.EqualTo(1));
            Assert.That(result.SpawnableBundleCount, Is.EqualTo(2));
            Assert.That(result.PreviewBundleCount, Is.EqualTo(1));
        }

        [Test]
        public void MissingSharedMonoScriptsBundleRejectsOutput()
        {
            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "marrow-npc-pack-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            Touch("catalog_test.json");
            Touch("catalog_test.hash");
            Touch("Author.Pallet.pallet.json");
            Touch("npc_spawnables_assets_spawnable/first.bundle");

            NpcPalletOutputInventory result =
                NpcPalletPackCoordinator.InspectOutput(
                    temporaryDirectory, 1);

            Assert.That(result.IsComplete, Is.False);
            Assert.That(result.MonoScriptsBundleCount, Is.Zero);
        }

        [Test]
        public void NpcPalletWithoutAnyExpectedSpawnableIsRejected()
        {
            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "marrow-npc-pack-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            Touch("catalog_test.json");
            Touch("catalog_test.hash");
            Touch("Author.Pallet.pallet.json");
            Touch("Author.Pallet_monoscripts.bundle");

            NpcPalletOutputInventory result =
                NpcPalletPackCoordinator.InspectOutput(
                    temporaryDirectory, 0);

            Assert.That(result.IsComplete, Is.False);
            Assert.That(result.ExpectedSpawnableBundleCount, Is.Zero);
        }

        private void Touch(string relativePath)
        {
            string path = Path.Combine(
                temporaryDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)
                                      ?? temporaryDirectory);
            File.WriteAllBytes(path, Array.Empty<byte>());
        }
    }
}
