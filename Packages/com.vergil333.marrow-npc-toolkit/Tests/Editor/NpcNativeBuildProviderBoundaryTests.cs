using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Build;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcNativeBuildProviderBoundaryTests
    {
        [Test]
        public void RegistrySelectsOneMatchingCapableProvider()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            try
            {
                var registry = new NpcNativeBuildProviderRegistry();
                var provider = new FakeProviderA(
                    profile.CompatibilityProfileId,
                    NpcCompatibilityCapabilities.CoreAnatomy);
                registry.Register(provider);

                NpcNativeBuildProviderSelection selection = registry.Resolve(
                    profile,
                    NpcCompatibilityCapabilities.CoreAnatomy);

                Assert.That(selection.CanBuild, Is.True);
                Assert.That(selection.Provider, Is.SameAs(provider));
                Assert.That(selection.Status,
                    Is.EqualTo(NpcNativeBuildProviderSelectionStatus.Available));
                Assert.That(provider.ProbeCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RegistryRefusesProviderMissingRequestedCapability()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            try
            {
                var registry = new NpcNativeBuildProviderRegistry();
                registry.Register(new FakeProviderA(
                    profile.CompatibilityProfileId,
                    NpcCompatibilityCapabilities.CoreAnatomy));

                NpcNativeBuildProviderSelection selection = registry.Resolve(
                    profile,
                    NpcCompatibilityCapabilities.CoreAnatomy |
                    NpcCompatibilityCapabilities.AI);

                Assert.That(selection.CanBuild, Is.False);
                Assert.That(selection.Status,
                    Is.EqualTo(NpcNativeBuildProviderSelectionStatus.CapabilityMismatch));
                Assert.That(selection.Detail, Does.Contain("AI"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AmbiguousProvidersRequireExactProviderId()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            try
            {
                var first = new FakeProviderA(
                    profile.CompatibilityProfileId,
                    NpcCompatibilityCapabilities.CoreAnatomy);
                var second = new FakeProviderB(
                    profile.CompatibilityProfileId,
                    NpcCompatibilityCapabilities.CoreAnatomy);
                var registry = new NpcNativeBuildProviderRegistry();
                registry.Register(second);
                registry.Register(first);

                NpcNativeBuildProviderSelection ambiguous = registry.Resolve(
                    profile,
                    NpcCompatibilityCapabilities.CoreAnatomy);
                NpcNativeBuildProviderSelection explicitSelection = registry.Resolve(
                    profile,
                    NpcCompatibilityCapabilities.CoreAnatomy,
                    first.ProviderId);

                Assert.That(ambiguous.Status,
                    Is.EqualTo(NpcNativeBuildProviderSelectionStatus.AmbiguousProvider));
                Assert.That(ambiguous.CandidateProviderIds,
                    Is.EqualTo(new[] { "fake.a", "fake.b" }));
                Assert.That(explicitSelection.CanBuild, Is.True);
                Assert.That(explicitSelection.Provider, Is.SameAs(first));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FakeProviderConfiguresThenValidatesSavedOutputWithSameReceipt()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            NpcDefinition definition = ScriptableObject.CreateInstance<NpcDefinition>();
            var root = new GameObject("Generated NPC");
            var animationRoot = new GameObject("AnimationRoot");
            var physicsRoot = new GameObject("Physics");
            var hips = new GameObject(HumanBodyBones.Hips.ToString());
            animationRoot.transform.SetParent(root.transform, false);
            physicsRoot.transform.SetParent(root.transform, false);
            hips.transform.SetParent(physicsRoot.transform, false);
            try
            {
                var context = Activator.CreateInstance(
                    typeof(NpcNativeBuildContext),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[]
                    {
                        definition,
                        root,
                        NpcCompatibilityCapabilities.CoreAnatomy,
                        "input-fingerprint",
                        1,
                    },
                    null) as NpcNativeBuildContext;
                Assert.That(context, Is.Not.Null);
                var provider = new FakeProviderA(
                    profile.CompatibilityProfileId,
                    NpcCompatibilityCapabilities.CoreAnatomy);

                NpcNativeBuildProviderResult result =
                    provider.ConfigureStagedPrefab(context);
                var validationContext = Activator.CreateInstance(
                    typeof(NpcNativeBuildValidationContext),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[]
                    {
                        definition,
                        root,
                        NpcCompatibilityCapabilities.CoreAnatomy,
                        "input-fingerprint",
                        "Assets/Generated/Pass.prefab",
                    },
                    null) as NpcNativeBuildValidationContext;
                Assert.That(validationContext, Is.Not.Null);
                NpcNativeBuildProviderResult validationResult =
                    provider.ValidateSavedPrefab(validationContext);

                Assert.That(result.Success, Is.True);
                Assert.That(result.StructuralFingerprint,
                    Is.EqualTo("fake-native-structure-v1"));
                Assert.That(validationResult.Success, Is.True);
                Assert.That(validationResult.StructuralFingerprint,
                    Is.EqualTo(result.StructuralFingerprint));
                Assert.That(context.AnimationRoot, Is.SameAs(animationRoot.transform));
                Assert.That(context.PhysicsRoot, Is.SameAs(physicsRoot.transform));
                Assert.That(context.FindPhysicsBody(HumanBodyBones.Hips),
                    Is.SameAs(hips.transform));
                Assert.That(validationContext.Definition, Is.SameAs(definition));
                Assert.That(validationContext.OutputRoot, Is.SameAs(root));
                Assert.That(validationContext.AnimationRoot,
                    Is.SameAs(animationRoot.transform));
                Assert.That(validationContext.PhysicsRoot,
                    Is.SameAs(physicsRoot.transform));
                Assert.That(validationContext.RequiredCapabilities,
                    Is.EqualTo(NpcCompatibilityCapabilities.CoreAnatomy));
                Assert.That(validationContext.InputFingerprint,
                    Is.EqualTo("input-fingerprint"));
                Assert.That(validationContext.OutputAssetPath,
                    Is.EqualTo("Assets/Generated/Pass.prefab"));
                Assert.That(
                    validationContext.FindPhysicsBody(HumanBodyBones.Hips),
                    Is.SameAs(hips.transform));
                Assert.That(root.transform.Find("NativeConfigured"), Is.Not.Null);
                Assert.That(provider.ConfigureCount, Is.EqualTo(1));
                Assert.That(provider.ValidationCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SuccessWithoutProviderFingerprintBecomesFailure()
        {
            NpcNativeBuildProviderResult result =
                NpcNativeBuildProviderResult.Succeeded(" ");

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.Messages[0].Code,
                Is.EqualTo("PROVIDER_FINGERPRINT_MISSING"));
        }

        [Test]
        public void InputGuardDetectsAndRestoresAudioProfileMutation()
        {
            var audio = ScriptableObject.CreateInstance<NpcAudioProfile>();
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            var preview = new GameObject("Preview");
            try
            {
                definition.Initialize(
                    null,
                    NpcAvatarSourceKind.MarrowAvatarPrefab,
                    null,
                    null,
                    null,
                    "source-guid",
                    "source-hash",
                    audio);
                audio.SetProvenance(
                    "English", "Before", "Credit", "Permission", string.Empty);
                string before = EditorJsonUtility.ToJson(audio, false);
                NpcNativeBuildInputGuard guard =
                    NpcNativeBuildInputGuard.Capture(definition, preview);

                audio.SetProvenance(
                    "English", "After", "Credit", "Permission", string.Empty);

                Assert.That(guard.FindMutation(), Does.Contain(audio.name));
                guard.RestoreScriptableObjects();
                Assert.That(EditorJsonUtility.ToJson(audio, false), Is.EqualTo(before));
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(audio);
            }
        }

        [Test]
        public void InputGuardDetectsAndRestoresMovementProfileMutation()
        {
            var movement = ScriptableObject.CreateInstance<NpcMovementProfile>();
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            var preview = new GameObject("Preview");
            try
            {
                movement.ResetToDefaults();
                movement.SetAutoFitMeasurements(
                    1.6f,
                    1.72f,
                    1.7f,
                    0.82f,
                    0.84f,
                    0.3f,
                    0.26f,
                    0.02f,
                    0.38f,
                    0f,
                    Vector3.forward,
                    Vector3.forward,
                    "source-hash",
                    "authoring-hash");
                definition.Initialize(
                    null,
                    NpcAvatarSourceKind.MarrowAvatarPrefab,
                    null,
                    null,
                    null,
                    "source-guid",
                    "source-hash",
                    null,
                    movement);
                string before = EditorJsonUtility.ToJson(movement, false);
                NpcNativeBuildInputGuard guard =
                    NpcNativeBuildInputGuard.Capture(definition, preview);

                movement.StrideScale = 1.25f;

                Assert.That(guard.FindMutation(), Does.Contain(movement.name));
                guard.RestoreScriptableObjects();
                Assert.That(EditorJsonUtility.ToJson(movement, false),
                    Is.EqualTo(before));
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(movement);
            }
        }

        private static NpcBuildProfile CreateBuildProfile()
        {
            var profile = ScriptableObject.CreateInstance<NpcBuildProfile>();
            profile.Initialize("Tester", "Example", "Assets/Example");
            return profile;
        }

        private abstract class FakeProvider : INpcNativeBuildProvider
        {
            private readonly NpcCompatibilityCapabilities capabilities;

            public abstract string ProviderId { get; }
            public string DisplayName => "Fake native provider";
            public string CompatibilityProfileId { get; }
            public int ProbeCount { get; private set; }
            public int ConfigureCount { get; private set; }
            public int ValidationCount { get; private set; }

            protected FakeProvider(
                string compatibilityProfileId,
                NpcCompatibilityCapabilities capabilities)
            {
                CompatibilityProfileId = compatibilityProfileId;
                this.capabilities = capabilities;
            }

            public NpcCompatibilityProbeResult Probe()
            {
                ProbeCount++;
                return NpcCompatibilityProbeResult.Available(capabilities);
            }

            public NpcNativeBuildProviderResult ConfigureStagedPrefab(
                NpcNativeBuildContext context)
            {
                ConfigureCount++;
                if (context == null || context.OutputRoot == null)
                    return NpcNativeBuildProviderResult.Failed(
                        "FAKE_CONTEXT_MISSING",
                        "No staged output root was supplied.");
                var marker = new GameObject("NativeConfigured");
                marker.transform.SetParent(context.OutputRoot.transform, false);
                return NpcNativeBuildProviderResult.Succeeded(
                    "fake-native-structure-v1");
            }

            public NpcNativeBuildProviderResult ValidateSavedPrefab(
                NpcNativeBuildValidationContext context)
            {
                ValidationCount++;
                if (context == null || context.OutputRoot == null)
                    return NpcNativeBuildProviderResult.Failed(
                        "FAKE_VALIDATION_CONTEXT_MISSING",
                        "No saved output root was supplied.");
                if (context.OutputRoot.transform.Find("NativeConfigured") == null)
                    return NpcNativeBuildProviderResult.Failed(
                        "FAKE_MARKER_MISSING",
                        "The configured marker did not survive serialization.");
                return NpcNativeBuildProviderResult.Succeeded(
                    "fake-native-structure-v1");
            }
        }

        private sealed class FakeProviderA : FakeProvider
        {
            public override string ProviderId => "fake.a";

            public FakeProviderA(
                string compatibilityProfileId,
                NpcCompatibilityCapabilities capabilities)
                : base(compatibilityProfileId, capabilities)
            {
            }
        }

        private sealed class FakeProviderB : FakeProvider
        {
            public override string ProviderId => "fake.b";

            public FakeProviderB(
                string compatibilityProfileId,
                NpcCompatibilityCapabilities capabilities)
                : base(compatibilityProfileId, capabilities)
            {
            }
        }
    }
}
