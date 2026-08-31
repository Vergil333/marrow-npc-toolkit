using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        private const string RendererBridgePrefix = "__MARROW_NPC_BRIDGE__";
        private const float RendererBridgeMatrixTolerance = 0.00005f;

        /// <summary>
        /// Keeps the imported Avatar hierarchy as the inert bind-time skeleton
        /// while routing every skinned renderer to component-free transforms
        /// below the physically authoritative 16-body hierarchy at runtime.
        /// </summary>
        private static RendererBridgeShell ConfigureRendererBridgeShell(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Vergil333.MarrowNpcToolkit.Authoring.NpcDefinition definition)
        {
            ValidateRendererBridgeArguments(
                outputRoot, animationRoot, physicsRoot, roles);
            Type rebindType = ResolveRendererBridgeComponentType();
            List<SkinnedMeshRenderer> renderers = CollectRendererBridgeRenderers(
                animationRoot);

            Component[] existingRebinds = outputRoot.GetComponentsInChildren(
                rebindType, true);
            if (existingRebinds.Length != 0)
                throw new InvalidOperationException(
                    "The staged Avatar already contains SkinnedBoneRebind "
                    + "components. The renderer bridge pass requires a clean "
                    + "source so each renderer receives exactly one component.");

            List<Transform> sourceBones = CollectRendererBridgeSourceBones(
                animationRoot, renderers);
            var bridges = new Dictionary<Transform, Transform>();
            var ownerRoles = new Dictionary<Transform, HumanBodyBones>();
            var sourceRestMatrices = sourceBones.ToDictionary(
                sourceBone => sourceBone,
                sourceBone => sourceBone.localToWorldMatrix);

            foreach (Transform sourceBone in sourceBones)
            {
                HumanBodyBones ownerRole = ResolveRendererBridgeOwnerRole(
                    sourceBone, animationRoot, roles);
                Transform owner = roles[ownerRole].Body;
                if (IsPhysicalJawRendererSource(sourceBone, roles))
                {
                    bridges.Add(sourceBone, owner);
                    ownerRoles.Add(sourceBone, HumanBodyBones.Jaw);
                    continue;
                }
                string bridgeName = RendererBridgeName(
                    sourceBone, animationRoot);
                Transform[] collisions = DirectChildren(owner)
                    .Where(value => string.Equals(
                        value.name, bridgeName, StringComparison.Ordinal))
                    .ToArray();
                if (collisions.Length != 0)
                    throw new InvalidOperationException(
                        "The physical owner " + ownerRole
                        + " already contains the reserved bridge object '"
                        + bridgeName + "'.");

                var bridgeObject = new GameObject(bridgeName)
                {
                    layer = owner.gameObject.layer,
                };
                Transform bridge = bridgeObject.transform;
                bridge.SetParent(owner, false);
                Matrix4x4 localMatrix = owner.worldToLocalMatrix
                    * sourceRestMatrices[sourceBone];
                ApplyRendererBridgeLocalMatrix(
                    bridge,
                    localMatrix,
                    "renderer bridge for "
                    + StableRendererBridgeTransformKey(
                        animationRoot, sourceBone));
                EnsureRendererBridgeWorldMatrix(
                    sourceRestMatrices[sourceBone],
                    bridge.localToWorldMatrix,
                    sourceBone.name + " initial world matrix");

                bridges.Add(sourceBone, bridge);
                ownerRoles.Add(sourceBone, ownerRole);
            }

            var rebinds = new Dictionary<SkinnedMeshRenderer, Component>();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Transform[] sourceOrder = renderer.bones;
                Transform[] bridgeOrder = sourceOrder
                    .Select(sourceBone => bridges[sourceBone])
                    .ToArray();
                Component rebind = AddNative(
                    renderer.gameObject,
                    rebindType,
                    renderer.name + " SkinnedBoneRebind");
                ConfigureRendererRebind(rebind, renderer, bridgeOrder);
                renderer.rootBone = physicsRoot;
                renderer.updateWhenOffscreen = true;
                EditorUtility.SetDirty(renderer);
                rebinds.Add(renderer, rebind);
            }

            var shell = new RendererBridgeShell(
                renderers,
                rebinds,
                bridges,
                ownerRoles,
                sourceRestMatrices);
            ValidateRendererBridgeShell(
                outputRoot, animationRoot, physicsRoot, roles, shell, null);
            shell.SecondaryMotion = definition.IncludeSecondaryMotion
                ? ConfigureSecondaryMotionShell(
                    outputRoot,
                    animationRoot,
                    physicsRoot,
                    roles,
                    shell)
                : null;
            ValidateRendererBridgeShell(
                outputRoot,
                animationRoot,
                physicsRoot,
                roles,
                shell,
                shell.SecondaryMotion?.Bridges);
            return shell;
        }

        /// <summary>
        /// Resolves only durable prefab paths and serialized references. This
        /// is deliberately independent of instance IDs so it can run after the
        /// coordinator saves, unloads, and reloads the generated prefab.
        /// </summary>
        private static RendererBridgeShell ResolveRendererBridgeShell(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Vergil333.MarrowNpcToolkit.Authoring.NpcDefinition definition)
        {
            ValidateRendererBridgeArguments(
                outputRoot, animationRoot, physicsRoot, roles);
            Type rebindType = ResolveRendererBridgeComponentType();
            List<SkinnedMeshRenderer> renderers = CollectRendererBridgeRenderers(
                animationRoot);
            List<Transform> sourceBones = CollectRendererBridgeSourceBones(
                animationRoot, renderers);

            var bridges = new Dictionary<Transform, Transform>();
            var ownerRoles = new Dictionary<Transform, HumanBodyBones>();
            var sourceRestMatrices = new Dictionary<Transform, Matrix4x4>();
            foreach (Transform sourceBone in sourceBones)
            {
                HumanBodyBones ownerRole = ResolveRendererBridgeOwnerRole(
                    sourceBone, animationRoot, roles);
                Transform owner = roles[ownerRole].Body;
                if (IsPhysicalJawRendererSource(sourceBone, roles))
                {
                    bridges.Add(sourceBone, owner);
                    ownerRoles.Add(sourceBone, HumanBodyBones.Jaw);
                    sourceRestMatrices.Add(
                        sourceBone, sourceBone.localToWorldMatrix);
                    continue;
                }
                string bridgeName = RendererBridgeName(
                    sourceBone, animationRoot);
                Transform[] matches = DirectChildren(owner)
                    .Where(value => string.Equals(
                        value.name, bridgeName, StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(
                        "Expected one saved renderer bridge named '"
                        + bridgeName + "' directly below " + ownerRole
                        + "; found " + matches.Length + ".");
                bridges.Add(sourceBone, matches[0]);
                ownerRoles.Add(sourceBone, ownerRole);
                sourceRestMatrices.Add(
                    sourceBone, sourceBone.localToWorldMatrix);
            }

            var rebinds = new Dictionary<SkinnedMeshRenderer, Component>();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Component[] matches = renderer.gameObject
                    .GetComponents(rebindType)
                    .Where(component => component != null
                        && new SerializedObject(component)
                            .FindProperty("skinnedMeshRenderer")
                            ?.objectReferenceValue == renderer)
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(
                        "Expected one saved SkinnedBoneRebind for renderer "
                        + RendererBridgeRendererKey(animationRoot, renderer)
                        + "; found " + matches.Length + ".");
                rebinds.Add(renderer, matches[0]);
            }

            var shell = new RendererBridgeShell(
                renderers,
                rebinds,
                bridges,
                ownerRoles,
                sourceRestMatrices);
            shell.SecondaryMotion = definition.IncludeSecondaryMotion
                ? ResolveSecondaryMotionShell(
                    outputRoot,
                    animationRoot,
                    physicsRoot,
                    roles,
                    shell)
                : null;
            ValidateRendererBridgeShell(
                outputRoot,
                animationRoot,
                physicsRoot,
                roles,
                shell,
                shell.SecondaryMotion?.Bridges);
            return shell;
        }

        private static void ValidateRendererBridgeShell(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            RendererBridgeShell shell,
            IReadOnlyCollection<Transform> secondaryMotionBridges)
        {
            ValidateRendererBridgeArguments(
                outputRoot, animationRoot, physicsRoot, roles);
            if (shell == null)
                throw new InvalidOperationException(
                    "The renderer bridge shell was not resolved.");

            Type rebindType = ResolveRendererBridgeComponentType();
            List<SkinnedMeshRenderer> expectedRenderers =
                CollectRendererBridgeRenderers(animationRoot);
            List<Transform> sourceBones = CollectRendererBridgeSourceBones(
                animationRoot, expectedRenderers);
            bool physicalJaw = roles.ContainsKey(HumanBodyBones.Jaw);
            bool hasJawRendererSource = physicalJaw
                && sourceBones.Contains(roles[HumanBodyBones.Jaw].Target);
            if (physicalJaw && !hasJawRendererSource)
                throw new InvalidOperationException(
                    "Physical Jaw requires at least one renderer slot sourced from "
                    + "the accepted Avatar Jaw transform.");
            if (!expectedRenderers.SequenceEqual(shell.Renderers)
                || shell.Rebinds.Count != expectedRenderers.Count
                || shell.Bridges.Count != sourceBones.Count
                || shell.OwnerRoles.Count != sourceBones.Count
                || shell.SourceRestMatrices.Count != sourceBones.Count)
                throw new InvalidOperationException(
                    "The renderer bridge shell does not cover every renderer "
                    + "and unique source bone exactly once.");
            if (secondaryMotionBridges != null
                && (secondaryMotionBridges.Count != 2
                    || secondaryMotionBridges.Distinct().Count() != 2
                    || secondaryMotionBridges.Any(value => value == null
                        || !shell.Bridges.Values.Contains(value))))
                throw new InvalidOperationException(
                    "Secondary Motion must reserve exactly two distinct renderer "
                    + "bridge transforms from this renderer shell.");

            Component[] allRebinds = outputRoot.GetComponentsInChildren(
                rebindType, true);
            if (allRebinds.Length != expectedRenderers.Count
                || allRebinds.Distinct().Count() != allRebinds.Length
                || shell.Rebinds.Values.Distinct().Count()
                    != expectedRenderers.Count
                || allRebinds.Any(component =>
                    !shell.Rebinds.Values.Contains(component)))
                throw new InvalidOperationException(
                    "SkinnedBoneRebind must exist exactly once per eligible "
                    + "renderer and nowhere else in the generated prefab.");

            Transform[] namedBridges = outputRoot
                .GetComponentsInChildren<Transform>(true)
                .Where(value => value.name.StartsWith(
                    RendererBridgePrefix, StringComparison.Ordinal))
                .ToArray();
            int expectedNamedBridgeCount = sourceBones.Count
                - (hasJawRendererSource ? 1 : 0);
            if (namedBridges.Length != expectedNamedBridgeCount
                || namedBridges.Distinct().Count() != namedBridges.Length
                || shell.Bridges.Values.Distinct().Count() != sourceBones.Count
                || namedBridges.Any(value =>
                    !shell.Bridges.Values.Contains(value)))
                throw new InvalidOperationException(
                    "The generated prefab must have one reserved component-free "
                    + "bridge transform per unique source bone and no stale bridges.");

            foreach (Transform sourceBone in sourceBones)
            {
                if (sourceBone == null
                    || !shell.Bridges.TryGetValue(
                        sourceBone, out Transform bridge)
                    || bridge == null)
                    throw new InvalidOperationException(
                        "A skinned renderer contains a null or unmapped source bone.");
                HumanBodyBones expectedOwnerRole =
                    ResolveRendererBridgeOwnerRole(
                        sourceBone, animationRoot, roles);
                bool jawSource = IsPhysicalJawRendererSource(sourceBone, roles);
                if (!shell.OwnerRoles.TryGetValue(
                        sourceBone, out HumanBodyBones actualOwnerRole)
                    || actualOwnerRole != expectedOwnerRole
                    || jawSource && bridge != roles[HumanBodyBones.Jaw].Body
                    || !jawSource && bridge.parent != roles[expectedOwnerRole].Body)
                    throw new InvalidOperationException(
                        sourceBone.name
                        + " is not directly parented below its semantic physical "
                        + "owner " + expectedOwnerRole + ".");
                if (jawSource)
                {
                    if (bridge.name.StartsWith(
                            RendererBridgePrefix, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "The renderer source Jaw must map directly to the "
                            + "physical Jaw body, never a bridge transform.");
                    if (!shell.SourceRestMatrices.TryGetValue(
                            sourceBone, out Matrix4x4 jawSourceRest))
                        throw new InvalidOperationException(
                            "The renderer source Jaw has no captured rest matrix.");
                    EnsureRendererBridgeWorldMatrix(
                        jawSourceRest,
                        sourceBone.localToWorldMatrix,
                        "Jaw stable source rest matrix");
                    continue;
                }
                if (!string.Equals(
                        bridge.name,
                        RendererBridgeName(sourceBone, animationRoot),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        sourceBone.name
                        + " renderer bridge has a non-deterministic saved name.");

                Component[] bridgeComponents = bridge.GetComponents<Component>();
                bool secondaryMotionBridge = secondaryMotionBridges != null
                    && secondaryMotionBridges.Contains(bridge);
                if (secondaryMotionBridge)
                {
                    if (bridgeComponents.Length != 4
                        || bridgeComponents.Count(value => value is Transform) != 1
                        || bridgeComponents.Count(value => value is Rigidbody) != 1
                        || bridgeComponents.Count(
                            value => value is SphereCollider) != 1
                        || bridgeComponents.Count(
                            value => value is ConfigurableJoint) != 1)
                        throw new InvalidOperationException(
                            bridge.name + " must contain only the exact Transform, "
                            + "Rigidbody, SphereCollider, and ConfigurableJoint "
                            + "Secondary Motion component set.");
                }
                else if (bridgeComponents.Length != 1
                         || !(bridgeComponents[0] is Transform))
                    throw new InvalidOperationException(
                        bridge.name
                        + " must remain component-free except for its Transform.");
                Matrix4x4 sourceRest = sourceBone.localToWorldMatrix;
                if (!shell.SourceRestMatrices.TryGetValue(
                        sourceBone, out Matrix4x4 recordedSourceRest))
                    throw new InvalidOperationException(
                        sourceBone.name + " has no captured rest matrix.");
                EnsureRendererBridgeWorldMatrix(
                    recordedSourceRest,
                    sourceRest,
                    sourceBone.name + " stable source rest matrix");
                EnsureRendererBridgeWorldMatrix(
                    sourceRest,
                    bridge.localToWorldMatrix,
                    sourceBone.name + " source/bridge rest matrix");

                string expectedPath = RelativePath(
                    outputRoot.transform,
                    roles[expectedOwnerRole].Body)
                    + "/" + bridge.name;
                if (!string.Equals(
                        RelativePath(outputRoot.transform, bridge),
                        expectedPath,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        bridge.name
                        + " did not preserve its deterministic saved prefab path.");
            }

            foreach (SkinnedMeshRenderer renderer in expectedRenderers)
            {
                if (renderer.rootBone != physicsRoot
                    || !renderer.updateWhenOffscreen)
                    throw new InvalidOperationException(
                        RendererBridgeRendererKey(animationRoot, renderer)
                        + " must use Physics as rootBone and update offscreen.");
                if (!shell.Rebinds.TryGetValue(
                        renderer, out Component rebind)
                    || rebind == null
                    || rebind.transform != renderer.transform
                    || !rebindType.IsInstanceOfType(rebind))
                    throw new InvalidOperationException(
                        RendererBridgeRendererKey(animationRoot, renderer)
                        + " has no local SkinnedBoneRebind.");

                Transform[] sourceOrder = renderer.bones;
                var serialized = new SerializedObject(rebind);
                SerializedProperty targetBones = Require(serialized, "bones");
                SerializedProperty cache = Require(serialized, "rebindBone");
                if (!targetBones.isArray || !cache.isArray
                    || targetBones.arraySize != sourceOrder.Length
                    || cache.arraySize != sourceOrder.Length)
                    throw new InvalidOperationException(
                        RendererBridgeRendererKey(animationRoot, renderer)
                        + " does not preserve the source bone count in its "
                        + "SkinnedBoneRebind arrays.");
                for (int index = 0; index < sourceOrder.Length; index++)
                {
                    Transform sourceBone = sourceOrder[index];
                    if (sourceBone == null
                        || !shell.Bridges.TryGetValue(
                            sourceBone, out Transform expectedBridge)
                        || targetBones.GetArrayElementAtIndex(index)
                            .objectReferenceValue != expectedBridge
                        || cache.GetArrayElementAtIndex(index).intValue != 0)
                        throw new InvalidOperationException(
                            RendererBridgeRendererKey(animationRoot, renderer)
                            + " has a null, reordered, unmapped, or non-zero "
                            + "SkinnedBoneRebind entry at index " + index + ".");
                }
                if (Require(serialized, "skinnedMeshRenderer")
                        .objectReferenceValue != renderer
                    || Require(serialized, "meshToRead")
                        .objectReferenceValue != null
                    || Require(serialized, "meshToWrite")
                        .objectReferenceValue != null)
                    throw new InvalidOperationException(
                        RendererBridgeRendererKey(animationRoot, renderer)
                        + " has incorrect renderer or mesh references.");
            }
        }

        private static void AppendRendererBridgeFingerprint(
            StringBuilder text,
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            RendererBridgeShell shell)
        {
            if (text == null || outputRoot == null || animationRoot == null
                || physicsRoot == null || shell == null)
                throw new ArgumentNullException(
                    "Renderer bridge fingerprint arguments cannot be null.");

            text.Append("rendererBridge=")
                .Append(shell.Renderers.Count).Append(',')
                .Append(shell.Bridges.Count).Append(',')
                .Append(RelativePath(outputRoot.transform, physicsRoot))
                .Append('|');
            foreach (Transform sourceBone in shell.Bridges.Keys.OrderBy(
                         value => StableRendererBridgeTransformKey(
                             animationRoot, value),
                         StringComparer.Ordinal))
            {
                Transform bridge = shell.Bridges[sourceBone];
                text.Append("bridge:")
                    .Append(StableRendererBridgeTransformKey(
                        animationRoot, sourceBone))
                    .Append(':').Append(shell.OwnerRoles[sourceBone])
                    .Append(':')
                    .Append(RelativePath(outputRoot.transform, bridge))
                    .Append(':');
                AppendRendererBridgeMatrix(text, sourceBone.localToWorldMatrix);
                AppendRendererBridgeMatrix(text, bridge.localToWorldMatrix);
                text.Append('|');
            }
            foreach (SkinnedMeshRenderer renderer in shell.Renderers)
            {
                text.Append("rebind:")
                    .Append(RendererBridgeRendererKey(
                        animationRoot, renderer))
                    .Append(':')
                    .Append(RelativePath(outputRoot.transform, renderer.rootBone))
                    .Append(':').Append(renderer.updateWhenOffscreen ? '1' : '0')
                    .Append(':');
                foreach (Transform sourceBone in renderer.bones)
                    text.Append(StableRendererBridgeTransformKey(
                            animationRoot, sourceBone))
                        .Append('>')
                        .Append(RelativePath(
                            outputRoot.transform,
                            shell.Bridges[sourceBone]))
                        .Append(',');
                text.Append('|');
            }
        }

        private static void ConfigureRendererRebind(
            Component rebind,
            SkinnedMeshRenderer renderer,
            IReadOnlyList<Transform> orderedBridges)
        {
            var serialized = new SerializedObject(rebind);
            SerializedProperty bones = Require(serialized, "bones");
            SerializedProperty cache = Require(serialized, "rebindBone");
            bones.arraySize = orderedBridges.Count;
            cache.arraySize = orderedBridges.Count;
            for (int index = 0; index < orderedBridges.Count; index++)
            {
                bones.GetArrayElementAtIndex(index).objectReferenceValue =
                    orderedBridges[index];
                cache.GetArrayElementAtIndex(index).intValue = 0;
            }
            Require(serialized, "skinnedMeshRenderer").objectReferenceValue =
                renderer;
            Require(serialized, "meshToRead").objectReferenceValue = null;
            Require(serialized, "meshToWrite").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Type ResolveRendererBridgeComponentType()
        {
            const string fullName =
                "SLZ.Marrow.PuppetMasta.SkinnedBoneRebind";
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => string.Equals(
                    assembly.GetName().Name,
                    "SLZ.Marrow",
                    StringComparison.Ordinal))
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(value => value != null);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                throw new TypeLoadException(
                    fullName
                    + " is unavailable from the exact SLZ.Marrow assembly.");
            return type;
        }

        private static List<SkinnedMeshRenderer> CollectRendererBridgeRenderers(
            Transform animationRoot)
        {
            var renderers = animationRoot
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer != null
                    && renderer.bones != null
                    && renderer.bones.Length > 0)
                .OrderBy(
                    renderer => RendererBridgeRendererKey(
                        animationRoot, renderer),
                    StringComparer.Ordinal)
                .ToList();
            if (renderers.Count == 0)
                throw new InvalidOperationException(
                    "AnimationRoot has no skinned renderer with a bone array.");
            if (renderers.Select(renderer =>
                    RendererBridgeRendererKey(animationRoot, renderer))
                .Distinct(StringComparer.Ordinal).Count() != renderers.Count)
                throw new InvalidOperationException(
                    "AnimationRoot renderer keys are not deterministic and unique.");
            return renderers;
        }

        private static List<Transform> CollectRendererBridgeSourceBones(
            Transform animationRoot,
            IEnumerable<SkinnedMeshRenderer> renderers)
        {
            var sourceBones = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Transform[] bones = renderer.bones;
                if (bones == null || bones.Length == 0)
                    throw new InvalidOperationException(
                        RendererBridgeRendererKey(animationRoot, renderer)
                        + " lost its source bone array.");
                for (int index = 0; index < bones.Length; index++)
                {
                    Transform bone = bones[index];
                    if (bone == null)
                        throw new InvalidOperationException(
                            RendererBridgeRendererKey(animationRoot, renderer)
                            + " contains a null source bone at index " + index + ".");
                    if (!WithinRendererBridgeRoot(bone, animationRoot))
                        throw new InvalidOperationException(
                            RendererBridgeRendererKey(animationRoot, renderer)
                            + " references a source bone outside AnimationRoot: "
                            + bone.name + ".");
                    sourceBones.Add(bone);
                }
            }
            return sourceBones.OrderBy(
                    value => StableRendererBridgeTransformKey(
                        animationRoot, value),
                    StringComparer.Ordinal)
                .ToList();
        }

        private static HumanBodyBones ResolveRendererBridgeOwnerRole(
            Transform sourceBone,
            Transform animationRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            if (sourceBone == null
                || !WithinRendererBridgeRoot(sourceBone, animationRoot))
                throw new InvalidOperationException(
                    "Renderer source bone is null or outside AnimationRoot.");
            var roleForTarget = roles.ToDictionary(
                pair => pair.Value.Target,
                pair => pair.Key);
            if (roleForTarget.TryGetValue(
                    sourceBone, out HumanBodyBones exactRole))
                return exactRole;

            for (Transform cursor = sourceBone.parent;
                 cursor != null && WithinRendererBridgeRoot(
                     cursor, animationRoot);
                 cursor = cursor.parent)
                if (roleForTarget.TryGetValue(
                        cursor, out HumanBodyBones ancestorRole))
                    return ancestorRole;

            Transform hipsTarget = roles[HumanBodyBones.Hips].Target;
            if (sourceBone == hipsTarget || hipsTarget.IsChildOf(sourceBone))
                return HumanBodyBones.Hips;

            throw new InvalidOperationException(
                "Cannot map renderer source bone '"
                + StableRendererBridgeTransformKey(animationRoot, sourceBone)
                + "' to an exact canonical body, its nearest canonical "
                + "ancestor, or the Hips root fallback.");
        }

        private static void ValidateRendererBridgeArguments(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            if (outputRoot == null || animationRoot == null
                || physicsRoot == null || roles == null)
                throw new ArgumentNullException(
                    "Renderer bridge roots and role map cannot be null.");
            if (animationRoot.parent != outputRoot.transform
                || physicsRoot.parent != outputRoot.transform)
                throw new InvalidOperationException(
                    "Renderer bridge requires direct AnimationRoot and Physics "
                    + "siblings below the output root.");
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            if (roles.Count != entityOrder.Count)
                throw new InvalidOperationException(
                    "Renderer bridge requires the complete " + entityOrder.Count
                    + "-body role map.");
            foreach (HumanBodyBones role in entityOrder)
            {
                if (!roles.TryGetValue(role, out NativeRole value)
                    || value == null || value.Target == null || value.Body == null
                    || !WithinRendererBridgeRoot(value.Target, animationRoot)
                    || !WithinRendererBridgeRoot(value.Body, physicsRoot))
                    throw new InvalidOperationException(
                        role + " is missing a durable AnimationRoot target or "
                        + "Physics owner for renderer rebinding.");
            }
            if (roles.Values.Select(value => value.Target).Distinct().Count()
                    != roles.Count
                || roles.Values.Select(value => value.Body).Distinct().Count()
                    != roles.Count)
                throw new InvalidOperationException(
                    "Renderer bridge role targets and physical owners must be unique.");
        }

        private static bool IsPhysicalJawRendererSource(
            Transform sourceBone,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            return sourceBone != null && roles != null
                && roles.TryGetValue(HumanBodyBones.Jaw, out NativeRole jaw)
                && jaw != null && sourceBone == jaw.Target;
        }

        private static string RendererBridgeName(
            Transform sourceBone,
            Transform animationRoot)
        {
            string stableKey = StableRendererBridgeTransformKey(
                animationRoot, sourceBone);
            string readable = new string(sourceBone.name
                .Select(character => char.IsLetterOrDigit(character)
                    || character == '_' || character == '-'
                        ? character : '_')
                .Take(48)
                .ToArray());
            if (string.IsNullOrEmpty(readable))
                readable = "Bone";
            return RendererBridgePrefix + readable + "_"
                + RendererBridgeFnv1a64(stableKey).ToString(
                    "x16", CultureInfo.InvariantCulture);
        }

        private static string RendererBridgeRendererKey(
            Transform animationRoot,
            SkinnedMeshRenderer renderer)
        {
            SkinnedMeshRenderer[] components = renderer.transform
                .GetComponents<SkinnedMeshRenderer>();
            int ordinal = Array.IndexOf(components, renderer);
            if (ordinal < 0)
                throw new InvalidOperationException(
                    "Renderer is not attached to its declared Transform.");
            return StableRendererBridgeTransformKey(
                animationRoot, renderer.transform)
                + "#SkinnedMeshRenderer[" + ordinal + "]";
        }

        private static string StableRendererBridgeTransformKey(
            Transform root,
            Transform value)
        {
            if (root == null || value == null
                || !WithinRendererBridgeRoot(value, root))
                return "<outside>";
            if (value == root)
                return ".";
            var segments = new List<string>();
            for (Transform cursor = value;
                 cursor != null && cursor != root;
                 cursor = cursor.parent)
                segments.Add(
                    cursor.name.Length + ":" + cursor.name + "["
                    + cursor.GetSiblingIndex() + "]");
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static ulong RendererBridgeFnv1a64(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            foreach (char character in value)
            {
                hash ^= (byte)(character & 0xff);
                hash *= prime;
                hash ^= (byte)(character >> 8);
                hash *= prime;
            }
            return hash;
        }

        private static IEnumerable<Transform> DirectChildren(Transform parent)
        {
            for (int index = 0; index < parent.childCount; index++)
                yield return parent.GetChild(index);
        }

        private static bool WithinRendererBridgeRoot(
            Transform value,
            Transform root)
        {
            return value != null && root != null
                && (value == root || value.IsChildOf(root));
        }

        private static void ApplyRendererBridgeLocalMatrix(
            Transform target,
            Matrix4x4 matrix,
            string label)
        {
            if (!RendererBridgeMatrixIsFinite(matrix))
                throw new InvalidOperationException(
                    label + " contains a non-finite local matrix.");
            Vector3 x = matrix.GetColumn(0);
            Vector3 y = matrix.GetColumn(1);
            Vector3 z = matrix.GetColumn(2);
            Vector3 scale = new Vector3(
                x.magnitude, y.magnitude, z.magnitude);
            if (scale.x < 0.0000001f || scale.y < 0.0000001f
                || scale.z < 0.0000001f)
                throw new InvalidOperationException(
                    label + " contains a singular local matrix.");
            x /= scale.x;
            y /= scale.y;
            z /= scale.z;

            float handedness = Vector3.Dot(Vector3.Cross(x, y), z);
            if (Mathf.Abs(handedness) < 0.9999f)
                throw new InvalidOperationException(
                    label + " contains shear that a Unity Transform cannot "
                    + "preserve exactly.");
            if (handedness < 0f)
            {
                x = -x;
                scale.x = -scale.x;
            }
            float shear = Mathf.Max(
                Mathf.Abs(Vector3.Dot(x, y)),
                Mathf.Max(
                    Mathf.Abs(Vector3.Dot(x, z)),
                    Mathf.Abs(Vector3.Dot(y, z))));
            if (shear > 0.00005f)
                throw new InvalidOperationException(
                    label + " contains shear " + shear.ToString(
                        "R", CultureInfo.InvariantCulture)
                    + " that a Unity Transform cannot preserve exactly.");

            Quaternion rotation = Quaternion.LookRotation(z, y);
            Vector3 position = matrix.GetColumn(3);
            Matrix4x4 rebuilt = Matrix4x4.TRS(
                position, rotation, scale);
            EnsureRendererBridgeWorldMatrix(
                matrix, rebuilt, label + " local TRS decomposition");
            target.localPosition = position;
            target.localRotation = rotation;
            target.localScale = scale;
        }

        private static void EnsureRendererBridgeWorldMatrix(
            Matrix4x4 expected,
            Matrix4x4 actual,
            string label)
        {
            float error = RendererBridgeMatrixError(expected, actual);
            if (!IsFinite(error)
                || error > RendererBridgeMatrixTolerance)
                throw new InvalidOperationException(
                    label + " differs by " + error.ToString(
                        "R", CultureInfo.InvariantCulture)
                    + "; maximum allowed error is "
                    + RendererBridgeMatrixTolerance.ToString(
                        "R", CultureInfo.InvariantCulture) + ".");
        }

        private static float RendererBridgeMatrixError(
            Matrix4x4 expected,
            Matrix4x4 actual)
        {
            float maximum = 0f;
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    maximum = Mathf.Max(
                        maximum,
                        Mathf.Abs(expected[row, column]
                            - actual[row, column]));
            return maximum;
        }

        private static bool RendererBridgeMatrixIsFinite(Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    if (!IsFinite(matrix[row, column]))
                        return false;
            return true;
        }

        private static void AppendRendererBridgeMatrix(
            StringBuilder text,
            Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    text.Append(F(matrix[row, column])).Append(',');
            text.Append(':');
        }

        private sealed class RendererBridgeShell
        {
            public IReadOnlyList<SkinnedMeshRenderer> Renderers { get; }
            public IReadOnlyDictionary<SkinnedMeshRenderer, Component> Rebinds
            {
                get;
            }
            public IReadOnlyDictionary<Transform, Transform> Bridges { get; }
            public IReadOnlyDictionary<Transform, HumanBodyBones> OwnerRoles
            {
                get;
            }
            public IReadOnlyDictionary<Transform, Matrix4x4> SourceRestMatrices
            {
                get;
            }
            public SecondaryMotionShell SecondaryMotion { get; set; }

            public RendererBridgeShell(
                IReadOnlyList<SkinnedMeshRenderer> renderers,
                IReadOnlyDictionary<SkinnedMeshRenderer, Component> rebinds,
                IReadOnlyDictionary<Transform, Transform> bridges,
                IReadOnlyDictionary<Transform, HumanBodyBones> ownerRoles,
                IReadOnlyDictionary<Transform, Matrix4x4> sourceRestMatrices)
            {
                Renderers = renderers;
                Rebinds = rebinds;
                Bridges = bridges;
                OwnerRoles = ownerRoles;
                SourceRestMatrices = sourceRestMatrices;
            }
        }
    }
}
