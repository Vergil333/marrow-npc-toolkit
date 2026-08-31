using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;

namespace Vergil333.MarrowNpcToolkit.Editor.Build
{
    public enum NpcNativeBuildProviderSelectionStatus
    {
        InvalidRequest,
        NoProviderRegistered,
        RequestedProviderNotFound,
        CompatibilityProfileMismatch,
        ProviderUnavailable,
        CapabilityMismatch,
        ProbeFailed,
        AmbiguousProvider,
        Available,
    }

    public sealed class NpcNativeBuildProviderSelection
    {
        private readonly string[] candidateProviderIds;

        public NpcNativeBuildProviderSelectionStatus Status { get; }
        public bool CanBuild => Status == NpcNativeBuildProviderSelectionStatus.Available
                                && Provider != null;
        public INpcNativeBuildProvider Provider { get; }
        public NpcCompatibilityProbeResult ProbeResult { get; }
        public NpcCompatibilityCapabilities RequiredCapabilities { get; }
        public string Detail { get; }
        public IReadOnlyList<string> CandidateProviderIds => candidateProviderIds;

        internal NpcNativeBuildProviderSelection(
            NpcNativeBuildProviderSelectionStatus status,
            INpcNativeBuildProvider provider,
            NpcCompatibilityProbeResult probeResult,
            NpcCompatibilityCapabilities requiredCapabilities,
            string detail,
            IEnumerable<string> candidateProviderIds)
        {
            Status = status;
            Provider = provider;
            ProbeResult = probeResult;
            RequiredCapabilities = requiredCapabilities;
            Detail = detail ?? string.Empty;
            this.candidateProviderIds = (candidateProviderIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Discovers and selects native build providers without referencing their
    /// patch-specific assemblies. Selection never silently depends on TypeCache
    /// order: one capable provider must be selected explicitly or be unique.
    /// </summary>
    public sealed class NpcNativeBuildProviderRegistry
    {
        private readonly List<INpcNativeBuildProvider> providers =
            new List<INpcNativeBuildProvider>();
        private readonly bool autoDiscoverProjectProviders;
        private bool projectProvidersDiscovered;

        public static NpcNativeBuildProviderRegistry Default { get; } =
            new NpcNativeBuildProviderRegistry(true);

        public IReadOnlyList<INpcNativeBuildProvider> Providers =>
            providers.AsReadOnly();

        public NpcNativeBuildProviderRegistry()
            : this(false)
        {
        }

        private NpcNativeBuildProviderRegistry(bool autoDiscoverProjectProviders)
        {
            this.autoDiscoverProjectProviders = autoDiscoverProjectProviders;
        }

        public void Register(INpcNativeBuildProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            if (!providers.Any(value =>
                    value != null && value.GetType() == provider.GetType()))
                providers.Add(provider);
        }

        public bool Unregister(INpcNativeBuildProvider provider)
        {
            return provider != null && providers.Remove(provider);
        }

        public int DiscoverProjectProviders()
        {
            int discovered = 0;
            IEnumerable<Type> types = TypeCache
                .GetTypesDerivedFrom<INpcNativeBuildProvider>()
                .Where(type => type != null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal);
            foreach (Type type in types)
            {
                if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters
                    || type.GetConstructor(
                        BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null) == null
                    || providers.Any(value =>
                        value != null && value.GetType() == type))
                    continue;
                try
                {
                    var provider = Activator.CreateInstance(type, true)
                        as INpcNativeBuildProvider;
                    if (provider == null)
                        continue;
                    int previousCount = providers.Count;
                    Register(provider);
                    if (providers.Count > previousCount)
                        discovered++;
                }
                catch
                {
                    // An optional provider must never prevent the toolkit UI
                    // or another provider from loading.
                }
            }
            projectProvidersDiscovered = true;
            return discovered;
        }

        public NpcNativeBuildProviderSelection Resolve(
            NpcBuildProfile buildProfile,
            NpcCompatibilityCapabilities requiredCapabilities,
            string requestedProviderId = null)
        {
            if (autoDiscoverProjectProviders && !projectProvidersDiscovered)
                DiscoverProjectProviders();

            NpcCompatibilityCapabilities unknown = requiredCapabilities
                                                   & ~NpcCompatibilityCapabilities.All;
            if (buildProfile == null || string.IsNullOrWhiteSpace(
                    buildProfile.CompatibilityProfileId)
                || requiredCapabilities == NpcCompatibilityCapabilities.None
                || unknown != NpcCompatibilityCapabilities.None)
                return Selection(
                    NpcNativeBuildProviderSelectionStatus.InvalidRequest,
                    null,
                    null,
                    requiredCapabilities,
                    "A Build Profile, compatibility profile ID, and known required capabilities are required.",
                    Array.Empty<string>());

            INpcNativeBuildProvider[] snapshot = providers
                .Where(value => value != null)
                .OrderBy(SafeProviderId, StringComparer.Ordinal)
                .ThenBy(value => value.GetType().FullName, StringComparer.Ordinal)
                .ToArray();
            if (snapshot.Length == 0)
                return Selection(
                    NpcNativeBuildProviderSelectionStatus.NoProviderRegistered,
                    null,
                    null,
                    requiredCapabilities,
                    "No native NPC build provider is registered.",
                    Array.Empty<string>());

            if (!string.IsNullOrWhiteSpace(requestedProviderId))
            {
                snapshot = snapshot.Where(value => string.Equals(
                        SafeProviderId(value),
                        requestedProviderId,
                        StringComparison.Ordinal))
                    .ToArray();
                if (snapshot.Length == 0)
                    return Selection(
                        NpcNativeBuildProviderSelectionStatus.RequestedProviderNotFound,
                        null,
                        null,
                        requiredCapabilities,
                        $"The requested native provider '{requestedProviderId}' is not registered.",
                        ProviderIds(providers));
            }

            string profileId = buildProfile.CompatibilityProfileId;
            INpcNativeBuildProvider[] matching = snapshot.Where(value =>
                    string.Equals(
                        SafeCompatibilityProfileId(value),
                        profileId,
                        StringComparison.Ordinal))
                .ToArray();
            if (matching.Length == 0)
                return Selection(
                    NpcNativeBuildProviderSelectionStatus.CompatibilityProfileMismatch,
                    null,
                    null,
                    requiredCapabilities,
                    $"No selected native build provider supports compatibility profile '{profileId}'.",
                    ProviderIds(snapshot));

            var capable = new List<ProviderCandidate>();
            var unavailableDetails = new List<string>();
            var capabilityDetails = new List<string>();
            var failureDetails = new List<string>();
            foreach (INpcNativeBuildProvider provider in matching)
            {
                string id = SafeProviderId(provider);
                if (string.IsNullOrWhiteSpace(id))
                {
                    failureDetails.Add(
                        provider.GetType().FullName + ": provider ID is blank");
                    continue;
                }
                try
                {
                    NpcCompatibilityProbeResult result = provider.Probe();
                    if (result == null)
                    {
                        failureDetails.Add(id + ": returned no probe result");
                        continue;
                    }
                    if (!result.IsAvailable)
                    {
                        unavailableDetails.Add(id + ": " + result.Detail);
                        continue;
                    }
                    if ((result.Capabilities & requiredCapabilities)
                        != requiredCapabilities)
                    {
                        NpcCompatibilityCapabilities missing = requiredCapabilities
                                                               & ~result.Capabilities;
                        capabilityDetails.Add(id + ": missing " + missing);
                        continue;
                    }
                    capable.Add(new ProviderCandidate(provider, result));
                }
                catch (Exception exception)
                {
                    failureDetails.Add(
                        id + ": " + exception.GetType().Name + ": " + exception.Message);
                }
            }

            if (capable.Count == 1)
            {
                ProviderCandidate selected = capable[0];
                return Selection(
                    NpcNativeBuildProviderSelectionStatus.Available,
                    selected.Provider,
                    selected.Result,
                    requiredCapabilities,
                    string.IsNullOrWhiteSpace(selected.Result.Detail)
                        ? "The native build provider is ready."
                        : selected.Result.Detail,
                    ProviderIds(capable.Select(value => value.Provider)));
            }
            if (capable.Count > 1)
                return Selection(
                    NpcNativeBuildProviderSelectionStatus.AmbiguousProvider,
                    null,
                    null,
                    requiredCapabilities,
                    "More than one capable native build provider matches. Select one by its exact provider ID.",
                    ProviderIds(capable.Select(value => value.Provider)));
            if (capabilityDetails.Count > 0)
                return Selection(
                    NpcNativeBuildProviderSelectionStatus.CapabilityMismatch,
                    null,
                    null,
                    requiredCapabilities,
                    string.Join("; ", capabilityDetails),
                    ProviderIds(matching));
            if (unavailableDetails.Count > 0)
                return Selection(
                    NpcNativeBuildProviderSelectionStatus.ProviderUnavailable,
                    null,
                    null,
                    requiredCapabilities,
                    string.Join("; ", unavailableDetails),
                    ProviderIds(matching));
            return Selection(
                NpcNativeBuildProviderSelectionStatus.ProbeFailed,
                null,
                null,
                requiredCapabilities,
                failureDetails.Count == 0
                    ? "Every matching native provider failed its readiness probe."
                    : string.Join("; ", failureDetails),
                ProviderIds(matching));
        }

        private static NpcNativeBuildProviderSelection Selection(
            NpcNativeBuildProviderSelectionStatus status,
            INpcNativeBuildProvider provider,
            NpcCompatibilityProbeResult result,
            NpcCompatibilityCapabilities required,
            string detail,
            IEnumerable<string> candidateIds)
        {
            return new NpcNativeBuildProviderSelection(
                status, provider, result, required, detail, candidateIds);
        }

        private static IEnumerable<string> ProviderIds(
            IEnumerable<INpcNativeBuildProvider> values)
        {
            return (values ?? Array.Empty<INpcNativeBuildProvider>())
                .Select(SafeProviderId);
        }

        private static string SafeProviderId(INpcNativeBuildProvider provider)
        {
            try
            {
                return provider?.ProviderId ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeCompatibilityProfileId(
            INpcNativeBuildProvider provider)
        {
            try
            {
                return provider?.CompatibilityProfileId ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class ProviderCandidate
        {
            public INpcNativeBuildProvider Provider { get; }
            public NpcCompatibilityProbeResult Result { get; }

            public ProviderCandidate(
                INpcNativeBuildProvider provider,
                NpcCompatibilityProbeResult result)
            {
                Provider = provider;
                Result = result;
            }
        }
    }
}
