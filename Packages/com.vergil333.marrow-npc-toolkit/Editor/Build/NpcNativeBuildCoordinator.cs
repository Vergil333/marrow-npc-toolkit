using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Validation;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor.Build
{
    public sealed class NpcNativeBuildRequest
    {
        public NpcDefinition Definition { get; }
        public NpcCompatibilityCapabilities RequiredCapabilities { get; }
        public string RequestedProviderId { get; }
        public string OutputPrefabPath { get; }

        public NpcNativeBuildRequest(
            NpcDefinition definition,
            NpcCompatibilityCapabilities requiredCapabilities =
                NpcCompatibilityCapabilities.CoreAnatomy,
            string requestedProviderId = null,
            string outputPrefabPath = null)
        {
            Definition = definition;
            RequiredCapabilities = requiredCapabilities;
            RequestedProviderId = requestedProviderId ?? string.Empty;
            OutputPrefabPath = outputPrefabPath ?? string.Empty;
        }
    }

    public enum NpcNativeBuildStatus
    {
        InvalidRequest,
        PhysicsNotReady,
        ProviderNotReady,
        ProviderFailed,
        InputMutationDetected,
        DeterminismFailed,
        StagingFailed,
        CommitFailed,
        Succeeded,
    }

    public sealed class NpcNativeBuildReport
    {
        private readonly NpcNativeBuildMessage[] messages;

        public NpcNativeBuildStatus Status { get; }
        public bool Success => Status == NpcNativeBuildStatus.Succeeded;
        public string OutputPrefabPath { get; }
        public string InputFingerprint { get; }
        public string OutputFingerprint { get; }
        public string ProviderId { get; }
        public string ReceiptAssetPath { get; }
        public bool ReplacedExistingOutput { get; }
        public bool PreviousOutputPreserved { get; }
        public NpcBuildReadinessReport Readiness { get; }
        public NpcNativeBuildProviderSelection ProviderSelection { get; }
        public IReadOnlyList<NpcNativeBuildMessage> Messages => messages;

        internal NpcNativeBuildReport(
            NpcNativeBuildStatus status,
            string outputPrefabPath,
            string inputFingerprint,
            string outputFingerprint,
            string providerId,
            bool replacedExistingOutput,
            bool previousOutputPreserved,
            NpcBuildReadinessReport readiness,
            NpcNativeBuildProviderSelection providerSelection,
            IEnumerable<NpcNativeBuildMessage> messages)
        {
            Status = status;
            OutputPrefabPath = outputPrefabPath ?? string.Empty;
            InputFingerprint = inputFingerprint ?? string.Empty;
            OutputFingerprint = outputFingerprint ?? string.Empty;
            ProviderId = providerId ?? string.Empty;
            ReceiptAssetPath = status == NpcNativeBuildStatus.Succeeded
                ? NpcNativeBuildReceiptUtility.GetReceiptPath(OutputPrefabPath)
                : string.Empty;
            ReplacedExistingOutput = replacedExistingOutput;
            PreviousOutputPreserved = previousOutputPreserved;
            Readiness = readiness;
            ProviderSelection = providerSelection;
            this.messages = (messages ?? Array.Empty<NpcNativeBuildMessage>())
                .Where(value => value != null)
                .ToArray();
        }
    }

    /// <summary>
    /// The package-owned transaction around patch-specific prefab configuration.
    /// It validates first, generates twice in isolated preview scenes, compares
    /// semantic receipts, and only then commits one prefab. A failed rebuild
    /// restores the previous prefab bytes while preserving its .meta/GUID.
    /// </summary>
    public static class NpcNativeBuildCoordinator
    {
        public static NpcNativeBuildReport Build(
            NpcNativeBuildRequest request,
            NpcNativeBuildProviderRegistry registry = null)
        {
            var messages = new List<NpcNativeBuildMessage>();
            if (request == null || request.Definition == null)
                return Report(
                    NpcNativeBuildStatus.InvalidRequest,
                    request,
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    false,
                    true,
                    Error("BUILD_REQUEST_INVALID", "Select an NPC Definition first."));

            NpcDefinition definition = request.Definition;
            string outputPath = string.IsNullOrWhiteSpace(request.OutputPrefabPath)
                ? GetDefaultOutputPath(definition)
                : NormalizeAssetPath(request.OutputPrefabPath);
            string previewPath = NpcPhysicsPreviewBuilder.GetOutputPath(definition);
            if (!IsValidOutputPath(outputPath)
                || string.Equals(
                    outputPath,
                    AssetDatabase.GetAssetPath(definition.SourceAvatar),
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    outputPath,
                    previewPath,
                    StringComparison.OrdinalIgnoreCase))
                return Report(
                    NpcNativeBuildStatus.InvalidRequest,
                    request,
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    false,
                    true,
                    Error(
                        "OUTPUT_PATH_INVALID",
                        "The native output must be a separate .prefab path under Assets/."),
                    outputPath);
            PrefabStage openStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (openStage != null && string.Equals(
                    openStage.assetPath,
                    outputPath,
                    StringComparison.OrdinalIgnoreCase))
                return Report(
                    NpcNativeBuildStatus.InvalidRequest,
                    request,
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    false,
                    true,
                    Error(
                        "OUTPUT_PREFAB_OPEN",
                        "Close the existing native output prefab before rebuilding it."),
                    outputPath);

            NpcBuildReadinessReport readiness =
                NpcBuildReadinessDoctor.Validate(definition);
            if (!readiness.ReadyForBuild)
                return Report(
                    NpcNativeBuildStatus.PhysicsNotReady,
                    request,
                    readiness.Fingerprint,
                    string.Empty,
                    readiness,
                    null,
                    false,
                    true,
                    Error(
                        "PHYSICS_NOT_READY",
                        $"Step 4 has {readiness.ErrorCount} blocking physics error(s)."),
                    outputPath);

            registry = registry ?? NpcNativeBuildProviderRegistry.Default;
            NpcNativeBuildProviderSelection selection = registry.Resolve(
                definition.BuildProfile,
                request.RequiredCapabilities,
                request.RequestedProviderId);
            if (!selection.CanBuild)
                return Report(
                    NpcNativeBuildStatus.ProviderNotReady,
                    request,
                    readiness.Fingerprint,
                    string.Empty,
                    readiness,
                    selection,
                    false,
                    true,
                    Error("PROVIDER_NOT_READY", selection.Detail),
                    outputPath);

            string inputFingerprint = ComputeNativeInputFingerprint(
                definition,
                readiness.Fingerprint,
                SafeProviderId(selection.Provider),
                request.RequiredCapabilities);
            GameObject preview = AssetDatabase.LoadAssetAtPath<GameObject>(previewPath);
            if (preview == null)
                return Report(
                    NpcNativeBuildStatus.PhysicsNotReady,
                    request,
                    inputFingerprint,
                    string.Empty,
                    readiness,
                    selection,
                    false,
                    true,
                    Error("PHYSICS_PREVIEW_MISSING", "Generate the Physics Preview first."),
                    outputPath);

            var guard = NpcNativeBuildInputGuard.Capture(definition, preview);
            string stageRoot = StageRoot(outputPath);
            string passOnePath = stageRoot + "/Pass1/" + Path.GetFileName(outputPath);
            string passTwoPath = stageRoot + "/Pass2/" + Path.GetFileName(outputPath);
            bool outputExisted = AssetDatabase.LoadAssetAtPath<Object>(outputPath) != null;
            PassReceipt passOne = null;
            PassReceipt passTwo = null;
            try
            {
                passOne = BuildPass(
                    selection.Provider,
                    definition,
                    preview,
                    request.RequiredCapabilities,
                    inputFingerprint,
                    1,
                    passOnePath);
                Add(messages, passOne.Messages);
                string mutation = guard.FindMutation();
                if (!string.IsNullOrEmpty(mutation))
                {
                    guard.RestoreScriptableObjects();
                    messages.Add(Error("INPUT_MUTATION_DETECTED", mutation));
                    return Report(
                        NpcNativeBuildStatus.InputMutationDetected,
                        request,
                        inputFingerprint,
                        string.Empty,
                        readiness,
                        selection,
                        false,
                        true,
                        messages,
                        outputPath);
                }
                if (!passOne.Success)
                    return Report(
                        NpcNativeBuildStatus.ProviderFailed,
                        request,
                        inputFingerprint,
                        string.Empty,
                        readiness,
                        selection,
                        false,
                        true,
                        messages,
                        outputPath);

                passTwo = BuildPass(
                    selection.Provider,
                    definition,
                    preview,
                    request.RequiredCapabilities,
                    inputFingerprint,
                    2,
                    passTwoPath);
                Add(messages, passTwo.Messages);
                mutation = guard.FindMutation();
                if (!string.IsNullOrEmpty(mutation))
                {
                    guard.RestoreScriptableObjects();
                    messages.Add(Error("INPUT_MUTATION_DETECTED", mutation));
                    return Report(
                        NpcNativeBuildStatus.InputMutationDetected,
                        request,
                        inputFingerprint,
                        string.Empty,
                        readiness,
                        selection,
                        false,
                        true,
                        messages,
                        outputPath);
                }
                if (!passTwo.Success)
                    return Report(
                        NpcNativeBuildStatus.ProviderFailed,
                        request,
                        inputFingerprint,
                        string.Empty,
                        readiness,
                        selection,
                        false,
                        true,
                        messages,
                        outputPath);

                if (!string.Equals(
                        passOne.OutputFingerprint,
                        passTwo.OutputFingerprint,
                        StringComparison.Ordinal))
                {
                    messages.Add(Error(
                        "NONDETERMINISTIC_PROVIDER_OUTPUT",
                        "Two isolated native generation passes produced different structural fingerprints. Nothing was committed."));
                    return Report(
                        NpcNativeBuildStatus.DeterminismFailed,
                        request,
                        inputFingerprint,
                        string.Empty,
                        readiness,
                        selection,
                        false,
                        true,
                        messages,
                        outputPath);
                }

                PrefabCommitResult commit = CommitPrefabAndReceipt(
                    passOnePath,
                    outputPath,
                    definition,
                    readiness.Fingerprint,
                    inputFingerprint,
                    SafeProviderId(selection.Provider),
                    request.RequiredCapabilities,
                    passOne.ProviderFingerprint,
                    passOne.OutputFingerprint);
                if (!commit.Success)
                {
                    messages.Add(Error("PREFAB_COMMIT_FAILED", commit.Detail));
                    return Report(
                        NpcNativeBuildStatus.CommitFailed,
                        request,
                        inputFingerprint,
                        passOne.OutputFingerprint,
                        readiness,
                        selection,
                        false,
                        commit.PreviousOutputPreserved,
                        messages,
                        outputPath);
                }

                messages.Add(new NpcNativeBuildMessage(
                    NpcNativeBuildMessageSeverity.Info,
                    "NATIVE_PREFAB_GENERATED",
                    outputExisted
                        ? "Rebuilt the native prefab and preserved its existing asset GUID."
                        : "Generated the native prefab as a new asset."));
                messages.Add(new NpcNativeBuildMessage(
                    NpcNativeBuildMessageSeverity.Info,
                    "NATIVE_BUILD_RECEIPT_WRITTEN",
                    "Recorded the verified native build beside the prefab at '"
                    + NpcNativeBuildReceiptUtility.GetReceiptPath(outputPath) + "'."));
                return Report(
                    NpcNativeBuildStatus.Succeeded,
                    request,
                    inputFingerprint,
                    passOne.OutputFingerprint,
                    readiness,
                    selection,
                    outputExisted,
                    true,
                    messages,
                    outputPath);
            }
            catch (Exception exception)
            {
                string mutation = guard.FindMutation();
                if (!string.IsNullOrEmpty(mutation))
                {
                    guard.RestoreScriptableObjects();
                    messages.Add(Error("INPUT_MUTATION_DETECTED", mutation));
                    return Report(
                        NpcNativeBuildStatus.InputMutationDetected,
                        request,
                        inputFingerprint,
                        string.Empty,
                        readiness,
                        selection,
                        false,
                        true,
                        messages,
                        outputPath);
                }
                messages.Add(Error(
                    "NATIVE_BUILD_EXCEPTION",
                    exception.GetType().Name + ": " + exception.Message));
                return Report(
                    NpcNativeBuildStatus.StagingFailed,
                    request,
                    inputFingerprint,
                    string.Empty,
                    readiness,
                    selection,
                    false,
                    true,
                    messages,
                    outputPath);
            }
            finally
            {
                DeleteStageRoot(stageRoot);
            }
        }

        public static string GetDefaultOutputPath(NpcDefinition definition)
        {
            if (definition == null || definition.BuildProfile == null
                                   || definition.SourceAvatar == null
                                   || string.IsNullOrWhiteSpace(
                                       definition.BuildProfile.GeneratedAssetFolder))
                return string.Empty;
            string folder = NormalizeAssetPath(
                definition.BuildProfile.GeneratedAssetFolder).TrimEnd('/');
            return folder + "/Native/" + SafeAssetName(definition.SourceAvatar.name)
                   + "Npc.prefab";
        }

        /// <summary>
        /// Computes the provider-specific native input identity from the
        /// definition/readiness fingerprint. Publication version and target
        /// platform belong to the separate packaging fingerprint and are not
        /// added here.
        /// </summary>
        public static string ComputeNativeInputFingerprint(
            NpcDefinition definition,
            string definitionFingerprint,
            string providerId,
            NpcCompatibilityCapabilities requiredCapabilities)
        {
            string compatibilityProfileId = definition?.BuildProfile == null
                ? string.Empty
                : definition.BuildProfile.CompatibilityProfileId ?? string.Empty;
            return Hash128.Compute(
                (definitionFingerprint ?? string.Empty) + "|"
                + (providerId ?? string.Empty) + "|"
                + requiredCapabilities + "|"
                + compatibilityProfileId).ToString();
        }

        private static PassReceipt BuildPass(
            INpcNativeBuildProvider provider,
            NpcDefinition definition,
            GameObject preview,
            NpcCompatibilityCapabilities requiredCapabilities,
            string inputFingerprint,
            int passNumber,
            string stagingPath)
        {
            var messages = new List<NpcNativeBuildMessage>();
            Scene previewScene = default;
            GameObject outputRoot = null;
            try
            {
                EnsureAssetFolder(
                    Path.GetDirectoryName(stagingPath)?.Replace('\\', '/'));
                previewScene = EditorSceneManager.NewPreviewScene();
                outputRoot = PrefabUtility.InstantiatePrefab(preview, previewScene)
                    as GameObject;
                if (outputRoot == null)
                    return PassReceipt.Failed(Error(
                        "PREVIEW_INSTANTIATION_FAILED",
                        "Unity could not instantiate the validated Physics Preview."));
                PrefabUtility.UnpackPrefabInstance(
                    outputRoot,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);

                var context = new NpcNativeBuildContext(
                    definition,
                    outputRoot,
                    requiredCapabilities,
                    inputFingerprint,
                    passNumber);
                if (context.AnimationRoot == null || context.PhysicsRoot == null)
                    return PassReceipt.Failed(Error(
                        "PREVIEW_ROOTS_MISSING",
                        "The staged prefab does not contain direct AnimationRoot and Physics siblings."));

                NpcNativeBuildProviderResult result;
                try
                {
                    result = provider.ConfigureStagedPrefab(context);
                }
                catch (Exception exception)
                {
                    return PassReceipt.Failed(Error(
                        "PROVIDER_BUILD_EXCEPTION",
                        exception.GetType().Name + ": " + exception.Message));
                }
                if (result == null)
                    return PassReceipt.Failed(Error(
                        "PROVIDER_RESULT_MISSING",
                        "The native provider returned no build result."));
                Add(messages, result.Messages);
                if (!result.Success)
                {
                    if (!messages.Any(value =>
                            value.Severity == NpcNativeBuildMessageSeverity.Error))
                        messages.Add(Error(
                            "PROVIDER_BUILD_FAILED",
                            "The native provider did not complete this generation pass."));
                    return PassReceipt.Failed(messages);
                }

                string coreSnapshotBeforeSave = CreateCoreSnapshot(outputRoot);
                string coreFingerprintBeforeSave = Hash128.Compute(
                    coreSnapshotBeforeSave).ToString();
                bool saved;
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(
                    outputRoot,
                    stagingPath,
                    out saved);
                if (!saved || savedPrefab == null)
                    return PassReceipt.Failed(Error(
                        "STAGED_PREFAB_SAVE_FAILED",
                        "Unity did not save the isolated native prefab pass."));
                AssetDatabase.ImportAsset(
                    stagingPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                GameObject reloadedPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(stagingPath);
                if (reloadedPrefab == null)
                    return PassReceipt.Failed(Error(
                        "STAGED_PREFAB_RELOAD_FAILED",
                        "Unity could not reload the saved native prefab pass."));

                var validationContext = new NpcNativeBuildValidationContext(
                    definition,
                    reloadedPrefab,
                    requiredCapabilities,
                    inputFingerprint,
                    stagingPath);
                if (validationContext.AnimationRoot == null
                    || validationContext.PhysicsRoot == null)
                    return PassReceipt.Failed(Error(
                        "SAVED_PREFAB_ROOTS_MISSING",
                        "The saved native prefab does not contain direct AnimationRoot and Physics siblings."));

                string coreSnapshotBeforeValidation =
                    CreateCoreSnapshot(reloadedPrefab);
                string coreFingerprintBeforeValidation = Hash128.Compute(
                    coreSnapshotBeforeValidation).ToString();
                if (!string.Equals(
                        coreFingerprintBeforeSave,
                        coreFingerprintBeforeValidation,
                        StringComparison.Ordinal))
                {
                    messages.Add(Error(
                        "STAGED_PREFAB_ROUNDTRIP_CHANGED",
                        "Unity changed the staged native prefab while saving and "
                        + "reloading it. Nothing was committed. First difference: "
                        + DescribeFirstDifference(
                            coreSnapshotBeforeSave,
                            coreSnapshotBeforeValidation)));
                    return PassReceipt.Failed(messages);
                }
                NpcNativeBuildProviderResult validationResult;
                try
                {
                    validationResult = provider.ValidateSavedPrefab(
                        validationContext);
                }
                catch (Exception exception)
                {
                    return PassReceipt.Failed(Error(
                        "PROVIDER_VALIDATION_EXCEPTION",
                        exception.GetType().Name + ": " + exception.Message));
                }
                if (validationResult == null)
                    return PassReceipt.Failed(Error(
                        "PROVIDER_VALIDATION_RESULT_MISSING",
                        "The native provider returned no saved-prefab validation result."));
                Add(messages, validationResult.Messages);
                if (!validationResult.Success)
                {
                    if (!messages.Any(value =>
                            value.Severity == NpcNativeBuildMessageSeverity.Error))
                        messages.Add(Error(
                            "PROVIDER_VALIDATION_FAILED",
                            "The native provider did not validate the saved prefab."));
                    return PassReceipt.Failed(messages);
                }
                if (!string.Equals(
                        result.StructuralFingerprint,
                        validationResult.StructuralFingerprint,
                        StringComparison.Ordinal))
                {
                    messages.Add(Error(
                        "PROVIDER_POST_SAVE_FINGERPRINT_MISMATCH",
                        "The native provider reported a different structural "
                        + "fingerprint after Unity saved and reloaded the prefab. "
                        + "Nothing was committed. First difference: "
                        + DescribeFirstDifference(
                            result.StructuralFingerprint,
                            validationResult.StructuralFingerprint)));
                    return PassReceipt.Failed(messages);
                }
                string coreFingerprintAfterValidation =
                    ComputeCoreFingerprint(reloadedPrefab);
                if (!string.Equals(
                        coreFingerprintBeforeValidation,
                        coreFingerprintAfterValidation,
                        StringComparison.Ordinal))
                {
                    messages.Add(Error(
                        "PROVIDER_VALIDATION_MUTATED_OUTPUT",
                        "The native provider changed the saved prefab while validating it."));
                    return PassReceipt.Failed(messages);
                }

                string outputFingerprint = Hash128.Compute(
                    inputFingerprint + "|"
                    + validationResult.StructuralFingerprint + "|"
                    + coreFingerprintBeforeValidation).ToString();
                return PassReceipt.Succeeded(
                    stagingPath,
                    validationResult.StructuralFingerprint,
                    outputFingerprint,
                    messages);
            }
            finally
            {
                if (outputRoot != null)
                    Object.DestroyImmediate(outputRoot);
                if (previewScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static PrefabCommitResult CommitPrefabAndReceipt(
            string stagingPath,
            string outputPath,
            NpcDefinition definition,
            string definitionFingerprint,
            string inputFingerprint,
            string providerId,
            NpcCompatibilityCapabilities requestedCapabilities,
            string providerFingerprint,
            string outputFingerprint)
        {
            string receiptPath = NpcNativeBuildReceiptUtility.GetReceiptPath(
                outputPath);
            if (string.IsNullOrWhiteSpace(receiptPath))
                return PrefabCommitResult.Failed(
                    "Could not derive a native-build receipt path.", true);
            if (definition == null
                || string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(definition))
                || string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(definition))))
                return PrefabCommitResult.Failed(
                    "The NPC Definition must be a persistent asset before a native build receipt can be created.",
                    true);
            Object receiptOccupant = AssetDatabase.LoadMainAssetAtPath(receiptPath);
            if (receiptOccupant != null
                && !(receiptOccupant is NpcNativeBuildReceipt))
                return PrefabCommitResult.Failed(
                    "A non-receipt asset already occupies '" + receiptPath + "'.",
                    true);

            string outputFolder = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
            List<string> createdFolders = EnsureAssetFolder(outputFolder);
            var outputBackup = AssetFileBackup.Capture(outputPath);
            var receiptBackup = AssetFileBackup.Capture(receiptPath);
            PrefabCommitResult prefabCommit = CommitPrefab(stagingPath, outputPath);
            if (!prefabCommit.Success)
            {
                PruneEmptyFolders(createdFolders);
                return prefabCommit;
            }

            // The staged prefab can already live beside the final output (for
            // example in tests and custom build integrations).  A receipt
            // staged under its final filename would then make CommitReceipt
            // attempt to move an asset onto itself.  Always use a private,
            // collision-resistant sidecar name and commit it only after the
            // prefab has passed validation.
            string stagedReceiptPath = (Path.GetDirectoryName(stagingPath)
                                        ?? "Assets").Replace('\\', '/')
                                       + "/__NpcNativeBuildReceipt_"
                                       + Guid.NewGuid().ToString("N")
                                       + ".asset";
            try
            {
                GameObject committedPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
                if (committedPrefab == null)
                    throw new InvalidOperationException(
                        "The committed native prefab could not be reloaded for its receipt.");
                string prefabGuid = AssetDatabase.AssetPathToGUID(outputPath);
                string dependencyHash = AssetDatabase.GetAssetDependencyHash(
                    outputPath).ToString();
                if (string.IsNullOrWhiteSpace(prefabGuid)
                    || string.IsNullOrWhiteSpace(dependencyHash))
                    throw new InvalidOperationException(
                        "Unity did not provide the committed prefab GUID and dependency hash.");

                var data = new NpcNativeBuildReceiptData(
                    definition,
                    definitionFingerprint,
                    inputFingerprint,
                    providerId,
                    requestedCapabilities,
                    outputPath,
                    prefabGuid,
                    dependencyHash,
                    providerFingerprint,
                    outputFingerprint,
                    DateTime.UtcNow);
                var stagedReceipt = ScriptableObject.CreateInstance<
                    NpcNativeBuildReceipt>();
                try
                {
                    stagedReceipt.name = Path.GetFileNameWithoutExtension(
                        stagedReceiptPath);
                    stagedReceipt.Initialize(data);
                    AssetDatabase.CreateAsset(stagedReceipt, stagedReceiptPath);
                }
                finally
                {
                    if (stagedReceipt != null
                        && string.IsNullOrEmpty(
                            AssetDatabase.GetAssetPath(stagedReceipt)))
                        Object.DestroyImmediate(stagedReceipt);
                }
                AssetDatabase.SaveAssetIfDirty(stagedReceipt);
                AssetDatabase.ImportAsset(
                    stagedReceiptPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadAssetAtPath<NpcNativeBuildReceipt>(
                        stagedReceiptPath) == null)
                    throw new InvalidOperationException(
                        "Unity could not reload the staged native-build receipt.");

                CommitReceipt(stagedReceiptPath, receiptPath, receiptBackup.Existed);
                NpcNativeBuildReceipt committedReceipt =
                    AssetDatabase.LoadAssetAtPath<NpcNativeBuildReceipt>(receiptPath);
                NpcNativeBuildReceiptValidationReport validation =
                    NpcNativeBuildReceiptUtility.Validate(
                        committedReceipt,
                        definition,
                        definitionFingerprint,
                        inputFingerprint,
                        providerId,
                        requestedCapabilities);
                if (!validation.IsValid)
                    throw new InvalidOperationException(
                        "The committed native-build receipt failed read-only validation: "
                        + string.Join(", ", validation.Issues.Select(value => value.Code)));
                return PrefabCommitResult.Succeeded();
            }
            catch (Exception exception)
            {
                bool receiptRestored = receiptBackup.Restore();
                bool outputRestored = outputBackup.Restore();
                if (AssetDatabase.LoadAssetAtPath<Object>(stagedReceiptPath) != null)
                    AssetDatabase.DeleteAsset(stagedReceiptPath);
                PruneEmptyFolders(createdFolders);
                return PrefabCommitResult.Failed(
                    "Native prefab/receipt transaction failed: " + exception.Message,
                    receiptRestored && outputRestored);
            }
        }

        private static void CommitReceipt(
            string stagingPath,
            string receiptPath,
            bool receiptExisted)
        {
            if (!receiptExisted)
            {
                string moveError = AssetDatabase.MoveAsset(stagingPath, receiptPath);
                if (!string.IsNullOrEmpty(moveError))
                    throw new InvalidOperationException(moveError);
            }
            else
            {
                File.Copy(
                    AbsoluteAssetPath(stagingPath),
                    AbsoluteAssetPath(receiptPath),
                    true);
                AssetDatabase.DeleteAsset(stagingPath);
            }
            AssetDatabase.ImportAsset(
                receiptPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            if (AssetDatabase.LoadAssetAtPath<NpcNativeBuildReceipt>(receiptPath)
                == null)
                throw new InvalidOperationException(
                    "Unity could not load the native-build receipt after commit.");
        }

        private static PrefabCommitResult CommitPrefab(
            string stagingPath,
            string outputPath)
        {
            GameObject staged = AssetDatabase.LoadAssetAtPath<GameObject>(stagingPath);
            if (staged == null)
                return PrefabCommitResult.Failed(
                    "The verified staged prefab is missing.", true);
            List<string> createdFolders = EnsureAssetFolder(
                Path.GetDirectoryName(outputPath)?.Replace('\\', '/'));
            Object existing = AssetDatabase.LoadAssetAtPath<Object>(outputPath);
            if (existing == null)
            {
                string moveError = AssetDatabase.MoveAsset(stagingPath, outputPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    PruneEmptyFolders(createdFolders);
                    return PrefabCommitResult.Failed(moveError, true);
                }
                AssetDatabase.ImportAsset(
                    outputPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(outputPath) == null)
                {
                    string rollback = AssetDatabase.MoveAsset(outputPath, stagingPath);
                    PruneEmptyFolders(createdFolders);
                    return PrefabCommitResult.Failed(
                        "The new prefab could not be loaded after commit. " + rollback,
                        string.IsNullOrEmpty(rollback));
                }
                return PrefabCommitResult.Succeeded();
            }
            if (!(existing is GameObject))
                return PrefabCommitResult.Failed(
                    "A non-prefab asset already occupies the requested output path.",
                    true);

            string stagedAbsolute = AbsoluteAssetPath(stagingPath);
            string outputAbsolute = AbsoluteAssetPath(outputPath);
            byte[] previousBytes = File.ReadAllBytes(outputAbsolute);
            try
            {
                File.Copy(stagedAbsolute, outputAbsolute, true);
                AssetDatabase.ImportAsset(
                    outputPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(outputPath) == null)
                    throw new InvalidOperationException(
                        "Unity could not load the replaced prefab.");
                AssetDatabase.DeleteAsset(stagingPath);
                return PrefabCommitResult.Succeeded();
            }
            catch (Exception exception)
            {
                bool restored = false;
                try
                {
                    File.WriteAllBytes(outputAbsolute, previousBytes);
                    AssetDatabase.ImportAsset(
                        outputPath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                    restored = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath)
                               != null;
                }
                catch
                {
                    restored = false;
                }
                return PrefabCommitResult.Failed(exception.Message, restored);
            }
        }

        private static string ComputeCoreFingerprint(GameObject root)
        {
            return Hash128.Compute(CreateCoreSnapshot(root)).ToString();
        }

        private static string CreateCoreSnapshot(GameObject root)
        {
            var builder = new StringBuilder(8192);
            AppendTransform(builder, root.transform, root.transform);
            return builder.ToString();
        }

        private static string DescribeFirstDifference(
            string before,
            string after)
        {
            string[] beforeTokens = (before ?? string.Empty).Split('|');
            string[] afterTokens = (after ?? string.Empty).Split('|');
            int count = Math.Min(beforeTokens.Length, afterTokens.Length);
            int index = 0;
            while (index < count && string.Equals(
                       beforeTokens[index], afterTokens[index],
                       StringComparison.Ordinal))
                index++;
            if (index == count)
                return "token count " + beforeTokens.Length + " -> "
                       + afterTokens.Length;
            int start = Math.Max(0, index - 2);
            int end = Math.Min(count - 1, index + 2);
            string beforeContext = string.Join(", ", beforeTokens
                .Skip(start).Take(end - start + 1));
            string afterContext = string.Join(", ", afterTokens
                .Skip(start).Take(end - start + 1));
            return "token " + index + " [before: " + beforeContext
                   + "] [after: " + afterContext + "]";
        }

        private static void AppendTransform(
            StringBuilder builder,
            Transform root,
            Transform value)
        {
            builder.Append(RelativePath(root, value)).Append('|');
            Append(builder, value.localPosition);
            Append(builder, value.localRotation);
            Append(builder, value.localScale);
            Component[] components = value.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    builder.Append("<missing>|");
                    continue;
                }
                builder.Append(component.GetType().FullName).Append('|');
                AppendSerializedComponent(builder, root, component);
                AppendKnownComponent(builder, root, component);
            }
            for (int i = 0; i < value.childCount; i++)
                AppendTransform(builder, root, value.GetChild(i));
        }

        private static void AppendSerializedComponent(
            StringBuilder builder,
            Transform root,
            Component component)
        {
            var serialized = new SerializedObject(component);
            serialized.UpdateIfRequiredOrScript();
            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                if (IsPrefabSerializationBookkeeping(property.propertyPath))
                {
                    enterChildren = false;
                    continue;
                }
                enterChildren = true;
                builder.Append(property.propertyPath).Append(':')
                    .Append((int)property.propertyType).Append(':');
                AppendSerializedValue(builder, root, property);
                builder.Append('|');
                if (property.propertyType == SerializedPropertyType.ObjectReference
                    || property.propertyType
                    == SerializedPropertyType.ExposedReference)
                    enterChildren = false;
            }
        }

        private static bool IsPrefabSerializationBookkeeping(string propertyPath)
        {
            return string.Equals(
                       propertyPath,
                       "m_CorrespondingSourceObject",
                       StringComparison.Ordinal)
                   || propertyPath.StartsWith(
                       "m_CorrespondingSourceObject.",
                       StringComparison.Ordinal)
                   || string.Equals(
                       propertyPath,
                       "m_PrefabInstance",
                       StringComparison.Ordinal)
                   || propertyPath.StartsWith(
                       "m_PrefabInstance.",
                       StringComparison.Ordinal)
                   || string.Equals(
                       propertyPath,
                       "m_PrefabAsset",
                       StringComparison.Ordinal)
                   || propertyPath.StartsWith(
                       "m_PrefabAsset.",
                       StringComparison.Ordinal);
        }

        private static void AppendSerializedValue(
            StringBuilder builder,
            Transform root,
            SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                    builder.Append(property.intValue);
                    break;
                case SerializedPropertyType.Boolean:
                    builder.Append(property.boolValue);
                    break;
                case SerializedPropertyType.Float:
                    Append(builder, property.floatValue);
                    break;
                case SerializedPropertyType.String:
                    builder.Append(property.stringValue ?? string.Empty);
                    break;
                case SerializedPropertyType.Color:
                    Color color = property.colorValue;
                    Append(builder, color.r);
                    Append(builder, color.g);
                    Append(builder, color.b);
                    Append(builder, color.a);
                    break;
                case SerializedPropertyType.ObjectReference:
                    AppendObjectReference(builder, root, property.objectReferenceValue);
                    break;
                case SerializedPropertyType.Enum:
                    builder.Append(property.enumValueIndex);
                    break;
                case SerializedPropertyType.Vector2:
                    Vector2 vector2 = property.vector2Value;
                    Append(builder, vector2.x);
                    Append(builder, vector2.y);
                    break;
                case SerializedPropertyType.Vector3:
                    Append(builder, property.vector3Value);
                    break;
                case SerializedPropertyType.Vector4:
                    Vector4 vector4 = property.vector4Value;
                    Append(builder, vector4.x);
                    Append(builder, vector4.y);
                    Append(builder, vector4.z);
                    Append(builder, vector4.w);
                    break;
                case SerializedPropertyType.Rect:
                    Rect rect = property.rectValue;
                    Append(builder, rect.x);
                    Append(builder, rect.y);
                    Append(builder, rect.width);
                    Append(builder, rect.height);
                    break;
                case SerializedPropertyType.AnimationCurve:
                    AppendAnimationCurve(builder, property.animationCurveValue);
                    break;
                case SerializedPropertyType.Bounds:
                    Bounds bounds = property.boundsValue;
                    Append(builder, bounds.center);
                    Append(builder, bounds.size);
                    break;
                case SerializedPropertyType.Quaternion:
                    Append(builder, property.quaternionValue);
                    break;
                case SerializedPropertyType.ExposedReference:
                    AppendObjectReference(builder, root, property.exposedReferenceValue);
                    break;
                case SerializedPropertyType.FixedBufferSize:
                    builder.Append(property.fixedBufferSize);
                    break;
                case SerializedPropertyType.Vector2Int:
                    Vector2Int vector2Int = property.vector2IntValue;
                    builder.Append(vector2Int.x).Append(',').Append(vector2Int.y);
                    break;
                case SerializedPropertyType.Vector3Int:
                    Vector3Int vector3Int = property.vector3IntValue;
                    builder.Append(vector3Int.x).Append(',').Append(vector3Int.y)
                        .Append(',').Append(vector3Int.z);
                    break;
                case SerializedPropertyType.RectInt:
                    RectInt rectInt = property.rectIntValue;
                    builder.Append(rectInt.x).Append(',').Append(rectInt.y)
                        .Append(',').Append(rectInt.width).Append(',')
                        .Append(rectInt.height);
                    break;
                case SerializedPropertyType.BoundsInt:
                    BoundsInt boundsInt = property.boundsIntValue;
                    builder.Append(boundsInt.position.x).Append(',')
                        .Append(boundsInt.position.y).Append(',')
                        .Append(boundsInt.position.z).Append(',')
                        .Append(boundsInt.size.x).Append(',')
                        .Append(boundsInt.size.y).Append(',')
                        .Append(boundsInt.size.z);
                    break;
                case SerializedPropertyType.ManagedReference:
                    builder.Append(property.managedReferenceFullTypename ?? string.Empty);
                    break;
                default:
                    // Generic structs and arrays are represented by their
                    // recursively visited children. Gradient is intentionally
                    // identified by type; none of the supported native anatomy
                    // contract uses a Gradient value.
                    builder.Append(property.type ?? string.Empty);
                    break;
            }
        }

        private static void AppendAnimationCurve(
            StringBuilder builder,
            AnimationCurve curve)
        {
            Keyframe[] keys = curve == null ? Array.Empty<Keyframe>() : curve.keys;
            builder.Append(keys.Length).Append(':');
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                Append(builder, key.time);
                Append(builder, key.value);
                Append(builder, key.inTangent);
                Append(builder, key.outTangent);
                Append(builder, key.inWeight);
                Append(builder, key.outWeight);
                builder.Append((int)key.weightedMode).Append(':');
            }
            if (curve != null)
                builder.Append((int)curve.preWrapMode).Append(':')
                    .Append((int)curve.postWrapMode);
        }

        private static void AppendObjectReference(
            StringBuilder builder,
            Transform root,
            Object value)
        {
            if (value == null)
            {
                builder.Append("<null>");
                return;
            }
            var gameObject = value as GameObject;
            if (gameObject != null && IsWithin(root, gameObject.transform))
            {
                builder.Append("go:").Append(RelativePath(root, gameObject.transform));
                return;
            }
            var component = value as Component;
            if (component != null && IsWithin(root, component.transform))
            {
                Component[] peers = component.gameObject.GetComponents(
                    component.GetType());
                int componentIndex = Array.IndexOf(peers, component);
                builder.Append("component:")
                    .Append(RelativePath(root, component.transform)).Append(':')
                    .Append(component.GetType().FullName).Append(':')
                    .Append(componentIndex);
                return;
            }
            string guid;
            long localId;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    value, out guid, out localId))
            {
                builder.Append("asset:").Append(guid).Append(':').Append(localId);
                return;
            }
            builder.Append("object:").Append(value.GetType().FullName).Append(':')
                .Append(value.name ?? string.Empty);
        }

        private static bool IsWithin(Transform root, Transform value)
        {
            return root != null && value != null
                   && (value == root || value.IsChildOf(root));
        }

        private static void AppendKnownComponent(
            StringBuilder builder,
            Transform root,
            Component component)
        {
            var body = component as Rigidbody;
            if (body != null)
            {
                Append(builder, body.mass);
                Append(builder, body.drag);
                Append(builder, body.angularDrag);
                builder.Append(body.useGravity).Append('|')
                    .Append(body.isKinematic).Append('|')
                    .Append((int)body.constraints).Append('|');
                return;
            }
            var box = component as BoxCollider;
            if (box != null)
            {
                Append(builder, box.center);
                Append(builder, box.size);
                builder.Append(box.isTrigger).Append('|');
                return;
            }
            var capsule = component as CapsuleCollider;
            if (capsule != null)
            {
                Append(builder, capsule.center);
                Append(builder, capsule.radius);
                Append(builder, capsule.height);
                builder.Append(capsule.direction).Append('|')
                    .Append(capsule.isTrigger).Append('|');
                return;
            }
            var sphere = component as SphereCollider;
            if (sphere != null)
            {
                Append(builder, sphere.center);
                Append(builder, sphere.radius);
                builder.Append(sphere.isTrigger).Append('|');
                return;
            }
            var joint = component as ConfigurableJoint;
            if (joint != null)
            {
                builder.Append(joint.connectedBody == null
                        ? "<world>"
                        : RelativePath(root, joint.connectedBody.transform))
                    .Append('|');
                Append(builder, joint.anchor);
                Append(builder, joint.connectedAnchor);
                Append(builder, joint.axis);
                Append(builder, joint.secondaryAxis);
                builder.Append((int)joint.xMotion).Append('|')
                    .Append((int)joint.yMotion).Append('|')
                    .Append((int)joint.zMotion).Append('|')
                    .Append((int)joint.angularXMotion).Append('|')
                    .Append((int)joint.angularYMotion).Append('|')
                    .Append((int)joint.angularZMotion).Append('|');
                Append(builder, joint.lowAngularXLimit.limit);
                Append(builder, joint.highAngularXLimit.limit);
                Append(builder, joint.angularYLimit.limit);
                Append(builder, joint.angularZLimit.limit);
                Append(builder, joint.slerpDrive.positionSpring);
                Append(builder, joint.slerpDrive.positionDamper);
                Append(builder, joint.slerpDrive.maximumForce);
                return;
            }
            var skinned = component as SkinnedMeshRenderer;
            if (skinned != null)
            {
                builder.Append(AssetGuid(skinned.sharedMesh)).Append('|')
                    .Append(skinned.enabled).Append('|');
                AppendMaterials(builder, skinned.sharedMaterials);
                return;
            }
            var renderer = component as Renderer;
            if (renderer != null)
            {
                builder.Append(renderer.enabled).Append('|');
                AppendMaterials(builder, renderer.sharedMaterials);
            }
        }

        private static void AppendMaterials(
            StringBuilder builder,
            Material[] materials)
        {
            materials = materials ?? Array.Empty<Material>();
            builder.Append(materials.Length).Append('|');
            for (int i = 0; i < materials.Length; i++)
                builder.Append(AssetGuid(materials[i])).Append('|');
        }

        private static string AssetGuid(Object value)
        {
            if (value == null)
                return string.Empty;
            return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value));
        }

        private static string RelativePath(Transform root, Transform value)
        {
            if (root == value)
                return "<root>[0]";
            var parts = new List<string>();
            Transform current = value;
            while (current != null && current != root)
            {
                parts.Add(current.name + "[" + current.GetSiblingIndex() + "]");
                current = current.parent;
            }
            parts.Reverse();
            return "<root>[0]/" + string.Join("/", parts);
        }

        private static void Append(StringBuilder builder, Vector3 value)
        {
            Append(builder, value.x);
            Append(builder, value.y);
            Append(builder, value.z);
        }

        private static void Append(StringBuilder builder, Quaternion value)
        {
            Append(builder, value.x);
            Append(builder, value.y);
            Append(builder, value.z);
            Append(builder, value.w);
        }

        private static void Append(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture))
                .Append('|');
        }

        private static bool IsValidOutputPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                   && path.StartsWith("Assets/", StringComparison.Ordinal)
                   && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                   && path.IndexOf("/../", StringComparison.Ordinal) < 0;
        }

        private static string StageRoot(string outputPath)
        {
            return "Assets/__NpcToolkitStaging_"
                   + Guid.NewGuid().ToString("N");
        }

        private static void DeleteStageRoot(string stageRoot)
        {
            if (!string.IsNullOrWhiteSpace(stageRoot)
                && stageRoot.StartsWith("Assets/", StringComparison.Ordinal)
                && AssetDatabase.IsValidFolder(stageRoot))
                AssetDatabase.DeleteAsset(stageRoot);
        }

        private static List<string> EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new InvalidOperationException("An asset folder is required.");
            folder = NormalizeAssetPath(folder).TrimEnd('/');
            if (!folder.StartsWith("Assets", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Generated asset folders must be under Assets/.");
            if (AssetDatabase.IsValidFolder(folder))
                return new List<string>();
            var created = new List<string>();
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                    created.Add(next);
                }
                current = next;
            }
            return created;
        }

        private static void PruneEmptyFolders(IReadOnlyList<string> folders)
        {
            if (folders == null)
                return;
            for (int i = folders.Count - 1; i >= 0; i--)
            {
                string folder = folders[i];
                if (!AssetDatabase.IsValidFolder(folder))
                    continue;
                string absolute = AbsoluteAssetPath(folder);
                string[] entries = Directory.Exists(absolute)
                    ? Directory.GetFileSystemEntries(absolute)
                    : Array.Empty<string>();
                bool hasContent = entries.Any(value =>
                    !value.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
                if (!hasContent)
                    AssetDatabase.DeleteAsset(folder);
            }
        }

        private static string AbsoluteAssetPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException(
                    "Could not resolve the Unity project root.");
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static string NormalizeAssetPath(string value)
        {
            return (value ?? string.Empty).Replace('\\', '/').Trim();
        }

        private static string SafeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Npc";
            var builder = new StringBuilder();
            foreach (char character in value)
                if (char.IsLetterOrDigit(character) || character == '_' || character == '-')
                    builder.Append(character);
            return builder.Length == 0 ? "Npc" : builder.ToString();
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

        private static NpcNativeBuildMessage Error(string code, string message)
        {
            return new NpcNativeBuildMessage(
                NpcNativeBuildMessageSeverity.Error,
                code,
                message);
        }

        private static void Add(
            ICollection<NpcNativeBuildMessage> target,
            IEnumerable<NpcNativeBuildMessage> values)
        {
            if (values == null)
                return;
            foreach (NpcNativeBuildMessage value in values)
                if (value != null && !target.Any(existing =>
                        existing != null
                        && existing.Severity == value.Severity
                        && string.Equals(existing.Code, value.Code,
                            StringComparison.Ordinal)
                        && string.Equals(existing.Message, value.Message,
                            StringComparison.Ordinal)))
                    target.Add(value);
        }

        private static NpcNativeBuildReport Report(
            NpcNativeBuildStatus status,
            NpcNativeBuildRequest request,
            string inputFingerprint,
            string outputFingerprint,
            NpcBuildReadinessReport readiness,
            NpcNativeBuildProviderSelection selection,
            bool replacedExisting,
            bool previousPreserved,
            NpcNativeBuildMessage message,
            string outputPath = null)
        {
            return Report(
                status,
                request,
                inputFingerprint,
                outputFingerprint,
                readiness,
                selection,
                replacedExisting,
                previousPreserved,
                new[] { message },
                outputPath);
        }

        private static NpcNativeBuildReport Report(
            NpcNativeBuildStatus status,
            NpcNativeBuildRequest request,
            string inputFingerprint,
            string outputFingerprint,
            NpcBuildReadinessReport readiness,
            NpcNativeBuildProviderSelection selection,
            bool replacedExisting,
            bool previousPreserved,
            IEnumerable<NpcNativeBuildMessage> messages,
            string outputPath = null)
        {
            string path = outputPath ?? (request == null
                ? string.Empty
                : request.OutputPrefabPath);
            return new NpcNativeBuildReport(
                status,
                path,
                inputFingerprint,
                outputFingerprint,
                SafeProviderId(selection?.Provider),
                replacedExisting,
                previousPreserved,
                readiness,
                selection,
                messages);
        }

        private sealed class PassReceipt
        {
            private readonly NpcNativeBuildMessage[] messages;

            public bool Success { get; }
            public string StagingPath { get; }
            public string ProviderFingerprint { get; }
            public string OutputFingerprint { get; }
            public IReadOnlyList<NpcNativeBuildMessage> Messages => messages;

            private PassReceipt(
                bool success,
                string stagingPath,
                string providerFingerprint,
                string outputFingerprint,
                IEnumerable<NpcNativeBuildMessage> messages)
            {
                Success = success;
                StagingPath = stagingPath ?? string.Empty;
                ProviderFingerprint = providerFingerprint ?? string.Empty;
                OutputFingerprint = outputFingerprint ?? string.Empty;
                this.messages = (messages ?? Array.Empty<NpcNativeBuildMessage>())
                    .Where(value => value != null)
                    .ToArray();
            }

            public static PassReceipt Succeeded(
                string stagingPath,
                string providerFingerprint,
                string fingerprint,
                IEnumerable<NpcNativeBuildMessage> messages)
            {
                return new PassReceipt(
                    true,
                    stagingPath,
                    providerFingerprint,
                    fingerprint,
                    messages);
            }

            public static PassReceipt Failed(NpcNativeBuildMessage message)
            {
                return new PassReceipt(
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    new[] { message });
            }

            public static PassReceipt Failed(
                IEnumerable<NpcNativeBuildMessage> messages)
            {
                return new PassReceipt(
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    messages);
            }
        }

        private sealed class PrefabCommitResult
        {
            public bool Success { get; }
            public string Detail { get; }
            public bool PreviousOutputPreserved { get; }

            private PrefabCommitResult(
                bool success,
                string detail,
                bool previousOutputPreserved)
            {
                Success = success;
                Detail = detail ?? string.Empty;
                PreviousOutputPreserved = previousOutputPreserved;
            }

            public static PrefabCommitResult Succeeded()
            {
                return new PrefabCommitResult(true, string.Empty, true);
            }

            public static PrefabCommitResult Failed(
                string detail,
                bool previousOutputPreserved)
            {
                return new PrefabCommitResult(
                    false, detail, previousOutputPreserved);
            }
        }

        private sealed class AssetFileBackup
        {
            public string AssetPath { get; }
            public bool Existed { get; }
            private readonly byte[] bytes;

            private AssetFileBackup(
                string assetPath,
                bool existed,
                byte[] bytes)
            {
                AssetPath = assetPath;
                Existed = existed;
                this.bytes = bytes ?? Array.Empty<byte>();
            }

            public static AssetFileBackup Capture(string assetPath)
            {
                string absolutePath = AbsoluteAssetPath(assetPath);
                bool existed = File.Exists(absolutePath);
                return new AssetFileBackup(
                    assetPath,
                    existed,
                    existed ? File.ReadAllBytes(absolutePath) : Array.Empty<byte>());
            }

            public bool Restore()
            {
                try
                {
                    string absolutePath = AbsoluteAssetPath(AssetPath);
                    if (!Existed)
                    {
                        if (AssetDatabase.LoadMainAssetAtPath(AssetPath) != null
                            || File.Exists(absolutePath))
                            AssetDatabase.DeleteAsset(AssetPath);
                        return AssetDatabase.LoadMainAssetAtPath(AssetPath) == null
                               && !File.Exists(absolutePath);
                    }

                    File.WriteAllBytes(absolutePath, bytes);
                    AssetDatabase.ImportAsset(
                        AssetPath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                    return File.Exists(absolutePath)
                           && bytes.SequenceEqual(File.ReadAllBytes(absolutePath))
                           && AssetDatabase.LoadMainAssetAtPath(AssetPath) != null;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    internal sealed class NpcNativeBuildInputGuard
    {
        private readonly ObjectSnapshot[] snapshots;
        private readonly AssetSnapshot[] assets;

        private NpcNativeBuildInputGuard(
            ObjectSnapshot[] snapshots,
            AssetSnapshot[] assets)
        {
            this.snapshots = snapshots;
            this.assets = assets;
        }

        public static NpcNativeBuildInputGuard Capture(
            NpcDefinition definition,
            GameObject preview)
        {
            var objects = new Object[]
            {
                definition,
                definition?.AvatarSourceProfile,
                definition?.AnatomyProfile,
                definition?.MovementProfile,
                definition?.MovementProfile?.ProviderStandingPose as ScriptableObject,
                definition?.MovementProfile?.ProviderMovementConfig as ScriptableObject,
                definition?.BuildProfile,
                definition?.AudioProfile,
            };
            ObjectSnapshot[] snapshots = objects
                .Where(value => value != null)
                .Select(ObjectSnapshot.Capture)
                .ToArray();
            var assetObjects = new Object[]
            {
                definition,
                definition?.AvatarSourceProfile,
                definition?.AnatomyProfile,
                definition?.MovementProfile,
                definition?.MovementProfile?.ProviderStandingPose,
                definition?.MovementProfile?.ProviderMovementConfig,
                definition?.BuildProfile,
                definition?.AudioProfile,
                definition?.SourceAvatar,
                preview,
            };
            AssetSnapshot[] assets = assetObjects
                .Where(value => value != null)
                .Select(AssetDatabase.GetAssetPath)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Select(AssetSnapshot.Capture)
                .ToArray();
            return new NpcNativeBuildInputGuard(snapshots, assets);
        }

        public string FindMutation()
        {
            foreach (ObjectSnapshot snapshot in snapshots)
                if (!snapshot.Matches())
                    return "The provider changed authoring object '"
                           + snapshot.Label + "'. Nothing was committed.";
            foreach (AssetSnapshot asset in assets)
                if (!asset.Matches())
                    return "The provider changed input asset '" + asset.Path
                           + "'. Nothing was committed.";
            return string.Empty;
        }

        public void RestoreScriptableObjects()
        {
            string error = RestoreInputs();
            if (!string.IsNullOrWhiteSpace(error))
                Debug.LogError(error);
        }

        /// <summary>
        /// Restores both the exact persistent asset files and the pre-call
        /// in-memory/dirty state. Validation providers are read-only, so merely
        /// repairing the loaded ScriptableObject would be insufficient: a
        /// provider may already have saved its accidental edit to disk.
        /// </summary>
        public string RestoreInputs()
        {
            var errors = new List<string>();
            foreach (AssetSnapshot asset in assets)
            {
                // A dependency hash can drift only because another guarded
                // asset changed. Restore concrete file/meta mutations first;
                // validate all transitive hashes after every file is back.
                if (asset.SavedFilesMatch()) continue;
                if (!asset.Restore(out string detail))
                    errors.Add(detail);
            }
            foreach (ObjectSnapshot snapshot in snapshots)
            {
                try
                {
                    snapshot.Restore();
                }
                catch (Exception exception)
                {
                    errors.Add(
                        "Could not restore authoring object '" + snapshot.Label
                        + "': " + exception.Message);
                }
            }
            foreach (AssetSnapshot asset in assets)
                if (!asset.Matches())
                    errors.Add(
                        "Input asset '" + asset.Path
                        + "' still differs after rollback.");
            return string.Join("\n", errors.Distinct(StringComparer.Ordinal));
        }

        private sealed class ObjectSnapshot
        {
            private Object target;
            public Object Target => ResolveTarget();
            public string Label { get; }
            private readonly string json;
            private readonly bool wasDirty;
            private readonly string assetPath;
            private readonly long localFileId;

            private ObjectSnapshot(
                Object target,
                string label,
                string json,
                bool wasDirty,
                string assetPath,
                long localFileId)
            {
                this.target = target;
                Label = label;
                this.json = json;
                this.wasDirty = wasDirty;
                this.assetPath = assetPath;
                this.localFileId = localFileId;
            }

            public static ObjectSnapshot Capture(Object target)
            {
                string path = AssetDatabase.GetAssetPath(target);
                long fileId = 0;
                if (!string.IsNullOrWhiteSpace(path))
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        target, out _, out fileId);
                return new ObjectSnapshot(
                    target,
                    target.name,
                    EditorJsonUtility.ToJson(target, false),
                    EditorUtility.IsDirty(target),
                    path,
                    fileId);
            }

            public bool Matches()
            {
                Object value = ResolveTarget();
                return value != null
                       && string.Equals(
                           json,
                           EditorJsonUtility.ToJson(value, false),
                           StringComparison.Ordinal)
                       && wasDirty == EditorUtility.IsDirty(value);
            }

            public void Restore()
            {
                Object value = ResolveTarget();
                if (value == null)
                    return;
                EditorJsonUtility.FromJsonOverwrite(json, value);
                if (wasDirty)
                    EditorUtility.SetDirty(value);
                else
                    EditorUtility.ClearDirty(value);
            }

            private Object ResolveTarget()
            {
                if (target != null || string.IsNullOrWhiteSpace(assetPath))
                    return target;
                foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(
                             assetPath))
                {
                    if (candidate == null
                        || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                            candidate, out _, out long candidateId)
                        || candidateId != localFileId)
                        continue;
                    target = candidate;
                    break;
                }
                return target;
            }
        }

        private sealed class AssetSnapshot
        {
            public string Path { get; }
            private readonly string dependencyHash;
            private readonly bool assetExisted;
            private readonly byte[] assetBytes;
            private readonly bool metaExisted;
            private readonly byte[] metaBytes;

            private AssetSnapshot(
                string path,
                string dependencyHash,
                bool assetExisted,
                byte[] assetBytes,
                bool metaExisted,
                byte[] metaBytes)
            {
                Path = path;
                this.dependencyHash = dependencyHash;
                this.assetExisted = assetExisted;
                this.assetBytes = assetBytes ?? Array.Empty<byte>();
                this.metaExisted = metaExisted;
                this.metaBytes = metaBytes ?? Array.Empty<byte>();
            }

            public static AssetSnapshot Capture(string path)
            {
                string absolute = AbsolutePath(path);
                string meta = absolute + ".meta";
                bool assetExists = File.Exists(absolute);
                bool metaExists = File.Exists(meta);
                return new AssetSnapshot(
                    path,
                    AssetDatabase.GetAssetDependencyHash(path).ToString(),
                    assetExists,
                    assetExists ? File.ReadAllBytes(absolute) : Array.Empty<byte>(),
                    metaExists,
                    metaExists ? File.ReadAllBytes(meta) : Array.Empty<byte>());
            }

            public bool Matches()
            {
                return SavedFilesMatch()
                       && string.Equals(
                           dependencyHash,
                           AssetDatabase.GetAssetDependencyHash(Path).ToString(),
                           StringComparison.Ordinal);
            }

            public bool SavedFilesMatch()
            {
                string absolute = AbsolutePath(Path);
                return FileMatches(absolute, assetExisted, assetBytes)
                       && FileMatches(absolute + ".meta", metaExisted, metaBytes);
            }

            public bool Restore(out string detail)
            {
                try
                {
                    string absolute = AbsolutePath(Path);
                    RestoreFile(absolute, assetExisted, assetBytes);
                    RestoreFile(absolute + ".meta", metaExisted, metaBytes);
                    AssetDatabase.ImportAsset(
                        Path,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                    if (!SavedFilesMatch())
                    {
                        detail = "Input asset '" + Path
                                 + "' still differs after restoring its saved bytes.";
                        return false;
                    }
                    detail = string.Empty;
                    return true;
                }
                catch (Exception exception)
                {
                    detail = "Could not restore input asset '" + Path + "': "
                             + exception.Message;
                    return false;
                }
            }

            private static string AbsolutePath(string assetPath)
            {
                string projectRoot = Directory.GetParent(Application.dataPath)
                    ?.FullName;
                if (string.IsNullOrWhiteSpace(projectRoot))
                    throw new InvalidOperationException(
                        "Unity project root could not be resolved.");
                return System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(projectRoot, assetPath));
            }

            private static bool FileMatches(
                string path,
                bool existed,
                byte[] expected)
            {
                if (File.Exists(path) != existed) return false;
                return !existed || expected.SequenceEqual(File.ReadAllBytes(path));
            }

            private static void RestoreFile(
                string path,
                bool existed,
                byte[] bytes)
            {
                if (!existed)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                string directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, bytes);
            }
        }
    }
}
