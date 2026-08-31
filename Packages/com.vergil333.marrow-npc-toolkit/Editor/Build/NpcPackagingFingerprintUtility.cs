using System;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Validation;

namespace Vergil333.MarrowNpcToolkit.Editor.Build
{
    /// <summary>
    /// Stable identity for the future pallet/crate packing step. Publication
    /// metadata is intentionally kept out of physics/native readiness and
    /// belongs here instead.
    /// </summary>
    public static class NpcPackagingFingerprintUtility
    {
        public static string Compute(NpcDefinition definition)
        {
            string outputPath =
                NpcNativeBuildCoordinator.GetDefaultOutputPath(definition);
            NpcNativeBuildReceipt receipt = string.IsNullOrWhiteSpace(outputPath)
                ? null
                : NpcNativeBuildReceiptUtility.LoadForPrefab(outputPath);
            return Compute(definition, receipt);
        }

        /// <summary>
        /// Computes a deterministic packaging identity from current public
        /// metadata and stable fields in a successful native-build receipt.
        /// Receipt asset bytes and build timestamps are deliberately excluded,
        /// so rebuilding identical native content does not force a repack.
        /// </summary>
        public static string Compute(
            NpcDefinition definition,
            NpcNativeBuildReceipt receipt)
        {
            var value = new StringBuilder("npc-packaging-v4|");
            if (definition == null)
            {
                Append(value, "<no-definition>");
                AppendReceipt(value, receipt);
                return Hash128.Compute(value.ToString()).ToString();
            }

            string definitionPath = AssetDatabase.GetAssetPath(definition);
            Append(value, string.IsNullOrWhiteSpace(definitionPath)
                ? "<transient-definition>"
                : AssetDatabase.AssetPathToGUID(definitionPath));
            Append(value, definition.SourceAssetGuid);
            NpcBuildProfile build = definition.BuildProfile;
            if (build == null)
            {
                Append(value, "<no-build-profile>");
            }
            else
            {
                Append(value, build.Author);
                Append(value, build.PalletTitle);
                Append(value, build.CrateTitle);
                Append(value, build.Description);
                Append(value, build.Version);
                Append(value, ((int)build.TargetPlatform)
                    .ToString(CultureInfo.InvariantCulture));
                Append(value, NormalizeAssetPath(build.GeneratedAssetFolder));
                Append(value, build.CompatibilityProfileId);
                Append(value, build.PalletAssetGuid);
                Append(value, build.SpawnableCrateAssetGuid);
            }
            Append(value, NpcBuildReadinessDoctor.ComputeMovementFingerprint(
                definition.MovementProfile));
            AppendReceipt(value, receipt);
            return Hash128.Compute(value.ToString()).ToString();
        }

        private static void AppendReceipt(
            StringBuilder value,
            NpcNativeBuildReceipt receipt)
        {
            if (receipt == null)
            {
                Append(value, "<no-native-receipt>");
                return;
            }

            Append(value, receipt.SchemaVersion.ToString(
                CultureInfo.InvariantCulture));
            Append(value, receipt.DefinitionAssetGuid);
            Append(value, receipt.DefinitionFingerprint);
            Append(value, receipt.InputFingerprint);
            Append(value, receipt.ProviderId);
            Append(value, ((int)receipt.RequestedCapabilities).ToString(
                CultureInfo.InvariantCulture));
            Append(value, NormalizeAssetPath(receipt.NativePrefabAssetPath));
            Append(value, receipt.NativePrefabAssetGuid);
            Append(value, receipt.ProviderFingerprint);
            Append(value, receipt.OutputFingerprint);
            Append(value, receipt.CompatibilityProfileId);
        }

        private static void Append(StringBuilder builder, string field)
        {
            string safe = field ?? string.Empty;
            builder.Append(safe.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(safe).Append('|');
        }

        private static string NormalizeAssetPath(string value)
        {
            return (value ?? string.Empty).Replace('\\', '/').Trim();
        }
    }
}
