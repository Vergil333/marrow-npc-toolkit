using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Build;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Validation;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcPackagingFingerprintTests
    {
        private readonly List<Object> cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = cleanup.Count - 1; index >= 0; index--)
                if (cleanup[index] != null)
                    Object.DestroyImmediate(cleanup[index]);
            cleanup.Clear();
        }

        [Test]
        public void VersionAndPlatformChangePackagingButNotReadinessOrNativeInput()
        {
            NpcDefinition definition = CreateDefinition();
            NpcBuildProfile build = definition.BuildProfile;
            Fingerprints original = Fingerprints.For(definition);

            SetField(build, "version", "9.8.7");
            Fingerprints changedVersion = Fingerprints.For(definition);
            AssertNativeUnchanged(original, changedVersion);
            Assert.That(changedVersion.Packaging,
                Is.Not.EqualTo(original.Packaging));

            SetField(build, "version", "0.1.0");
            SetField(build, "targetPlatform", NpcTargetPlatform.Windows);
            Fingerprints changedPlatform = Fingerprints.For(definition);
            AssertNativeUnchanged(original, changedPlatform);
            Assert.That(changedPlatform.Packaging,
                Is.Not.EqualTo(original.Packaging));
        }

        [Test]
        public void SpawnableAssetBindingsChangePackagingButNotReadinessOrNativeInput()
        {
            NpcDefinition definition = CreateDefinition();
            NpcBuildProfile build = definition.BuildProfile;
            Fingerprints original = Fingerprints.For(definition);

            build.SetSpawnableAssetBindings(
                "11111111111111111111111111111111",
                "22222222222222222222222222222222");
            Fingerprints changed = Fingerprints.For(definition);

            AssertNativeUnchanged(original, changed);
            Assert.That(changed.Packaging, Is.Not.EqualTo(original.Packaging));

            build.SetSpawnableAssetBindings(
                "11111111111111111111111111111111",
                "33333333333333333333333333333333");
            Fingerprints changedCrate = Fingerprints.For(definition);
            AssertNativeUnchanged(original, changedCrate);
            Assert.That(changedCrate.Packaging, Is.Not.EqualTo(changed.Packaging));
        }

        [Test]
        public void EveryBuildProfilePackagingFieldParticipatesDeterministically()
        {
            NpcDefinition definition = CreateDefinition();
            NpcBuildProfile build = definition.BuildProfile;
            string baseline = NpcPackagingFingerprintUtility.Compute(
                definition, null);

            AssertFieldChanges(build, definition, baseline,
                "author", "Another Author");
            AssertFieldChanges(build, definition, baseline,
                "palletTitle", "Another Pallet");
            AssertFieldChanges(build, definition, baseline,
                "crateTitle", "Another Crate");
            AssertFieldChanges(build, definition, baseline,
                "description", "Another public description.");
            AssertFieldChanges(build, definition, baseline,
                "version", "2.0.0");
            AssertFieldChanges(build, definition, baseline,
                "targetPlatform", NpcTargetPlatform.Windows);
            AssertFieldChanges(build, definition, baseline,
                "generatedAssetFolder", "Assets/Another/Generated");
            AssertFieldChanges(build, definition, baseline,
                "compatibilityProfileId", "another-provider-contract");
            AssertFieldChanges(build, definition, baseline,
                "palletAssetGuid", "11111111111111111111111111111111");
            AssertFieldChanges(build, definition, baseline,
                "spawnableCrateAssetGuid", "22222222222222222222222222222222");

            Assert.That(NpcPackagingFingerprintUtility.Compute(definition, null),
                Is.EqualTo(baseline));
        }

        [Test]
        public void StableReceiptFieldsParticipateButBuildTimestampDoesNot()
        {
            NpcDefinition definition = CreateDefinition();
            var receipt = Track(ScriptableObject.CreateInstance<
                NpcNativeBuildReceipt>());
            SetField(receipt, "schemaVersion",
                NpcNativeBuildReceipt.CurrentSchemaVersion);
            SetField(receipt, "definitionAssetGuid", "definition-guid");
            SetField(receipt, "definitionFingerprint", "definition-fingerprint");
            SetField(receipt, "inputFingerprint", "input-fingerprint");
            SetField(receipt, "providerId", "provider.id");
            SetField(receipt, "requestedCapabilities",
                NpcCompatibilityCapabilities.CoreAnatomy |
                NpcCompatibilityCapabilities.AI);
            SetField(receipt, "nativePrefabAssetPath",
                "Assets/Generated/Npc.prefab");
            SetField(receipt, "nativePrefabAssetGuid", "prefab-guid");
            SetField(receipt, "nativePrefabDependencyHash", "prefab-hash");
            SetField(receipt, "providerFingerprint", "provider-fingerprint");
            SetField(receipt, "outputFingerprint", "output-fingerprint");
            SetField(receipt, "compatibilityProfileId", "compatibility-id");
            SetField(receipt, "builtAtUtc", "2026-08-19T12:00:00.0000000Z");
            SetField(receipt, "builtAtUtcTicks", 639017424000000000L);

            string withoutReceipt = NpcPackagingFingerprintUtility.Compute(
                definition, null);
            string first = NpcPackagingFingerprintUtility.Compute(
                definition, receipt);
            SetField(receipt, "nativePrefabDependencyHash",
                "different-imported-artifact-hash");
            string dependencyHashChanged = NpcPackagingFingerprintUtility.Compute(
                definition, receipt);
            SetField(receipt, "builtAtUtc", "2026-08-20T12:00:00.0000000Z");
            SetField(receipt, "builtAtUtcTicks", 639018288000000000L);
            string timestampChanged = NpcPackagingFingerprintUtility.Compute(
                definition, receipt);
            SetField(receipt, "outputFingerprint", "different-output");
            string outputChanged = NpcPackagingFingerprintUtility.Compute(
                definition, receipt);

            Assert.That(first, Is.Not.EqualTo(withoutReceipt));
            Assert.That(dependencyHashChanged, Is.EqualTo(first));
            Assert.That(timestampChanged, Is.EqualTo(first));
            Assert.That(outputChanged, Is.Not.EqualTo(first));
        }

        private static void AssertNativeUnchanged(
            Fingerprints expected,
            Fingerprints actual)
        {
            Assert.That(actual.Readiness, Is.EqualTo(expected.Readiness));
            Assert.That(actual.NativeInput, Is.EqualTo(expected.NativeInput));
        }

        private static void AssertFieldChanges(
            NpcBuildProfile build,
            NpcDefinition definition,
            string baseline,
            string fieldName,
            object changedValue)
        {
            FieldInfo field = FindField(build.GetType(), fieldName);
            object original = field.GetValue(build);
            try
            {
                field.SetValue(build, changedValue);
                string changed = NpcPackagingFingerprintUtility.Compute(
                    definition, null);
                Assert.That(changed, Is.Not.EqualTo(baseline), fieldName);
                Assert.That(NpcPackagingFingerprintUtility.Compute(
                    definition, null), Is.EqualTo(changed), fieldName);
            }
            finally
            {
                field.SetValue(build, original);
            }
        }

        private NpcDefinition CreateDefinition()
        {
            var build = Track(ScriptableObject.CreateInstance<NpcBuildProfile>());
            build.Initialize("Author", "Example", "Assets/Example");
            var definition = Track(ScriptableObject.CreateInstance<NpcDefinition>());
            definition.name = "ExampleNpcDefinition";
            definition.Initialize(
                null,
                NpcAvatarSourceKind.MarrowAvatarPrefab,
                null,
                null,
                build,
                "source-guid",
                "source-hash");
            return definition;
        }

        private T Track<T>(T value) where T : Object
        {
            cleanup.Add(value);
            return value;
        }

        private static void SetField(Object target, string fieldName, object value)
        {
            FindField(target.GetType(), fieldName).SetValue(target, value);
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return field;
        }

        private sealed class Fingerprints
        {
            public string Readiness { get; }
            public string NativeInput { get; }
            public string Packaging { get; }

            private Fingerprints(
                string readiness,
                string nativeInput,
                string packaging)
            {
                Readiness = readiness;
                NativeInput = nativeInput;
                Packaging = packaging;
            }

            public static Fingerprints For(NpcDefinition definition)
            {
                string readiness = NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, null).Fingerprint;
                string nativeInput =
                    NpcNativeBuildCoordinator.ComputeNativeInputFingerprint(
                        definition,
                        readiness,
                        "test.provider",
                        NpcCompatibilityCapabilities.CoreAnatomy |
                        NpcCompatibilityCapabilities.AI);
                string packaging = NpcPackagingFingerprintUtility.Compute(
                    definition, null);
                return new Fingerprints(readiness, nativeInput, packaging);
            }
        }
    }
}
