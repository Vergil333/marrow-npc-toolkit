using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Project-local Patch 6 gaze generation. The provider deliberately does
    /// not create or modify controller assets: gaze is offered only when the
    /// explicitly selected controller already owns the deterministic one-shot
    /// LookAroundIdly initialization state used by the validated reference
    /// NPC. A native pooled-spawn delay also invokes gaze startup after the
    /// spawned hierarchy and player camera references are ready.
    /// </summary>
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        private const float GazeScalarTolerance = 0.0001f;
        private const float GazeInitEventTime = 0.001f;
        private const string GazeInitMethod = "LookAroundIdly";
        private const float GazeSpawnInitDelaySeconds = 0.1f;
        private const string GazeSpawnDelayTypeName =
            "SLZ.Utilities.GenericSpawnDelayEvent";
        private static readonly string[] GazeSpawnInitMethods =
        {
            "LookAroundIdly",
            "LookAtPlayerSimple",
        };

        private static bool TryPreflightGazeBuild(out string detail)
        {
            try
            {
                BehaviourTypes behaviourTypes = BehaviourTypes.Resolve();
                MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings =
                    RequireBehaviourSettings(behaviourTypes);
                GazeTypes gazeTypes = GazeTypes.Resolve();
                ValidateGazeTemplate(
                    settings.BehaviourTemplate, gazeTypes);
                GazeControllerInitContract controller =
                    ValidateGazeControllerInit(settings.AnimatorController);
                detail = "The explicit Behaviour Template has one complete gaze "
                         + "pair with two renderer-used eye targets, and the "
                         + "configured controller has deterministic "
                         + GazeInitMethod + " initialization ("
                         + controller.DisplayName + "), plus native delayed "
                         + "pooled-spawn gaze startup.";
                return true;
            }
            catch (Exception exception)
            {
                detail = "Patch 6 gaze preflight failed: " + exception.Message;
                return false;
            }
        }

        internal static bool TryPreflightGazeForSmoke(out string detail)
        {
            return TryPreflightGazeBuild(out detail);
        }

        private static GazeShell ConfigureGazeShell(
            GameObject outputRoot,
            Transform animationRoot,
            NpcDefinition definition,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            NativeBehaviourShell behaviourShell,
            RendererBridgeShell rendererBridge)
        {
            ValidateGazeArguments(
                outputRoot,
                animationRoot,
                animator,
                roles,
                behaviourShell,
                rendererBridge);
            GazeTypes types = GazeTypes.Resolve();
            ValidateGazeTemplate(
                behaviourShell.Settings.BehaviourTemplate, types);
            GazeControllerInitContract controller =
                ValidateGazeControllerInit(
                    behaviourShell.Settings.AnimatorController);
            EnsureNoGazeComponents(outputRoot, types);

            Transform sourceLeftEye = RequireGazeSourceEye(
                definition,
                animationRoot,
                animator,
                HumanBodyBones.LeftEye);
            Transform sourceRightEye = RequireGazeSourceEye(
                definition,
                animationRoot,
                animator,
                HumanBodyBones.RightEye);
            Transform physicalLeftEye = ResolvePhysicalGazeEye(
                sourceLeftEye,
                HumanBodyBones.LeftEye,
                roles,
                rendererBridge);
            Transform physicalRightEye = ResolvePhysicalGazeEye(
                sourceRightEye,
                HumanBodyBones.RightEye,
                roles,
                rendererBridge);
            if (physicalLeftEye == physicalRightEye)
                throw new InvalidOperationException(
                    "LeftEye and RightEye resolved to the same physical bridge.");

            Component templateEyeAnimator = RequireTemplateComponent(
                behaviourShell.Settings.BehaviourTemplate,
                types.EyeAndHeadAnimator,
                "EyeAndHeadAnimator");
            Component templateLookTarget = RequireTemplateComponent(
                behaviourShell.Settings.BehaviourTemplate,
                types.LookTargetController,
                "LookTargetController");
            Component eyeAnimator = CopyTemplateComponent(
                templateEyeAnimator,
                animator.gameObject,
                "EyeAndHeadAnimator");
            Component lookTarget = CopyTemplateComponent(
                templateLookTarget,
                animator.gameObject,
                "LookTargetController");
            ConfigureEyeAndHeadAnimator(
                eyeAnimator, physicalLeftEye, physicalRightEye);
            ConfigureLookTargetController(lookTarget);
            Component spawnInitializer = animator.gameObject.AddComponent(
                types.GenericSpawnDelayEvent);
            ConfigureGazeSpawnInitializer(
                spawnInitializer, behaviourShell.Poolee, lookTarget);
            ConfigureGazeDeathListeners(
                behaviourShell.PowerLegs, lookTarget, eyeAnimator);

            var shell = new GazeShell(
                eyeAnimator,
                lookTarget,
                sourceLeftEye,
                sourceRightEye,
                physicalLeftEye,
                physicalRightEye,
                spawnInitializer,
                controller);
            ValidateGazeShell(
                outputRoot,
                animationRoot,
                definition,
                animator,
                roles,
                behaviourShell,
                rendererBridge,
                shell);
            return shell;
        }

        private static GazeShell ResolveGazeShell(
            GameObject outputRoot,
            Transform animationRoot,
            NpcDefinition definition,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            NativeBehaviourShell behaviourShell,
            RendererBridgeShell rendererBridge)
        {
            ValidateGazeArguments(
                outputRoot,
                animationRoot,
                animator,
                roles,
                behaviourShell,
                rendererBridge);
            GazeTypes types = GazeTypes.Resolve();
            ValidateGazeTemplate(
                behaviourShell.Settings.BehaviourTemplate, types);
            GazeControllerInitContract controller =
                ValidateGazeControllerInit(
                    behaviourShell.Settings.AnimatorController);
            Component[] eyeAnimators = outputRoot.GetComponentsInChildren(
                types.EyeAndHeadAnimator, true);
            Component[] lookTargets = outputRoot.GetComponentsInChildren(
                types.LookTargetController, true);
            Component[] spawnInitializers = outputRoot.GetComponentsInChildren(
                types.GenericSpawnDelayEvent, true);
            if (eyeAnimators.Length != 1 || lookTargets.Length != 1
                || spawnInitializers.Length != 1)
                throw new InvalidOperationException(
                    "The saved NPC must contain exactly one EyeAndHeadAnimator "
                    + "one LookTargetController, and one native pooled-spawn "
                    + "gaze initializer; found " + eyeAnimators.Length + ", "
                    + lookTargets.Length + ", and "
                    + spawnInitializers.Length + ".");

            Transform sourceLeftEye = RequireGazeSourceEye(
                definition,
                animationRoot,
                animator,
                HumanBodyBones.LeftEye);
            Transform sourceRightEye = RequireGazeSourceEye(
                definition,
                animationRoot,
                animator,
                HumanBodyBones.RightEye);
            Transform physicalLeftEye = ResolvePhysicalGazeEye(
                sourceLeftEye,
                HumanBodyBones.LeftEye,
                roles,
                rendererBridge);
            Transform physicalRightEye = ResolvePhysicalGazeEye(
                sourceRightEye,
                HumanBodyBones.RightEye,
                roles,
                rendererBridge);
            var shell = new GazeShell(
                eyeAnimators[0],
                lookTargets[0],
                sourceLeftEye,
                sourceRightEye,
                physicalLeftEye,
                physicalRightEye,
                spawnInitializers[0],
                controller);
            ValidateGazeShell(
                outputRoot,
                animationRoot,
                definition,
                animator,
                roles,
                behaviourShell,
                rendererBridge,
                shell);
            return shell;
        }

        private static void ValidateGazeArguments(
            GameObject outputRoot,
            Transform animationRoot,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            NativeBehaviourShell behaviourShell,
            RendererBridgeShell rendererBridge)
        {
            if (outputRoot == null || animationRoot == null || animator == null
                || roles == null || behaviourShell == null
                || rendererBridge == null)
                throw new ArgumentNullException(
                    "Gaze generation requires the output, Animator, role map, "
                    + "behaviour shell, and renderer bridge.");
            if (!animator.transform.IsChildOf(animationRoot)
                || roles.Count != EntityOrderFor(roles).Count)
                throw new InvalidOperationException(
                    "Gaze generation requires the routed Humanoid Animator and "
                    + "complete " + EntityOrderFor(roles).Count
                    + "-body role map.");
        }

        private static void ValidateGazeTemplate(
            GameObject template,
            GazeTypes types)
        {
            ValidatePersistentAsset(
                template, typeof(GameObject), "Behaviour Template");
            Component eyeAnimator = RequireTemplateComponent(
                template, types.EyeAndHeadAnimator, "EyeAndHeadAnimator");
            Component lookTarget = RequireTemplateComponent(
                template, types.LookTargetController, "LookTargetController");
            Animator[] animators = template.GetComponentsInChildren<Animator>(true)
                .Where(value => value != null
                                && value.avatar != null
                                && value.avatar.isHuman)
                .ToArray();
            if (animators.Length != 1
                || eyeAnimator.gameObject != animators[0].gameObject
                || lookTarget.gameObject != animators[0].gameObject)
                throw new InvalidOperationException(
                    "The Behaviour Template gaze components must both be on its "
                    + "one Humanoid Animator GameObject.");

            var eyeData = new SerializedObject(eyeAnimator);
            Transform leftEye = Require(eyeData, "controlData.leftEye")
                .objectReferenceValue as Transform;
            Transform rightEye = Require(eyeData, "controlData.rightEye")
                .objectReferenceValue as Transform;
            if (leftEye == null || rightEye == null || leftEye == rightEye
                || !leftEye.IsChildOf(template.transform)
                || !rightEye.IsChildOf(template.transform))
                throw new InvalidOperationException(
                    "The Behaviour Template must map two distinct gaze eye "
                    + "targets inside its prefab hierarchy.");
            if (!TemplateGazeTargetIsRendererUsed(template, leftEye)
                || !TemplateGazeTargetIsRendererUsed(template, rightEye))
                throw new InvalidOperationException(
                    "Both Behaviour Template gaze eyes must be used by a "
                    + "SkinnedMeshRenderer or its SkinnedBoneRebind mapping.");
        }

        private static bool TemplateGazeTargetIsRendererUsed(
            GameObject template,
            Transform target)
        {
            foreach (SkinnedMeshRenderer renderer
                     in template.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer != null && renderer.bones != null
                    && renderer.bones.Contains(target))
                    return true;

            Type rebindType = ResolveRendererBridgeComponentType();
            foreach (Component rebind
                     in template.GetComponentsInChildren(rebindType, true))
            {
                var data = new SerializedObject(rebind);
                SerializedProperty bones = Require(data, "bones");
                for (int index = 0; index < bones.arraySize; index++)
                    if (bones.GetArrayElementAtIndex(index)
                            .objectReferenceValue == target)
                        return true;
            }
            return false;
        }

        private static GazeControllerInitContract ValidateGazeControllerInit(
            RuntimeAnimatorController runtimeController)
        {
            ValidatePersistentAsset(
                runtimeController,
                typeof(RuntimeAnimatorController),
                "Animator Controller");
            AnimatorOverrideController overrides =
                runtimeController as AnimatorOverrideController;
            RuntimeAnimatorController baseRuntime = overrides == null
                ? runtimeController
                : overrides.runtimeAnimatorController;
            AnimatorController controller = baseRuntime as AnimatorController;
            if (controller == null)
                throw new InvalidOperationException(
                    "The gaze controller must be an inspectable "
                    + "AnimatorController or AnimatorOverrideController.");

            var effectiveClips = new HashSet<AnimationClip>();
            foreach (AnimationClip clip in controller.animationClips)
            {
                AnimationClip effective = ResolveGazeOverride(clip, overrides);
                if (effective != null)
                    effectiveClips.Add(effective);
            }
            var eventClips = new List<AnimationClip>();
            foreach (AnimationClip clip in effectiveClips)
            {
                AnimationEvent[] matching = AnimationUtility
                    .GetAnimationEvents(clip)
                    .Where(GazeInitEventMatches)
                    .ToArray();
                for (int index = 0; index < matching.Length; index++)
                    eventClips.Add(clip);
            }
            if (eventClips.Count != 1)
                throw new InvalidOperationException(
                    "The configured controller must contain exactly one "
                    + GazeInitMethod + " AnimationEvent at "
                    + GazeInitEventTime.ToString(
                        "0.000", CultureInfo.InvariantCulture)
                    + " seconds with RequireReceiver; found "
                    + eventClips.Count + ".");

            AnimationClip initClip = eventClips[0];
            var candidates = new List<GazeControllerInitContract>();
            AnimatorControllerLayer[] layers = controller.layers;
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                AnimatorControllerLayer layer = layers[layerIndex];
                AnimatorState defaultState = layer.stateMachine?.defaultState;
                AnimationClip defaultClip = defaultState == null
                    ? null
                    : ResolveGazeOverride(
                        defaultState.motion as AnimationClip, overrides);
                if (defaultClip != initClip)
                    continue;
                AnimatorStateTransition[] transitions = defaultState.transitions;
                if (layer.defaultWeight < 1f - GazeScalarTolerance
                    || transitions == null || transitions.Length != 1
                    || transitions[0].destinationState == null
                    || !transitions[0].hasExitTime
                    || Math.Abs(transitions[0].exitTime - 1f)
                        > GazeScalarTolerance
                    || !transitions[0].hasFixedDuration
                    || transitions[0].conditions.Length != 0)
                    throw new InvalidOperationException(
                        "The " + GazeInitMethod + " clip must be the default "
                        + "state of a full-weight layer with one unconditional, "
                        + "fixed-duration exit-time transition.");
                candidates.Add(new GazeControllerInitContract(
                    runtimeController,
                    layerIndex,
                    layer.name,
                    defaultState.name,
                    initClip));
            }
            if (candidates.Count != 1)
                throw new InvalidOperationException(
                    "Exactly one full-weight controller layer must start in the "
                    + GazeInitMethod + " event clip; found "
                    + candidates.Count + ".");
            return candidates[0];
        }

        private static AnimationClip ResolveGazeOverride(
            AnimationClip clip,
            AnimatorOverrideController overrides)
        {
            if (clip == null)
                return null;
            AnimationClip replacement = overrides == null
                ? null
                : overrides[clip];
            return replacement != null ? replacement : clip;
        }

        private static bool GazeInitEventMatches(AnimationEvent value)
        {
            return value != null
                   && string.Equals(
                       value.functionName, GazeInitMethod,
                       StringComparison.Ordinal)
                   && Math.Abs(value.time - GazeInitEventTime)
                       <= GazeScalarTolerance
                   && value.messageOptions
                       == SendMessageOptions.RequireReceiver
                   && string.IsNullOrEmpty(value.stringParameter)
                   && value.objectReferenceParameter == null
                   && Math.Abs(value.floatParameter) <= GazeScalarTolerance
                   && value.intParameter == 0;
        }

        private static void EnsureNoGazeComponents(
            GameObject outputRoot,
            GazeTypes types)
        {
            int eyeCount = outputRoot.GetComponentsInChildren(
                types.EyeAndHeadAnimator, true).Length;
            int targetCount = outputRoot.GetComponentsInChildren(
                types.LookTargetController, true).Length;
            int spawnInitializerCount = outputRoot.GetComponentsInChildren(
                types.GenericSpawnDelayEvent, true).Length;
            if (eyeCount != 0 || targetCount != 0
                || spawnInitializerCount != 0)
                throw new InvalidOperationException(
                    "The staged Avatar already contains gaze components. Gaze "
                    + "generation requires a clean Avatar so it can construct "
                    + "one fresh EyeAndHeadAnimator/LookTargetController pair "
                    + "and pooled-spawn initializer.");
        }

        private static Transform RequireGazeSourceEye(
            NpcDefinition definition,
            Transform animationRoot,
            Animator animator,
            HumanBodyBones eyeRole)
        {
            NpcAvatarSourceProfile sourceProfile =
                definition?.AvatarSourceProfile;
            if (sourceProfile == null || animationRoot == null
                || animationRoot.childCount != 1 || animator == null)
                throw new InvalidOperationException(
                    "Gaze eye resolution requires the accepted Avatar source "
                    + "profile, its one routed source instance, and Animator.");
            Transform avatarRoot = animationRoot.GetChild(0);
            Transform animatorTransform = string.IsNullOrWhiteSpace(
                    sourceProfile.AnimatorPath)
                ? avatarRoot
                : avatarRoot.Find(sourceProfile.AnimatorPath);
            if (animatorTransform != animator.transform)
                throw new InvalidOperationException(
                    "The accepted AvatarSourceProfile.AnimatorPath no longer "
                    + "resolves to the routed gaze Animator.");

            Transform eye = ResolveAvatarBone(
                sourceProfile, avatarRoot, eyeRole);
            // Persistent prefab assets may return null for optional Humanoid
            // eyes. The captured source path is therefore authoritative. A
            // live mapping, when Unity exposes it, must agree with that path.
            Transform liveEye = animator.GetBoneTransform(eyeRole);
            if (liveEye != null && liveEye != eye)
                throw new InvalidOperationException(
                    "The live Humanoid mapping for " + eyeRole
                    + " disagrees with its accepted AvatarSourceProfile path.");
            return eye;
        }

        private static Transform ResolvePhysicalGazeEye(
            Transform sourceEye,
            HumanBodyBones eyeRole,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            RendererBridgeShell rendererBridge)
        {
            bool rendererUsed = rendererBridge.Renderers.Any(
                renderer => renderer != null && renderer.bones != null
                            && renderer.bones.Contains(sourceEye));
            if (!rendererUsed
                || !rendererBridge.Bridges.TryGetValue(
                    sourceEye, out Transform physicalEye)
                || physicalEye == null
                || !rendererBridge.OwnerRoles.TryGetValue(
                    sourceEye, out HumanBodyBones ownerRole)
                || ownerRole != HumanBodyBones.Head)
                throw new InvalidOperationException(
                    eyeRole + " must be a renderer-used Humanoid eye with a "
                    + "physical renderer bridge owned by Head.");
            if (physicalEye.parent != roles[HumanBodyBones.Head].Body)
                throw new InvalidOperationException(
                    eyeRole + " physical gaze target must be a direct child of "
                    + "the Head body.");
            Component[] components = physicalEye.GetComponents<Component>();
            if (components.Length != 1 || !(components[0] is Transform))
                throw new InvalidOperationException(
                    eyeRole + " physical gaze target must remain a "
                    + "component-free renderer bridge Transform.");
            return physicalEye;
        }

        private static void ConfigureEyeAndHeadAnimator(
            Component eyeAnimator,
            Transform physicalLeftEye,
            Transform physicalRightEye)
        {
            var data = new SerializedObject(eyeAnimator);
            SetFloat(data, "headWeight", 0f);
            SetObject(data, "headBoneNonMecanimXform", null);
            SetInt(data, "areUpdatedControlledExternally", 0);
            SetInt(data, "controlData.eyeControl", 2);
            SetObject(data, "controlData.leftEye", physicalLeftEye);
            SetObject(data, "controlData.rightEye", physicalRightEye);
            foreach (string field in new[]
                     {
                         "isEyeBallDefaultSet", "isEyeBoneDefaultSet",
                         "isEyeBallLookUpSet", "isEyeBoneLookUpSet",
                         "isEyeBallLookDownSet", "isEyeBoneLookDownSet",
                     })
                SetInt(data, "controlData." + field, 0);
            foreach (string limiterName in new[]
                     {
                         "leftBoneEyeRotationLimiter",
                         "rightBoneEyeRotationLimiter",
                         "leftEyeballEyeRotationLimiter",
                         "rightEyeballEyeRotationLimiter",
                     })
            {
                string limiter = "controlData." + limiterName;
                SetObject(data, limiter + ".transform", null);
                Require(data, limiter + ".defaultQ").quaternionValue =
                    Quaternion.identity;
                Require(data, limiter + ".lookUpQ").quaternionValue =
                    Quaternion.identity;
                Require(data, limiter + ".lookDownQ").quaternionValue =
                    Quaternion.identity;
                SetFloat(data, limiter + ".maxUpAngle", 0f);
                SetFloat(data, limiter + ".maxDownAngle", 0f);
                SetInt(data, limiter + ".isLookUpSet", 0);
                SetInt(data, limiter + ".isLookDownSet", 0);
            }
            SetInt(data, "controlData.eyelidControl", 2);
            SetInt(data, "controlData.eyelidsFollowEyesVertically", 0);
            SetInt(data, "eyelidsFollowEyesVertically", 0);
            foreach (string eyeLid in new[]
                     {
                         "upperEyeLidLeft", "upperEyeLidRight",
                         "lowerEyeLidLeft", "lowerEyeLidRight",
                     })
                SetObject(data, "controlData." + eyeLid, null);
            foreach (string array in new[]
                     {
                         "blendshapesForBlinking",
                         "blendshapesForLookingUp",
                         "blendshapesForLookingDown",
                         "blendshapesConfigs",
                     })
                SetArraySize(data, "controlData." + array, 0);
            SetBool(data, "m_Enabled", true);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureLookTargetController(Component lookTarget)
        {
            var data = new SerializedObject(lookTarget);
            SetArraySize(data, "pointsOfInterest", 0);
            SetFloat(data, "lookAtPlayerRatio", 1f);
            SetFloat(data, "stareBackFactor", 0.8f);
            SetFloat(data, "noticePlayerDistance", 15f);
            SetFloat(data, "personalSpaceDistance", 0f);
            SetFloat(data, "minLookTime", 3f);
            SetFloat(data, "maxLookTime", 10f);
            SetObject(data, "thirdPersonPlayerEyeCenter", null);
            SetInt(data, "keepTargetEvenWhenLost", 0);
            foreach (string eventName in new[]
                     {
                         "OnStartLookingAtPlayer",
                         "OnStopLookingAtPlayer",
                         "OnPlayerEntersPersonalSpace",
                         "OnLookAwayFromShyness",
                     })
                SetArraySize(
                    data,
                    eventName + ".m_PersistentCalls.m_Calls",
                    0);
            SetBool(data, "m_Enabled", true);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureGazeSpawnInitializer(
            Component spawnInitializer,
            Component poolee,
            Component lookTarget)
        {
            if (spawnInitializer == null || poolee == null || lookTarget == null)
                throw new ArgumentNullException(
                    "Pooled gaze initialization requires its native delay, "
                    + "Poolee, and LookTargetController.");
            var data = new SerializedObject(spawnInitializer);
            SetObject(data, "_poolee", poolee);
            SetFloat(
                data, "secondsUntilEvent", GazeSpawnInitDelaySeconds);
            SerializedProperty calls = Require(
                data, "delayedEvent.m_PersistentCalls.m_Calls");
            if (!calls.isArray)
                throw new InvalidOperationException(
                    "GenericSpawnDelayEvent.delayedEvent calls are not an "
                    + "inspectable array.");
            calls.arraySize = 0;
            foreach (string methodName in GazeSpawnInitMethods)
                AppendGazeSpawnCall(calls, lookTarget, methodName);
            SetBool(data, "m_Enabled", true);
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawnInitializer);
        }

        private static void AppendGazeSpawnCall(
            SerializedProperty calls,
            Component lookTarget,
            string methodName)
        {
            int index = calls.arraySize;
            calls.arraySize = index + 1;
            SerializedProperty call = calls.GetArrayElementAtIndex(index);
            RequireRelative(call, "m_Target").objectReferenceValue = lookTarget;
            RequireRelative(call, "m_TargetAssemblyTypeName").stringValue =
                GazeTargetAssemblyTypeName(lookTarget);
            RequireRelative(call, "m_MethodName").stringValue = methodName;
            // PersistentListenerMode.Void invokes the public, parameterless
            // native gaze methods in the exact serialized call order.
            RequireRelative(call, "m_Mode").intValue = 1;
            SerializedProperty arguments = RequireRelative(call, "m_Arguments");
            RequireRelative(arguments, "m_ObjectArgument").objectReferenceValue =
                null;
            RequireRelative(
                arguments, "m_ObjectArgumentAssemblyTypeName").stringValue =
                string.Empty;
            RequireRelative(arguments, "m_IntArgument").intValue = 0;
            RequireRelative(arguments, "m_FloatArgument").floatValue = 0f;
            RequireRelative(arguments, "m_StringArgument").stringValue =
                string.Empty;
            RequireRelative(arguments, "m_BoolArgument").boolValue = false;
            RequireRelative(call, "m_CallState").intValue = 2;
        }

        private static string GazeTargetAssemblyTypeName(Component lookTarget)
        {
            Type type = lookTarget.GetType();
            return type.FullName + ", " + type.Assembly.GetName().Name;
        }

        private static void ConfigureGazeDeathListeners(
            Component powerLegs,
            Component lookTarget,
            Component eyeAnimator)
        {
            var data = new SerializedObject(powerLegs);
            SerializedProperty calls = Require(
                data, "OnDeathStart.m_PersistentCalls.m_Calls");
            if (!calls.isArray)
                throw new InvalidOperationException(
                    "BehaviourPowerLegs.OnDeathStart persistent calls are not "
                    + "an inspectable array.");
            for (int index = calls.arraySize - 1; index >= 0; index--)
            {
                UnityEngine.Object target = RequireRelative(
                    calls.GetArrayElementAtIndex(index), "m_Target")
                    .objectReferenceValue;
                if (target == null || target == lookTarget || target == eyeAnimator)
                    DeleteGazePersistentCall(calls, index);
            }
            AppendGazeDisableListener(calls, lookTarget);
            AppendGazeDisableListener(calls, eyeAnimator);
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(powerLegs);
        }

        private static void DeleteGazePersistentCall(
            SerializedProperty calls,
            int index)
        {
            int originalSize = calls.arraySize;
            calls.DeleteArrayElementAtIndex(index);
            if (calls.arraySize == originalSize)
                calls.DeleteArrayElementAtIndex(index);
        }

        private static void AppendGazeDisableListener(
            SerializedProperty calls,
            Component target)
        {
            int index = calls.arraySize;
            calls.arraySize = index + 1;
            SerializedProperty call = calls.GetArrayElementAtIndex(index);
            RequireRelative(call, "m_Target").objectReferenceValue = target;
            RequireRelative(call, "m_TargetAssemblyTypeName").stringValue =
                "UnityEngine.Behaviour, UnityEngine";
            RequireRelative(call, "m_MethodName").stringValue = "set_enabled";
            RequireRelative(call, "m_Mode").intValue = 6;
            SerializedProperty arguments = RequireRelative(call, "m_Arguments");
            RequireRelative(arguments, "m_ObjectArgument").objectReferenceValue =
                null;
            RequireRelative(
                arguments, "m_ObjectArgumentAssemblyTypeName").stringValue =
                string.Empty;
            RequireRelative(arguments, "m_IntArgument").intValue = 0;
            RequireRelative(arguments, "m_FloatArgument").floatValue = 0f;
            RequireRelative(arguments, "m_StringArgument").stringValue =
                string.Empty;
            RequireRelative(arguments, "m_BoolArgument").boolValue = false;
            RequireRelative(call, "m_CallState").intValue = 2;
        }

        private static void ValidateGazeShell(
            GameObject outputRoot,
            Transform animationRoot,
            NpcDefinition definition,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            NativeBehaviourShell behaviourShell,
            RendererBridgeShell rendererBridge,
            GazeShell shell)
        {
            ValidateGazeArguments(
                outputRoot,
                animationRoot,
                animator,
                roles,
                behaviourShell,
                rendererBridge);
            if (shell == null || shell.EyeAnimator == null
                || shell.LookTarget == null || shell.SpawnInitializer == null)
                throw new InvalidOperationException(
                    "The gaze shell was not resolved.");
            GazeTypes types = GazeTypes.Resolve();
            if (outputRoot.GetComponentsInChildren(
                    types.EyeAndHeadAnimator, true).Length != 1
                || outputRoot.GetComponentsInChildren(
                    types.LookTargetController, true).Length != 1
                || outputRoot.GetComponentsInChildren(
                    types.GenericSpawnDelayEvent, true).Length != 1
                || shell.EyeAnimator.gameObject != animator.gameObject
                || shell.LookTarget.gameObject != animator.gameObject
                || shell.SpawnInitializer.gameObject != animator.gameObject)
                throw new InvalidOperationException(
                    "The gaze pair and pooled-spawn initializer must exist "
                    + "exactly once and share the routed Humanoid Animator "
                    + "GameObject.");
            if (!(shell.EyeAnimator is Behaviour eyeBehaviour)
                || !(shell.LookTarget is Behaviour targetBehaviour)
                || !(shell.SpawnInitializer is Behaviour spawnBehaviour)
                || !eyeBehaviour.enabled || !targetBehaviour.enabled
                || !spawnBehaviour.enabled)
                throw new InvalidOperationException(
                    "All generated gaze behaviours must be enabled.");
            if (animator.runtimeAnimatorController
                != behaviourShell.Settings.AnimatorController)
                throw new InvalidOperationException(
                    "The routed Animator does not use the explicitly configured "
                    + "gaze-ready controller.");

            if (RequireGazeSourceEye(
                    definition,
                    animationRoot,
                    animator,
                    HumanBodyBones.LeftEye)
                    != shell.SourceLeftEye
                || RequireGazeSourceEye(
                    definition,
                    animationRoot,
                    animator,
                    HumanBodyBones.RightEye)
                    != shell.SourceRightEye
                || ResolvePhysicalGazeEye(
                        shell.SourceLeftEye,
                        HumanBodyBones.LeftEye,
                        roles,
                        rendererBridge)
                    != shell.PhysicalLeftEye
                || ResolvePhysicalGazeEye(
                        shell.SourceRightEye,
                        HumanBodyBones.RightEye,
                        roles,
                        rendererBridge)
                    != shell.PhysicalRightEye
                || shell.PhysicalLeftEye == shell.PhysicalRightEye)
                throw new InvalidOperationException(
                    "The generated gaze pair lost its two physical renderer "
                    + "bridge targets.");

            ValidateEyeAndHeadAnimator(shell);
            ValidateLookTargetController(shell.LookTarget);
            ValidateGazeSpawnInitializer(
                shell.SpawnInitializer,
                behaviourShell.Poolee,
                shell.LookTarget);
            GazeControllerInitContract controller =
                ValidateGazeControllerInit(
                    behaviourShell.Settings.AnimatorController);
            if (!string.Equals(
                    controller.Receipt,
                    shell.Controller.Receipt,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The configured gaze initialization contract changed while "
                    + "the native prefab was generated.");
            ValidateGazeDeathListeners(
                behaviourShell.PowerLegs,
                shell.LookTarget,
                shell.EyeAnimator);
        }

        private static void ValidateEyeAndHeadAnimator(GazeShell shell)
        {
            var data = new SerializedObject(shell.EyeAnimator);
            if (Math.Abs(Require(data, "headWeight").floatValue)
                    > GazeScalarTolerance
                || Require(data, "headBoneNonMecanimXform")
                    .objectReferenceValue != null
                || Require(data, "areUpdatedControlledExternally").intValue != 0
                || Require(data, "controlData.eyeControl").intValue != 2
                || Require(data, "controlData.leftEye").objectReferenceValue
                    != shell.PhysicalLeftEye
                || Require(data, "controlData.rightEye").objectReferenceValue
                    != shell.PhysicalRightEye
                || Require(data, "controlData.eyelidControl").intValue != 2
                || Require(data, "controlData.eyelidsFollowEyesVertically")
                    .intValue != 0
                || Require(data, "eyelidsFollowEyesVertically").intValue != 0)
                throw new InvalidOperationException(
                    "EyeAndHeadAnimator is not bound to the accepted physical "
                    + "eye-only, zero-head-weight contract.");
            foreach (string field in new[]
                     {
                         "isEyeBallDefaultSet", "isEyeBoneDefaultSet",
                         "isEyeBallLookUpSet", "isEyeBoneLookUpSet",
                         "isEyeBallLookDownSet", "isEyeBoneLookDownSet",
                     })
                if (Require(data, "controlData." + field).intValue != 0)
                    throw new InvalidOperationException(
                        "EyeAndHeadAnimator retained initialized donor state in "
                        + field + ".");
            foreach (string limiterName in new[]
                     {
                         "leftBoneEyeRotationLimiter",
                         "rightBoneEyeRotationLimiter",
                         "leftEyeballEyeRotationLimiter",
                         "rightEyeballEyeRotationLimiter",
                     })
            {
                string limiter = "controlData." + limiterName;
                if (Require(data, limiter + ".transform")
                        .objectReferenceValue != null
                    || Require(data, limiter + ".defaultQ").quaternionValue
                        != Quaternion.identity
                    || Require(data, limiter + ".lookUpQ").quaternionValue
                        != Quaternion.identity
                    || Require(data, limiter + ".lookDownQ").quaternionValue
                        != Quaternion.identity
                    || Math.Abs(Require(data, limiter + ".maxUpAngle")
                        .floatValue) > GazeScalarTolerance
                    || Math.Abs(Require(data, limiter + ".maxDownAngle")
                        .floatValue) > GazeScalarTolerance
                    || Require(data, limiter + ".isLookUpSet").intValue != 0
                    || Require(data, limiter + ".isLookDownSet").intValue != 0)
                    throw new InvalidOperationException(
                        limiterName + " retained donor calibration state.");
            }
            foreach (string array in new[]
                     {
                         "blendshapesForBlinking",
                         "blendshapesForLookingUp",
                         "blendshapesForLookingDown",
                         "blendshapesConfigs",
                     })
                if (Require(data, "controlData." + array).arraySize != 0)
                    throw new InvalidOperationException(
                        "EyeAndHeadAnimator must not retain donor " + array + ".");
        }

        private static void ValidateLookTargetController(Component lookTarget)
        {
            var data = new SerializedObject(lookTarget);
            if (Require(data, "pointsOfInterest").arraySize != 0
                || Math.Abs(Require(data, "lookAtPlayerRatio").floatValue - 1f)
                    > GazeScalarTolerance
                || Math.Abs(Require(data, "stareBackFactor").floatValue - 0.8f)
                    > GazeScalarTolerance
                || Math.Abs(Require(data, "noticePlayerDistance").floatValue - 15f)
                    > GazeScalarTolerance
                || Math.Abs(Require(data, "personalSpaceDistance").floatValue)
                    > GazeScalarTolerance
                || Math.Abs(Require(data, "minLookTime").floatValue - 3f)
                    > GazeScalarTolerance
                || Math.Abs(Require(data, "maxLookTime").floatValue - 10f)
                    > GazeScalarTolerance
                || Require(data, "thirdPersonPlayerEyeCenter")
                    .objectReferenceValue != null
                || Require(data, "keepTargetEvenWhenLost").intValue != 0)
                throw new InvalidOperationException(
                    "LookTargetController does not match the deterministic "
                    + "player-gaze scalar contract.");
            foreach (string eventName in new[]
                     {
                         "OnStartLookingAtPlayer",
                         "OnStopLookingAtPlayer",
                         "OnPlayerEntersPersonalSpace",
                         "OnLookAwayFromShyness",
                     })
                if (Require(
                        data,
                        eventName + ".m_PersistentCalls.m_Calls").arraySize != 0)
                    throw new InvalidOperationException(
                        "LookTargetController retained donor listeners in "
                        + eventName + ".");
        }

        private static void ValidateGazeSpawnInitializer(
            Component spawnInitializer,
            Component poolee,
            Component lookTarget)
        {
            var data = new SerializedObject(spawnInitializer);
            if (Require(data, "_poolee").objectReferenceValue != poolee
                || Math.Abs(
                    Require(data, "secondsUntilEvent").floatValue
                    - GazeSpawnInitDelaySeconds) > GazeScalarTolerance)
                throw new InvalidOperationException(
                    "The pooled gaze initializer lost its Poolee or exact "
                    + GazeSpawnInitDelaySeconds.ToString(
                        "0.0", CultureInfo.InvariantCulture)
                    + " second delay.");

            SerializedProperty calls = Require(
                data, "delayedEvent.m_PersistentCalls.m_Calls");
            if (calls.arraySize != GazeSpawnInitMethods.Length)
                throw new InvalidOperationException(
                    "The pooled gaze initializer must contain exactly "
                    + GazeSpawnInitMethods.Length + " ordered calls.");
            string targetTypeName = GazeTargetAssemblyTypeName(lookTarget);
            for (int index = 0; index < GazeSpawnInitMethods.Length; index++)
            {
                SerializedProperty call = calls.GetArrayElementAtIndex(index);
                if (RequireRelative(call, "m_Target").objectReferenceValue
                        != lookTarget
                    || !string.Equals(
                        RequireRelative(call, "m_TargetAssemblyTypeName")
                            .stringValue,
                        targetTypeName,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        RequireRelative(call, "m_MethodName").stringValue,
                        GazeSpawnInitMethods[index],
                        StringComparison.Ordinal)
                    || RequireRelative(call, "m_Mode").intValue != 1
                    || RequireRelative(call, "m_CallState").intValue != 2)
                    throw new InvalidOperationException(
                        "The pooled gaze initializer call at index " + index
                        + " must be enabled " + GazeSpawnInitMethods[index]
                        + " on the generated LookTargetController.");
                SerializedProperty arguments = RequireRelative(
                    call, "m_Arguments");
                if (RequireRelative(arguments, "m_ObjectArgument")
                        .objectReferenceValue != null
                    || !string.IsNullOrEmpty(RequireRelative(
                        arguments,
                        "m_ObjectArgumentAssemblyTypeName").stringValue)
                    || RequireRelative(arguments, "m_IntArgument").intValue != 0
                    || Math.Abs(RequireRelative(arguments, "m_FloatArgument")
                        .floatValue) > GazeScalarTolerance
                    || !string.IsNullOrEmpty(RequireRelative(
                        arguments, "m_StringArgument").stringValue)
                    || RequireRelative(arguments, "m_BoolArgument").boolValue)
                    throw new InvalidOperationException(
                        "A pooled gaze initializer call retained unexpected "
                        + "argument data.");
            }
        }

        private static void ValidateGazeDeathListeners(
            Component powerLegs,
            Component lookTarget,
            Component eyeAnimator)
        {
            var data = new SerializedObject(powerLegs);
            SerializedProperty calls = Require(
                data, "OnDeathStart.m_PersistentCalls.m_Calls");
            int lookTargetCount = 0;
            int eyeAnimatorCount = 0;
            var gazeOrder = new List<UnityEngine.Object>();
            for (int index = 0; index < calls.arraySize; index++)
            {
                SerializedProperty call = calls.GetArrayElementAtIndex(index);
                UnityEngine.Object target = RequireRelative(call, "m_Target")
                    .objectReferenceValue;
                if (target == null)
                    throw new InvalidOperationException(
                        "OnDeathStart contains a missing persistent target.");
                if (target != lookTarget && target != eyeAnimator)
                    continue;
                if (!string.Equals(
                        RequireRelative(call, "m_TargetAssemblyTypeName")
                            .stringValue,
                        "UnityEngine.Behaviour, UnityEngine",
                        StringComparison.Ordinal)
                    || !string.Equals(
                        RequireRelative(call, "m_MethodName").stringValue,
                        "set_enabled",
                        StringComparison.Ordinal)
                    || RequireRelative(call, "m_Mode").intValue != 6
                    || RequireRelative(call, "m_CallState").intValue != 2)
                    throw new InvalidOperationException(
                        "A gaze OnDeathStart listener is not an enabled bool "
                        + "persistent call to Behaviour.set_enabled.");
                SerializedProperty arguments = RequireRelative(
                    call, "m_Arguments");
                if (RequireRelative(arguments, "m_ObjectArgument")
                        .objectReferenceValue != null
                    || !string.IsNullOrEmpty(RequireRelative(
                        arguments,
                        "m_ObjectArgumentAssemblyTypeName").stringValue)
                    || RequireRelative(arguments, "m_IntArgument").intValue != 0
                    || Math.Abs(RequireRelative(arguments, "m_FloatArgument")
                        .floatValue) > GazeScalarTolerance
                    || !string.IsNullOrEmpty(RequireRelative(
                        arguments, "m_StringArgument").stringValue)
                    || RequireRelative(arguments, "m_BoolArgument").boolValue)
                    throw new InvalidOperationException(
                        "A gaze OnDeathStart listener must pass false and no "
                        + "other argument data.");
                if (target == lookTarget)
                    lookTargetCount++;
                else
                    eyeAnimatorCount++;
                gazeOrder.Add(target);
            }
            if (lookTargetCount != 1 || eyeAnimatorCount != 1
                || gazeOrder.Count != 2
                || gazeOrder[0] != lookTarget
                || gazeOrder[1] != eyeAnimator)
                throw new InvalidOperationException(
                    "OnDeathStart must contain exactly two gaze-disable "
                    + "listeners: LookTargetController first and "
                    + "EyeAndHeadAnimator second.");
        }

        private static void AppendGazeFingerprint(
            StringBuilder text,
            GameObject outputRoot,
            GazeShell shell,
            Component powerLegs)
        {
            text.Append("gaze=")
                .Append(RelativePath(
                    outputRoot.transform, shell.EyeAnimator.transform))
                .Append(':')
                .Append(RelativePath(
                    outputRoot.transform, shell.LookTarget.transform))
                .Append('|')
                .Append("gaze-eyes=")
                .Append(RelativePath(
                    outputRoot.transform, shell.PhysicalLeftEye))
                .Append(',')
                .Append(RelativePath(
                    outputRoot.transform, shell.PhysicalRightEye))
                .Append('|')
                .Append("gaze-controller=")
                .Append(shell.Controller.Receipt)
                .Append('|')
                .Append("gaze-spawn-init=")
                .Append(RelativePath(
                    outputRoot.transform, shell.SpawnInitializer.transform))
                .Append('@')
                .Append(GazeSpawnInitDelaySeconds.ToString(
                    "0.0", CultureInfo.InvariantCulture))
                .Append('>')
                .Append(string.Join(",", GazeSpawnInitMethods))
                .Append('|')
                .Append("gaze-death=")
                .Append(RelativePath(outputRoot.transform, powerLegs.transform))
                .Append(">look,eye|");
        }

        private sealed class GazeShell
        {
            public Component EyeAnimator { get; }
            public Component LookTarget { get; }
            public Transform SourceLeftEye { get; }
            public Transform SourceRightEye { get; }
            public Transform PhysicalLeftEye { get; }
            public Transform PhysicalRightEye { get; }
            public Component SpawnInitializer { get; }
            public GazeControllerInitContract Controller { get; }

            public GazeShell(
                Component eyeAnimator,
                Component lookTarget,
                Transform sourceLeftEye,
                Transform sourceRightEye,
                Transform physicalLeftEye,
                Transform physicalRightEye,
                Component spawnInitializer,
                GazeControllerInitContract controller)
            {
                EyeAnimator = eyeAnimator;
                LookTarget = lookTarget;
                SourceLeftEye = sourceLeftEye;
                SourceRightEye = sourceRightEye;
                PhysicalLeftEye = physicalLeftEye;
                PhysicalRightEye = physicalRightEye;
                SpawnInitializer = spawnInitializer;
                Controller = controller;
            }
        }

        private sealed class GazeControllerInitContract
        {
            public string Receipt { get; }
            public string DisplayName { get; }

            public GazeControllerInitContract(
                RuntimeAnimatorController controller,
                int layerIndex,
                string layerName,
                string stateName,
                AnimationClip clip)
            {
                DisplayName = layerName + "/" + stateName;
                Receipt = StableAssetId(controller) + ':'
                          + layerIndex + ':' + layerName.Length + ':' + layerName
                          + ':' + stateName.Length + ':' + stateName
                          + ':' + StableAssetId(clip)
                          + ':' + GazeInitEventTime.ToString(
                              "0.000", CultureInfo.InvariantCulture);
            }
        }

        private sealed class GazeTypes
        {
            public Type EyeAndHeadAnimator { get; }
            public Type LookTargetController { get; }
            public Type GenericSpawnDelayEvent { get; }

            private GazeTypes(
                Type eyeAndHeadAnimator,
                Type lookTargetController,
                Type genericSpawnDelayEvent)
            {
                EyeAndHeadAnimator = eyeAndHeadAnimator;
                LookTargetController = lookTargetController;
                GenericSpawnDelayEvent = genericSpawnDelayEvent;
            }

            public static GazeTypes Resolve()
            {
                return new GazeTypes(
                    ResolveGazeType(
                        "RealisticEyeMovements.EyeAndHeadAnimator"),
                    ResolveGazeType(
                        "RealisticEyeMovements.LookTargetController"),
                    ResolveGazeType(GazeSpawnDelayTypeName));
            }

            private static Type ResolveGazeType(string fullName)
            {
                Type type = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(value => string.Equals(
                        value.GetName().Name,
                        GameAssembly,
                        StringComparison.Ordinal))
                    .Select(value => value.GetType(fullName, false))
                    .FirstOrDefault(value => value != null);
                if (type == null || !typeof(Component).IsAssignableFrom(type))
                    throw new TypeLoadException(
                        fullName + " is unavailable from " + GameAssembly + ".");
                return type;
            }
        }
    }
}
