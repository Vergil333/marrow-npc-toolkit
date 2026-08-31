using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SLZ.Marrow.Warehouse;
using SLZ.MarrowEditor;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.Editor.Build
{
    public enum NpcPalletPackStatus
    {
        InvalidRequest,
        TargetPlatformMismatch,
        SpawnablePreparationFailed,
        ProjectValidationFailed,
        BuildTargetSwitchFailed,
        PackFailed,
        OutputValidationFailed,
        BuildEnvironmentRestoreFailed,
        Succeeded,
    }

    public enum NpcPalletPackMessageSeverity
    {
        Info,
        Warning,
        Error,
    }

    public sealed class NpcPalletPackMessage
    {
        public NpcPalletPackMessageSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }

        internal NpcPalletPackMessage(
            NpcPalletPackMessageSeverity severity,
            string code,
            string message)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public sealed class NpcPalletOutputInventory
    {
        public string OutputDirectory { get; }
        public int FileCount { get; }
        public int CatalogJsonCount { get; }
        public int CatalogHashCount { get; }
        public int PalletJsonCount { get; }
        public int MonoScriptsBundleCount { get; }
        public int SpawnableBundleCount { get; }
        public int PreviewBundleCount { get; }
        public int ExpectedSpawnableBundleCount { get; }
        public bool IsComplete =>
            CatalogJsonCount == 1
            && CatalogHashCount == 1
            && PalletJsonCount == 1
            && MonoScriptsBundleCount == 1
            && ExpectedSpawnableBundleCount > 0
            && SpawnableBundleCount >= ExpectedSpawnableBundleCount;

        internal NpcPalletOutputInventory(
            string outputDirectory,
            int fileCount,
            int catalogJsonCount,
            int catalogHashCount,
            int palletJsonCount,
            int monoScriptsBundleCount,
            int spawnableBundleCount,
            int previewBundleCount,
            int expectedSpawnableBundleCount)
        {
            OutputDirectory = outputDirectory ?? string.Empty;
            FileCount = fileCount;
            CatalogJsonCount = catalogJsonCount;
            CatalogHashCount = catalogHashCount;
            PalletJsonCount = palletJsonCount;
            MonoScriptsBundleCount = monoScriptsBundleCount;
            SpawnableBundleCount = spawnableBundleCount;
            PreviewBundleCount = previewBundleCount;
            ExpectedSpawnableBundleCount = expectedSpawnableBundleCount;
        }
    }

    public sealed class NpcPalletPackReport
    {
        private readonly NpcPalletPackMessage[] messages;

        public NpcPalletPackStatus Status { get; }
        public bool Success => Status == NpcPalletPackStatus.Succeeded;
        public NpcTargetPlatform RequestedPlatform { get; }
        public BuildTarget RequiredBuildTarget { get; }
        public BuildTarget ActiveBuildTarget { get; }
        public string PalletAssetPath { get; }
        public string PalletBarcode { get; }
        public string CrateAssetPath { get; }
        public string CrateBarcode { get; }
        public string PackagingFingerprint { get; }
        public string BuildError { get; }
        public double BuildDurationSeconds { get; }
        public NpcPalletOutputInventory Output { get; }
        public NpcSpawnableCratePreparationReport Preparation { get; }
        public IReadOnlyList<NpcPalletPackMessage> Messages => messages;

        internal NpcPalletPackReport(
            NpcPalletPackStatus status,
            NpcTargetPlatform requestedPlatform,
            BuildTarget requiredBuildTarget,
            BuildTarget activeBuildTarget,
            NpcSpawnableCratePreparationReport preparation,
            string buildError,
            double buildDurationSeconds,
            NpcPalletOutputInventory output,
            IEnumerable<NpcPalletPackMessage> messages)
        {
            Status = status;
            RequestedPlatform = requestedPlatform;
            RequiredBuildTarget = requiredBuildTarget;
            ActiveBuildTarget = activeBuildTarget;
            Preparation = preparation;
            PalletAssetPath = preparation?.PalletAssetPath ?? string.Empty;
            PalletBarcode = preparation?.PalletBarcode ?? string.Empty;
            CrateAssetPath = preparation?.CrateAssetPath ?? string.Empty;
            CrateBarcode = preparation?.CrateBarcode ?? string.Empty;
            PackagingFingerprint = preparation?.PackagingFingerprint
                                   ?? string.Empty;
            BuildError = buildError ?? string.Empty;
            BuildDurationSeconds = buildDurationSeconds;
            Output = output;
            this.messages = (messages ?? Array.Empty<NpcPalletPackMessage>())
                .Where(value => value != null)
                .ToArray();
        }
    }

    /// <summary>
    /// Step 5C packs the whole GUID-bound Pallet for the selected target and
    /// checks that the official Marrow output is complete. A successful pack
    /// is packaging proof only; runtime spawn and interaction proof remain a
    /// separate step.
    /// </summary>
    public static class NpcPalletPackCoordinator
    {
        public static BuildTarget RequiredBuildTarget(
            NpcTargetPlatform platform)
        {
            return platform == NpcTargetPlatform.Quest
                ? BuildTarget.Android
                : BuildTarget.StandaloneWindows64;
        }

        internal static BuildTargetGroup RequiredBuildTargetGroup(
            NpcTargetPlatform platform)
        {
            return platform == NpcTargetPlatform.Quest
                ? BuildTargetGroup.Android
                : BuildTargetGroup.Standalone;
        }

        public static bool IsRequiredBuildTargetActive(
            NpcTargetPlatform platform)
        {
            return EditorUserBuildSettings.activeBuildTarget
                   == RequiredBuildTarget(platform);
        }

        /// <summary>
        /// Starts Unity's normal platform switch. A successful switch may
        /// trigger a domain reload, so callers must not assume their window
        /// state survives it.
        /// </summary>
        public static bool TrySwitchBuildTarget(
            NpcTargetPlatform platform,
            out string detail)
        {
            BuildTarget target = RequiredBuildTarget(platform);
            if (EditorUserBuildSettings.activeBuildTarget == target)
            {
                detail = "The required build target is already active.";
                return true;
            }

            BuildTargetGroup group = target == BuildTarget.Android
                ? BuildTargetGroup.Android
                : BuildTargetGroup.Standalone;
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                group, target);
            detail = switched
                ? "Unity switched to " + FriendlyPlatform(platform)
                  + ". Reopen Step 5C after scripts finish refreshing."
                : "Unity could not switch to " + FriendlyPlatform(platform)
                  + ". Install that platform module in Unity Hub and try again.";
            return switched;
        }

        public static NpcPalletPackReport Pack(NpcDefinition definition)
        {
            NpcBuildProfile build = definition?.BuildProfile;
            NpcTargetPlatform platform = build?.TargetPlatform
                                         ?? NpcTargetPlatform.Quest;
            BuildTarget required = RequiredBuildTarget(platform);
            BuildTargetGroup requiredGroup =
                RequiredBuildTargetGroup(platform);
            BuildTarget originalTarget =
                EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup originalGroup = BuildTargetGroupFor(
                originalTarget,
                EditorUserBuildSettings.selectedBuildTargetGroup);
            if (definition == null || build == null)
                return Report(
                    NpcPalletPackStatus.InvalidRequest,
                    platform,
                    required,
                    originalTarget,
                    null,
                    "PACK_REQUEST_INVALID",
                    "Select an NPC Definition with a Build Profile first.");

            NpcSpawnableCratePreparationReport preparation =
                NpcSpawnableCratePreparationCoordinator.Prepare(
                    new NpcSpawnableCratePreparationRequest(definition));
            if (!preparation.Success)
                return Report(
                    NpcPalletPackStatus.SpawnablePreparationFailed,
                    platform,
                    required,
                    originalTarget,
                    preparation,
                    "PACK_PREPARATION_FAILED",
                    "Step 5B could not verify the Pallet, Spawnable Crate, and native-build receipt. Resolve its message before packing.");

            Pallet pallet = AssetDatabase.LoadAssetAtPath<Pallet>(
                preparation.PalletAssetPath);
            SpawnableCrate crate =
                AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                    preparation.CrateAssetPath);
            if (pallet == null || crate == null)
                return Report(
                    NpcPalletPackStatus.SpawnablePreparationFailed,
                    platform,
                    required,
                    originalTarget,
                    preparation,
                    "PACK_BOUND_ASSET_MISSING",
                    "The GUID-bound Pallet or Spawnable Crate could not be reloaded after Step 5B.");

            var projectIssues = new List<
                MarrowProjectValidation.MarrowValidationRule>();
            bool projectValid = MarrowProjectValidation.ValidateProject();
            if (!projectValid)
                MarrowProjectValidation.GetIssues(projectIssues);
            if (!projectValid)
            {
                string details = string.Join(" ", projectIssues
                    .Select(value => value?.message)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim()));
                return Report(
                    NpcPalletPackStatus.ProjectValidationFailed,
                    platform,
                    required,
                    originalTarget,
                    preparation,
                    "MARROW_PROJECT_VALIDATION_FAILED",
                    string.IsNullOrWhiteSpace(details)
                        ? "The Marrow project settings are not ready for packing. Open the Marrow validation window and resolve its checks."
                        : "The Marrow project settings are not ready for packing: "
                          + details);
            }

            ScriptingImplementation configuredBackend =
                PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
            bool useMacWindowsMonoCrossPack = RequiresMacWindowsMonoCrossPack(
                Application.platform,
                required,
                configuredBackend);
            if (RequiresNonStandaloneStartForMacWindowsPack(
                    Application.platform,
                    required,
                    configuredBackend,
                    originalGroup))
                return Report(
                    NpcPalletPackStatus.TargetPlatformMismatch,
                    platform,
                    required,
                    originalTarget,
                    preparation,
                    "MAC_WINDOWS_CROSS_PACK_START_TARGET_UNSAFE",
                    "Windows PC packing on macOS cannot safely start while a Standalone target is active with IL2CPP. Switch Unity to Quest / Android once, then click Step 5C. The toolkit will temporarily switch to Windows, pack, return to Android, and restore IL2CPP.");

            bool switchRequired = RequiresBuildTargetSwitch(
                originalGroup,
                originalTarget,
                requiredGroup,
                required);
            NpcPalletPackReport report = null;
            var restoreFailures = new List<string>();
            try
            {
                if (useMacWindowsMonoCrossPack)
                    PlayerSettings.SetScriptingBackend(
                        BuildTargetGroup.Standalone,
                        ScriptingImplementation.Mono2x);

                if (switchRequired)
                {
                    bool switched = EditorUserBuildSettings
                        .SwitchActiveBuildTarget(requiredGroup, required);
                    if (!switched || RequiresBuildTargetSwitch(
                            CurrentBuildTargetGroup(),
                            EditorUserBuildSettings.activeBuildTarget,
                            requiredGroup,
                            required))
                        report = Report(
                            NpcPalletPackStatus.BuildTargetSwitchFailed,
                            platform,
                            required,
                            originalTarget,
                            preparation,
                            "PACK_TARGET_SWITCH_FAILED",
                            "Unity could not temporarily switch to "
                            + FriendlyPlatform(platform)
                            + ". Install that platform module in Unity Hub and try again.");
                }

                if (report == null)
                    report = PackActiveTarget(
                        pallet,
                        platform,
                        required,
                        originalTarget,
                        preparation,
                        useMacWindowsMonoCrossPack);
            }
            catch (Exception exception)
            {
                report = new NpcPalletPackReport(
                    NpcPalletPackStatus.PackFailed,
                    platform,
                    required,
                    originalTarget,
                    preparation,
                    exception.Message,
                    0d,
                    null,
                    new[]
                    {
                        Error(
                            "MARROW_PACK_EXCEPTION",
                            exception.GetType().Name + ": "
                            + exception.Message),
                    });
            }
            finally
            {
                if (RequiresBuildTargetSwitch(
                        CurrentBuildTargetGroup(),
                        EditorUserBuildSettings.activeBuildTarget,
                        originalGroup,
                        originalTarget))
                {
                    try
                    {
                        bool restored = EditorUserBuildSettings
                            .SwitchActiveBuildTarget(
                                originalGroup, originalTarget);
                        if (!restored || RequiresBuildTargetSwitch(
                                CurrentBuildTargetGroup(),
                                EditorUserBuildSettings.activeBuildTarget,
                                originalGroup,
                                originalTarget))
                            restoreFailures.Add(
                                "Unity could not return to the original build target "
                                + originalTarget + ".");
                    }
                    catch (Exception exception)
                    {
                        restoreFailures.Add(
                            "Returning to the original build target failed: "
                            + exception.Message);
                    }
                }

                if (useMacWindowsMonoCrossPack)
                {
                    if (!IsStandaloneBackendInactive(
                            CurrentBuildTargetGroup()))
                    {
                        restoreFailures.Add(
                            "The Standalone scripting backend was left on Mono because a Standalone target is still active. Return Unity to the original non-Standalone target, then restore IL2CPP in Player Settings.");
                    }
                    else
                    {
                        try
                        {
                            PlayerSettings.SetScriptingBackend(
                                BuildTargetGroup.Standalone,
                                configuredBackend);
                            ScriptingImplementation restoredBackend =
                                PlayerSettings.GetScriptingBackend(
                                    BuildTargetGroup.Standalone);
                            if (!IsScriptingBackendRestored(
                                    restoredBackend,
                                    configuredBackend))
                                restoreFailures.Add(
                                    "Unity did not restore the original Standalone scripting backend "
                                    + configuredBackend + "; it remains "
                                    + restoredBackend + ".");
                        }
                        catch (Exception exception)
                        {
                            restoreFailures.Add(
                                "Restoring the Standalone scripting backend failed: "
                                + exception.Message);
                        }
                    }
                }
            }

            if (restoreFailures.Count > 0)
                return WithEnvironmentRestoreFailure(
                    report,
                    platform,
                    required,
                    originalTarget,
                    preparation,
                    restoreFailures);

            if (report != null && report.Success
                               && (switchRequired
                                   || useMacWindowsMonoCrossPack))
                report = WithAdditionalMessage(
                    report,
                    Info(
                        "PACK_ENVIRONMENT_RESTORED",
                        "Unity returned to " + originalTarget
                        + " and restored the project settings used before packing."));
            return report;
        }

        internal static bool RequiresMacWindowsMonoCrossPack(
            RuntimePlatform editorPlatform,
            BuildTarget target,
            ScriptingImplementation configuredBackend)
        {
            return editorPlatform == RuntimePlatform.OSXEditor
                   && target == BuildTarget.StandaloneWindows64
                   && configuredBackend == ScriptingImplementation.IL2CPP;
        }

        internal static bool RequiresNonStandaloneStartForMacWindowsPack(
            RuntimePlatform editorPlatform,
            BuildTarget target,
            ScriptingImplementation configuredBackend,
            BuildTargetGroup originalGroup)
        {
            return RequiresMacWindowsMonoCrossPack(
                       editorPlatform, target, configuredBackend)
                   && originalGroup == BuildTargetGroup.Standalone;
        }

        internal static bool RequiresBuildTargetSwitch(
            BuildTargetGroup activeGroup,
            BuildTarget activeTarget,
            BuildTargetGroup requiredGroup,
            BuildTarget requiredTarget)
        {
            return activeGroup != requiredGroup
                   || activeTarget != requiredTarget;
        }

        internal static bool IsStandaloneBackendInactive(
            BuildTargetGroup activeGroup)
        {
            return activeGroup != BuildTargetGroup.Standalone;
        }

        internal static bool IsScriptingBackendRestored(
            ScriptingImplementation actual,
            ScriptingImplementation expected)
        {
            return actual == expected;
        }

        internal static BuildTargetGroup CurrentBuildTargetGroup()
        {
            return BuildTargetGroupFor(
                EditorUserBuildSettings.activeBuildTarget,
                EditorUserBuildSettings.selectedBuildTargetGroup);
        }

        private static BuildTargetGroup BuildTargetGroupFor(
            BuildTarget target,
            BuildTargetGroup fallback)
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            return group == BuildTargetGroup.Unknown ? fallback : group;
        }

        private static NpcPalletPackReport PackActiveTarget(
            Pallet pallet,
            NpcTargetPlatform platform,
            BuildTarget required,
            BuildTarget originalTarget,
            NpcSpawnableCratePreparationReport preparation,
            bool useMacWindowsMonoCrossPack)
        {
            RestoreBacklinks(pallet);
            bool packed = PalletPackerEditor.PackPallet(
                pallet,
                out var buildResult,
                dedupe: false,
                checkValidation: false);
            string buildError = buildResult?.Error ?? string.Empty;
            double duration = buildResult?.Duration ?? 0d;
            if (!packed || buildResult == null
                        || !string.IsNullOrWhiteSpace(buildError))
                return new NpcPalletPackReport(
                    NpcPalletPackStatus.PackFailed,
                    platform,
                    required,
                    originalTarget,
                    preparation,
                    buildError,
                    duration,
                    null,
                    new[]
                    {
                        Error(
                            "MARROW_PACK_FAILED",
                            buildResult == null
                                ? "The official Marrow packer returned no build result."
                                : string.IsNullOrWhiteSpace(buildError)
                                ? "The official Marrow packer did not report success."
                                : "The official Marrow packer reported: "
                                  + buildError),
                    });

            string outputDirectory = GetOutputDirectory(pallet);
            int expectedSpawnables = pallet.Crates
                .OfType<SpawnableCrate>()
                .Count();
            NpcPalletOutputInventory output = InspectOutput(
                outputDirectory, expectedSpawnables);
            if (!output.IsComplete)
                return new NpcPalletPackReport(
                    NpcPalletPackStatus.OutputValidationFailed,
                    platform,
                    required,
                    originalTarget,
                    preparation,
                    buildError,
                    duration,
                    output,
                    new[]
                    {
                        Error(
                            "PALLET_OUTPUT_INCOMPLETE",
                            DescribeOutput(output)
                            + " The pack was not accepted as complete."),
                    });

            var messages = new List<NpcPalletPackMessage>
            {
                Info(
                    "PALLET_PACK_SUCCEEDED",
                    "Packed the complete Pallet for "
                    + FriendlyPlatform(platform) + ". "
                    + DescribeOutput(output)),
                Warning(
                    "RUNTIME_PROOF_PENDING",
                    "Packing passed, but this does not prove the NPC spawns or behaves correctly in BONELAB. Continue with the runtime test checklist."),
            };
            if (useMacWindowsMonoCrossPack)
                messages.Add(Info(
                    "MAC_WINDOWS_CROSS_PACK_PROFILE",
                    "Unity used its installed Windows Mono cross-build profile for this Pallet content. The output is Windows PC content; BONELAB runtime proof is still separate."));
            if (output.PreviewBundleCount < expectedSpawnables)
                messages.Add(Warning(
                    "PREVIEW_BUNDLES_FEWER_THAN_SPAWNABLES",
                    "The Pallet packed, but fewer preview bundles than Spawnable Crates were found. Check spawn-gun thumbnails before release."));
            return new NpcPalletPackReport(
                NpcPalletPackStatus.Succeeded,
                platform,
                required,
                originalTarget,
                preparation,
                buildError,
                duration,
                output,
                messages);
        }

        private static NpcPalletPackReport WithAdditionalMessage(
            NpcPalletPackReport report,
            NpcPalletPackMessage message)
        {
            return new NpcPalletPackReport(
                report.Status,
                report.RequestedPlatform,
                report.RequiredBuildTarget,
                report.ActiveBuildTarget,
                report.Preparation,
                report.BuildError,
                report.BuildDurationSeconds,
                report.Output,
                report.Messages.Concat(new[] { message }));
        }

        private static NpcPalletPackReport WithEnvironmentRestoreFailure(
            NpcPalletPackReport report,
            NpcTargetPlatform platform,
            BuildTarget required,
            BuildTarget originalTarget,
            NpcSpawnableCratePreparationReport preparation,
            IEnumerable<string> failures)
        {
            string detail = string.Join(" ", (failures ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));
            var messages = report?.Messages.ToList()
                           ?? new List<NpcPalletPackMessage>();
            messages.Add(Error(
                "BUILD_ENVIRONMENT_RESTORE_FAILED",
                detail + " The Pallet result is not accepted until Unity's original build environment is restored."));
            string buildError = string.Join(" ", new[]
                {
                    report?.BuildError,
                    detail,
                }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return new NpcPalletPackReport(
                NpcPalletPackStatus.BuildEnvironmentRestoreFailed,
                report?.RequestedPlatform ?? platform,
                report?.RequiredBuildTarget ?? required,
                report?.ActiveBuildTarget ?? originalTarget,
                report?.Preparation ?? preparation,
                buildError,
                report?.BuildDurationSeconds ?? 0d,
                report?.Output,
                messages);
        }

        internal static NpcPalletOutputInventory InspectOutput(
            string outputDirectory,
            int expectedSpawnableBundles)
        {
            string normalized = NormalizePath(outputDirectory);
            string[] files = Directory.Exists(normalized)
                ? Directory.GetFiles(normalized, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();
            return new NpcPalletOutputInventory(
                normalized,
                files.Length,
                files.Count(path => FileName(path).StartsWith(
                    "catalog_", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)),
                files.Count(path => FileName(path).StartsWith(
                    "catalog_", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase)),
                files.Count(path => path.EndsWith(
                    ".pallet.json", StringComparison.OrdinalIgnoreCase)),
                files.Count(path => path.EndsWith(
                    "_monoscripts.bundle", StringComparison.OrdinalIgnoreCase)),
                files.Count(path => path.EndsWith(
                    ".bundle", StringComparison.OrdinalIgnoreCase)
                    && NormalizePath(path).IndexOf(
                        "_spawnables_assets_spawnable/",
                        StringComparison.OrdinalIgnoreCase) >= 0),
                files.Count(path => path.EndsWith(
                    ".bundle", StringComparison.OrdinalIgnoreCase)
                    && NormalizePath(path).IndexOf(
                        "_spawnables_assets_previewmesh/",
                        StringComparison.OrdinalIgnoreCase) >= 0),
                Math.Max(0, expectedSpawnableBundles));
        }

        internal static string GetOutputDirectory(Pallet pallet)
        {
            if (pallet == null) return string.Empty;
            string evaluated = AddressablesManager.GetBuiltModFolder(pallet);
            if (string.IsNullOrWhiteSpace(evaluated)) return string.Empty;
            if (Path.IsPathRooted(evaluated))
                return NormalizePath(Path.GetFullPath(evaluated));
            DirectoryInfo project = Directory.GetParent(Application.dataPath);
            if (project == null) return string.Empty;
            return NormalizePath(Path.GetFullPath(Path.Combine(
                project.FullName, evaluated)));
        }

        private static void RestoreBacklinks(Pallet pallet)
        {
            if (pallet?.Crates == null) return;
            foreach (Crate value in pallet.Crates)
                if (value != null) value.Pallet = pallet;
        }

        private static string DescribeOutput(NpcPalletOutputInventory output)
        {
            if (output == null) return "No output inventory was available.";
            return output.FileCount + " files; catalog "
                   + output.CatalogJsonCount + "/1; catalog hash "
                   + output.CatalogHashCount + "/1; pallet JSON "
                   + output.PalletJsonCount + "/1; MonoScripts "
                   + output.MonoScriptsBundleCount + "/1; Spawnable bundles "
                   + output.SpawnableBundleCount + "/"
                   + output.ExpectedSpawnableBundleCount + "; preview bundles "
                   + output.PreviewBundleCount + ".";
        }

        private static NpcPalletPackReport Report(
            NpcPalletPackStatus status,
            NpcTargetPlatform platform,
            BuildTarget required,
            BuildTarget active,
            NpcSpawnableCratePreparationReport preparation,
            string code,
            string message)
        {
            return new NpcPalletPackReport(
                status,
                platform,
                required,
                active,
                preparation,
                string.Empty,
                0d,
                null,
                new[] { Error(code, message) });
        }

        private static NpcPalletPackMessage Info(
            string code,
            string message)
        {
            return new NpcPalletPackMessage(
                NpcPalletPackMessageSeverity.Info, code, message);
        }

        private static NpcPalletPackMessage Warning(
            string code,
            string message)
        {
            return new NpcPalletPackMessage(
                NpcPalletPackMessageSeverity.Warning, code, message);
        }

        private static NpcPalletPackMessage Error(
            string code,
            string message)
        {
            return new NpcPalletPackMessage(
                NpcPalletPackMessageSeverity.Error, code, message);
        }

        private static string FriendlyPlatform(NpcTargetPlatform platform)
        {
            return platform == NpcTargetPlatform.Quest
                ? "Quest / Android"
                : "Windows PC";
        }

        private static string FileName(string path)
        {
            return Path.GetFileName(path) ?? string.Empty;
        }

        private static string NormalizePath(string value)
        {
            return (value ?? string.Empty).Replace('\\', '/').Trim();
        }
    }
}
