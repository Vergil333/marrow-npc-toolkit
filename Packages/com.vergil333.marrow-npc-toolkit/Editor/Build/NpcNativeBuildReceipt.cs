using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Validation;

namespace Vergil333.MarrowNpcToolkit.Editor.Build
{
    /// <summary>
    /// Durable proof of one successfully committed native-prefab transaction.
    /// The receipt is an Editor-only sidecar; the generated runtime prefab does
    /// not reference it or any toolkit type.
    /// </summary>
    public sealed class NpcNativeBuildReceipt : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField] private NpcDefinition definition;
        [SerializeField] private string definitionAssetGuid;
        [SerializeField] private string definitionFingerprint;
        [SerializeField] private string inputFingerprint;
        [SerializeField] private string providerId;
        [SerializeField] private NpcCompatibilityCapabilities requestedCapabilities;
        [SerializeField] private string nativePrefabAssetPath;
        [SerializeField] private string nativePrefabAssetGuid;
        [SerializeField] private string nativePrefabDependencyHash;
        [SerializeField] private string providerFingerprint;
        [SerializeField] private string outputFingerprint;
        [SerializeField] private string compatibilityProfileId;
        [SerializeField] private string toolkitVersion;
        [SerializeField] private string builtAtUtc;
        [SerializeField] private long builtAtUtcTicks;

        public int SchemaVersion => schemaVersion;
        public NpcDefinition Definition => definition;
        public string DefinitionAssetGuid => definitionAssetGuid;

        /// <summary>
        /// Fingerprint supplied by the definition/readiness layer. It is kept
        /// separate from InputFingerprint and from the packaging fingerprint,
        /// so release metadata edits do not invalidate native output.
        /// </summary>
        public string DefinitionFingerprint => definitionFingerprint;

        /// <summary>
        /// Exact provider-selection/build-input fingerprint used for this build.
        /// </summary>
        public string InputFingerprint => inputFingerprint;

        public string ProviderId => providerId;
        public NpcCompatibilityCapabilities RequestedCapabilities =>
            requestedCapabilities;
        public string NativePrefabAssetPath => nativePrefabAssetPath;
        public string NativePrefabAssetGuid => nativePrefabAssetGuid;
        public string NativePrefabDependencyHash => nativePrefabDependencyHash;
        public string ProviderFingerprint => providerFingerprint;
        public string OutputFingerprint => outputFingerprint;
        public string CompatibilityProfileId => compatibilityProfileId;
        public string ToolkitVersion => toolkitVersion;
        public string BuiltAtUtc => builtAtUtc;
        public long BuiltAtUtcTicks => builtAtUtcTicks;

        internal void Initialize(NpcNativeBuildReceiptData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            schemaVersion = CurrentSchemaVersion;
            definition = data.Definition;
            definitionAssetGuid = data.DefinitionAssetGuid;
            definitionFingerprint = data.DefinitionFingerprint;
            inputFingerprint = data.InputFingerprint;
            providerId = data.ProviderId;
            requestedCapabilities = data.RequestedCapabilities;
            nativePrefabAssetPath = data.NativePrefabAssetPath;
            nativePrefabAssetGuid = data.NativePrefabAssetGuid;
            nativePrefabDependencyHash = data.NativePrefabDependencyHash;
            providerFingerprint = data.ProviderFingerprint;
            outputFingerprint = data.OutputFingerprint;
            compatibilityProfileId = data.CompatibilityProfileId;
            toolkitVersion = data.ToolkitVersion;
            builtAtUtc = data.BuiltAtUtc;
            builtAtUtcTicks = data.BuiltAtUtcTicks;
        }
    }

    /// <summary>
    /// Immutable data handed from the deterministic build transaction to the
    /// receipt asset. DefinitionFingerprint and InputFingerprint deliberately
    /// remain distinct contracts.
    /// </summary>
    internal sealed class NpcNativeBuildReceiptData
    {
        public NpcDefinition Definition { get; }
        public string DefinitionAssetGuid { get; }
        public string DefinitionFingerprint { get; }
        public string InputFingerprint { get; }
        public string ProviderId { get; }
        public NpcCompatibilityCapabilities RequestedCapabilities { get; }
        public string NativePrefabAssetPath { get; }
        public string NativePrefabAssetGuid { get; }
        public string NativePrefabDependencyHash { get; }
        public string ProviderFingerprint { get; }
        public string OutputFingerprint { get; }
        public string CompatibilityProfileId { get; }
        public string ToolkitVersion { get; }
        public string BuiltAtUtc { get; }
        public long BuiltAtUtcTicks { get; }

        public NpcNativeBuildReceiptData(
            NpcDefinition definition,
            string definitionFingerprint,
            string inputFingerprint,
            string providerId,
            NpcCompatibilityCapabilities requestedCapabilities,
            string nativePrefabAssetPath,
            string nativePrefabAssetGuid,
            string nativePrefabDependencyHash,
            string providerFingerprint,
            string outputFingerprint,
            DateTime builtAtUtc)
        {
            Definition = definition;
            DefinitionAssetGuid = AssetDatabase.AssetPathToGUID(
                AssetDatabase.GetAssetPath(definition));
            DefinitionFingerprint = definitionFingerprint ?? string.Empty;
            InputFingerprint = inputFingerprint ?? string.Empty;
            ProviderId = providerId ?? string.Empty;
            RequestedCapabilities = requestedCapabilities;
            NativePrefabAssetPath = NormalizeAssetPath(nativePrefabAssetPath);
            NativePrefabAssetGuid = nativePrefabAssetGuid ?? string.Empty;
            NativePrefabDependencyHash = nativePrefabDependencyHash ?? string.Empty;
            ProviderFingerprint = providerFingerprint ?? string.Empty;
            OutputFingerprint = outputFingerprint ?? string.Empty;
            CompatibilityProfileId = definition?.BuildProfile == null
                ? string.Empty
                : definition.BuildProfile.CompatibilityProfileId ?? string.Empty;
            ToolkitVersion = NpcToolkitVersion.Current;
            DateTime utc = builtAtUtc.Kind == DateTimeKind.Utc
                ? builtAtUtc
                : builtAtUtc.ToUniversalTime();
            BuiltAtUtc = utc.ToString("o", CultureInfo.InvariantCulture);
            BuiltAtUtcTicks = utc.Ticks;
        }

        private static string NormalizeAssetPath(string value)
        {
            return (value ?? string.Empty).Replace('\\', '/').Trim();
        }
    }

    public sealed class NpcNativeBuildReceiptIssue
    {
        public string Code { get; }
        public string Message { get; }

        internal NpcNativeBuildReceiptIssue(string code, string message)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            return Code + ": " + Message;
        }
    }

    public sealed class NpcNativeBuildReceiptValidationReport
    {
        private readonly NpcNativeBuildReceiptIssue[] issues;

        public NpcNativeBuildReceipt Receipt { get; }
        public bool IsValid => issues.Length == 0;
        public IReadOnlyList<NpcNativeBuildReceiptIssue> Issues => issues;

        internal NpcNativeBuildReceiptValidationReport(
            NpcNativeBuildReceipt receipt,
            IEnumerable<NpcNativeBuildReceiptIssue> issues)
        {
            Receipt = receipt;
            this.issues = (issues ?? Array.Empty<NpcNativeBuildReceiptIssue>())
                .Where(value => value != null)
                .ToArray();
        }
    }

    public sealed class NpcNativeBuildReceiptInspection
    {
        public NpcNativeBuildReceipt Receipt { get; }
        public NpcBuildReadinessReport Readiness { get; }
        public NpcNativeBuildReceiptValidationReport Validation { get; }
        public NpcCompatibilityCapabilities RequestedCapabilities { get; }
        public bool HasReceipt => Receipt != null;
        public bool IsCurrent => Readiness != null
                                 && Readiness.ReadyForBuild
                                 && Validation != null
                                 && Validation.IsValid;

        internal NpcNativeBuildReceiptInspection(
            NpcNativeBuildReceipt receipt,
            NpcBuildReadinessReport readiness,
            NpcNativeBuildReceiptValidationReport validation,
            NpcCompatibilityCapabilities requestedCapabilities)
        {
            Receipt = receipt;
            Readiness = readiness;
            Validation = validation;
            RequestedCapabilities = requestedCapabilities;
        }
    }

    /// <summary>
    /// Read-only receipt lookup and validation for future packing/runtime-test
    /// workflows. Validation never imports, saves, dirties, or repairs assets.
    /// </summary>
    public static class NpcNativeBuildReceiptUtility
    {
        public const string ReceiptSuffix = ".NativeBuildReceipt.asset";

        public static string GetReceiptPath(string nativePrefabAssetPath)
        {
            string path = NormalizeAssetPath(nativePrefabAssetPath);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return path.Substring(0, path.Length - ".prefab".Length)
                   + ReceiptSuffix;
        }

        public static NpcNativeBuildReceipt LoadForPrefab(
            string nativePrefabAssetPath)
        {
            string receiptPath = GetReceiptPath(nativePrefabAssetPath);
            return string.IsNullOrEmpty(receiptPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<NpcNativeBuildReceipt>(receiptPath);
        }

        /// <summary>
        /// Recomputes the current authoring/readiness contract and validates a
        /// saved receipt against it. This is the shared read-only gate for the
        /// Step 5 display and crate preparation.
        /// </summary>
        public static NpcNativeBuildReceiptInspection InspectCurrent(
            NpcDefinition definition,
            string nativePrefabAssetPath = null)
        {
            NpcCompatibilityCapabilities capabilities =
                NpcCompatibilityRequirements.ForDefinition(definition);
            if (definition == null)
                return new NpcNativeBuildReceiptInspection(
                    null,
                    null,
                    Validate(null),
                    capabilities);

            string nativePath = string.IsNullOrWhiteSpace(nativePrefabAssetPath)
                ? NpcNativeBuildCoordinator.GetDefaultOutputPath(definition)
                : NormalizeAssetPath(nativePrefabAssetPath);
            NpcNativeBuildReceipt receipt = LoadForPrefab(nativePath);
            if (receipt == null)
                return new NpcNativeBuildReceiptInspection(
                    null,
                    null,
                    Validate(null, definition),
                    capabilities);

            NpcBuildReadinessReport readiness =
                NpcBuildReadinessDoctor.Validate(definition);
            string expectedInput =
                NpcNativeBuildCoordinator.ComputeNativeInputFingerprint(
                    definition,
                    readiness.Fingerprint,
                    receipt.ProviderId,
                    capabilities);
            NpcNativeBuildReceiptValidationReport validation = Validate(
                receipt,
                definition,
                readiness.Fingerprint,
                expectedInput,
                receipt.ProviderId,
                capabilities);
            return new NpcNativeBuildReceiptInspection(
                receipt,
                readiness,
                validation,
                capabilities);
        }

        public static NpcNativeBuildReceiptValidationReport Validate(
            NpcNativeBuildReceipt receipt,
            NpcDefinition expectedDefinition = null,
            string expectedDefinitionFingerprint = null,
            string expectedInputFingerprint = null,
            string expectedProviderId = null,
            NpcCompatibilityCapabilities? expectedCapabilities = null)
        {
            var issues = new List<NpcNativeBuildReceiptIssue>();
            if (receipt == null)
            {
                Add(issues, "NATIVE_RECEIPT_MISSING",
                    "The native-build receipt is missing.");
                return new NpcNativeBuildReceiptValidationReport(null, issues);
            }

            if (receipt.SchemaVersion != NpcNativeBuildReceipt.CurrentSchemaVersion)
                Add(issues, "NATIVE_RECEIPT_SCHEMA_UNSUPPORTED",
                    "The native-build receipt schema is not supported by this toolkit version.");
            if (receipt.Definition == null)
                Add(issues, "NATIVE_RECEIPT_DEFINITION_MISSING",
                    "The receipt no longer references its NPC Definition.");
            if (string.IsNullOrWhiteSpace(receipt.DefinitionAssetGuid))
                Add(issues, "NATIVE_RECEIPT_DEFINITION_GUID_MISSING",
                    "The receipt has no persistent NPC Definition GUID.");
            else if (receipt.Definition != null)
            {
                string currentDefinitionGuid = AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(receipt.Definition));
                if (!string.Equals(currentDefinitionGuid,
                        receipt.DefinitionAssetGuid, StringComparison.Ordinal))
                    Add(issues, "NATIVE_RECEIPT_DEFINITION_GUID_MISMATCH",
                        "The receipt's NPC Definition reference no longer matches its recorded GUID.");
            }
            Require(issues, receipt.DefinitionFingerprint,
                "NATIVE_RECEIPT_DEFINITION_FINGERPRINT_MISSING",
                "The receipt has no definition fingerprint.");
            Require(issues, receipt.InputFingerprint,
                "NATIVE_RECEIPT_INPUT_FINGERPRINT_MISSING",
                "The receipt has no native-build input fingerprint.");
            Require(issues, receipt.ProviderId,
                "NATIVE_RECEIPT_PROVIDER_MISSING",
                "The receipt has no native provider ID.");
            if ((receipt.RequestedCapabilities
                 & NpcCompatibilityCapabilities.CoreAnatomy) == 0)
                Add(issues, "NATIVE_RECEIPT_CAPABILITIES_INVALID",
                    "The recorded native capabilities do not include Core Anatomy.");
            Require(issues, receipt.ProviderFingerprint,
                "NATIVE_RECEIPT_PROVIDER_FINGERPRINT_MISSING",
                "The receipt has no provider structural fingerprint.");
            Require(issues, receipt.OutputFingerprint,
                "NATIVE_RECEIPT_OUTPUT_FINGERPRINT_MISSING",
                "The receipt has no combined native output fingerprint.");
            Require(issues, receipt.CompatibilityProfileId,
                "NATIVE_RECEIPT_COMPATIBILITY_PROFILE_MISSING",
                "The receipt has no compatibility profile ID.");
            Require(issues, receipt.ToolkitVersion,
                "NATIVE_RECEIPT_TOOLKIT_VERSION_MISSING",
                "The receipt has no toolkit version.");
            DateTime parsedUtc;
            if (receipt.BuiltAtUtcTicks <= 0
                || !DateTime.TryParseExact(
                    receipt.BuiltAtUtc,
                    "o",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out parsedUtc)
                || parsedUtc.ToUniversalTime().Ticks != receipt.BuiltAtUtcTicks)
                Add(issues, "NATIVE_RECEIPT_TIMESTAMP_INVALID",
                    "The receipt's UTC build timestamp is invalid.");

            ValidatePrefab(receipt, issues);
            ValidateExpectedInputs(
                receipt,
                expectedDefinition,
                expectedDefinitionFingerprint,
                expectedInputFingerprint,
                expectedProviderId,
                expectedCapabilities,
                issues);
            return new NpcNativeBuildReceiptValidationReport(receipt, issues);
        }

        private static void ValidatePrefab(
            NpcNativeBuildReceipt receipt,
            ICollection<NpcNativeBuildReceiptIssue> issues)
        {
            string prefabPath = NormalizeAssetPath(receipt.NativePrefabAssetPath);
            if (!prefabPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "NATIVE_RECEIPT_PREFAB_PATH_INVALID",
                    "The receipt does not contain a valid prefab path under Assets/.");
                return;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Add(issues, "NATIVE_RECEIPT_PREFAB_MISSING",
                    "The native prefab recorded by the receipt is missing.");
                return;
            }
            string currentGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (!string.Equals(
                    currentGuid,
                    receipt.NativePrefabAssetGuid,
                    StringComparison.Ordinal))
                Add(issues, "NATIVE_RECEIPT_PREFAB_GUID_MISMATCH",
                    "The native prefab GUID no longer matches the successful build.");
            string currentHash = AssetDatabase.GetAssetDependencyHash(prefabPath)
                .ToString();
            if (!string.Equals(
                    currentHash,
                    receipt.NativePrefabDependencyHash,
                    StringComparison.Ordinal))
                Add(issues, "NATIVE_RECEIPT_PREFAB_DEPENDENCY_CHANGED",
                    "The native prefab or one of its dependencies changed after the successful build.");

            string receiptPath = AssetDatabase.GetAssetPath(receipt);
            string expectedReceiptPath = GetReceiptPath(prefabPath);
            if (!string.IsNullOrWhiteSpace(receiptPath)
                && !string.Equals(
                    NormalizeAssetPath(receiptPath),
                    expectedReceiptPath,
                    StringComparison.Ordinal))
                Add(issues, "NATIVE_RECEIPT_LOCATION_INVALID",
                    "The receipt is not stored beside its native prefab.");
        }

        private static void ValidateExpectedInputs(
            NpcNativeBuildReceipt receipt,
            NpcDefinition expectedDefinition,
            string expectedDefinitionFingerprint,
            string expectedInputFingerprint,
            string expectedProviderId,
            NpcCompatibilityCapabilities? expectedCapabilities,
            ICollection<NpcNativeBuildReceiptIssue> issues)
        {
            if (expectedDefinition != null && receipt.Definition != expectedDefinition)
                Add(issues, "NATIVE_RECEIPT_DEFINITION_MISMATCH",
                    "The receipt belongs to a different NPC Definition.");
            CompareOptional(issues,
                expectedDefinitionFingerprint,
                receipt.DefinitionFingerprint,
                "NATIVE_RECEIPT_DEFINITION_FINGERPRINT_CHANGED",
                "The NPC Definition fingerprint changed after the native build.");
            CompareOptional(issues,
                expectedInputFingerprint,
                receipt.InputFingerprint,
                "NATIVE_RECEIPT_INPUT_FINGERPRINT_CHANGED",
                "The native-build input fingerprint no longer matches.");
            CompareOptional(issues,
                expectedProviderId,
                receipt.ProviderId,
                "NATIVE_RECEIPT_PROVIDER_CHANGED",
                "The requested native provider no longer matches the receipt.");
            if (expectedCapabilities.HasValue
                && expectedCapabilities.Value != receipt.RequestedCapabilities)
                Add(issues, "NATIVE_RECEIPT_CAPABILITIES_CHANGED",
                    "The requested native capabilities no longer match the receipt.");
        }

        private static void CompareOptional(
            ICollection<NpcNativeBuildReceiptIssue> issues,
            string expected,
            string actual,
            string code,
            string message)
        {
            if (expected != null
                && !string.Equals(expected, actual, StringComparison.Ordinal))
                Add(issues, code, message);
        }

        private static void Require(
            ICollection<NpcNativeBuildReceiptIssue> issues,
            string value,
            string code,
            string message)
        {
            if (string.IsNullOrWhiteSpace(value))
                Add(issues, code, message);
        }

        private static void Add(
            ICollection<NpcNativeBuildReceiptIssue> issues,
            string code,
            string message)
        {
            issues.Add(new NpcNativeBuildReceiptIssue(code, message));
        }

        private static string NormalizeAssetPath(string value)
        {
            return (value ?? string.Empty).Replace('\\', '/').Trim();
        }
    }
}
