using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SLZ.Marrow.Warehouse;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Validation;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor.Build
{
    public enum NpcSpawnableCratePreparationStatus
    {
        InvalidRequest,
        NativeReceiptMissing,
        NativeReceiptStale,
        BindingInvalid,
        BoundAssetMissing,
        CommitFailed,
        ValidationFailed,
        Succeeded,
    }

    public enum NpcSpawnableCratePreparationMessageSeverity
    {
        Info,
        Warning,
        Error,
    }

    public sealed class NpcSpawnableCratePreparationMessage
    {
        public NpcSpawnableCratePreparationMessageSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }

        internal NpcSpawnableCratePreparationMessage(
            NpcSpawnableCratePreparationMessageSeverity severity,
            string code,
            string message)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public sealed class NpcSpawnableCratePreparationRequest
    {
        public NpcDefinition Definition { get; }
        public string NativePrefabAssetPath { get; }

        public NpcSpawnableCratePreparationRequest(
            NpcDefinition definition,
            string nativePrefabAssetPath = null)
        {
            Definition = definition;
            NativePrefabAssetPath = nativePrefabAssetPath ?? string.Empty;
        }
    }

    public sealed class NpcSpawnableCratePreparationReport
    {
        private readonly NpcSpawnableCratePreparationMessage[] messages;

        public NpcSpawnableCratePreparationStatus Status { get; }
        public bool Success => Status == NpcSpawnableCratePreparationStatus.Succeeded;
        public bool PalletCreated { get; }
        public bool CrateCreated { get; }
        public bool PreviousAssetsPreserved { get; }
        public string NativePrefabAssetPath { get; }
        public string PalletAssetPath { get; }
        public string PalletAssetGuid { get; }
        public string PalletTitle { get; }
        public string PalletBarcode { get; }
        public string CrateAssetPath { get; }
        public string CrateAssetGuid { get; }
        public string CrateTitle { get; }
        public string CrateBarcode { get; }
        public string PackagingFingerprint { get; }
        public NpcBuildReadinessReport Readiness { get; }
        public NpcNativeBuildReceiptValidationReport ReceiptValidation { get; }
        public IReadOnlyList<NpcSpawnableCratePreparationMessage> Messages =>
            messages;

        internal NpcSpawnableCratePreparationReport(
            NpcSpawnableCratePreparationStatus status,
            bool palletCreated,
            bool crateCreated,
            bool previousAssetsPreserved,
            string nativePrefabAssetPath,
            Pallet pallet,
            SpawnableCrate crate,
            string packagingFingerprint,
            NpcBuildReadinessReport readiness,
            NpcNativeBuildReceiptValidationReport receiptValidation,
            IEnumerable<NpcSpawnableCratePreparationMessage> messages)
        {
            Status = status;
            PalletCreated = palletCreated;
            CrateCreated = crateCreated;
            PreviousAssetsPreserved = previousAssetsPreserved;
            NativePrefabAssetPath = nativePrefabAssetPath ?? string.Empty;
            PalletAssetPath = AssetDatabase.GetAssetPath(pallet) ?? string.Empty;
            PalletAssetGuid = string.IsNullOrWhiteSpace(PalletAssetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(PalletAssetPath);
            PalletTitle = pallet?.Title ?? string.Empty;
            PalletBarcode = pallet?.Barcode?.ID ?? string.Empty;
            CrateAssetPath = AssetDatabase.GetAssetPath(crate) ?? string.Empty;
            CrateAssetGuid = string.IsNullOrWhiteSpace(CrateAssetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(CrateAssetPath);
            CrateTitle = crate?.Title ?? string.Empty;
            CrateBarcode = crate?.Barcode?.ID ?? string.Empty;
            PackagingFingerprint = packagingFingerprint ?? string.Empty;
            Readiness = readiness;
            ReceiptValidation = receiptValidation;
            this.messages = (messages
                             ?? Array.Empty<NpcSpawnableCratePreparationMessage>())
                .Where(value => value != null)
                .ToArray();
        }
    }

    /// <summary>
    /// Step 5B transaction. Existing assets are resolved only by the GUIDs
    /// stored in NpcBuildProfile; titles are metadata and are never lookup keys.
    /// </summary>
    public static class NpcSpawnableCratePreparationCoordinator
    {
        public static NpcSpawnableCratePreparationReport Prepare(
            NpcSpawnableCratePreparationRequest request)
        {
            if (request == null || request.Definition == null
                                || request.Definition.BuildProfile == null)
                return Failure(
                    NpcSpawnableCratePreparationStatus.InvalidRequest,
                    request?.Definition,
                    string.Empty,
                    "SPAWNABLE_REQUEST_INVALID",
                    "Select an NPC Definition with a Build Profile first.");

            NpcDefinition definition = request.Definition;
            NpcBuildProfile build = definition.BuildProfile;
            if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(build)))
                return Failure(
                    NpcSpawnableCratePreparationStatus.InvalidRequest,
                    definition,
                    string.Empty,
                    "BUILD_PROFILE_NOT_PERSISTENT",
                    "Save the NPC Build Profile before preparing its Pallet and Spawnable Crate.");

            string nativePath = string.IsNullOrWhiteSpace(
                request.NativePrefabAssetPath)
                ? NpcNativeBuildCoordinator.GetDefaultOutputPath(definition)
                : NormalizeAssetPath(request.NativePrefabAssetPath);
            NpcNativeBuildReceiptInspection receiptInspection =
                NpcNativeBuildReceiptUtility.InspectCurrent(
                    definition,
                    nativePath);
            NpcNativeBuildReceipt receipt = receiptInspection.Receipt;
            if (receipt == null)
                return Failure(
                    NpcSpawnableCratePreparationStatus.NativeReceiptMissing,
                    definition,
                    nativePath,
                    "NATIVE_RECEIPT_MISSING",
                    "Run Step 5A first. Step 5B requires its durable native-build receipt.");

            NpcBuildReadinessReport readiness = receiptInspection.Readiness;
            if (!readiness.ReadyForBuild)
                return Failure(
                    NpcSpawnableCratePreparationStatus.NativeReceiptStale,
                    definition,
                    nativePath,
                    "NATIVE_INPUT_NOT_READY",
                    "The current NPC authoring inputs no longer pass Step 4. Recheck readiness and rebuild Step 5A before preparing a crate.",
                    readiness,
                    receiptInspection.Validation);

            NpcNativeBuildReceiptValidationReport currentReceipt =
                receiptInspection.Validation;
            if (!currentReceipt.IsValid)
                return ReceiptFailure(
                    definition, nativePath, readiness, currentReceipt);

            return PrepareVerified(
                definition,
                receipt,
                nativePath,
                readiness,
                currentReceipt);
        }

        /// <summary>
        /// Read-only bound-asset summary used by the window after a domain
        /// reload. It never searches by title and never repairs assets.
        /// </summary>
        public static NpcSpawnableCratePreparationReport InspectBindings(
            NpcDefinition definition)
        {
            if (definition?.BuildProfile == null)
                return Failure(
                    NpcSpawnableCratePreparationStatus.InvalidRequest,
                    definition,
                    string.Empty,
                    "BUILD_PROFILE_MISSING",
                    "No NPC Build Profile is selected.");
            NpcBuildProfile build = definition.BuildProfile;
            if (!HasBothBindings(build))
                return Failure(
                    NpcSpawnableCratePreparationStatus.BindingInvalid,
                    definition,
                    NpcNativeBuildCoordinator.GetDefaultOutputPath(definition),
                    "SPAWNABLE_BINDINGS_INCOMPLETE",
                    "Step 5B has not created complete Pallet and Spawnable Crate bindings yet.");
            if (!TryResolveBindings(
                    build,
                    out Pallet pallet,
                    out SpawnableCrate crate,
                    out string detail))
                return Failure(
                    NpcSpawnableCratePreparationStatus.BoundAssetMissing,
                    definition,
                    NpcNativeBuildCoordinator.GetDefaultOutputPath(definition),
                    "BOUND_SPAWNABLE_ASSET_MISSING",
                    detail);
            NpcNativeBuildReceipt receipt =
                NpcNativeBuildReceiptUtility.LoadForPrefab(
                    NpcNativeBuildCoordinator.GetDefaultOutputPath(definition));
            string packaging = NpcPackagingFingerprintUtility.Compute(
                definition, receipt);
            return new NpcSpawnableCratePreparationReport(
                NpcSpawnableCratePreparationStatus.Succeeded,
                false,
                false,
                true,
                receipt?.NativePrefabAssetPath ?? string.Empty,
                pallet,
                crate,
                packaging,
                null,
                receipt == null
                    ? null
                    : NpcNativeBuildReceiptUtility.Validate(receipt, definition),
                new[]
                {
                    Info("SPAWNABLE_BINDINGS_FOUND",
                        "Found the bound Pallet and Spawnable Crate by their saved asset GUIDs."),
                });
        }

        internal static NpcSpawnableCratePreparationReport PrepareVerified(
            NpcDefinition definition,
            NpcNativeBuildReceipt receipt,
            string nativePath,
            NpcBuildReadinessReport readiness = null,
            NpcNativeBuildReceiptValidationReport receiptValidation = null)
        {
            NpcBuildProfile build = definition?.BuildProfile;
            if (definition == null || build == null || receipt == null)
                return Failure(
                    NpcSpawnableCratePreparationStatus.InvalidRequest,
                    definition,
                    nativePath,
                    "SPAWNABLE_TRANSACTION_INPUT_INVALID",
                    "The verified crate transaction is missing required inputs.",
                    readiness,
                    receiptValidation);

            string metadataError = ValidateMetadata(build);
            if (!string.IsNullOrEmpty(metadataError))
                return Failure(
                    NpcSpawnableCratePreparationStatus.InvalidRequest,
                    definition,
                    nativePath,
                    "SPAWNABLE_METADATA_INVALID",
                    metadataError,
                    readiness,
                    receiptValidation);

            bool palletBound = !string.IsNullOrWhiteSpace(build.PalletAssetGuid);
            bool crateBound = !string.IsNullOrWhiteSpace(
                build.SpawnableCrateAssetGuid);
            if (palletBound != crateBound)
                return Failure(
                    NpcSpawnableCratePreparationStatus.BindingInvalid,
                    definition,
                    nativePath,
                    "SPAWNABLE_BINDINGS_PARTIAL",
                    "The Build Profile contains only one asset binding. Restore both saved GUIDs or clear both before creating new assets.",
                    readiness,
                    receiptValidation);

            bool create = !palletBound;
            Pallet pallet = null;
            SpawnableCrate crate = null;
            if (!create && !TryResolveBindings(
                    build, out pallet, out crate, out string resolveError))
                return Failure(
                    NpcSpawnableCratePreparationStatus.BoundAssetMissing,
                    definition,
                    nativePath,
                    "BOUND_SPAWNABLE_ASSET_MISSING",
                    resolveError,
                    readiness,
                    receiptValidation);

            string buildPath = AssetDatabase.GetAssetPath(build);
            if (string.IsNullOrWhiteSpace(buildPath))
                return Failure(
                    NpcSpawnableCratePreparationStatus.InvalidRequest,
                    definition,
                    nativePath,
                    "BUILD_PROFILE_NOT_PERSISTENT",
                    "The Build Profile must be a persistent Project asset.",
                    readiness,
                    receiptValidation);

            AssetByteSnapshot buildBackup;
            try
            {
                buildBackup = AssetByteSnapshot.Capture(buildPath, build);
            }
            catch (Exception exception)
            {
                return Failure(
                    NpcSpawnableCratePreparationStatus.CommitFailed,
                    definition,
                    nativePath,
                    "BUILD_PROFILE_SNAPSHOT_FAILED",
                    "Step 5B could not snapshot the Build Profile before writing: "
                    + exception.Message,
                    readiness,
                    receiptValidation);
            }
            AssetByteSnapshot palletBackup = null;
            AssetByteSnapshot crateBackup = null;
            string palletPath = string.Empty;
            string cratePath = string.Empty;
            var createdAssets = new List<string>();
            var createdFolders = new List<string>();
            bool palletCreated = false;
            bool crateCreated = false;
            string expectedPalletBarcode = string.Empty;
            string expectedCrateBarcode = string.Empty;
            try
            {
                if (create)
                {
                    pallet = Pallet.CreatePallet(build.PalletTitle, build.Author);
                    crate = Crate.CreateCrateT<SpawnableCrate>(
                        pallet,
                        build.CrateTitle,
                        new MarrowAsset(receipt.NativePrefabAssetGuid));
                    if (pallet == null || crate == null)
                        throw new InvalidOperationException(
                            "The official Marrow creation API returned no Pallet or Spawnable Crate.");
                    RequireValidBarcode(pallet.Barcode, "Pallet");
                    RequireValidBarcode(crate.Barcode, "Spawnable Crate");
                    expectedPalletBarcode = pallet.Barcode.ID;
                    expectedCrateBarcode = crate.Barcode.ID;

                    string folder = GetAssetFolder(build, pallet.Barcode.ID);
                    createdFolders.AddRange(EnsureAssetFolder(folder));
                    palletPath = folder + "/_"
                                 + SafeAssetName(build.PalletTitle)
                                 + ".pallet.asset";
                    cratePath = folder + "/"
                                + SafeAssetName(build.CrateTitle)
                                + ".spawnable.asset";
                    RequireUnoccupied(palletPath);
                    RequireUnoccupied(cratePath);
                    AssetDatabase.CreateAsset(pallet, palletPath);
                    createdAssets.Add(palletPath);
                    palletCreated = true;
                    AssetDatabase.CreateAsset(crate, cratePath);
                    createdAssets.Add(cratePath);
                    crateCreated = true;
                    pallet.Crates.Add(crate);
                    build.SetSpawnableAssetBindings(
                        AssetDatabase.AssetPathToGUID(palletPath),
                        AssetDatabase.AssetPathToGUID(cratePath));
                }
                else
                {
                    palletPath = AssetDatabase.GetAssetPath(pallet);
                    cratePath = AssetDatabase.GetAssetPath(crate);
                    palletBackup = AssetByteSnapshot.Capture(palletPath, pallet);
                    crateBackup = AssetByteSnapshot.Capture(cratePath, crate);
                    expectedPalletBarcode = pallet.Barcode?.ID ?? string.Empty;
                    expectedCrateBarcode = crate.Barcode?.ID ?? string.Empty;
                }

                pallet.name = build.PalletTitle;
                pallet.Title = build.PalletTitle;
                pallet.Author = build.Author;
                pallet.Version = build.Version;
                pallet.Description = build.Description;
                crate.name = build.CrateTitle;
                crate.Title = build.CrateTitle;
                crate.Description = build.Description;
                crate.MainAsset = new MarrowAsset(receipt.NativePrefabAssetGuid);
                EnsureOneBoundCrate(pallet, crate);
                RestoreBacklinks(pallet);

                EditorUtility.SetDirty(crate);
                EditorUtility.SetDirty(pallet);
                EditorUtility.SetDirty(build);
                AssetDatabase.SaveAssetIfDirty(crate);
                AssetDatabase.SaveAssetIfDirty(pallet);
                AssetDatabase.SaveAssetIfDirty(build);
                Import(cratePath);
                Import(palletPath);
                Import(buildPath);

                NpcBuildProfile savedBuild =
                    AssetDatabase.LoadAssetAtPath<NpcBuildProfile>(buildPath);
                Pallet savedPallet = AssetDatabase.LoadAssetAtPath<Pallet>(palletPath);
                SpawnableCrate savedCrate =
                    AssetDatabase.LoadAssetAtPath<SpawnableCrate>(cratePath);
                if (savedBuild == null || savedPallet == null || savedCrate == null)
                    throw new PreparationValidationException(
                        "Unity could not reload all three saved Step 5B assets.");
                RestoreBacklinks(savedPallet);
                ValidateSaved(
                    savedBuild,
                    savedPallet,
                    savedCrate,
                    receipt,
                    expectedPalletBarcode,
                    expectedCrateBarcode);

                string packaging = NpcPackagingFingerprintUtility.Compute(
                    definition, receipt);
                return new NpcSpawnableCratePreparationReport(
                    NpcSpawnableCratePreparationStatus.Succeeded,
                    palletCreated,
                    crateCreated,
                    true,
                    receipt.NativePrefabAssetPath,
                    savedPallet,
                    savedCrate,
                    packaging,
                    readiness,
                    receiptValidation,
                    new[]
                    {
                        Info(
                            create
                                ? "SPAWNABLE_ASSETS_CREATED"
                                : "SPAWNABLE_ASSETS_UPDATED",
                            create
                                ? "Created and bound a new Marrow Pallet and Spawnable Crate. Their barcodes are now stable."
                                : "Updated the GUID-bound Pallet and Spawnable Crate without regenerating either barcode."),
                    });
            }
            catch (Exception exception)
            {
                bool restored = true;
                for (int index = createdAssets.Count - 1; index >= 0; index--)
                    if (AssetDatabase.LoadMainAssetAtPath(createdAssets[index]) != null
                        && !AssetDatabase.DeleteAsset(createdAssets[index]))
                        restored = false;
                if (crateBackup != null) restored &= crateBackup.Restore();
                if (palletBackup != null) restored &= palletBackup.Restore();
                restored &= buildBackup.Restore();
                Pallet restoredPallet = palletBackup == null
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Pallet>(palletBackup.Path);
                RestoreBacklinks(restoredPallet);
                PruneEmptyFolders(createdFolders);
                NpcSpawnableCratePreparationStatus status =
                    exception is PreparationValidationException
                        ? NpcSpawnableCratePreparationStatus.ValidationFailed
                        : NpcSpawnableCratePreparationStatus.CommitFailed;
                return new NpcSpawnableCratePreparationReport(
                    status,
                    palletCreated,
                    crateCreated,
                    restored,
                    nativePath,
                    restoredPallet,
                    crateBackup == null
                        ? null
                        : AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                            crateBackup.Path),
                    string.Empty,
                    readiness,
                    receiptValidation,
                    new[]
                    {
                        Error(
                            status == NpcSpawnableCratePreparationStatus.ValidationFailed
                                ? "SPAWNABLE_POST_SAVE_VALIDATION_FAILED"
                                : "SPAWNABLE_TRANSACTION_FAILED",
                            exception.GetType().Name + ": " + exception.Message
                            + (restored
                                ? " Previous assets were restored."
                                : " One or more previous assets could not be fully restored.")),
                    });
            }
            finally
            {
                if (pallet != null
                    && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(pallet)))
                    Object.DestroyImmediate(pallet);
                if (crate != null
                    && string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(crate)))
                    Object.DestroyImmediate(crate);
            }
        }

        private static void ValidateSaved(
            NpcBuildProfile build,
            Pallet pallet,
            SpawnableCrate crate,
            NpcNativeBuildReceipt receipt,
            string expectedPalletBarcode,
            string expectedCrateBarcode)
        {
            string palletPath = AssetDatabase.GetAssetPath(pallet);
            string cratePath = AssetDatabase.GetAssetPath(crate);
            string palletGuid = AssetDatabase.AssetPathToGUID(palletPath);
            string crateGuid = AssetDatabase.AssetPathToGUID(cratePath);
            if (!string.Equals(build.PalletAssetGuid, palletGuid,
                    StringComparison.Ordinal)
                || !string.Equals(build.SpawnableCrateAssetGuid, crateGuid,
                    StringComparison.Ordinal)
                || !string.Equals(AssetDatabase.GUIDToAssetPath(
                        build.PalletAssetGuid), palletPath, StringComparison.Ordinal)
                || !string.Equals(AssetDatabase.GUIDToAssetPath(
                        build.SpawnableCrateAssetGuid), cratePath,
                    StringComparison.Ordinal))
                throw new PreparationValidationException(
                    "The saved Build Profile GUID bindings do not resolve exactly to the saved Pallet and Spawnable Crate.");
            if (!string.Equals(pallet.Title, build.PalletTitle,
                    StringComparison.Ordinal)
                || !string.Equals(pallet.Author, build.Author,
                    StringComparison.Ordinal)
                || !string.Equals(pallet.Version, build.Version,
                    StringComparison.Ordinal)
                || !string.Equals(pallet.Description, build.Description,
                    StringComparison.Ordinal))
                throw new PreparationValidationException(
                    "The saved Pallet metadata does not match the Build Profile.");
            if (!string.Equals(crate.Title, build.CrateTitle,
                    StringComparison.Ordinal)
                || !string.Equals(crate.Description, build.Description,
                    StringComparison.Ordinal)
                || !string.Equals(crate.MainAsset?.AssetGUID,
                    receipt.NativePrefabAssetGuid, StringComparison.Ordinal))
                throw new PreparationValidationException(
                    "The saved Spawnable Crate metadata or Main Asset does not match the current native prefab.");
            if (!string.Equals(pallet.Barcode?.ID, expectedPalletBarcode,
                    StringComparison.Ordinal)
                || !string.Equals(crate.Barcode?.ID, expectedCrateBarcode,
                    StringComparison.Ordinal))
                throw new PreparationValidationException(
                    "A Pallet or Spawnable Crate barcode changed during Step 5B.");
            RequireValidBarcodeForValidation(pallet.Barcode, "Pallet");
            RequireValidBarcodeForValidation(crate.Barcode, "Spawnable Crate");
            if (pallet.Crates == null
                || pallet.Crates.Any(value => value == null)
                || pallet.Crates.Count(value => value == crate) != 1)
                throw new PreparationValidationException(
                    "The saved Pallet must contain the bound Spawnable Crate exactly once and no missing crate entries.");
            if (pallet.Crates.Any(value => value.Pallet != pallet))
                throw new PreparationValidationException(
                    "Every Crate.Pallet backlink must point to the saved Pallet.");
        }

        private static void EnsureOneBoundCrate(Pallet pallet, SpawnableCrate crate)
        {
            if (pallet.Crates == null) pallet.Crates = new List<Crate>();
            bool found = false;
            for (int index = pallet.Crates.Count - 1; index >= 0; index--)
            {
                if (pallet.Crates[index] != crate) continue;
                if (!found)
                {
                    found = true;
                    continue;
                }
                pallet.Crates.RemoveAt(index);
            }
            if (!found) pallet.Crates.Add(crate);
        }

        private static void RestoreBacklinks(Pallet pallet)
        {
            if (pallet?.Crates == null) return;
            foreach (Crate value in pallet.Crates)
                if (value != null) value.Pallet = pallet;
        }

        private static bool TryResolveBindings(
            NpcBuildProfile build,
            out Pallet pallet,
            out SpawnableCrate crate,
            out string detail)
        {
            pallet = null;
            crate = null;
            string palletPath = AssetDatabase.GUIDToAssetPath(
                build.PalletAssetGuid);
            string cratePath = AssetDatabase.GUIDToAssetPath(
                build.SpawnableCrateAssetGuid);
            if (string.IsNullOrWhiteSpace(palletPath))
            {
                detail = "The bound Pallet GUID no longer resolves to an asset. Step 5B will not replace it by title.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(cratePath))
            {
                detail = "The bound Spawnable Crate GUID no longer resolves to an asset. Step 5B will not replace it by title.";
                return false;
            }
            pallet = AssetDatabase.LoadAssetAtPath<Pallet>(palletPath);
            crate = AssetDatabase.LoadAssetAtPath<SpawnableCrate>(cratePath);
            if (pallet == null || crate == null)
            {
                detail = "A bound GUID resolves to the wrong asset type. Expected one Pallet and one Spawnable Crate.";
                return false;
            }
            if (pallet.Crates == null || !pallet.Crates.Contains(crate))
            {
                detail = "The two bound GUIDs do not describe one Pallet/Crate relationship. The bound Spawnable Crate is not listed in the bound Pallet, so Step 5B will not reparent it implicitly.";
                pallet = null;
                crate = null;
                return false;
            }
            detail = string.Empty;
            return true;
        }

        private static bool HasBothBindings(NpcBuildProfile build)
        {
            return build != null
                   && !string.IsNullOrWhiteSpace(build.PalletAssetGuid)
                   && !string.IsNullOrWhiteSpace(
                       build.SpawnableCrateAssetGuid);
        }

        private static string ValidateMetadata(NpcBuildProfile build)
        {
            if (string.IsNullOrWhiteSpace(build.Author))
                return "Build Profile Author is required.";
            if (string.IsNullOrWhiteSpace(build.PalletTitle))
                return "Build Profile Pallet Title is required.";
            if (string.IsNullOrWhiteSpace(build.CrateTitle))
                return "Build Profile Crate Title is required.";
            if (string.IsNullOrWhiteSpace(build.Version))
                return "Build Profile Version is required.";
            string folder = NormalizeAssetPath(build.GeneratedAssetFolder);
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal)
                || folder.IndexOf("/../", StringComparison.Ordinal) >= 0)
                return "Generated Asset Folder must be a safe path under Assets/.";
            return string.Empty;
        }

        private static string GetAssetFolder(
            NpcBuildProfile build,
            string palletBarcode)
        {
            return NormalizeAssetPath(build.GeneratedAssetFolder).TrimEnd('/')
                   + "/Pallet/" + SafeAssetName(palletBarcode);
        }

        private static void RequireUnoccupied(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null
                || File.Exists(AbsoluteAssetPath(path)))
                throw new InvalidOperationException(
                    "An asset already occupies the new Step 5B path '" + path
                    + "'. No title-based reuse was attempted.");
        }

        private static void RequireValidBarcode(Barcode barcode, string label)
        {
            if (!Barcode.IsValid(barcode) || !Barcode.IsValidSize(barcode))
                throw new InvalidOperationException(
                    label + " barcode is empty or exceeds the Marrow limit. Shorten the public author/title before creating assets.");
        }

        private static void RequireValidBarcodeForValidation(
            Barcode barcode,
            string label)
        {
            if (!Barcode.IsValid(barcode) || !Barcode.IsValidSize(barcode))
                throw new PreparationValidationException(
                    label + " barcode is empty or exceeds the Marrow limit.");
        }

        private static void Import(string path)
        {
            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private static List<string> EnsureAssetFolder(string folder)
        {
            folder = NormalizeAssetPath(folder).TrimEnd('/');
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Step 5B assets must be stored under Assets/.");
            var created = new List<string>();
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                    created.Add(next);
                }
                current = next;
            }
            return created;
        }

        private static void PruneEmptyFolders(IReadOnlyList<string> folders)
        {
            if (folders == null) return;
            for (int index = folders.Count - 1; index >= 0; index--)
            {
                string folder = folders[index];
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                string absolute = AbsoluteAssetPath(folder);
                string[] entries = Directory.Exists(absolute)
                    ? Directory.GetFileSystemEntries(absolute)
                    : Array.Empty<string>();
                if (entries.All(value => value.EndsWith(
                        ".meta", StringComparison.OrdinalIgnoreCase)))
                    AssetDatabase.DeleteAsset(folder);
            }
        }

        private static string AbsoluteAssetPath(string path)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new InvalidOperationException(
                    "Could not resolve the Unity Project root.");
            return Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        private static string SafeAssetName(string value)
        {
            var builder = new StringBuilder();
            foreach (char character in value ?? string.Empty)
                if (char.IsLetterOrDigit(character)
                    || character == '_' || character == '-')
                    builder.Append(character);
            if (builder.Length == 0) builder.Append("Npc");
            if (builder.Length > 64) builder.Length = 64;
            return builder.ToString();
        }

        private static string NormalizeAssetPath(string value)
        {
            return (value ?? string.Empty).Replace('\\', '/').Trim();
        }

        private static NpcSpawnableCratePreparationReport ReceiptFailure(
            NpcDefinition definition,
            string nativePath,
            NpcBuildReadinessReport readiness,
            NpcNativeBuildReceiptValidationReport validation)
        {
            bool authoringInputsChanged = validation != null
                && validation.Issues.Any(value =>
                    value.Code == "NATIVE_RECEIPT_DEFINITION_FINGERPRINT_CHANGED"
                    || value.Code == "NATIVE_RECEIPT_INPUT_FINGERPRINT_CHANGED"
                    || value.Code == "NATIVE_RECEIPT_CAPABILITIES_CHANGED");
            string detail = authoringInputsChanged
                ? "The generated NPC is out of date because its authoring settings changed."
                : validation == null
                    ? "The native-build receipt is stale."
                    : string.Join(" ", validation.Issues.Select(
                        value => value.Message));
            return Failure(
                NpcSpawnableCratePreparationStatus.NativeReceiptStale,
                definition,
                nativePath,
                "NATIVE_RECEIPT_STALE",
                detail + " Rebuild Step 5A before preparing a crate.",
                readiness,
                validation);
        }

        private static NpcSpawnableCratePreparationReport Failure(
            NpcSpawnableCratePreparationStatus status,
            NpcDefinition definition,
            string nativePath,
            string code,
            string message,
            NpcBuildReadinessReport readiness = null,
            NpcNativeBuildReceiptValidationReport receiptValidation = null)
        {
            return new NpcSpawnableCratePreparationReport(
                status,
                false,
                false,
                true,
                nativePath,
                null,
                null,
                string.Empty,
                readiness,
                receiptValidation,
                new[] { Error(code, message) });
        }

        private static NpcSpawnableCratePreparationMessage Info(
            string code,
            string message)
        {
            return new NpcSpawnableCratePreparationMessage(
                NpcSpawnableCratePreparationMessageSeverity.Info,
                code,
                message);
        }

        private static NpcSpawnableCratePreparationMessage Error(
            string code,
            string message)
        {
            return new NpcSpawnableCratePreparationMessage(
                NpcSpawnableCratePreparationMessageSeverity.Error,
                code,
                message);
        }

        private sealed class PreparationValidationException : Exception
        {
            public PreparationValidationException(string message) : base(message)
            {
            }
        }

        private sealed class AssetByteSnapshot
        {
            public string Path { get; }
            private readonly Object target;
            private readonly byte[] bytes;
            private readonly string json;
            private readonly bool wasDirty;
            private readonly string guid;

            private AssetByteSnapshot(
                string path,
                Object target,
                byte[] bytes,
                string json,
                bool wasDirty,
                string guid)
            {
                Path = path;
                this.target = target;
                this.bytes = bytes;
                this.json = json;
                this.wasDirty = wasDirty;
                this.guid = guid;
            }

            public static AssetByteSnapshot Capture(string path, Object target)
            {
                string absolute = AbsoluteAssetPath(path);
                if (target == null || !File.Exists(absolute))
                    throw new InvalidOperationException(
                        "Could not snapshot existing asset '" + path + "'.");
                return new AssetByteSnapshot(
                    path,
                    target,
                    File.ReadAllBytes(absolute),
                    EditorJsonUtility.ToJson(target, false),
                    EditorUtility.IsDirty(target),
                    AssetDatabase.AssetPathToGUID(path));
            }

            public bool Restore()
            {
                try
                {
                    File.WriteAllBytes(AbsoluteAssetPath(Path), bytes);
                    Import(Path);
                    if (target != null)
                    {
                        EditorJsonUtility.FromJsonOverwrite(json, target);
                        if (!wasDirty) EditorUtility.ClearDirty(target);
                    }
                    return string.Equals(
                               AssetDatabase.AssetPathToGUID(Path),
                               guid,
                               StringComparison.Ordinal)
                           && bytes.SequenceEqual(
                               File.ReadAllBytes(AbsoluteAssetPath(Path)))
                           && AssetDatabase.LoadMainAssetAtPath(Path) != null;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
