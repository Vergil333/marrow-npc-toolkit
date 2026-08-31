using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;

public static class ReleaseSmokeProbe
{
    private static readonly string[] MarrowTypes =
    {
        "SLZ.Marrow.AI.AIBrain",
        "SLZ.Marrow.AI.TriggerRefProxy",
        "SLZ.Marrow.Combat.VisualDamageController",
        "SLZ.Marrow.Data.EnemyPoseData",
        "SLZ.Marrow.Data.HandPoseData",
        "SLZ.Marrow.InteractableHost",
        "SLZ.Marrow.InteractableHostManager",
        "SLZ.Marrow.Mechanics.LiteLoco",
        "SLZ.Marrow.Pool.Poolee",
        "SLZ.Marrow.PuppetMasta.BehaviourBaseNav",
        "SLZ.Marrow.PuppetMasta.PuppetMaster"
    };

    private static readonly string[] GameTypes =
    {
        "PuppetMasta.BehaviourPowerLegs",
        "RealisticEyeMovements.EyeAndHeadAnimator",
        "RealisticEyeMovements.LookTargetController",
        "SLZ.Bonelab.AgentLinkControl",
        "SLZ.Combat.VisualDamageReceiver",
        "SLZ.Utilities.GenericSpawnDelayEvent",
        "SLZ.VRMK.LimbIKSlz"
    };

    public static void InstallDeclarations()
    {
        const string bootstrapTypeName =
            "Vergil333.MarrowNpcToolkit.ProjectCompatibility."
            + "MarrowNpcToolkitPatch6DeclarationBootstrap, "
            + "Vergil333.MarrowNpcToolkit.Patch6.Editor";
        Type bootstrap = Type.GetType(bootstrapTypeName, throwOnError: false);
        MethodInfo install = bootstrap == null
            ? null
            : bootstrap.GetMethod(
                "InstallOrUpdate",
                BindingFlags.Static | BindingFlags.NonPublic);
        bool installed = install != null
                         && (bool)install.Invoke(null, new object[] { false });
        if (!installed)
        {
            Debug.LogError(
                "MARROW_NPC_TOOLKIT_DECLARATION_BOOTSTRAP_FAIL");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("MARROW_NPC_TOOLKIT_DECLARATION_BOOTSTRAP_PASS");
        EditorApplication.Exit(0);
    }

    public static void Run()
    {
        var failures = new List<string>();
        RequireAssembly("Vergil333.MarrowNpcToolkit.Patch6.Editor", failures);
        RequireTypes(MarrowTypes, "SLZ.Marrow", failures);
        RequireTypes(GameTypes, "Assembly-CSharp", failures);

        string[] providerIds = NpcCompatibilityProbeRegistry.Default.Probes
            .Select(value => value == null ? string.Empty : value.ProviderId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        int providerCount = providerIds.Count(value =>
            string.Equals(value, "vergil333.bonelab-patch6",
                StringComparison.Ordinal));
        if (providerCount != 1)
        {
            failures.Add(
                "Expected exactly one vergil333.bonelab-patch6 provider; found "
                + providerCount + ". Registered providers: "
                + string.Join(", ", providerIds));
        }

        if (failures.Count == 0)
        {
            Debug.Log("MARROW_NPC_TOOLKIT_RELEASE_SMOKE_PASS");
            EditorApplication.Exit(0);
            return;
        }

        foreach (string failure in failures)
            Debug.LogError("MARROW_NPC_TOOLKIT_RELEASE_SMOKE_FAIL: " + failure);
        EditorApplication.Exit(1);
    }

    private static void RequireAssembly(
        string assemblyName,
        ICollection<string> failures)
    {
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(value =>
            string.Equals(value.GetName().Name, assemblyName,
                StringComparison.Ordinal)))
            failures.Add("Assembly is not loaded: " + assemblyName);
    }

    private static void RequireTypes(
        IEnumerable<string> typeNames,
        string assemblyName,
        ICollection<string> failures)
    {
        foreach (string typeName in typeNames)
        {
            Type type = Type.GetType(
                typeName + ", " + assemblyName,
                throwOnError: false);
            if (type == null)
                failures.Add("Type is unavailable: " + typeName + ", "
                             + assemblyName);
        }
    }
}
