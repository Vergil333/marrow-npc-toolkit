using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.Editor.Compatibility
{
    /// <summary>
    /// Registry for optional native NPC providers. The core registry starts
    /// empty; Default discovers project-local parameterless probes, and provider
    /// packages may also register explicitly during editor initialization.
    /// </summary>
    public sealed class NpcCompatibilityProbeRegistry
    {
        private readonly List<INpcCompatibilityProbe> probes =
            new List<INpcCompatibilityProbe>();
        private readonly bool autoDiscoverProjectProbes;
        private bool projectProbesDiscovered;

        public static NpcCompatibilityProbeRegistry Default { get; } =
            new NpcCompatibilityProbeRegistry(true);

        public IReadOnlyList<INpcCompatibilityProbe> Probes => probes.AsReadOnly();

        public NpcCompatibilityProbeRegistry()
            : this(false)
        {
        }

        private NpcCompatibilityProbeRegistry(bool autoDiscoverProjectProbes)
        {
            this.autoDiscoverProjectProbes = autoDiscoverProjectProbes;
        }

        public void Register(INpcCompatibilityProbe probe)
        {
            if (probe == null)
                throw new ArgumentNullException(nameof(probe));
            if (!probes.Any(value =>
                value != null && value.GetType() == probe.GetType()))
                probes.Add(probe);
        }

        public bool Unregister(INpcCompatibilityProbe probe)
        {
            return probe != null && probes.Remove(probe);
        }

        /// <summary>
        /// Finds parameterless INpcCompatibilityProbe implementations in loaded
        /// editor assemblies. This lets a project-local provider live outside
        /// this public package. Provider construction failures are ignored so an
        /// optional provider can never prevent the toolkit core from loading.
        /// </summary>
        public int DiscoverProjectProbes()
        {
            int discovered = 0;
            foreach (Type type in TypeCache.GetTypesDerivedFrom<INpcCompatibilityProbe>())
            {
                if (type == null || type.IsAbstract || type.IsInterface ||
                    type.ContainsGenericParameters ||
                    type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null) == null ||
                    probes.Any(value => value != null && value.GetType() == type))
                    continue;

                try
                {
                    var probe = Activator.CreateInstance(type, true) as
                        INpcCompatibilityProbe;
                    if (probe == null)
                        continue;
                    int countBeforeRegistration = probes.Count;
                    Register(probe);
                    if (probes.Count > countBeforeRegistration)
                        discovered++;
                }
                catch
                {
                    // Optional providers are data inputs. A broken constructor
                    // must not prevent the public authoring package from loading.
                }
            }

            projectProbesDiscovered = true;
            return discovered;
        }

        public NpcCompatibilityReport Evaluate(NpcBuildProfile buildProfile)
        {
            return Evaluate(buildProfile, NpcSdkEnvironmentProbe.Probe());
        }

        public NpcCompatibilityReport Evaluate(
            NpcBuildProfile buildProfile,
            NpcSdkEnvironment avatarSdkEnvironment)
        {
            if (autoDiscoverProjectProbes && !projectProbesDiscovered)
                DiscoverProjectProbes();

            if (buildProfile == null)
            {
                return CreateReport(
                    string.Empty,
                    avatarSdkEnvironment,
                    NpcNativeProviderStatus.InvalidBuildProfile,
                    null,
                    NpcCompatibilityCapabilities.None,
                    "No NPC Build Profile was supplied.",
                    Array.Empty<string>());
            }

            string requestedProfileId = buildProfile.CompatibilityProfileId;
            if (string.IsNullOrWhiteSpace(requestedProfileId))
            {
                return CreateReport(
                    string.Empty,
                    avatarSdkEnvironment,
                    NpcNativeProviderStatus.InvalidBuildProfile,
                    null,
                    NpcCompatibilityCapabilities.None,
                    "The NPC Build Profile has no compatibility profile ID.",
                    Array.Empty<string>());
            }

            INpcCompatibilityProbe[] snapshot = probes
                .Where(value => value != null)
                .ToArray();
            string[] discoveredProfileIds = snapshot
                .Select(SafeCompatibilityProfileId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            if (snapshot.Length == 0)
            {
                string avatarDetail = IsAvatarSdkAvailable(avatarSdkEnvironment)
                    ? "Avatar authoring can still use the detected Avatar SDK."
                    : "No supported Avatar SDK was detected separately.";
                return CreateReport(
                    requestedProfileId,
                    avatarSdkEnvironment,
                    NpcNativeProviderStatus.NoProviderRegistered,
                    null,
                    NpcCompatibilityCapabilities.None,
                    $"No native NPC provider is registered for '{requestedProfileId}'. " +
                    avatarDetail,
                    discoveredProfileIds);
            }

            INpcCompatibilityProbe[] matching = snapshot
                .Where(value => string.Equals(
                    SafeCompatibilityProfileId(value),
                    requestedProfileId,
                    StringComparison.Ordinal))
                .ToArray();
            if (matching.Length == 0)
            {
                string discovered = discoveredProfileIds.Length == 0
                    ? "no declared compatibility profiles"
                    : string.Join(", ", discoveredProfileIds);
                return CreateReport(
                    requestedProfileId,
                    avatarSdkEnvironment,
                    NpcNativeProviderStatus.CompatibilityProfileMismatch,
                    null,
                    NpcCompatibilityCapabilities.None,
                    $"Registered native NPC providers do not match " +
                    $"'{requestedProfileId}' (found: {discovered}).",
                    discoveredProfileIds);
            }

            NpcCompatibilityReport firstUnavailable = null;
            NpcCompatibilityReport firstFailure = null;
            foreach (INpcCompatibilityProbe probe in matching)
            {
                try
                {
                    NpcCompatibilityProbeResult result = probe.Probe();
                    if (result == null)
                    {
                        firstFailure = firstFailure ?? CreateReport(
                            requestedProfileId,
                            avatarSdkEnvironment,
                            NpcNativeProviderStatus.ProbeFailed,
                            probe,
                            NpcCompatibilityCapabilities.None,
                            "The native NPC provider returned no probe result.",
                            discoveredProfileIds);
                        continue;
                    }

                    if (result.IsAvailable)
                    {
                        return CreateReport(
                            requestedProfileId,
                            avatarSdkEnvironment,
                            NpcNativeProviderStatus.Available,
                            probe,
                            result.Capabilities,
                            result.Detail,
                            discoveredProfileIds);
                    }

                    firstUnavailable = firstUnavailable ?? CreateReport(
                        requestedProfileId,
                        avatarSdkEnvironment,
                        NpcNativeProviderStatus.ProviderUnavailable,
                        probe,
                        NpcCompatibilityCapabilities.None,
                        string.IsNullOrWhiteSpace(result.Detail)
                            ? "The matching native NPC provider is not available."
                            : result.Detail,
                        discoveredProfileIds);
                }
                catch (Exception exception)
                {
                    firstFailure = firstFailure ?? CreateReport(
                        requestedProfileId,
                        avatarSdkEnvironment,
                        NpcNativeProviderStatus.ProbeFailed,
                        probe,
                        NpcCompatibilityCapabilities.None,
                        $"The native NPC provider probe failed: " +
                        $"{exception.GetType().Name}: {exception.Message}",
                        discoveredProfileIds);
                }
            }

            return firstUnavailable ?? firstFailure ?? CreateReport(
                requestedProfileId,
                avatarSdkEnvironment,
                NpcNativeProviderStatus.ProviderUnavailable,
                matching[0],
                NpcCompatibilityCapabilities.None,
                "No matching native NPC provider is currently available.",
                discoveredProfileIds);
        }

        private static string SafeCompatibilityProfileId(INpcCompatibilityProbe probe)
        {
            try
            {
                return probe?.CompatibilityProfileId ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsAvatarSdkAvailable(NpcSdkEnvironment environment)
        {
            return environment != null &&
                environment.ProviderKind != NpcMarrowProviderKind.Unknown;
        }

        private static NpcCompatibilityReport CreateReport(
            string requestedProfileId,
            NpcSdkEnvironment avatarSdkEnvironment,
            NpcNativeProviderStatus status,
            INpcCompatibilityProbe provider,
            NpcCompatibilityCapabilities capabilities,
            string detail,
            IEnumerable<string> discoveredProfileIds)
        {
            return new NpcCompatibilityReport(
                requestedProfileId,
                avatarSdkEnvironment,
                status,
                SafeValue(provider, value => value.ProviderId),
                SafeValue(provider, value => value.DisplayName),
                SafeCompatibilityProfileId(provider),
                capabilities,
                detail,
                discoveredProfileIds);
        }

        private static string SafeValue(
            INpcCompatibilityProbe probe,
            Func<INpcCompatibilityProbe, string> selector)
        {
            if (probe == null)
                return string.Empty;
            try
            {
                return selector(probe) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
