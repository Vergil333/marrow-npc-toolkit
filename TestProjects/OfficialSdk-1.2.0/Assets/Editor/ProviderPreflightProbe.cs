using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;

public static class ProviderPreflightProbe
{
    [Serializable]
    private sealed class Result
    {
        public bool ok;
        public string providerId;
        public string capabilities;
        public string detail;
        public Reference[] references;
    }

    [Serializable]
    private sealed class Reference
    {
        public string key;
        public string path;
        public string guid;
    }

    [Serializable]
    private sealed class Settings
    {
        public int schemaVersion = 4;
        public string behaviourTemplateGuid;
        public string locomotionReferenceGuid;
        public string animatorControllerGuid;
        public string baseEnemyConfigGuid;
        public string standingIdleGuid;
        public string jawStandingIdleGuid;
        public string openHandGuid;
        public string fistGuid;
        public string pistolGuid;
        public string pistolOffhandGuid;
        public string genericGripPoseGuid;
        public string cylinderGripPoseGuid;
        public string plantedFootMaterialGuid;
        public string liftedFootMaterialGuid;
    }

    public static void Run()
    {
        string resultPath = ReadArgument("-npcToolkitResult");
        if (string.IsNullOrWhiteSpace(resultPath)
            || !Path.IsPathRooted(resultPath))
            throw new ArgumentException(
                "Missing -npcToolkitResult <absolute JSON path>.");

        var output = new Result
        {
            providerId = "vergil333.bonelab-patch6",
            capabilities = NpcCompatibilityCapabilities.None.ToString(),
            detail = string.Empty,
        };

        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.SetCurrentDirectory(projectRoot);
            Reference[] references =
            {
                Resolve("behaviourTemplateGuid", "-npcToolkitBehaviourTemplate"),
                Resolve("locomotionReferenceGuid", "-npcToolkitLocomotionReference"),
                Resolve("animatorControllerGuid", "-npcToolkitAnimatorController"),
                Resolve("baseEnemyConfigGuid", "-npcToolkitBaseEnemyConfig"),
                Resolve("standingIdleGuid", "-npcToolkitStandingIdle"),
                Resolve("jawStandingIdleGuid", "-npcToolkitJawStandingIdle"),
                Resolve("openHandGuid", "-npcToolkitOpenHand"),
                Resolve("fistGuid", "-npcToolkitFist"),
                Resolve("pistolGuid", "-npcToolkitPistol"),
                Resolve("pistolOffhandGuid", "-npcToolkitPistolOffhand"),
                Resolve("genericGripPoseGuid", "-npcToolkitGenericGripPose"),
                Resolve("cylinderGripPoseGuid", "-npcToolkitCylinderGripPose"),
                Resolve("plantedFootMaterialGuid", "-npcToolkitPlantedFootMaterial"),
                Resolve("liftedFootMaterialGuid", "-npcToolkitLiftedFootMaterial"),
            };
            output.references = references;
            var settings = new Settings
            {
                behaviourTemplateGuid = references[0].guid,
                locomotionReferenceGuid = references[1].guid,
                animatorControllerGuid = references[2].guid,
                baseEnemyConfigGuid = references[3].guid,
                standingIdleGuid = references[4].guid,
                jawStandingIdleGuid = references[5].guid,
                openHandGuid = references[6].guid,
                fistGuid = references[7].guid,
                pistolGuid = references[8].guid,
                pistolOffhandGuid = references[9].guid,
                genericGripPoseGuid = references[10].guid,
                cylinderGripPoseGuid = references[11].guid,
                plantedFootMaterialGuid = references[12].guid,
                liftedFootMaterialGuid = references[13].guid,
            };
            string settingsPath = Path.Combine(
                projectRoot,
                "ProjectSettings/MarrowNpcToolkitPatch6BehaviourSettings.json");
            File.WriteAllText(settingsPath, JsonUtility.ToJson(settings, true));

            NpcCompatibilityProbeRegistry registry =
                NpcCompatibilityProbeRegistry.Default;
            registry.DiscoverProjectProbes();
            INpcCompatibilityProbe provider = registry.Probes.SingleOrDefault(
                value => string.Equals(
                    value.ProviderId,
                    output.providerId,
                    StringComparison.Ordinal));
            if (provider == null)
                throw new InvalidOperationException(
                    "The Patch 6 provider is not registered.");

            NpcCompatibilityProbeResult probe = provider.Probe();
            output.capabilities = probe.Capabilities.ToString();
            output.detail = probe.Detail;
            output.ok = probe.IsAvailable
                        && (probe.Capabilities & NpcCompatibilityCapabilities.All)
                        == NpcCompatibilityCapabilities.All;
            if (!output.ok)
                throw new InvalidOperationException(
                    "Provider preflight did not supply every capability. "
                    + output.detail);
        }
        catch (Exception exception)
        {
            output.ok = false;
            output.detail = string.IsNullOrWhiteSpace(output.detail)
                ? exception.ToString()
                : output.detail + "\n" + exception;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
            File.WriteAllText(resultPath, JsonUtility.ToJson(output, true));
            Debug.Log("MARROW_NPC_TOOLKIT_PROVIDER_PREFLIGHT_"
                      + (output.ok ? "PASS" : "FAIL") + "\n" + resultPath);
        }

        EditorApplication.Exit(output.ok ? 0 : 1);
    }

    private static Reference Resolve(string key, string argument)
    {
        string path = ReadArgument(argument).Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path)
            || !path.StartsWith("Assets/", StringComparison.Ordinal)
            || AssetDatabase.LoadMainAssetAtPath(path) == null)
            throw new InvalidOperationException(
                "Missing or invalid " + argument + " asset path: " + path);
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrWhiteSpace(guid))
            throw new InvalidOperationException(
                "The reference has no asset GUID: " + path);
        return new Reference { key = key, path = path, guid = guid };
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
