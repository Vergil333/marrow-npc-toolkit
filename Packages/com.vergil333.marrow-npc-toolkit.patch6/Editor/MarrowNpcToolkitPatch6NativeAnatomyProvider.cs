using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Build;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Project-local Patch 6 implementation of the toolkit's first native
    /// generation milestone. It consumes the already validated Unity physics
    /// preview and adds only the native 16-body anatomy shell. No extracted
    /// prefab, game asset, or patch-specific declaration is copied into the
    /// public toolkit package.
    /// </summary>
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        private const float NativeLinearDrag = 0.15f;
        private const float NativeAngularDrag = 0.15f;
        private const float NativeMaxAngularVelocity = 20f;
        private const int NativeSolverIterations = 20;
        private const int NativeVelocityIterations = 20;
        private const float NativeBlendTime = 0.1f;
        private const int NativePuppetSolverIterations = 6;

        // The accepted Patch 6 contract keeps the entity registry in canonical
        // hierarchy/display order. It is intentionally separate from the
        // PuppetMaster/PowerLegs legs-first muscle order below.
        private static readonly HumanBodyBones[] NativeEntityOrder =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
        };

        public NpcNativeBuildProviderResult ConfigureStagedPrefab(
            NpcNativeBuildContext context)
        {
            try
            {
                ValidateContext(context);
                CoreTypes types = CoreTypes.Resolve();
                Animator animator = FindHumanoidAnimator(
                    context.AnimationRoot, context.Definition);
                if (context.Definition.IncludePhysicalJaw)
                    ConfigureJawMuscleTarget(
                        context.Definition,
                        context.AnimationRoot,
                        animator);
                Dictionary<HumanBodyBones, NativeRole> roles = ResolveRoles(
                    context.Definition,
                    context.AnimationRoot,
                    context.PhysicsRoot,
                    animator);
                IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);

                EnsureNoNativeComponents(context.OutputRoot, types);

                // Knee hinges are a provider/runtime concern, not collider
                // authoring. Humanoid retargeting can place a character's
                // animated knee bend plane on the opposite side of the generic
                // Anatomy-profile hinge basis. Canonicalize the two generated
                // hinges from the configured locomotion clips before MarrowBody
                // and MarrowJoint capture their pool defaults.
                bool behaviourRequested = RequiresBehaviourShell(
                    context.RequiredCapabilities);
                if (context.Definition.IncludeSecondaryMotion
                    && !behaviourRequested)
                    throw new InvalidOperationException(
                        "Secondary Motion requires the complete AI/pooling "
                        + "behavior shell so its renderer bridges survive pooling.");
                if (behaviourRequested)
                {
                    BehaviourTypes behaviourTypes = BehaviourTypes.Resolve();
                    MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings =
                        RequireBehaviourSettings(
                            behaviourTypes,
                            roles.ContainsKey(HumanBodyBones.Jaw));
                    ValidateBehaviourController(settings.AnimatorController);
                    AlignGeneratedKneeHingesToLocomotion(
                        context.Definition,
                        animator,
                        roles,
                        settings.AnimatorController);
                }

                Component entity = AddNative(
                    context.OutputRoot, types.MarrowEntity, "MarrowEntity");
                var marrowBodies = new Dictionary<HumanBodyBones, Component>();
                var marrowJoints = new Dictionary<HumanBodyBones, Component>();

                Physics.SyncTransforms();
                foreach (HumanBodyBones role in entityOrder)
                {
                    NativeRole nativeRole = roles[role];
                    ConfigureLivePhysics(nativeRole);
                    Component marrowBody = AddNative(
                        nativeRole.Body.gameObject, types.MarrowBody, "MarrowBody");
                    ConfigureMarrowBody(
                        marrowBody, entity, nativeRole, context.OutputRoot.transform);
                    marrowBodies.Add(role, marrowBody);
                }

                foreach (HumanBodyBones role in entityOrder)
                {
                    NativeRole nativeRole = roles[role];
                    Component marrowJoint = AddNative(
                        nativeRole.Body.gameObject, types.MarrowJoint, "MarrowJoint");
                    ConfigureMarrowJoint(
                        marrowJoint,
                        entity,
                        nativeRole,
                        marrowBodies[role],
                        nativeRole.HasParent
                            ? marrowBodies[nativeRole.ParentRole]
                            : null);
                    marrowJoints.Add(role, marrowJoint);
                }

                Component puppet = AddNative(
                    context.PhysicsRoot.gameObject,
                    types.PuppetMaster,
                    "PuppetMaster");
                ConfigurePuppetMaster(
                    puppet,
                    entity,
                    context,
                    roles,
                    marrowBodies,
                    marrowJoints);
                ConfigureMarrowEntity(
                    entity,
                    context,
                    roles,
                    marrowBodies,
                    marrowJoints,
                    puppet);

                ValidateNativeShell(
                    context.OutputRoot,
                    context.AnimationRoot,
                    types,
                    roles,
                    entity,
                    marrowBodies,
                    marrowJoints,
                    puppet);

                bool gripsRequested = RequiresGripShell(
                    context.RequiredCapabilities);
                GripShell gripShell = gripsRequested
                    ? ConfigureGripShell(context.OutputRoot, roles)
                    : null;
                bool jawRequested = RequiresJawShell(
                    context.RequiredCapabilities);
                NativeJawShell jawShell = jawRequested
                    ? ConfigureJawShell(context.OutputRoot, roles)
                    : null;

                bool gazeRequested = RequiresGazeShell(
                    context.RequiredCapabilities);
                bool audioRequested = RequiresAudioShell(
                    context.RequiredCapabilities);
                string behaviourFingerprint = string.Empty;
                if (behaviourRequested)
                {
                    NativeBehaviourShell behaviourShell = ConfigureBehaviourShell(
                        context,
                        animator,
                        roles,
                        entity,
                        marrowBodies,
                        puppet);
                    RendererBridgeShell rendererBridge = ConfigureRendererBridgeShell(
                        context.OutputRoot,
                        context.AnimationRoot,
                        context.PhysicsRoot,
                        roles,
                        context.Definition);
                    GazeShell gazeShell = gazeRequested
                        ? ConfigureGazeShell(
                            context.OutputRoot,
                            context.AnimationRoot,
                            context.Definition,
                            animator,
                            roles,
                            behaviourShell,
                            rendererBridge)
                        : null;
                    NativeAudioShell audioShell = audioRequested
                        ? ConfigureAudioShell(
                            context.Definition,
                            behaviourShell)
                        : null;
                    ValidateNoExternalSceneReferences(context.OutputRoot);
                    string semanticFingerprint =
                        MarrowNpcToolkitPatch6NativeBehaviourValidation
                            .ValidateAndReceipt(
                                context.OutputRoot,
                                context.Definition,
                                context.AnimationRoot,
                                context.PhysicsRoot);
                    behaviourFingerprint = CreateBehaviourFingerprint(
                        context.OutputRoot,
                        context.AnimationRoot,
                        context.PhysicsRoot,
                        roles,
                        behaviourShell,
                        rendererBridge,
                        gazeShell,
                        audioShell,
                        semanticFingerprint);
                }

                string coreFingerprint = CreateStructuralFingerprint(
                    context.InputFingerprint,
                    context.OutputRoot,
                    roles,
                    entity,
                    marrowBodies,
                    marrowJoints,
                    puppet);
                string gripFingerprint = gripsRequested
                    ? CreateGripFingerprint(context.OutputRoot, roles, gripShell)
                    : string.Empty;
                string jawFingerprint = jawRequested
                    ? CreateJawFingerprint(context.OutputRoot, roles, jawShell)
                    : string.Empty;
                string fingerprint = "patch6-provider-v6|core="
                                     + coreFingerprint + "|"
                                     + (behaviourRequested
                                         ? behaviourFingerprint
                                         : string.Empty)
                                     + (gripsRequested
                                         ? "|grips=" + gripFingerprint
                                         : string.Empty)
                                     + (jawRequested
                                         ? "|jaw=" + jawFingerprint
                                         : string.Empty);
                var messages = new List<NpcNativeBuildMessage>
                {
                    new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_CORE_ANATOMY_GENERATED",
                        "Generated the deterministic 16-body Patch 6 native "
                        + "anatomy shell with Marrow entity/body/joint caches "
                        + "and PuppetMaster muscles."),
                };
                if (jawRequested)
                {
                    messages[0] = new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_CORE_ANATOMY_GENERATED",
                        "Generated the deterministic 17-body Patch 6 native "
                        + "anatomy shell with the accepted Physical Jaw hinge, "
                        + "Marrow registries, and PuppetMaster muscle.");
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_PHYSICAL_JAW_GENERATED",
                        "Generated and validated the Head-connected Physical Jaw, "
                        + "centered jaw grip, 17-entry pose, and native registries."));
                }
                if (behaviourRequested)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_BEHAVIOUR_SHELL_GENERATED",
                        "Generated and semantically validated the Patch 6 AI, "
                        + "PowerLegs, locomotion, pooling, interaction, melee-"
                        + "damage, sensor, and renderer-rebind baseline."));
                else
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Warning,
                        "PATCH6_CORE_ONLY",
                        "This request generated only native anatomy. Request AI "
                        + "and Pooling together to build the behavior shell."));
                if (gripsRequested)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_BODY_GRIPS_GENERATED",
                        "Generated and validated 8 generic body grabs and 8 "
                        + "cylinder limb grabs from the staged anatomy."));
                if (gazeRequested)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_GAZE_GENERATED",
                        "Generated the Patch 6 physical-eye gaze pair, verified "
                        + "the explicit controller initializer, and installed "
                        + "its two death-disable listeners."));
                if (audioRequested)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_AUDIO_PROFILE_GENERATED",
                        "Bound the selected persistent NPC Audio Profile to "
                        + "PowerLegs, ImpactSrc, and FootstepSFX without copying "
                        + "or creating audio assets."));
                if (context.Definition.IncludeSecondaryMotion)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_SECONDARY_MOTION_GENERATED",
                        "Generated and validated two spring-driven secondary-"
                        + "motion bodies from the Avatar's Breast Soft Body bones, "
                        + "outside the canonical Marrow/PuppetMaster contract."));
                return NpcNativeBuildProviderResult.Succeeded(
                    fingerprint,
                    messages);
            }
            catch (Exception exception)
            {
                return NpcNativeBuildProviderResult.Failed(
                    "PATCH6_NATIVE_ANATOMY_FAILED",
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        public NpcNativeBuildProviderResult ValidateSavedPrefab(
            NpcNativeBuildValidationContext context)
        {
            try
            {
                ValidateContext(context);
                CoreTypes types = CoreTypes.Resolve();
                Animator animator = FindHumanoidAnimator(
                    context.AnimationRoot, context.Definition);
                if (context.Definition.IncludePhysicalJaw)
                    ValidateJawMuscleTarget(
                        context.Definition,
                        context.AnimationRoot,
                        animator);
                Dictionary<HumanBodyBones, NativeRole> roles = ResolveRoles(
                    context.Definition,
                    context.AnimationRoot,
                    context.PhysicsRoot,
                    animator);
                IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);

                Component entity = RequireOnlyComponent(
                    context.OutputRoot, types.MarrowEntity, "MarrowEntity");
                Component puppet = RequireOnlyComponent(
                    context.PhysicsRoot.gameObject,
                    types.PuppetMaster,
                    "PuppetMaster");
                var marrowBodies = new Dictionary<HumanBodyBones, Component>();
                var marrowJoints = new Dictionary<HumanBodyBones, Component>();
                foreach (HumanBodyBones role in entityOrder)
                {
                    NativeRole nativeRole = roles[role];
                    marrowBodies.Add(
                        role,
                        RequireOnlyComponent(
                            nativeRole.Body.gameObject,
                            types.MarrowBody,
                            role + " MarrowBody"));
                    marrowJoints.Add(
                        role,
                        RequireOnlyComponent(
                            nativeRole.Body.gameObject,
                            types.MarrowJoint,
                            role + " MarrowJoint"));
                }

                ValidateNativeShell(
                    context.OutputRoot,
                    context.AnimationRoot,
                    types,
                    roles,
                    entity,
                    marrowBodies,
                    marrowJoints,
                    puppet);
                bool gripsRequested = RequiresGripShell(
                    context.RequiredCapabilities);
                GripShell gripShell = gripsRequested
                    ? ResolveGripShell(context.OutputRoot, roles)
                    : null;
                bool jawRequested = RequiresJawShell(
                    context.RequiredCapabilities);
                NativeJawShell jawShell = jawRequested
                    ? ResolveJawShell(context.OutputRoot, roles)
                    : null;
                bool behaviourRequested = RequiresBehaviourShell(
                    context.RequiredCapabilities);
                if (context.Definition.IncludeSecondaryMotion
                    && !behaviourRequested)
                    throw new InvalidOperationException(
                        "Secondary Motion requires the complete AI/pooling "
                        + "behavior shell so its renderer bridges survive pooling.");
                bool gazeRequested = RequiresGazeShell(
                    context.RequiredCapabilities);
                bool audioRequested = RequiresAudioShell(
                    context.RequiredCapabilities);
                string behaviourFingerprint = string.Empty;
                if (behaviourRequested)
                {
                    NativeBehaviourShell behaviourShell = ResolveBehaviourShell(
                        context.OutputRoot,
                        context.AnimationRoot,
                        context.PhysicsRoot,
                        context.Definition,
                        animator,
                        roles,
                        entity,
                        marrowBodies,
                        puppet);
                    RendererBridgeShell rendererBridge = ResolveRendererBridgeShell(
                        context.OutputRoot,
                        context.AnimationRoot,
                        context.PhysicsRoot,
                        roles,
                        context.Definition);
                    GazeShell gazeShell = gazeRequested
                        ? ResolveGazeShell(
                            context.OutputRoot,
                            context.AnimationRoot,
                            context.Definition,
                            animator,
                            roles,
                            behaviourShell,
                            rendererBridge)
                        : null;
                    NativeAudioShell audioShell = audioRequested
                        ? ResolveAudioShell(
                            context.Definition,
                            behaviourShell)
                        : null;
                    ValidateNoExternalSceneReferences(context.OutputRoot);
                    string semanticFingerprint =
                        MarrowNpcToolkitPatch6NativeBehaviourValidation
                            .ValidateAndReceipt(
                                context.OutputRoot,
                                context.Definition,
                                context.AnimationRoot,
                                context.PhysicsRoot);
                    behaviourFingerprint = CreateBehaviourFingerprint(
                        context.OutputRoot,
                        context.AnimationRoot,
                        context.PhysicsRoot,
                        roles,
                        behaviourShell,
                        rendererBridge,
                        gazeShell,
                        audioShell,
                        semanticFingerprint);
                }
                string coreFingerprint = CreateStructuralFingerprint(
                    context.InputFingerprint,
                    context.OutputRoot,
                    roles,
                    entity,
                    marrowBodies,
                    marrowJoints,
                    puppet);
                string gripFingerprint = gripsRequested
                    ? CreateGripFingerprint(context.OutputRoot, roles, gripShell)
                    : string.Empty;
                string jawFingerprint = jawRequested
                    ? CreateJawFingerprint(context.OutputRoot, roles, jawShell)
                    : string.Empty;
                string fingerprint = "patch6-provider-v6|core="
                                     + coreFingerprint + "|"
                                     + (behaviourRequested
                                         ? behaviourFingerprint
                                         : string.Empty)
                                     + (gripsRequested
                                         ? "|grips=" + gripFingerprint
                                         : string.Empty)
                                     + (jawRequested
                                         ? "|jaw=" + jawFingerprint
                                         : string.Empty);
                var messages = new List<NpcNativeBuildMessage>
                {
                    new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        behaviourRequested
                            ? "PATCH6_BEHAVIOUR_SHELL_RELOADED"
                            : "PATCH6_CORE_ANATOMY_RELOADED",
                        behaviourRequested
                            ? "The saved prefab reloaded with the complete "
                              + "Patch 6 behavior shell and the same semantic receipt."
                            : "The saved prefab reloaded with the complete Patch 6 "
                              + "native anatomy shell and the same semantic receipt."),
                };
                if (gripsRequested)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_BODY_GRIPS_RELOADED",
                        "The saved prefab retained the exact 8 generic and 8 "
                        + "cylinder body-grab contract."));
                if (jawRequested)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_PHYSICAL_JAW_RELOADED",
                        "The saved prefab retained the exact 17th body, jaw hinge, "
                        + "grip, pose, renderer mapping, and native registries."));
                if (gazeRequested)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_GAZE_RELOADED",
                        "The saved prefab retained the same physical-eye, "
                        + "controller-init, and death-listener gaze receipt."));
                if (audioRequested)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_AUDIO_PROFILE_RELOADED",
                        "The saved prefab retained every ordered Audio Profile "
                        + "reference and the same PowerLegs/footstep settings."));
                if (context.Definition.IncludeSecondaryMotion)
                    messages.Add(new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Info,
                        "PATCH6_SECONDARY_MOTION_RELOADED",
                        "The saved prefab retained both spring-driven secondary-"
                        + "motion bodies without changing the canonical Marrow "
                        + "or PuppetMaster registries."));
                return NpcNativeBuildProviderResult.Succeeded(
                    fingerprint,
                    messages);
            }
            catch (Exception exception)
            {
                return NpcNativeBuildProviderResult.Failed(
                    "PATCH6_SAVED_ANATOMY_INVALID",
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static string CreateBehaviourFingerprint(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            NativeBehaviourShell behaviourShell,
            RendererBridgeShell rendererBridge,
            GazeShell gazeShell,
            NativeAudioShell audioShell,
            string semanticFingerprint)
        {
            if (string.IsNullOrWhiteSpace(semanticFingerprint))
                throw new InvalidOperationException(
                    "Native behaviour validation returned an empty semantic receipt.");

            var receipt = new StringBuilder();
            AppendBehaviourFingerprint(receipt, outputRoot, roles, behaviourShell);
            AppendRendererBridgeFingerprint(
                receipt,
                outputRoot,
                animationRoot,
                physicsRoot,
                rendererBridge);
            if (rendererBridge.SecondaryMotion != null)
                AppendSecondaryMotionFingerprint(
                    receipt,
                    outputRoot,
                    animationRoot,
                    rendererBridge.SecondaryMotion);
            if (gazeShell != null)
                AppendGazeFingerprint(
                    receipt,
                    outputRoot,
                    gazeShell,
                    behaviourShell.PowerLegs);
            if (audioShell != null)
                AppendAudioFingerprint(receipt, audioShell);
            receipt.Append("semantic=").Append(semanticFingerprint).Append('|');
            // Keep a tokenized canonical receipt for precise post-save
            // diagnostics. NpcNativeBuildCoordinator hashes it into the final
            // compact OutputFingerprint after both passes agree.
            return receipt.ToString();
        }

        private static void ValidateContext(NpcNativeBuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            ValidateCommonContext(
                context.Definition,
                context.OutputRoot,
                context.AnimationRoot,
                context.PhysicsRoot,
                context.RequiredCapabilities);
        }

        private static void ValidateContext(NpcNativeBuildValidationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            ValidateCommonContext(
                context.Definition,
                context.OutputRoot,
                context.AnimationRoot,
                context.PhysicsRoot,
                context.RequiredCapabilities);
            if (string.IsNullOrWhiteSpace(context.OutputAssetPath)
                || !string.Equals(
                    AssetDatabase.GetAssetPath(context.OutputRoot),
                    context.OutputAssetPath,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The validation root is not the saved prefab requested by the coordinator.");
        }

        private static void ValidateCommonContext(
            NpcDefinition definition,
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            NpcCompatibilityCapabilities requiredCapabilities)
        {
            if (definition == null || definition.AnatomyProfile == null)
                throw new InvalidOperationException(
                    "The build context has no NPC Definition or Anatomy Profile.");
            if (outputRoot == null || animationRoot == null || physicsRoot == null)
                throw new InvalidOperationException(
                    "The staged prefab must have direct AnimationRoot and Physics siblings.");
            if (animationRoot.parent != outputRoot.transform
                || physicsRoot.parent != outputRoot.transform)
                throw new InvalidOperationException(
                    "AnimationRoot and Physics must be direct output-root siblings.");
            if ((requiredCapabilities & NpcCompatibilityCapabilities.CoreAnatomy) == 0)
                throw new InvalidOperationException(
                    "The native anatomy provider requires CoreAnatomy in the build request.");
            NpcCompatibilityCapabilities supported =
                NpcCompatibilityCapabilities.CoreAnatomy
                | NpcCompatibilityCapabilities.AI
                | NpcCompatibilityCapabilities.Pooling
                | NpcCompatibilityCapabilities.Grips
                | NpcCompatibilityCapabilities.Gaze
                | NpcCompatibilityCapabilities.Jaw
                | NpcCompatibilityCapabilities.Audio
                | NpcCompatibilityCapabilities.SecondaryMotion;
            NpcCompatibilityCapabilities unsupported = requiredCapabilities
                & ~supported;
            if (unsupported != NpcCompatibilityCapabilities.None)
                throw new InvalidOperationException(
                    "This milestone cannot generate requested capabilities: " + unsupported);
            NpcCompatibilityCapabilities behaviour = requiredCapabilities
                & (NpcCompatibilityCapabilities.AI
                   | NpcCompatibilityCapabilities.Pooling);
            if (behaviour != NpcCompatibilityCapabilities.None
                && behaviour != (NpcCompatibilityCapabilities.AI
                                  | NpcCompatibilityCapabilities.Pooling))
                throw new InvalidOperationException(
                    "Patch 6 AI and Pooling are one coupled behavior shell and "
                    + "must be requested together.");
            if (RequiresGripShell(requiredCapabilities)
                && behaviour != (NpcCompatibilityCapabilities.AI
                                  | NpcCompatibilityCapabilities.Pooling))
                throw new InvalidOperationException(
                    "Patch 6 body grabs require the coupled AI/pooling interaction "
                    + "shell in this provider.");
            if (RequiresGazeShell(requiredCapabilities)
                && behaviour != (NpcCompatibilityCapabilities.AI
                                  | NpcCompatibilityCapabilities.Pooling))
                throw new InvalidOperationException(
                    "Patch 6 gaze requires the coupled AI/pooling behavior shell "
                    + "in this provider.");
            if (RequiresAudioShell(requiredCapabilities)
                && behaviour != (NpcCompatibilityCapabilities.AI
                                  | NpcCompatibilityCapabilities.Pooling))
                throw new InvalidOperationException(
                    "Patch 6 audio requires the coupled AI/pooling behavior shell "
                    + "in this provider.");
            if (RequiresJawShell(requiredCapabilities)
                && behaviour != (NpcCompatibilityCapabilities.AI
                                  | NpcCompatibilityCapabilities.Pooling))
                throw new InvalidOperationException(
                    "Patch 6 Physical Jaw requires the coupled AI/pooling "
                    + "behavior and interaction shell in this provider.");
            if (RequiresSecondaryMotionShell(requiredCapabilities)
                && behaviour != (NpcCompatibilityCapabilities.AI
                                  | NpcCompatibilityCapabilities.Pooling))
                throw new InvalidOperationException(
                    "Patch 6 Secondary Motion requires the coupled AI/pooling "
                    + "behavior and renderer-rebind shell in this provider.");
            if (RequiresJawShell(requiredCapabilities)
                && !definition.IncludePhysicalJaw)
                throw new InvalidOperationException(
                    "The Jaw capability was requested while Physical Jaw is "
                    + "disabled on the NPC Definition.");
            if (!RequiresJawShell(requiredCapabilities)
                && definition.IncludePhysicalJaw)
                throw new InvalidOperationException(
                    "Physical Jaw is enabled on the NPC Definition but the build "
                    + "request did not include the Jaw capability.");
            if (RequiresSecondaryMotionShell(requiredCapabilities)
                && !definition.IncludeSecondaryMotion)
                throw new InvalidOperationException(
                    "Secondary Motion was requested while it is disabled on the "
                    + "NPC Definition.");
            if (!RequiresSecondaryMotionShell(requiredCapabilities)
                && definition.IncludeSecondaryMotion)
                throw new InvalidOperationException(
                    "Secondary Motion is enabled on the NPC Definition but the "
                    + "build request did not include its capability.");
            if (RequiresAudioShell(requiredCapabilities)
                && definition.AudioMode != NpcAudioMode.Profile)
                throw new InvalidOperationException(
                    "The Audio capability was requested while NPC Audio is not "
                    + "set to Profile mode.");
            if (!RequiresAudioShell(requiredCapabilities)
                && definition.AudioMode == NpcAudioMode.Profile)
                throw new InvalidOperationException(
                    "NPC Audio is set to Profile mode but the build request did "
                    + "not include the Audio capability.");
        }

        private static bool RequiresBehaviourShell(
            NpcCompatibilityCapabilities capabilities)
        {
            return (capabilities
                    & (NpcCompatibilityCapabilities.AI
                       | NpcCompatibilityCapabilities.Pooling
                       | NpcCompatibilityCapabilities.Gaze
                       | NpcCompatibilityCapabilities.Jaw
                       | NpcCompatibilityCapabilities.Audio
                       | NpcCompatibilityCapabilities.SecondaryMotion))
                   != NpcCompatibilityCapabilities.None;
        }

        private static bool RequiresGazeShell(
            NpcCompatibilityCapabilities capabilities)
        {
            return (capabilities & NpcCompatibilityCapabilities.Gaze) != 0;
        }

        private static bool RequiresSecondaryMotionShell(
            NpcCompatibilityCapabilities capabilities)
        {
            return (capabilities
                    & NpcCompatibilityCapabilities.SecondaryMotion) != 0;
        }

        private static Animator FindHumanoidAnimator(
            Transform animationRoot,
            NpcDefinition definition)
        {
            if (definition?.AvatarSourceProfile == null
                || animationRoot == null || animationRoot.childCount != 1)
                throw new InvalidOperationException(
                    "AnimationRoot must contain the one routed Avatar instance.");
            Transform avatarRoot = animationRoot.GetChild(0);
            string path = definition.AvatarSourceProfile.AnimatorPath;
            Transform animatorTransform = string.IsNullOrWhiteSpace(path)
                ? avatarRoot
                : avatarRoot.Find(path);
            Animator animator = animatorTransform == null
                ? null
                : animatorTransform.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                throw new InvalidOperationException(
                    "The accepted AvatarSourceProfile.AnimatorPath did not resolve "
                    + "to its Humanoid Animator below AnimationRoot.");
            return animator;
        }

        private static Dictionary<HumanBodyBones, NativeRole> ResolveRoles(
            NpcDefinition definition,
            Transform animationRoot,
            Transform physicsRoot,
            Animator animator)
        {
            var result = new Dictionary<HumanBodyBones, NativeRole>();
            Transform avatarRoot = animationRoot.GetChild(0);
            Transform[] physicsTransforms = physicsRoot
                .GetComponentsInChildren<Transform>(true);
            IReadOnlyList<HumanBodyBones> muscleOrder = definition.IncludePhysicalJaw
                ? NativeMuscleOrderWithJaw
                : NpcHumanoidGraph.NativeMuscleOrder;
            foreach (HumanBodyBones role in muscleOrder)
            {
                Transform[] matches = physicsTransforms.Where(value =>
                        value != physicsRoot
                        && string.Equals(value.name, role.ToString(),
                            StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(
                        "Expected one Physics body named " + role + "; found "
                        + matches.Length + ".");

                Transform body = matches[0];
                Rigidbody rigidbody = body.GetComponent<Rigidbody>();
                ConfigurableJoint joint = body.GetComponent<ConfigurableJoint>();
                // The preview contract keeps the authored collision shape on
                // one direct PrimaryCollider child. Saved behaviour prefabs
                // additionally contain one child Entity tracker box per body;
                // resolve the explicit marker so that tracker colliders can
                // never be mistaken for the physical body shape.
                Transform[] primaryHolders = body.Cast<Transform>()
                    .Where(value => string.Equals(
                            value.name, "PrimaryCollider", StringComparison.Ordinal)
                        || role == HumanBodyBones.Jaw && string.Equals(
                            value.name, "JawGripCenter", StringComparison.Ordinal))
                    .ToArray();
                Collider[] primaryColliders = primaryHolders.Length == 1
                    ? primaryHolders[0].GetComponents<Collider>()
                    : Array.Empty<Collider>();
                Transform target = role == HumanBodyBones.Jaw
                    ? ResolveAvatarJaw(
                        definition.AvatarSourceProfile,
                        avatarRoot)
                    : ResolveAvatarBone(
                        definition.AvatarSourceProfile,
                        avatarRoot,
                        role);
                // GetBoneTransform is useful on a live prefab instance, but it
                // may return null for a persistent prefab asset after Unity has
                // saved/reloaded it. The captured source-profile path is the
                // durable contract in both contexts. When Unity does expose the
                // live Humanoid mapping, require it to agree with that path.
                Transform humanoidTarget = animator.GetBoneTransform(role);
                if (humanoidTarget != null && humanoidTarget != target)
                    throw new InvalidOperationException(
                        "The live Humanoid mapping for " + role
                        + " disagrees with its accepted AvatarSourceProfile path.");
                Transform muscleTarget = role == HumanBodyBones.Jaw
                    ? RequireDirectChild(target, JawMuscleTargetName)
                    : target;
                NpcBodyRoleProfile profile = role == HumanBodyBones.Jaw
                    ? definition.AnatomyProfile.OptionalJaw
                    : definition.AnatomyProfile.FindRole(role);
                if (rigidbody == null || joint == null
                    || primaryHolders.Length != 1
                    || primaryColliders.Length != 1
                    || target == null || !target.IsChildOf(animationRoot)
                    || muscleTarget == null
                    || !muscleTarget.IsChildOf(animationRoot)
                    || profile == null || !profile.Enabled)
                    throw new InvalidOperationException(
                        role + " is missing its body, joint, one direct "
                        + "PrimaryCollider/JawGripCenter shape, enabled profile, or "
                        + "AnimationRoot target. Found marker="
                        + primaryHolders.Length + ", markerColliders="
                        + primaryColliders.Length + ", rigidbody="
                        + (rigidbody != null) + ", joint=" + (joint != null)
                        + ", target=" + (target != null) + ", profile="
                        + (profile != null && profile.Enabled) + ".");
                ValidateNativeTuning(profile, role);

                HumanBodyBones parentRole;
                bool hasParent;
                if (role == HumanBodyBones.Jaw)
                {
                    parentRole = HumanBodyBones.Head;
                    hasParent = true;
                }
                else
                    hasParent = NpcHumanoidGraph.TryGetParent(
                        role, out parentRole);
                Transform expectedParent = hasParent
                    ? result[parentRole].Body
                    : physicsRoot;
                if (body.parent != expectedParent)
                    throw new InvalidOperationException(
                        role + " is not a direct child of its canonical physics parent "
                        + expectedParent.name + ".");
                if (hasParent && joint.connectedBody != result[parentRole].Rigidbody)
                    throw new InvalidOperationException(
                        role + " does not connect to its canonical parent Rigidbody.");
                if (!hasParent && joint.connectedBody != null)
                    throw new InvalidOperationException(
                        "The Hips root joint must not have a connected Rigidbody.");

                result.Add(role, new NativeRole(
                    role,
                        body,
                        target,
                        muscleTarget,
                        rigidbody,
                    joint,
                    primaryColliders[0],
                    profile,
                    hasParent,
                    parentRole));
            }

            int expectedCount = definition.IncludePhysicalJaw ? 17 : 16;
            var canonicalBodies = new HashSet<Transform>(
                result.Values.Select(value => value.Body));
            Rigidbody[] allRigidbodies = physicsRoot
                .GetComponentsInChildren<Rigidbody>(true);
            ConfigurableJoint[] allJoints = physicsRoot
                .GetComponentsInChildren<ConfigurableJoint>(true);
            Rigidbody[] additionalRigidbodies = allRigidbodies
                .Where(value => !canonicalBodies.Contains(value.transform))
                .ToArray();
            ConfigurableJoint[] additionalJoints = allJoints
                .Where(value => !canonicalBodies.Contains(value.transform))
                .ToArray();
            bool allowedSecondaryStaging = definition.IncludeSecondaryMotion
                && additionalRigidbodies.Length <= 2
                && additionalJoints.Length <= 2
                && additionalRigidbodies.Select(value => value.transform)
                    .OrderBy(RelativePhysicsPath, StringComparer.Ordinal)
                    .SequenceEqual(
                        additionalJoints.Select(value => value.transform)
                            .OrderBy(RelativePhysicsPath, StringComparer.Ordinal))
                && additionalRigidbodies.All(value =>
                    IsReservedRendererBridge(value.transform, physicsRoot));
            if (result.Count != expectedCount
                || allRigidbodies.Length != expectedCount
                    + additionalRigidbodies.Length
                || allJoints.Length != expectedCount + additionalJoints.Length
                || (additionalRigidbodies.Length != 0
                    || additionalJoints.Length != 0)
                    && !allowedSecondaryStaging)
                throw new InvalidOperationException(
                    "The staged physics graph does not preserve the exact "
                    + expectedCount + " canonical bodies/joints plus at most two "
                    + "reserved Secondary Motion renderer bridges.");
            return result;
        }

        private static string RelativePhysicsPath(Transform value)
        {
            if (value == null)
                return string.Empty;
            Transform root = value;
            while (root.parent != null
                   && !string.Equals(
                       root.name, "Physics", StringComparison.Ordinal))
                root = root.parent;
            return RelativePath(root, value);
        }

        private static bool IsReservedRendererBridge(
            Transform value,
            Transform physicsRoot)
        {
            return value != null && physicsRoot != null
                && value.IsChildOf(physicsRoot)
                && value.name.StartsWith(
                    RendererBridgePrefix, StringComparison.Ordinal);
        }

        private static Transform ResolveAvatarJaw(
            NpcAvatarSourceProfile sourceProfile,
            Transform avatarRoot)
        {
            if (sourceProfile == null || avatarRoot == null
                || string.IsNullOrWhiteSpace(sourceProfile.JawPath))
                throw new InvalidOperationException(
                    "The accepted Avatar source profile has no durable Jaw path.");
            Transform target = avatarRoot.Find(sourceProfile.JawPath);
            if (target == null)
                throw new InvalidOperationException(
                    "The accepted Avatar Jaw path no longer resolves below "
                    + "AnimationRoot: '" + sourceProfile.JawPath + "'.");
            return target;
        }

        private static Transform ResolveAvatarBone(
            NpcAvatarSourceProfile sourceProfile,
            Transform avatarRoot,
            HumanBodyBones role)
        {
            if (sourceProfile == null || avatarRoot == null)
                throw new InvalidOperationException(
                    "The accepted Avatar source profile or routed Avatar root is missing.");
            NpcHumanoidBoneBinding[] matches = sourceProfile.HumanoidBones
                .Where(binding => binding.Role == role)
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one accepted Avatar binding for " + role
                    + "; found " + matches.Length + ".");
            string path = matches[0].TransformPath;
            Transform target = string.IsNullOrWhiteSpace(path)
                ? avatarRoot
                : avatarRoot.Find(path);
            if (target == null)
                throw new InvalidOperationException(
                    "The accepted Avatar binding for " + role
                    + " no longer resolves below AnimationRoot: '" + path + "'.");
            return target;
        }

        private static void ValidateNativeTuning(
            NpcBodyRoleProfile profile,
            HumanBodyBones role)
        {
            if (!IsFinite(profile.MassKilograms) || profile.MassKilograms <= 0f
                || !IsFinite(profile.MuscleSpring) || profile.MuscleSpring <= 0f
                || !IsFinite(profile.MuscleDamper) || profile.MuscleDamper <= 0f
                || !IsFinite(profile.MuscleWeight) || profile.MuscleWeight <= 0f
                || profile.MuscleWeight > 1f
                || !IsFinite(profile.JointDriveMaxForce)
                || profile.JointDriveMaxForce <= 0f)
                throw new InvalidOperationException(
                    role + " has invalid native mass, muscle, or joint-drive tuning.");
        }

        // Unity's GetComponentInParent<T>() can skip inactive prefab ancestors.
        // Walk the serialized hierarchy explicitly so collider ownership is the
        // same in an inactive staging prefab as it is in a loaded scene.
        private static Rigidbody FindOwningRigidbody(Transform child)
        {
            for (Transform current = child; current != null; current = current.parent)
            {
                Rigidbody body = current.GetComponent<Rigidbody>();
                if (body != null)
                    return body;
            }
            return null;
        }

        private static void EnsureNoNativeComponents(GameObject root, CoreTypes types)
        {
            if (root.GetComponentsInChildren(types.MarrowEntity, true).Length != 0
                || root.GetComponentsInChildren(types.MarrowBody, true).Length != 0
                || root.GetComponentsInChildren(types.MarrowJoint, true).Length != 0
                || root.GetComponentsInChildren(types.PuppetMaster, true).Length != 0)
                throw new InvalidOperationException(
                    "The staged Unity preview already contains native anatomy components.");
        }

        private static Component AddNative(GameObject holder, Type type, string label)
        {
            Component component = holder.AddComponent(type);
            if (component == null)
                throw new InvalidOperationException("Could not add " + label + ".");
            return component;
        }

        private static Component RequireOnlyComponent(
            GameObject holder,
            Type type,
            string label)
        {
            Component[] components = holder.GetComponents(type);
            if (components.Length != 1 || components[0] == null)
                throw new InvalidOperationException(
                    "Expected exactly one " + label + " directly on "
                    + holder.name + "; found " + components.Length + ".");
            return components[0];
        }

        private static void ConfigureLivePhysics(NativeRole role)
        {
            if (role.Role == HumanBodyBones.Jaw)
            {
                ConfigureJawLivePhysics(role);
                return;
            }
            Rigidbody body = role.Rigidbody;
            body.mass = role.Profile.MassKilograms;
            body.drag = NativeLinearDrag;
            body.angularDrag = NativeAngularDrag;
            body.useGravity = true;
            body.isKinematic = false;
            body.detectCollisions = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.constraints = RigidbodyConstraints.None;
            body.maxAngularVelocity = NativeMaxAngularVelocity;
            body.solverIterations = NativeSolverIterations;
            body.solverVelocityIterations = NativeVelocityIterations;

            ConfigurableJoint joint = role.Joint;
            // Preserve the proven Patch 6 NPC contract: XYAndZ is the active
            // rotation mode, while the cached Slerp drive still carries the
            // authored force cap used by the native initialization path.
            joint.rotationDriveMode = RotationDriveMode.XYAndZ;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 0f,
                positionDamper = 0f,
                maximumForce = role.Profile.JointDriveMaxForce,
            };
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
        }

        private static void ConfigureMarrowBody(
            Component marrowBody,
            Component entity,
            NativeRole role,
            Transform entityTransform)
        {
            Rigidbody rigidbody = role.Rigidbody;
            Physics.SyncTransforms();
            rigidbody.ResetCenterOfMass();
            rigidbody.ResetInertiaTensor();
            Vector3 centerOfMass = rigidbody.centerOfMass;
            Vector3 inertiaTensor = rigidbody.inertiaTensor;
            // The validated reference cache uses identity here. Copying PhysX's live
            // auto-computed rotation turns it into a persistent override and
            // previously caused asymmetric lower-arm spin after pooling.
            Quaternion inertiaRotation = Quaternion.identity;
            if (!IsFinite(centerOfMass) || !IsFinite(inertiaTensor)
                || inertiaTensor.x <= 0f || inertiaTensor.y <= 0f
                || inertiaTensor.z <= 0f)
                throw new InvalidOperationException(
                    role.Role + " produced an invalid collider-derived mass distribution.");

            var serialized = new SerializedObject(marrowBody);
            SetObject(serialized, "<Entity>k__BackingField", entity);
            SerializedProperty info = Require(serialized, "_defaultRigidbodyInfo");
            SetRelativeFloat(info, "mass", rigidbody.mass);
            SetRelativeFloat(info, "drag", rigidbody.drag);
            SetRelativeFloat(info, "angularDrag", rigidbody.angularDrag);
            SetRelativeBool(info, "useGravity", rigidbody.useGravity);
            SetRelativeBool(info, "isKinematic", rigidbody.isKinematic);
            SetRelativeBool(info, "detectCollisions", rigidbody.detectCollisions);
            SetRelativeBool(info, "interpolate",
                rigidbody.interpolation != RigidbodyInterpolation.None);
            SetRelativeInt(info, "collisionDetection",
                (int)rigidbody.collisionDetectionMode);
            SetRelativeInt(info, "constraints", (int)rigidbody.constraints);
            SetRelativeVector(info, "centerOfMass", centerOfMass);
            SetRelativeVector(info, "inertiaTensor", inertiaTensor);
            SetRelativeQuaternion(info, "inertiaTensorRotation", inertiaRotation);
            SetRelativeVector(info, "initalVelocity", Vector3.zero);
            SetRelativeVector(info, "initialAngularVelocity", Vector3.zero);

            Require(serialized, "_bounds").boundsValue = ColliderBoundsInFrame(
                role.Collider, role.Body);
            ConfigureTrackerSettings(Require(serialized, "trackerSettings"));

            SerializedProperty initial = Require(
                serialized, "<InitInEntityTransform>k__BackingField");
            SetRelativeVector(
                initial,
                "position",
                entityTransform.InverseTransformPoint(role.Body.position));
            SetRelativeQuaternion(
                initial,
                "rotation",
                (Quaternion.Inverse(entityTransform.rotation)
                    * role.Body.rotation).normalized);

            SetObject(serialized, "_rigidbody", rigidbody);
            SetObjectArray(serialized, "_colliders", new UnityEngine.Object[]
            {
                role.Collider,
            });
            SetObjectArray(serialized, "_trackers", Array.Empty<UnityEngine.Object>());
            SetObjectArray(serialized, "_triggers", Array.Empty<UnityEngine.Object>());
            SetObjectArray(serialized, "_bodiesToIgnore", Array.Empty<UnityEngine.Object>());
            SetObjectArray(
                serialized, "_collidersToIgnore", Array.Empty<UnityEngine.Object>());
            SetBool(serialized, "<HasRigidbody>k__BackingField", true);
            SetBool(serialized, "<isCenterOfMassOverride>k__BackingField", false);
            SetVector(serialized, "<CenterOfMass>k__BackingField", Vector3.zero);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureTrackerSettings(SerializedProperty settings)
        {
            SetRelativeInt(settings, "layers", 1);
            SerializedProperty entries = RequireRelative(settings, "settings");
            entries.arraySize = 3;
            for (int index = 0; index < entries.arraySize; index++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                SetRelativeBool(entry, "isActive", index == 0);
                SetRelativeInt(entry, "layer", 1);
                SetRelativeInt(entry, "type", 0);
                SetRelativeVector(entry, "center", Vector3.zero);
                SetRelativeVector(entry, "size", Vector3.one * 0.5f);
                SetRelativeFloat(entry, "radius", 0.5f);
                SetRelativeFloat(entry, "height", 2f);
                SetRelativeInt(entry, "direction", 0);
            }
        }

        private static void ConfigureMarrowJoint(
            Component marrowJoint,
            Component entity,
            NativeRole role,
            Component bodyA,
            Component bodyB)
        {
            ConfigurableJoint joint = role.Joint;
            var serialized = new SerializedObject(marrowJoint);
            SerializedProperty info = Require(serialized, "_defaultConfigJointInfo");
            SetRelativeQuaternion(info, "startRotation", role.Body.localRotation);
            SetRelativeVector(info, "axis", joint.axis);
            SetRelativeVector(info, "secondaryAxis", joint.secondaryAxis);
            SetRelativeVector(info, "anchor", joint.anchor);
            SetRelativeVector(info, "connectedAnchor", joint.connectedAnchor);
            SetRelativeBool(info, "autoConfigureConnectedAnchor",
                joint.autoConfigureConnectedAnchor);
            SetRelativeFloat(info, "breakForce", joint.breakForce);
            SetRelativeFloat(info, "breakTorque", joint.breakTorque);
            SetRelativeBool(info, "enableCollision", joint.enableCollision);
            SetRelativeBool(info, "enablePreprocessing", joint.enablePreprocessing);
            SetRelativeFloat(info, "massScale", joint.massScale);
            SetRelativeFloat(info, "connectedMassScale", joint.connectedMassScale);
            SetRelativeFloat(info, "projectionAngle", joint.projectionAngle);
            SetRelativeFloat(info, "projectionDistance", joint.projectionDistance);
            SetRelativeInt(info, "projectionModeExt", (int)joint.projectionMode);
            SetDrive(info, "slerpDriveExt", joint.slerpDrive);
            SetDrive(info, "angularYZDriveExt", joint.angularYZDrive);
            SetDrive(info, "angularXDriveExt", joint.angularXDrive);
            SetRelativeInt(info, "rotationDriveMode", (int)joint.rotationDriveMode);
            SetRelativeVector(info, "targetAngularVelocity", joint.targetAngularVelocity);
            SetRelativeQuaternion(info, "targetRotation", joint.targetRotation);
            SetDrive(info, "zDriveExt", joint.zDrive);
            SetDrive(info, "yDriveExt", joint.yDrive);
            SetDrive(info, "xDriveExt", joint.xDrive);
            SetRelativeVector(info, "targetVelocity", joint.targetVelocity);
            SetRelativeVector(info, "targetPosition", joint.targetPosition);
            SetLimit(info, "angularZLimitExt", joint.angularZLimit);
            SetLimit(info, "angularYLimitExt", joint.angularYLimit);
            SetLimit(info, "highAngularXLimitExt", joint.highAngularXLimit);
            SetLimit(info, "lowAngularXLimitExt", joint.lowAngularXLimit);
            SetLimit(info, "linearLimitExt", joint.linearLimit);
            SetLimitSpring(
                info, "angularYZLimitSpringExt", joint.angularYZLimitSpring);
            SetLimitSpring(
                info, "angularXLimitSpringExt", joint.angularXLimitSpring);
            SetLimitSpring(info, "linearLimitSpringExt", joint.linearLimitSpring);
            SetRelativeInt(info, "angularZMotion", (int)joint.angularZMotion);
            SetRelativeInt(info, "angularYMotion", (int)joint.angularYMotion);
            SetRelativeInt(info, "angularXMotion", (int)joint.angularXMotion);
            SetRelativeInt(info, "zMotion", (int)joint.zMotion);
            SetRelativeInt(info, "yMotion", (int)joint.yMotion);
            SetRelativeInt(info, "xMotion", (int)joint.xMotion);
            SetRelativeBool(info, "configuredInWorldSpace", joint.configuredInWorldSpace);
            SetRelativeBool(info, "swapBodies", joint.swapBodies);

            SetObject(serialized, "_bodyA", bodyA);
            SetObject(serialized, "_bodyB", bodyB);
            SetObject(serialized, "_configurableJoint", joint);
            SetObject(serialized, "_entity", entity);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePuppetMaster(
            Component puppet,
            Component entity,
            NpcNativeBuildContext context,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowBodies,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowJoints)
        {
            IReadOnlyList<HumanBodyBones> muscleOrder = MuscleOrderFor(roles);
            float globalSpring = muscleOrder
                .Select(role => roles[role].Profile.MuscleSpring)
                .Max();
            float globalDamper = muscleOrder
                .Select(role => roles[role].Profile.MuscleDamper)
                .Max();
            if (globalSpring <= 0f || globalDamper <= 0f)
                throw new InvalidOperationException(
                    "The Anatomy Profile has no valid native muscle spring or damper.");

            var serialized = new SerializedObject(puppet);
            SetObject(serialized, "marrowEntity", entity);
            SetObject(serialized, "_poolee", null);
            SetObject(serialized, "humanoidConfig", null);
            SetObject(serialized, "targetRoot", context.AnimationRoot);
            SetInt(serialized, "state", 0);
            SerializedProperty state = Require(serialized, "stateSettings");
            SetRelativeFloat(state, "killDuration", 0.75f);
            SetRelativeFloat(state, "deadMuscleWeight", 0.01f);
            SetRelativeFloat(state, "deadMuscleDamper", 400f);
            SetRelativeFloat(state, "maxFreezeSqrVelocity", 0.2f);
            SetRelativeInt(state, "enableAngularLimitsOnKill", 1);
            SetRelativeInt(state, "enableInternalCollisionsOnKill", 1);
            SetInt(serialized, "mode", 0);
            SetFloat(serialized, "blendTime", NativeBlendTime);
            SetInt(serialized, "solverIterationCount", NativePuppetSolverIterations);
            SetInt(serialized, "visualizeTargetAnimation", 0);
            SetInt(serialized, "visualizeTargetPose", 0);
            SetFloat(serialized, "mappingWeight", 0f);
            SetFloat(serialized, "muscleWeight", 1f);
            SetFloat(serialized, "muscleSpring", globalSpring);
            SetFloat(serialized, "muscleDamper", globalDamper);
            SetInt(serialized, "updateJointAnchors", 1);
            SetInt(serialized, "angularLimits", 1);
            SetInt(serialized, "internalCollisions", 0);

            SerializedProperty muscles = Require(serialized, "muscles");
            muscles.arraySize = muscleOrder.Count;
            for (int index = 0; index < muscleOrder.Count; index++)
            {
                HumanBodyBones role = muscleOrder[index];
                NativeRole nativeRole = roles[role];
                SerializedProperty muscle = muscles.GetArrayElementAtIndex(index);
                SetRelativeString(
                    muscle,
                    "name",
                    role == HumanBodyBones.Jaw ? "Jaw_M" : role.ToString());
                SetRelativeObject(muscle, "target", nativeRole.MuscleTarget);
                SerializedProperty props = RequireRelative(muscle, "props");
                SetRelativeInt(props, "group", MuscleGroup(role));
                SetRelativeFloat(
                    props,
                    "mappingWeight",
                    role == HumanBodyBones.Jaw ? 0f : 1f);
                SetRelativeFloat(props, "muscleWeight",
                    nativeRole.Profile.MuscleWeight);
                SetRelativeFloat(props, "muscleDamper",
                    nativeRole.Profile.MuscleDamper / globalDamper);
                SetRelativeInt(props, "mapPosition", MapPosition(role) ? 1 : 0);
                SetRelativeIntArray(
                    props,
                    "ignoredMuscleIndexs",
                    IgnoredMuscleIndices(role));

                SetRelativeIntArray(muscle, "parentIndexes", Array.Empty<int>());
                SetRelativeIntArray(muscle, "childIndexes", Array.Empty<int>());
                SetRelativeByteArray(muscle, "childFlags", Array.Empty<byte>());
                SetRelativeIntArray(muscle, "kinshipDegrees", Array.Empty<int>());
                SetRelativeObject(muscle, "broadcaster", null);
                SetRelativeObject(muscle, "jointBreakBroadcaster", null);
                SetRelativeVector(muscle, "positionOffset", Vector3.zero);
                SetRelativeVector(muscle, "mappedVelocity", Vector3.zero);
                SetRelativeVector(muscle, "mappedAngularVelocity", Vector3.zero);
                SetRelativeObject(muscle, "marrowJoint", marrowJoints[role]);
                SetRelativeObject(muscle, "marrowBody", marrowBodies[role]);
            }

            SetInt(serialized, "cullAnimators", 1);
            SetObjectArray(
                serialized, "cullableAnimators", Array.Empty<UnityEngine.Object>());
            SetObjectArray(serialized, "solvers", Array.Empty<UnityEngine.Object>());
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureMarrowEntity(
            Component entity,
            NpcNativeBuildContext context,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowBodies,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowJoints,
            Component puppet)
        {
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            var serialized = new SerializedObject(entity);
            SetObjectArray(
                serialized,
                "_bodies",
                entityOrder.Select(role =>
                    (UnityEngine.Object)marrowBodies[role]).ToArray());
            SetObjectArray(
                serialized,
                "_joints",
                entityOrder.Select(role =>
                    (UnityEngine.Object)marrowJoints[role]).ToArray());
            SetObject(serialized, "_anchorBody", marrowBodies[HumanBodyBones.Hips]);
            SetObject(serialized, "_poolee", null);
            SetObjectArray(serialized, "_behaviours", new UnityEngine.Object[]
            {
                puppet,
            });
            SetVector(serialized, "_originalScale", context.OutputRoot.transform.localScale);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidateNativeShell(
            GameObject outputRoot,
            Transform animationRoot,
            CoreTypes types,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component entity,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowBodies,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowJoints,
            Component puppet)
        {
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            IReadOnlyList<HumanBodyBones> muscleOrder = MuscleOrderFor(roles);
            int expectedCount = entityOrder.Count;
            if (outputRoot.GetComponentsInChildren(types.MarrowEntity, true).Length != 1
                || outputRoot.GetComponentsInChildren(types.MarrowBody, true).Length
                    != expectedCount
                || outputRoot.GetComponentsInChildren(types.MarrowJoint, true).Length
                    != expectedCount
                || outputRoot.GetComponentsInChildren(types.PuppetMaster, true).Length
                    != 1)
                throw new InvalidOperationException(
                    "The native component count is not Entity 1 / Body "
                    + expectedCount + " / Joint " + expectedCount + " / Puppet 1.");

            SerializedProperty entityBodies = Require(new SerializedObject(entity), "_bodies");
            SerializedProperty entityJoints = Require(new SerializedObject(entity), "_joints");
            if (entityBodies.arraySize != expectedCount
                || entityJoints.arraySize != expectedCount)
                throw new InvalidOperationException(
                    "The MarrowEntity registry does not contain exactly "
                    + expectedCount + " bodies and joints.");
            for (int index = 0; index < entityOrder.Count; index++)
            {
                HumanBodyBones role = entityOrder[index];
                if (entityBodies.GetArrayElementAtIndex(index).objectReferenceValue
                        != marrowBodies[role]
                    || entityJoints.GetArrayElementAtIndex(index).objectReferenceValue
                        != marrowJoints[role])
                    throw new InvalidOperationException(
                        "The MarrowEntity registry order differs at " + role + ".");
            }

            var puppetObject = new SerializedObject(puppet);
            if (Require(puppetObject, "targetRoot").objectReferenceValue
                    != animationRoot
                || Require(puppetObject, "marrowEntity").objectReferenceValue != entity
                || Require(puppetObject, "cullAnimators").intValue != 1)
                throw new InvalidOperationException(
                    "PuppetMaster does not reference AnimationRoot, its MarrowEntity, "
                    + "and the accepted animator-culling mode.");
            if (roles.ContainsKey(HumanBodyBones.Jaw)
                && (Math.Abs(Require(
                        puppetObject, "muscleSpring").floatValue
                    - JawMuscleSpring) > JawTolerance
                    || Math.Abs(Require(
                        puppetObject, "muscleDamper").floatValue
                    - JawMuscleDamper) > JawTolerance))
                throw new InvalidOperationException(
                    "Physical Jaw must retain the accepted global PuppetMaster "
                    + "spring/damper contract.");
            SerializedProperty muscles = Require(puppetObject, "muscles");
            if (muscles.arraySize != muscleOrder.Count)
                throw new InvalidOperationException(
                    "PuppetMaster does not contain the exact " + muscleOrder.Count
                    + "-muscle array.");
            var uniqueBodies = new HashSet<UnityEngine.Object>();
            var uniqueJoints = new HashSet<UnityEngine.Object>();
            var uniqueTargets = new HashSet<UnityEngine.Object>();
            for (int index = 0; index < muscles.arraySize; index++)
            {
                HumanBodyBones role = muscleOrder[index];
                SerializedProperty muscle = muscles.GetArrayElementAtIndex(index);
                UnityEngine.Object target = RequireRelative(muscle, "target")
                    .objectReferenceValue;
                UnityEngine.Object body = RequireRelative(muscle, "marrowBody")
                    .objectReferenceValue;
                UnityEngine.Object joint = RequireRelative(muscle, "marrowJoint")
                    .objectReferenceValue;
                if (target != roles[role].MuscleTarget || body != marrowBodies[role]
                    || joint != marrowJoints[role])
                    throw new InvalidOperationException(
                        "PuppetMaster muscle " + index + " does not map " + role + ".");
                SerializedProperty props = RequireRelative(muscle, "props");
                if (role == HumanBodyBones.Jaw
                    && (!string.Equals(
                            RequireRelative(muscle, "name").stringValue,
                            "Jaw_M",
                            StringComparison.Ordinal)
                        || RequireRelative(props, "group").intValue != 2
                        || Math.Abs(RequireRelative(
                            props, "mappingWeight").floatValue) > JawTolerance
                        || Math.Abs(RequireRelative(
                            props, "muscleWeight").floatValue - 1f) > JawTolerance
                        || Math.Abs(RequireRelative(
                            props, "muscleDamper").floatValue - 1f) > JawTolerance
                        || RequireRelative(props, "mapPosition").intValue != 0
                        || RequireRelative(
                            props, "ignoredMuscleIndexs").arraySize != 0
                        || RequireRelative(muscle, "parentIndexes").arraySize != 0
                        || RequireRelative(muscle, "childIndexes").arraySize != 0
                        || RequireRelative(muscle, "childFlags").arraySize != 0
                        || RequireRelative(muscle, "kinshipDegrees").arraySize != 0
                        || RequireRelative(muscle, "broadcaster")
                            .objectReferenceValue != null
                        || RequireRelative(muscle, "jointBreakBroadcaster")
                            .objectReferenceValue != null
                        || Vector3.Distance(RequireRelative(
                            muscle, "positionOffset").vector3Value, Vector3.zero)
                            > JawTolerance
                        || Vector3.Distance(RequireRelative(
                            muscle, "mappedVelocity").vector3Value, Vector3.zero)
                            > JawTolerance
                        || Vector3.Distance(RequireRelative(
                            muscle, "mappedAngularVelocity").vector3Value,
                            Vector3.zero) > JawTolerance))
                    throw new InvalidOperationException(
                        "PuppetMaster Jaw muscle differs from the accepted append-only contract.");
                uniqueTargets.Add(target);
                uniqueBodies.Add(body);
                uniqueJoints.Add(joint);
            }
            if (uniqueTargets.Count != expectedCount
                || uniqueBodies.Count != expectedCount
                || uniqueJoints.Count != expectedCount)
                throw new InvalidOperationException(
                    "PuppetMaster does not own " + expectedCount
                    + " unique target/body/joint mappings.");

            foreach (HumanBodyBones role in muscleOrder)
            {
                SerializedObject bodyObject = new SerializedObject(marrowBodies[role]);
                SerializedObject jointObject = new SerializedObject(marrowJoints[role]);
                SerializedProperty cachedJoint = Require(
                    jointObject, "_defaultConfigJointInfo");
                Vector3 cachedAxis = RequireRelative(
                    cachedJoint, "axis").vector3Value;
                Vector3 cachedSecondaryAxis = RequireRelative(
                    cachedJoint, "secondaryAxis").vector3Value;
                if (Require(bodyObject, "<Entity>k__BackingField").objectReferenceValue
                        != entity
                    || Require(bodyObject, "_rigidbody").objectReferenceValue
                        != roles[role].Rigidbody
                    || Require(bodyObject, "_colliders").arraySize != 1
                    || Require(jointObject, "_bodyA").objectReferenceValue
                        != marrowBodies[role]
                    || Require(jointObject, "_entity").objectReferenceValue != entity
                    || Require(jointObject, "_configurableJoint").objectReferenceValue
                        != roles[role].Joint
                    || roles[role].Joint.rotationDriveMode
                        != (role == HumanBodyBones.Jaw
                            ? RotationDriveMode.Slerp
                            : RotationDriveMode.XYAndZ)
                    || RequireRelative(cachedJoint, "rotationDriveMode").intValue
                        != (int)(role == HumanBodyBones.Jaw
                            ? RotationDriveMode.Slerp
                            : RotationDriveMode.XYAndZ)
                    || Vector3.Distance(cachedAxis, roles[role].Joint.axis)
                        > 0.000001f
                    || Vector3.Distance(
                        cachedSecondaryAxis,
                        roles[role].Joint.secondaryAxis) > 0.000001f)
                    throw new InvalidOperationException(
                        role + " has an incomplete Marrow body/joint reference contract.");
                UnityEngine.Object expectedBodyB = roles[role].HasParent
                    ? marrowBodies[roles[role].ParentRole]
                    : null;
                if (Require(jointObject, "_bodyB").objectReferenceValue != expectedBodyB)
                    throw new InvalidOperationException(
                        role + " has the wrong MarrowJoint parent body.");
                if (role == HumanBodyBones.Jaw)
                    ValidateJawMarrowCache(
                        roles[role], marrowBodies[role], marrowJoints[role]);
            }
        }

        private static string CreateStructuralFingerprint(
            string inputFingerprint,
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component entity,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowBodies,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowJoints,
            Component puppet)
        {
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            IReadOnlyList<HumanBodyBones> muscleOrder = MuscleOrderFor(roles);
            var text = new StringBuilder(8192);
            text.Append(ProviderIdStatic).Append('|')
                .Append(inputFingerprint).Append('|')
                .Append("entity=").Append(RelativePath(
                    outputRoot.transform, entity.transform)).Append('|')
                .Append("puppet=").Append(RelativePath(
                    outputRoot.transform, puppet.transform)).Append('|');

            foreach (HumanBodyBones role in entityOrder)
            {
                NativeRole value = roles[role];
                var bodyObject = new SerializedObject(marrowBodies[role]);
                SerializedProperty rigidbodyInfo = Require(
                    bodyObject, "_defaultRigidbodyInfo");
                Vector3 cachedCenter = RequireRelative(
                    rigidbodyInfo, "centerOfMass").vector3Value;
                Vector3 cachedInertia = RequireRelative(
                    rigidbodyInfo, "inertiaTensor").vector3Value;
                Quaternion cachedInertiaRotation = RequireRelative(
                    rigidbodyInfo, "inertiaTensorRotation").quaternionValue;
                text.Append("E:").Append(role).Append(':')
                    .Append(RelativePath(outputRoot.transform, value.Body))
                    .Append(':');
                AppendVector(text, value.Body.localPosition);
                AppendQuaternion(text, value.Body.localRotation);
                AppendVector(text, value.Body.localScale);
                AppendVector(text, value.Joint.axis);
                AppendVector(text, value.Joint.secondaryAxis);
                text.Append(F(RequireRelative(
                        rigidbodyInfo, "mass").floatValue)).Append(':')
                    .Append(F(cachedCenter.x)).Append(':')
                    .Append(F(cachedCenter.y)).Append(':')
                    .Append(F(cachedCenter.z)).Append(':')
                    .Append(F(cachedInertia.x)).Append(':')
                    .Append(F(cachedInertia.y)).Append(':')
                    .Append(F(cachedInertia.z)).Append(':')
                    .Append(F(cachedInertiaRotation.x)).Append(':')
                    .Append(F(cachedInertiaRotation.y)).Append(':')
                    .Append(F(cachedInertiaRotation.z)).Append(':')
                    .Append(F(cachedInertiaRotation.w)).Append(':')
                    .Append((int)value.Joint.xMotion).Append(':')
                    .Append((int)value.Joint.yMotion).Append(':')
                    .Append((int)value.Joint.zMotion).Append(':')
                    .Append((int)value.Joint.angularXMotion).Append(':')
                    .Append((int)value.Joint.angularYMotion).Append(':')
                    .Append((int)value.Joint.angularZMotion).Append(':')
                    .Append((int)value.Joint.rotationDriveMode).Append(':')
                    .Append(F(value.Joint.lowAngularXLimit.limit)).Append(':')
                    .Append(F(value.Joint.highAngularXLimit.limit)).Append(':')
                    .Append(F(value.Joint.angularYLimit.limit)).Append(':')
                    .Append(F(value.Joint.angularZLimit.limit)).Append(':')
                    .Append(F(value.Joint.slerpDrive.positionSpring)).Append(':')
                    .Append(F(value.Joint.slerpDrive.positionDamper)).Append(':')
                    .Append(F(value.Joint.slerpDrive.maximumForce)).Append(':')
                    .Append(RelativePath(outputRoot.transform,
                        marrowBodies[role].transform)).Append(':')
                    .Append(RelativePath(outputRoot.transform,
                        marrowJoints[role].transform)).Append('|');
            }

            var puppetObject = new SerializedObject(puppet);
            text.Append("P:")
                .Append(F(Require(puppetObject, "mappingWeight").floatValue)).Append(':')
                .Append(F(Require(puppetObject, "muscleWeight").floatValue)).Append(':')
                .Append(F(Require(puppetObject, "muscleSpring").floatValue)).Append(':')
                .Append(F(Require(puppetObject, "muscleDamper").floatValue)).Append(':')
                .Append(Require(puppetObject, "cullAnimators").intValue).Append('|');
            SerializedProperty muscles = Require(puppetObject, "muscles");
            for (int index = 0; index < muscles.arraySize; index++)
            {
                HumanBodyBones role = muscleOrder[index];
                SerializedProperty muscle = muscles.GetArrayElementAtIndex(index);
                SerializedProperty props = RequireRelative(muscle, "props");
                text.Append("M:").Append(index).Append(':').Append(role).Append(':')
                    .Append(RequireRelative(muscle, "name").stringValue).Append(':')
                    .Append(RelativePath(
                        outputRoot.transform,
                        roles[role].MuscleTarget))
                    .Append(':').Append(RequireRelative(props, "group").intValue)
                    .Append(':').Append(F(RequireRelative(
                        props, "mappingWeight").floatValue))
                    .Append(':').Append(F(RequireRelative(
                        props, "muscleWeight").floatValue))
                    .Append(':').Append(F(RequireRelative(
                        props, "muscleDamper").floatValue))
                    .Append(':').Append(RequireRelative(props, "mapPosition").intValue)
                    .Append(':').Append(string.Join(",", IgnoredMuscleIndices(role)))
                    .Append('|');
            }
            return Hash128.Compute(text.ToString()).ToString();
        }

        private const string ProviderIdStatic =
            "vergil333.bonelab-patch6";

        private static int MuscleGroup(HumanBodyBones role)
        {
            switch (role)
            {
                case HumanBodyBones.Hips: return 0;
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest: return 1;
                case HumanBodyBones.Head: return 2;
                case HumanBodyBones.Jaw: return 2;
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.RightLowerArm: return 3;
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand: return 4;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.RightLowerLeg: return 5;
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot: return 6;
                default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
            }
        }

        private static bool MapPosition(HumanBodyBones role)
        {
            return role == HumanBodyBones.Head
                   || role == HumanBodyBones.LeftUpperArm
                   || role == HumanBodyBones.RightUpperArm;
        }

        private static int[] IgnoredMuscleIndices(HumanBodyBones role)
        {
            switch (role)
            {
                case HumanBodyBones.LeftUpperLeg: return new[] { 4, 5 };
                case HumanBodyBones.LeftLowerLeg: return new[] { 4, 5, 6 };
                case HumanBodyBones.LeftFoot: return new[] { 5, 6 };
                case HumanBodyBones.Chest: return new[] { 0 };
                default: return Array.Empty<int>();
            }
        }

        private static Bounds ColliderBoundsInFrame(Collider collider, Transform frame)
        {
            Vector3 scale = collider.transform.lossyScale;
            if (Vector3.Distance(scale, Vector3.one) > 0.0001f)
                throw new InvalidOperationException(
                    collider.name + " has non-unit collider scale " + scale + ".");

            Vector3 center;
            Vector3 extents;
            var box = collider as BoxCollider;
            if (box != null)
            {
                center = frame.InverseTransformPoint(
                    box.transform.TransformPoint(box.center));
                Vector3 half = box.size * 0.5f;
                Vector3 x = frame.InverseTransformVector(
                    box.transform.TransformVector(Vector3.right * half.x));
                Vector3 y = frame.InverseTransformVector(
                    box.transform.TransformVector(Vector3.up * half.y));
                Vector3 z = frame.InverseTransformVector(
                    box.transform.TransformVector(Vector3.forward * half.z));
                extents = new Vector3(
                    Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            }
            else if (collider is SphereCollider sphere)
            {
                center = frame.InverseTransformPoint(
                    sphere.transform.TransformPoint(sphere.center));
                extents = Vector3.one * sphere.radius;
            }
            else if (collider is CapsuleCollider capsule)
            {
                center = frame.InverseTransformPoint(
                    capsule.transform.TransformPoint(capsule.center));
                Vector3 axis = capsule.direction == 0
                    ? Vector3.right
                    : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                axis = frame.InverseTransformDirection(
                    capsule.transform.TransformDirection(axis)).normalized;
                float cylinderHalf = Mathf.Max(
                    0f, capsule.height * 0.5f - capsule.radius);
                extents = new Vector3(
                    Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z))
                    * cylinderHalf + Vector3.one * capsule.radius;
            }
            else
                throw new InvalidOperationException(
                    "Unsupported native collider type: "
                    + collider.GetType().FullName + ".");

            var local = new Bounds(center, extents * 2f);
            if (!IsFinite(local.center) || !IsFinite(local.size)
                || local.size.x <= 0f || local.size.y <= 0f
                || local.size.z <= 0f)
                throw new InvalidOperationException(
                    collider.name + " produced invalid MarrowBody bounds.");
            return local;
        }

        private static void SetDrive(
            SerializedProperty owner,
            string name,
            JointDrive drive)
        {
            SerializedProperty value = RequireRelative(owner, name);
            SetRelativeFloat(value, "positionSpring", drive.positionSpring);
            SetRelativeFloat(value, "positionDamper", drive.positionDamper);
            SetRelativeFloat(value, "maximumForce", drive.maximumForce);
        }

        private static void SetLimit(
            SerializedProperty owner,
            string name,
            SoftJointLimit limit)
        {
            SerializedProperty value = RequireRelative(owner, name);
            SetRelativeFloat(value, "limit", limit.limit);
            SetRelativeFloat(value, "bounciness", limit.bounciness);
            SetRelativeFloat(value, "contactDistance", limit.contactDistance);
        }

        private static void SetLimitSpring(
            SerializedProperty owner,
            string name,
            SoftJointLimitSpring spring)
        {
            SerializedProperty value = RequireRelative(owner, name);
            SetRelativeFloat(value, "spring", spring.spring);
            SetRelativeFloat(value, "damper", spring.damper);
        }

        private static SerializedProperty Require(
            SerializedObject owner,
            string path)
        {
            SerializedProperty property = owner.FindProperty(path);
            if (property == null)
                throw new MissingFieldException(
                    owner.targetObject.GetType().FullName, path);
            return property;
        }

        private static SerializedProperty RequireRelative(
            SerializedProperty owner,
            string name)
        {
            SerializedProperty property = owner.FindPropertyRelative(name);
            if (property == null)
                throw new MissingFieldException(owner.propertyPath, name);
            return property;
        }

        private static void SetObject(
            SerializedObject owner,
            string path,
            UnityEngine.Object value)
        {
            Require(owner, path).objectReferenceValue = value;
        }

        private static void SetObjectArray(
            SerializedObject owner,
            string path,
            IReadOnlyList<UnityEngine.Object> values)
        {
            SerializedProperty property = Require(owner, path);
            property.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
        }

        private static void SetRelativeObject(
            SerializedProperty owner,
            string name,
            UnityEngine.Object value)
        {
            RequireRelative(owner, name).objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject owner, string path, float value)
        {
            Require(owner, path).floatValue = value;
        }

        private static void SetInt(SerializedObject owner, string path, int value)
        {
            Require(owner, path).intValue = value;
        }

        private static void SetBool(SerializedObject owner, string path, bool value)
        {
            Require(owner, path).boolValue = value;
        }

        private static void SetVector(
            SerializedObject owner,
            string path,
            Vector3 value)
        {
            Require(owner, path).vector3Value = value;
        }

        private static void SetRelativeString(
            SerializedProperty owner,
            string name,
            string value)
        {
            RequireRelative(owner, name).stringValue = value;
        }

        private static void SetRelativeFloat(
            SerializedProperty owner,
            string name,
            float value)
        {
            RequireRelative(owner, name).floatValue = value;
        }

        private static void SetRelativeInt(
            SerializedProperty owner,
            string name,
            int value)
        {
            RequireRelative(owner, name).intValue = value;
        }

        private static void SetRelativeBool(
            SerializedProperty owner,
            string name,
            bool value)
        {
            RequireRelative(owner, name).boolValue = value;
        }

        private static void SetRelativeVector(
            SerializedProperty owner,
            string name,
            Vector3 value)
        {
            RequireRelative(owner, name).vector3Value = value;
        }

        private static void SetRelativeQuaternion(
            SerializedProperty owner,
            string name,
            Quaternion value)
        {
            RequireRelative(owner, name).quaternionValue = value;
        }

        private static void SetRelativeIntArray(
            SerializedProperty owner,
            string name,
            IReadOnlyList<int> values)
        {
            SerializedProperty property = RequireRelative(owner, name);
            property.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
                property.GetArrayElementAtIndex(index).intValue = values[index];
        }

        private static void SetRelativeByteArray(
            SerializedProperty owner,
            string name,
            IReadOnlyList<byte> values)
        {
            SerializedProperty property = RequireRelative(owner, name);
            property.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
                property.GetArrayElementAtIndex(index).intValue = values[index];
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string RelativePath(Transform root, Transform value)
        {
            if (root == null || value == null)
                return "<null>";
            if (value == root)
                return string.Empty;
            var names = new List<string>();
            Transform cursor = value;
            while (cursor != null && cursor != root)
            {
                names.Add(cursor.name);
                cursor = cursor.parent;
            }
            if (cursor != root)
                return "<outside>";
            names.Reverse();
            return string.Join("/", names);
        }

        private static void AppendVector(StringBuilder text, Vector3 value)
        {
            text.Append(F(value.x)).Append(',').Append(F(value.y)).Append(',')
                .Append(F(value.z)).Append(':');
        }

        private static void AppendQuaternion(StringBuilder text, Quaternion value)
        {
            text.Append(F(value.x)).Append(',').Append(F(value.y)).Append(',')
                .Append(F(value.z)).Append(',').Append(F(value.w)).Append(':');
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private sealed class NativeRole
        {
            public HumanBodyBones Role { get; }
            public Transform Body { get; }
            // Target remains the accepted deform/renderer bone. A Physical
            // Jaw uses a separate corrected child for PuppetMaster so the
            // Humanoid Animator cannot bias the closed mouth pose.
            public Transform Target { get; }
            public Transform MuscleTarget { get; }
            public Rigidbody Rigidbody { get; }
            public ConfigurableJoint Joint { get; }
            public Collider Collider { get; }
            public NpcBodyRoleProfile Profile { get; }
            public bool HasParent { get; }
            public HumanBodyBones ParentRole { get; }

            public NativeRole(
                HumanBodyBones role,
                Transform body,
                Transform target,
                Transform muscleTarget,
                Rigidbody rigidbody,
                ConfigurableJoint joint,
                Collider collider,
                NpcBodyRoleProfile profile,
                bool hasParent,
                HumanBodyBones parentRole)
            {
                Role = role;
                Body = body;
                Target = target;
                MuscleTarget = muscleTarget;
                Rigidbody = rigidbody;
                Joint = joint;
                Collider = collider;
                Profile = profile;
                HasParent = hasParent;
                ParentRole = parentRole;
            }
        }

        private sealed class CoreTypes
        {
            public Type MarrowEntity { get; }
            public Type MarrowBody { get; }
            public Type MarrowJoint { get; }
            public Type PuppetMaster { get; }

            private CoreTypes(
                Type marrowEntity,
                Type marrowBody,
                Type marrowJoint,
                Type puppetMaster)
            {
                MarrowEntity = marrowEntity;
                MarrowBody = marrowBody;
                MarrowJoint = marrowJoint;
                PuppetMaster = puppetMaster;
            }

            public static CoreTypes Resolve()
            {
                return new CoreTypes(
                    ResolveComponentType("SLZ.Marrow.Interaction.MarrowEntity"),
                    ResolveComponentType("SLZ.Marrow.Interaction.MarrowBody"),
                    ResolveComponentType("SLZ.Marrow.Interaction.MarrowJoint"),
                    ResolveComponentType("SLZ.Marrow.PuppetMasta.PuppetMaster"));
            }

            private static Type ResolveComponentType(string fullName)
            {
                Type type = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(value => string.Equals(
                        value.GetName().Name, "SLZ.Marrow", StringComparison.Ordinal))
                    .Select(value => value.GetType(fullName, false))
                    .FirstOrDefault(value => value != null);
                if (type == null || !typeof(Component).IsAssignableFrom(type))
                    throw new TypeLoadException(
                        fullName + " is unavailable from the exact SLZ.Marrow assembly.");
                return type;
            }
        }
    }
}
