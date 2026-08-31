using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Project-local BONELAB Patch 6 physical-jaw extension. The public package
    /// authors the seventeenth preview body; this module installs only the
    /// native runtime relationships and never creates or mutates input assets.
    /// </summary>
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        private const float JawMass = 1.1765f;
        private const float JawSlerpSpring = 50000f;
        private const float JawSlerpDamper = 500f;
        private const float JawSlerpMaximumForce = 36f;
        private const float JawMuscleSpring = 5000000f;
        private const float JawMuscleDamper = 100000f;
        private const float JawGripRadius = 0.2f;
        private const float JawGripMinBreakForce = 2000f;
        private const float JawGripMaxBreakForce = 3000f;
        private const float JawGripDefaultDistance = 0.1f;
        private const float JawTolerance = 0.0001f;
        private const float JawTargetMaximumCorrectionDegrees = 45f;
        // Headset telemetry from the accepted v75/v77 jaw measured 0.5647
        // degrees of residual opening after its target matched the reference
        // closed pose. This is an explicit provider calibration for the fixed
        // Patch 6 mass, joint drive, and PuppetMaster muscle contract above;
        // it is not presented as a per-Avatar measurement. The larger Animator
        // bias and the hinge direction are still derived from each Humanoid.
        private const float JawRuntimeSettlingCompensationDegrees = 0.5647f;
        private const string JawMuscleTargetName = "JawMuscleTarget";

        private static readonly HumanBodyBones[] NativeEntityOrderWithJaw =
            NativeEntityOrder.Concat(new[] { HumanBodyBones.Jaw }).ToArray();

        private static readonly HumanBodyBones[] NativeMuscleOrderWithJaw =
            Vergil333.MarrowNpcToolkit.Authoring.NpcHumanoidGraph
                .NativeMuscleOrder.Concat(new[] { HumanBodyBones.Jaw }).ToArray();

        private static bool RequiresJawShell(
            NpcCompatibilityCapabilities capabilities)
        {
            return (capabilities & NpcCompatibilityCapabilities.Jaw) != 0;
        }

        private static IReadOnlyList<HumanBodyBones> EntityOrderFor(
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            return roles != null && roles.ContainsKey(HumanBodyBones.Jaw)
                ? NativeEntityOrderWithJaw
                : NativeEntityOrder;
        }

        private static IReadOnlyList<HumanBodyBones> MuscleOrderFor(
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            return roles != null && roles.ContainsKey(HumanBodyBones.Jaw)
                ? NativeMuscleOrderWithJaw
                : Vergil333.MarrowNpcToolkit.Authoring.NpcHumanoidGraph
                    .NativeMuscleOrder;
        }

        private static bool TryPreflightJawBuild(out string detail)
        {
            try
            {
                JawTypes types = JawTypes.Resolve();
                MarrowNpcToolkitPatch6BehaviourSettings.JawResolved settings =
                    RequireJawSettings(types);
                ValidateJawStandingPose(settings.StandingIdle);
                detail = "The explicit 17-entry standing pose, GenericGrip pose, "
                         + "and Patch 6 Physical Jaw declarations passed preflight.";
                return true;
            }
            catch (Exception exception)
            {
                detail = "Patch 6 Physical Jaw preflight failed: "
                         + exception.Message;
                return false;
            }
        }

        internal static bool TryPreflightJawForSmoke(out string detail)
        {
            return TryPreflightJawBuild(out detail);
        }

        private static void ConfigureJawMuscleTarget(
            NpcDefinition definition,
            Transform animationRoot,
            Animator animator)
        {
            Transform jaw = ResolveJawTargetBone(
                definition, animationRoot, animator);
            if (jaw.Cast<Transform>().Any(child => string.Equals(
                    child.name, JawMuscleTargetName, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "The staged Avatar already contains a reserved "
                    + JawMuscleTargetName + " child.");
            Quaternion correction = ComputeJawTargetCorrection(definition);
            var targetObject = new GameObject(JawMuscleTargetName);
            Transform target = targetObject.transform;
            target.SetParent(jaw, false);
            target.localPosition = Vector3.zero;
            target.localRotation = correction;
            target.localScale = Vector3.one;
            targetObject.layer = jaw.gameObject.layer;
            ValidateJawMuscleTarget(
                definition, animationRoot, animator);
        }

        private static void ValidateJawMuscleTarget(
            NpcDefinition definition,
            Transform animationRoot,
            Animator animator)
        {
            Transform jaw = ResolveJawTargetBone(
                definition, animationRoot, animator);
            Transform[] targets = jaw.Cast<Transform>()
                .Where(child => string.Equals(
                    child.name, JawMuscleTargetName, StringComparison.Ordinal))
                .ToArray();
            if (targets.Length != 1)
                throw new InvalidOperationException(
                    "Physical Jaw requires exactly one direct "
                    + JawMuscleTargetName + " child; found " + targets.Length + ".");
            Transform target = targets[0];
            Quaternion expected = ComputeJawTargetCorrection(definition);
            if (target.childCount != 0
                || target.gameObject.GetComponents<Component>().Length != 1
                || target.gameObject.layer != jaw.gameObject.layer
                || Vector3.Distance(target.localPosition, Vector3.zero)
                    > JawTolerance
                || Vector3.Distance(target.localScale, Vector3.one)
                    > JawTolerance
                || Quaternion.Angle(target.localRotation, expected)
                    > 0.001f)
                throw new InvalidOperationException(
                    "The saved Physical Jaw correction target differs from the "
                    + "Avatar-derived closed-mouth contract.");
        }

        private static Transform ResolveJawTargetBone(
            NpcDefinition definition,
            Transform animationRoot,
            Animator animator)
        {
            if (definition == null || definition.AvatarSourceProfile == null
                || animationRoot == null || animationRoot.childCount != 1
                || animator == null || !animator.transform.IsChildOf(animationRoot))
                throw new InvalidOperationException(
                    "Physical Jaw correction requires one routed Avatar and its "
                    + "accepted Humanoid Animator.");
            Transform jaw = ResolveAvatarJaw(
                definition.AvatarSourceProfile,
                animationRoot.GetChild(0));
            Transform humanoidJaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            if (humanoidJaw != null && humanoidJaw != jaw)
                throw new InvalidOperationException(
                    "The live Humanoid Jaw disagrees with the accepted Jaw path.");
            return jaw;
        }

        private static Quaternion ComputeJawTargetCorrection(
            NpcDefinition definition)
        {
            if (definition == null || definition.SourceAvatar == null
                || definition.AvatarSourceProfile == null
                || definition.AnatomyProfile == null)
                throw new InvalidOperationException(
                    "Physical Jaw correction requires the accepted Avatar, source "
                    + "profile, and Anatomy Profile.");
            Quaternion closed = definition.AnatomyProfile
                .JawClosedLocalRotation.normalized;
            if (!IsFinite(closed.x) || !IsFinite(closed.y)
                || !IsFinite(closed.z) || !IsFinite(closed.w)
                || Math.Abs(1f - Quaternion.Dot(closed, closed)) > 0.002f)
                throw new InvalidOperationException(
                    "The Anatomy Profile has no finite normalized closed Jaw rotation.");

            BehaviourTypes types = BehaviourTypes.Resolve();
            MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings =
                RequireBehaviourSettings(types, true);
            AnimationClip idle = ResolveConfiguredIdleClip(
                settings.AnimatorController);
            Scene scene = default;
            GameObject authoringRoot = null;
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                authoringRoot = new GameObject("Patch6JawTargetAuthoring");
                SceneManager.MoveGameObjectToScene(authoringRoot, scene);
                Transform animationRoot = new GameObject("AnimationRoot").transform;
                animationRoot.SetParent(authoringRoot.transform, false);
                GameObject avatar = PrefabUtility.InstantiatePrefab(
                    definition.SourceAvatar, scene) as GameObject;
                if (avatar == null)
                    throw new InvalidOperationException(
                        "Unity could not instantiate the accepted Avatar while "
                        + "deriving its Jaw correction.");
                avatar.transform.SetParent(animationRoot, false);
                Transform animatorHolder = string.IsNullOrWhiteSpace(
                        definition.AvatarSourceProfile.AnimatorPath)
                    ? avatar.transform
                    : avatar.transform.Find(
                        definition.AvatarSourceProfile.AnimatorPath);
                Animator animator = animatorHolder == null
                    ? null : animatorHolder.GetComponent<Animator>();
                if (animator == null || animator.avatar == null
                    || !animator.avatar.isHuman)
                    throw new InvalidOperationException(
                        "The accepted Animator path did not resolve while deriving "
                        + "the Jaw correction.");
                Transform jaw = ResolveAvatarJaw(
                    definition.AvatarSourceProfile, avatar.transform);
                if (Quaternion.Angle(jaw.localRotation, closed) > 0.001f)
                    throw new InvalidOperationException(
                        "The accepted Avatar Jaw rest rotation changed after the "
                        + "Anatomy baseline was captured. Refit physics first.");
                Vector3 jawHingeAxis = jaw.InverseTransformDirection(
                    avatar.transform.right).normalized;
                if (!IsFinite(jawHingeAxis.x)
                    || !IsFinite(jawHingeAxis.y)
                    || !IsFinite(jawHingeAxis.z)
                    || jawHingeAxis.sqrMagnitude < 0.999f)
                    throw new InvalidOperationException(
                        "The accepted Avatar does not provide a finite Jaw hinge "
                        + "axis for runtime settling compensation.");

                idle.SampleAnimation(animator.gameObject, 0f);
                Quaternion sampled = jaw.localRotation.normalized;
                Quaternion animatorCorrection =
                    (Quaternion.Inverse(sampled) * closed).normalized;
                Quaternion settlingCorrection = Quaternion.AngleAxis(
                    -JawRuntimeSettlingCompensationDegrees,
                    jawHingeAxis);
                Quaternion correction =
                    (settlingCorrection * animatorCorrection).normalized;
                float angle = Quaternion.Angle(Quaternion.identity, correction);
                if (!IsFinite(correction.x) || !IsFinite(correction.y)
                    || !IsFinite(correction.z) || !IsFinite(correction.w)
                    || angle > JawTargetMaximumCorrectionDegrees)
                    throw new InvalidOperationException(
                        "The derived Animator/hinge Jaw target correction is invalid or "
                        + "larger than " + JawTargetMaximumCorrectionDegrees
                        + " degrees (" + angle.ToString("R", CultureInfo.InvariantCulture)
                        + ").");
                return correction;
            }
            finally
            {
                if (authoringRoot != null)
                    UnityEngine.Object.DestroyImmediate(authoringRoot);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        internal static bool TryGetExpectedJawTargetCorrectionForSmoke(
            NpcDefinition definition,
            out Quaternion correction,
            out string detail)
        {
            try
            {
                correction = ComputeJawTargetCorrection(definition);
                detail = Quaternion.Angle(
                    Quaternion.identity, correction).ToString(
                    "R", CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exception)
            {
                correction = Quaternion.identity;
                detail = exception.Message;
                return false;
            }
        }

        private static MarrowNpcToolkitPatch6BehaviourSettings.JawResolved
            RequireJawSettings(JawTypes types)
        {
            if (!MarrowNpcToolkitPatch6BehaviourSettings.TryResolveJaw(
                    types.EnemyPoseData,
                    types.HandPose,
                    out MarrowNpcToolkitPatch6BehaviourSettings.JawResolved settings,
                    out string detail))
                throw new InvalidOperationException(detail);
            ValidatePersistentAsset(
                settings.StandingIdle,
                types.EnemyPoseData,
                "17-body Physical Jaw Standing Pose");
            ValidatePersistentAsset(
                settings.GenericGripPose,
                types.HandPose,
                "Physical Jaw GenericGrip Pose");
            return settings;
        }

        private static void ValidateJawStandingPose(UnityEngine.Object pose)
        {
            var serialized = new SerializedObject(pose);
            SerializedProperty positions = Require(serialized, "posePositions");
            SerializedProperty rotations = Require(serialized, "poseRotations");
            if (!positions.isArray || !rotations.isArray
                || positions.arraySize != 17 || rotations.arraySize != 17)
                throw new InvalidOperationException(
                    "The configured Physical Jaw standing pose must contain "
                    + "exactly 17 positions and 17 rotations in PuppetMaster "
                    + "muscle order.");
            for (int index = 0; index < 17; index++)
            {
                Vector3 position = positions.GetArrayElementAtIndex(index)
                    .vector3Value;
                Quaternion rotation = rotations.GetArrayElementAtIndex(index)
                    .quaternionValue;
                if (!IsFinite(position)
                    || !IsFinite(rotation.x) || !IsFinite(rotation.y)
                    || !IsFinite(rotation.z) || !IsFinite(rotation.w)
                    || Math.Abs(1f - (rotation.x * rotation.x
                                      + rotation.y * rotation.y
                                      + rotation.z * rotation.z
                                      + rotation.w * rotation.w)) > 0.002f)
                    throw new InvalidOperationException(
                        "The Physical Jaw standing pose contains invalid data at "
                        + "muscle " + index + ".");
            }
        }

        private static NativeJawShell ConfigureJawShell(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            ValidateJawRoleArguments(outputRoot, roles);
            JawTypes types = JawTypes.Resolve();
            MarrowNpcToolkitPatch6BehaviourSettings.JawResolved settings =
                RequireJawSettings(types);
            ValidateJawStandingPose(settings.StandingIdle);

            NativeRole jaw = roles[HumanBodyBones.Jaw];
            if (jaw.Collider.transform.parent != jaw.Body
                || !string.Equals(
                    jaw.Collider.transform.name,
                    "PrimaryCollider",
                    StringComparison.Ordinal)
                || jaw.Collider.gameObject.GetComponents(types.GenericGrip).Length != 0
                || Enumerable.Range(0, jaw.Body.childCount)
                    .Select(jaw.Body.GetChild)
                    .Any(child => string.Equals(
                        child.name, "JawGripCenter", StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "The staged Jaw does not have one clean direct "
                    + "PrimaryCollider holder.");

            // The public preview calls this holder PrimaryCollider. Native
            // Patch 6 calls the same centered collider frame JawGripCenter.
            // Rename and reuse it so MarrowBody, Tracker, and GenericGrip all
            // share one physical BoxCollider instead of duplicating volume.
            Transform holder = jaw.Collider.transform;
            holder.name = "JawGripCenter";

            Component grip = AddNative(
                holder.gameObject, types.GenericGrip, "Physical Jaw GenericGrip");
            ConfigureJawGrip(grip, settings.GenericGripPose);
            var shell = new NativeJawShell(settings, types, holder, grip);
            ValidateJawShell(outputRoot, roles, shell);
            return shell;
        }

        private static NativeJawShell ResolveJawShell(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            ValidateJawRoleArguments(outputRoot, roles);
            JawTypes types = JawTypes.Resolve();
            MarrowNpcToolkitPatch6BehaviourSettings.JawResolved settings =
                RequireJawSettings(types);
            ValidateJawStandingPose(settings.StandingIdle);
            NativeRole jaw = roles[HumanBodyBones.Jaw];
            Transform holder = RequireDirectChild(jaw.Body, "JawGripCenter");
            if (holder != jaw.Collider.transform)
                throw new InvalidOperationException(
                    "Saved JawGripCenter is not the registered Jaw collider holder.");
            Component grip = RequireOnlyComponent(
                holder.gameObject, types.GenericGrip, "Physical Jaw GenericGrip");
            var shell = new NativeJawShell(settings, types, holder, grip);
            ValidateJawShell(outputRoot, roles, shell);
            return shell;
        }

        private static void ConfigureJawGrip(
            Component grip,
            UnityEngine.Object handPose)
        {
            var data = new SerializedObject(grip);
            SetInt(data, "isThrowable", 1);
            SetInt(data, "ignoreGripTargetOnAttach", 0);
            SetObjectArray(data, "gripColliders", Array.Empty<UnityEngine.Object>());
            SetObjectArray(
                data, "additionalGripColliders", Array.Empty<UnityEngine.Object>());
            Keyframe[] keys =
            {
                FlatJawGripKey(-180f),
                FlatJawGripKey(0f),
                FlatJawGripKey(180f),
            };
            var curve = new AnimationCurve(keys)
            {
                preWrapMode = WrapMode.Loop,
                postWrapMode = WrapMode.Loop,
            };
            Require(data, "handleAmplifyCurve").animationCurveValue = curve;
            SetObject(data, "handPose", handPose);
            SetVector(data, "primaryMovementAxis", Vector3.forward);
            SetVector(data, "secondaryMovementAxis", Vector3.up);
            SetInt(data, "gripOptions", 0);
            SetFloat(data, "priority", 1f);
            SetFloat(data, "minBreakForce", JawGripMinBreakForce);
            SetFloat(data, "maxBreakForce", JawGripMaxBreakForce);
            SetFloat(data, "defaultGripDistance", JawGripDefaultDistance);
            SetFloat(data, "gripDistanceSqr", float.PositiveInfinity);
            SetFloat(data, "radius", JawGripRadius);
            SetObject(data, "targetTransform", null);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Keyframe FlatJawGripKey(float time)
        {
            return new Keyframe(time, 0f, 0f, 0f, 0f, 0f)
            {
                weightedMode = WeightedMode.None,
            };
        }

        private static void ValidateJawShell(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            NativeJawShell shell)
        {
            ValidateJawRoleArguments(outputRoot, roles);
            if (shell == null)
                throw new InvalidOperationException(
                    "The Physical Jaw shell was not resolved.");
            NativeRole jaw = roles[HumanBodyBones.Jaw];
            BoxCollider primary = shell.GripCenter == null
                ? null
                : shell.GripCenter.GetComponent<BoxCollider>();
            if (shell.Grip.transform != shell.GripCenter
                || shell.GripCenter.parent != jaw.Body
                || shell.GripCenter != jaw.Collider.transform
                || !string.Equals(
                    shell.GripCenter.name,
                    "JawGripCenter",
                    StringComparison.Ordinal)
                || shell.GripCenter.GetComponents(shell.Types.GenericGrip).Length != 1
                || shell.GripCenter.GetComponents<BoxCollider>().Length != 1
                || primary == null || !primary.enabled || primary.isTrigger
                || shell.GripCenter.GetComponents<Component>().Length != 3)
                throw new InvalidOperationException(
                    "JawGripCenter must be a direct Jaw child with exactly one "
                    + "GenericGrip and one enabled non-trigger BoxCollider.");

            if (!NearGrip(shell.GripCenter.localScale, Vector3.one))
                throw new InvalidOperationException(
                    "Physical Jaw grip center must retain unit scale.");

            if (!(shell.Grip is Behaviour behaviour) || !behaviour.enabled)
                throw new InvalidOperationException(
                    "The Physical Jaw GenericGrip must be enabled.");
            var data = new SerializedObject(shell.Grip);
            RequireGripByte(data, "isThrowable", 1);
            RequireGripByte(data, "ignoreGripTargetOnAttach", 0);
            RequireEmptyObjectArray(data, "gripColliders");
            RequireEmptyObjectArray(data, "additionalGripColliders");
            ValidateJawGripCurve(
                Require(data, "handleAmplifyCurve").animationCurveValue);
            RequireGripObject(data, "handPose", shell.Settings.GenericGripPose);
            RequireGripVector(data, "primaryMovementAxis", Vector3.forward);
            RequireGripVector(data, "secondaryMovementAxis", Vector3.up);
            RequireGripByte(data, "gripOptions", 0);
            RequireGripFloat(data, "priority", 1f);
            RequireGripFloat(data, "minBreakForce", JawGripMinBreakForce);
            RequireGripFloat(data, "maxBreakForce", JawGripMaxBreakForce);
            RequireGripFloat(data, "defaultGripDistance", JawGripDefaultDistance);
            if (!float.IsPositiveInfinity(
                    Require(data, "gripDistanceSqr").floatValue))
                throw new InvalidOperationException(
                    "Physical Jaw GenericGrip.gripDistanceSqr must be positive infinity.");
            RequireGripFloat(data, "radius", JawGripRadius);
            RequireGripObject(data, "targetTransform", null);
        }

        private static void ValidateJawGripCurve(AnimationCurve curve)
        {
            if (curve == null || curve.length != 3
                || curve.preWrapMode != WrapMode.Loop
                || curve.postWrapMode != WrapMode.Loop)
                throw new InvalidOperationException(
                    "Physical Jaw GenericGrip requires the accepted three-key "
                    + "flat handle amplification curve.");
            float[] times = { -180f, 0f, 180f };
            for (int index = 0; index < times.Length; index++)
            {
                Keyframe key = curve.keys[index];
                if (Math.Abs(key.time - times[index]) > JawTolerance
                    || Math.Abs(key.value) > JawTolerance
                    || Math.Abs(key.inTangent) > JawTolerance
                    || Math.Abs(key.outTangent) > JawTolerance
                    || key.weightedMode != WeightedMode.None
                    || Math.Abs(key.inWeight) > JawTolerance
                    || Math.Abs(key.outWeight) > JawTolerance)
                    throw new InvalidOperationException(
                        "Physical Jaw GenericGrip curve differs at key "
                        + index + ".");
            }
        }

        private static void ValidateJawRoleArguments(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            if (outputRoot == null || roles == null
                || !roles.TryGetValue(HumanBodyBones.Jaw, out NativeRole jaw)
                || jaw == null || jaw.Body == null || jaw.Target == null
                || jaw.MuscleTarget == null
                || jaw.MuscleTarget.parent != jaw.Target
                || !string.Equals(
                    jaw.MuscleTarget.name,
                    JawMuscleTargetName,
                    StringComparison.Ordinal)
                || jaw.Collider == null || jaw.Profile == null
                || !jaw.HasParent || jaw.ParentRole != HumanBodyBones.Head
                || jaw.Body.parent != roles[HumanBodyBones.Head].Body
                || !(jaw.Collider is BoxCollider))
                throw new InvalidOperationException(
                    "Physical Jaw requires one enabled Box role directly below Head.");
        }

        private static void ConfigureJawLivePhysics(NativeRole jaw)
        {
            ValidateJawAuthoredTuning(jaw);
            Rigidbody body = jaw.Rigidbody;
            body.mass = JawMass;
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

            ConfigurableJoint joint = jaw.Joint;
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Locked;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -28f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 0f };
            joint.angularYLimit = new SoftJointLimit { limit = 10f };
            // Patch 6 serializes a ten-degree Z limit even though Z is locked.
            joint.angularZLimit = new SoftJointLimit { limit = 10f };
            ResolveCanonicalJawJointFrame(
                jaw, out Vector3 jointAxis, out Vector3 secondaryAxis);
            joint.axis = jointAxis;
            joint.secondaryAxis = secondaryAxis;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = JawSlerpSpring,
                positionDamper = JawSlerpDamper,
                maximumForce = JawSlerpMaximumForce,
            };
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
        }

        private static void ValidateJawAuthoredTuning(NativeRole jaw)
        {
            if (jaw.Role != HumanBodyBones.Jaw
                || Math.Abs(jaw.Profile.MassKilograms - JawMass) > JawTolerance
                || jaw.Profile.AngularXMotion
                    != Vergil333.MarrowNpcToolkit.Authoring.NpcJointMotion.Limited
                || jaw.Profile.AngularYMotion
                    != Vergil333.MarrowNpcToolkit.Authoring.NpcJointMotion.Limited
                || jaw.Profile.AngularZMotion
                    != Vergil333.MarrowNpcToolkit.Authoring.NpcJointMotion.Locked
                || !IsFinite(jaw.Profile.JointAxis)
                || !IsFinite(jaw.Profile.JointSecondaryAxis)
                || jaw.Profile.JointAxis.sqrMagnitude < 0.99f
                || jaw.Profile.JointSecondaryAxis.sqrMagnitude < 0.99f
                || Vector3.Distance(
                    jaw.Profile.AngularLowLimits,
                    new Vector3(-28f, -10f, 0f)) > JawTolerance
                || Vector3.Distance(
                    jaw.Profile.AngularHighLimits,
                    new Vector3(0f, 10f, 0f)) > JawTolerance
                || Math.Abs(jaw.Profile.JointDriveMaxForce
                    - JawSlerpMaximumForce) > JawTolerance
                || Math.Abs(jaw.Profile.MuscleSpring
                    - JawMuscleSpring) > JawTolerance
                || Math.Abs(jaw.Profile.MuscleDamper
                    - JawMuscleDamper) > JawTolerance
                || Math.Abs(jaw.Profile.MuscleWeight - 1f) > JawTolerance
                || jaw.Profile.ColliderShape
                    != Vergil333.MarrowNpcToolkit.Authoring.NpcColliderShape.Box)
                throw new InvalidOperationException(
                    "The authored Physical Jaw tuning no longer matches the "
                    + "accepted Patch 6 -28..0 / 10 degree hinge contract.");
        }

        private static void ValidateJawNativePhysics(NativeRole jaw)
        {
            ValidateJawAuthoredTuning(jaw);
            ResolveCanonicalJawJointFrame(
                jaw, out Vector3 jointAxis, out Vector3 secondaryAxis);
            Rigidbody body = jaw.Rigidbody;
            ConfigurableJoint joint = jaw.Joint;
            if (Math.Abs(body.mass - JawMass) > JawTolerance
                || Math.Abs(body.drag - NativeLinearDrag) > JawTolerance
                || Math.Abs(body.angularDrag - NativeAngularDrag) > JawTolerance
                || !body.useGravity || body.isKinematic || !body.detectCollisions
                || joint.connectedBody == null
                || joint.connectedBody.transform != jaw.Body.parent
                || joint.xMotion != ConfigurableJointMotion.Locked
                || joint.yMotion != ConfigurableJointMotion.Locked
                || joint.zMotion != ConfigurableJointMotion.Locked
                || joint.angularXMotion != ConfigurableJointMotion.Limited
                || joint.angularYMotion != ConfigurableJointMotion.Limited
                || joint.angularZMotion != ConfigurableJointMotion.Locked
                || Math.Abs(joint.lowAngularXLimit.limit + 28f) > JawTolerance
                || Math.Abs(joint.highAngularXLimit.limit) > JawTolerance
                || Math.Abs(joint.angularYLimit.limit - 10f) > JawTolerance
                || Math.Abs(joint.angularZLimit.limit - 10f) > JawTolerance
                || Vector3.Distance(
                    joint.axis, jointAxis) > JawTolerance
                || Vector3.Distance(
                    joint.secondaryAxis, secondaryAxis) > JawTolerance
                || joint.rotationDriveMode != RotationDriveMode.Slerp
                || Math.Abs(joint.slerpDrive.positionSpring
                    - JawSlerpSpring) > JawTolerance
                || Math.Abs(joint.slerpDrive.positionDamper
                    - JawSlerpDamper) > JawTolerance
                || Math.Abs(joint.slerpDrive.maximumForce
                    - JawSlerpMaximumForce) > JawTolerance)
                throw new InvalidOperationException(
                    "The live Physical Jaw rigidbody/joint contract differs from "
                    + "the accepted Patch 6 hinge.");
        }

        private static void ValidateJawMarrowCache(
            NativeRole jaw,
            Component marrowBody,
            Component marrowJoint)
        {
            ValidateJawNativePhysics(jaw);
            var bodyObject = new SerializedObject(marrowBody);
            SerializedProperty rigidbodyInfo = Require(
                bodyObject, "_defaultRigidbodyInfo");
            SerializedProperty colliders = Require(bodyObject, "_colliders");
            if (Math.Abs(RequireRelative(
                    rigidbodyInfo, "mass").floatValue - JawMass) > JawTolerance
                || Math.Abs(RequireRelative(
                    rigidbodyInfo, "drag").floatValue
                    - NativeLinearDrag) > JawTolerance
                || Math.Abs(RequireRelative(
                    rigidbodyInfo, "angularDrag").floatValue
                    - NativeAngularDrag) > JawTolerance
                || !RequireRelative(rigidbodyInfo, "useGravity").boolValue
                || RequireRelative(rigidbodyInfo, "isKinematic").boolValue
                || !RequireRelative(rigidbodyInfo, "detectCollisions").boolValue
                || colliders.arraySize != 1
                || colliders.GetArrayElementAtIndex(0).objectReferenceValue
                    != jaw.Collider
                || Require(bodyObject, "_rigidbody").objectReferenceValue
                    != jaw.Rigidbody)
                throw new InvalidOperationException(
                    "The cached Physical Jaw MarrowBody differs from its live "
                    + "Rigidbody/collider contract.");

            var jointObject = new SerializedObject(marrowJoint);
            ResolveCanonicalJawJointFrame(
                jaw, out Vector3 jointAxis, out Vector3 secondaryAxis);
            SerializedProperty cached = Require(
                jointObject, "_defaultConfigJointInfo");
            SerializedProperty slerp = RequireRelative(cached, "slerpDriveExt");
            if (RequireRelative(cached, "xMotion").intValue
                    != (int)ConfigurableJointMotion.Locked
                || RequireRelative(cached, "yMotion").intValue
                    != (int)ConfigurableJointMotion.Locked
                || RequireRelative(cached, "zMotion").intValue
                    != (int)ConfigurableJointMotion.Locked
                || RequireRelative(cached, "angularXMotion").intValue
                    != (int)ConfigurableJointMotion.Limited
                || RequireRelative(cached, "angularYMotion").intValue
                    != (int)ConfigurableJointMotion.Limited
                || RequireRelative(cached, "angularZMotion").intValue
                    != (int)ConfigurableJointMotion.Locked
                || RequireRelative(cached, "rotationDriveMode").intValue
                    != (int)RotationDriveMode.Slerp
                || Vector3.Distance(
                    RequireRelative(cached, "axis").vector3Value,
                    jointAxis) > JawTolerance
                || Vector3.Distance(
                    RequireRelative(cached, "secondaryAxis").vector3Value,
                    secondaryAxis) > JawTolerance
                || Math.Abs(RequireRelative(
                    RequireRelative(cached, "lowAngularXLimitExt"),
                    "limit").floatValue + 28f) > JawTolerance
                || Math.Abs(RequireRelative(
                    RequireRelative(cached, "highAngularXLimitExt"),
                    "limit").floatValue) > JawTolerance
                || Math.Abs(RequireRelative(
                    RequireRelative(cached, "angularYLimitExt"),
                    "limit").floatValue - 10f) > JawTolerance
                || Math.Abs(RequireRelative(
                    RequireRelative(cached, "angularZLimitExt"),
                    "limit").floatValue - 10f) > JawTolerance
                || Math.Abs(RequireRelative(
                    slerp, "positionSpring").floatValue
                    - JawSlerpSpring) > JawTolerance
                || Math.Abs(RequireRelative(
                    slerp, "positionDamper").floatValue
                    - JawSlerpDamper) > JawTolerance
                || Math.Abs(RequireRelative(
                    slerp, "maximumForce").floatValue
                    - JawSlerpMaximumForce) > JawTolerance)
                throw new InvalidOperationException(
                    "The cached Physical Jaw MarrowJoint differs from the exact "
                    + "live Slerp hinge contract.");
        }

        private static void ResolveCanonicalJawJointFrame(
            NativeRole jaw,
            out Vector3 axis,
            out Vector3 secondaryAxis)
        {
            if (jaw == null || jaw.Role != HumanBodyBones.Jaw
                || jaw.Target == null || jaw.Body == null)
                throw new InvalidOperationException(
                    "Physical Jaw is missing its accepted deform target or body.");
            Transform avatarRoot = jaw.Target;
            while (avatarRoot.parent != null
                   && !string.Equals(
                       avatarRoot.parent.name,
                       "AnimationRoot",
                       StringComparison.Ordinal))
                avatarRoot = avatarRoot.parent;
            if (avatarRoot.parent == null
                || !string.Equals(
                    avatarRoot.parent.name,
                    "AnimationRoot",
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Physical Jaw could not resolve the routed Avatar frame.");

            // The asymmetric -28..0 range opens around the Avatar right
            // direction. On the validated imported Jaw frame this is local
            // -X, which matches the headset-proven v75/v77 native contract.
            Vector3 worldAxis = avatarRoot.right;
            Vector3 worldSecondary = Vector3.ProjectOnPlane(
                avatarRoot.up, worldAxis).normalized;
            if (worldSecondary.sqrMagnitude < 0.999f)
                throw new InvalidOperationException(
                    "The routed Avatar does not provide an orthogonal Jaw frame.");
            axis = jaw.Body.InverseTransformDirection(worldAxis).normalized;
            secondaryAxis = jaw.Body.InverseTransformDirection(
                worldSecondary).normalized;
        }

        private static string CreateJawFingerprint(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            NativeJawShell shell)
        {
            ValidateJawShell(outputRoot, roles, shell);
            ValidateJawNativePhysics(roles[HumanBodyBones.Jaw]);
            var text = new StringBuilder(2048);
            NativeRole jaw = roles[HumanBodyBones.Jaw];
            text.Append("patch6-physical-jaw-v4|pose=")
                .Append(GripAssetKey(shell.Settings.StandingIdle))
                .Append("|gripPose=")
                .Append(GripAssetKey(shell.Settings.GenericGripPose))
                .Append("|holder=")
                .Append(RelativePath(outputRoot.transform, shell.GripCenter))
                .Append('|');
            AppendVector(text, shell.GripCenter.localPosition);
            AppendQuaternion(text, shell.GripCenter.localRotation);
            text.Append("muscleTarget=")
                .Append(RelativePath(outputRoot.transform, jaw.MuscleTarget))
                .Append('|');
            AppendQuaternion(text, jaw.MuscleTarget.localRotation);
            var grip = new SerializedObject(shell.Grip);
            text.Append("grip=")
                .Append(Require(grip, "isThrowable").intValue).Append(',')
                .Append(Require(grip, "gripOptions").intValue).Append(',')
                .Append(Require(grip, "minBreakForce").floatValue.ToString(
                    "R", CultureInfo.InvariantCulture)).Append(',')
                .Append(Require(grip, "maxBreakForce").floatValue.ToString(
                    "R", CultureInfo.InvariantCulture)).Append(',')
                .Append(Require(grip, "defaultGripDistance").floatValue.ToString(
                    "R", CultureInfo.InvariantCulture)).Append(',')
                .Append(float.IsPositiveInfinity(
                    Require(grip, "gripDistanceSqr").floatValue) ? "inf" : "bad")
                .Append(',')
                .Append(Require(grip, "radius").floatValue.ToString(
                    "R", CultureInfo.InvariantCulture)).Append('|');
            AnimationCurve curve = Require(
                grip, "handleAmplifyCurve").animationCurveValue;
            text.Append("curve=")
                .Append((int)curve.preWrapMode).Append(',')
                .Append((int)curve.postWrapMode).Append(',');
            foreach (Keyframe key in curve.keys)
            {
                text.Append(F(key.time)).Append(':')
                    .Append(F(key.value)).Append(':')
                    .Append(F(key.inTangent)).Append(':')
                    .Append(F(key.outTangent)).Append(':')
                    .Append((int)key.weightedMode).Append(':')
                    .Append(F(key.inWeight)).Append(':')
                    .Append(F(key.outWeight)).Append(',');
            }
            text.Append('|');
            return text.ToString();
        }

        private sealed class NativeJawShell
        {
            public MarrowNpcToolkitPatch6BehaviourSettings.JawResolved Settings
            {
                get;
            }
            public JawTypes Types { get; }
            public Transform GripCenter { get; }
            public Component Grip { get; }

            public NativeJawShell(
                MarrowNpcToolkitPatch6BehaviourSettings.JawResolved settings,
                JawTypes types,
                Transform gripCenter,
                Component grip)
            {
                Settings = settings;
                Types = types;
                GripCenter = gripCenter;
                Grip = grip;
            }
        }

        private sealed class JawTypes
        {
            public Type GenericGrip { get; }
            public Type HandPose { get; }
            public Type EnemyPoseData { get; }

            private JawTypes(Type genericGrip, Type handPose, Type enemyPoseData)
            {
                GenericGrip = genericGrip;
                HandPose = handPose;
                EnemyPoseData = enemyPoseData;
            }

            public static JawTypes Resolve()
            {
                return new JawTypes(
                    ResolvePatch6ComponentType(
                        "SLZ.Marrow.GenericGrip", "SLZ.Marrow"),
                    ResolvePatch6ScriptableType(
                        "SLZ.Marrow.HandPose", "SLZ.Marrow"),
                    ResolvePatch6ScriptableType(
                        "SLZ.Marrow.Data.EnemyPoseData", "SLZ.Marrow"));
            }
        }

        private static Type ResolvePatch6ScriptableType(
            string fullName,
            string assemblyName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(value => string.Equals(
                    value.GetName().Name, assemblyName, StringComparison.Ordinal))
                .Select(value => value.GetType(fullName, false))
                .FirstOrDefault(value => value != null);
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type))
                throw new TypeLoadException(
                    fullName + " is unavailable from " + assemblyName + ".");
            return type;
        }
    }
}
