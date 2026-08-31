using System;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Build;

namespace Vergil333.MarrowNpcToolkit.Editor.Movement
{
    /// <summary>
    /// One read-only currentness check shared by the guided Step 3D UI and the
    /// Step 4 readiness doctor. Object references alone are insufficient: the
    /// selected provider must recompute the same recipe fingerprint.
    /// </summary>
    public static class NpcMovementRecipeValidator
    {
        public static NpcMovementRecipeValidationReport Validate(
            NpcDefinition definition,
            NpcMovementProfile profile,
            NpcMovementAuthoringProviderRegistry registry = null)
        {
            if (definition == null || profile == null)
                return NpcMovementRecipeValidationReport.Stale(
                    "The NPC Definition or Movement Profile is missing.");
            if (profile.ProviderStandingPose == null
                || profile.ProviderMovementConfig == null
                || string.IsNullOrWhiteSpace(
                    profile.ProviderRecipeFingerprint))
                return NpcMovementRecipeValidationReport.Stale(
                    "Run Step 3D Recalculate Movement for This Avatar to prepare the native standing pose and movement settings.");

            NpcNativeBuildInputGuard guard =
                NpcNativeBuildInputGuard.Capture(definition, null);

            try
            {
                registry = registry
                           ?? NpcMovementAuthoringProviderRegistry.Default;
                NpcMovementAuthoringProviderSelection selection =
                    registry.Resolve(definition.BuildProfile);
                if (!selection.CanPrepare)
                    return Guarded(
                        guard,
                        NpcMovementRecipeValidationReport.Stale(
                            "Compatible native movement preparation is unavailable for the selected build target. Check the installed BONELAB compatibility support, then refresh Step 3D."));

                NpcMovementAuthoringValidationResult validation =
                    selection.Provider.Validate(definition, profile);
                if (validation == null)
                    return Guarded(
                        guard,
                        NpcMovementRecipeValidationReport.Stale(
                            "The movement provider returned no validation result."));
                if (!validation.IsCurrent)
                    return Guarded(
                        guard,
                        NpcMovementRecipeValidationReport.Stale(
                            validation.Messages.Count == 0
                                ? "The native movement setup is out of date. Refresh Step 3D."
                                : string.Join("\n", validation.Messages)));
                if (!string.Equals(
                        validation.RecipeFingerprint,
                        profile.ProviderRecipeFingerprint,
                        StringComparison.Ordinal))
                    return Guarded(
                        guard,
                        NpcMovementRecipeValidationReport.Stale(
                            "Native movement inputs changed after preparation. Refresh Step 3D."));
                return Guarded(
                    guard,
                    NpcMovementRecipeValidationReport.Current(
                        validation.RecipeFingerprint,
                        validation.Messages.Count == 0
                            ? string.Empty
                            : string.Join("\n", validation.Messages)));
            }
            catch (Exception exception)
            {
                return Guarded(
                    guard,
                    NpcMovementRecipeValidationReport.Stale(
                        "The movement provider could not validate its setup: "
                        + exception.Message));
            }
        }

        private static NpcMovementRecipeValidationReport Guarded(
            NpcNativeBuildInputGuard guard,
            NpcMovementRecipeValidationReport report)
        {
            string mutation = guard?.FindMutation() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(mutation))
                return report;

            string rollbackError = guard.RestoreInputs();
            return NpcMovementRecipeValidationReport.Stale(
                "The movement provider violated its read-only validation contract. "
                + mutation
                + (string.IsNullOrWhiteSpace(rollbackError)
                    ? string.Empty
                    : " Rollback was incomplete: " + rollbackError));
        }
    }

    public sealed class NpcMovementRecipeValidationReport
    {
        public bool IsCurrent { get; }
        public string Fingerprint { get; }
        public string Detail { get; }

        private NpcMovementRecipeValidationReport(
            bool isCurrent,
            string fingerprint,
            string detail)
        {
            IsCurrent = isCurrent;
            Fingerprint = fingerprint ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        internal static NpcMovementRecipeValidationReport Current(
            string fingerprint,
            string detail)
        {
            return new NpcMovementRecipeValidationReport(
                true, fingerprint, detail);
        }

        internal static NpcMovementRecipeValidationReport Stale(string detail)
        {
            return new NpcMovementRecipeValidationReport(
                false, string.Empty, detail);
        }
    }
}
