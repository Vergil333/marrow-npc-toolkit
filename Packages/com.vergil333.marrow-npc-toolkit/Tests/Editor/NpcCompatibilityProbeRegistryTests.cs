using System;
using NUnit.Framework;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcCompatibilityProbeRegistryTests
    {
        [Test]
        public void DefinitionRequirementsTrackEveryOptionalCapability()
        {
            NpcCompatibilityCapabilities requiredBaseline =
                NpcCompatibilityCapabilities.CoreAnatomy |
                NpcCompatibilityCapabilities.AI |
                NpcCompatibilityCapabilities.Pooling;
            Assert.That(
                NpcCompatibilityRequirements.ForDefinition(null),
                Is.EqualTo(requiredBaseline));

            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            try
            {
                definition.IncludeHandGrips = false;
                definition.IncludeGaze = false;
                definition.IncludePhysicalJaw = false;
                definition.IncludeNpcAudio = false;
                definition.IncludeSecondaryMotion = false;
                Assert.That(
                    NpcCompatibilityRequirements.ForDefinition(definition),
                    Is.EqualTo(requiredBaseline));

                definition.IncludeHandGrips = true;
                Assert.That(
                    NpcCompatibilityRequirements.ForDefinition(definition),
                    Is.EqualTo(requiredBaseline |
                               NpcCompatibilityCapabilities.Grips));
                definition.IncludeHandGrips = false;

                definition.IncludeGaze = true;
                Assert.That(
                    NpcCompatibilityRequirements.ForDefinition(definition),
                    Is.EqualTo(requiredBaseline |
                               NpcCompatibilityCapabilities.Gaze));
                definition.IncludeGaze = false;

                definition.IncludePhysicalJaw = true;
                Assert.That(
                    NpcCompatibilityRequirements.ForDefinition(definition),
                    Is.EqualTo(requiredBaseline |
                               NpcCompatibilityCapabilities.Jaw));
                definition.IncludePhysicalJaw = false;

                definition.IncludeNpcAudio = true;
                Assert.That(
                    NpcCompatibilityRequirements.ForDefinition(definition),
                    Is.EqualTo(requiredBaseline |
                               NpcCompatibilityCapabilities.Audio));
                definition.IncludeNpcAudio = false;

                definition.IncludeSecondaryMotion = true;
                Assert.That(
                    NpcCompatibilityRequirements.ForDefinition(definition),
                    Is.EqualTo(requiredBaseline |
                               NpcCompatibilityCapabilities.SecondaryMotion));
                definition.IncludeSecondaryMotion = false;

                definition.IncludeNpcAudio = true;
                definition.IncludeHandGrips = true;
                definition.IncludeGaze = true;
                definition.IncludePhysicalJaw = true;
                definition.IncludeSecondaryMotion = true;
                Assert.That(
                    NpcCompatibilityRequirements.ForDefinition(definition),
                    Is.EqualTo(NpcCompatibilityCapabilities.All));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void NoNativeProviderKeepsAvatarSdkAvailabilityIndependent()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            try
            {
                var registry = new NpcCompatibilityProbeRegistry();
                NpcCompatibilityReport report = registry.Evaluate(
                    profile,
                    CreateAvatarSdkEnvironment());

                Assert.That(report.AvatarSdkAvailable, Is.True);
                Assert.That(report.NativeNpcProviderAvailable, Is.False);
                Assert.That(report.NativeProviderStatus,
                    Is.EqualTo(NpcNativeProviderStatus.NoProviderRegistered));
                Assert.That(report.Capabilities,
                    Is.EqualTo(NpcCompatibilityCapabilities.None));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void UnknownAvatarPackageIsNotReportedAsAvailable()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            try
            {
                var registry = new NpcCompatibilityProbeRegistry();
                var unknownAvatarEnvironment = new NpcSdkEnvironment(
                    NpcMarrowProviderKind.Unknown,
                    "",
                    "",
                    "Unknown Marrow provider");

                NpcCompatibilityReport report = registry.Evaluate(
                    profile,
                    unknownAvatarEnvironment);

                Assert.That(report.AvatarSdkAvailable, Is.False);
                Assert.That(report.NativeProviderStatus,
                    Is.EqualTo(NpcNativeProviderStatus.NoProviderRegistered));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void MatchingProviderReportsEveryCapabilityIndependently()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            try
            {
                var registry = new NpcCompatibilityProbeRegistry();
                registry.Register(new FakeProbe(
                    "provider.patch6",
                    "Patch 6 Provider",
                    profile.CompatibilityProfileId,
                    NpcCompatibilityProbeResult.Available(
                        NpcCompatibilityCapabilities.CoreAnatomy |
                        NpcCompatibilityCapabilities.Pooling |
                        NpcCompatibilityCapabilities.Gaze |
                        NpcCompatibilityCapabilities.Audio |
                        NpcCompatibilityCapabilities.SecondaryMotion)));

                NpcCompatibilityReport report = registry.Evaluate(profile, null);

                Assert.That(report.RequestedCompatibilityProfileId,
                    Is.EqualTo(profile.CompatibilityProfileId));
                Assert.That(report.ProviderId, Is.EqualTo("provider.patch6"));
                Assert.That(report.NativeNpcProviderAvailable, Is.True);
                Assert.That(report.AvatarSdkAvailable, Is.False);
                Assert.That(report.SupportsCoreAnatomy, Is.True);
                Assert.That(report.SupportsAI, Is.False);
                Assert.That(report.SupportsPooling, Is.True);
                Assert.That(report.SupportsGrips, Is.False);
                Assert.That(report.SupportsGaze, Is.True);
                Assert.That(report.SupportsJaw, Is.False);
                Assert.That(report.SupportsAudio, Is.True);
                Assert.That(report.SupportsSecondaryMotion, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void MismatchedProviderIsDataAndIsNotInvoked()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            try
            {
                var probe = new FakeProbe(
                    "provider.other",
                    "Other Provider",
                    "another-contract",
                    NpcCompatibilityProbeResult.Available(
                        NpcCompatibilityCapabilities.All));
                var registry = new NpcCompatibilityProbeRegistry();
                registry.Register(probe);

                NpcCompatibilityReport report = registry.Evaluate(
                    profile,
                    CreateAvatarSdkEnvironment());

                Assert.That(report.NativeProviderStatus,
                    Is.EqualTo(NpcNativeProviderStatus.CompatibilityProfileMismatch));
                Assert.That(report.NativeNpcProviderAvailable, Is.False);
                Assert.That(report.AvatarSdkAvailable, Is.True);
                Assert.That(report.DiscoveredCompatibilityProfileIds,
                    Is.EquivalentTo(new[] { "another-contract" }));
                Assert.That(probe.ProbeCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void MatchingUnavailableProviderReturnsClearStatus()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            try
            {
                var registry = new NpcCompatibilityProbeRegistry();
                registry.Register(new FakeProbe(
                    "provider.missing-declarations",
                    "Unavailable Provider",
                    profile.CompatibilityProfileId,
                    NpcCompatibilityProbeResult.Unavailable(
                        "Required provider declarations are not installed.")));

                NpcCompatibilityReport report = registry.Evaluate(
                    profile,
                    CreateAvatarSdkEnvironment());

                Assert.That(report.NativeProviderStatus,
                    Is.EqualTo(NpcNativeProviderStatus.ProviderUnavailable));
                Assert.That(report.NativeNpcProviderAvailable, Is.False);
                Assert.That(report.Detail,
                    Does.Contain("declarations are not installed"));
                Assert.That(report.Capabilities,
                    Is.EqualTo(NpcCompatibilityCapabilities.None));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ProbeExceptionBecomesAReportInsteadOfEscaping()
        {
            NpcBuildProfile profile = CreateBuildProfile();
            try
            {
                var registry = new NpcCompatibilityProbeRegistry();
                registry.Register(new ThrowingProbe(profile.CompatibilityProfileId));

                NpcCompatibilityReport report = null;
                Assert.DoesNotThrow(() => report = registry.Evaluate(profile, null));
                Assert.That(report, Is.Not.Null);
                Assert.That(report.NativeProviderStatus,
                    Is.EqualTo(NpcNativeProviderStatus.ProbeFailed));
                Assert.That(report.Detail, Does.Contain("InvalidOperationException"));
                Assert.That(report.NativeNpcProviderAvailable, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RegistrationIsDeduplicatedByImplementationType()
        {
            var registry = new NpcCompatibilityProbeRegistry();
            registry.Register(new FakeProbe(
                "provider.first",
                "First",
                "first-contract",
                NpcCompatibilityProbeResult.Unavailable("First")));
            registry.Register(new FakeProbe(
                "provider.second",
                "Second",
                "second-contract",
                NpcCompatibilityProbeResult.Unavailable("Second")));

            Assert.That(registry.Probes.Count, Is.EqualTo(1));
            Assert.That(registry.Probes[0].ProviderId, Is.EqualTo("provider.first"));
        }

        private static NpcBuildProfile CreateBuildProfile()
        {
            var profile = ScriptableObject.CreateInstance<NpcBuildProfile>();
            profile.Initialize("Tester", "Example", "Assets/Example");
            return profile;
        }

        private static NpcSdkEnvironment CreateAvatarSdkEnvironment()
        {
            return new NpcSdkEnvironment(
                NpcMarrowProviderKind.Official,
                "com.stresslevelzero.marrow.sdk",
                "1.2.0",
                "Official Marrow SDK");
        }

        private sealed class FakeProbe : INpcCompatibilityProbe
        {
            private readonly NpcCompatibilityProbeResult result;

            public string ProviderId { get; }
            public string DisplayName { get; }
            public string CompatibilityProfileId { get; }
            public int ProbeCount { get; private set; }

            public FakeProbe(
                string providerId,
                string displayName,
                string compatibilityProfileId,
                NpcCompatibilityProbeResult result)
            {
                ProviderId = providerId;
                DisplayName = displayName;
                CompatibilityProfileId = compatibilityProfileId;
                this.result = result;
            }

            public NpcCompatibilityProbeResult Probe()
            {
                ProbeCount++;
                return result;
            }
        }

        private sealed class ThrowingProbe : INpcCompatibilityProbe
        {
            public string ProviderId => "provider.throwing";
            public string DisplayName => "Throwing Provider";
            public string CompatibilityProfileId { get; }

            public ThrowingProbe(string compatibilityProfileId)
            {
                CompatibilityProfileId = compatibilityProfileId;
            }

            public NpcCompatibilityProbeResult Probe()
            {
                throw new InvalidOperationException("Probe failed for testing.");
            }
        }
    }
}
