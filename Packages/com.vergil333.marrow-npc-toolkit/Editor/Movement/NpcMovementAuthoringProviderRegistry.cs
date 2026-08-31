using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;

namespace Vergil333.MarrowNpcToolkit.Editor.Movement
{
    public enum NpcMovementAuthoringProviderSelectionStatus
    {
        InvalidRequest,
        NoProviderRegistered,
        RequestedProviderNotFound,
        CompatibilityProfileMismatch,
        ProviderUnavailable,
        ProbeFailed,
        AmbiguousProvider,
        Available,
    }

    public sealed class NpcMovementAuthoringProviderSelection
    {
        private readonly string[] candidateProviderIds;

        public NpcMovementAuthoringProviderSelectionStatus Status { get; }
        public bool CanPrepare =>
            Status == NpcMovementAuthoringProviderSelectionStatus.Available
            && Provider != null;
        public INpcMovementAuthoringProvider Provider { get; }
        public NpcCompatibilityProbeResult ProbeResult { get; }
        public string Detail { get; }
        public IReadOnlyList<string> CandidateProviderIds => candidateProviderIds;

        internal NpcMovementAuthoringProviderSelection(
            NpcMovementAuthoringProviderSelectionStatus status,
            INpcMovementAuthoringProvider provider,
            NpcCompatibilityProbeResult probeResult,
            string detail,
            IEnumerable<string> providerIds)
        {
            Status = status;
            Provider = provider;
            ProbeResult = probeResult;
            Detail = detail ?? string.Empty;
            candidateProviderIds = (providerIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Discovers optional project-local movement authoring providers without
    /// referencing their patch-specific assemblies. Selection is deterministic:
    /// exactly one available provider must match the Build Profile contract.
    /// </summary>
    public sealed class NpcMovementAuthoringProviderRegistry
    {
        private readonly List<INpcMovementAuthoringProvider> providers =
            new List<INpcMovementAuthoringProvider>();
        private readonly bool autoDiscoverProjectProviders;
        private bool projectProvidersDiscovered;

        public static NpcMovementAuthoringProviderRegistry Default { get; } =
            new NpcMovementAuthoringProviderRegistry(true);

        public IReadOnlyList<INpcMovementAuthoringProvider> Providers =>
            providers.AsReadOnly();

        public NpcMovementAuthoringProviderRegistry()
            : this(false)
        {
        }

        private NpcMovementAuthoringProviderRegistry(
            bool autoDiscoverProjectProviders)
        {
            this.autoDiscoverProjectProviders = autoDiscoverProjectProviders;
        }

        public void Register(INpcMovementAuthoringProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            if (!providers.Any(value =>
                    value != null && value.GetType() == provider.GetType()))
                providers.Add(provider);
        }

        public bool Unregister(INpcMovementAuthoringProvider provider)
        {
            return provider != null && providers.Remove(provider);
        }

        public int DiscoverProjectProviders()
        {
            int discovered = 0;
            IEnumerable<Type> types = TypeCache
                .GetTypesDerivedFrom<INpcMovementAuthoringProvider>()
                .Where(type => type != null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal);
            foreach (Type type in types)
            {
                if (type.IsAbstract || type.IsInterface
                    || type.ContainsGenericParameters
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
                        as INpcMovementAuthoringProvider;
                    if (provider == null) continue;
                    int previous = providers.Count;
                    Register(provider);
                    if (providers.Count > previous) discovered++;
                }
                catch
                {
                    // An optional provider must not prevent the public editor or
                    // another provider from loading.
                }
            }
            projectProvidersDiscovered = true;
            return discovered;
        }

        public NpcMovementAuthoringProviderSelection Resolve(
            NpcBuildProfile buildProfile,
            string requestedProviderId = null)
        {
            if (autoDiscoverProjectProviders && !projectProvidersDiscovered)
                DiscoverProjectProviders();

            if (buildProfile == null || string.IsNullOrWhiteSpace(
                    buildProfile.CompatibilityProfileId))
                return Selection(
                    NpcMovementAuthoringProviderSelectionStatus.InvalidRequest,
                    null,
                    null,
                    "A Build Profile with a compatibility profile ID is required.",
                    Array.Empty<string>());

            INpcMovementAuthoringProvider[] snapshot = providers
                .Where(value => value != null)
                .OrderBy(SafeProviderId, StringComparer.Ordinal)
                .ThenBy(value => value.GetType().FullName, StringComparer.Ordinal)
                .ToArray();
            if (snapshot.Length == 0)
                return Selection(
                    NpcMovementAuthoringProviderSelectionStatus.NoProviderRegistered,
                    null,
                    null,
                    "No movement authoring provider is registered. The public standing/balance estimate is still available.",
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
                        NpcMovementAuthoringProviderSelectionStatus.RequestedProviderNotFound,
                        null,
                        null,
                        $"The requested movement provider '{requestedProviderId}' is not registered.",
                        ProviderIds(providers));
            }

            string profileId = buildProfile.CompatibilityProfileId;
            INpcMovementAuthoringProvider[] matching = snapshot.Where(value =>
                    string.Equals(
                        SafeCompatibilityProfileId(value),
                        profileId,
                        StringComparison.Ordinal))
                .ToArray();
            if (matching.Length == 0)
                return Selection(
                    NpcMovementAuthoringProviderSelectionStatus.CompatibilityProfileMismatch,
                    null,
                    null,
                    $"No movement authoring provider supports compatibility profile '{profileId}'. The editor estimate remains available.",
                    ProviderIds(snapshot));

            var available = new List<ProviderCandidate>();
            var unavailable = new List<string>();
            var failures = new List<string>();
            foreach (INpcMovementAuthoringProvider provider in matching)
            {
                string id = SafeProviderId(provider);
                if (string.IsNullOrWhiteSpace(id))
                {
                    failures.Add(provider.GetType().FullName
                                 + ": provider ID is blank");
                    continue;
                }
                try
                {
                    NpcCompatibilityProbeResult result = provider.Probe();
                    if (result == null)
                        failures.Add(id + ": returned no probe result");
                    else if (!result.IsAvailable)
                        unavailable.Add(id + ": " + result.Detail);
                    else
                        available.Add(new ProviderCandidate(provider, result));
                }
                catch (Exception exception)
                {
                    failures.Add(id + ": " + exception.GetType().Name + ": "
                                 + exception.Message);
                }
            }

            if (available.Count == 1)
            {
                ProviderCandidate candidate = available[0];
                return Selection(
                    NpcMovementAuthoringProviderSelectionStatus.Available,
                    candidate.Provider,
                    candidate.Result,
                    string.IsNullOrWhiteSpace(candidate.Result.Detail)
                        ? candidate.Provider.DisplayName
                        : candidate.Result.Detail,
                    ProviderIds(available.Select(value => value.Provider)));
            }
            if (available.Count > 1)
                return Selection(
                    NpcMovementAuthoringProviderSelectionStatus.AmbiguousProvider,
                    null,
                    null,
                    "Multiple movement authoring providers match. Select an exact provider ID instead of relying on discovery order.",
                    ProviderIds(available.Select(value => value.Provider)));
            if (unavailable.Count > 0)
                return Selection(
                    NpcMovementAuthoringProviderSelectionStatus.ProviderUnavailable,
                    null,
                    null,
                    string.Join("\n", unavailable),
                    ProviderIds(matching));
            return Selection(
                NpcMovementAuthoringProviderSelectionStatus.ProbeFailed,
                null,
                null,
                failures.Count == 0
                    ? "The matching movement provider could not be probed."
                    : string.Join("\n", failures),
                ProviderIds(matching));
        }

        private static NpcMovementAuthoringProviderSelection Selection(
            NpcMovementAuthoringProviderSelectionStatus status,
            INpcMovementAuthoringProvider provider,
            NpcCompatibilityProbeResult probe,
            string detail,
            IEnumerable<string> ids)
        {
            return new NpcMovementAuthoringProviderSelection(
                status, provider, probe, detail, ids);
        }

        private static string SafeProviderId(INpcMovementAuthoringProvider value)
        {
            try { return value?.ProviderId ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeCompatibilityProfileId(
            INpcMovementAuthoringProvider value)
        {
            try { return value?.CompatibilityProfileId ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static IEnumerable<string> ProviderIds(
            IEnumerable<INpcMovementAuthoringProvider> values)
        {
            return values == null
                ? Array.Empty<string>()
                : values.Select(SafeProviderId);
        }

        private sealed class ProviderCandidate
        {
            public INpcMovementAuthoringProvider Provider { get; }
            public NpcCompatibilityProbeResult Result { get; }

            public ProviderCandidate(
                INpcMovementAuthoringProvider provider,
                NpcCompatibilityProbeResult result)
            {
                Provider = provider;
                Result = result;
            }
        }
    }
}
