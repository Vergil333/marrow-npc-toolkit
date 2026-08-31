using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Explicit, project-local provenance for native Patch 6 behaviour data.
    /// The public toolkit never ships or guesses game assets. A project owner
    /// deliberately selects a legally available template and data assets, and
    /// the provider resolves only those stable GUIDs.
    /// </summary>
    internal static class MarrowNpcToolkitPatch6BehaviourSettings
    {
        internal const string SettingsPath =
            "ProjectSettings/MarrowNpcToolkitPatch6BehaviourSettings.json";

        [Serializable]
        internal sealed class Data
        {
            public int schemaVersion = 4;
            public string behaviourTemplateGuid = string.Empty;
            public string locomotionReferenceGuid = string.Empty;
            public string animatorControllerGuid = string.Empty;
            public string baseEnemyConfigGuid = string.Empty;
            public string standingIdleGuid = string.Empty;
            public string jawStandingIdleGuid = string.Empty;
            public string openHandGuid = string.Empty;
            public string fistGuid = string.Empty;
            public string pistolGuid = string.Empty;
            public string pistolOffhandGuid = string.Empty;
            public string genericGripPoseGuid = string.Empty;
            public string cylinderGripPoseGuid = string.Empty;
            public string plantedFootMaterialGuid = string.Empty;
            public string liftedFootMaterialGuid = string.Empty;
        }

        internal sealed class JawResolved
        {
            public Data Source { get; }
            public UnityEngine.Object StandingIdle { get; }
            public UnityEngine.Object GenericGripPose { get; }

            public JawResolved(
                Data source,
                UnityEngine.Object standingIdle,
                UnityEngine.Object genericGripPose)
            {
                Source = source;
                StandingIdle = standingIdle;
                GenericGripPose = genericGripPose;
            }
        }

        internal sealed class GripResolved
        {
            public Data Source { get; }
            public UnityEngine.Object GenericGripPose { get; }
            public UnityEngine.Object CylinderGripPose { get; }

            public GripResolved(
                Data source,
                UnityEngine.Object genericGripPose,
                UnityEngine.Object cylinderGripPose)
            {
                Source = source;
                GenericGripPose = genericGripPose;
                CylinderGripPose = cylinderGripPose;
            }
        }

        internal sealed class Resolved
        {
            public Data Source { get; }
            public GameObject BehaviourTemplate { get; }
            public GameObject LocomotionReference { get; }
            public RuntimeAnimatorController AnimatorController { get; }
            public UnityEngine.Object BaseEnemyConfig { get; }
            public UnityEngine.Object StandingIdle { get; }
            public UnityEngine.Object OpenHand { get; }
            public UnityEngine.Object Fist { get; }
            public UnityEngine.Object Pistol { get; }
            public UnityEngine.Object PistolOffhand { get; }
            public PhysicMaterial PlantedFootMaterial { get; }
            public PhysicMaterial LiftedFootMaterial { get; }

            public Resolved(
                Data source,
                GameObject behaviourTemplate,
                GameObject locomotionReference,
                RuntimeAnimatorController animatorController,
                UnityEngine.Object baseEnemyConfig,
                UnityEngine.Object standingIdle,
                UnityEngine.Object openHand,
                UnityEngine.Object fist,
                UnityEngine.Object pistol,
                UnityEngine.Object pistolOffhand,
                PhysicMaterial plantedFootMaterial,
                PhysicMaterial liftedFootMaterial)
            {
                Source = source;
                BehaviourTemplate = behaviourTemplate;
                LocomotionReference = locomotionReference;
                AnimatorController = animatorController;
                BaseEnemyConfig = baseEnemyConfig;
                StandingIdle = standingIdle;
                OpenHand = openHand;
                Fist = fist;
                Pistol = pistol;
                PistolOffhand = pistolOffhand;
                PlantedFootMaterial = plantedFootMaterial;
                LiftedFootMaterial = liftedFootMaterial;
            }
        }

        internal static Data Load()
        {
            if (!File.Exists(SettingsPath))
                return new Data();
            try
            {
                Data value = JsonUtility.FromJson<Data>(
                    File.ReadAllText(SettingsPath));
                return value ?? new Data();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Patch 6 behaviour settings are not valid JSON: "
                    + exception.Message,
                    exception);
            }
        }

        internal static bool TryResolve(
            Type baseConfigType,
            Type enemyPoseType,
            Type handPoseType,
            out Resolved resolved,
            out string detail)
        {
            return TryResolve(
                baseConfigType,
                enemyPoseType,
                handPoseType,
                false,
                out resolved,
                out detail);
        }

        internal static bool TryResolve(
            Type baseConfigType,
            Type enemyPoseType,
            Type handPoseType,
            bool physicalJaw,
            out Resolved resolved,
            out string detail)
        {
            resolved = null;
            detail = string.Empty;
            Data data;
            try
            {
                data = Load();
            }
            catch (Exception exception)
            {
                detail = exception.Message;
                return false;
            }

            var missing = new System.Collections.Generic.List<string>();
            GameObject template = ResolveAsset(
                data.behaviourTemplateGuid, typeof(GameObject), "Behaviour Template",
                missing) as GameObject;
            string locomotionGuid = string.IsNullOrWhiteSpace(
                    data.locomotionReferenceGuid)
                ? data.behaviourTemplateGuid
                : data.locomotionReferenceGuid;
            GameObject locomotionReference = ResolveAsset(
                locomotionGuid,
                typeof(GameObject),
                "Stock Locomotion Reference",
                missing) as GameObject;
            RuntimeAnimatorController controller = ResolveAsset(
                data.animatorControllerGuid, typeof(RuntimeAnimatorController),
                "Animator Controller", missing) as RuntimeAnimatorController;
            UnityEngine.Object config = ResolveAsset(
                data.baseEnemyConfigGuid, baseConfigType, "Base Enemy Config", missing);
            UnityEngine.Object standing = ResolveAsset(
                physicalJaw ? data.jawStandingIdleGuid : data.standingIdleGuid,
                enemyPoseType,
                physicalJaw ? "17-body Physical Jaw Standing Pose"
                    : "16-body Standing Pose",
                missing);
            UnityEngine.Object open = ResolveAsset(
                data.openHandGuid, handPoseType, "Open Hand Pose", missing);
            UnityEngine.Object fist = ResolveAsset(
                data.fistGuid, handPoseType, "Fist Hand Pose", missing);
            UnityEngine.Object pistol = ResolveAsset(
                data.pistolGuid, handPoseType, "Pistol Hand Pose", missing);
            UnityEngine.Object offhand = ResolveAsset(
                data.pistolOffhandGuid, handPoseType,
                "Offhand Pistol Hand Pose", missing);
            PhysicMaterial planted = ResolveAsset(
                data.plantedFootMaterialGuid, typeof(PhysicMaterial),
                "Planted Foot Material", missing) as PhysicMaterial;
            PhysicMaterial lifted = ResolveAsset(
                data.liftedFootMaterialGuid, typeof(PhysicMaterial),
                "Lifted Foot Material", missing) as PhysicMaterial;
            if (missing.Count != 0)
            {
                detail = "Open Project Settings > Marrow NPC Toolkit > Patch 6 "
                         + "Behaviour and assign: " + string.Join(", ", missing) + ".";
                return false;
            }

            resolved = new Resolved(
                data, template, locomotionReference, controller, config, standing,
                open, fist, pistol, offhand, planted, lifted);
            detail = "The explicit project-local Patch 6 behaviour profile is complete.";
            return true;
        }

        internal static bool TryResolveJaw(
            Type enemyPoseType,
            Type handPoseType,
            out JawResolved resolved,
            out string detail)
        {
            resolved = null;
            detail = string.Empty;
            Data data;
            try
            {
                data = Load();
            }
            catch (Exception exception)
            {
                detail = exception.Message;
                return false;
            }

            var missing = new System.Collections.Generic.List<string>();
            UnityEngine.Object standing = ResolveAsset(
                data.jawStandingIdleGuid,
                enemyPoseType,
                "17-body Physical Jaw Standing Pose",
                missing);
            UnityEngine.Object genericGrip = ResolveAsset(
                data.genericGripPoseGuid,
                handPoseType,
                "Generic Body-Grab Pose",
                missing);
            if (missing.Count != 0)
            {
                detail = "Open Project Settings > Marrow NPC Toolkit > Patch 6 "
                         + "Behaviour and assign: " + string.Join(", ", missing)
                         + ".";
                return false;
            }

            resolved = new JawResolved(data, standing, genericGrip);
            detail = "The explicit project-local Physical Jaw pose and grip "
                     + "inputs are complete.";
            return true;
        }

        internal static bool TryResolveGrips(
            Type handPoseType,
            out GripResolved resolved,
            out string detail)
        {
            resolved = null;
            detail = string.Empty;
            Data data;
            try
            {
                data = Load();
            }
            catch (Exception exception)
            {
                detail = exception.Message;
                return false;
            }

            var missing = new System.Collections.Generic.List<string>();
            UnityEngine.Object generic = ResolveAsset(
                data.genericGripPoseGuid,
                handPoseType,
                "Generic Body-Grab Pose",
                missing);
            UnityEngine.Object cylinder = ResolveAsset(
                data.cylinderGripPoseGuid,
                handPoseType,
                "Cylinder Limb-Grab Pose",
                missing);
            if (missing.Count != 0)
            {
                detail = "Open Project Settings > Marrow NPC Toolkit > Patch 6 "
                         + "Behaviour and assign: " + string.Join(", ", missing) + ".";
                return false;
            }

            resolved = new GripResolved(data, generic, cylinder);
            detail = "The explicit project-local Patch 6 body-grab poses are complete.";
            return true;
        }

        private static UnityEngine.Object ResolveAsset(
            string guid,
            Type expectedType,
            string label,
            System.Collections.Generic.ICollection<string> missing)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                missing.Add(label);
                return null;
            }
            string path = AssetDatabase.GUIDToAssetPath(guid.Trim());
            UnityEngine.Object value = string.IsNullOrWhiteSpace(path)
                ? null
                : AssetDatabase.LoadAssetAtPath(path, expectedType);
            if (value == null || !expectedType.IsInstanceOfType(value))
            {
                missing.Add(label);
                return null;
            }
            return value;
        }

        private static string GuidOf(UnityEngine.Object value)
        {
            if (value == null)
                return string.Empty;
            string path = AssetDatabase.GetAssetPath(value);
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(path);
        }

        private static void Save(Data value)
        {
            string json = JsonUtility.ToJson(value ?? new Data(), true) + "\n";
            File.WriteAllText(SettingsPath, json);
        }

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            Data data = null;
            GameObject template = null;
            GameObject locomotionReference = null;
            RuntimeAnimatorController controller = null;
            UnityEngine.Object config = null;
            UnityEngine.Object standing = null;
            UnityEngine.Object jawStanding = null;
            UnityEngine.Object open = null;
            UnityEngine.Object fist = null;
            UnityEngine.Object pistol = null;
            UnityEngine.Object offhand = null;
            UnityEngine.Object genericGripPose = null;
            UnityEngine.Object cylinderGripPose = null;
            PhysicMaterial planted = null;
            PhysicMaterial lifted = null;

            void Reload()
            {
                data = Load();
                template = LoadLoose(data.behaviourTemplateGuid, typeof(GameObject))
                    as GameObject;
                locomotionReference = LoadLoose(
                    string.IsNullOrWhiteSpace(data.locomotionReferenceGuid)
                        ? data.behaviourTemplateGuid
                        : data.locomotionReferenceGuid,
                    typeof(GameObject)) as GameObject;
                controller = LoadLoose(
                    data.animatorControllerGuid,
                    typeof(RuntimeAnimatorController)) as RuntimeAnimatorController;
                config = LoadLoose(data.baseEnemyConfigGuid, typeof(UnityEngine.Object));
                standing = LoadLoose(data.standingIdleGuid, typeof(UnityEngine.Object));
                jawStanding = LoadLoose(
                    data.jawStandingIdleGuid, typeof(UnityEngine.Object));
                open = LoadLoose(data.openHandGuid, typeof(UnityEngine.Object));
                fist = LoadLoose(data.fistGuid, typeof(UnityEngine.Object));
                pistol = LoadLoose(data.pistolGuid, typeof(UnityEngine.Object));
                offhand = LoadLoose(
                    data.pistolOffhandGuid, typeof(UnityEngine.Object));
                genericGripPose = LoadLoose(
                    data.genericGripPoseGuid, typeof(UnityEngine.Object));
                cylinderGripPose = LoadLoose(
                    data.cylinderGripPoseGuid, typeof(UnityEngine.Object));
                planted = LoadLoose(
                    data.plantedFootMaterialGuid,
                    typeof(PhysicMaterial)) as PhysicMaterial;
                lifted = LoadLoose(
                    data.liftedFootMaterialGuid,
                    typeof(PhysicMaterial)) as PhysicMaterial;
            }

            return new SettingsProvider(
                "Project/Marrow NPC Toolkit/Patch 6 Behaviour",
                SettingsScope.Project)
            {
                label = "Patch 6 Behaviour",
                activateHandler = (_, __) => Reload(),
                guiHandler = _ =>
                {
                    if (data == null)
                        Reload();
                    EditorGUILayout.HelpBox(
                        "These are deliberate project-local inputs. The toolkit "
                        + "does not bundle, search for, or redistribute native game "
                        + "assets. The core standing pose must contain exactly 16 "
                        + "entries; Physical Jaw uses its own explicit 17-entry pose. "
                        + "The Behaviour Template supplies the compatible runtime "
                        + "graph. The separate Stock Locomotion Reference supplies "
                        + "only LiteLoco gait curves for proportional adaptation.",
                        MessageType.Info);
                    MarrowNpcToolkitPatch6DeclarationBootstrap.Status
                        declarationStatus =
                            MarrowNpcToolkitPatch6DeclarationBootstrap.GetStatus();
                    EditorGUILayout.HelpBox(
                        declarationStatus.Detail,
                        declarationStatus.IsReady
                            ? MessageType.Info
                            : MessageType.Warning);
                    if (GUILayout.Button(
                        declarationStatus.HasInstalledFiles
                            ? "Review / Update Patch 6 Project Declarations"
                            : "Install Patch 6 Project Declarations"))
                    {
                        MarrowNpcToolkitPatch6DeclarationBootstrap.InstallOrUpdate(
                            true);
                    }
                    EditorGUILayout.Space();
                    template = (GameObject)EditorGUILayout.ObjectField(
                        "Behaviour Template", template, typeof(GameObject), false);
                    locomotionReference = (GameObject)EditorGUILayout.ObjectField(
                        "Stock Locomotion Reference",
                        locomotionReference,
                        typeof(GameObject),
                        false);
                    controller = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                        "Animator Controller", controller,
                        typeof(RuntimeAnimatorController), false);
                    config = EditorGUILayout.ObjectField(
                        "Base Enemy Config", config, typeof(UnityEngine.Object), false);
                    standing = EditorGUILayout.ObjectField(
                        "16-body Standing Pose", standing,
                        typeof(UnityEngine.Object), false);
                    jawStanding = EditorGUILayout.ObjectField(
                        "17-body Jaw Standing Pose", jawStanding,
                        typeof(UnityEngine.Object), false);
                    open = EditorGUILayout.ObjectField(
                        "Open Hand Pose", open, typeof(UnityEngine.Object), false);
                    fist = EditorGUILayout.ObjectField(
                        "Fist Hand Pose", fist, typeof(UnityEngine.Object), false);
                    pistol = EditorGUILayout.ObjectField(
                        "Pistol Hand Pose", pistol, typeof(UnityEngine.Object), false);
                    offhand = EditorGUILayout.ObjectField(
                        "Offhand Pistol Pose", offhand,
                        typeof(UnityEngine.Object), false);
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(
                        "Player Body Grabs", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "These two HandPose assets shape the player's hands while "
                        + "grabbing the NPC. They are separate from the NPC's own "
                        + "Open/Fist/Pistol hand-animation poses above.",
                        MessageType.None);
                    genericGripPose = EditorGUILayout.ObjectField(
                        "Generic Body-Grab Pose", genericGripPose,
                        typeof(UnityEngine.Object), false);
                    cylinderGripPose = EditorGUILayout.ObjectField(
                        "Cylinder Limb-Grab Pose", cylinderGripPose,
                        typeof(UnityEngine.Object), false);
                    planted = (PhysicMaterial)EditorGUILayout.ObjectField(
                        "Planted Foot Material", planted,
                        typeof(PhysicMaterial), false);
                    lifted = (PhysicMaterial)EditorGUILayout.ObjectField(
                        "Lifted Foot Material", lifted,
                        typeof(PhysicMaterial), false);

                    using (new EditorGUI.DisabledScope(template == null))
                    {
                        if (GUILayout.Button("Read Required Data From Template"))
                            ReadTemplateDefaults(
                                template, ref controller, ref config, ref standing,
                                ref open, ref fist, ref pistol, ref offhand,
                                ref planted, ref lifted);
                    }
                    if (GUILayout.Button("Save Patch 6 Behaviour Settings"))
                    {
                        data.schemaVersion = 4;
                        data.behaviourTemplateGuid = GuidOf(template);
                        data.locomotionReferenceGuid = GuidOf(locomotionReference);
                        data.animatorControllerGuid = GuidOf(controller);
                        data.baseEnemyConfigGuid = GuidOf(config);
                        data.standingIdleGuid = GuidOf(standing);
                        data.jawStandingIdleGuid = GuidOf(jawStanding);
                        data.openHandGuid = GuidOf(open);
                        data.fistGuid = GuidOf(fist);
                        data.pistolGuid = GuidOf(pistol);
                        data.pistolOffhandGuid = GuidOf(offhand);
                        data.genericGripPoseGuid = GuidOf(genericGripPose);
                        data.cylinderGripPoseGuid = GuidOf(cylinderGripPose);
                        data.plantedFootMaterialGuid = GuidOf(planted);
                        data.liftedFootMaterialGuid = GuidOf(lifted);
                        Save(data);
                    }
                },
                keywords = new System.Collections.Generic.HashSet<string>(new[]
                {
                    "NPC", "Patch 6", "PowerLegs", "Behaviour", "Hand Pose",
                    "Grip", "Body Grab", "Physical Jaw", "Standing Pose",
                    "Locomotion", "Gait",
                }),
            };
        }

        private static UnityEngine.Object LoadLoose(string guid, Type type)
        {
            string path = string.IsNullOrWhiteSpace(guid)
                ? string.Empty
                : AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrWhiteSpace(path)
                ? null
                : AssetDatabase.LoadAssetAtPath(path, type);
        }

        private static void ReadTemplateDefaults(
            GameObject template,
            ref RuntimeAnimatorController controller,
            ref UnityEngine.Object config,
            ref UnityEngine.Object standing,
            ref UnityEngine.Object open,
            ref UnityEngine.Object fist,
            ref UnityEngine.Object pistol,
            ref UnityEngine.Object offhand,
            ref PhysicMaterial planted,
            ref PhysicMaterial lifted)
        {
            Type powerType = ResolveComponentType(
                "PuppetMasta.BehaviourPowerLegs", "Assembly-CSharp");
            Component[] powers = template.GetComponentsInChildren(powerType, true)
                .Cast<Component>().ToArray();
            if (powers.Length != 1)
                throw new InvalidOperationException(
                    "The selected template must contain exactly one BehaviourPowerLegs.");
            var serialized = new SerializedObject(powers[0]);
            Animator[] animators = template.GetComponentsInChildren<Animator>(true)
                .Where(value => value.avatar != null && value.avatar.isHuman)
                .ToArray();
            if (animators.Length != 1
                || animators[0].runtimeAnimatorController == null)
                throw new InvalidOperationException(
                    "The selected template needs one Humanoid Animator with a controller.");
            controller = animators[0].runtimeAnimatorController;
            config = RequireObject(serialized, "prefabConfig");
            standing = RequireObject(serialized, "standingIdle");
            open = RequireObject(serialized, "handPoser.OpenHand");
            fist = RequireObject(serialized, "handPoser.Fist");
            pistol = RequireObject(serialized, "handPoser.Pistol");
            offhand = RequireObject(serialized, "handPoser.PistolOffhand");

            Type locoType = ResolveComponentType(
                "SLZ.Marrow.Mechanics.LiteLoco", "SLZ.Marrow");
            Component[] locos = template.GetComponentsInChildren(locoType, true)
                .Cast<Component>().ToArray();
            if (locos.Length != 1)
                throw new InvalidOperationException(
                    "The selected template needs exactly one LiteLoco.");
            SerializedProperty steps = new SerializedObject(locos[0])
                .FindProperty("stepGroups")?.GetArrayElementAtIndex(0)
                .FindPropertyRelative("footsteps");
            if (steps == null || steps.arraySize != 2)
                throw new InvalidOperationException(
                    "The selected template needs one two-foot LiteLoco step group.");
            SerializedProperty first = steps.GetArrayElementAtIndex(0);
            Collider sourceFoot = first.FindPropertyRelative("footCollider")
                ?.objectReferenceValue as Collider;
            planted = sourceFoot == null ? null : sourceFoot.sharedMaterial;
            lifted = first.FindPropertyRelative("liftedMat")
                ?.objectReferenceValue as PhysicMaterial;
            if (planted == null || lifted == null)
                throw new InvalidOperationException(
                    "The selected template has no persistent planted/lifted foot materials.");
        }

        private static UnityEngine.Object RequireObject(
            SerializedObject owner,
            string path)
        {
            SerializedProperty property = owner.FindProperty(path);
            if (property == null
                || property.propertyType != SerializedPropertyType.ObjectReference
                || property.objectReferenceValue == null)
                throw new InvalidOperationException(
                    "The selected template has no persistent " + path + " asset.");
            return property.objectReferenceValue;
        }

        private static Type ResolveComponentType(string fullName, string assemblyName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(value => string.Equals(
                    value.GetName().Name, assemblyName, StringComparison.Ordinal))
                .Select(value => value.GetType(fullName, false))
                .FirstOrDefault(value => value != null);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                throw new TypeLoadException(
                    fullName + " is unavailable from " + assemblyName + ".");
            return type;
        }
    }
}
