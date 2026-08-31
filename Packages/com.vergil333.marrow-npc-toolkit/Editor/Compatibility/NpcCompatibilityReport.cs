using System;
using System.Collections.Generic;

namespace Vergil333.MarrowNpcToolkit.Editor.Compatibility
{
    public enum NpcNativeProviderStatus
    {
        NoProviderRegistered,
        CompatibilityProfileMismatch,
        ProviderUnavailable,
        ProbeFailed,
        Available,
        InvalidBuildProfile,
    }

    /// <summary>
    /// A data-only compatibility snapshot. Avatar SDK availability and native
    /// NPC provider availability are intentionally reported independently.
    /// </summary>
    public sealed class NpcCompatibilityReport
    {
        private readonly string[] discoveredCompatibilityProfileIds;

        public string RequestedCompatibilityProfileId { get; }
        public NpcSdkEnvironment AvatarSdkEnvironment { get; }
        public bool AvatarSdkAvailable =>
            AvatarSdkEnvironment != null &&
            AvatarSdkEnvironment.ProviderKind != NpcMarrowProviderKind.Unknown;
        public NpcNativeProviderStatus NativeProviderStatus { get; }
        public bool NativeNpcProviderAvailable =>
            NativeProviderStatus == NpcNativeProviderStatus.Available;
        public string ProviderId { get; }
        public string ProviderDisplayName { get; }
        public string ProviderCompatibilityProfileId { get; }
        public NpcCompatibilityCapabilities Capabilities { get; }
        public string Detail { get; }
        public IReadOnlyList<string> DiscoveredCompatibilityProfileIds =>
            discoveredCompatibilityProfileIds;

        public bool SupportsCoreAnatomy =>
            Supports(NpcCompatibilityCapabilities.CoreAnatomy);
        public bool SupportsAI => Supports(NpcCompatibilityCapabilities.AI);
        public bool SupportsPooling => Supports(NpcCompatibilityCapabilities.Pooling);
        public bool SupportsGrips => Supports(NpcCompatibilityCapabilities.Grips);
        public bool SupportsGaze => Supports(NpcCompatibilityCapabilities.Gaze);
        public bool SupportsJaw => Supports(NpcCompatibilityCapabilities.Jaw);
        public bool SupportsAudio => Supports(NpcCompatibilityCapabilities.Audio);
        public bool SupportsSecondaryMotion =>
            Supports(NpcCompatibilityCapabilities.SecondaryMotion);

        internal NpcCompatibilityReport(
            string requestedCompatibilityProfileId,
            NpcSdkEnvironment avatarSdkEnvironment,
            NpcNativeProviderStatus nativeProviderStatus,
            string providerId,
            string providerDisplayName,
            string providerCompatibilityProfileId,
            NpcCompatibilityCapabilities capabilities,
            string detail,
            IEnumerable<string> discoveredCompatibilityProfileIds)
        {
            RequestedCompatibilityProfileId = requestedCompatibilityProfileId ?? string.Empty;
            AvatarSdkEnvironment = avatarSdkEnvironment;
            NativeProviderStatus = nativeProviderStatus;
            ProviderId = providerId ?? string.Empty;
            ProviderDisplayName = providerDisplayName ?? string.Empty;
            ProviderCompatibilityProfileId = providerCompatibilityProfileId ?? string.Empty;
            Capabilities = nativeProviderStatus == NpcNativeProviderStatus.Available
                ? capabilities & NpcCompatibilityCapabilities.All
                : NpcCompatibilityCapabilities.None;
            Detail = detail ?? string.Empty;
            this.discoveredCompatibilityProfileIds = Copy(
                discoveredCompatibilityProfileIds);
        }

        public bool Supports(NpcCompatibilityCapabilities capability)
        {
            if (capability == NpcCompatibilityCapabilities.None)
                return false;

            NpcCompatibilityCapabilities known =
                capability & NpcCompatibilityCapabilities.All;
            return known == capability && (Capabilities & capability) == capability;
        }

        private static string[] Copy(IEnumerable<string> values)
        {
            if (values == null)
                return Array.Empty<string>();

            var result = new List<string>();
            foreach (string value in values)
                result.Add(value ?? string.Empty);
            return result.ToArray();
        }
    }
}
