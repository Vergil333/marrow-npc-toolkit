using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Build;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Project-local Patch 6 AI/pooling shell. Native declarations and authored
    /// data remain outside the public package; the selected template contributes
    /// value data only, while every scene reference is rebound to the staged NPC.
    /// </summary>
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        private static readonly int[] NativeHealthGroups =
        {
            0, 3, 3, 3, 4, 4, 4, 0, 0, 5, 1, 1, 1, 2, 2, 2,
        };

        private static IReadOnlyList<int> HealthGroupsFor(
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            return roles != null && roles.ContainsKey(HumanBodyBones.Jaw)
                ? NativeHealthGroups.Concat(new[] { 5 }).ToArray()
                : NativeHealthGroups;
        }

        private static readonly string[] NativeSilentSfxArrays =
        {
            "agro", "unAgro", "painSmall", "painBig", "death", "jumpCharge",
            "jump", "smallEffort", "mediumEffort", "largeEffort", "attack1",
            "attackLand1", "attack2", "impactHead", "impactSpine", "impactLimb",
        };

        private static readonly string[] NativeHumanoidIkReferences =
        {
            "lfClav", "lfAc", "lfUpperTwist", "lfTwist", "lfWrist", "lfHand",
            "rtClav", "rtAc", "rtUpperTwist", "rtTwist", "rtWrist", "rtHand",
            "skinnedSpine1", "neck1", "neck2", "neckTop", "spine1", "spine2",
            "spineTop", "neck", "head", "eyeL", "eyeR",
        };

        private static NativeBehaviourShell ConfigureBehaviourShell(
            NpcNativeBuildContext context,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component entity,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowBodies,
            Component puppet)
        {
            BehaviourTypes types = BehaviourTypes.Resolve();
            MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings =
                RequireBehaviourSettings(
                    types, roles.ContainsKey(HumanBodyBones.Jaw));
            ValidateBehaviourTemplate(settings.BehaviourTemplate, types);
            ValidateLocomotionReference(settings.LocomotionReference, types);
            Patch6MovementBuildSettings movement =
                Patch6MovementBuildSettings.Resolve(
                    context.Definition,
                    settings,
                    types.BaseEnemyConfig,
                    types.EnemyPoseData,
                    roles.ContainsKey(HumanBodyBones.Jaw),
                    true);
            EnsureNoBehaviourComponents(context.OutputRoot, types);

            ConfigureTargetAnimator(animator, settings.AnimatorController);
            Transform avatarRoot = RequireMovementAvatarRoot(
                context.AnimationRoot, animator);
            BehaviourGraph graph = CreateBehaviourGraph(
                context.OutputRoot, avatarRoot, context.Definition, animator, roles,
                settings.BehaviourTemplate, types, movement);

            Component poolee = AddNative(
                context.OutputRoot, types.Poolee, "Poolee");
            Component brain = AddNative(
                context.OutputRoot, types.AIBrain, "AIBrain");
            Component liteLoco = CopyTemplateComponent(
                RequireTemplateComponent(
                    settings.BehaviourTemplate, types.LiteLoco, "LiteLoco"),
                graph.AiRig.gameObject,
                "LiteLoco");
            Component navAgent = CopyTemplateComponent(
                RequireTemplateComponent(
                    settings.BehaviourTemplate, types.NavMeshAgent,
                    "NavMeshAgent"),
                graph.AiRig.gameObject,
                "NavMeshAgent");
            ConfigureNavAgent(
                navAgent, context.OutputRoot.transform, avatarRoot, movement);
            Component footstepSfx = CopyTemplateComponent(
                RequireTemplateComponent(
                    settings.BehaviourTemplate, types.FootstepSfx,
                    "FootstepSFX"),
                graph.FootstepSfx.gameObject,
                "FootstepSFX");
            ConfigureSilentFootstepSfx(footstepSfx);
            ConfigureLiteLoco(
                liteLoco, graph, roles, settings, movement, footstepSfx);

            Component powerLegs = CopyTemplateComponent(
                RequireTemplateComponent(
                    settings.BehaviourTemplate, types.PowerLegs,
                    "BehaviourPowerLegs"),
                graph.PowerHolder.gameObject,
                "BehaviourPowerLegs");
            SphereCollider vision = graph.PowerHolder.gameObject
                .AddComponent<SphereCollider>();
            vision.enabled = true;
            vision.isTrigger = true;
            vision.radius = 5f;
            vision.center = new Vector3(0f, 0f, 4f);

            AudioSource impactSource = graph.ImpactSource.gameObject
                .AddComponent<AudioSource>();
            ConfigureSilentImpactSource(impactSource);
            IReadOnlyDictionary<HumanBodyBones, Component> limbSolvers =
                ConfigureLimbSolvers(
                    settings.BehaviourTemplate, roles, graph, types);
            ConfigurePowerLegs(
                powerLegs,
                puppet,
                poolee,
                graph,
                animator,
                roles,
                settings,
                movement,
                impactSource,
                limbSolvers);

            InteractionShell interaction = ConfigureInteractionShell(
                context.OutputRoot,
                roles,
                entity,
                marrowBodies,
                puppet,
                poolee,
                brain,
                powerLegs,
                navAgent);

            var shell = new NativeBehaviourShell(
                settings,
                movement,
                graph,
                poolee,
                brain,
                liteLoco,
                navAgent,
                footstepSfx,
                powerLegs,
                vision,
                impactSource,
                limbSolvers,
                interaction);
            ValidateBehaviourShell(
                context.OutputRoot,
                context.AnimationRoot,
                context.PhysicsRoot,
                context.Definition,
                animator,
                roles,
                entity,
                marrowBodies,
                puppet,
                shell);
            ValidateNoExternalSceneReferences(context.OutputRoot);
            return shell;
        }

        private static NativeBehaviourShell ResolveBehaviourShell(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            NpcDefinition definition,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component entity,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowBodies,
            Component puppet)
        {
            BehaviourTypes types = BehaviourTypes.Resolve();
            MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings =
                RequireBehaviourSettings(
                    types, roles.ContainsKey(HumanBodyBones.Jaw));
            Patch6MovementBuildSettings movement =
                Patch6MovementBuildSettings.Resolve(
                    definition,
                    settings,
                    types.BaseEnemyConfig,
                    types.EnemyPoseData,
                    roles.ContainsKey(HumanBodyBones.Jaw),
                    false);
            ValidateGeneratedKneeHingesToLocomotion(
                definition,
                animator,
                roles,
                settings.AnimatorController);
            Transform aiRig = RequireDirectChild(outputRoot.transform, "AiRig");
            BehaviourGraph graph = ResolveBehaviourGraph(
                outputRoot, roles, aiRig);
            Component poolee = RequireOnlyComponent(
                outputRoot, types.Poolee, "Poolee");
            Component brain = RequireOnlyComponent(
                outputRoot, types.AIBrain, "AIBrain");
            Component liteLoco = RequireOnlyComponent(
                aiRig.gameObject, types.LiteLoco, "LiteLoco");
            Component navAgent = RequireOnlyComponent(
                aiRig.gameObject, types.NavMeshAgent, "NavMeshAgent");
            Component footstepSfx = RequireOnlyComponent(
                graph.FootstepSfx.gameObject, types.FootstepSfx,
                "FootstepSFX");
            Component powerLegs = RequireOnlyComponent(
                graph.PowerHolder.gameObject, types.PowerLegs,
                "BehaviourPowerLegs");
            SphereCollider[] visions = graph.PowerHolder
                .GetComponents<SphereCollider>();
            if (visions.Length != 1)
                throw new InvalidOperationException(
                    "BehaviourPowerLegs must have exactly one vision trigger.");
            AudioSource[] impactSources = graph.ImpactSource
                .GetComponents<AudioSource>();
            if (impactSources.Length != 1)
                throw new InvalidOperationException(
                    "ImpactSrc must have exactly one AudioSource.");

            var solvers = new Dictionary<HumanBodyBones, Component>();
            foreach (HumanBodyBones role in SolverRoles)
            {
                Transform bone = roles[role].Target;
                if (bone == null)
                    throw new InvalidOperationException(
                        "Accepted Avatar source path cannot resolve " + role + ".");
                solvers.Add(
                    role,
                    RequireOnlyComponent(
                        bone.gameObject, types.LimbIk, role + " LimbIKSlz"));
            }
            InteractionShell interaction = ResolveInteractionShell(outputRoot, roles);
            var shell = new NativeBehaviourShell(
                settings,
                movement,
                graph,
                poolee,
                brain,
                liteLoco,
                navAgent,
                footstepSfx,
                powerLegs,
                visions[0],
                impactSources[0],
                solvers,
                interaction);
            ValidateBehaviourShell(
                outputRoot,
                animationRoot,
                physicsRoot,
                definition,
                animator,
                roles,
                entity,
                marrowBodies,
                puppet,
                shell);
            ValidateNoExternalSceneReferences(outputRoot);
            return shell;
        }

        private static readonly HumanBodyBones[] SolverRoles =
        {
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm,
        };

        private static bool TryPreflightBehaviourBuild(out string detail)
        {
            try
            {
                BehaviourTypes types = BehaviourTypes.Resolve();
                MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings =
                    RequireBehaviourSettings(types);
                ValidateBehaviourTemplate(settings.BehaviourTemplate, types);
                ValidateLocomotionReference(settings.LocomotionReference, types);
                ValidateBehaviourController(settings.AnimatorController);
                ValidateBaseEnemyConfig(settings.BaseEnemyConfig);
                detail = "The explicit project-local behaviour profile passed "
                         + "its controller, config, pose, hand, foot-material, "
                         + "template-component, and Limb IK preflight.";
                return true;
            }
            catch (Exception exception)
            {
                detail = "Patch 6 behaviour profile preflight failed: "
                         + exception.Message;
                return false;
            }
        }

        private static MarrowNpcToolkitPatch6BehaviourSettings.Resolved
            RequireBehaviourSettings(
                BehaviourTypes types,
                bool physicalJaw = false)
        {
            if (!MarrowNpcToolkitPatch6BehaviourSettings.TryResolve(
                    types.BaseEnemyConfig,
                    types.EnemyPoseData,
                    types.HandPoseData,
                    physicalJaw,
                    out var settings,
                    out string detail))
                throw new InvalidOperationException(detail);
            ValidatePersistentAsset(
                settings.AnimatorController, types.RuntimeAnimatorController,
                "Animator Controller");
            ValidatePersistentAsset(
                settings.LocomotionReference,
                typeof(GameObject),
                "Stock Locomotion Reference");
            ValidatePersistentAsset(
                settings.BaseEnemyConfig, types.BaseEnemyConfig,
                "Base Enemy Config");
            ValidatePersistentAsset(
                settings.StandingIdle, types.EnemyPoseData,
                "Standing Pose");
            ValidatePersistentAsset(settings.OpenHand, types.HandPoseData, "Open Hand");
            ValidatePersistentAsset(settings.Fist, types.HandPoseData, "Fist");
            ValidatePersistentAsset(settings.Pistol, types.HandPoseData, "Pistol");
            ValidatePersistentAsset(
                settings.PistolOffhand, types.HandPoseData, "Pistol Offhand");
            ValidatePersistentAsset(
                settings.PlantedFootMaterial, typeof(PhysicMaterial),
                "Planted Foot Material");
            ValidatePersistentAsset(
                settings.LiftedFootMaterial, typeof(PhysicMaterial),
                "Lifted Foot Material");

            var pose = new SerializedObject(settings.StandingIdle);
            SerializedProperty positions = Require(pose, "posePositions");
            SerializedProperty rotations = Require(pose, "poseRotations");
            int expectedPoseCount = physicalJaw ? 17 : 16;
            if (!positions.isArray || positions.arraySize != expectedPoseCount
                || !rotations.isArray || rotations.arraySize != expectedPoseCount)
                throw new InvalidOperationException(
                    "The configured standing pose must contain exactly "
                    + expectedPoseCount + " positions and " + expectedPoseCount
                    + " rotations in PuppetMaster muscle order.");
            if (physicalJaw)
                ValidateJawStandingPose(settings.StandingIdle);
            return settings;
        }

        private static void ValidatePersistentAsset(
            UnityEngine.Object value,
            Type expectedType,
            string label)
        {
            if (value == null || !expectedType.IsInstanceOfType(value)
                || !EditorUtility.IsPersistent(value)
                || string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(value)))
                throw new InvalidOperationException(
                    label + " must be an explicit persistent project asset.");
        }

        private static void ValidateBehaviourTemplate(
            GameObject template,
            BehaviourTypes types)
        {
            ValidatePersistentAsset(template, typeof(GameObject), "Behaviour Template");
            Component templatePower = RequireTemplateComponent(
                template, types.PowerLegs, "BehaviourPowerLegs");
            if (templatePower.gameObject.layer != 30)
                throw new InvalidOperationException(
                    "The Behaviour Template PowerLegs holder must use layer 30 "
                    + "so its copied vision trigger matches the Patch 6 contract.");
            RequireTemplateComponent(template, types.LiteLoco, "LiteLoco");
            RequireTemplateComponent(template, types.NavMeshAgent, "NavMeshAgent");
            RequireTemplateComponent(template, types.FootstepSfx, "FootstepSFX");
            Animator[] animators = template.GetComponentsInChildren<Animator>(true)
                .Where(value => value.avatar != null && value.avatar.isHuman)
                .ToArray();
            if (animators.Length != 1)
                throw new InvalidOperationException(
                    "The explicit Behaviour Template must have one Humanoid Animator.");
            foreach (HumanBodyBones role in SolverRoles)
                RequireTemplateLimbSolver(template, role, types);
        }

        private static void ValidateLocomotionReference(
            GameObject reference,
            BehaviourTypes types)
        {
            ValidatePersistentAsset(
                reference,
                typeof(GameObject),
                "Stock Locomotion Reference");
            Component loco = RequireTemplateComponent(
                reference, types.LiteLoco, "Stock Locomotion Reference LiteLoco");
            SerializedProperty groups = Require(
                new SerializedObject(loco), "stepGroups");
            if (!groups.isArray || groups.arraySize != 1)
                throw new InvalidOperationException(
                    "The Stock Locomotion Reference LiteLoco must have exactly "
                    + "one step group.");
            SerializedProperty group = groups.GetArrayElementAtIndex(0);
            float legLength = RequireRelative(group, "legLength").floatValue;
            SerializedProperty gears = RequireRelative(group, "gears");
            SerializedProperty grounder = RequireRelative(group, "grounder");
            if (!IsFinite(legLength) || legLength <= 0f
                || RequireRelative(group, "FootXVCurve")
                    .animationCurveValue == null
                || !gears.isArray || gears.arraySize == 0
                || RequireRelative(grounder, "maxStep").floatValue <= 0f
                || RequireRelative(grounder, "footSpeed").floatValue <= 0f)
                throw new InvalidOperationException(
                    "The Stock Locomotion Reference has an incomplete or "
                    + "non-positive gait contract.");
            for (int index = 0; index < gears.arraySize; index++)
            {
                SerializedProperty gear = gears.GetArrayElementAtIndex(index);
                foreach (string curve in new[]
                         {
                             "StepRateVCurve", "stepHeight", "StepZInterp",
                             "StepAnkleBend", "MuscleUsage",
                         })
                    if (RequireRelative(gear, curve).animationCurveValue == null)
                        throw new InvalidOperationException(
                            "The Stock Locomotion Reference gear " + index
                            + " has no " + curve + " curve.");
            }
        }

        private static Component RequireTemplateLimbSolver(
            GameObject template,
            HumanBodyBones role,
            BehaviourTypes types)
        {
            Component[] matches = template.GetComponentsInChildren(types.LimbIk, true)
                .Cast<Component>()
                .Where(value => value != null
                                && string.Equals(
                                    value.transform.name,
                                    role.ToString(),
                                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "The Behaviour Template must have exactly one LimbIKSlz "
                    + "on its animated " + role + " transform; found "
                    + matches.Length + ".");
            return matches[0];
        }

        private static void ValidateBehaviourController(
            RuntimeAnimatorController runtimeController)
        {
            AnimatorOverrideController overrides =
                runtimeController as AnimatorOverrideController;
            RuntimeAnimatorController baseRuntime = overrides == null
                ? runtimeController
                : overrides.runtimeAnimatorController;
            AnimatorController controller = baseRuntime as AnimatorController;
            if (controller == null)
                throw new InvalidOperationException(
                    "The configured controller must be an inspectable "
                    + "AnimatorController or AnimatorOverrideController.");

            var states = new Dictionary<string, List<AnimatorState>>(
                StringComparer.Ordinal);
            foreach (AnimatorControllerLayer layer in controller.layers)
                CollectBehaviourControllerStates(layer.stateMachine, states);
            foreach (string stateName in new[]
                     {
                         "Idle2", "Loco", "GetUpFromFace", "GetUpFromBack",
                     })
            {
                if (!states.TryGetValue(stateName, out List<AnimatorState> matches)
                    || matches.Count != 1
                    || FirstBehaviourControllerClip(matches[0].motion, overrides)
                        == null)
                    throw new InvalidOperationException(
                        "The configured controller must contain one motion-backed "
                        + stateName + " state.");
            }

            AnimatorControllerLayer[] layers = controller.layers;
            if (layers.Length == 0
                || !string.Equals(
                    layers[0].name, "Base Layer", StringComparison.Ordinal)
                || layers[0].stateMachine == null
                || layers[0].stateMachine.defaultState == null
                || FirstBehaviourControllerClip(
                    layers[0].stateMachine.defaultState.motion,
                    overrides) == null)
                throw new InvalidOperationException(
                    "The configured controller Base Layer must have a persistent "
                    + "motion-backed default state.");

            var parameters = controller.parameters.ToDictionary(
                value => value.name,
                value => value.type,
                StringComparer.Ordinal);
            var required = new Dictionary<string, AnimatorControllerParameterType>
            {
                ["syncTime"] = AnimatorControllerParameterType.Float,
                ["locoCycle"] = AnimatorControllerParameterType.Float,
                ["m/sec"] = AnimatorControllerParameterType.Float,
                ["crouch"] = AnimatorControllerParameterType.Bool,
                ["angry"] = AnimatorControllerParameterType.Bool,
                ["awake"] = AnimatorControllerParameterType.Bool,
                ["unGrounded"] = AnimatorControllerParameterType.Bool,
                ["jump"] = AnimatorControllerParameterType.Trigger,
                ["preJump"] = AnimatorControllerParameterType.Trigger,
                ["attack"] = AnimatorControllerParameterType.Trigger,
                ["idle"] = AnimatorControllerParameterType.Trigger,
                ["flinch"] = AnimatorControllerParameterType.Trigger,
            };
            foreach (KeyValuePair<string, AnimatorControllerParameterType> pair
                     in required)
                if (!parameters.TryGetValue(pair.Key, out var actual)
                    || actual != pair.Value)
                    throw new InvalidOperationException(
                        "The configured controller parameter " + pair.Key
                        + " is missing or has the wrong type.");
        }

        private static void CollectBehaviourControllerStates(
            AnimatorStateMachine machine,
            IDictionary<string, List<AnimatorState>> result)
        {
            if (machine == null) return;
            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state == null) continue;
                if (!result.TryGetValue(child.state.name, out var matches))
                {
                    matches = new List<AnimatorState>();
                    result.Add(child.state.name, matches);
                }
                matches.Add(child.state);
            }
            foreach (ChildAnimatorStateMachine child in machine.stateMachines)
                CollectBehaviourControllerStates(child.stateMachine, result);
        }

        private static AnimationClip FirstBehaviourControllerClip(
            Motion motion,
            AnimatorOverrideController overrides)
        {
            if (motion is AnimationClip clip)
            {
                AnimationClip replacement = overrides == null
                    ? null
                    : overrides[clip];
                AnimationClip result = replacement != null ? replacement : clip;
                return result.length > 0f
                       && EditorUtility.IsPersistent(result)
                    ? result
                    : null;
            }
            if (motion is BlendTree tree)
                foreach (ChildMotion child in tree.children)
                {
                    AnimationClip found = FirstBehaviourControllerClip(
                        child.motion, overrides);
                    if (found != null) return found;
                }
            return null;
        }

        private static void ValidateBaseEnemyConfig(UnityEngine.Object config)
        {
            var serialized = new SerializedObject(config);
            SerializedProperty sensors = Require(serialized, "sensorSettings");
            SerializedProperty health = Require(serialized, "healthSettings");
            float visionFov = RequireRelative(sensors, "visionFov").floatValue;
            if (!IsFinite(visionFov) || visionFov <= 0f || visionFov > 180f)
                throw new InvalidOperationException(
                    "Base Enemy Config sensorSettings.visionFov must be within "
                    + "(0, 180].");
            foreach (string field in new[]
                     {
                         "maxHitPoints", "maxAppendageHp", "stunRecovery",
                         "maxStunSeconds",
                     })
            {
                float value = RequireRelative(health, field).floatValue;
                if (!IsFinite(value) || value <= 0f)
                    throw new InvalidOperationException(
                        "Base Enemy Config healthSettings." + field
                        + " must be positive.");
            }

            foreach (string usageName in new[]
                     {
                         "restingUsage", "roamUsage", "investigateUsage",
                         "engagedUsage", "agroedUsage",
                     })
            {
                SerializedProperty usage = Require(serialized, usageName);
                bool anyActive = false;
                foreach (string field in new[]
                         {
                             "hips", "spine", "legLf", "legRt", "armLf",
                             "armRt",
                         })
                {
                    float value = RequireRelative(usage, field).floatValue;
                    if (!IsFinite(value) || value < 0f)
                        throw new InvalidOperationException(
                            "Base Enemy Config " + usageName + "." + field
                            + " must be finite and non-negative.");
                    anyActive |= value > 0f;
                }
                if (!anyActive)
                    throw new InvalidOperationException(
                        "Base Enemy Config " + usageName
                        + " cannot disable every PuppetMaster muscle group.");
            }
        }

        private static void EnsureNoBehaviourComponents(
            GameObject outputRoot,
            BehaviourTypes types)
        {
            foreach (KeyValuePair<Type, string> expected in new Dictionary<Type, string>
            {
                [types.Poolee] = "Poolee",
                [types.AIBrain] = "AIBrain",
                [types.PowerLegs] = "BehaviourPowerLegs",
                [types.LiteLoco] = "LiteLoco",
                [types.NavMeshAgent] = "NavMeshAgent",
                [types.FootstepSfx] = "FootstepSFX",
                [types.LimbIk] = "LimbIKSlz",
            })
                if (outputRoot.GetComponentsInChildren(expected.Key, true).Length != 0)
                    throw new InvalidOperationException(
                        "The staged preview already contains " + expected.Value + ".");
            if (outputRoot.transform.Find("AiRig") != null)
                throw new InvalidOperationException(
                    "The staged preview already contains a direct AiRig.");
        }

        private static Component RequireTemplateComponent(
            GameObject template,
            Type type,
            string label)
        {
            Component[] components = template.GetComponentsInChildren(type, true)
                .Cast<Component>().ToArray();
            if (components.Length != 1)
                throw new InvalidOperationException(
                    "The explicit Behaviour Template must contain exactly one "
                    + label + "; found " + components.Length + ".");
            return components[0];
        }

        private static Component CopyTemplateComponent(
            Component template,
            GameObject holder,
            string label)
        {
            Component target = AddNative(holder, template.GetType(), label);
            EditorUtility.CopySerialized(template, target);
            ClearExternalSceneReferences(target);
            return target;
        }

        private static void ClearExternalSceneReferences(Component component)
        {
            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = true;
                if (property.propertyType != SerializedPropertyType.ObjectReference
                    || property.propertyPath == "m_Script"
                    || property.propertyPath == "m_GameObject"
                    || property.propertyPath.StartsWith(
                        "m_CorrespondingSourceObject", StringComparison.Ordinal)
                    || property.propertyPath.StartsWith("m_Prefab", StringComparison.Ordinal))
                    continue;
                UnityEngine.Object value = property.objectReferenceValue;
                if (value is Component || value is GameObject || value is Transform)
                    property.objectReferenceValue = null;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureTargetAnimator(
            Animator animator,
            RuntimeAnimatorController controller)
        {
            animator.enabled = true;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
        }

        private static BehaviourGraph CreateBehaviourGraph(
            GameObject outputRoot,
            Transform avatarRoot,
            NpcDefinition definition,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            GameObject template,
            BehaviourTypes types,
            Patch6MovementBuildSettings movement)
        {
            Transform aiRig = CreateChild(outputRoot.transform, "AiRig");
            Transform movementFrame = movement.UsesLegacyFallback
                ? outputRoot.transform
                : avatarRoot;
            aiRig.localPosition = Vector3.zero;
            aiRig.localRotation = Quaternion.identity;
            aiRig.localScale = Vector3.one;
            Component templateLoco = RequireTemplateComponent(
                template, types.LiteLoco, "LiteLoco");
            aiRig.gameObject.layer = templateLoco.gameObject.layer;

            Transform locoFrame = CreateChild(aiRig, "LocoPelvisFrame");
            Transform hipsTarget = roles[HumanBodyBones.Hips].Target;
            Transform hipsFrame = hipsTarget.parent;
            if (hipsFrame == null)
                throw new InvalidOperationException(
                    "The Humanoid Hips target has no parent coordinate frame.");
            locoFrame.SetPositionAndRotation(hipsFrame.position, hipsFrame.rotation);
            locoFrame.localScale = Vector3.one;
            Transform pelvis = CreateChild(locoFrame, "Pelvis");
            MatchWorld(pelvis, hipsTarget);
            Transform leftHip = CreateChild(pelvis, "Hip_L");
            Transform rightHip = CreateChild(pelvis, "Hip_R");
            MatchWorld(leftHip, roles[HumanBodyBones.LeftUpperLeg].Target);
            MatchWorld(rightHip, roles[HumanBodyBones.RightUpperLeg].Target);

            Transform neutralRoot = CreateChild(aiRig, "NeutralRoot");
            neutralRoot.SetPositionAndRotation(
                movementFrame.position, movementFrame.rotation);
            neutralRoot.localScale = Vector3.one;
            Transform leftFoot = CreateChild(neutralRoot, "Foot_L");
            Transform rightFoot = CreateChild(neutralRoot, "Foot_R");
            Transform leftNeutral = CreateChild(neutralRoot, "Foot_L_neutral");
            Transform rightNeutral = CreateChild(neutralRoot, "Foot_R_neutral");
            ConfigureFootControl(
                movementFrame,
                definition.AnatomyProfile,
                animator,
                roles[HumanBodyBones.LeftFoot],
                HumanBodyBones.LeftToes,
                true,
                movement,
                leftFoot,
                leftNeutral);
            ConfigureFootControl(
                movementFrame,
                definition.AnatomyProfile,
                animator,
                roles[HumanBodyBones.RightFoot],
                HumanBodyBones.RightToes,
                false,
                movement,
                rightFoot,
                rightNeutral);
            Transform leftAnkleTarget = CreateChild(leftFoot, "Ankle_L_IKtarget");
            Transform rightAnkleTarget = CreateChild(rightFoot, "Ankle_R_IKtarget");
            MatchWorldWithUpOffset(
                leftAnkleTarget,
                roles[HumanBodyBones.LeftFoot].Target,
                movement.UsesLegacyFallback
                    ? outputRoot.transform.up * 0.01f
                    : Vector3.zero);
            MatchWorldWithUpOffset(
                rightAnkleTarget,
                roles[HumanBodyBones.RightFoot].Target,
                movement.UsesLegacyFallback
                    ? outputRoot.transform.up * 0.01f
                    : Vector3.zero);
            Transform footstepSfx = CreateChild(neutralRoot, "FootstepSfx");

            Transform leftHand = CreateChild(aiRig, "Hand_L");
            Transform rightHand = CreateChild(aiRig, "Hand_R");
            MatchWorld(leftHand, roles[HumanBodyBones.LeftHand].Target);
            MatchWorld(rightHand, roles[HumanBodyBones.RightHand].Target);
            Transform leftHandTarget = CreateChild(leftHand, "hand_L_target");
            Transform rightHandTarget = CreateChild(rightHand, "hand_R_target");
            leftHandTarget.localPosition = Vector3.zero;
            leftHandTarget.localRotation = Quaternion.identity;
            leftHandTarget.localScale = Vector3.one;
            rightHandTarget.localPosition = Vector3.zero;
            rightHandTarget.localRotation = Quaternion.identity;
            rightHandTarget.localScale = Vector3.one;

            Transform powerHolder = CreateChild(aiRig, "BehaviourPowerLegs");
            Component templatePower = RequireTemplateComponent(
                template, types.PowerLegs, "BehaviourPowerLegs");
            powerHolder.gameObject.layer = templatePower.gameObject.layer;
            Transform impactSource = CreateChild(powerHolder, "ImpactSrc");
            Transform templateImpact = templatePower.transform.Find("ImpactSrc");
            impactSource.gameObject.layer = templateImpact == null
                ? templatePower.gameObject.layer
                : templateImpact.gameObject.layer;

            Transform physicalHead = roles[HumanBodyBones.Head].Body;
            Transform eye = CreateChild(physicalHead, "EyeTran");
            Transform leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            Vector3 eyePosition = leftEye != null && rightEye != null
                ? (leftEye.position + rightEye.position) * 0.5f
                : roles[HumanBodyBones.Head].Target.position
                  + outputRoot.transform.forward * 0.08f;
            eye.SetPositionAndRotation(
                eyePosition,
                Quaternion.LookRotation(
                    outputRoot.transform.forward, outputRoot.transform.up));
            eye.localScale = Vector3.one;
            eye.gameObject.layer = physicalHead.gameObject.layer;

            if (!movement.UsesLegacyFallback)
                ApplyMovementHelperTuning(
                    movementFrame,
                    movement,
                    pelvis,
                    leftFoot,
                    rightFoot,
                    leftNeutral,
                    rightNeutral);

            return new BehaviourGraph(
                aiRig,
                locoFrame,
                pelvis,
                leftHip,
                rightHip,
                neutralRoot,
                leftFoot,
                rightFoot,
                leftNeutral,
                rightNeutral,
                leftAnkleTarget,
                rightAnkleTarget,
                footstepSfx,
                leftHand,
                rightHand,
                leftHandTarget,
                rightHandTarget,
                powerHolder,
                impactSource,
                eye);
        }

        private static BehaviourGraph ResolveBehaviourGraph(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Transform aiRig)
        {
            Transform locoFrame = RequireDirectChild(aiRig, "LocoPelvisFrame");
            Transform pelvis = RequireDirectChild(locoFrame, "Pelvis");
            Transform leftHip = RequireDirectChild(pelvis, "Hip_L");
            Transform rightHip = RequireDirectChild(pelvis, "Hip_R");
            Transform neutralRoot = RequireDirectChild(aiRig, "NeutralRoot");
            Transform leftFoot = RequireDirectChild(neutralRoot, "Foot_L");
            Transform rightFoot = RequireDirectChild(neutralRoot, "Foot_R");
            Transform leftNeutral = RequireDirectChild(
                neutralRoot, "Foot_L_neutral");
            Transform rightNeutral = RequireDirectChild(
                neutralRoot, "Foot_R_neutral");
            Transform leftAnkleTarget = RequireDirectChild(
                leftFoot, "Ankle_L_IKtarget");
            Transform rightAnkleTarget = RequireDirectChild(
                rightFoot, "Ankle_R_IKtarget");
            Transform footstepSfx = RequireDirectChild(
                neutralRoot, "FootstepSfx");
            Transform leftHand = RequireDirectChild(aiRig, "Hand_L");
            Transform rightHand = RequireDirectChild(aiRig, "Hand_R");
            Transform leftHandTarget = RequireDirectChild(
                leftHand, "hand_L_target");
            Transform rightHandTarget = RequireDirectChild(
                rightHand, "hand_R_target");
            Transform powerHolder = RequireDirectChild(
                aiRig, "BehaviourPowerLegs");
            Transform impact = RequireDirectChild(powerHolder, "ImpactSrc");
            Transform eye = RequireDirectChild(
                roles[HumanBodyBones.Head].Body, "EyeTran");
            return new BehaviourGraph(
                aiRig,
                locoFrame,
                pelvis,
                leftHip,
                rightHip,
                neutralRoot,
                leftFoot,
                rightFoot,
                leftNeutral,
                rightNeutral,
                leftAnkleTarget,
                rightAnkleTarget,
                footstepSfx,
                leftHand,
                rightHand,
                leftHandTarget,
                rightHandTarget,
                powerHolder,
                impact,
                eye);
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (Enumerable.Range(0, parent.childCount)
                .Select(parent.GetChild)
                .Any(value => string.Equals(
                    value.name, name, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "Cannot create duplicate direct child " + name + " below "
                    + parent.name + ".");
            var value = new GameObject(name).transform;
            value.SetParent(parent, false);
            value.localPosition = Vector3.zero;
            value.localRotation = Quaternion.identity;
            value.localScale = Vector3.one;
            return value;
        }

        private static Transform RequireDirectChild(Transform parent, string name)
        {
            Transform[] values = Enumerable.Range(0, parent.childCount)
                .Select(parent.GetChild)
                .Where(value => string.Equals(
                    value.name, name, StringComparison.Ordinal))
                .ToArray();
            if (values.Length != 1)
                throw new InvalidOperationException(
                    "Expected one direct child " + name + " below " + parent.name
                    + "; found " + values.Length + ".");
            return values[0];
        }

        private static Transform RequireMovementAvatarRoot(
            Transform animationRoot,
            Animator animator)
        {
            if (animationRoot == null || animator == null
                || !animator.transform.IsChildOf(animationRoot))
                throw new InvalidOperationException(
                    "Movement authoring requires the routed Animator beneath "
                    + "AnimationRoot.");
            Transform value = animator.transform;
            while (value.parent != animationRoot)
            {
                value = value.parent;
                if (value == null)
                    throw new InvalidOperationException(
                        "The routed Animator has no Avatar root beneath AnimationRoot.");
            }
            return value;
        }

        private static void MatchWorld(Transform target, Transform source)
        {
            target.SetPositionAndRotation(source.position, source.rotation);
            target.localScale = Vector3.one;
        }

        private static void MatchWorldWithUpOffset(
            Transform target,
            Transform source,
            Vector3 offset)
        {
            target.SetPositionAndRotation(source.position + offset, source.rotation);
            target.localScale = Vector3.one;
        }

        private static void ConfigureFootControl(
            Transform movementFrame,
            NpcAnatomyProfile anatomy,
            Animator animator,
            NativeRole footRole,
            HumanBodyBones toeRole,
            bool left,
            Patch6MovementBuildSettings movement,
            Transform control,
            Transform neutral)
        {
            Transform toe = animator.GetBoneTransform(toeRole);
            Vector3 sourcePosition = movement.UsesLegacyFallback
                ? toe == null
                    ? footRole.Target.position
                    : toe.position
                : movementFrame.TransformPoint(
                    left ? anatomy.LeftSoleLocal : anatomy.RightSoleLocal);
            Vector3 position;
            if (movement.UsesLegacyFallback)
            {
                Bounds localBounds = ColliderBoundsInFrame(
                    footRole.Collider, movementFrame);
                Vector3 local = movementFrame.InverseTransformPoint(sourcePosition);
                local.y = localBounds.min.y;
                position = movementFrame.TransformPoint(local);
            }
            else
            {
                Vector3 up = movementFrame.up.normalized;
                Vector3 solePlane = movementFrame.position
                                    + up * movement.SoleHeight;
                position = sourcePosition
                           + up * Vector3.Dot(solePlane - sourcePosition, up);
            }
            Vector3 forward = movement.UsesLegacyFallback
                ? toe == null
                    ? movementFrame.forward
                    : Vector3.ProjectOnPlane(
                        toe.position - footRole.Target.position, movementFrame.up)
                : movementFrame.TransformDirection(
                    left
                        ? movement.LeftFootForwardLocal
                        : movement.RightFootForwardLocal);
            if (forward.sqrMagnitude < 0.000001f)
                forward = movementFrame.forward;
            float yawCorrection = left
                ? movement.LeftFootYawCorrectionDegrees
                : movement.RightFootYawCorrectionDegrees;
            forward = Quaternion.AngleAxis(yawCorrection, movementFrame.up)
                      * Vector3.ProjectOnPlane(
                          forward, movementFrame.up).normalized;
            Quaternion rotation = Quaternion.LookRotation(
                forward.normalized, movementFrame.up);
            control.SetPositionAndRotation(position, rotation);
            control.localScale = Vector3.one;
            neutral.SetPositionAndRotation(position, movementFrame.rotation);
            neutral.localScale = Vector3.one;
        }

        private static void ApplyMovementHelperTuning(
            Transform movementFrame,
            Patch6MovementBuildSettings movement,
            Transform pelvis,
            Transform leftFoot,
            Transform rightFoot,
            Transform leftNeutral,
            Transform rightNeutral)
        {
            pelvis.position += movementFrame.up.normalized
                               * movement.PelvisHeightOffset;
            ScalePairWidth(
                movementFrame, leftFoot, rightFoot, movement.StanceWidthScale);
            ScalePairWidth(
                movementFrame,
                leftNeutral,
                rightNeutral,
                movement.StanceWidthScale);
        }

        private static void ScalePairWidth(
            Transform movementFrame,
            Transform left,
            Transform right,
            float scale)
        {
            Vector3 axis = movementFrame.right.normalized;
            float leftDistance = Vector3.Dot(
                left.position - movementFrame.position, axis);
            float rightDistance = Vector3.Dot(
                right.position - movementFrame.position, axis);
            float center = (leftDistance + rightDistance) * 0.5f;
            left.position += axis
                             * (center + (leftDistance - center) * scale
                                - leftDistance);
            right.position += axis
                              * (center + (rightDistance - center) * scale
                                 - rightDistance);
        }

        private static void ConfigureSilentFootstepSfx(Component footstepSfx)
        {
            var data = new SerializedObject(footstepSfx);
            SetFloat(data, "volumeMult", 1f);
            foreach (string path in new[] { "walkConcrete", "runConcrete" })
            {
                SerializedProperty array = Require(data, path);
                if (!array.isArray)
                    throw new InvalidOperationException(
                        "FootstepSFX." + path + " is not an array.");
                array.arraySize = 6;
                for (int index = 0; index < array.arraySize; index++)
                    array.GetArrayElementAtIndex(index).objectReferenceValue = null;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureSilentImpactSource(AudioSource source)
        {
            source.enabled = true;
            source.playOnAwake = false;
            source.clip = null;
            source.loop = false;
            source.mute = false;
            source.spatialize = false;
            source.volume = 1f;
            source.pitch = 1f;
            source.priority = 128;
            source.minDistance = 1f;
            source.maxDistance = 500f;
        }

        private static void ConfigureNavAgent(
            Component navAgent,
            Transform outputRoot,
            Transform movementFrame,
            Patch6MovementBuildSettings movement)
        {
            if (movement.UsesLegacyFallback)
                return;
            var data = new SerializedObject(navAgent);
            SetFloat(data, "m_Radius", movement.NavRadius);
            SetFloat(data, "m_Height", movement.NavHeight);
            SetFloat(
                data,
                "m_BaseOffset",
                MovementNavBaseOffset(outputRoot, movementFrame, movement));
            SetFloat(data, "m_Speed", movement.WalkSpeed);
            SetFloat(data, "m_Acceleration", movement.Acceleration);
            SetFloat(data, "m_AngularSpeed", movement.AngularSpeed);
            SetFloat(data, "m_StoppingDistance", movement.StoppingDistance);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static float MovementNavBaseOffset(
            Transform outputRoot,
            Transform movementFrame,
            Patch6MovementBuildSettings movement)
        {
            Vector3 solePlane = movementFrame.position
                                + movementFrame.up.normalized
                                * movement.SoleHeight;
            return Vector3.Dot(
                solePlane - outputRoot.position, outputRoot.up.normalized);
        }

        private static void ConfigureLiteLoco(
            Component liteLoco,
            BehaviourGraph graph,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings,
            Patch6MovementBuildSettings movement,
            Component footstepSfx)
        {
            var data = new SerializedObject(liteLoco);
            SetFloat(data, "weight", 1f);
            SetObject(data, "root", graph.AiRig);
            SetObject(data, "neutralRoot", graph.NeutralRoot);
            SerializedProperty groups = Require(data, "stepGroups");
            if (!groups.isArray || groups.arraySize != 1)
                throw new InvalidOperationException(
                    "The Behaviour Template LiteLoco must have one step group.");
            SerializedProperty group = groups.GetArrayElementAtIndex(0);
            CopyStockReferenceGait(group, settings, liteLoco.GetType());
            SetRelativeObject(group, "pelvis", graph.Pelvis);
            SetRelativeInt(group, "sisterStepGroup", -1);
            float donorLegLength = RequireRelative(group, "legLength").floatValue;
            float outputLegLength;
            if (movement.UsesLegacyFallback)
            {
                float leftLength = Vector3.Distance(
                    roles[HumanBodyBones.LeftUpperLeg].Target.position,
                    roles[HumanBodyBones.LeftFoot].Target.position);
                float rightLength = Vector3.Distance(
                    roles[HumanBodyBones.RightUpperLeg].Target.position,
                    roles[HumanBodyBones.RightFoot].Target.position);
                outputLegLength = (leftLength + rightLength) * 0.5f;
            }
            else
                outputLegLength = movement.MeanLegLength;
            SetRelativeFloat(group, "legLength", outputLegLength);
            if (!movement.UsesLegacyFallback)
                ScaleLiteLocoSpatialAndRateValues(
                    group,
                    outputLegLength / donorLegLength,
                    movement);
            SerializedProperty footsteps = RequireRelative(group, "footsteps");
            if (!footsteps.isArray || footsteps.arraySize != 2)
                throw new InvalidOperationException(
                    "The Behaviour Template LiteLoco must have two footsteps.");

            ConfigureLocoFootstep(
                footsteps.GetArrayElementAtIndex(0),
                graph.LeftHip,
                graph.LeftFoot,
                graph.LeftFootNeutral,
                roles[HumanBodyBones.LeftFoot].Collider,
                settings,
                movement,
                footstepSfx);
            ConfigureLocoFootstep(
                footsteps.GetArrayElementAtIndex(1),
                graph.RightHip,
                graph.RightFoot,
                graph.RightFootNeutral,
                roles[HumanBodyBones.RightFoot].Collider,
                settings,
                movement,
                footstepSfx);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CopyStockReferenceGait(
            SerializedProperty outputGroup,
            MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings,
            Type liteLocoType)
        {
            Component referenceLoco = RequireTemplateComponent(
                settings.LocomotionReference,
                liteLocoType,
                "Stock Locomotion Reference LiteLoco");
            SerializedProperty referenceGroups = Require(
                new SerializedObject(referenceLoco), "stepGroups");
            if (!referenceGroups.isArray || referenceGroups.arraySize != 1)
                throw new InvalidOperationException(
                    "The Stock Locomotion Reference LiteLoco must have one step group.");
            SerializedProperty referenceGroup =
                referenceGroups.GetArrayElementAtIndex(0);
            SetRelativeFloat(
                outputGroup,
                "legLength",
                RequireRelative(referenceGroup, "legLength").floatValue);
            SetRelativeCurve(
                outputGroup,
                "FootXVCurve",
                RequireRelative(referenceGroup, "FootXVCurve").animationCurveValue);
            SetRelativeInt(
                outputGroup,
                "_gear",
                RequireRelative(referenceGroup, "_gear").intValue);
            SetRelativeBool(
                outputGroup,
                "computeAnimCycle",
                RequireRelative(referenceGroup, "computeAnimCycle").boolValue);
            SetRelativeBool(
                outputGroup,
                "visualizeAnimCycle",
                RequireRelative(referenceGroup, "visualizeAnimCycle").boolValue);
            SetRelativeFloat(
                outputGroup,
                "animCycle",
                RequireRelative(referenceGroup, "animCycle").floatValue);

            SerializedProperty referenceGears = RequireRelative(
                referenceGroup, "gears");
            SerializedProperty outputGears = RequireRelative(
                outputGroup, "gears");
            outputGears.arraySize = referenceGears.arraySize;
            for (int index = 0; index < referenceGears.arraySize; index++)
            {
                SerializedProperty referenceGear =
                    referenceGears.GetArrayElementAtIndex(index);
                SerializedProperty outputGear =
                    outputGears.GetArrayElementAtIndex(index);
                foreach (string scalar in new[]
                         {
                             "upshiftVel", "downshiftVel",
                             "stepProgressThreshold", "stepfromtoWeight",
                             "minStepThreshold",
                         })
                    SetRelativeFloat(
                        outputGear,
                        scalar,
                        RequireRelative(referenceGear, scalar).floatValue);
                foreach (string curve in new[]
                         {
                             "StepRateVCurve", "stepHeight", "StepZInterp",
                             "StepAnkleBend", "MuscleUsage",
                         })
                    SetRelativeCurve(
                        outputGear,
                        curve,
                        RequireRelative(referenceGear, curve).animationCurveValue);
            }
            SerializedProperty referenceGrounder = RequireRelative(
                referenceGroup, "grounder");
            SerializedProperty outputGrounder = RequireRelative(
                outputGroup, "grounder");
            SetRelativeFloat(
                outputGrounder,
                "maxStep",
                RequireRelative(referenceGrounder, "maxStep").floatValue);
            SetRelativeFloat(
                outputGrounder,
                "footSpeed",
                RequireRelative(referenceGrounder, "footSpeed").floatValue);
        }

        private static void SetRelativeCurve(
            SerializedProperty owner,
            string name,
            AnimationCurve value)
        {
            AnimationCurve source = value ?? throw new InvalidOperationException(
                "The Stock Locomotion Reference has no " + name + " curve.");
            var copy = new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
            RequireRelative(owner, name).animationCurveValue = copy;
        }

        private static void ConfigureLocoFootstep(
            SerializedProperty step,
            Transform hip,
            Transform foot,
            Transform neutral,
            Collider footCollider,
            MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings,
            Patch6MovementBuildSettings movement,
            Component footstepSfx)
        {
            footCollider.sharedMaterial = settings.PlantedFootMaterial;
            SetRelativeObject(step, "hip", hip);
            SetRelativeObject(step, "foot", foot);
            SetRelativeObject(step, "neutralTarget", neutral);
            SetRelativeObject(step, "footCollider", footCollider);
            SetRelativeObject(step, "liftedMat", settings.LiftedFootMaterial);
            SetRelativeObject(step, "stepSfx", footstepSfx);
            if (!movement.UsesLegacyFallback)
                SetRelativeFloat(step, "rotationOffset", 0f);
        }

        private static void ScaleLiteLocoSpatialAndRateValues(
            SerializedProperty group,
            float donorToAvatarLegRatio,
            Patch6MovementBuildSettings movement)
        {
            if (!IsFinite(donorToAvatarLegRatio)
                || donorToAvatarLegRatio <= 0f)
                throw new InvalidOperationException(
                    "The donor-to-Avatar leg ratio is not positive and finite.");
            float strideScale = donorToAvatarLegRatio * movement.StrideScale;
            float heightScale = donorToAvatarLegRatio * movement.StepHeightScale;
            // StepRateVCurve outputs cadence at an absolute Nav velocity.  A
            // proportionally larger leg therefore takes proportionally fewer
            // steps to retain the donor's metres-per-second semantics.  Grounder
            // foot speed is spatial travel per step times cadence, so the size
            // ratio cancels and only the explicit stride/rate tuning remains.
            float cadenceScale = movement.StepRateScale
                                 / donorToAvatarLegRatio;
            float footSpeedScale = movement.StrideScale
                                   * movement.StepRateScale;
            ScaleCurveOutput(
                RequireRelative(group, "FootXVCurve"), strideScale);
            SerializedProperty gears = RequireRelative(group, "gears");
            if (!gears.isArray || gears.arraySize == 0)
                throw new InvalidOperationException(
                    "The Behaviour Template LiteLoco must have at least one gait gear.");
            for (int index = 0; index < gears.arraySize; index++)
            {
                SerializedProperty gear = gears.GetArrayElementAtIndex(index);
                SetRelativeFloat(
                    gear,
                    "minStepThreshold",
                    RequireRelative(gear, "minStepThreshold").floatValue
                    * strideScale);
                ScaleCurveOutput(
                    RequireRelative(gear, "StepRateVCurve"),
                    cadenceScale);
                ScaleCurveOutput(
                    RequireRelative(gear, "stepHeight"), heightScale);
            }
            SerializedProperty grounder = RequireRelative(group, "grounder");
            SetRelativeFloat(
                grounder,
                "maxStep",
                RequireRelative(grounder, "maxStep").floatValue * heightScale);
            SetRelativeFloat(
                grounder,
                "footSpeed",
                RequireRelative(grounder, "footSpeed").floatValue
                * footSpeedScale);
        }

        private static void ScaleCurveOutput(
            SerializedProperty property,
            float scale)
        {
            if (property.propertyType != SerializedPropertyType.AnimationCurve
                || !IsFinite(scale) || scale <= 0f)
                throw new InvalidOperationException(
                    property.propertyPath
                    + " must be an AnimationCurve with a positive finite scale.");
            AnimationCurve source = property.animationCurveValue;
            Keyframe[] keys = source.keys;
            for (int index = 0; index < keys.Length; index++)
            {
                Keyframe key = keys[index];
                key.value *= scale;
                key.inTangent *= scale;
                key.outTangent *= scale;
                keys[index] = key;
            }
            var scaled = new AnimationCurve(keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
            property.animationCurveValue = scaled;
        }

        private static IReadOnlyDictionary<HumanBodyBones, Component>
            ConfigureLimbSolvers(
                GameObject template,
                IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
                BehaviourGraph graph,
                BehaviourTypes types)
        {
            var result = new Dictionary<HumanBodyBones, Component>();
            foreach (HumanBodyBones role in SolverRoles)
            {
                Component sourceSolver = RequireTemplateLimbSolver(
                    template, role, types);
                Transform targetUpper = roles[role].Target;
                if (targetUpper == null)
                    throw new InvalidOperationException(
                        "Cannot resolve the accepted target bone for " + role + ".");
                Component solver = CopyTemplateComponent(
                    sourceSolver, targetUpper.gameObject, role + " LimbIKSlz");
                ConfigureLimbSolver(
                    solver,
                    role,
                    roles,
                    SolverTarget(graph, role));
                if (solver is Behaviour behaviour)
                    behaviour.enabled = false;
                result.Add(role, solver);
            }
            return result;
        }

        private static Transform SolverTarget(
            BehaviourGraph graph,
            HumanBodyBones role)
        {
            switch (role)
            {
                case HumanBodyBones.LeftUpperLeg: return graph.LeftAnkleTarget;
                case HumanBodyBones.RightUpperLeg: return graph.RightAnkleTarget;
                case HumanBodyBones.LeftUpperArm: return graph.LeftHandTarget;
                case HumanBodyBones.RightUpperArm: return graph.RightHandTarget;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role, null);
            }
        }

        private static void ConfigureLimbSolver(
            Component solver,
            HumanBodyBones role,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Transform target)
        {
            HumanBodyBones lowerRole;
            HumanBodyBones endRole;
            switch (role)
            {
                case HumanBodyBones.LeftUpperLeg:
                    lowerRole = HumanBodyBones.LeftLowerLeg;
                    endRole = HumanBodyBones.LeftFoot;
                    break;
                case HumanBodyBones.RightUpperLeg:
                    lowerRole = HumanBodyBones.RightLowerLeg;
                    endRole = HumanBodyBones.RightFoot;
                    break;
                case HumanBodyBones.LeftUpperArm:
                    lowerRole = HumanBodyBones.LeftLowerArm;
                    endRole = HumanBodyBones.LeftHand;
                    break;
                case HumanBodyBones.RightUpperArm:
                    lowerRole = HumanBodyBones.RightLowerArm;
                    endRole = HumanBodyBones.RightHand;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role, null);
            }
            Transform upper = roles[role].Target;
            Transform lower = roles[lowerRole].Target;
            Transform end = roles[endRole].Target;
            if (upper == null || lower == null || end == null || target == null)
                throw new InvalidOperationException(role + " IK chain is incomplete.");

            var data = new SerializedObject(solver);
            SetObject(data, "animator", null);
            SetObject(data, "solver.root", upper);
            SetObject(data, "solver.target", target);
            SetObject(data, "solver.bone1.transform", upper);
            SetObject(data, "solver.bone2.transform", lower);
            SetObject(data, "solver.bone3.transform", end);
            SetObject(data, "solver.bendGoal", null);
            SerializedProperty solverData = Require(data, "solver");
            Transform[] chain = { upper, lower, end };
            for (int index = 0; index < chain.Length; index++)
            {
                SerializedProperty bone = RequireRelative(
                    solverData, "bone" + (index + 1));
                SetRelativeVector(
                    bone, "defaultLocalPosition", chain[index].localPosition);
                SetRelativeQuaternion(
                    bone, "defaultLocalRotation", chain[index].localRotation);
                SetRelativeVector(bone, "solverPosition", chain[index].position);
                SetRelativeQuaternion(bone, "solverRotation", chain[index].rotation);
                if (index < chain.Length - 1)
                {
                    Vector3 delta = chain[index + 1].position
                                    - chain[index].position;
                    SetRelativeFloat(bone, "sqrMag", delta.sqrMagnitude);
                    SetRelativeVector(
                        bone,
                        "axis",
                        chain[index].InverseTransformDirection(delta).normalized);
                }
            }
            SetRelativeVector(solverData, "IKPosition", target.position);
            SetRelativeQuaternion(solverData, "IKRotation", target.rotation);
            SetRelativeVector(
                solverData,
                "bendNormal",
                Vector3.Cross(
                    lower.position - upper.position,
                    end.position - lower.position));
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePowerLegs(
            Component powerLegs,
            Component puppet,
            Component poolee,
            BehaviourGraph graph,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings,
            Patch6MovementBuildSettings movement,
            AudioSource impactSource,
            IReadOnlyDictionary<HumanBodyBones, Component> limbSolvers)
        {
            var data = new SerializedObject(powerLegs);
            SetObject(data, "puppetMaster", puppet);
            SetObject(data, "_poolee", poolee);
            SetObject(data, "prefabConfig", movement.MovementConfig);
            SetObject(data, "overrideConfig", movement.MovementConfig);
            SetObject(data, "standingIdle", movement.StandingPose);
            SetObject(data, "eyeTran", graph.EyeTran);
            SetObject(data, "sensors.target", null);
            SetInt(data, "sensors.blockVisionRaycast.m_Bits", 65);
            SetFloat(data, "sensors.visionFov", 85f);
            SetFloat(data, "sensors.additionalMass", 0f);
            SetFloat(data, "sensors.footSupported", 0f);
            SetFloat(data, "sensors.handSupported", 0f);
            SetFloat(data, "sensors.bodySupported", 0f);

            foreach (string field in NativeSilentSfxArrays)
                SetArraySize(data, "sfx." + field, 0);
            foreach (string field in new[]
            {
                "dotLoop1", "agroMovementLoop", "movementLoop",
            })
                SetObject(data, "sfx." + field, null);
            SetObject(data, "sfx.impactSource", impactSource);
            SetFloat(data, "sfx.pitchMultiplier", 1f);

            IReadOnlyList<int> healthGroups = HealthGroupsFor(roles);
            SerializedProperty health = Require(data, "health.muscles");
            health.arraySize = healthGroups.Count;
            for (int index = 0; index < healthGroups.Count; index++)
                health.GetArrayElementAtIndex(index).intValue =
                    healthGroups[index];
            SetFloat(
                data,
                "health.aggression",
                movement.StartingHostility);
            SetFloat(data, "health.irritability", 1f);
            SetFloat(data, "health.placability", 1f);
            // PowerLegs raises a friendly NPC's aggression after a hit by
            // damage / max HP * vengefulness. The movement profile stores the
            // nontechnical result of a 25%-health hit; its derived multiplier
            // reproduces that selected result without implying pathing works.
            SetFloat(
                data,
                "health.vengefulness",
                movement.RetaliationVengefulness);

            SetInt(data, "getUpWhileResting", 1);
            SetInt(data, "followPlayer", 1);
            SetInt(data, "mentalState", 0);
            SetInt(data, "locoState", 0);
            SetFloat(data, "restingRange", 5f);
            SetFloat(data, "activeRange", 7f);
            if (!movement.UsesLegacyFallback)
            {
                SetFloat(data, "roamSpeed", movement.PowerRoamSpeed);
                SetFloat(data, "roamAngSpeed", movement.PowerAngularSpeed);
                SetFloat(data, "agroedSpeed", movement.PowerAgroSpeed);
                SetFloat(data, "agroedAngSpeed", movement.PowerAngularSpeed);
            }
            SetInt(data, "engagedMode", 1);
            SetFloat(data, "desiredDistance", 2f);
            SetFloat(
                data,
                "engagedSpeed",
                movement.UsesLegacyFallback
                    ? 1f
                    : movement.PowerEngagedSpeed);
            SetFloat(data, "meleeCooldown", 1.3f);
            SetObject(data, "aiLocoController", null);
            SetInt(data, "useAiLocoController", 0);
            ConfigureAnimatorEvent(data, "onGetUpProne", "GetUpFromFace");
            ConfigureAnimatorEvent(data, "onGetUpSupine", "GetUpFromBack");

            SetObject(data, "handPoser.OpenHand", settings.OpenHand);
            SetObject(data, "handPoser.Fist", settings.Fist);
            SetObject(data, "handPoser.Pistol", settings.Pistol);
            SetObject(data, "handPoser.PistolOffhand", settings.PistolOffhand);
            SetArraySize(data, "handPoser.leftHandRefs", 0);
            SetArraySize(data, "handPoser.rightHandRefs", 0);
            SetArraySize(data, "emissionRenderers", 0);

            SetInt(data, "ik.isHuman", 0);
            SetInt(data, "ik.footIkOn", 0);
            SetInt(data, "ik.armIkActiveLf", 0);
            SetInt(data, "ik.armIkActiveRt", 0);
            SetInt(data, "ik.lfShoulderMuscleIndex", 10);
            SetInt(data, "ik.rtShoulderMuscleIndex", 13);
            SetObjectArray(
                data,
                "ik.footIkSolvers",
                new UnityEngine.Object[]
                {
                    limbSolvers[HumanBodyBones.LeftUpperLeg],
                    limbSolvers[HumanBodyBones.RightUpperLeg],
                });
            SetObjectArray(
                data,
                "ik.armIkSolvers",
                new UnityEngine.Object[]
                {
                    limbSolvers[HumanBodyBones.LeftUpperArm],
                    limbSolvers[HumanBodyBones.RightUpperArm],
                });
            Transform leftToe = animator.GetBoneTransform(HumanBodyBones.LeftToes)
                                ?? animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightToe = animator.GetBoneTransform(HumanBodyBones.RightToes)
                                 ?? animator.GetBoneTransform(HumanBodyBones.RightFoot);
            SetObjectArray(
                data,
                "ik.toeTrans",
                new UnityEngine.Object[] { leftToe, rightToe });
            SetObject(data, "ik.lfHandTarget", graph.LeftHand);
            SetObject(data, "ik.rtHandTarget", graph.RightHand);
            SetObject(
                data,
                "ik.lfHandAnim",
                animator.GetBoneTransform(HumanBodyBones.LeftHand));
            SetObject(
                data,
                "ik.rtHandAnim",
                animator.GetBoneTransform(HumanBodyBones.RightHand));
            foreach (string field in NativeHumanoidIkReferences)
                SetObject(data, "ik." + field, null);

            SetIntIfPresent(data, "faceAnim.faceAnimEnabled", 0);
            SetObjectIfPresent(
                data,
                "faceAnim.mouthTran",
                roles.TryGetValue(HumanBodyBones.Jaw, out NativeRole jaw)
                    ? jaw.Body
                    : null);
            foreach (string field in new[]
            {
                "greetings", "agros", "unAgros", "deaths", "painSmalls",
                "painBigs", "attack1s", "efforts", "eventLines",
            })
                SetArraySizeIfPresent(data, "faceAnim." + field, 0);
            SetObjectIfPresent(data, "spawnable.policyData", null);
            SetObjectIfPresent(data, "throwVfx", null);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureAnimatorEvent(
            SerializedObject data,
            string path,
            string state)
        {
            SerializedProperty events = Require(data, path);
            if (!events.isArray)
                throw new InvalidOperationException(path + " is not an array.");
            events.arraySize = 1;
            SerializedProperty value = events.GetArrayElementAtIndex(0);
            RequireRelative(value, "animationState").stringValue = state;
            RequireRelative(value, "crossfadeTime").floatValue = 0f;
            RequireRelative(value, "layer").intValue = 0;
            RequireRelative(value, "resetNormalizedTime").intValue = 1;
        }

        private static void SetArraySize(
            SerializedObject data,
            string path,
            int size)
        {
            SerializedProperty array = Require(data, path);
            if (!array.isArray)
                throw new InvalidOperationException(path + " is not an array.");
            array.arraySize = size;
        }

        private static void SetArraySizeIfPresent(
            SerializedObject data,
            string path,
            int size)
        {
            SerializedProperty array = data.FindProperty(path);
            if (array != null && array.isArray)
                array.arraySize = size;
        }

        private static void SetObjectIfPresent(
            SerializedObject data,
            string path,
            UnityEngine.Object value)
        {
            SerializedProperty property = data.FindProperty(path);
            if (property != null
                && property.propertyType == SerializedPropertyType.ObjectReference)
                property.objectReferenceValue = value;
        }

        private static void SetIntIfPresent(
            SerializedObject data,
            string path,
            int value)
        {
            SerializedProperty property = data.FindProperty(path);
            if (property != null)
                property.intValue = value;
        }

        private static void ValidateFaceAnim(
            SerializedObject power,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            SerializedProperty enabled = power.FindProperty(
                "faceAnim.faceAnimEnabled");
            if (enabled != null && enabled.intValue != 0)
                throw new InvalidOperationException(
                    "Patch 6 FaceAnim must remain disabled.");
            SerializedProperty mouth = power.FindProperty("faceAnim.mouthTran");
            UnityEngine.Object expectedMouth = roles.TryGetValue(
                HumanBodyBones.Jaw, out NativeRole jaw)
                ? jaw.Body
                : null;
            if (mouth != null && mouth.objectReferenceValue != expectedMouth)
                throw new InvalidOperationException(
                    "FaceAnim.mouthTran does not point to the Physical Jaw body.");
            foreach (string field in new[]
            {
                "greetings", "agros", "unAgros", "deaths", "painSmalls",
                "painBigs", "attack1s", "efforts", "eventLines",
            })
            {
                SerializedProperty array = power.FindProperty("faceAnim." + field);
                if (array != null && (!array.isArray || array.arraySize != 0))
                    throw new InvalidOperationException(
                        "FaceAnim." + field + " must remain an empty array.");
            }
        }

        private static void ValidateBehaviourShell(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            NpcDefinition definition,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component entity,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowBodies,
            Component puppet,
            NativeBehaviourShell shell)
        {
            if (shell == null)
                throw new InvalidOperationException(
                    "The Patch 6 behaviour shell was not resolved.");
            BehaviourTypes types = BehaviourTypes.Resolve();
            if (outputRoot.GetComponentsInChildren(types.Poolee, true).Length != 1
                || outputRoot.GetComponentsInChildren(types.AIBrain, true).Length != 1
                || outputRoot.GetComponentsInChildren(types.PowerLegs, true).Length != 1
                || outputRoot.GetComponentsInChildren(types.LiteLoco, true).Length != 1
                || outputRoot.GetComponentsInChildren(types.NavMeshAgent, true).Length
                    != 1
                || outputRoot.GetComponentsInChildren(types.FootstepSfx, true).Length
                    != 1
                || outputRoot.GetComponentsInChildren(types.LimbIk, true).Length != 4)
                throw new InvalidOperationException(
                    "The behaviour shell has duplicate or missing native components.");
            if (!animator.enabled || animator.runtimeAnimatorController
                    != shell.Settings.AnimatorController
                || animator.applyRootMotion
                || animator.cullingMode != AnimatorCullingMode.AlwaysAnimate
                || animator.updateMode != AnimatorUpdateMode.Normal)
                throw new InvalidOperationException(
                    "The routed Animator is not in the accepted Patch 6 mode.");
            if (shell.Graph.AiRig.parent != outputRoot.transform
                || shell.Graph.PowerHolder.parent != shell.Graph.AiRig
                || shell.Graph.NeutralRoot.parent != shell.Graph.AiRig
                || shell.Graph.EyeTran.parent != roles[HumanBodyBones.Head].Body
                || shell.Graph.EyeTran.GetComponents<Component>().Length != 1
                || Vector3.Dot(
                    shell.Graph.EyeTran.forward, outputRoot.transform.forward) < 0.999f)
                throw new InvalidOperationException(
                    "The behavior helper hierarchy or physical eye frame is invalid.");

            var power = new SerializedObject(shell.PowerLegs);
            if (Require(power, "puppetMaster").objectReferenceValue != puppet
                || Require(power, "_poolee").objectReferenceValue != shell.Poolee
                || Require(power, "prefabConfig").objectReferenceValue
                    != shell.Movement.MovementConfig
                || Require(power, "overrideConfig").objectReferenceValue
                    != shell.Movement.MovementConfig
                || Require(power, "standingIdle").objectReferenceValue
                    != shell.Movement.StandingPose
                || Require(power, "eyeTran").objectReferenceValue
                    != shell.Graph.EyeTran
                || Require(power, "sfx.impactSource").objectReferenceValue
                    != shell.ImpactSource)
                throw new InvalidOperationException(
                    "PowerLegs is missing its core Puppet/pool/config/pose references.");
            foreach (KeyValuePair<string, UnityEngine.Object> expected
                     in new Dictionary<string, UnityEngine.Object>
                     {
                         ["OpenHand"] = shell.Settings.OpenHand,
                         ["Fist"] = shell.Settings.Fist,
                         ["Pistol"] = shell.Settings.Pistol,
                         ["PistolOffhand"] = shell.Settings.PistolOffhand,
                     })
                if (Require(power, "handPoser." + expected.Key).objectReferenceValue
                    != expected.Value)
                    throw new InvalidOperationException(
                        "PowerLegs hand pose differs at " + expected.Key + ".");
            SerializedProperty health = Require(power, "health.muscles");
            IReadOnlyList<int> healthGroups = HealthGroupsFor(roles);
            if (health.arraySize != healthGroups.Count)
                throw new InvalidOperationException(
                    "PowerLegs health does not have exactly " + healthGroups.Count
                    + " muscle groups.");
            for (int index = 0; index < healthGroups.Count; index++)
                if (health.GetArrayElementAtIndex(index).intValue
                    != healthGroups[index])
                    throw new InvalidOperationException(
                        "PowerLegs health group differs at " + index + ".");
            if (Math.Abs(
                    Require(power, "health.aggression").floatValue
                    - shell.Movement.StartingHostility) > 0.0001f
                || Math.Abs(
                    Require(power, "health.vengefulness").floatValue
                    - shell.Movement.RetaliationVengefulness) > 0.0001f)
                throw new InvalidOperationException(
                    "PowerLegs must retain the hostility response selected "
                    + "by the author.");
            var movementConfig = new SerializedObject(
                shell.Movement.MovementConfig);
            if (Math.Abs(Require(
                    movementConfig,
                    "healthSettings.aggression").floatValue
                    - shell.Movement.StartingHostility) > 0.0001f
                || Math.Abs(Require(
                    movementConfig,
                    "healthSettings.vengefulness").floatValue
                    - shell.Movement.RetaliationVengefulness) > 0.0001f)
                throw new InvalidOperationException(
                    "The runtime-applied movement config must preserve the "
                    + "hostility response selected by the author.");
            ValidateFaceAnim(power, roles);
            if (Require(power, "ik.isHuman").intValue != 0
                || Require(power, "ik.footIkOn").intValue != 0
                || Require(power, "ik.footIkSolvers").arraySize != 2
                || Require(power, "ik.armIkSolvers").arraySize != 2)
                throw new InvalidOperationException(
                    "PowerLegs does not retain the accepted non-human four-solver IK.");

            if (!shell.Vision.enabled || !shell.Vision.isTrigger
                || Math.Abs(shell.Vision.radius - 5f) > 0.0001f
                || Vector3.Distance(
                    shell.Vision.center, new Vector3(0f, 0f, 4f)) > 0.0001f)
                throw new InvalidOperationException(
                    "PowerLegs vision trigger geometry is invalid.");
            if (!shell.ImpactSource.enabled || shell.ImpactSource.playOnAwake
                || shell.ImpactSource.clip != null || shell.ImpactSource.loop
                || shell.ImpactSource.spatialize)
                throw new InvalidOperationException(
                    "ImpactSrc is not the accepted silent AudioSource.");

            ValidateLiteLoco(shell, roles);
            Transform movementFrame = shell.Movement.UsesLegacyFallback
                ? outputRoot.transform
                : RequireMovementAvatarRoot(animationRoot, animator);
            ValidateMovementApplication(
                shell,
                outputRoot.transform,
                movementFrame,
                definition,
                roles);
            foreach (HumanBodyBones role in SolverRoles)
            {
                Component solver = shell.LimbSolvers[role];
                if (solver.transform != roles[role].Target
                    || solver is Behaviour behaviour && behaviour.enabled)
                    throw new InvalidOperationException(
                        role + " LimbIKSlz is not on the routed bone and disabled.");
            }
            ValidateInteractionShell(
                outputRoot,
                roles,
                entity,
                marrowBodies,
                puppet,
                shell.Poolee,
                shell.Brain,
                shell.PowerLegs,
                shell.NavAgent,
                shell.Interaction);
        }

        private static void ValidateLiteLoco(
            NativeBehaviourShell shell,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            var data = new SerializedObject(shell.LiteLoco);
            if (Require(data, "root").objectReferenceValue != shell.Graph.AiRig
                || Require(data, "neutralRoot").objectReferenceValue
                    != shell.Graph.NeutralRoot
                || Math.Abs(Require(data, "weight").floatValue - 1f) > 0.0001f)
                throw new InvalidOperationException(
                    "LiteLoco root/neutral/weight contract is incomplete.");
            SerializedProperty groups = Require(data, "stepGroups");
            if (groups.arraySize != 1)
                throw new InvalidOperationException(
                    "LiteLoco must retain one step group.");
            SerializedProperty group = groups.GetArrayElementAtIndex(0);
            if (RequireRelative(group, "pelvis").objectReferenceValue
                    != shell.Graph.Pelvis
                || RequireRelative(group, "legLength").floatValue <= 0f)
                throw new InvalidOperationException(
                    "LiteLoco pelvis/leg length is invalid.");
            SerializedProperty steps = RequireRelative(group, "footsteps");
            if (steps.arraySize != 2)
                throw new InvalidOperationException(
                    "LiteLoco must retain two footsteps.");
            ValidateLiteLocoStep(
                steps.GetArrayElementAtIndex(0),
                shell.Graph.LeftHip,
                shell.Graph.LeftFoot,
                shell.Graph.LeftFootNeutral,
                roles[HumanBodyBones.LeftFoot].Collider,
                shell);
            ValidateLiteLocoStep(
                steps.GetArrayElementAtIndex(1),
                shell.Graph.RightHip,
                shell.Graph.RightFoot,
                shell.Graph.RightFootNeutral,
                roles[HumanBodyBones.RightFoot].Collider,
                shell);
        }

        private static void ValidateLiteLocoStep(
            SerializedProperty step,
            Transform hip,
            Transform foot,
            Transform neutral,
            Collider collider,
            NativeBehaviourShell shell)
        {
            if (RequireRelative(step, "hip").objectReferenceValue != hip
                || RequireRelative(step, "foot").objectReferenceValue != foot
                || RequireRelative(step, "neutralTarget").objectReferenceValue
                    != neutral
                || RequireRelative(step, "footCollider").objectReferenceValue
                    != collider
                || RequireRelative(step, "liftedMat").objectReferenceValue
                    != shell.Settings.LiftedFootMaterial
                || RequireRelative(step, "stepSfx").objectReferenceValue
                    != shell.FootstepSfx
                || collider.sharedMaterial != shell.Settings.PlantedFootMaterial)
                throw new InvalidOperationException(
                    "LiteLoco footstep references do not match their target foot.");
        }

        private static void ValidateMovementApplication(
            NativeBehaviourShell shell,
            Transform outputRoot,
            Transform movementFrame,
            NpcDefinition definition,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            Patch6MovementBuildSettings movement = shell.Movement;
            if (movement.UsesLegacyFallback)
                return;

            const float tolerance = 0.0002f;
            Vector3 expectedPelvis = roles[HumanBodyBones.Hips].Target.position
                                     + movementFrame.up.normalized
                                     * movement.PelvisHeightOffset;
            if (Vector3.Distance(shell.Graph.Pelvis.position, expectedPelvis)
                > tolerance)
                throw new InvalidOperationException(
                    "Loco pelvis does not retain Movement Profile height tuning.");

            Vector3 pelvisOffset = movementFrame.up.normalized
                                   * movement.PelvisHeightOffset;
            if (Vector3.Distance(
                    shell.Graph.LeftHip.position,
                    roles[HumanBodyBones.LeftUpperLeg].Target.position
                    + pelvisOffset) > tolerance
                || Vector3.Distance(
                    shell.Graph.RightHip.position,
                    roles[HumanBodyBones.RightUpperLeg].Target.position
                    + pelvisOffset) > tolerance)
                throw new InvalidOperationException(
                    "Loco hip helpers no longer follow their accepted upper-leg "
                    + "targets and pelvis height offset.");
            Vector3 expectedLeftFoot = ExpectedMovementFootPosition(
                movementFrame,
                definition.AnatomyProfile.LeftSoleLocal,
                movement.SoleHeight);
            Vector3 expectedRightFoot = ExpectedMovementFootPosition(
                movementFrame,
                definition.AnatomyProfile.RightSoleLocal,
                movement.SoleHeight);
            ValidateMovementPairWidth(
                movementFrame,
                expectedLeftFoot,
                expectedRightFoot,
                shell.Graph.LeftFoot,
                shell.Graph.RightFoot,
                movement.StanceWidthScale,
                "active foot");
            ValidateMovementPairWidth(
                movementFrame,
                expectedLeftFoot,
                expectedRightFoot,
                shell.Graph.LeftFootNeutral,
                shell.Graph.RightFootNeutral,
                movement.StanceWidthScale,
                "neutral foot");
            foreach (Transform foot in new[]
                     {
                         shell.Graph.LeftFoot, shell.Graph.RightFoot,
                     })
                if (Math.Abs(Vector3.Dot(
                                 foot.position - movementFrame.position,
                                 movementFrame.up.normalized)
                             - movement.SoleHeight) > tolerance)
                    throw new InvalidOperationException(
                        "A locomotion foot control does not retain the measured sole height.");
            ValidateMovementFootHeading(
                movementFrame,
                shell.Graph.LeftFoot,
                movement.LeftFootForwardLocal,
                movement.LeftFootYawCorrectionDegrees,
                "Left");
            ValidateMovementFootHeading(
                movementFrame,
                shell.Graph.RightFoot,
                movement.RightFootForwardLocal,
                movement.RightFootYawCorrectionDegrees,
                "Right");

            var nav = new SerializedObject(shell.NavAgent);
            RequireNear(nav, "m_Radius", movement.NavRadius);
            RequireNear(nav, "m_Height", movement.NavHeight);
            RequireNear(
                nav,
                "m_BaseOffset",
                MovementNavBaseOffset(outputRoot, movementFrame, movement));
            RequireNear(nav, "m_Speed", movement.WalkSpeed);
            RequireNear(nav, "m_Acceleration", movement.Acceleration);
            RequireNear(nav, "m_AngularSpeed", movement.AngularSpeed);
            RequireNear(nav, "m_StoppingDistance", movement.StoppingDistance);

            BehaviourTypes types = BehaviourTypes.Resolve();
            Component donorLoco = RequireTemplateComponent(
                shell.Settings.LocomotionReference,
                types.LiteLoco,
                "Stock Locomotion Reference LiteLoco");
            SerializedProperty donorGroup = Require(
                    new SerializedObject(donorLoco), "stepGroups")
                .GetArrayElementAtIndex(0);
            SerializedProperty outputGroup = Require(
                    new SerializedObject(shell.LiteLoco), "stepGroups")
                .GetArrayElementAtIndex(0);
            float donorLeg = RequireRelative(donorGroup, "legLength").floatValue;
            float ratio = movement.MeanLegLength / donorLeg;
            float cadenceScale = movement.StepRateScale / ratio;
            float footSpeedScale = movement.StrideScale
                                   * movement.StepRateScale;
            RequireRelativeNear(outputGroup, "legLength", movement.MeanLegLength);
            RequireScaledCurve(
                RequireRelative(donorGroup, "FootXVCurve"),
                RequireRelative(outputGroup, "FootXVCurve"),
                ratio * movement.StrideScale,
                "FootXVCurve");
            SerializedProperty donorGears = RequireRelative(donorGroup, "gears");
            SerializedProperty outputGears = RequireRelative(outputGroup, "gears");
            if (!donorGears.isArray || !outputGears.isArray
                || donorGears.arraySize != outputGears.arraySize)
                throw new InvalidOperationException(
                    "Scaled LiteLoco gears differ from their donor array.");
            for (int index = 0; index < donorGears.arraySize; index++)
            {
                SerializedProperty donorGear = donorGears.GetArrayElementAtIndex(index);
                SerializedProperty outputGear = outputGears.GetArrayElementAtIndex(index);
                float expectedThreshold = RequireRelative(
                    donorGear, "minStepThreshold").floatValue
                    * ratio * movement.StrideScale;
                RequireRelativeNear(
                    outputGear, "minStepThreshold", expectedThreshold);
                RequireScaledCurve(
                    RequireRelative(donorGear, "StepRateVCurve"),
                    RequireRelative(outputGear, "StepRateVCurve"),
                    cadenceScale,
                    "gear " + index + " StepRateVCurve");
                RequireScaledCurve(
                    RequireRelative(donorGear, "stepHeight"),
                    RequireRelative(outputGear, "stepHeight"),
                    ratio * movement.StepHeightScale,
                    "gear " + index + " stepHeight");
                foreach (string unchanged in new[]
                         {
                             "StepZInterp", "StepAnkleBend", "MuscleUsage",
                         })
                    RequireScaledCurve(
                        RequireRelative(donorGear, unchanged),
                        RequireRelative(outputGear, unchanged),
                        1f,
                        "gear " + index + " " + unchanged);
            }
            SerializedProperty donorGrounder = RequireRelative(
                donorGroup, "grounder");
            SerializedProperty outputGrounder = RequireRelative(
                outputGroup, "grounder");
            RequireRelativeNear(
                outputGrounder,
                "maxStep",
                RequireRelative(donorGrounder, "maxStep").floatValue
                * ratio * movement.StepHeightScale);
            RequireRelativeNear(
                outputGrounder,
                "footSpeed",
                RequireRelative(donorGrounder, "footSpeed").floatValue
                * footSpeedScale);
            SerializedProperty footsteps = RequireRelative(
                outputGroup, "footsteps");
            for (int index = 0; index < footsteps.arraySize; index++)
                RequireRelativeNear(
                    footsteps.GetArrayElementAtIndex(index),
                    "rotationOffset",
                    0f);

            var power = new SerializedObject(shell.PowerLegs);
            RequireNear(power, "roamSpeed", movement.PowerRoamSpeed);
            RequireNear(power, "agroedSpeed", movement.PowerAgroSpeed);
            RequireNear(power, "engagedSpeed", movement.PowerEngagedSpeed);
            foreach (string angular in new[]
                     {
                         "roamAngSpeed", "agroedAngSpeed",
                     })
                RequireNear(power, angular, movement.PowerAngularSpeed);
            var config = new SerializedObject(movement.MovementConfig);
            RequireNear(config, "roamSpeed", movement.ConfigRoamSpeed);
            RequireNear(config, "agroedSpeed", movement.ConfigAgroSpeed);
            foreach (string angular in new[]
                     {
                         "roamAngSpeed", "agroedAngSpeed",
                     })
                RequireNear(config, angular, movement.ConfigAngularSpeed);
        }

        private static Vector3 ExpectedMovementFootPosition(
            Transform movementFrame,
            Vector3 soleLocal,
            float soleHeight)
        {
            Vector3 sourcePosition = movementFrame.TransformPoint(soleLocal);
            Vector3 up = movementFrame.up.normalized;
            Vector3 solePlane = movementFrame.position + up * soleHeight;
            return sourcePosition
                   + up * Vector3.Dot(solePlane - sourcePosition, up);
        }

        private static void ValidateMovementPairWidth(
            Transform movementFrame,
            Vector3 sourceLeft,
            Vector3 sourceRight,
            Transform actualLeft,
            Transform actualRight,
            float scale,
            string label)
        {
            Vector3 axis = movementFrame.right.normalized;
            float leftDistance = Vector3.Dot(
                sourceLeft - movementFrame.position, axis);
            float rightDistance = Vector3.Dot(
                sourceRight - movementFrame.position, axis);
            float center = (leftDistance + rightDistance) * 0.5f;
            Vector3 expectedLeft = sourceLeft
                                   + axis
                                   * (center + (leftDistance - center) * scale
                                      - leftDistance);
            Vector3 expectedRight = sourceRight
                                    + axis
                                    * (center + (rightDistance - center) * scale
                                       - rightDistance);
            if (Vector3.Distance(
                    actualLeft.position, expectedLeft) > 0.0002f
                || Vector3.Distance(
                    actualRight.position, expectedRight) > 0.0002f)
                throw new InvalidOperationException(
                    "The " + label + " helper pair does not retain the Movement "
                    + "Profile stance-width tuning. Expected L/R "
                    + expectedLeft.ToString("R") + " / "
                    + expectedRight.ToString("R") + ", found "
                    + actualLeft.position.ToString("R") + " / "
                    + actualRight.position.ToString("R") + ".");
        }

        private static void ValidateMovementFootHeading(
            Transform movementFrame,
            Transform foot,
            Vector3 localForward,
            float yawCorrection,
            string label)
        {
            Vector3 up = movementFrame.up.normalized;
            Vector3 expected = Quaternion.AngleAxis(yawCorrection, up)
                               * movementFrame.TransformDirection(localForward);
            expected = Vector3.ProjectOnPlane(expected, up).normalized;
            Vector3 actual = Vector3.ProjectOnPlane(foot.forward, up).normalized;
            if (expected.sqrMagnitude < 0.999f || actual.sqrMagnitude < 0.999f
                || Vector3.Dot(expected, actual) < 0.9999f)
                throw new InvalidOperationException(
                    label + " foot heading does not match the Movement Profile.");
        }

        private static void RequireNear(
            SerializedObject owner,
            string path,
            float expected)
        {
            float actual = Require(owner, path).floatValue;
            if (!IsFinite(actual) || Math.Abs(actual - expected) > 0.0001f)
                throw new InvalidOperationException(
                    path + " differs from the Movement Profile: expected "
                    + expected.ToString("R") + ", found " + actual.ToString("R")
                    + ".");
        }

        private static void RequireRelativeNear(
            SerializedProperty owner,
            string path,
            float expected)
        {
            float actual = RequireRelative(owner, path).floatValue;
            if (!IsFinite(actual) || Math.Abs(actual - expected) > 0.0001f)
                throw new InvalidOperationException(
                    owner.propertyPath + "." + path
                    + " differs from the Movement Profile.");
        }

        private static void RequireScaledCurve(
            SerializedProperty donor,
            SerializedProperty output,
            float scale,
            string label)
        {
            AnimationCurve left = donor.animationCurveValue;
            AnimationCurve right = output.animationCurveValue;
            Keyframe[] sourceKeys = left.keys;
            Keyframe[] outputKeys = right.keys;
            if (left.preWrapMode != right.preWrapMode
                || left.postWrapMode != right.postWrapMode
                || sourceKeys.Length != outputKeys.Length)
                throw new InvalidOperationException(
                    label + " does not retain the donor curve topology.");
            for (int index = 0; index < sourceKeys.Length; index++)
            {
                Keyframe source = sourceKeys[index];
                Keyframe value = outputKeys[index];
                if (Math.Abs(source.time - value.time) > 0.0001f
                    || Math.Abs(source.value * scale - value.value) > 0.0001f
                    || Math.Abs(source.inTangent * scale - value.inTangent) > 0.0002f
                    || Math.Abs(source.outTangent * scale - value.outTangent) > 0.0002f
                    || Math.Abs(source.inWeight - value.inWeight) > 0.0001f
                    || Math.Abs(source.outWeight - value.outWeight) > 0.0001f
                    || source.weightedMode != value.weightedMode)
                    throw new InvalidOperationException(
                        label + " differs from its exact scaled donor value at key "
                        + index + ".");
            }
        }

        private static void ValidateNoExternalSceneReferences(GameObject outputRoot)
        {
            foreach (Component component
                     in outputRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;
                while (property.Next(enterChildren))
                {
                    enterChildren = true;
                    if (property.propertyType
                            != SerializedPropertyType.ObjectReference
                        || property.propertyPath == "m_Script"
                        || property.propertyPath == "m_GameObject"
                        || property.propertyPath.StartsWith(
                            "m_CorrespondingSourceObject", StringComparison.Ordinal)
                        || property.propertyPath.StartsWith(
                            "m_Prefab", StringComparison.Ordinal))
                        continue;
                    UnityEngine.Object value = property.objectReferenceValue;
                    Transform referenced = value is GameObject gameObject
                        ? gameObject.transform
                        : value is Component referencedComponent
                            ? referencedComponent.transform
                            : null;
                    if (referenced != null
                        && referenced != outputRoot.transform
                        && !referenced.IsChildOf(outputRoot.transform))
                        throw new InvalidOperationException(
                            component.GetType().Name + "." + property.propertyPath
                            + " points outside the staged NPC hierarchy.");
                }
            }
        }

        private static void AppendBehaviourFingerprint(
            StringBuilder text,
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            NativeBehaviourShell shell)
        {
            text.Append("behaviour=")
                .Append(RelativePath(outputRoot.transform, shell.Graph.AiRig)).Append(',')
                .Append(RelativePath(
                    outputRoot.transform, shell.Graph.PowerHolder)).Append(',')
                .Append(RelativePath(outputRoot.transform, shell.Graph.EyeTran))
                .Append('|')
                .Append("controller=")
                .Append(StableAssetId(shell.Settings.AnimatorController)).Append('|')
                .Append("config=")
                .Append(StableAssetId(shell.Movement.MovementConfig)).Append('|')
                .Append("pose=")
                .Append(GripAssetKey(shell.Movement.StandingPose)).Append('|')
                .Append("movementRecipe=")
                .Append(shell.Movement.ProviderRecipeFingerprint).Append('|')
                .Append("hands=")
                .Append(StableAssetId(shell.Settings.OpenHand)).Append(',')
                .Append(StableAssetId(shell.Settings.Fist)).Append(',')
                .Append(StableAssetId(shell.Settings.Pistol)).Append(',')
                .Append(StableAssetId(shell.Settings.PistolOffhand)).Append('|')
                .Append("feet=")
                .Append(StableAssetId(shell.Settings.PlantedFootMaterial)).Append(',')
                .Append(StableAssetId(shell.Settings.LiftedFootMaterial)).Append('|');
            var power = new SerializedObject(shell.PowerLegs);
            SerializedProperty health = Require(power, "health.muscles");
            text.Append("health=");
            for (int index = 0; index < health.arraySize; index++)
                text.Append(health.GetArrayElementAtIndex(index).intValue).Append(',');
            text.Append("aggression=")
                .Append(Require(power, "health.aggression").floatValue)
                .Append(",vengefulness=")
                .Append(Require(power, "health.vengefulness").floatValue)
                .Append(',');
            SerializedProperty mouth = power.FindProperty("faceAnim.mouthTran");
            text.Append("|mouth=").Append(mouth == null
                ? "field-absent"
                : RelativePath(outputRoot.transform,
                    (mouth.objectReferenceValue as Transform))).Append('|');
            AppendInteractionFingerprint(
                text, outputRoot, roles, shell.Interaction);
        }

        private static string StableAssetId(UnityEngine.Object value)
        {
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                value, out string guid, out long localId)
                ? guid + ":" + localId
                : "missing";
        }

        private sealed class BehaviourGraph
        {
            public Transform AiRig { get; }
            public Transform LocoPelvisFrame { get; }
            public Transform Pelvis { get; }
            public Transform LeftHip { get; }
            public Transform RightHip { get; }
            public Transform NeutralRoot { get; }
            public Transform LeftFoot { get; }
            public Transform RightFoot { get; }
            public Transform LeftFootNeutral { get; }
            public Transform RightFootNeutral { get; }
            public Transform LeftAnkleTarget { get; }
            public Transform RightAnkleTarget { get; }
            public Transform FootstepSfx { get; }
            public Transform LeftHand { get; }
            public Transform RightHand { get; }
            public Transform LeftHandTarget { get; }
            public Transform RightHandTarget { get; }
            public Transform PowerHolder { get; }
            public Transform ImpactSource { get; }
            public Transform EyeTran { get; }

            public BehaviourGraph(
                Transform aiRig,
                Transform locoPelvisFrame,
                Transform pelvis,
                Transform leftHip,
                Transform rightHip,
                Transform neutralRoot,
                Transform leftFoot,
                Transform rightFoot,
                Transform leftFootNeutral,
                Transform rightFootNeutral,
                Transform leftAnkleTarget,
                Transform rightAnkleTarget,
                Transform footstepSfx,
                Transform leftHand,
                Transform rightHand,
                Transform leftHandTarget,
                Transform rightHandTarget,
                Transform powerHolder,
                Transform impactSource,
                Transform eyeTran)
            {
                AiRig = aiRig;
                LocoPelvisFrame = locoPelvisFrame;
                Pelvis = pelvis;
                LeftHip = leftHip;
                RightHip = rightHip;
                NeutralRoot = neutralRoot;
                LeftFoot = leftFoot;
                RightFoot = rightFoot;
                LeftFootNeutral = leftFootNeutral;
                RightFootNeutral = rightFootNeutral;
                LeftAnkleTarget = leftAnkleTarget;
                RightAnkleTarget = rightAnkleTarget;
                FootstepSfx = footstepSfx;
                LeftHand = leftHand;
                RightHand = rightHand;
                LeftHandTarget = leftHandTarget;
                RightHandTarget = rightHandTarget;
                PowerHolder = powerHolder;
                ImpactSource = impactSource;
                EyeTran = eyeTran;
            }
        }

        private sealed class NativeBehaviourShell
        {
            public MarrowNpcToolkitPatch6BehaviourSettings.Resolved Settings
            {
                get;
            }
            public Patch6MovementBuildSettings Movement { get; }
            public BehaviourGraph Graph { get; }
            public Component Poolee { get; }
            public Component Brain { get; }
            public Component LiteLoco { get; }
            public Component NavAgent { get; }
            public Component FootstepSfx { get; }
            public Component PowerLegs { get; }
            public SphereCollider Vision { get; }
            public AudioSource ImpactSource { get; }
            public IReadOnlyDictionary<HumanBodyBones, Component> LimbSolvers
            {
                get;
            }
            public InteractionShell Interaction { get; }

            public NativeBehaviourShell(
                MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings,
                Patch6MovementBuildSettings movement,
                BehaviourGraph graph,
                Component poolee,
                Component brain,
                Component liteLoco,
                Component navAgent,
                Component footstepSfx,
                Component powerLegs,
                SphereCollider vision,
                AudioSource impactSource,
                IReadOnlyDictionary<HumanBodyBones, Component> limbSolvers,
                InteractionShell interaction)
            {
                Settings = settings;
                Movement = movement;
                Graph = graph;
                Poolee = poolee;
                Brain = brain;
                LiteLoco = liteLoco;
                NavAgent = navAgent;
                FootstepSfx = footstepSfx;
                PowerLegs = powerLegs;
                Vision = vision;
                ImpactSource = impactSource;
                LimbSolvers = limbSolvers;
                Interaction = interaction;
            }
        }

        private sealed class BehaviourTypes
        {
            public Type Poolee { get; }
            public Type AIBrain { get; }
            public Type PowerLegs { get; }
            public Type LiteLoco { get; }
            public Type NavMeshAgent { get; }
            public Type FootstepSfx { get; }
            public Type LimbIk { get; }
            public Type BaseEnemyConfig { get; }
            public Type EnemyPoseData { get; }
            public Type HandPoseData { get; }
            public Type RuntimeAnimatorController =>
                typeof(UnityEngine.RuntimeAnimatorController);

            private BehaviourTypes(
                Type poolee,
                Type aiBrain,
                Type powerLegs,
                Type liteLoco,
                Type navMeshAgent,
                Type footstepSfx,
                Type limbIk,
                Type baseEnemyConfig,
                Type enemyPoseData,
                Type handPoseData)
            {
                Poolee = poolee;
                AIBrain = aiBrain;
                PowerLegs = powerLegs;
                LiteLoco = liteLoco;
                NavMeshAgent = navMeshAgent;
                FootstepSfx = footstepSfx;
                LimbIk = limbIk;
                BaseEnemyConfig = baseEnemyConfig;
                EnemyPoseData = enemyPoseData;
                HandPoseData = handPoseData;
            }

            public static BehaviourTypes Resolve()
            {
                return new BehaviourTypes(
                    ResolveType("SLZ.Marrow.Pool.Poolee", "SLZ.Marrow", true),
                    ResolveType("SLZ.Marrow.AI.AIBrain", "SLZ.Marrow", true),
                    ResolveType(
                        "PuppetMasta.BehaviourPowerLegs", "Assembly-CSharp", true),
                    ResolveType(
                        "SLZ.Marrow.Mechanics.LiteLoco", "SLZ.Marrow", true),
                    ResolveType(
                        "UnityEngine.AI.NavMeshAgent", "UnityEngine.AIModule", true),
                    ResolveType(
                        "SLZ.Marrow.Audio.FootstepSFX", "SLZ.Marrow", true),
                    ResolveType("SLZ.VRMK.LimbIKSlz", "Assembly-CSharp", true),
                    ResolveType(
                        "SLZ.Marrow.PuppetMasta.BaseEnemyConfig",
                        "SLZ.Marrow",
                        false),
                    ResolveType(
                        "SLZ.Marrow.Data.EnemyPoseData", "SLZ.Marrow", false),
                    ResolveType(
                        "SLZ.Marrow.Data.HandPoseData", "SLZ.Marrow", false));
            }

            private static Type ResolveType(
                string fullName,
                string assemblyName,
                bool component)
            {
                Type type = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(value => string.Equals(
                        value.GetName().Name, assemblyName,
                        StringComparison.Ordinal))
                    .Select(value => value.GetType(fullName, false))
                    .FirstOrDefault(value => value != null);
                if (type == null
                    || component && !typeof(Component).IsAssignableFrom(type))
                    throw new TypeLoadException(
                        fullName + " is unavailable from " + assemblyName + ".");
                return type;
            }
        }
    }
}
