using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        private const float SecondaryMotionMass = 0.08f;
        private const float SecondaryMotionDrag = 0.15f;
        private const float SecondaryMotionAngularDrag = 0.5f;
        private const float SecondaryMotionLimit = 0.04f;
        private const float SecondaryMotionSpring = 55f;
        private const float SecondaryMotionDamper = 2.5f;
        private const float SecondaryMotionMaximumForce = 25f;
        private const float SecondaryMotionColliderRadius = 0.055f;
        private const float SecondaryMotionTolerance = 0.00001f;

        private static readonly string[] SecondaryMotionSourceProperties =
        {
            "primaryRt",
            "secondaryLf",
        };

        /// <summary>
        /// Builds spring bodies only from the Avatar's explicit Breast Soft Body
        /// references. No transform name, sex, model, or hierarchy convention is
        /// assumed: the renderer bridge map supplies the physical copy and its
        /// nearest canonical owner.
        /// </summary>
        private static SecondaryMotionShell ConfigureSecondaryMotionShell(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            RendererBridgeShell rendererBridge)
        {
            List<SecondaryMotionBinding> bindings =
                ResolveSecondaryMotionBindings(
                    outputRoot,
                    animationRoot,
                    physicsRoot,
                    roles,
                    rendererBridge,
                    false);

            foreach (SecondaryMotionBinding binding in bindings)
            {
                Component[] existing = binding.Bridge.GetComponents<Component>();
                if (existing.Length != 1 || !(existing[0] is Transform))
                    throw new InvalidOperationException(
                        binding.Bridge.name + " is not an untouched renderer "
                        + "bridge before Secondary Motion generation.");

                Rigidbody body = binding.Bridge.gameObject.AddComponent<Rigidbody>();
                body.mass = SecondaryMotionMass;
                body.drag = SecondaryMotionDrag;
                body.angularDrag = SecondaryMotionAngularDrag;
                body.useGravity = true;
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.constraints = RigidbodyConstraints.None;
                body.detectCollisions = true;

                SphereCollider collider = binding.Bridge.gameObject
                    .AddComponent<SphereCollider>();
                collider.center = Vector3.zero;
                collider.radius = SecondaryMotionColliderRadius;
                collider.isTrigger = false;
                collider.enabled = true;

                ConfigurableJoint joint = binding.Bridge.gameObject
                    .AddComponent<ConfigurableJoint>();
                joint.connectedBody = binding.OwnerBody;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = Vector3.zero;
                joint.connectedAnchor = binding.OwnerBody.transform
                    .InverseTransformPoint(binding.Bridge.position);
                joint.axis = Vector3.right;
                joint.secondaryAxis = Vector3.up;
                joint.xMotion = ConfigurableJointMotion.Limited;
                joint.yMotion = ConfigurableJointMotion.Limited;
                joint.zMotion = ConfigurableJointMotion.Limited;
                joint.angularXMotion = ConfigurableJointMotion.Locked;
                joint.angularYMotion = ConfigurableJointMotion.Locked;
                joint.angularZMotion = ConfigurableJointMotion.Locked;
                joint.linearLimit = new SoftJointLimit
                {
                    limit = SecondaryMotionLimit,
                    bounciness = 0f,
                    contactDistance = SecondaryMotionLimit * 0.1f,
                };
                JointDrive drive = new JointDrive
                {
                    positionSpring = SecondaryMotionSpring,
                    positionDamper = SecondaryMotionDamper,
                    maximumForce = SecondaryMotionMaximumForce,
                };
                joint.xDrive = drive;
                joint.yDrive = drive;
                joint.zDrive = drive;
                joint.targetPosition = Vector3.zero;
                joint.targetVelocity = Vector3.zero;
                joint.targetRotation = Quaternion.identity;
                joint.targetAngularVelocity = Vector3.zero;
                joint.rotationDriveMode = RotationDriveMode.XYAndZ;
                joint.projectionMode = JointProjectionMode.PositionAndRotation;
                joint.projectionDistance = SecondaryMotionLimit * 0.5f;
                joint.enableCollision = false;
                joint.enablePreprocessing = true;
                joint.massScale = 1f;
                joint.connectedMassScale = 1f;

                binding.Body = body;
                binding.Collider = collider;
                binding.Joint = joint;
                EditorUtility.SetDirty(binding.Bridge.gameObject);
            }

            Physics.SyncTransforms();
            var shell = new SecondaryMotionShell(bindings);
            ValidateSecondaryMotionShell(
                outputRoot,
                animationRoot,
                physicsRoot,
                roles,
                rendererBridge,
                shell);
            return shell;
        }

        /// <summary>
        /// Re-resolves the two Avatar references, renderer bridges, and Unity
        /// physics components after the coordinator unloads/reloads the prefab.
        /// </summary>
        private static SecondaryMotionShell ResolveSecondaryMotionShell(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            RendererBridgeShell rendererBridge)
        {
            List<SecondaryMotionBinding> bindings =
                ResolveSecondaryMotionBindings(
                    outputRoot,
                    animationRoot,
                    physicsRoot,
                    roles,
                    rendererBridge,
                    true);
            var shell = new SecondaryMotionShell(bindings);
            ValidateSecondaryMotionShell(
                outputRoot,
                animationRoot,
                physicsRoot,
                roles,
                rendererBridge,
                shell);
            return shell;
        }

        private static List<SecondaryMotionBinding>
            ResolveSecondaryMotionBindings(
                GameObject outputRoot,
                Transform animationRoot,
                Transform physicsRoot,
                IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
                RendererBridgeShell rendererBridge,
                bool requireComponents)
        {
            if (outputRoot == null || animationRoot == null || physicsRoot == null
                || roles == null || rendererBridge == null)
                throw new ArgumentNullException(
                    "Secondary Motion roots, roles, and renderer bridge cannot be null.");

            Type avatarType = ResolveSecondaryMotionAvatarType();
            Component[] avatars = animationRoot
                .GetComponentsInChildren(avatarType, true)
                .Cast<Component>()
                .ToArray();
            if (avatars.Length != 1)
                throw new InvalidOperationException(
                    "Secondary Motion requires exactly one cloned SLZ.VRMK.Avatar "
                    + "below AnimationRoot; found " + avatars.Length + ".");

            var avatarObject = new SerializedObject(avatars[0]);
            SerializedProperty breast = avatarObject.FindProperty("bulgeBreast");
            if (breast == null)
                throw new MissingFieldException(
                    avatarType.FullName,
                    "bulgeBreast");

            var bindings = new List<SecondaryMotionBinding>();
            foreach (string propertyName in SecondaryMotionSourceProperties)
            {
                SerializedProperty property = breast.FindPropertyRelative(
                    propertyName);
                Transform source = property?.objectReferenceValue as Transform;
                if (source == null || !source.IsChildOf(animationRoot))
                    throw new InvalidOperationException(
                        "Avatar Breast Soft Body '" + propertyName
                        + "' is blank or outside AnimationRoot.");
                if (!rendererBridge.Bridges.TryGetValue(
                        source, out Transform bridge)
                    || bridge == null
                    || !bridge.name.StartsWith(
                        RendererBridgePrefix, StringComparison.Ordinal)
                    || !bridge.IsChildOf(physicsRoot))
                    throw new InvalidOperationException(
                        "Avatar Breast Soft Body '" + propertyName
                        + "' is not a skinned source bone with a reserved renderer "
                        + "bridge below Physics.");
                if (!rendererBridge.OwnerRoles.TryGetValue(
                        source, out HumanBodyBones ownerRole)
                    || !roles.TryGetValue(ownerRole, out NativeRole owner)
                    || owner == null || owner.Body == null
                    || owner.Rigidbody == null || bridge.parent != owner.Body)
                    throw new InvalidOperationException(
                        "Avatar Breast Soft Body '" + propertyName
                        + "' has no exact canonical physical owner.");

                var binding = new SecondaryMotionBinding(
                    propertyName,
                    source,
                    bridge,
                    ownerRole,
                    owner.Rigidbody);
                if (requireComponents)
                {
                    binding.Body = RequireOnlySecondaryMotionComponent<Rigidbody>(
                        bridge,
                        propertyName + " Rigidbody");
                    binding.Collider =
                        RequireOnlySecondaryMotionComponent<SphereCollider>(
                            bridge,
                            propertyName + " SphereCollider");
                    binding.Joint =
                        RequireOnlySecondaryMotionComponent<ConfigurableJoint>(
                            bridge,
                            propertyName + " ConfigurableJoint");
                }
                bindings.Add(binding);
            }

            if (bindings.Count != 2
                || bindings.Select(value => value.Source).Distinct().Count() != 2
                || bindings.Select(value => value.Bridge).Distinct().Count() != 2)
                throw new InvalidOperationException(
                    "Avatar Breast Soft Body must reference two distinct skinned "
                    + "source bones and two distinct physical renderer bridges.");
            return bindings;
        }

        private static T RequireOnlySecondaryMotionComponent<T>(
            Transform bridge,
            string label)
            where T : Component
        {
            T[] components = bridge.GetComponents<T>();
            if (components.Length != 1)
                throw new InvalidOperationException(
                    label + " must exist exactly once; found "
                    + components.Length + ".");
            return components[0];
        }

        private static void ValidateSecondaryMotionShell(
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            RendererBridgeShell rendererBridge,
            SecondaryMotionShell shell)
        {
            if (shell == null || shell.Bindings.Count != 2)
                throw new InvalidOperationException(
                    "Secondary Motion did not resolve exactly two bindings.");

            var canonicalBodies = new HashSet<Transform>(
                roles.Values.Select(value => value.Body));
            Transform[] additionalBodies = physicsRoot
                .GetComponentsInChildren<Rigidbody>(true)
                .Select(value => value.transform)
                .Where(value => !canonicalBodies.Contains(value))
                .OrderBy(value => RelativePath(outputRoot.transform, value),
                    StringComparer.Ordinal)
                .ToArray();
            Transform[] additionalJoints = physicsRoot
                .GetComponentsInChildren<ConfigurableJoint>(true)
                .Select(value => value.transform)
                .Where(value => !canonicalBodies.Contains(value))
                .OrderBy(value => RelativePath(outputRoot.transform, value),
                    StringComparer.Ordinal)
                .ToArray();
            Transform[] expectedBridges = shell.Bridges
                .OrderBy(value => RelativePath(outputRoot.transform, value),
                    StringComparer.Ordinal)
                .ToArray();
            if (!additionalBodies.SequenceEqual(expectedBridges)
                || !additionalJoints.SequenceEqual(expectedBridges))
                throw new InvalidOperationException(
                    "Only the two validated Secondary Motion renderer bridges may "
                    + "extend the canonical Unity Rigidbody/Joint graph.");

            foreach (SecondaryMotionBinding binding in shell.Bindings)
            {
                if (binding.Source == null || binding.Bridge == null
                    || binding.Body == null || binding.Collider == null
                    || binding.Joint == null
                    || !binding.Source.IsChildOf(animationRoot)
                    || !rendererBridge.Bridges.TryGetValue(
                        binding.Source, out Transform expectedBridge)
                    || expectedBridge != binding.Bridge
                    || rendererBridge.OwnerRoles[binding.Source]
                        != binding.OwnerRole
                    || roles[binding.OwnerRole].Rigidbody != binding.OwnerBody
                    || binding.Bridge.parent
                        != roles[binding.OwnerRole].Body
                    || binding.Bridge.childCount != 0)
                    throw new InvalidOperationException(
                        binding.PropertyName
                        + " lost its source/bridge/canonical-owner contract.");

                Component[] components = binding.Bridge.GetComponents<Component>();
                if (components.Length != 4
                    || components.Count(value => value is Transform) != 1
                    || components.Count(value => value is Rigidbody) != 1
                    || components.Count(value => value is SphereCollider) != 1
                    || components.Count(value => value is ConfigurableJoint) != 1)
                    throw new InvalidOperationException(
                        binding.PropertyName + " contains a non-secondary component "
                        + "or duplicate physics component.");

                Rigidbody body = binding.Body;
                SphereCollider collider = binding.Collider;
                ConfigurableJoint joint = binding.Joint;
                Vector3 expectedConnectedAnchor = binding.OwnerBody.transform
                    .InverseTransformPoint(binding.Bridge.position);
                if (!NearSecondaryMotion(body.mass, SecondaryMotionMass)
                    || !NearSecondaryMotion(body.drag, SecondaryMotionDrag)
                    || !NearSecondaryMotion(
                        body.angularDrag, SecondaryMotionAngularDrag)
                    || !body.useGravity || body.isKinematic
                    || body.interpolation != RigidbodyInterpolation.Interpolate
                    || body.collisionDetectionMode
                        != CollisionDetectionMode.Discrete
                    || body.constraints != RigidbodyConstraints.None
                    || !body.detectCollisions
                    || !collider.enabled || collider.isTrigger
                    || !NearSecondaryMotion(
                        collider.radius, SecondaryMotionColliderRadius)
                    || !NearSecondaryMotion(collider.center, Vector3.zero)
                    || joint.connectedBody != binding.OwnerBody
                    || joint.autoConfigureConnectedAnchor
                    || !NearSecondaryMotion(joint.anchor, Vector3.zero)
                    || !NearSecondaryMotion(
                        joint.connectedAnchor, expectedConnectedAnchor)
                    || !NearSecondaryMotion(joint.axis, Vector3.right)
                    || !NearSecondaryMotion(joint.secondaryAxis, Vector3.up)
                    || joint.xMotion != ConfigurableJointMotion.Limited
                    || joint.yMotion != ConfigurableJointMotion.Limited
                    || joint.zMotion != ConfigurableJointMotion.Limited
                    || joint.angularXMotion != ConfigurableJointMotion.Locked
                    || joint.angularYMotion != ConfigurableJointMotion.Locked
                    || joint.angularZMotion != ConfigurableJointMotion.Locked
                    || !NearSecondaryMotion(
                        joint.linearLimit.limit, SecondaryMotionLimit)
                    || !NearSecondaryMotion(
                        joint.linearLimit.bounciness, 0f)
                    || !NearSecondaryMotion(
                        joint.linearLimit.contactDistance,
                        SecondaryMotionLimit * 0.1f)
                    || !SecondaryMotionDriveMatches(joint.xDrive)
                    || !SecondaryMotionDriveMatches(joint.yDrive)
                    || !SecondaryMotionDriveMatches(joint.zDrive)
                    || !NearSecondaryMotion(joint.targetPosition, Vector3.zero)
                    || !NearSecondaryMotion(joint.targetVelocity, Vector3.zero)
                    || Quaternion.Angle(
                        joint.targetRotation, Quaternion.identity) > 0.001f
                    || !NearSecondaryMotion(
                        joint.targetAngularVelocity, Vector3.zero)
                    || joint.rotationDriveMode != RotationDriveMode.XYAndZ
                    || joint.projectionMode
                        != JointProjectionMode.PositionAndRotation
                    || !NearSecondaryMotion(
                        joint.projectionDistance,
                        SecondaryMotionLimit * 0.5f)
                    || joint.enableCollision || !joint.enablePreprocessing
                    || !NearSecondaryMotion(joint.massScale, 1f)
                    || !NearSecondaryMotion(joint.connectedMassScale, 1f))
                    throw new InvalidOperationException(
                        binding.PropertyName
                        + " Secondary Motion physics differs from the safe preset.");
            }
        }

        private static bool SecondaryMotionDriveMatches(JointDrive drive)
        {
            return NearSecondaryMotion(
                       drive.positionSpring, SecondaryMotionSpring)
                   && NearSecondaryMotion(
                       drive.positionDamper, SecondaryMotionDamper)
                   && NearSecondaryMotion(
                       drive.maximumForce, SecondaryMotionMaximumForce);
        }

        private static bool NearSecondaryMotion(float actual, float expected)
        {
            return IsFinite(actual)
                && Mathf.Abs(actual - expected) <= SecondaryMotionTolerance;
        }

        private static bool NearSecondaryMotion(Vector3 actual, Vector3 expected)
        {
            return IsFinite(actual.x) && IsFinite(actual.y) && IsFinite(actual.z)
                && Vector3.Distance(actual, expected) <= SecondaryMotionTolerance;
        }

        private static Type ResolveSecondaryMotionAvatarType()
        {
            const string fullName = "SLZ.VRMK.Avatar";
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

        internal static bool TryValidateSecondaryMotionForSmoke(
            NpcDefinition definition,
            GameObject outputRoot,
            Transform animationRoot,
            Transform physicsRoot,
            out int bodyCount,
            out int jointCount,
            out int colliderCount,
            out string detail)
        {
            bodyCount = 0;
            jointCount = 0;
            colliderCount = 0;
            detail = string.Empty;
            try
            {
                if (definition == null || !definition.IncludeSecondaryMotion)
                    throw new InvalidOperationException(
                        "The smoke definition did not request Secondary Motion.");
                Animator animator = FindHumanoidAnimator(
                    animationRoot, definition);
                Dictionary<HumanBodyBones, NativeRole> roles = ResolveRoles(
                    definition,
                    animationRoot,
                    physicsRoot,
                    animator);
                RendererBridgeShell rendererBridge = ResolveRendererBridgeShell(
                    outputRoot,
                    animationRoot,
                    physicsRoot,
                    roles,
                    definition);
                SecondaryMotionShell shell = rendererBridge.SecondaryMotion;
                if (shell == null || shell.Bindings.Count != 2)
                    throw new InvalidOperationException(
                        "The saved renderer bridge has no complete Secondary "
                        + "Motion shell.");
                bodyCount = shell.Bindings.Count(value => value.Body != null);
                jointCount = shell.Bindings.Count(value => value.Joint != null);
                colliderCount = shell.Bindings.Count(
                    value => value.Collider != null);
                if (bodyCount != 2 || jointCount != 2 || colliderCount != 2)
                    throw new InvalidOperationException(
                        "The saved Secondary Motion component counts are not 2/2/2.");
                detail = "Validated two Avatar-referenced spring bodies outside "
                    + "the canonical Marrow/PuppetMaster graph.";
                return true;
            }
            catch (Exception exception)
            {
                detail = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static void AppendSecondaryMotionFingerprint(
            StringBuilder text,
            GameObject outputRoot,
            Transform animationRoot,
            SecondaryMotionShell shell)
        {
            if (text == null || outputRoot == null || animationRoot == null
                || shell == null || shell.Bindings.Count != 2)
                throw new ArgumentNullException(
                    "Secondary Motion fingerprint arguments are incomplete.");

            text.Append("secondaryMotion=")
                .Append(shell.Bindings.Count).Append('|');
            foreach (SecondaryMotionBinding binding in shell.Bindings)
            {
                Rigidbody body = binding.Body;
                SphereCollider collider = binding.Collider;
                ConfigurableJoint joint = binding.Joint;
                text.Append("soft:")
                    .Append(binding.PropertyName).Append(':')
                    .Append(StableRendererBridgeTransformKey(
                        animationRoot, binding.Source)).Append('>')
                    .Append(RelativePath(outputRoot.transform, binding.Bridge))
                    .Append(':').Append(binding.OwnerRole).Append(':')
                    .Append(RelativePath(
                        outputRoot.transform, binding.OwnerBody.transform))
                    .Append(':')
                    .Append(F(body.mass)).Append(',')
                    .Append(F(body.drag)).Append(',')
                    .Append(F(body.angularDrag)).Append(',')
                    .Append(body.useGravity ? '1' : '0')
                    .Append(body.isKinematic ? '1' : '0')
                    .Append((int)body.interpolation).Append(',')
                    .Append((int)body.collisionDetectionMode).Append(',')
                    .Append((int)body.constraints).Append(',')
                    .Append(body.detectCollisions ? '1' : '0').Append(':');
                AppendVector(text, collider.center);
                text.Append(F(collider.radius)).Append(',')
                    .Append(collider.enabled ? '1' : '0')
                    .Append(collider.isTrigger ? '1' : '0').Append(':');
                AppendVector(text, joint.anchor);
                AppendVector(text, joint.connectedAnchor);
                AppendVector(text, joint.axis);
                AppendVector(text, joint.secondaryAxis);
                text.Append((int)joint.xMotion).Append(',')
                    .Append((int)joint.yMotion).Append(',')
                    .Append((int)joint.zMotion).Append(',')
                    .Append((int)joint.angularXMotion).Append(',')
                    .Append((int)joint.angularYMotion).Append(',')
                    .Append((int)joint.angularZMotion).Append(',')
                    .Append(F(joint.linearLimit.limit)).Append(',')
                    .Append(F(joint.linearLimit.bounciness)).Append(',')
                    .Append(F(joint.linearLimit.contactDistance)).Append(',');
                AppendSecondaryMotionDrive(text, joint.xDrive);
                AppendSecondaryMotionDrive(text, joint.yDrive);
                AppendSecondaryMotionDrive(text, joint.zDrive);
                AppendVector(text, joint.targetPosition);
                AppendVector(text, joint.targetVelocity);
                AppendQuaternion(text, joint.targetRotation);
                AppendVector(text, joint.targetAngularVelocity);
                text.Append((int)joint.rotationDriveMode).Append(',')
                    .Append((int)joint.projectionMode).Append(',')
                    .Append(F(joint.projectionDistance)).Append(',')
                    .Append(joint.enableCollision ? '1' : '0')
                    .Append(joint.enablePreprocessing ? '1' : '0')
                    .Append(F(joint.massScale)).Append(',')
                    .Append(F(joint.connectedMassScale)).Append('|');
            }
        }

        private static void AppendSecondaryMotionDrive(
            StringBuilder text,
            JointDrive drive)
        {
            text.Append(F(drive.positionSpring)).Append(',')
                .Append(F(drive.positionDamper)).Append(',')
                .Append(F(drive.maximumForce)).Append(',');
        }

        private sealed class SecondaryMotionShell
        {
            public IReadOnlyList<SecondaryMotionBinding> Bindings { get; }
            public IReadOnlyCollection<Transform> Bridges { get; }

            public SecondaryMotionShell(
                IReadOnlyList<SecondaryMotionBinding> bindings)
            {
                Bindings = bindings ?? throw new ArgumentNullException(
                    nameof(bindings));
                Bridges = bindings.Select(value => value.Bridge).ToArray();
            }
        }

        private sealed class SecondaryMotionBinding
        {
            public string PropertyName { get; }
            public Transform Source { get; }
            public Transform Bridge { get; }
            public HumanBodyBones OwnerRole { get; }
            public Rigidbody OwnerBody { get; }
            public Rigidbody Body { get; set; }
            public SphereCollider Collider { get; set; }
            public ConfigurableJoint Joint { get; set; }

            public SecondaryMotionBinding(
                string propertyName,
                Transform source,
                Transform bridge,
                HumanBodyBones ownerRole,
                Rigidbody ownerBody)
            {
                PropertyName = propertyName;
                Source = source;
                Bridge = bridge;
                OwnerRole = ownerRole;
                OwnerBody = ownerBody;
            }
        }
    }
}
