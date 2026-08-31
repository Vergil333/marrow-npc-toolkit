using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SLZ.MarrowEditor;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;
using Vergil333.MarrowNpcToolkit.Editor.Build;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Movement;
using Vergil333.MarrowNpcToolkit.Editor.Validation;

public static class ExactCandidateBuildProbe
{
    [Serializable]
    private sealed class Result
    {
        public bool ok;
        public string definitionPath;
        public string platform;
        public string startingBuildTarget;
        public string endingBuildTarget;
        public string startingStandaloneBackend;
        public string endingStandaloneBackend;
        public string baselineFingerprint;
        public string previewFingerprint;
        public string previewAssetDependencyHash;
        public string movementFingerprint;
        public string movementRecipeFingerprint;
        public string readinessFingerprint;
        public string nativeInputFingerprint;
        public string nativeOutputFingerprint;
        public string nativePrefabPath;
        public string nativePrefabAssetGuid;
        public string nativePrefabDependencyHash;
        public string palletBarcode;
        public string crateBarcode;
        public string packagingFingerprint;
        public string outputDirectory;
        public int outputFileCount;
        public string[] messages;
    }

    public static void Run()
    {
        string definitionPath = ReadArgument("-npcToolkitDefinition")
            .Replace('\\', '/');
        string resultPath = ReadArgument("-npcToolkitResult");
        string platformText = ReadArgument("-npcToolkitPlatform");
        var output = new Result
        {
            definitionPath = definitionPath,
            platform = platformText,
            messages = Array.Empty<string>(),
        };

        try
        {
            if (string.IsNullOrWhiteSpace(resultPath)
                || !Path.IsPathRooted(resultPath))
                throw new InvalidOperationException(
                    "Missing -npcToolkitResult <absolute JSON path>.");
            NpcDefinition definition = AssetDatabase.LoadAssetAtPath<NpcDefinition>(
                definitionPath);
            if (definition == null || definition.BuildProfile == null
                                   || definition.MovementProfile == null)
                throw new InvalidOperationException(
                    "The supplied NPC Definition is incomplete: " + definitionPath);

            if (!Enum.TryParse(platformText, true, out NpcTargetPlatform platform))
                throw new InvalidOperationException(
                    "-npcToolkitPlatform must be Quest or Windows.");
            BuildTarget startingBuildTarget =
                EditorUserBuildSettings.activeBuildTarget;
            ScriptingImplementation startingStandaloneBackend =
                PlayerSettings.GetScriptingBackend(
                    BuildTargetGroup.Standalone);
            output.startingBuildTarget = startingBuildTarget.ToString();
            output.startingStandaloneBackend =
                startingStandaloneBackend.ToString();
            var buildData = new SerializedObject(definition.BuildProfile);
            SerializedProperty target = buildData.FindProperty("targetPlatform");
            if (target == null)
                throw new InvalidOperationException(
                    "The Build Profile has no targetPlatform field.");
            target.enumValueIndex = (int)platform;
            buildData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition.BuildProfile);
            AssetDatabase.SaveAssets();

            // Project repairs can import or rewrite assets. Finish them before
            // creating the deterministic native-build and crate receipts.
            EnsureAndroidAstc();
            if (!MarrowProjectValidation.ValidateProject())
            {
                var issues = new List<
                    MarrowProjectValidation.MarrowValidationRule>();
                MarrowProjectValidation.GetIssues(issues);
                MarrowProjectValidation.FixIssues(issues);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.SaveAssets();
            }
            if (!MarrowProjectValidation.ValidateProject())
            {
                var remaining = new List<
                    MarrowProjectValidation.MarrowValidationRule>();
                MarrowProjectValidation.GetIssues(remaining);
                throw new InvalidOperationException(
                    "Marrow project validation failed after its official fixes: "
                    + string.Join(" | ", remaining.Select(
                        value => value.message)));
            }

            NpcBaselineFitReport baseline = NpcBaselineFitter.Fit(
                definition,
                overwriteReviewed: false,
                registerUndo: false);
            if (!baseline.Success)
                throw new InvalidOperationException(
                    "Baseline receipt refresh failed: "
                    + string.Join(" | ", baseline.Issues));
            output.baselineFingerprint = baseline.Fingerprint;
            EditorUtility.SetDirty(definition.AnatomyProfile);
            AssetDatabase.SaveAssets();

            NpcPhysicsPreviewReport preview = NpcPhysicsPreviewBuilder.Build(definition);
            if (!preview.Success)
                throw new InvalidOperationException(
                    "Physics preview failed: " + string.Join(" | ", preview.Issues));
            output.previewFingerprint = preview.Fingerprint;
            output.previewAssetDependencyHash = AssetDatabase
                .GetAssetDependencyHash(preview.AssetPath)
                .ToString();

            NpcMovementFitReport movement = NpcMovementProfileFitter.Fit(
                definition,
                resetReviewedTuning: true,
                registerUndo: false);
            if (!movement.Success)
                throw new InvalidOperationException(
                    "Movement recalculation failed: "
                    + string.Join(" | ", movement.Issues));
            output.movementFingerprint = movement.Fingerprint;

            NpcMovementAuthoringProviderSelection movementProvider =
                NpcMovementAuthoringProviderRegistry.Default.Resolve(
                    definition.BuildProfile,
                    "vergil333.bonelab-patch6");
            if (!movementProvider.CanPrepare)
                throw new InvalidOperationException(
                    "Movement provider unavailable: " + movementProvider.Detail);
            NpcMovementAuthoringResult movementRecipe =
                movementProvider.Provider.Prepare(
                    definition,
                    definition.MovementProfile);
            if (movementRecipe == null || !movementRecipe.Success)
                throw new InvalidOperationException(
                    "Movement recipe failed: "
                    + (movementRecipe == null
                        ? "no result"
                        : string.Join(" | ", movementRecipe.Messages)));
            output.movementRecipeFingerprint = movementRecipe.RecipeFingerprint;
            EditorUtility.SetDirty(definition.MovementProfile);
            AssetDatabase.SaveAssets();

            NpcBuildReadinessReport readiness =
                NpcBuildReadinessDoctor.Validate(definition);
            output.readinessFingerprint = readiness.Fingerprint;
            if (!readiness.ReadyForBuild)
                throw new InvalidOperationException(
                    "Readiness failed: " + string.Join(" | ", readiness.Issues.Select(
                        value => value.Code + ": " + value.Message)));

            NpcCompatibilityCapabilities required =
                NpcCompatibilityRequirements.ForDefinition(definition);
            NpcNativeBuildReport native = NpcNativeBuildCoordinator.Build(
                new NpcNativeBuildRequest(
                    definition,
                    required,
                    "vergil333.bonelab-patch6"));
            output.nativeInputFingerprint = native.InputFingerprint;
            output.nativeOutputFingerprint = native.OutputFingerprint;
            output.nativePrefabPath = native.OutputPrefabPath;
            if (!native.Success)
                throw new InvalidOperationException(
                    "Native build failed (" + native.Status + "): "
                    + string.Join(" | ", native.Messages.Select(
                        value => value.Code + ": " + value.Message)));
            NpcNativeBuildReceipt receipt = NpcNativeBuildReceiptUtility
                .LoadForPrefab(native.OutputPrefabPath);
            if (receipt == null)
                throw new InvalidOperationException(
                    "The successful native build did not save its receipt.");
            output.nativePrefabAssetGuid = receipt.NativePrefabAssetGuid;
            output.nativePrefabDependencyHash =
                receipt.NativePrefabDependencyHash;

            NpcSpawnableCratePreparationReport preparation =
                NpcSpawnableCratePreparationCoordinator.Prepare(
                    new NpcSpawnableCratePreparationRequest(definition));
            output.palletBarcode = preparation.PalletBarcode;
            output.crateBarcode = preparation.CrateBarcode;
            output.packagingFingerprint = preparation.PackagingFingerprint;
            if (!preparation.Success)
                throw new InvalidOperationException(
                    "Spawnable preparation failed (" + preparation.Status + "): "
                    + string.Join(" | ", preparation.Messages.Select(
                        value => value.Code + ": " + value.Message)));

            NpcPalletPackReport pack = NpcPalletPackCoordinator.Pack(definition);
            output.packagingFingerprint = pack.PackagingFingerprint;
            output.outputDirectory = pack.Output?.OutputDirectory ?? string.Empty;
            output.outputFileCount = pack.Output?.FileCount ?? 0;
            output.messages = pack.Messages.Select(
                value => value.Code + ": " + value.Message).ToArray();
            if (!pack.Success)
                throw new InvalidOperationException(
                    "Pallet pack failed (" + pack.Status + "): "
                    + string.Join(" | ", output.messages));
            if (EditorUserBuildSettings.activeBuildTarget
                != startingBuildTarget)
                throw new InvalidOperationException(
                    "Step 5C did not restore the starting build target "
                    + startingBuildTarget + "; Unity is on "
                    + EditorUserBuildSettings.activeBuildTarget + ".");
            ScriptingImplementation endingStandaloneBackend =
                PlayerSettings.GetScriptingBackend(
                    BuildTargetGroup.Standalone);
            if (endingStandaloneBackend != startingStandaloneBackend)
                throw new InvalidOperationException(
                    "Step 5C did not restore the starting Standalone backend "
                    + startingStandaloneBackend + "; Unity is on "
                    + endingStandaloneBackend + ".");

            output.ok = true;
        }
        catch (Exception exception)
        {
            output.ok = false;
            output.messages = output.messages.Concat(new[]
            {
                exception.ToString(),
            }).ToArray();
            Debug.LogException(exception);
        }
        finally
        {
            output.endingBuildTarget =
                EditorUserBuildSettings.activeBuildTarget.ToString();
            output.endingStandaloneBackend = PlayerSettings
                .GetScriptingBackend(BuildTargetGroup.Standalone)
                .ToString();
            if (!string.IsNullOrWhiteSpace(resultPath)
                && Path.IsPathRooted(resultPath))
            {
                string directory = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(resultPath, JsonUtility.ToJson(output, true));
            }
            Debug.Log("MARROW_NPC_TOOLKIT_EXACT_CANDIDATE_"
                      + (output.ok ? "PASS" : "FAIL") + " "
                      + output.platform);
        }

        EditorApplication.Exit(output.ok ? 0 : 1);
    }

    private static void EnsureAndroidAstc()
    {
        MethodInfo setter = typeof(PlayerSettings).GetMethod(
            "SetDefaultTextureCompressionFormat",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (setter == null)
            throw new MissingMethodException(
                typeof(PlayerSettings).FullName,
                "SetDefaultTextureCompressionFormat");

        // Marrow SDK 1.2.0 validates numeric value 3 as ASTC, but its repair
        // accidentally invokes the getter with setter arguments. Apply the
        // same required value through Unity's actual setter before validation.
        setter.Invoke(null, new object[] { BuildTargetGroup.Android, 3 });
    }

    private static string ReadArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.Ordinal))
                return args[index + 1];
        return string.Empty;
    }
}
